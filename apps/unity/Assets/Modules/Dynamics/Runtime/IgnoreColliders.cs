using UnityEngine;

namespace PhysicsHands.Dynamics
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Dynamics", "PhysicsHands.Runtime", "IgnoreColliders")]
    public class IgnoreColliders : MonoBehaviour
    {
        [SerializeField]
        private Collider[] _colliders;
        [SerializeField]
        private Collider[] _collidersToIgnore;
        private void Awake()
        {
            if (_colliders == null || _collidersToIgnore == null)
                return;
            foreach (Collider other in _collidersToIgnore)
                IgnoreThisCollider(other);
        }

        public void IgnoreThisCollider(Collider other)
        {
            if (other == null || _colliders == null)
                return;
            foreach (Collider own in _colliders)
                if (own != null && own != other)
                    Physics.IgnoreCollision(own, other, true);
        }
    }
}
