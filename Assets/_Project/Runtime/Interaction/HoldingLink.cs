using System;
using PhysicsHands.Dynamics;
using PhysicsHands.Hands;
using PhysicsHands.Posing;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>
    /// Physical connection between a holder and a held object: a ConfigurableJoint on the
    /// object connected to the hand's attach rigidbody. With a HandPoserManager the object
    /// is pulled (over <c>snapTweenTime</c>) until its authored wrist pose matches the
    /// physical wrist; without one the current relative pose is kept (procedural grasp).
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.Holder + 1)]
    public class HoldingLink : MonoBehaviour
    {
        [Header("Procedural grasp drives")]
        [SerializeField] private float _proceduralSpring = 10000f;
        [SerializeField] private float _proceduralDamper = 100f;
        [SerializeField] private float _proceduralAngularSpring = 1000f;
        [SerializeField] private float _proceduralAngularDamper = 10f;
        [SerializeField] private float _maximumForce = 5000f;
        [SerializeField] private float _releaseCollisionDelay = 0.3f;

        private Holder _holder;
        private HandGrabber _handGrabber;
        private Holdable _holdable;
        private HandPoserManager _poser;
        private ConfigurableJoint _holdingJoint;
        private Rigidbody _connectedBody;
        private bool _isSnapped;
        private bool _distantGrab;
        private bool _ignoredHandColliders;
        private float _snapProgress;
        private float _snapDuration;
        private Vector3 _startConnectedAnchor;
        private Vector3 _goalConnectedAnchor;
        private Quaternion _jointStartLocalRotation;
        private Quaternion _startTargetLocal;
        private Quaternion _goalTargetLocal;

        public event Action<HoldingLink> OnSnap;

        public bool IsAttached => _holdingJoint != null;
        public bool IsSnapped => _isSnapped;
        public Holdable Holdable => _holdable;

        private void Awake()
        {
            _holder = GetComponent<Holder>();
            _handGrabber = _holder as HandGrabber;
        }

        public void Attach(Holder holder, Holdable holdable, bool distantGrab)
        {
            Release();
            _holder = holder;
            _handGrabber = holder as HandGrabber;
            _holdable = holdable;
            _distantGrab = distantGrab;
            if (holdable.Rigidbody.isKinematic) return;

            HoloHand hand = _handGrabber != null ? _handGrabber.Hand : null;
            HandSide side = hand != null ? hand.HandType : HandSide.Right;
            _poser = holdable.HandPoserManager;
            if (_poser != null && _poser.GetHandPoser(side) == null) _poser = null;

            _connectedBody = hand != null
                ? hand.DetachableHand.GetAttachPoint(_poser != null ? _poser.HandAttachPoint : HandAttachPoint.Palm)
                : GetComponentInParent<Rigidbody>();
            InitializeJoint(hand, side);
            ApplyHandMultipliers(hand, true);
        }

        private void InitializeJoint(HoloHand hand, HandSide side)
        {
            Rigidbody body = _holdable.Rigidbody;
            _holdingJoint = body.gameObject.AddComponent<ConfigurableJoint>();
            _holdingJoint.autoConfigureConnectedAnchor = false;
            _holdingJoint.connectedBody = _connectedBody;
            _holdingJoint.enableCollision = false;
            _holdingJoint.enablePreprocessing = false;
            _holdingJoint.rotationDriveMode = RotationDriveMode.Slerp;
            // Explicitly drive compliant axes. Locked axes bypass the configured
            // springs and fight finger contacts with an effectively rigid weld.
            _holdingJoint.xMotion = _holdingJoint.yMotion = _holdingJoint.zMotion = ConfigurableJointMotion.Free;
            _holdingJoint.angularXMotion = _holdingJoint.angularYMotion = _holdingJoint.angularZMotion = ConfigurableJointMotion.Free;

            Transform handWrist = hand != null && hand.Wrist != null ? hand.Wrist : _holder.SnapJointAnchor;
            Transform connected = _connectedBody != null ? _connectedBody.transform : null;
            Quaternion connectedRotation = connected != null ? connected.rotation : Quaternion.identity;

            Vector3 objectAnchorWorld;
            Vector3 goalAnchorWorld;
            Quaternion goalObjectRotation;
            if (_poser != null)
            {
                Transform wristTarget = _poser.GetWristTarget(side);
                objectAnchorWorld = wristTarget.position;
                goalAnchorWorld = handWrist.position;
                goalObjectRotation = handWrist.rotation * Quaternion.Inverse(wristTarget.rotation) * body.rotation;
                _snapDuration = _poser.SnapType == HandSnapType.MoveHandToObject ? 0f : _poser.SnapTweenTime;
            }
            else
            {
                objectAnchorWorld = _holder.SnapJointAnchor.position;
                goalAnchorWorld = objectAnchorWorld;
                goalObjectRotation = body.rotation;
                _snapDuration = 0f;
            }
            if (_distantGrab) _snapDuration = Mathf.Max(_snapDuration, 0.3f);

            _holdingJoint.anchor = body.transform.InverseTransformPoint(objectAnchorWorld);
            _startConnectedAnchor = connected != null ? connected.InverseTransformPoint(objectAnchorWorld) : objectAnchorWorld;
            _goalConnectedAnchor = connected != null ? connected.InverseTransformPoint(goalAnchorWorld) : goalAnchorWorld;
            _holdingJoint.connectedAnchor = _snapDuration > 0f ? _startConnectedAnchor : _goalConnectedAnchor;

            _jointStartLocalRotation = Quaternion.Inverse(connectedRotation) * body.rotation;
            _startTargetLocal = _jointStartLocalRotation;
            _goalTargetLocal = Quaternion.Inverse(connectedRotation) * goalObjectRotation;

            float spring = _poser != null ? _poser.Spring : _proceduralSpring;
            float damper = _poser != null ? _poser.Damper : _proceduralDamper;
            float angularSpring = _poser != null ? _poser.AngularSpring : _proceduralAngularSpring;
            float angularDamper = _poser != null ? _poser.AngularDamper : _proceduralAngularDamper;
            AdjustJoint(spring, angularSpring, damper, angularDamper);

            _snapProgress = _snapDuration > 0f ? 0f : 1f;
            _isSnapped = _snapProgress >= 1f;
            ApplySnapProgress();
            if (_isSnapped) OnSnap?.Invoke(this);
        }

        public void AdjustJoint(float spring, float angularSpring, float damper, float angularDamper)
        {
            if (_holdingJoint == null) return;
            var linear = new JointDrive { positionSpring = spring, positionDamper = damper, maximumForce = _maximumForce };
            _holdingJoint.xDrive = linear;
            _holdingJoint.yDrive = linear;
            _holdingJoint.zDrive = linear;
            _holdingJoint.slerpDrive = new JointDrive
            {
                positionSpring = angularSpring, positionDamper = angularDamper, maximumForce = _maximumForce,
            };
        }

        private void ApplyHandMultipliers(HoloHand hand, bool grabbing)
        {
            if (hand == null) return;
            Collider[] colliders = new Collider[_holdable.Colliders.Count];
            for (int i = 0; i < colliders.Length; i++) colliders[i] = _holdable.Colliders[i];

            if (!grabbing)
            {
                hand.ResetGrabMultipliers();
                hand.HeldRigidbody = null;
                hand.CustomPose = null;
                if (_ignoredHandColliders)
                {
                    hand.MakeAllBoneCollidersIgnoreColliders(colliders, false);
                    hand.MakeFingersIgnoreCollidersForDuration(colliders, _releaseCollisionDelay);
                }
                _ignoredHandColliders = false;
                return;
            }

            hand.HeldRigidbody = _holdable.Rigidbody;
            if (_poser == null) return;

            hand.SetGrabDriveMultipliers(_poser.HandSpringMultiplier, _poser.HandDamperMultiplier,
                _poser.HandAngularSpringMultiplier, _poser.HandAngularDamperMultiplier);
            hand.GrabAntiGravityMultiplier = _poser.GrabAntiGravityMultiplier;
            hand.GrabDragMultiplier = _poser.GrabDragMultiplier;
            hand.SetFingerSpring(_poser.FingerSpringMultiplier);
            hand.CustomPose = _poser.GetHandPoser(hand.HandType);
            hand.CustomPoseAlternative = _poser.GetHandPoserAlternative(hand.HandType);
            if (_poser.IgnoreGravityOnGrab) _holdable.Rigidbody.useGravity = false;
            if (_poser.IgnoreHandCollidersOnGrab)
            {
                hand.MakeAllBoneCollidersIgnoreColliders(colliders, true);
                _ignoredHandColliders = true;
            }
        }

        private void FixedUpdate()
        {
            if (_holdingJoint == null) return;
            if (!_isSnapped) CheckSnap();
            CheckDetach();
        }

        private void CheckSnap()
        {
            _snapProgress = Mathf.MoveTowards(_snapProgress, 1f, Time.fixedDeltaTime / Mathf.Max(1e-4f, _snapDuration));
            ApplySnapProgress();
            if (_snapProgress < 1f) return;
            _isSnapped = true;
            OnSnap?.Invoke(this);
        }

        private void ApplySnapProgress()
        {
            float t = Mathf.SmoothStep(0f, 1f, _snapProgress);
            _holdingJoint.connectedAnchor = Vector3.Lerp(_startConnectedAnchor, _goalConnectedAnchor, t);
            _holdingJoint.SetTargetRotationLocal(Quaternion.Slerp(_startTargetLocal, _goalTargetLocal, t), _jointStartLocalRotation);
        }

        private void CheckDetach()
        {
            if (_handGrabber == null || _handGrabber.Hand == null) return;
            if (_isSnapped && DistanceFromJointAnchor() > _holdable.HandToObjectReleaseDistance)
            {
                _holder.Release();
                return;
            }

            JointEnforcer wrist = _handGrabber.Hand.DetachableHand.Enforcer;
            if (wrist.GetDistanceFromTarget() > _holdable.HandToTargetReleaseDistance) _holder.Release();
        }

        /// <summary>Distance between the two joint anchors in world space.</summary>
        public float DistanceFromJointAnchor()
        {
            if (_holdingJoint == null) return 0f;
            Vector3 a = _holdingJoint.transform.TransformPoint(_holdingJoint.anchor);
            Vector3 b = _connectedBody != null ? _connectedBody.transform.TransformPoint(_holdingJoint.connectedAnchor) : _holdingJoint.connectedAnchor;
            return Vector3.Distance(a, b);
        }

        public float AngleFromJointAnchor()
        {
            if (_holdingJoint == null) return 0f;
            Quaternion connected = _connectedBody != null ? _connectedBody.rotation : Quaternion.identity;
            return Quaternion.Angle(connected * _goalTargetLocal, _holdingJoint.transform.rotation);
        }

        private void OnJointBreak(float breakForce)
        {
            if (_holder != null) _holder.Release();
        }

        public void Release()
        {
            if (_holdable == null) return;
            HoloHand hand = _handGrabber != null ? _handGrabber.Hand : null;
            ApplyHandMultipliers(hand, false);
            if (_holdingJoint != null) Destroy(_holdingJoint);
            _holdingJoint = null;
            _holdable = null;
            _poser = null;
            _isSnapped = false;
        }

        public void ResetJoint()
        {
            if (_holdable == null) return;
            Holdable holdable = _holdable;
            Release();
            Attach(_holder, holdable, false);
        }
    }
}
