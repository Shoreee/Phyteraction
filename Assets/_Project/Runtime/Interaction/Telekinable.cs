using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>Source of telekinetic forces (implemented by the Telekinesis power).</summary>
    public interface ITelekinesisHandler
    {
        void ApplyForces(Telekinable telekinable);
    }

    /// <summary>Marks an object as movable by telekinesis; applies all active handlers' forces.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Telekinable : MonoBehaviour
    {
        [SerializeField] private Interactable _interactable;

        private readonly List<ITelekinesisHandler> _telekinesisHandlers = new();
        private Rigidbody _rb;
        private Holdable _holdable;

        public Rigidbody Rigidbody => _rb;
        public Holdable Holdable => _holdable;
        public Interactable Interactable => _interactable;
        public bool IsControlled => _telekinesisHandlers.Count > 0;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _holdable = GetComponent<Holdable>();
            if (_interactable == null) _interactable = GetComponent<Interactable>();
        }

        public void AddHandler(ITelekinesisHandler handler)
        {
            if (!_telekinesisHandlers.Contains(handler)) _telekinesisHandlers.Add(handler);
        }

        public void RemoveHandler(ITelekinesisHandler handler) => _telekinesisHandlers.Remove(handler);

        private void FixedUpdate()
        {
            for (int i = _telekinesisHandlers.Count - 1; i >= 0; i--) _telekinesisHandlers[i].ApplyForces(this);
        }
    }
}
