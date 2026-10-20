using UnityEngine;

namespace PhysicsHands.Dynamics
{
    /// <summary>
    /// Drives a rigidbody towards a target transform through its ConfigurableJoint.
    /// Used by the physical wrist (target = tracked hand) and by every finger bone
    /// (target = matching bone of the visual HandPoser).
    ///
    /// Distal phalanges are configured as <c>_kinematic + _removeJoint + _rotationOnly</c>:
    /// they lose their joint and simply copy the target's local rotation while riding
    /// on the parent phalanx.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.JointEnforcer)]
    public class JointEnforcer : MonoBehaviour
    {
        [SerializeField] private bool _enforce = true;

        [Space]
        [SerializeField] private ConfigurableJoint _joint;
        [SerializeField] private Transform _targetRotation;
        [SerializeField] private bool _createTarget;

        [Space]
        [SerializeField] private bool _reinitializeOnEnable;
        [SerializeField] private bool _resetOnDisable;
        [Tooltip("Keep the connected anchor at the rest position under the (animated) parent transform.")]
        [SerializeField] private bool _updateAnchor;

        [Space]
        [SerializeField] private bool _kinematic;
        [SerializeField] private bool _removeJoint;
        [SerializeField] private bool _rotationOnly;
        [SerializeField] private bool _cancelGravity = true;
        [SerializeField] private int _solverIterationCount = 12;
        [SerializeField] private Transform _customMassCenter;

        [Space]
        [SerializeField] private bool _useTargetVelocity;
        [SerializeField] private VelocityEstimator _velocityEstimator;

        private Rigidbody _rigidbody;
        private float _initialMass;
        private float _positionSpringMultiplier = 1f;
        private float _positionDamperMultiplier = 1f;
        private float _angularSpringMultiplier = 1f;
        private float _angularDamperMultiplier = 1f;
        private Vector3 _initialLossyScale;
        private Vector3 _initialConnectedLossyScale;
        private Vector3 _initialLocalPosition;
        private Quaternion _initialLocalRotation;
        private Quaternion _initialTargetLocalRotation;
        private Quaternion _jointStartLocalRotation;
        private Vector3 _initialAnchor;
        private Vector3 _initialConnectedAnchor;
        private JointDrive _initialXDrive;
        private JointDrive _initialYDrive;
        private JointDrive _initialZDrive;
        private JointDrive _initialAngularXDrive;
        private JointDrive _initialAngularYZDrive;
        private JointDrive _initialSlerpDrive;
        private bool _initialCancelGravity;
        private bool _initialized;
        private Transform _targetConnectedSpace;
        private Quaternion _targetRestRelative;

        public int SolverIterationCount => _solverIterationCount;
        public Rigidbody Rigidbody => _rigidbody;
        public ConfigurableJoint Joint => _joint;
        public bool UpdateAnchor => _updateAnchor;
        public Transform TargetRotation => _targetRotation;
        public bool IsKinematic => _rigidbody != null && _rigidbody.isKinematic;

        public bool Enforce
        {
            get => _enforce;
            set => _enforce = value;
        }

        public bool CancelGravity
        {
            get => _cancelGravity;
            set => _cancelGravity = value;
        }

        public float PositionSpringMultiplier { get => _positionSpringMultiplier; set { _positionSpringMultiplier = value; ApplyDrives(); } }
        public float PositionDamperMultiplier { get => _positionDamperMultiplier; set { _positionDamperMultiplier = value; ApplyDrives(); } }
        public float AngularSpringMultiplier { get => _angularSpringMultiplier; set { _angularSpringMultiplier = value; ApplyDrives(); } }
        public float AngularDamperMultiplier { get => _angularDamperMultiplier; set { _angularDamperMultiplier = value; ApplyDrives(); } }

        public Vector3 TargetVelocity =>
            _useTargetVelocity && _velocityEstimator != null ? _velocityEstimator.GetVelocityEstimate() : Vector3.zero;

        private Transform ConnectedSpace =>
            _joint != null && _joint.connectedBody != null ? _joint.connectedBody.transform : transform.parent;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            if (_joint == null) _joint = GetComponent<ConfigurableJoint>();

            _initialMass = _rigidbody.mass;
            _initialLocalPosition = transform.localPosition;
            _initialLocalRotation = transform.localRotation;
            _initialLossyScale = transform.lossyScale;
            _initialCancelGravity = _cancelGravity;

            if (_targetRotation == null && _createTarget) CreateTarget();
            if (_targetRotation != null) _initialTargetLocalRotation = _targetRotation.localRotation;
            CaptureJointState();
            // Reference rotations must be relative to the reference parent, never
            // to an already deflected physical parent (which creates feedback).
            if (_joint != null && _joint.connectedBody != null && _targetRotation != null)
            {
                var parentEnforcer = _joint.connectedBody.GetComponent<JointEnforcer>();
                if (_targetRotation.IsChildOf(_joint.connectedBody.transform))
                    _targetConnectedSpace = _joint.connectedBody.transform;
                else if (parentEnforcer != null) _targetConnectedSpace = parentEnforcer.TargetRotation;
                if (_targetConnectedSpace != null)
                    _targetRestRelative = Quaternion.Inverse(_targetConnectedSpace.rotation) * _targetRotation.rotation;
            }
        }

        private void Start()
        {
            InitializeJoint();

            if (_customMassCenter != null) SetCenterOfMass(_customMassCenter);
            if (_removeJoint && _joint != null)
            {
                Destroy(_joint);
                _joint = null;
            }
            if (_kinematic) MakeKinematic(true);
            // This body's render pose already inherits the interpolated parent.
            // A second world-space interpolation would pull the tip off the finger.
            if (FollowsParent) _rigidbody.interpolation = RigidbodyInterpolation.None;
        }

        private void OnEnable()
        {
            if (_initialized && _reinitializeOnEnable) ResetJoint();
        }

        private void OnDisable()
        {
            if (_initialized && _resetOnDisable) ResetPosition();
        }

        private bool FollowsParent => _kinematic && _removeJoint && _rotationOnly;

        // Dynamic parents are interpolated after Update. Restore the complete local
        // attachment before RigMapper reads the skeleton in LateUpdate. Otherwise a
        // jointless tip retains an old world position as its parent moves away.
        private void LateUpdate()
        {
            if (_initialized && IsKinematic && _joint == null && FollowsParent)
                transform.SetLocalPositionAndRotation(_initialLocalPosition, AttachedLocalRotation);
        }

        private Quaternion AttachedLocalRotation
        {
            get
            {
                if (_targetRotation == null) return _initialLocalRotation;
                return _initialLocalRotation * Quaternion.Inverse(_initialTargetLocalRotation) * _targetRotation.localRotation;
            }
        }

        private Pose AttachedPose(bool physicsPose)
        {
            Transform parent = transform.parent;
            if (parent == null) return new Pose(_initialLocalPosition, AttachedLocalRotation);
            Rigidbody parentBody = physicsPose ? parent.GetComponent<Rigidbody>() : null;
            Vector3 position = parentBody != null ? parentBody.position : parent.position;
            Quaternion rotation = parentBody != null ? parentBody.rotation : parent.rotation;
            return new Pose(position + rotation * Vector3.Scale(parent.lossyScale, _initialLocalPosition),
                rotation * AttachedLocalRotation);
        }

        private void FixedUpdate()
        {
            if (!_initialized) return;

            if (_updateAnchor) DoUpdateAnchor();
            AdjustJoint();

            if (IsKinematic)
            {
                Map();
                return;
            }

            HandleGravity();
            if (_enforce) HandleJointTarget();
        }

        public void InitializeJoint()
        {
            _rigidbody.solverIterations = Mathf.Max(1, _solverIterationCount);
            _rigidbody.maxAngularVelocity = 100f;
            ApplyDrives();
            _initialized = true;
        }

        /// <summary>
        /// Refreshes drive and anchors without rebasing the authored rest frame.
        /// Reconnecting a deflected body would bake its deformation into the joint.
        /// </summary>
        public void ResetJoint()
        {
            if (_joint == null) return;
            // Preserve the authored rest frame when recovering from a deflected pose.
            RefreshAnchors();
            ApplyDrives();
        }

        private void CaptureJointState()
        {
            if (_joint == null) return;

            Transform space = ConnectedSpace;
            _jointStartLocalRotation = space != null ? Quaternion.Inverse(space.rotation) * transform.rotation : transform.rotation;
            _initialAnchor = _joint.anchor;
            _initialConnectedAnchor = _joint.connectedAnchor;
            _initialConnectedLossyScale = space != null ? space.lossyScale : Vector3.one;

            if (_initialized) return;
            _initialXDrive = _joint.xDrive;
            _initialYDrive = _joint.yDrive;
            _initialZDrive = _joint.zDrive;
            _initialAngularXDrive = _joint.angularXDrive;
            _initialAngularYZDrive = _joint.angularYZDrive;
            _initialSlerpDrive = _joint.slerpDrive;
        }

        private void CreateTarget()
        {
            var target = new GameObject($"{name}_Target").transform;
            target.SetParent(transform.parent, false);
            target.SetLocalPositionAndRotation(transform.localPosition, transform.localRotation);
            _targetRotation = target;
        }

        private void Map()
        {
            if (_targetRotation == null) return;

            if (_joint != null)
            {
                _rigidbody.MovePosition(GetTargetPosition());
                _rigidbody.MoveRotation(GetTargetRotation());
                return;
            }

            if (FollowsParent)
            {
                Pose pose = AttachedPose(true);
                _rigidbody.MovePosition(pose.position);
                _rigidbody.MoveRotation(pose.rotation);
            }
            else
            {
                transform.localRotation = AttachedLocalRotation;
                if (!_rotationOnly) transform.localPosition = _initialLocalPosition;
            }
        }

        private void HandleGravity() => _rigidbody.useGravity = !_cancelGravity;

        private void HandleJointTarget()
        {
            if (_joint == null || _targetRotation == null) return;

            Transform space = ConnectedSpace;
            Quaternion targetLocal = space != null ? Quaternion.Inverse(space.rotation) * _targetRotation.rotation : _targetRotation.rotation;
            if (_targetConnectedSpace != null)
            {
                Quaternion relative = Quaternion.Inverse(_targetConnectedSpace.rotation) * _targetRotation.rotation;
                targetLocal = _jointStartLocalRotation * Quaternion.Inverse(_targetRestRelative) * relative;
            }
            _joint.SetTargetRotationLocal(targetLocal, _jointStartLocalRotation);
        }

        public void SetSolverIterations(int iterations)
        {
            _solverIterationCount = iterations;
            if (_rigidbody != null) _rigidbody.solverIterations = Mathf.Max(1, iterations);
        }

        public void SetCenterOfMass(Transform center)
        {
            if (center == null) _rigidbody.ResetCenterOfMass();
            else _rigidbody.centerOfMass = transform.InverseTransformPoint(center.position);
        }

        public void SetMassMultiplier(float multiplier) => _rigidbody.mass = _initialMass * multiplier;

        public void SetDriveMultipliers(float positionSpring, float positionDamper, float angularSpring, float angularDamper)
        {
            _positionSpringMultiplier = positionSpring;
            _positionDamperMultiplier = positionDamper;
            _angularSpringMultiplier = angularSpring;
            _angularDamperMultiplier = angularDamper;
            ApplyDrives();
        }

        private void ApplyDrives()
        {
            if (_joint == null) return;
            _joint.xDrive = _initialXDrive.Scaled(_positionSpringMultiplier, _positionDamperMultiplier);
            _joint.yDrive = _initialYDrive.Scaled(_positionSpringMultiplier, _positionDamperMultiplier);
            _joint.zDrive = _initialZDrive.Scaled(_positionSpringMultiplier, _positionDamperMultiplier);
            _joint.angularXDrive = _initialAngularXDrive.Scaled(_angularSpringMultiplier, _angularDamperMultiplier);
            _joint.angularYZDrive = _initialAngularYZDrive.Scaled(_angularSpringMultiplier, _angularDamperMultiplier);
            _joint.slerpDrive = _initialSlerpDrive.Scaled(_angularSpringMultiplier, _angularDamperMultiplier);
        }

        public void MakeKinematic(bool kinematic)
        {
            if (!kinematic && _kinematic) return;
            _rigidbody.isKinematic = kinematic;
            if (!kinematic) ResetJoint();
        }

        /// <summary>Restores the default gravity cancellation after a grab changed it.</summary>
        public void ResetCancelGravity() => _cancelGravity = _initialCancelGravity;

        public Quaternion GetTargetRotation() => FollowsParent && _joint == null
            ? AttachedPose(false).rotation
            : _targetRotation != null ? _targetRotation.rotation : transform.rotation;

        /// <summary>World position at which the joint anchors would coincide.</summary>
        public Vector3 GetTargetPosition()
        {
            if (_joint == null)
                return FollowsParent ? AttachedPose(false).position
                    : _targetRotation != null && !_rotationOnly ? _targetRotation.position : transform.position;

            Vector3 connectedAnchorWorld = _joint.connectedBody != null
                ? _joint.connectedBody.transform.TransformPoint(_joint.connectedAnchor)
                : _joint.connectedAnchor;
            Vector3 anchorOffset = transform.TransformPoint(_joint.anchor) - transform.position;
            Quaternion correction = GetTargetRotation() * Quaternion.Inverse(transform.rotation);
            return connectedAnchorWorld - correction * anchorOffset;
        }

        public Vector3 GetOffsetFromTarget() => GetTargetPosition() - transform.position;
        public float GetDistanceFromTarget() => GetOffsetFromTarget().magnitude;
        public float GetAngleFromTarget() => Quaternion.Angle(transform.rotation, GetTargetRotation());

        public float GetAngleFromTargetOnYAxis()
        {
            Quaternion target = GetTargetRotation();
            Vector3 up = target * Vector3.up;
            return Vector3.SignedAngle(Vector3.ProjectOnPlane(target * Vector3.forward, up),
                Vector3.ProjectOnPlane(transform.forward, up), up);
        }

        public void MapToTarget()
        {
            Vector3 position = GetTargetPosition();
            Quaternion rotation = GetTargetRotation();
            transform.SetPositionAndRotation(position, rotation);
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;
        }

        public void InstantMapToTarget()
        {
            MapToTarget();
            ResetVelocity();
        }

        public void ResetVelocity()
        {
            if (_rigidbody.isKinematic) return;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        public void Reset()
        {
            if (_rigidbody == null) return;
            InstantMapToTarget();
        }

        public void ResetPosition()
        {
            transform.localPosition = _initialLocalPosition;
            transform.localRotation = _initialLocalRotation;
            ResetVelocity();
        }

        /// <summary>Re-applies anchors when the lossy scale of either side changed (hand scaling).</summary>
        public void AdjustJoint()
        {
            if (_joint == null) return;
            Transform space = ConnectedSpace;
            Vector3 connectedScale = space != null ? space.lossyScale : Vector3.one;
            if (transform.lossyScale != _initialLossyScale || connectedScale != _initialConnectedLossyScale)
            {
                _initialLossyScale = transform.lossyScale;
                _initialConnectedLossyScale = connectedScale;
                RefreshAnchors();
            }
        }

        public void RefreshAnchors()
        {
            if (_joint == null) return;
            _joint.anchor = _initialAnchor;
            if (!_updateAnchor) _joint.connectedAnchor = _initialConnectedAnchor;
        }

        public void DoUpdateAnchor()
        {
            if (_joint == null || _joint.connectedBody == null || transform.parent == null) return;

            Vector3 restAnchorLocal = _initialLocalPosition + _initialLocalRotation * Vector3.Scale(transform.localScale, _joint.anchor);
            Vector3 restAnchorWorld = transform.parent.TransformPoint(restAnchorLocal);
            _joint.connectedAnchor = _joint.connectedBody.transform.InverseTransformPoint(restAnchorWorld);
        }

        public static Vector3 InverseScale(Vector3 a, Vector3 b) => ConfigurableJointExtensions.InverseScale(a, b);
    }
}
