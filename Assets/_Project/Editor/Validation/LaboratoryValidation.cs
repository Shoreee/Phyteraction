using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using PhysicsHands.Laboratory;
using PhysicsHands.Tracking;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PhysicsHands.Editor.Validation
{
    [InitializeOnLoad]
    public static class LaboratoryValidation
    {
        private const string Key = "PhysicsHands.Validation";
        private static readonly List<string> Errors = new();
        private static double _start;
        private static int _firstFrame;
        private static bool _interactionChecked;
        private static float _worstWristDistance;
        private static float _stationaryWristDistance;
        private static float _frameWristDistance;
        private static Vector3 _settledCamera;
        private static Vector3 _settledWrist;
        private static int _gestureStage;
        private static Holdable _gestureProp;
        private static HandGrabber _gestureGrabber;
        private static bool _gestureCaptured;
        private static bool _pinchReachedHand;
        private static bool _headHandAligned;
        private static float _headHandError;
        private static int _originStage;
        private static Vector3 _originBefore;
        private static Vector3 _cameraBefore;
        private static Vector3 _wristBefore;
        private static XROrigin _origin;
        private static TrackedHand _trackedHeadHand;

        static LaboratoryValidation()
        {
            EditorApplication.playModeStateChanged += StateChanged;
        }

        [MenuItem("Physics Hands/Validation/Run Laboratory Smoke Test")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before testing.");
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sandbox.unity");
            SessionState.SetVector3(Key + ".CameraPosition", Camera.main.transform.localPosition);
            SessionState.SetVector3(Key + ".CameraEuler", Camera.main.transform.localEulerAngles);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Physics Hands/Validation/Compile visionOS Player Scripts")]
        public static void CompileVisionOS()
        {
            string output = "Logs/VisionOSScriptValidation";
            Directory.CreateDirectory(output);
            var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(
                new UnityEditor.Build.Player.ScriptCompilationSettings
                {
                    target = BuildTarget.VisionOS,
                    group = BuildTargetGroup.VisionOS,
                    options = UnityEditor.Build.Player.ScriptCompilationOptions.None
                }, output);
            bool passed = result.assemblies != null && result.assemblies.Count > 0;
            File.WriteAllText("Logs/visionos-script-validation.txt", passed ? "PASS: visionOS player scripts compiled." : "FAIL: no player assemblies.");
            if (!passed) throw new InvalidOperationException("visionOS script compilation failed.");
            Debug.Log("[PhysicsHands] visionOS player scripts compiled successfully.");
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // A running remote XR provider can exist without tracked hands.
                // Regression tests deliberately exercise deterministic simulated input.
                var input = HandInputManager.Instance;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(HandInputManager).GetField("_handTrackingSystem", flags).SetValue(input, HandTrackingSystem.Simulated);
                typeof(HandInputManager).GetMethod("SelectSystem", flags).Invoke(input, new object[] { HandTrackingSystem.Simulated });
                var camera = Camera.main;
                var driver = camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (driver != null) driver.enabled = false;
                camera.transform.SetLocalPositionAndRotation(SessionState.GetVector3(Key + ".CameraPosition", new Vector3(0, 1.35f, 0)),
                    Quaternion.Euler(SessionState.GetVector3(Key + ".CameraEuler", Vector3.zero)));
                Errors.Clear();
                _start = EditorApplication.timeSinceStartup;
                _firstFrame = Time.frameCount;
                _interactionChecked = false;
                _worstWristDistance = 0f;
                _stationaryWristDistance = 0f;
                _gestureStage = 0;
                _gestureCaptured = false;
                _pinchReachedHand = false;
                _headHandAligned = false;
                _headHandError = -1f;
                _originStage = 0;
                Application.logMessageReceived += Log;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Key, false);
                EditorApplication.update -= Tick;
                Application.logMessageReceived -= Log;
            }
        }

        private static void Log(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && Errors.Count < 30)
                Errors.Add(message + "\n" + stack);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition && !Errors.Contains(message)) Errors.Add(message);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            double elapsed = EditorApplication.timeSinceStartup - _start;
            if (elapsed < 2) return;
            try
            {
                CheckHeadsetOrigin();
                if (elapsed < 4) _frameWristDistance = 0f;
                var hands = UnityEngine.Object.FindObjectsByType<HoloHand>();
                Check(hands.Length == 2, "Expected two physical hands.");
                foreach (var hand in hands)
                {
                    Check(hand.IsInitialized() && hand.GoodTracking, hand.name + " tracking never initialized.");
                    if (hand.DetachableHand != null)
                    {
                        float distance = hand.DetachableHand.Enforcer.GetDistanceFromTarget();
                        _worstWristDistance = Mathf.Max(_worstWristDistance, distance);
                        if (elapsed < 4)
                        {
                            _frameWristDistance = Mathf.Max(_frameWristDistance, distance);
                            _stationaryWristDistance = _frameWristDistance;
                            _settledCamera = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                            if (hand.Wrist != null) _settledWrist = hand.Wrist.position;
                        }
                    }
                }
                foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>())
                    Check(float.IsFinite(rb.position.sqrMagnitude) && rb.position.sqrMagnitude < 10000, rb.name + " escaped or has nonfinite physics.");
                foreach (var skin in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include))
                    Check(skin.sharedMesh != null && skin.bones.All(b => b != null), skin.name + " has missing mesh/bones.");

                if (!_interactionChecked && elapsed > 4)
                {
                    _interactionChecked = true;
                    var grabber = UnityEngine.Object.FindObjectsByType<HandGrabber>().First();
                    var prop = UnityEngine.Object.FindObjectsByType<Holdable>().First();
                    Check(grabber.HoldingLink != null, "Grabber has no HoldingLink.");
                    prop.Rigidbody.position = grabber.SnapJointAnchor.position;
                    Check(grabber.Capture(prop, false), "Capture failed.");
                    Check(prop.IsHeld && grabber.IsHolding, "Capture state is inconsistent.");
                    grabber.Release();
                    Check(!prop.IsHeld && !grabber.IsHolding, "Release failed.");
                    var lab = UnityEngine.Object.FindAnyObjectByType<LaboratoryController>();
                    lab.ResetExperiment();
                    var respawner = prop.GetComponent<Respawner>();
                    prop.transform.position = new Vector3(0,-2,0);
                    prop.Rigidbody.position = prop.transform.position;
                    Check(respawner.CheckRespawnConditions(), "Out-of-bounds detection failed.");
                    respawner.Respawn();
                    Check(prop.Rigidbody.position.y > .3f, "Respawn did not restore prop.");
                }

                if (elapsed > 7 && _gestureStage == 0)
                {
                    _gestureStage = 1;
                    _gestureGrabber = UnityEngine.Object.FindObjectsByType<HandGrabber>().First(g => g.Side == TrackedHandSimulated.ActiveSide);
                    _gestureProp = UnityEngine.Object.FindObjectsByType<Holdable>().First();
                    var detectorRoot = new SerializedObject(_gestureGrabber).FindProperty("_grabDetector").objectReferenceValue as Transform;
                    var detector = detectorRoot != null ? detectorRoot.GetComponentInChildren<Collider>() : null;
                    Check(detector != null, "Grabber has no trigger collider.");
                    Vector3 point = detector != null ? detector.bounds.center : _gestureGrabber.transform.position;
                    _gestureProp.transform.position = point;
                    _gestureProp.Rigidbody.position = point;
                    _gestureProp.Rigidbody.constraints = RigidbodyConstraints.FreezeAll;
                    Physics.SyncTransforms();
                }
                if (elapsed > 8 && _gestureStage == 1)
                {
                    _gestureStage = 2;
                    Check(_gestureGrabber.ClosestHoldable == _gestureProp, "Trigger detection did not find the nearby prop.");
                    Press(UnityEngine.InputSystem.Key.G);
                }
                if (elapsed > 10 && _gestureStage == 2)
                {
                    _gestureStage = 3;
                    Check(_gestureGrabber.Hand != null && _gestureGrabber.Hand.TrackedHand != null && _gestureGrabber.Hand.TrackedHand.IsGrasping(),
                        "G key did not reach the tracked hand as a grasp.");
                    _gestureCaptured = _gestureGrabber.CapturedObject == _gestureProp && _gestureGrabber.HoldingLink.IsAttached;
                    Check(_gestureCaptured, "Simulated grasp gesture did not capture the detected prop.");
                    _gestureProp.Rigidbody.constraints = RigidbodyConstraints.None;
                    Press();
                }
                if (elapsed > 12 && _gestureStage == 3)
                {
                    _gestureStage = 4;
                    Check(!_gestureGrabber.IsHolding, "Opening the simulated hand did not release the prop.");
                    UnityEngine.Object.FindAnyObjectByType<LaboratoryController>().ResetExperiment();
                    Press(UnityEngine.InputSystem.Key.T);
                }
                if (elapsed > 13 && _gestureStage == 4)
                {
                    _gestureStage = 5;
                    TrackedHand tracked = _gestureGrabber.Hand != null ? _gestureGrabber.Hand.TrackedHand : null;
                    _pinchReachedHand = tracked != null && tracked.IsPinching(HandFinger.Index) && _gestureGrabber.Hand.IsIndexPinching();
                    Check(_pinchReachedHand, "T key did not reach the hand as an index pinch.");
                    Press();
                }
                if (elapsed > 14.5 && _originStage == 0)
                {
                    _originStage = 1;
                    _origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
                    _trackedHeadHand = HandInputManager.Instance.GetTrackedHand(TrackedHandSimulated.ActiveSide);
                    _originBefore = _origin.transform.position;
                    _cameraBefore = _origin.Camera.transform.position;
                    _wristBefore = _trackedHeadHand.Wrist.position;
                    _origin.transform.position = _originBefore + new Vector3(0.05f, 0f, 0.04f);
                }
                if (elapsed > 16 && _originStage == 1)
                {
                    _originStage = 2;
                    Vector3 shift = _origin.transform.position - _originBefore;
                    Vector3 cameraShift = _origin.Camera.transform.position - _cameraBefore;
                    Vector3 wristShift = _trackedHeadHand.Wrist.position - _wristBefore;
                    _headHandError = Vector3.Distance(cameraShift, wristShift);
                    _headHandAligned = Vector3.Distance(cameraShift, shift) < 0.01f && _headHandError < 0.02f;
                    Check(_headHandAligned, "Moving the headset origin did not keep the camera and tracked hand together.");
                    _origin.transform.position = _originBefore;
                }
            }
            catch (Exception ex) { if (Errors.Count < 30) Errors.Add(ex.ToString()); }
            if (elapsed < 20) return;
            Check(Time.frameCount - _firstFrame > 100, "Fewer than 100 frames in 20 seconds.");
            Check(_stationaryWristDistance < .5f, "Wrist deviated from target by >= 0.5 m.");
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            File.WriteAllText(Path.Combine(folder, "laboratory-validation.json"), JsonUtility.ToJson(new Result
            {
                passed = Errors.Count == 0,
                frames = Time.frameCount - _firstFrame,
                seconds = elapsed,
                worstWristDistance = _stationaryWristDistance,
                settledCameraX = _settledCamera.x,
                settledCameraY = _settledCamera.y,
                settledCameraZ = _settledCamera.z,
                settledWristX = _settledWrist.x,
                settledWristY = _settledWrist.y,
                settledWristZ = _settledWrist.z,
                gestureCaptured = _gestureCaptured,
                pinchReachedHand = _pinchReachedHand,
                headHandAligned = _headHandAligned,
                headHandError = _headHandError,
                errors = Errors.ToArray()
            }, true));
            CapturePreview(Path.Combine(folder, "laboratory-preview.png"));
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            Debug.Log($"[PhysicsHands] Laboratory validation: {(Errors.Count == 0 ? "PASS" : "FAIL")}, {Time.frameCount-_firstFrame} frames. See Logs/laboratory-validation.json");
            EditorApplication.isPlaying = false;
            if (Application.isBatchMode)
                EditorApplication.delayCall += () => EditorApplication.Exit(Errors.Count == 0 ? 0 : 1);
        }

        private static void Press(params UnityEngine.InputSystem.Key[] keys)
        {
            if (Keyboard.current == null)
            {
                Check(false, "No keyboard device; input-level checks could not run.");
                return;
            }
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        }

        private static void CheckHeadsetOrigin()
        {
            XROrigin origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            Check(origin != null && origin.Camera != null && origin.CameraFloorOffsetObject != null, "XR Origin is missing its camera or floor offset.");
            if (origin == null || origin.Camera == null || origin.CameraFloorOffsetObject == null) return;
            Check(origin.RequestedTrackingOriginMode == XROrigin.TrackingOriginMode.Floor, "Headset tracking origin is not Floor.");
            Check(origin.CameraYOffset < 0.01f, "Camera Y offset would stack on the visionOS eye height.");
            Transform floor = origin.CameraFloorOffsetObject.transform;
            Check(origin.Camera.transform.parent == floor, "Camera is not parented to the headset tracking offset.");
            if (_originStage != 1)
            {
                Vector3 view = origin.Camera.transform.position;
                Check(Mathf.Abs(view.y - 1.35f) < 0.05f && Mathf.Abs(view.z) < 0.05f, "Untracked viewpoint is not standing at the lab bench.");
            }
            foreach (TrackedHandXR hand in UnityEngine.Object.FindObjectsByType<TrackedHandXR>(FindObjectsInactive.Include))
                Check(hand.transform.parent == floor, hand.name + " is not in the headset tracking space.");
        }

        private static void CapturePreview(string path)
        {
            Camera camera = Camera.main;
            var target = new RenderTexture(1600,1000,24);
            RenderTexture oldTarget = camera.targetTexture;
            RenderTexture oldActive = RenderTexture.active;
            var texture = new Texture2D(1600,1000,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0,0,1600,1000),0,0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Serializable] private class Result
        {
            public bool passed;
            public int frames;
            public double seconds;
            public float worstWristDistance;
            public float settledCameraX;
            public float settledCameraY;
            public float settledCameraZ;
            public float settledWristX;
            public float settledWristY;
            public float settledWristZ;
            public bool gestureCaptured;
            public bool pinchReachedHand;
            public bool headHandAligned;
            public float headHandError;
            public string[] errors;
        }
    }
}
