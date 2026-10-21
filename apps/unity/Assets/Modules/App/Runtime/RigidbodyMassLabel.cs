using UnityEngine;

namespace PhysicsHands.App
{
    public sealed class RigidbodyMassLabel : MonoBehaviour
    {
        public Rigidbody body;
        public TextMesh label;
        public string interaction;
        float previousMass = -1;
        void Update()
        {
            if (body == null || label == null || Mathf.Approximately(body.mass, previousMass)) return;
            previousMass = body.mass;
            label.text = body.mass < 1 ? $"{body.mass * 1000:0} g\n{interaction}" : $"{body.mass:0.#} kg\n{interaction}";
        }
    }
}
