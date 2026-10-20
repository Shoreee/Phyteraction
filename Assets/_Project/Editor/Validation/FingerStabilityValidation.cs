using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhysicsHands.Hands;
using PhysicsHands.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicsHands.Editor.Validation
{
    [InitializeOnLoad]
    public static class FingerStabilityValidation
    {
        internal const string Key = "PhysicsHands.FingerStability";
        static FingerStabilityValidation()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    new GameObject("Finger stability regression").AddComponent<FingerStabilityProbe>();
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
            };
        }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists("Logs/test-fingers"))
            {
                File.Delete("Logs/test-fingers");
                Run();
            }
            else if (File.Exists("Logs/test-fingers-direct"))
            {
                File.Delete("Logs/test-fingers-direct");
                RunDirect();
            }
            else if (File.Exists("Logs/test-grasp"))
            {
                File.Delete("Logs/test-grasp");
                GraspRecoveryValidation.Run();
            }
            else if (File.Exists("Logs/test-laboratory"))
            {
                File.Delete("Logs/test-laboratory");
                LaboratoryValidation.Run();
            }
        }
        [MenuItem("Physics Hands/Validation/Stress Both Hands")]
        public static void Run()
        {
            SessionState.SetBool(Key + ".Direct", false);
            Open();
        }
        [MenuItem("Physics Hands/Validation/Stress Direct Tracked Hands")]
        public static void RunDirect()
        {
            SessionState.SetBool(Key + ".Direct", true);
            Open();
        }
        static void Open()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sandbox.unity");
            SessionState.SetVector3(Key + ".CameraPosition", Camera.main.transform.localPosition);
            SessionState.SetVector3(Key + ".CameraEuler", Camera.main.transform.localEulerAngles);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
    }

    // Synthetic XR samples must arrive before HoloHand consumes them.
    [DefaultExecutionOrder(ExecutionOrder.HoloHand - 10)]
    public class FingerStabilityProbe : MonoBehaviour
    {
        [Serializable] public class BoneResult
        {
            public string name;
            public bool kinematic;
            public bool hasJoint;
            public float restLength;
            public float maxLengthError;
            public float maxAnchorError;
            public float recoveryAngle;
            public float maxTrackingAngle;
        }
        [Serializable] public class Result
        {
            public bool passed;
            public int frames;
            public float seconds;
            public BoneResult[] bones;
            public float maxMovingWristError;
            public float meanMovingWristError;
            public float fixedDeltaTime;
            public float stepSettlingSeconds;
            public string[] errors;
        }
        readonly List<string> _errors = new();
        readonly Dictionary<Transform, BoneResult> _bones = new();
        readonly Dictionary<Transform, Quaternion> _restRotations = new();
        readonly Dictionary<TrackedHand, Pose> _wrists = new();
        float _start;
        int _frames;
        bool _ready;
        bool _direct;
        float _maxWristError, _sumWristError;
        string _worstWrist = "";
        int _wristSamples;
        float _stepSettlingSeconds = -1;
        void Start()
        {
            var input = HandInputManager.Instance;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(HandInputManager).GetField("_handTrackingSystem", flags).SetValue(input, HandTrackingSystem.Simulated);
            typeof(HandInputManager).GetMethod("SelectSystem", flags).Invoke(input, new object[] { HandTrackingSystem.Simulated });
            var camera = Camera.main;
            var driver = camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver != null) driver.enabled = false;
            camera.transform.SetLocalPositionAndRotation(SessionState.GetVector3(FingerStabilityValidation.Key + ".CameraPosition", new Vector3(0,1.35f,0)),
                Quaternion.Euler(SessionState.GetVector3(FingerStabilityValidation.Key + ".CameraEuler", Vector3.zero)));
            _start = Time.unscaledTime;
            _direct = SessionState.GetBool(FingerStabilityValidation.Key + ".Direct", false);
            Application.logMessageReceived += Log;
            StartCoroutine(EndFrameMeasures());
        }
        void OnDestroy() => Application.logMessageReceived -= Log;
        void Log(string message, string stack, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Error) && _errors.Count < 20) _errors.Add(message);
        }
        void Update()
        {
            float elapsed = Time.unscaledTime - _start;
            if (elapsed < 2) return;
            if (!_ready)
            {
                _ready = true;
                foreach (var hand in FindObjectsByType<HoloHand>())
                {
                    if (_direct) hand.DirectTrackingValidationOverride = true;
                    var source = hand.TrackedHand;
                    if (source == null || !source.GoodTracking) { _errors.Add($"Tracking did not initialize: {hand.name}, system {HandInputManager.Instance.ActiveSystem}, source {source}, enabled {source?.isActiveAndEnabled}, tracked {source?.IsTracked()}, confident {source?.HasHighConfidence()}."); continue; }
                    source.enabled = false;
                    _wrists[source] = new Pose(source.Wrist.position + Vector3.up*.3f, source.Wrist.rotation);
                    foreach (var finger in source.ReferenceHandPoser.FingerPosers)
                        foreach (var bone in finger.FingerBones)
                            if (bone.target != null) _restRotations[bone.target] = bone.target.localRotation;
                    foreach (var bone in hand.FingerBones)
                    {
                        var enforcer = bone.JointEnforcer;
                        _bones[bone.transform] = new BoneResult {
                            name = hand.HandType + "/" + bone.name,
                            restLength = Vector3.Distance(bone.transform.position, bone.transform.parent.position),
                            kinematic = enforcer.IsKinematic,
                            hasJoint = enforcer.Joint != null };
                    }
                }
            }
            float t = elapsed - 2;
            float motionTime = Mathf.Min(t, 12f);
            foreach (var pair in _wrists)
            {
                float sign = pair.Key.Handedness == HandSide.Right ? 1 : -1;
                float speed = _direct ? 22f : 1f;
                Vector3 move = new Vector3(Mathf.Sin(motionTime*1.7f*speed)*.13f, Mathf.Sin(motionTime*2.3f*speed)*.12f, Mathf.Sin(motionTime*1.3f*speed)*.18f);
                Quaternion turn = Quaternion.Euler(Mathf.Sin(motionTime*1.1f*speed)*75, Mathf.Sin(motionTime*.9f*speed)*80, Mathf.Sin(motionTime*1.5f*speed)*70);
                pair.Key.Wrist.SetPositionAndRotation(pair.Value.position + move + (t >= 14 ? Vector3.up * .15f : Vector3.zero), turn * pair.Value.rotation);
                foreach (var finger in pair.Key.ReferenceHandPoser.FingerPosers)
                    foreach (var bone in finger.FingerBones)
                        if (bone.target != null && _restRotations.TryGetValue(bone.target, out Quaternion rest))
                            bone.target.localRotation = t > 12 ? rest : rest * Quaternion.AngleAxis(sign * (1+Mathf.Sin(motionTime*2.2f))*35, Vector3.forward);
            }
            _frames++;
            if (t < 16) return;
            Finish(elapsed);
        }
        System.Collections.IEnumerator EndFrameMeasures()
        {
            var end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                Measure();
            }
        }
        void Measure()
        {
            if (!_ready || Time.unscaledTime - _start < 2.5f) return;
            if (Time.unscaledTime - _start < 13f)
                foreach (var hand in FindObjectsByType<HoloHand>())
                {
                    float error = Vector3.Distance(hand.Wrist.position, hand.TrackedHand.Wrist.position);
                    if (error > _maxWristError)
                        _worstWrist = $"{hand.name}: visual={hand.Wrist.position} tracked={hand.TrackedHand.Wrist.position} physics={hand.DetachableHand.Rb.position} anchor={hand.HandAnchor.position} direct={hand.DirectTracking}";
                    _maxWristError = Mathf.Max(_maxWristError, error);
                    _sumWristError += error; _wristSamples++;
                }
            float stepTime = Time.unscaledTime - _start - 16;
            if (stepTime >= 0 && _stepSettlingSeconds < 0 &&
                FindObjectsByType<HoloHand>().All(h => Vector3.Distance(h.Wrist.position, h.TrackedHand.Wrist.position) < .01f))
                _stepSettlingSeconds = stepTime;
            foreach (var pair in _bones)
            {
                var bone = pair.Key;
                var data = pair.Value;
                if (_direct && Time.unscaledTime - _start < 14f)
                    data.maxTrackingAngle = Mathf.Max(data.maxTrackingAngle, bone.GetComponent<PhysicsHands.Dynamics.JointEnforcer>().GetAngleFromTarget());
                float length = Vector3.Distance(bone.position, bone.parent.position);
                data.maxLengthError = Mathf.Max(data.maxLengthError, Mathf.Abs(length-data.restLength));
                var joint = bone.GetComponent<ConfigurableJoint>();
                if (joint != null && joint.connectedBody != null)
                    data.maxAnchorError = Mathf.Max(data.maxAnchorError, Vector3.Distance(
                        bone.TransformPoint(joint.anchor), joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)));
            }
        }
        void Finish(float elapsed)
        {
            foreach (var pair in _bones)
                pair.Value.recoveryAngle = pair.Key.GetComponent<PhysicsHands.Dynamics.JointEnforcer>().GetAngleFromTarget();
            foreach (var bone in _bones.Values)
                if (bone.recoveryAngle > 8f) _errors.Add(bone.name + " failed to reopen: " + bone.recoveryAngle);
            if (_direct)
                foreach (var bone in _bones.Values)
                    if (bone.maxTrackingAngle > 3f) _errors.Add(bone.name + " shook off tracking: " + bone.maxTrackingAngle);
            foreach (var bone in _bones.Values)
                if (!float.IsFinite(bone.maxLengthError) || bone.maxLengthError > (bone.kinematic ? .001f : .025f))
                    _errors.Add(bone.name + " stretched " + bone.maxLengthError.ToString("F4") + " m");
            if (_stepSettlingSeconds < 0 || _stepSettlingSeconds > .25f) _errors.Add("Wrist did not settle within 1 cm in 250 ms after a 15 cm step: " + _stepSettlingSeconds);
            if (_maxWristError > .05f) _errors.Add("Moving wrist error exceeded 5 cm: " + _maxWristError + " " + _worstWrist);
            var result = new Result { passed = _errors.Count == 0 && _bones.Count == 30, frames = _frames, seconds = elapsed,
                fixedDeltaTime = Time.fixedDeltaTime, stepSettlingSeconds = _stepSettlingSeconds,
                maxMovingWristError = _maxWristError, meanMovingWristError = _sumWristError / Mathf.Max(1, _wristSamples),
                bones = _bones.Values.ToArray(), errors = _errors.ToArray() };
            typeof(LaboratoryValidation).GetMethod("CapturePreview", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { "Logs/finger-stability-preview.png" });
            File.WriteAllText(_direct ? "Logs/finger-stability-direct.json" : "Logs/finger-stability.json", JsonUtility.ToJson(result, true));
            Debug.Log("[PhysicsHands] Finger stability " + (result.passed ? "PASS" : "FAIL"));
            EditorApplication.isPlaying = false;
            enabled = false;
        }
    }
}
