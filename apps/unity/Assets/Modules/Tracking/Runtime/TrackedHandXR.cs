using PhysicsHands.Posing;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace PhysicsHands.Tracking
{
    /// <summary>Hand tracking through the XR Hands subsystem (visionOS, OpenXR).</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Tracking", "PhysicsHands.Runtime", "TrackedHandXR")]
    public class TrackedHandXR : TrackedHand
    {
        [SerializeField]
        private XRHandRetargeter _xrHandToOvrHandMapper;
        [Tooltip("XR Origin camera-floor offset. Joint poses are in this space, the same space as the headset.")]
        [SerializeField]
        private Transform _trackingSpace;
        [Header("Pinch (metres between tips, with hysteresis)")]
        [SerializeField]
        private float _pinchThresholdIndex = 0.02f;
        [SerializeField]
        private float _pinchThresholdMiddle = 0.025f;
        [SerializeField]
        private float _pinchReleaseMultiplier = 1.5f;
        [SerializeField]
        private float _pinchStrengthRange = 0.06f;
        [Header("Grasp")]
        [SerializeField]
        private float _graspThreshold = Posing.HandPoser.ModerateGraspThreshold;
        private static readonly List<XRHandSubsystem> s_Subsystems = new();
        private XRHandSubsystem _handSubsystem;
        private XRHand _xrHand;
        private readonly bool[] _pinching = new bool[5];
        public override bool UsesJointTracking => true;
        protected override void Start()
        {
            base.Start();
            // XR joint transforms are inputs, not a second hand to draw over the physical rig.
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            ResolveTrackingSpace();
            InitializeHandSubsystem();
        }

        /// <summary>
        /// Joint poses and the headset pose are both relative to the XR Origin floor offset.
        /// Transforming by the camera would apply the head pose a second time.
        /// </summary>
        private void ResolveTrackingSpace()
        {
            if (_trackingSpace == null)
                _trackingSpace = transform.parent;
        }

        public void BindTrackingSpace(Transform space) => _trackingSpace = space;
        public void InitializeHandSubsystem()
        {
            if (_handSubsystem != null && _handSubsystem.running)
                return;
            s_Subsystems.Clear();
            SubsystemManager.GetSubsystems(s_Subsystems);
            _handSubsystem = s_Subsystems.Find(s => s.running);
        }

        protected override void Update()
        {
            if (_handSubsystem == null || !_handSubsystem.running)
                InitializeHandSubsystem();
            if (_handSubsystem != null)
            {
                _xrHand = Handedness == HandSide.Right ? _handSubsystem.rightHand : _handSubsystem.leftHand;
                if (_xrHand.isTracked && _xrHandToOvrHandMapper != null)
                    _xrHandToOvrHandMapper.Map(_xrHand, _trackingSpace);
            }

            UpdatePinchStates();
            base.Update();
        }

        public override bool IsTracked() => _handSubsystem != null && _handSubsystem.running && _xrHand.isTracked;
        public override bool HasHighConfidence()
        {
            if (!IsTracked())
                return false;
            XRHandJointTrackingState state = _xrHand.GetJoint(XRHandJointID.Wrist).trackingState;
            return (state & XRHandJointTrackingState.Pose) == XRHandJointTrackingState.Pose;
        }

        public override bool FingerHasHighConfidence(HandFinger finger) => IsTracked() && _xrHand.GetJoint(TipOf(finger)).TryGetPose(out _);
        public override bool IsGrasping() => ReferenceHandPoser != null && ReferenceHandPoser.GetGranularGraspAmount() >= _graspThreshold;
        public override bool IsPinching(HandFinger finger) => finger != HandFinger.Thumb && _pinching[(int)finger];
        public override float GetPinchingStrength(HandFinger finger)
        {
            float distance = PinchDistance(finger);
            if (float.IsInfinity(distance))
                return 0f;
            float threshold = finger == HandFinger.Index ? _pinchThresholdIndex : _pinchThresholdMiddle;
            return 1f - Mathf.Clamp01((distance - threshold) / _pinchStrengthRange);
        }

        public override float GetScale() => _xrHandToOvrHandMapper != null ? _xrHandToOvrHandMapper.GetScale() : 1f;
        private void UpdatePinchStates()
        {
            for (var finger = HandFinger.Index; finger <= HandFinger.Pinky; finger++)
            {
                float threshold = finger == HandFinger.Index ? _pinchThresholdIndex : _pinchThresholdMiddle;
                float distance = PinchDistance(finger);
                int i = (int)finger;
                _pinching[i] = _pinching[i] ? distance < threshold * _pinchReleaseMultiplier : distance < threshold;
            }
        }

        private float PinchDistance(HandFinger finger)
        {
            if (!IsTracked())
                return float.PositiveInfinity;
            if (!_xrHand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb) || !_xrHand.GetJoint(TipOf(finger)).TryGetPose(out Pose tip))
                return float.PositiveInfinity;
            float scale = _trackingSpace != null ? _trackingSpace.lossyScale.x : 1f;
            return Vector3.Distance(thumb.position, tip.position) * scale;
        }

        private static XRHandJointID TipOf(HandFinger finger) => finger switch
        {
        HandFinger.Thumb => XRHandJointID.ThumbTip, HandFinger.Index => XRHandJointID.IndexTip, HandFinger.Middle => XRHandJointID.MiddleTip, HandFinger.Ring => XRHandJointID.RingTip, _ => XRHandJointID.LittleTip, }

        ;
    }
}
