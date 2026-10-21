using System.Collections.Generic;
using PhysicsHands.Dynamics;
using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>
    /// Palm force field. With an open palm facing away from the head, objects in a sphere
    /// cast along the palm are attracted to a field point in front of the hand and held
    /// there; closing the hand shoots the captured objects along the palm.
    /// Active only when GameSettings.ForceFieldActive is on.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.Powers)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Powers", "PhysicsHands.Runtime", "ForceFieldGenerator")]
    public class ForceFieldGenerator : MonoBehaviour
    {
        [SerializeField]
        private PhysicsHand _hand;
        [SerializeField]
        private bool _cancelGravity = true;
        [SerializeField]
        private bool _enforceToCenter = true;
        [SerializeField]
        private float _enforceToCenterMultiplier = 1f;
        [SerializeField]
        private bool _canAttractMultiple = true;
        [SerializeField]
        private bool _distanceAttraction = true;
        [SerializeField]
        private LayerMask _objectsLayers = 513;
        [SerializeField]
        private float _mainIntensityMultiplier = 1f;
        [Tooltip("Acceleration multiplier by distance (m) from the field.")]
        [SerializeField]
        private AnimationCurve _attractionForceMultiplier = new(new Keyframe(0.151f, 4.929f), new Keyframe(19.84f, 1.178f));
        [Tooltip("Acceleration by normalized distance (0 = center, 1 = capture radius).")]
        [SerializeField]
        private AnimationCurve _capturedForceMultiplier = new(new Keyframe(0f, 0f), new Keyframe(0.16f, 7.77f), new Keyframe(0.28f, 22.19f), new Keyframe(0.63f, 25.79f), new Keyframe(1f, 0f));
        [SerializeField]
        private float _shootingForceMultiplier = 10f;
        [SerializeField]
        private float _maxRigidBodyVelocity = 10f;
        [SerializeField]
        private bool _useVelocityEstimatorToShoot = true;
        [SerializeField]
        private float _velocityFactor = 0.3f;
        [SerializeField]
        private VelocityEstimator _fieldVelocityEstimator;
        [SerializeField]
        private float _sphereCastAttractRadius = 0.25f;
        [SerializeField]
        private float _sphereCastCaptureRadius = 0.3f;
        [SerializeField]
        private ParticleSystem _emitter;
        [SerializeField]
        private Transform _emitterTransform;
        [SerializeField]
        private Transform _forceField;
        [SerializeField]
        private float _defaultFieldDistance = 0.4f;
        [SerializeField]
        private float _maxAttractionDistance = 5f;
        [SerializeField]
        private AudioSource _audioSource;
        [SerializeField]
        private AudioClip _impulseClip;
        [SerializeField]
        private List<Rigidbody> _rigidbodiesToIgnore = new();
        [SerializeField]
        private bool _activeOnStart;
        [SerializeField]
        private bool _repulsive;
        private readonly RaycastHit[] _attractedHits = new RaycastHit[32];
        private readonly Collider[] _capturedHits = new Collider[32];
        private readonly HashSet<Rigidbody> _capturedRigidbodies = new();
        private readonly HashSet<Rigidbody> _attractedRigidbodies = new();
        private readonly HashSet<Rigidbody> _previouslyCaptured = new();
        private bool _generatorRunning;
        private bool _active;
        private float _fieldDistance;
        public bool IsRunning => _generatorRunning;
        private Transform Emitter => _emitterTransform != null ? _emitterTransform : transform;
        private void Awake()
        {
            if (_hand == null)
                _hand = GetComponentInParent<PhysicsHand>();
            _fieldDistance = _defaultFieldDistance;
            _active = _activeOnStart;
            if (_hand != null && _hand.DetachableHand != null)
                AddRigidbodyToIgnore(_hand.DetachableHand.Rb);
        }

        private void OnDisable()
        {
            StopAttraction();
            foreach (Rigidbody body in _previouslyCaptured)
                if (body != null) body.useGravity = true;
            _previouslyCaptured.Clear();
            _attractedRigidbodies.Clear();
            System.Array.Clear(_capturedHits, 0, _capturedHits.Length);
            System.Array.Clear(_attractedHits, 0, _attractedHits.Length);
        }

        public void Activate(bool activate) => _active = activate;
        public void AddRigidbodyToIgnore(Rigidbody rb) => _rigidbodiesToIgnore.Add(rb);
        public void ResetForceFieldDistance() => _fieldDistance = _defaultFieldDistance;
        private bool GestureWantsField() => _hand != null && _hand.GoodTracking && !_hand.IsFacingHead() && _hand.GetGraspLevel() == GraspLevel.None;
        protected void FixedUpdate()
        {
            bool enabled = _active || (_hand != null && _hand.GameSettings != null && _hand.GameSettings.ForceFieldActive.Value);
            if (!enabled)
            {
                if (_generatorRunning)
                    StopAttraction();
                return;
            }

            if (GestureWantsField())
            {
                if (!_generatorRunning)
                    StartAttraction();
                HandleField();
            }
            else if (_generatorRunning)
            {
                if (_hand != null && _hand.GetGraspLevel() >= GraspLevel.Moderate)
                    HandleRelease(true);
                StopAttraction();
            }
        }

        public void StartAttraction()
        {
            _generatorRunning = true;
            if (_emitter != null)
                _emitter.Play();
        }

        public void StopAttraction()
        {
            _generatorRunning = false;
            HandleRelease(false);
            if (_emitter != null)
                _emitter.Stop();
        }

        private Vector3 FieldCenter => Emitter.position + Emitter.forward * _fieldDistance;
        private void HandleField()
        {
            if (_forceField != null)
                _forceField.position = FieldCenter;
            _previouslyCaptured.Clear();
            _previouslyCaptured.UnionWith(_capturedRigidbodies);
            _capturedRigidbodies.Clear();
            _attractedRigidbodies.Clear();
            int captured = Physics.OverlapSphereNonAlloc(FieldCenter, _sphereCastCaptureRadius, _capturedHits, _objectsLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < captured; i++)
            {
                Rigidbody rb = _capturedHits[i].attachedRigidbody;
                if (!IsValid(rb) || _capturedRigidbodies.Contains(rb))
                    continue;
                if (!_canAttractMultiple && _capturedRigidbodies.Count > 0)
                    break;
                _capturedRigidbodies.Add(rb);
                HandleCapture(rb);
            }

            if (!_distanceAttraction || (!_canAttractMultiple && _capturedRigidbodies.Count > 0))
                return;
            int attracted = Physics.SphereCastNonAlloc(Emitter.position, _sphereCastAttractRadius, Emitter.forward, _attractedHits, _maxAttractionDistance, _objectsLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < attracted; i++)
            {
                Rigidbody rb = _attractedHits[i].rigidbody;
                if (!IsValid(rb) || _capturedRigidbodies.Contains(rb) || !_attractedRigidbodies.Add(rb))
                    continue;
                HandleAttraction(rb);
            }

            foreach (Rigidbody rb in _previouslyCaptured)
                if (rb != null && !_capturedRigidbodies.Contains(rb))
                    rb.useGravity = true;
        }

        private bool IsValid(Rigidbody rb) => rb != null && !rb.isKinematic && !_rigidbodiesToIgnore.Contains(rb);
        private void HandleCapture(Rigidbody rb)
        {
            if (_cancelGravity)
                rb.useGravity = false;
            Vector3 toCenter = FieldCenter - rb.worldCenterOfMass;
            float normalized = Mathf.Clamp01(toCenter.magnitude / _sphereCastCaptureRadius);
            float intensity = _capturedForceMultiplier.Evaluate(normalized) * _mainIntensityMultiplier * (_repulsive ? -1f : 1f);
            Vector3 reference = Vector3.zero;
            if (_fieldVelocityEstimator != null)
                reference = _fieldVelocityEstimator.GetVelocityEstimate();
            Vector3 relative = rb.linearVelocity - reference;
            Vector3 acceleration = toCenter.normalized * intensity;
            if (_enforceToCenter)
                acceleration -= relative * (_enforceToCenterMultiplier * 4f);
            rb.AddForce(acceleration, ForceMode.Acceleration);
            rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, _maxRigidBodyVelocity);
        }

        private void HandleAttraction(Rigidbody rb)
        {
            Vector3 toCenter = FieldCenter - rb.worldCenterOfMass;
            float intensity = _attractionForceMultiplier.Evaluate(toCenter.magnitude) * _mainIntensityMultiplier * (_repulsive ? -1f : 1f);
            rb.AddForce(toCenter.normalized * intensity, ForceMode.Acceleration);
            rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, _maxRigidBodyVelocity);
        }

        /// <summary>Releases captured bodies; when shooting, launches them along the palm.</summary>
        private void HandleRelease(bool shoot)
        {
            Vector3 handVelocity = _useVelocityEstimatorToShoot && _fieldVelocityEstimator != null ? _fieldVelocityEstimator.GetVelocityEstimate() : Vector3.zero;
            foreach (Rigidbody rb in _capturedRigidbodies)
            {
                if (rb == null)
                    continue;
                rb.useGravity = true;
                if (!shoot)
                    continue;
                Vector3 velocity = Emitter.forward * _shootingForceMultiplier + handVelocity * (_velocityFactor * _shootingForceMultiplier);
                rb.linearVelocity = Vector3.ClampMagnitude(velocity, _maxRigidBodyVelocity);
            }

            if (shoot && _capturedRigidbodies.Count > 0 && _audioSource != null && _impulseClip != null)
                _audioSource.PlayOneShot(_impulseClip);
            _capturedRigidbodies.Clear();
            ResetForceFieldDistance();
        }
    }
}
