using UnityEngine;

namespace PhysicsHands.Dynamics
{
    [RequireComponent(typeof(Rigidbody))]
    public class NeverSleep : MonoBehaviour
    {
        private void Awake() => GetComponent<Rigidbody>().sleepThreshold = 0f;
    }
}
