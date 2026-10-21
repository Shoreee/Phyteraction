using UnityEngine;

namespace PhysicsHands.Dynamics
{
    /// <summary>
    /// Drives a rigidbody towards a target transform through its ConfigurableJoint.
    /// Used by the physical wrist (target = tracked hand) and by every finger bone
    /// (target = matching bone of the visual HandPoser).
    ///
    /// Tracked hand phalanges, including distal links, retain dynamic bodies and
    /// anatomical joints. World-space backward-PD muscles pursue each tracked bone;
    /// PhysX contact constraints determine the reachable pose.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.PhysicsJointDriver)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Dynamics", "PhysicsHands.Runtime", "JointEnforcer")]
    public class PhysicsJointDriver : MonoBehaviour
    {
        [SerializeField]
        private TrackedMuscleSettings _muscleSettings = new();
        [SerializeField]
        private JointDriverSettings _settings = new();
        [SerializeField]
        private JointDriverBindings _bindings = new();
        private Rigidbody _rigidbody;
        private readonly JointRestState _rest = new();
        private bool _initialized;
        private float _positionSpringMultiplier = 1f;
        private float _positionDamperMultiplier = 1f;
        private float _angularSpringMultiplier = 1f;
        private float _angularDamperMultiplier = 1f;
        private Transform _targetConnectedSpace;
        private IJointDriveContext _hand;
        public void Bind(IJointDriveContext context) => _hand = context;
        private Quaternion _targetRestRelative;
        public Transform TrackingTarget => _bindings.TrackingTarget;
        private Transform _trackedBone;
        private readonly TrackedBoneMuscle _muscle = new();
        private readonly TrackedJointPolicy _trackingPolicy = new();
        private System.Action _applyDrives;
        public int SolverIterationCount => _settings.SolverIterationCount;
        public Rigidbody Rigidbody => _rigidbody;
        public ConfigurableJoint Joint => _bindings.Joint;
        public bool UpdateAnchor => _settings.UpdateAnchor;
        public Transform TargetRotation => _bindings.TargetRotation;
        public bool IsKinematic => _rigidbody != null && _rigidbody.isKinematic;
        public bool Enforce
        {
            get => _settings.Enforce;
            set => _settings.Enforce = value;
        }

        public bool CancelGravity
        {
            get => _settings.CancelGravity;
            set => _settings.CancelGravity = value;
        }

        public float PositionSpringMultiplier
        {
            get => _positionSpringMultiplier;
            set
            {
                _positionSpringMultiplier = value;
                ApplyDrives();
            }
        }

        public float PositionDamperMultiplier
        {
            get => _positionDamperMultiplier;
            set
            {
                _positionDamperMultiplier = value;
                ApplyDrives();
            }
        }

        public float AngularSpringMultiplier
        {
            get => _angularSpringMultiplier;
            set
            {
                _angularSpringMultiplier = value;
                ApplyDrives();
            }
        }

        public float AngularDamperMultiplier
        {
            get => _angularDamperMultiplier;
            set
            {
                _angularDamperMultiplier = value;
                ApplyDrives();
            }
        }

        public Vector3 TargetVelocity => _settings.UseTargetVelocity && _bindings.VelocityEstimator != null ? _bindings.VelocityEstimator.GetVelocityEstimate() : Vector3.zero;
        private Transform ConnectedSpace => _bindings.Joint != null && _bindings.Joint.connectedBody != null ? _bindings.Joint.connectedBody.transform : transform.parent;
        private void Awake()
        {
            _applyDrives = ApplyDrives;
            _rigidbody = GetComponent<Rigidbody>();
            if (_bindings.Joint == null)
                _bindings.Joint = GetComponent<ConfigurableJoint>();
            _rest.Mass = _rigidbody.mass;
            _rest.Interpolation = _rigidbody.interpolation;
            _rest.LocalPosition = transform.localPosition;
            _rest.LocalRotation = transform.localRotation;
            _rest.LossyScale = transform.lossyScale;
            _rest.CancelGravity = _settings.CancelGravity;
            if (_bindings.TargetRotation == null && _settings.CreateTarget)
                CreateTarget();
            if (_bindings.TargetRotation != null)
            {
                _rest.TargetLocalRotation = _bindings.TargetRotation.localRotation;
                _rest.TargetLocalPosition = _bindings.TargetRotation.localPosition;
            }

            CaptureJointState();
            if (_bindings.Joint != null)
            {
                _rest.Projection = _bindings.Joint.projectionMode;
                _rest.Preprocessing = _bindings.Joint.enablePreprocessing;
            }

            // Reference rotations must be relative to the reference parent, never
            // to an already deflected physical parent (which creates feedback).
            if (_bindings.Joint != null && _bindings.Joint.connectedBody != null && _bindings.TargetRotation != null)
            {
                var parentEnforcer = _bindings.Joint.connectedBody.GetComponent<PhysicsJointDriver>();
                if (_bindings.TargetRotation.IsChildOf(_bindings.Joint.connectedBody.transform))
                    _targetConnectedSpace = _bindings.Joint.connectedBody.transform;
                else if (parentEnforcer != null)
                    _targetConnectedSpace = parentEnforcer.TargetRotation;
                if (_targetConnectedSpace != null)
                    _targetRestRelative = Quaternion.Inverse(_targetConnectedSpace.rotation) * _bindings.TargetRotation.rotation;
            }
        }

        private void Start()
        {
            // Distal collision bodies must participate in the constraint solver too.
            // Kinematic tips can otherwise drag through a wall while the palm stops.
            if (_hand != null && FollowsParent)
            {
                _settings.Kinematic = false;
                _settings.RemoveJoint = false;
                _settings.RotationOnly = false;
            }

            InitializeJoint();
            if (_bindings.CustomMassCenter != null)
                SetCenterOfMass(_bindings.CustomMassCenter);
            if (_settings.RemoveJoint && _bindings.Joint != null)
            {
                Destroy(_bindings.Joint);
                _bindings.Joint = null;
            }

            if (_settings.Kinematic)
                MakeKinematic(true);
            // This body's render pose already inherits the interpolated parent.
            // A second world-space interpolation would pull the tip off the finger.
            if (FollowsParent)
                _rigidbody.interpolation = RigidbodyInterpolation.None;
        }

        private void OnEnable()
        {
            if (_initialized && _settings.ReinitializeOnEnable)
                ResetJoint();
        }

        private void OnDisable()
        {
            if (_initialized && _settings.ResetOnDisable)
                ResetPosition();
        }

        private bool FollowsParent => _settings.Kinematic && _settings.RemoveJoint && _settings.RotationOnly;
        private Vector3 AttachedLocalPosition => JointTargetMapping.LocalPosition(_bindings.TargetRotation, transform, _rest);
        // Dynamic parents are interpolated after Update. Restore the complete local
        // attachment before RigMapper reads the skeleton in LateUpdate. Otherwise a
        // jointless tip retains an old world position as its parent moves away.
        private void LateUpdate()
        {
            if (_initialized && IsKinematic && _bindings.Joint == null && FollowsParent)
                transform.SetLocalPositionAndRotation(AttachedLocalPosition, AttachedLocalRotation);
        }

        private Quaternion AttachedLocalRotation => JointTargetMapping.LocalRotation(_bindings.TargetRotation, _rest);
        private Pose AttachedPose(bool physicsPose) => JointTargetMapping.AttachedPose(transform, _bindings.TargetRotation, _rest, physicsPose);
        private void FixedUpdate()
        {
            if (!_initialized)
                return;
            if (_settings.UpdateAnchor)
                DoUpdateAnchor();
            AdjustJoint();
            if (IsKinematic)
            {
                Map();
                return;
            }

            HandleGravity();
            bool direct = _hand != null && _hand.DirectTracking && _hand.GoodTracking && _settings.Enforce;
            _trackingPolicy.Update(direct, _rigidbody, _bindings.Joint, _rest, _settings.SolverIterationCount, _applyDrives);
            if (_settings.Enforce)
            {
                HandleJointTarget();
                TrackBone();
            }
        }

        private void TrackBone()
        {
            if (_hand == null || !_hand.DirectTracking || !_hand.GoodTracking || _bindings.Joint == null)
            {
                _muscle.Reset();
                return;
            }

            _trackedBone ??= _bindings.TrackingTarget;
            Vector3 position = _trackedBone != null ? _trackedBone.position : GetTargetPosition();
            Quaternion rotation = _trackedBone != null ? _trackedBone.rotation : GetTargetRotation();
            _muscle.Step(_rigidbody, position, rotation, _trackedBone != null ? Mathf.Max(0, _hand.FingerSpringMultiplier) : 1f, _muscleSettings.TrackingFrequency, _muscleSettings.TrackingAngularFrequency, _hand.PhysicalContactActive ? _muscleSettings.MaxTrackingAcceleration : _muscleSettings.MaxFreeTrackingAcceleration, _muscleSettings.MaxTrackingAngularAcceleration);
        }

        public void InitializeJoint()
        {
            _rigidbody.solverIterations = Mathf.Max(1, _settings.SolverIterationCount);
            _rigidbody.solverVelocityIterations = Mathf.Max(4, _settings.SolverIterationCount / 2);
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
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
            if (_bindings.Joint == null)
                return;
            // Preserve the authored rest frame when recovering from a deflected pose.
            RefreshAnchors();
            ApplyDrives();
        }

        private void CaptureJointState() => _rest.Capture(_bindings.Joint, ConnectedSpace, transform, _initialized);
        private void CreateTarget()
        {
            var target = new GameObject($"{name}_Target").transform;
            target.SetParent(transform.parent, false);
            target.SetLocalPositionAndRotation(transform.localPosition, transform.localRotation);
            _bindings.TargetRotation = target;
        }

        private void Map()
        {
            if (_bindings.TargetRotation == null)
                return;
            if (_bindings.Joint != null)
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
                if (!_settings.RotationOnly)
                    transform.localPosition = _rest.LocalPosition;
            }
        }

        private void HandleGravity() => _rigidbody.useGravity = !_settings.CancelGravity;
        private void HandleJointTarget()
        {
            if (_bindings.Joint == null || _bindings.TargetRotation == null)
                return;
            Transform space = ConnectedSpace;
            Quaternion targetLocal = space != null ? Quaternion.Inverse(space.rotation) * _bindings.TargetRotation.rotation : _bindings.TargetRotation.rotation;
            if (_targetConnectedSpace != null)
            {
                Quaternion relative = Quaternion.Inverse(_targetConnectedSpace.rotation) * _bindings.TargetRotation.rotation;
                targetLocal = _rest.JointStartLocalRotation * Quaternion.Inverse(_targetRestRelative) * relative;
            }

            _bindings.Joint.SetTargetRotationLocal(targetLocal, _rest.JointStartLocalRotation);
        }

        public void SetSolverIterations(int iterations)
        {
            _settings.SolverIterationCount = iterations;
            if (_rigidbody != null)
                _rigidbody.solverIterations = Mathf.Max(1, iterations);
        }

        public void SetCenterOfMass(Transform center)
        {
            if (center == null)
                _rigidbody.ResetCenterOfMass();
            else
                _rigidbody.centerOfMass = transform.InverseTransformPoint(center.position);
        }

        public void SetMassMultiplier(float multiplier) => _rigidbody.mass = _rest.Mass * multiplier;
        public void SetDriveMultipliers(float positionSpring, float positionDamper, float angularSpring, float angularDamper)
        {
            _positionSpringMultiplier = positionSpring;
            _positionDamperMultiplier = positionDamper;
            _angularSpringMultiplier = angularSpring;
            _angularDamperMultiplier = angularDamper;
            ApplyDrives();
        }

        private void ApplyDrives() => JointDrivePolicy.Apply(_bindings.Joint, _rest, _trackingPolicy.Active, _positionSpringMultiplier, _positionDamperMultiplier, _angularSpringMultiplier, _angularDamperMultiplier);
        public void MakeKinematic(bool kinematic)
        {
            if (!kinematic && _settings.Kinematic)
                return;
            _rigidbody.isKinematic = kinematic;
            if (!kinematic)
                ResetJoint();
        }

        /// <summary>Restores the default gravity cancellation after a grab changed it.</summary>
        public void ResetCancelGravity() => _settings.CancelGravity = _rest.CancelGravity;
        public Quaternion GetTargetRotation() => FollowsParent && _bindings.Joint == null ? AttachedPose(false).rotation : _bindings.TargetRotation != null ? _bindings.TargetRotation.rotation : transform.rotation;
        /// <summary>World position at which the joint anchors would coincide.</summary>
        public Vector3 GetTargetPosition()
        {
            if (_bindings.Joint == null)
                return FollowsParent ? AttachedPose(false).position : _bindings.TargetRotation != null && !_settings.RotationOnly ? _bindings.TargetRotation.position : transform.position;
            Vector3 connectedAnchorWorld = _bindings.Joint.connectedBody != null ? _bindings.Joint.connectedBody.transform.TransformPoint(_bindings.Joint.connectedAnchor) : _bindings.Joint.connectedAnchor;
            Vector3 anchorOffset = transform.TransformPoint(_bindings.Joint.anchor) - transform.position;
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
            return Vector3.SignedAngle(Vector3.ProjectOnPlane(target * Vector3.forward, up), Vector3.ProjectOnPlane(transform.forward, up), up);
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
            _muscle.Reset();
            if (_rigidbody.isKinematic)
                return;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        private void OnCollisionEnter(Collision collision) => ReportContact(collision);
        private void OnCollisionStay(Collision collision) => ReportContact(collision);
        private void ReportContact(Collision collision)
        {
            if (_hand == null || _hand.Owns(collision.collider))
                return;
            _hand.NotifyPhysicalContact(_rigidbody);
        }

        public void Reset()
        {
            if (_rigidbody == null)
                return;
            InstantMapToTarget();
        }

        public void ResetPosition()
        {
            transform.localPosition = _rest.LocalPosition;
            transform.localRotation = _rest.LocalRotation;
            ResetVelocity();
        }

        /// <summary>Re-applies anchors when the lossy scale of either side changed (hand scaling).</summary>
        public void AdjustJoint()
        {
            if (_bindings.Joint == null)
                return;
            Transform space = ConnectedSpace;
            Vector3 connectedScale = space != null ? space.lossyScale : Vector3.one;
            if (transform.lossyScale != _rest.LossyScale || connectedScale != _rest.ConnectedLossyScale)
            {
                _rest.LossyScale = transform.lossyScale;
                _rest.ConnectedLossyScale = connectedScale;
                RefreshAnchors();
            }
        }

        public void RefreshAnchors()
        {
            if (_bindings.Joint == null)
                return;
            _bindings.Joint.anchor = _rest.Anchor;
            if (!_settings.UpdateAnchor)
                _bindings.Joint.connectedAnchor = _rest.ConnectedAnchor;
        }

        public void DoUpdateAnchor()
        {
            if (_bindings.Joint == null || _bindings.Joint.connectedBody == null || transform.parent == null)
                return;
            Vector3 restAnchorLocal = AttachedLocalPosition + _rest.LocalRotation * Vector3.Scale(transform.localScale, _bindings.Joint.anchor);
            Vector3 restAnchorWorld = transform.parent.TransformPoint(restAnchorLocal);
            _bindings.Joint.connectedAnchor = _bindings.Joint.connectedBody.transform.InverseTransformPoint(restAnchorWorld);
        }

        public static Vector3 InverseScale(Vector3 a, Vector3 b) => ConfigurableJointExtensions.InverseScale(a, b);
    }
}
