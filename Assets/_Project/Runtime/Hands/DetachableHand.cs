using System;
using PhysicsHands.Dynamics;
using PhysicsHands.Posing;
using PhysicsHands.Settings;
using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>
    /// The physical wrist body. Every physics step it converts <see cref="HandSettings"/>
    /// into wrist joint drives, scaled by tracking quality, grab, drag and finger-pin
    /// multipliers. It can also detach (drives off, gravity on) and reattach.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.JointEnforcer - 10)]
    public class DetachableHand : MonoBehaviour
    {
        [SerializeField] private HoloHand _holoHand;
        [SerializeField] private ConfigurableJoint _joint;
        [SerializeField] private Rigidbody _rb;
        [SerializeField] private JointEnforcer _enforcer;
        [SerializeField] private bool _detachOnStart;
        [SerializeField] private VelocityEstimator _targetVelocityEstimator;
        [SerializeField] private Transform _attachedCOM;
        [SerializeField] private Transform _detachedCOM;
        [SerializeField] private Collider _detachableTrigger;
        [SerializeField] private LineRenderer _springLine;

        [SerializeField] private bool _canBeAttached = true;
        [SerializeField] private bool _canBeDetached = true;
        [SerializeField] private bool _attached = true;
        [SerializeField] private float _reattachMinHeight = 0.3f;
        [SerializeField] private float _reattachDelay = 1f;
        [Tooltip("Detach automatically when further than this from the target. 0 disables.")]
        [SerializeField] private float _autoDetachDistance;

        [SerializeField] private Transform _palm;
        [SerializeField] private Rigidbody _thumbRb;
        [SerializeField] private Rigidbody _indexRb;
        [SerializeField] private Rigidbody _middleRb;
        [SerializeField] private Rigidbody _ringRb;
        [SerializeField] private Rigidbody _pinkyRb;

        private float _dragSpringMultiplier = 1f;
        private float _dragDamperMultiplier = 1f;
        private float _detachMultiplier = 1f;
        private float _trackingSpringMultiplier = 1f;
        private float _trackingDamperMultiplier = 1f;
        private float _trackingSpringGoal = 1f;
        private float _trackingDamperGoal = 1f;
        private float _trackingRate = float.PositiveInfinity;
        private float _grabSpringMultiplier = 1f;
        private float _grabDamperMultiplier = 1f;
        private float _grabAngularSpringMultiplier = 1f;
        private float _grabAngularDamperMultiplier = 1f;
        private float _fingersPinMultiplier = 1f;
        private Vector3 _lastTargetPosition;
        private Vector3 _sampledTargetVelocity;
        private bool _hasTargetSample;
        private float _initialMass;
        private float _initialDrag;
        private float _detachedTime;

        public HoloHand HoloHand => _holoHand;
        public JointEnforcer Enforcer => _enforcer;
        public Rigidbody Rb => _rb;
        public bool Attached => _attached;
        public Transform Palm => _palm;

        public event Action<DetachableHand> OnAttach;
        public event Action<DetachableHand> OnDetach;

        private void Awake()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_enforcer == null) _enforcer = GetComponent<JointEnforcer>();
            if (_joint == null) _joint = _enforcer.Joint;
            if (_holoHand == null) _holoHand = GetComponentInParent<HoloHand>();
            _initialMass = _rb.mass;
            _initialDrag = _rb.linearDamping;
        }

        private void Start()
        {
            if (_attachedCOM != null) _enforcer.SetCenterOfMass(_attachedCOM);
            if (_detachOnStart) DetachHand(0f);
        }

        public Rigidbody GetAttachPoint(HandAttachPoint point) => point switch
        {
            HandAttachPoint.Thumb when _thumbRb != null => _thumbRb,
            HandAttachPoint.Index when _indexRb != null => _indexRb,
            HandAttachPoint.Middle when _middleRb != null => _middleRb,
            HandAttachPoint.Ring when _ringRb != null => _ringRb,
            HandAttachPoint.Pinky when _pinkyRb != null => _pinkyRb,
            _ => _rb,
        };

        private void Update()
        {
            if (!_attached) CheckReattach();
            else if (_canBeDetached && _autoDetachDistance > 0f && _enforcer.GetDistanceFromTarget() > _autoDetachDistance)
                DetachHand(0.2f);

            SampleTargetVelocity();
            HandleSpringLine();
        }

        private void SampleTargetVelocity()
        {
            if (!_attached || _holoHand == null || !_holoHand.GoodTracking || Time.deltaTime <= 0f)
            {
                _hasTargetSample = false;
                _sampledTargetVelocity = Vector3.zero;
                return;
            }
            Vector3 position = _enforcer.GetTargetPosition();
            Vector3 delta = position - _lastTargetPosition;
            // Reacquisition/recentering is a pose change, not throwing velocity.
            if (!_hasTargetSample || delta.sqrMagnitude > .25f * .25f)
                _sampledTargetVelocity = Vector3.zero;
            else
                _sampledTargetVelocity = Vector3.Lerp(_sampledTargetVelocity,
                    Vector3.ClampMagnitude(delta / Time.deltaTime, 3f),
                    1f - Mathf.Exp(-2f * Mathf.PI * 30f * Time.deltaTime));
            _lastTargetPosition = position;
            _hasTargetSample = true;
        }

        private void FixedUpdate()
        {
            float step = _trackingRate * Time.fixedDeltaTime;
            _trackingSpringMultiplier = Mathf.MoveTowards(_trackingSpringMultiplier, _trackingSpringGoal, step);
            _trackingDamperMultiplier = Mathf.MoveTowards(_trackingDamperMultiplier, _trackingDamperGoal, step);

            ComputeFingersPin();
            ComputeDynamicJointValues();
            HandleAntiGravity();
        }

        private void ComputeFingersPin()
        {
            _fingersPinMultiplier = 1f;
            if (_holoHand == null) return;
            foreach (HoloFinger finger in _holoHand.Fingers)
            {
                float pinned = Mathf.Lerp(1f, finger.UnpinWristMultiplier, finger.GetFingerPinValue());
                _fingersPinMultiplier = Mathf.Max(_fingersPinMultiplier, pinned);
            }
        }

        private void ComputeDynamicJointValues()
        {
            HandSettings settings = _holoHand != null ? _holoHand.HandSettings : null;
            if (settings == null) return;

            Vector3 targetVelocity = _targetVelocityEstimator != null
                ? _targetVelocityEstimator.GetVelocityEstimate()
                : settings.EnableTargetVelocityFollow ? _sampledTargetVelocity : _enforcer.TargetVelocity;
            Vector3 handVelocity = _rb.linearVelocity;
            var inputs = new HandSettings.Inputs
            {
                PositionError = _enforcer.GetDistanceFromTarget(),
                AngleError = _enforcer.GetAngleFromTarget() * Mathf.Deg2Rad,
                TargetSpeed = targetVelocity.magnitude,
                HandSpeed = handVelocity.magnitude,
                DeltaSpeed = (targetVelocity - handVelocity).magnitude,
            };

            Vector3 inertia = _rb.inertiaTensor;
            float meanInertia = (inertia.x + inertia.y + inertia.z) / 3f;
            float mass = _rb.mass;

            float linearSpring = settings.PositionSpring(inputs) * mass * settings.LinearSpringScale *
                (settings.EnableTargetVelocityFollow ? Mathf.Max(1f, settings.WristResponseMultiplier) : 1f);
            float linearDamper = settings.PositionDamper(inputs) * mass * settings.LinearDamperScale;
            // Serialized speed coefficient is 1000. Using it without a damping
            // bound can produce seconds of lag once a velocity source is connected.
            linearDamper = Mathf.Min(linearDamper, 2f * Mathf.Sqrt(linearSpring * mass) *
                Mathf.Max(.1f, settings.MaximumLinearDampingRatio));
            float angularSpring = settings.RotationSpring(inputs) * meanInertia * settings.AngularSpringScale;
            float angularDamper = settings.RotationDamper(inputs) * meanInertia * settings.AngularSpringScale +
                                  2f * Mathf.Sqrt(angularSpring * meanInertia) * settings.AngularDampingRatio;

            HumanoidSettings humanoid = _holoHand.HumanoidSettings;
            PhysicsSetup setup = _holoHand.PhysicsSetup;
            float wristSpring = (humanoid ? humanoid.WristSpringMultiplier : 1f) * (setup ? setup.humanoidWristSpringMultiplier : 1f);
            float wristAngularSpring = (humanoid ? humanoid.WristAngularSpringMultiplier : 1f) * (setup ? setup.humanoidWristSpringMultiplier : 1f);
            float wristDamper = (humanoid ? humanoid.WristDamperMultiplier : 1f) * (setup ? setup.humanoidWristDamperMultiplier : 1f);
            float wristAngularDamper = (humanoid ? humanoid.WristAngularDamperMultiplier : 1f) * (setup ? setup.humanoidWristDamperMultiplier : 1f);

            float common = _detachMultiplier * _trackingSpringMultiplier * _dragSpringMultiplier;
            float commonDamper = _detachMultiplier * _trackingDamperMultiplier * _dragDamperMultiplier;

            _enforcer.SetDriveMultipliers(
                linearSpring * wristSpring * common * _grabSpringMultiplier * _fingersPinMultiplier,
                linearDamper * wristDamper * commonDamper * _grabDamperMultiplier,
                angularSpring * wristAngularSpring * common * _grabAngularSpringMultiplier * _fingersPinMultiplier,
                angularDamper * wristAngularDamper * commonDamper * _grabAngularDamperMultiplier);


        }

        private void HandleAntiGravity()
        {
            if (!_attached || _holoHand == null) return;
            Rigidbody held = _holoHand.HeldRigidbody;
            if (held == null || held.isKinematic || !held.useGravity) return;
            _rb.AddForce(-Physics.gravity * (held.mass * _holoHand.GrabAntiGravityMultiplier), ForceMode.Force);
        }

        private void HandleSpringLine()
        {
            if (_springLine == null) return;
            bool show = !_attached || _enforcer.GetDistanceFromTarget() > 0.05f;
            _springLine.enabled = show;
            if (!show) return;
            _springLine.positionCount = 2;
            _springLine.SetPosition(0, transform.position);
            _springLine.SetPosition(1, _enforcer.GetTargetPosition());
        }

        public void InstantMapToTarget()
        {
            _enforcer.InstantMapToTarget();
            if (_holoHand == null) return;
            foreach (HoloFinger finger in _holoHand.Fingers) finger.InstantMapToTarget();
        }

        /// <summary>Temporarily weakens the wrist (e.g. while tracking is poor) and eases back over <paramref name="duration"/>.</summary>
        public void SetTrackingQualityDriveMultiplier(float springMultiplier, float damperMultiplier, float duration)
        {
            _trackingSpringGoal = springMultiplier;
            _trackingDamperGoal = damperMultiplier;
            float distance = Mathf.Max(Mathf.Abs(_trackingSpringMultiplier - springMultiplier), Mathf.Abs(_trackingDamperMultiplier - damperMultiplier));
            _trackingRate = duration > 0f ? distance / duration : float.PositiveInfinity;
        }

        public void SetJointGrabDriveMultiplier(float springMultiplier, float damperMultiplier, float angularSpringMultiplier, float angularDamperMultiplier)
        {
            _grabSpringMultiplier = springMultiplier;
            _grabDamperMultiplier = damperMultiplier;
            _grabAngularSpringMultiplier = angularSpringMultiplier;
            _grabAngularDamperMultiplier = angularDamperMultiplier;
        }

        public void SetJointGrabDriveSpringMultiplier(float springMultiplier, float angularSpringMultiplier)
        {
            _grabSpringMultiplier = springMultiplier;
            _grabAngularSpringMultiplier = angularSpringMultiplier;
        }

        public void SetDragSpringMultiplier(float value) => _dragSpringMultiplier = value;
        public void SetDragDamperMultiplier(float value) => _dragDamperMultiplier = value;
        public void SetDrag(float multiplier) => _rb.linearDamping = _initialDrag * multiplier;
        public void SetMassMultiplier(float multiplier) => _rb.mass = _initialMass * multiplier;
        public void AdjustJoint() => _enforcer.AdjustJoint();

        public void DetachHand(float detachTime)
        {
            if (_holoHand != null && _holoHand.DirectTracking) return;
            if (!_attached || !_canBeDetached) return;
            _attached = false;
            _detachedTime = Time.time;
            _detachMultiplier = 0f;
            _enforcer.CancelGravity = false;
            if (_detachedCOM != null) _enforcer.SetCenterOfMass(_detachedCOM);
            if (_detachableTrigger != null) _detachableTrigger.enabled = true;
            OnDetach?.Invoke(this);
        }

        public void Reattach(bool makeKinematic) => AttachHand(makeKinematic);

        public void AttachHand(bool makeKinematic)
        {
            if (_attached || !_canBeAttached) return;
            _attached = true;
            _detachMultiplier = 1f;
            _enforcer.ResetCancelGravity();
            if (_attachedCOM != null) _enforcer.SetCenterOfMass(_attachedCOM);
            if (_detachableTrigger != null) _detachableTrigger.enabled = false;
            InstantMapToTarget();
            if (makeKinematic && _holoHand != null) _holoHand.MakeKinematic(true, false);
            OnAttach?.Invoke(this);
        }

        private void CheckReattach()
        {
            if (Time.time - _detachedTime < _reattachDelay) return;
            bool fellOff = transform.position.y < _reattachMinHeight;
            bool touched = _detachableTrigger != null && _holoHand != null &&
                           Vector3.Distance(_enforcer.GetTargetPosition(), transform.position) < 0.08f;
            if (fellOff || touched) AttachHand(false);
        }
    }
}
