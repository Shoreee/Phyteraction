using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using PhysicsHands.Settings;
using UnityEngine;

namespace PhysicsHands.Laboratory
{
    [RequireComponent(typeof(Rigidbody))]
    public class LaboratoryProp : MonoBehaviour
    {
        private Rigidbody _body;
        private Holdable _holdable;
        private GameSettings _settings;
        private void Start()
        {
            _body = GetComponent<Rigidbody>();
            _holdable = GetComponent<Holdable>();
            _settings = HoloRig.Instance != null ? HoloRig.Instance.GameSettings : null;
        }
        private void FixedUpdate()
        {
            if (_settings != null && !_body.isKinematic && (_holdable == null || !_holdable.IsHeld))
                _body.useGravity = _settings.GravityActive.Value;
        }
    }
}
