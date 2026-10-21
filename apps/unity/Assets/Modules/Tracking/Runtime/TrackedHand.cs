using PhysicsHands.Posing;
using UnityEngine;

namespace PhysicsHands.Tracking
{
    /// <summary>
    /// Source of a tracked hand. Implementations write the tracked pose into
    /// <see cref = "ReferenceHandPoser"/> (the authored reference skeleton) every frame and expose
    /// tracking quality and pinch state.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.TrackedHand)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Tracking", "PhysicsHands.Runtime", "TrackedHand")]
    public abstract class TrackedHand : MonoBehaviour, IHandPoseSource
    {
        [SerializeField]
        private HandSide _handedness = HandSide.Right;
        [Space]
        [SerializeField]
        private HandPoser _referenceHandPoser;
        [SerializeField]
        private Transform _palm;
        [SerializeField]
        private Transform _forearmWrist;
        [SerializeField]
        private Transform _forearmArm;
        [Tooltip("Seconds of continuous high-confidence tracking before tracking is considered good.")]
        [SerializeField]
        private float _goodTrackingTime = 0.2f;
        [SerializeField]
        protected Transform _head;
        public void BindHead(Transform head) => _head = head;
        private bool _goodTracking;
        private float _goodTrackingStartTime = -1f;
        private Quaternion _lastKnownRotation = Quaternion.identity;
        private bool _isPinchingIndex;
        private bool _isPinchingMiddle;
        public virtual bool UsesJointTracking => false;
        public HandSide Handedness => _handedness;
        public HandPoser ReferenceHandPoser => _referenceHandPoser;
        public Transform Palm => _palm;
        public Transform Wrist => _referenceHandPoser != null ? _referenceHandPoser.Wrist : transform;
        public bool GoodTracking => _goodTracking;
        public Quaternion LastKnownRotation => _lastKnownRotation;
        protected virtual void Start()
        {
        }

        protected virtual void Update()
        {
            bool confident = IsTracked() && HasHighConfidence();
            if (!confident)
            {
                _goodTrackingStartTime = -1f;
                _goodTracking = false;
            }
            else
            {
                if (_goodTrackingStartTime < 0f)
                    _goodTrackingStartTime = Time.time;
                _goodTracking = Time.time - _goodTrackingStartTime >= _goodTrackingTime;
                _lastKnownRotation = Wrist.rotation;
            }

            _isPinchingIndex = IsPinching(HandFinger.Index);
            _isPinchingMiddle = IsPinching(HandFinger.Middle);
        }

        public bool IsPinchingIndex => _isPinchingIndex;
        public bool IsPinchingMiddle => _isPinchingMiddle;
        public bool IsFacingHead()
        {
            if (_head == null || _palm == null)
                return false;
            Vector3 toHead = (_head.position - _palm.position).normalized;
            return Vector3.Dot(PalmNormal, toHead) > 0.5f;
        }

        /// <summary>World-space direction the palm faces.</summary>
        public virtual Vector3 PalmNormal => _palm != null ? -_palm.up : -Wrist.up;
        public void Show(bool show)
        {
            if (_referenceHandPoser != null)
                _referenceHandPoser.ShowTarget(show);
        }

        public abstract bool IsTracked();
        public abstract bool HasHighConfidence();
        public abstract bool FingerHasHighConfidence(HandFinger finger);
        public abstract bool IsGrasping();
        public abstract bool IsPinching(HandFinger finger);
        public abstract float GetPinchingStrength(HandFinger finger);
        public abstract float GetScale();
        public virtual Transform GetForearmWristJoint() => _forearmWrist;
        public virtual Transform GetForearmArmJoint() => _forearmArm;
    }
}
