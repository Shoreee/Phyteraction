using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>Remembers an object's initial placement so it can be reset/respawned.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Interaction", "PhysicsHands.Runtime", "MovableObject")]
    public class MovableObject : MonoBehaviour
    {
        [SerializeField]
        private List<MovableObject> _childrenToReset = new();
        [SerializeField]
        private bool _unparentOnAwake;
        [SerializeField]
        private Transform _newParentOnAwake;
        [SerializeField]
        private Transform _customRespawnPosition;
        private Rigidbody _rb;
        private Vector3 _initialLocalPosition;
        private Quaternion _initialLocalRotation;
        private Vector3 _initialPosition;
        private Vector3 _initialScale;
        private Transform _initialParent;
        private bool _initialKinematic;
        private bool _initialized;
        public event Action<MovableObject> OnReset;
        public Rigidbody Rigidbody => _rb;
        private void Awake()
        {
            if (_unparentOnAwake)
                transform.SetParent(_newParentOnAwake, true);
            Initialize();
        }

        public void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;
            _rb = GetComponent<Rigidbody>();
            ResetInitialValues();
        }

        public void SetRigidbody(Rigidbody rb) => _rb = rb;
        public void ResetInitialValues()
        {
            _initialParent = transform.parent;
            _initialLocalPosition = transform.localPosition;
            _initialLocalRotation = transform.localRotation;
            _initialPosition = transform.position;
            _initialScale = transform.localScale;
            _initialKinematic = _rb != null && _rb.isKinematic;
        }

        public void DefineRespawnPosition(Transform spawnTransform) => _customRespawnPosition = spawnTransform;
        public void RemoveChild(MovableObject child) => _childrenToReset.Remove(child);
        public void SetInitialParent(Transform newParent) => _initialParent = newParent;
        public float GetDistanceFromInitialGlobalPosition() => Vector3.Distance(transform.position, _initialPosition);
        public float GetDistanceFromInitialLocalPosition() => Vector3.Distance(transform.localPosition, _initialLocalPosition);
        public void ResetObject(bool throwEvent, bool resetPosition)
        {
            if (resetPosition)
            {
                if (transform.parent != _initialParent)
                    transform.SetParent(_initialParent, true);
                if (_customRespawnPosition != null)
                    transform.SetPositionAndRotation(_customRespawnPosition.position, _customRespawnPosition.rotation);
                else
                    transform.SetLocalPositionAndRotation(_initialLocalPosition, _initialLocalRotation);
                if (_rb != null)
                {
                    _rb.position = transform.position;
                    _rb.rotation = transform.rotation;
                    if (!_rb.isKinematic)
                    {
                        _rb.linearVelocity = Vector3.zero;
                        _rb.angularVelocity = Vector3.zero;
                    }

                    _rb.isKinematic = _initialKinematic;
                }
            }

            foreach (MovableObject child in _childrenToReset)
                if (child != null)
                    child.ResetObject(false, resetPosition);
            if (throwEvent)
                OnReset?.Invoke(this);
        }

        public void MakeKinematic(bool kinematic)
        {
            if (_rb != null)
                _rb.isKinematic = kinematic;
        }

        public void ResetScale() => transform.localScale = _initialScale;
    }
}
