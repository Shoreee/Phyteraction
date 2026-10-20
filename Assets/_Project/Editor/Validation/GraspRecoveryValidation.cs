using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using PhysicsHands.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PhysicsHands.Editor.Validation
{
    [InitializeOnLoad]
    public static class GraspRecoveryValidation
    {
        const string Key = "PhysicsHands.GraspRecovery";
        static GraspRecoveryValidation()
        {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    new GameObject("Grasp recovery regression").AddComponent<GraspRecoveryProbe>();
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
            };
        }
        [MenuItem("Physics Hands/Validation/Grasp Lift Release Both Hands")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sandbox.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
    }

    [DefaultExecutionOrder(-400)]
    public class GraspRecoveryProbe : MonoBehaviour
    {
        [Serializable] public class HandResult
        {
            public string side;
            public bool capturedAfterClosing;
            public bool remainedHeld;
            public bool released;
            public float liftedMetres;
            public float droppedMetres;
            public float maxJointError;
            public float maxRecoveryAngle;
        }
        [Serializable] public class Result { public bool passed; public HandResult[] hands; public string[] errors; }
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<HandResult> _results = new();
        readonly List<string> _errors = new();
        float _start, _initialY, _releaseY;
        int _cycle = -1;
        HandGrabber _grabber;
        Holdable _prop;
        HandResult _result;
        bool _liftChecked, _releasedChecked;
        void Start()
        {
            var input = HandInputManager.Instance;
            typeof(HandInputManager).GetField("_handTrackingSystem", Flags).SetValue(input, HandTrackingSystem.Simulated);
            typeof(HandInputManager).GetMethod("SelectSystem", Flags).Invoke(input, new object[] { HandTrackingSystem.Simulated });
            var camera = Camera.main;
            var driver = camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver != null) driver.enabled = false;
            camera.transform.SetLocalPositionAndRotation(new Vector3(0,1.35f,0), Quaternion.identity);
            _start = Time.unscaledTime;
            Application.logMessageReceived += Log;
        }
        void OnDestroy() { Application.logMessageReceived -= Log; InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); }
        void Log(string message, string stack, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Error) && _errors.Count < 20) _errors.Add(message);
        }
        void Update()
        {
            float elapsed = Time.unscaledTime - _start;
            if (elapsed < 2) return;
            int cycle = (int)((elapsed - 2) / 13);
            if (cycle >= 2) { Finish(); return; }
            float t = elapsed - 2 - cycle * 13;
            if (_cycle != cycle)
            {
                if (_prop != null) Destroy(_prop.gameObject);
                _cycle = cycle;
                var side = cycle == 0 ? HandSide.Right : HandSide.Left;
                if (TrackedHandSimulated.ActiveSide != side) TrackedHandSimulated.SwitchHand();
                _grabber = FindObjectsByType<HandGrabber>().First(g => g.Side == side);
                _result = new HandResult { side = side.ToString() }; _results.Add(_result);
                _liftChecked = _releasedChecked = false;
                _prop = null;
            }
            // Close in empty air first. Only then move a prop into contact, so
            // this fails when capture is implemented only on the gesture edge.
            InputSystem.QueueStateEvent(Keyboard.current, t >= 1 && t < 8 ? new KeyboardState(Key.G) : new KeyboardState());
            float lift = t < 4 ? 0 : Mathf.Clamp01((t-4)/2) * .25f;
            foreach (var source in FindObjectsByType<TrackedHandSimulated>())
            {
                float sign = source.Handedness == HandSide.Right ? 1 : -1;
                typeof(TrackedHandSimulated).GetField("_localPosition", Flags).SetValue(source,
                    new Vector3(sign * .25f, .05f + (source.Handedness == _grabber.Side ? lift : 0), .4f));
            }
            if (t >= 2 && _prop == null)
            {
                var detector = (Transform)typeof(HandGrabber).GetField("_grabDetector", Flags).GetValue(_grabber);
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "Regression weight 300g";
                obj.transform.localScale = Vector3.one * .055f;
                obj.transform.position = detector.GetComponentInChildren<Collider>().bounds.center;
                var rb = obj.AddComponent<Rigidbody>(); rb.mass = .3f; rb.constraints = RigidbodyConstraints.FreezeAll;
                _prop = obj.AddComponent<Holdable>();
                _initialY = rb.position.y;
                Physics.SyncTransforms();
            }
            if (_prop != null && _grabber.CapturedObject == _prop)
            {
                _result.capturedAfterClosing = true;
                _prop.Rigidbody.constraints = RigidbodyConstraints.None;
                _result.maxJointError = Mathf.Max(_result.maxJointError, _grabber.HoldingLink.DistanceFromJointAnchor());
            }
            if (t >= 7 && !_liftChecked)
            {
                _liftChecked = true;
                _result.remainedHeld = _grabber.CapturedObject == _prop;
                _result.liftedMetres = _prop.Rigidbody.position.y - _initialY;
                _releaseY = _prop.Rigidbody.position.y;
            }
            if (t >= 9 && !_releasedChecked)
            {
                _releasedChecked = true;
                _result.released = !_grabber.IsHolding;
                _result.droppedMetres = _releaseY - _prop.Rigidbody.position.y;
            }
            if (t >= 11)
                foreach (var bone in _grabber.Hand.FingerBones)
                    _result.maxRecoveryAngle = Mathf.Max(_result.maxRecoveryAngle, bone.JointEnforcer.GetAngleFromTarget());
        }
        void Finish()
        {
            foreach (var hand in _results)
            {
                if (!hand.capturedAfterClosing || !hand.remainedHeld || hand.liftedMetres < .18f)
                    _errors.Add(hand.side + " did not lift and retain the 300 g prop.");
                if (!hand.released || hand.droppedMetres < .15f) _errors.Add(hand.side + " failed to release/drop.");
                if (hand.maxRecoveryAngle > 8) _errors.Add(hand.side + " fingers failed to recover after grasp.");
            }
            File.WriteAllText("Logs/grasp-recovery.json", JsonUtility.ToJson(new Result {
                passed = _errors.Count == 0 && _results.Count == 2, hands = _results.ToArray(), errors = _errors.ToArray() }, true));
            Debug.Log("[PhysicsHands] Grasp recovery " + (_errors.Count == 0 ? "PASS" : "FAIL"));
            EditorApplication.isPlaying = false;
            enabled = false;
        }
    }
}
