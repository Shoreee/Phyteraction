using System;
using UnityEngine;

namespace PhysicsHands.Posing
{
    public enum HandSnapType
    {
        MoveHandToObject = 0,
        MoveObjectToHand = 1,
        MoveBoth = 2,
    }

    public enum HandAttachPoint
    {
        Palm = 0,
        Thumb = 10,
        Index = 20,
        Middle = 30,
        Ring = 40,
        Pinky = 50,
    }

    /// <summary>
    /// Authored grab pose for an object. Each side has a HandPoser (finger pose + wrist
    /// placement) under a chain of helper transforms:
    /// FacingRotator → SnapBaseRotator → SnapTranslocator → SnapRotator → HandPoser.
    /// Free-rotation axes and the translocation segment let one pose cover e.g. a whole
    /// cylinder handle.
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Posing", "PhysicsHands.Runtime", "HandPoserManager")]
    public class HandPoserManager : MonoBehaviour
    {
        [Serializable]
        public struct HandSnap
        {
            public HandPoser HandPoser;
            public HandPoser HandPoserAlternative;
            public Transform FacingRotator;
            public Transform SnapBaseRotator;
            public Transform SnapTranslocator;
            public Transform TranslocationMin;
            public Transform TranslocationMax;
            public Transform SnapRotator;
            public Transform JointAnchor;
            public bool IsValid => HandPoser != null;
            public void Validate()
            {
                if (HandPoser == null)
                    return;
                if (SnapRotator == null)
                    SnapRotator = HandPoser.transform.parent;
                if (JointAnchor == null)
                    JointAnchor = HandPoser.Wrist != null ? HandPoser.Wrist : HandPoser.transform;
            }
        }

        [SerializeField]
        private HandSnapType _snapType = HandSnapType.MoveObjectToHand;
        [SerializeField]
        private HandAttachPoint _handAttachPoint = HandAttachPoint.Palm;
        [SerializeField]
        private HandSnap _handSnapRight;
        [SerializeField]
        private HandSnap _handSnapLeft;
        [SerializeField]
        private float _spring = 10000f;
        [SerializeField]
        private float _damper = 100f;
        [SerializeField]
        private float _angularSpring = 1000f;
        [SerializeField]
        private float _angularDamper = 10f;
        [SerializeField]
        private bool _ignoreGravityOnGrab;
        [SerializeField]
        private float _handMassMultiplier = 1f;
        [SerializeField]
        private float _handSpringMultiplier = 1f;
        [SerializeField]
        private float _handDamperMultiplier = 1f;
        [SerializeField]
        private float _handAngularSpringMultiplier = 1f;
        [SerializeField]
        private float _handAngularDamperMultiplier = 1f;
        [SerializeField]
        private bool _ignoreHandCollidersOnGrab = true;
        [SerializeField]
        private float _snapTweenTime = 0.5f;
        [Space]
        [SerializeField]
        private bool _snapRotation = true;
        [SerializeField]
        private bool _xFreeRotation;
        [SerializeField]
        private bool _yFreeRotation;
        [SerializeField]
        private bool _zFreeRotation;
        [SerializeField]
        private bool _xAxisFlip;
        [SerializeField]
        private bool _canOnlyGrabFromTop;
        [Space]
        [SerializeField]
        private float _snapThresholdDistance = 0.1f;
        [SerializeField]
        private float _snapThresholdAngle = 60f;
        [Space]
        [SerializeField]
        private float _grabAntiGravityMultiplier = 1f;
        [SerializeField]
        private float _grabDragMultiplier = 1f;
        [SerializeField]
        private float _fingerSpringMultiplier = 1f;
        private Quaternion _leftInitialBaseRotation = Quaternion.identity;
        private Quaternion _rightInitialBaseRotation = Quaternion.identity;
        private bool _isGrabbed;
        public HandSnap HandSnapRight => _handSnapRight;
        public HandSnap HandSnapLeft => _handSnapLeft;
        public HandSnapType SnapType => _snapType;
        public HandAttachPoint HandAttachPoint => _handAttachPoint;
        public bool IgnoreGravityOnGrab => _ignoreGravityOnGrab;
        public float SnapThresholdDistance => _snapThresholdDistance;
        public float SnapThresholdAngle => _snapThresholdAngle;
        public float HandMassMultiplier => _handMassMultiplier;
        public float HandSpringMultiplier => _handSpringMultiplier;
        public float HandDamperMultiplier => _handDamperMultiplier;
        public float HandAngularSpringMultiplier => _handAngularSpringMultiplier;
        public float HandAngularDamperMultiplier => _handAngularDamperMultiplier;
        public bool SnapRotation => _snapRotation;
        public bool IgnoreHandCollidersOnGrab => _ignoreHandCollidersOnGrab;
        public float FingerSpringMultiplier => _fingerSpringMultiplier;
        public float GrabAntiGravityMultiplier => _grabAntiGravityMultiplier;
        public float GrabDragMultiplier => _grabDragMultiplier;
        public float SnapTweenTime => _snapTweenTime;
        public bool XFreeRotation => _xFreeRotation;
        public bool YFreeRotation => _yFreeRotation;
        public bool ZFreeRotation => _zFreeRotation;
        public bool XAxisFlip => _xAxisFlip;
        public bool CanOnlyGrabFromTop => _canOnlyGrabFromTop;
        public float Spring => _spring;
        public float Damper => _damper;
        public float AngularSpring => _angularSpring;
        public float AngularDamper => _angularDamper;
        public Quaternion LeftInitialBaseRotation => _leftInitialBaseRotation;
        public Quaternion RightInitialBaseRotation => _rightInitialBaseRotation;
        private void Awake() => Init();
        public virtual void Init()
        {
            _handSnapRight.Validate();
            _handSnapLeft.Validate();
            if (_handSnapRight.SnapBaseRotator != null)
                _rightInitialBaseRotation = _handSnapRight.SnapBaseRotator.localRotation;
            if (_handSnapLeft.SnapBaseRotator != null)
                _leftInitialBaseRotation = _handSnapLeft.SnapBaseRotator.localRotation;
        }

        private void Start()
        {
            ShowHandPose(HandSide.Left, false);
            ShowHandPose(HandSide.Right, false);
        }

        public HandSnap GetSnap(HandSide side) => side == HandSide.Right ? _handSnapRight : _handSnapLeft;
        public HandPoser GetHandPoser(HandSide side) => GetSnap(side).HandPoser;
        public HandPoser GetHandPoserAlternative(HandSide side) => GetSnap(side).HandPoserAlternative;
        /// <summary>Where the hand wrist must be for this pose.</summary>
        public Transform GetWristTarget(HandSide side)
        {
            HandPoser poser = GetHandPoser(side);
            return poser == null ? null : poser.Wrist != null ? poser.Wrist : poser.transform;
        }

        public Transform GetSnapTransform(HandSide side) => GetSnap(side).JointAnchor;
        public void ShowHandPose(HandSide side, bool show) => GetHandPoser(side)?.ShowTarget(show);
        public void ShowTarget(HandSide side, bool show) => ShowHandPose(side, show);
        public void ShowHandPoserRight(bool show) => ShowHandPose(HandSide.Right, show);
        public void ShowHandPoserLeft(bool show) => ShowHandPose(HandSide.Left, show);
        public void SetGrabbed(bool grabbed) => _isGrabbed = grabbed;
        public bool IsGrabbed() => _isGrabbed;
        /// <summary>
        /// Adapts the free axes / translocation of a side's snap so the pose is as close as
        /// possible to the approaching hand wrist. Called while a hand focuses the object.
        /// </summary>
        public void UpdateSnap(HandSide side, Transform handWrist)
        {
            HandSnap snap = GetSnap(side);
            if (!snap.IsValid || handWrist == null)
                return;
            if (snap.SnapTranslocator != null && snap.TranslocationMin != null && snap.TranslocationMax != null)
            {
                Vector3 min = snap.TranslocationMin.position;
                Vector3 segment = snap.TranslocationMax.position - min;
                float t = segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(handWrist.position - min, segment) / segment.sqrMagnitude) : 0f;
                snap.SnapTranslocator.position = min + segment * t;
            }

            if (snap.SnapBaseRotator == null)
                return;
            // Base rotator B carries the wrist target W = B * L. Find the local rotation R about the
            // free axes that best satisfies B * R * L = H (the hand wrist).
            Quaternion initial = side == HandSide.Right ? _rightInitialBaseRotation : _leftInitialBaseRotation;
            snap.SnapBaseRotator.localRotation = initial;
            Quaternion b = snap.SnapBaseRotator.rotation;
            Quaternion l = Quaternion.Inverse(b) * GetWristTarget(side).rotation;
            Quaternion desired = Quaternion.Inverse(b) * handWrist.rotation * Quaternion.Inverse(l);
            Quaternion r = Quaternion.identity;
            if (_xFreeRotation)
                r *= Twist(Quaternion.Inverse(r) * desired, Vector3.right);
            if (_yFreeRotation)
                r *= Twist(Quaternion.Inverse(r) * desired, Vector3.up);
            if (_zFreeRotation)
                r *= Twist(Quaternion.Inverse(r) * desired, Vector3.forward);
            if (_xAxisFlip)
            {
                Quaternion flipped = r * Quaternion.AngleAxis(180f, Vector3.right);
                if (Quaternion.Angle(b * flipped * l, handWrist.rotation) < Quaternion.Angle(b * r * l, handWrist.rotation))
                    r = flipped;
            }

            snap.SnapBaseRotator.localRotation = initial * r;
        }

        /// <summary>True when the hand is close enough in position and angle to the pose.</summary>
        public bool IsWithinSnapThreshold(HandSide side, Transform handWrist)
        {
            Transform target = GetWristTarget(side);
            if (target == null || handWrist == null)
                return false;
            if (_canOnlyGrabFromTop && Vector3.Dot(handWrist.position - transform.position, Vector3.up) < 0f)
                return false;
            return Vector3.Distance(target.position, handWrist.position) <= _snapThresholdDistance && (!_snapRotation || Quaternion.Angle(target.rotation, handWrist.rotation) <= _snapThresholdAngle);
        }

        private static Quaternion Twist(Quaternion rotation, Vector3 axis)
        {
            Vector3 r = new(rotation.x, rotation.y, rotation.z);
            Vector3 p = Vector3.Project(r, axis);
            var twist = new Quaternion(p.x, p.y, p.z, rotation.w);
            float mag = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (mag < 1e-6f)
                return Quaternion.identity;
            return new Quaternion(twist.x / mag, twist.y / mag, twist.z / mag, twist.w / mag);
        }

        private void OnValidate()
        {
            _handSnapRight.Validate();
            _handSnapLeft.Validate();
        }
    }
}
