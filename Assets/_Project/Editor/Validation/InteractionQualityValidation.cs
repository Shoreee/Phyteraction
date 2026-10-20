using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using PhysicsHands.Settings;
using PhysicsHands.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PhysicsHands.Editor.Validation
{
    [InitializeOnLoad]
    public static class InteractionQualityValidation
    {
        const string Key = "PhysicsHands.InteractionQuality";
        static InteractionQualityValidation()
        {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    new GameObject("Interaction quality probe").AddComponent<InteractionQualityProbe>();
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key, false))
                {
                    SessionState.SetBool(Key, false);
                    if (Application.isBatchMode) EditorApplication.Exit(File.Exists("Logs/interaction-quality.json") &&
                        File.ReadAllText("Logs/interaction-quality.json").Contains("\"passed\": true") ? 0 : 1);
                }
            };
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sandbox.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
    }

    [DefaultExecutionOrder(-400)]
    public class InteractionQualityProbe : MonoBehaviour
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] public class Result
        {
            public bool passed;
            public float baselineMeanError, improvedMeanError, baselineMaxError, improvedMaxError;
            public bool[] pinchCaptured = new bool[2], pinchReleased = new bool[2];
            public bool[] shortGapRetained = new bool[2];
            public string[] errors;
        }
        KeyboardState _keys;
        void Update(){if(Keyboard.current!=null)InputSystem.QueueStateEvent(Keyboard.current,_keys);}
        readonly Result _result = new();
        readonly List<string> _errors = new();
        readonly List<HandSettings> _copies = new();
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Exception || type == LogType.Error) _errors.Add(message); }
        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            if(Keyboard.current==null)InputSystem.AddDevice<Keyboard>();
            if(Mouse.current==null)InputSystem.AddDevice<Mouse>();
            Time.captureFramerate=90;
            var input = HandInputManager.Instance;
            typeof(HandInputManager).GetField("_handTrackingSystem", Flags).SetValue(input, HandTrackingSystem.Simulated);
            typeof(HandInputManager).GetMethod("SelectSystem", Flags).Invoke(input, new object[] { HandTrackingSystem.Simulated });
            var driver = Camera.main.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver != null) driver.enabled = false;
            Camera.main.transform.SetLocalPositionAndRotation(new Vector3(0,1.35f,0), Quaternion.identity);
            yield return new WaitForSeconds(2);
            var hands = FindObjectsByType<HoloHand>().OrderBy(h => h.HandType).ToArray();
            foreach (var hand in hands)
            {
                hand.TrackedHand.enabled = false;
                var copy = Instantiate(hand.HandSettings); _copies.Add(copy); hand.HandSettings = copy;
            }
            // Fixed-step trajectory and fixed-step sampling avoid Editor FPS bias.
            for (int mode = 0; mode < 2; mode++)
            {
                foreach (var hand in hands) hand.HandSettings.EnableTargetVelocityFollow = mode == 1;
                float sum=0, max=0; int count=0;
                for (int i=0; i<450; i++)
                {
                    float t=i*Time.fixedDeltaTime;
                    foreach (var hand in hands)
                    {
                        float sign=hand.HandType==HandSide.Right?1:-1;
                        hand.TrackedHand.Wrist.position=new Vector3(sign*.27f+Mathf.Sin(t*4)*.18f,1.65f, .4f);
                    }
                    yield return null;
                    if (i<90) continue;
                    foreach (var hand in hands)
                    {
                        float error=Vector3.Distance(hand.DetachableHand.Rb.position,hand.DetachableHand.Enforcer.GetTargetPosition());
                        sum+=error;max=Mathf.Max(max,error);count++;
                    }
                }
                Debug.Log($"FOLLOW mode={mode} rb={hands[0].DetachableHand.Rb.isKinematic} good={hands[0].GoodTracking} k={hands[0].DetachableHand.Enforcer.Joint.xDrive.positionSpring} d={hands[0].DetachableHand.Enforcer.Joint.xDrive.positionDamper}");
                if(mode==0){_result.baselineMeanError=sum/count;_result.baselineMaxError=max;}
                else {_result.improvedMeanError=sum/count;_result.improvedMaxError=max;}
            }
            foreach(var hand in hands) hand.TrackedHand.enabled=true;
            for(int side=0;side<2;side++)
            {
                var hand=hands[side];
                if(TrackedHandSimulated.ActiveSide!=hand.HandType) TrackedHandSimulated.SwitchHand();
                var grabber=hand.GetComponentInChildren<HandGrabber>();
                // Eliminate the palm trigger route: this specifically tests fingertip selection.
                var reachable=(List<Holdable>)typeof(Holder).GetField("_reachableHoldables",Flags).GetValue(grabber);
                var detector=(Transform)typeof(HandGrabber).GetField("_grabDetector",Flags).GetValue(grabber);
                var disabled=detector.GetComponentsInChildren<Collider>().Where(c=>c.isTrigger).ToArray();
                foreach(var c in disabled)c.enabled=false;
                _keys=new KeyboardState();
                yield return new WaitForSeconds(.3f);
                _keys=new KeyboardState(Key.T);
                yield return new WaitForSeconds(.3f);
                var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere);obj.name="Fingertip query regression";
                obj.transform.localScale=Vector3.one*.018f;
                obj.transform.position=(hand.ThumbTip.position+hand.IndexTip.position)*.5f;
                var rb=obj.AddComponent<Rigidbody>();rb.useGravity=false;rb.mass=.05f;
                var prop=obj.AddComponent<Holdable>();
                typeof(Holdable).GetField("_restrictAllowedSnapTypes",Flags).SetValue(prop,false);
                Physics.SyncTransforms();
                for(int i=0;i<25;i++){reachable.Clear();yield return new WaitForFixedUpdate();}
                _result.pinchCaptured[side]=grabber.CapturedObject==prop;
                Debug.Log($"PINCH side={side} gesture={hand.IsIndexPinching()} tracked={hand.GoodTracking} dist={prop.DistanceTo((hand.ThumbTip.position+hand.IndexTip.position)*.5f)} held={grabber.CapturedObject} reachable={grabber.GetReachablesCount()}");
                // Two physics steps of a missing pinch must not drop the prop.
                _keys=new KeyboardState();
                yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                _result.shortGapRetained[side]=grabber.CapturedObject==prop;
                _keys=new KeyboardState(Key.T);
                yield return new WaitForSeconds(.15f);
                _keys=new KeyboardState();
                yield return new WaitForSecondsRealtime(.2f);
                _result.pinchReleased[side]=!grabber.IsHolding;
                grabber.ForceRelease();Destroy(obj);
                foreach(var c in disabled)c.enabled=true;
            }
            _result.passed=_errors.Count==0 && _result.improvedMeanError<_result.baselineMeanError &&
                _result.improvedMaxError<.06f && _result.pinchCaptured.All(x=>x) &&
                _result.pinchReleased.All(x=>x) && _result.shortGapRetained.All(x=>x);
            _result.errors=_errors.ToArray();
            File.WriteAllText("Logs/interaction-quality.json",JsonUtility.ToJson(_result,true));
            EditorApplication.isPlaying=false;
        }
        void OnDestroy()
        {
            Application.logMessageReceived-=Log;
            Time.captureFramerate=0;
            foreach(var copy in _copies)if(copy!=null)Destroy(copy);
            if(Keyboard.current!=null)_keys=new KeyboardState();
        }
    }
}
