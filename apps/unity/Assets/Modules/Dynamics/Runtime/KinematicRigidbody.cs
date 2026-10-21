using UnityEngine;

namespace PhysicsHands.Dynamics
{
    /// <summary>
    /// Kinematic body that follows a transform (the tracked hand target). The physical
    /// wrist is jointed to this body, so PhysX sees its velocity and damps relative to it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Dynamics", "PhysicsHands.Runtime", "KinematicRigidbody")]
    public class KinematicRigidbody : MonoBehaviour
    {
        [SerializeField]
        private Transform _mapToTransform;
        [SerializeField]
        private bool _mapRotation;
        private Rigidbody _rb;
        private Quaternion _initialLocalRotation;
        private Vector3 _initialLocalPosition;
        public Transform MapToTransform
        {
            get => _mapToTransform;
            set => _mapToTransform = value;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _initialLocalRotation = transform.localRotation;
            _initialLocalPosition = transform.localPosition;
        }

        private void FixedUpdate()
        {
            if (_mapToTransform == null)
                return;
            _rb.MovePosition(_mapToTransform.position);
            if (_mapRotation)
                _rb.MoveRotation(_mapToTransform.rotation);
        }

        public void ResetPose()
        {
            transform.localPosition = _initialLocalPosition;
            transform.localRotation = _initialLocalRotation;
        }
    }
}
