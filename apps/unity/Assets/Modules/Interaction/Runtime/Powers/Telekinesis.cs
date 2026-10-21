using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>
    /// Distance manipulation. Aim with the palm (sphere cast from the start point), pinch to
    /// lock the aimed object at its current distance (it follows the hand), make a fist to pull
    /// it in until the hand grabber captures it, release the pinch to drop or throw.
    /// Active only when GameSettings.TelekinesisActive is on.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.Powers)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Powers", "PhysicsHands.Runtime", "Telekinesis")]
    public class Telekinesis : MonoBehaviour, ITelekinesisHandler
    {
        public Camera headCamera;
        [SerializeField]
        private float _minDistance = 0.1f;
        [SerializeField]
        private float _maxDistance = 3f;
        [SerializeField]
        private float _radius = 0.2f;
        [SerializeField]
        private Transform _target;
        [SerializeField]
        private BezierPointer _bezierPointer;
        [SerializeField]
        private Transform _startPoint;
        [SerializeField]
        private Transform _endPoint;
        [SerializeField]
        private Gradient _colorValid = new();
        [SerializeField]
        private AnimationCurve _curveValid = AnimationCurve.Constant(0f, 1f, 0.003f);
        [SerializeField]
        private Gradient _colorAiming = new();
        [SerializeField]
        private AnimationCurve _curveAiming = AnimationCurve.Constant(0f, 1f, 0.002f);
        [SerializeField]
        private Gradient _colorEnforcing = new();
        [SerializeField]
        private AnimationCurve _curveEnforcing = AnimationCurve.Constant(0f, 1f, 0.004f);
        [SerializeField]
        private LayerMask _layerMask = 1 << PhysicsLayers.Grabbable;
        [SerializeField]
        private PhysicsHand _hand;
        [SerializeField]
        private HandGrabber _handGrabber;
        [Header("Forces (reimplementation parameters)")]
        [SerializeField]
        private float _spring = 150f;
        [SerializeField]
        private float _damper = 18f;
        [SerializeField]
        private float _maxAcceleration = 60f;
        [SerializeField]
        private float _attractSpeed = 1.5f;
        [SerializeField]
        private float _captureDistance = 0.15f;
        [SerializeField]
        private float _handDistanceGain = 2.5f;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Telekinable _aimed;
        private Telekinable _telekinable;
        private bool _isLocking;
        private bool _isAttracting;
        private float _lockDistance;
        private float _lockHandDistance;
        private Quaternion _lockRotationOffset;
        public bool IsLocking => _isLocking;
        public Telekinable Current => _telekinable;
        private bool Enabled => _hand != null && (_hand.GameSettings == null || _hand.GameSettings.TelekinesisActive.Value);
        private void Awake()
        {
            if (_hand == null)
                _hand = GetComponentInParent<PhysicsHand>();
            if (_handGrabber == null && _hand != null)
                _handGrabber = _hand.GetComponentInChildren<HandGrabber>();
            if (_startPoint == null)
                _startPoint = transform;
        }

        private void OnDisable()
        {
            Unlock();
            SetAimed(null);
            if (_bezierPointer != null)
                _bezierPointer.Activate(false);
        }

        private void Update()
        {
            if (!Enabled || _handGrabber == null || _handGrabber.IsHolding || !_hand.GoodTracking)
            {
                Unlock();
                SetAimed(null);
                if (_bezierPointer != null)
                    _bezierPointer.Activate(false);
                return;
            }

            if (_isLocking)
                HandleEnforcer();
            else
                HandleAiming();
        }

        private float DistanceFromHand() => _telekinable != null ? Vector3.Distance(_telekinable.Rigidbody.worldCenterOfMass, _startPoint.position) : 0f;
        private float HandExtension()
        {
            Camera head = headCamera;
            return head != null ? Vector3.Distance(head.transform.position, _startPoint.position) : 0.4f;
        }

        private void HandleAiming()
        {
            Telekinable found = null;
            int count = Physics.SphereCastNonAlloc(_startPoint.position, _radius, _startPoint.forward, _hits, _maxDistance, _layerMask, QueryTriggerInteraction.Collide);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].distance < _minDistance)
                    continue;
                Telekinable t = _hits[i].collider.attachedRigidbody != null ? _hits[i].collider.attachedRigidbody.GetComponent<Telekinable>() : null;
                if (t == null || t.Holdable != null && t.Holdable.IsHeld)
                    continue;
                if (_hits[i].distance < best)
                {
                    best = _hits[i].distance;
                    found = t;
                }
            }

            SetAimed(found);
            DrawCurve(found != null ? _colorValid : _colorAiming, found != null ? _curveValid : _curveAiming, found);
            if (found != null && _hand.IsIndexPinching())
                Lock(found);
        }

        private void SetAimed(Telekinable telekinable)
        {
            if (telekinable == _aimed)
                return;
            if (_aimed != null && _aimed.Interactable != null)
                _aimed.Interactable.Unhighlight();
            _aimed = telekinable;
            if (_aimed != null && _aimed.Interactable != null)
                _aimed.Interactable.Highlight();
        }

        private void Lock(Telekinable telekinable)
        {
            _telekinable = telekinable;
            _isLocking = true;
            _isAttracting = false;
            _lockDistance = DistanceFromHand();
            _lockHandDistance = HandExtension();
            _lockRotationOffset = Quaternion.Inverse(_startPoint.rotation) * telekinable.Rigidbody.rotation;
            telekinable.AddHandler(this);
            telekinable.Rigidbody.useGravity = false;
            _handGrabber.LockWithTelekinesis(telekinable.Holdable, true);
        }

        private void Unlock()
        {
            if (_telekinable == null)
                return;
            _telekinable.RemoveHandler(this);
            if (!_telekinable.IsControlled)
                _telekinable.Rigidbody.useGravity = true;
            if (_handGrabber != null)
                _handGrabber.LockWithTelekinesis(_telekinable.Holdable, false);
            _telekinable = null;
            _isLocking = false;
            _isAttracting = false;
        }

        private void HandleEnforcer()
        {
            if (_telekinable == null || !_telekinable.isActiveAndEnabled || !_hand.IsIndexPinching())
            {
                Unlock();
                return;
            }

            _isAttracting = _hand.IsGrasping(3, GraspLevel.Moderate);
            if (_isAttracting)
            {
                _lockDistance = Mathf.MoveTowards(_lockDistance, 0f, _attractSpeed * Time.deltaTime);
                if (DistanceFromHand() < _captureDistance && _telekinable.Holdable != null)
                {
                    Holdable holdable = _telekinable.Holdable;
                    Unlock();
                    _handGrabber.CaptureWithTelekinesis(holdable);
                    return;
                }
            }

            DrawCurve(_colorEnforcing, _curveEnforcing, _telekinable);
        }

        /// <summary>Called by the Telekinable in FixedUpdate.</summary>
        public void ApplyForces(Telekinable telekinable)
        {
            if (telekinable != _telekinable)
                return;
            Rigidbody rb = telekinable.Rigidbody;
            float push = (HandExtension() - _lockHandDistance) * _handDistanceGain;
            float distance = Mathf.Clamp(_lockDistance + (_isAttracting ? 0f : push), _isAttracting ? 0f : _minDistance, _maxDistance);
            Vector3 goal = _startPoint.position + _startPoint.forward * distance;
            if (_target != null)
                _target.position = goal;
            Vector3 acceleration = (goal - rb.worldCenterOfMass) * _spring - rb.linearVelocity * _damper;
            rb.AddForce(Vector3.ClampMagnitude(acceleration, _maxAcceleration), ForceMode.Acceleration);
            Quaternion goalRotation = _startPoint.rotation * _lockRotationOffset;
            (goalRotation * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            if (float.IsFinite(axis.x))
                rb.AddTorque(axis * (angle * Mathf.Deg2Rad * _spring * 0.1f) - rb.angularVelocity * (_damper * 0.3f), ForceMode.Acceleration);
        }

        private void DrawCurve(Gradient color, AnimationCurve width, Telekinable target)
        {
            if (_bezierPointer == null)
                return;
            if (target == null)
            {
                _bezierPointer.Activate(false);
                return;
            }

            if (_endPoint != null)
                _endPoint.position = target.Rigidbody.worldCenterOfMass;
            _bezierPointer.SetStyle(color, width);
            _bezierPointer.Activate(true);
        }
    }
}
