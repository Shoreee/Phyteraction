using PhysicsHands.Interaction;
using UnityEngine;

namespace PhysicsHands.App
{
    public sealed class PhysicsLabContent : MonoBehaviour, IWorkbenchContent
    {
        public Rigidbody[] bodies = System.Array.Empty<Rigidbody>();
        public LabPistol[] pistols = System.Array.Empty<LabPistol>();
        public LabProjectileSystem projectiles;
        Vector3[] positions;
        Quaternion[] rotations;
        public bool Available => true;
        public bool SupportsRoom => false;
        public bool SupportsPaper => false;
        public string Status => "PHYSICS LAB  ·  FRICTION / AUTHORED GRIP";
        void Awake()
        {
            positions = new Vector3[bodies.Length];
            rotations = new Quaternion[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                positions[i] = transform.InverseTransformPoint(bodies[i].position);
                rotations[i] = Quaternion.Inverse(transform.rotation) * bodies[i].rotation;
            }
        }
        public void SetContentActive(bool active) => gameObject.SetActive(active);
        public void ResetContent()
        {
            if (projectiles != null) projectiles.Clear();
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body.TryGetComponent(out Holdable holdable)) holdable.ForceRelease();
                body.position = transform.TransformPoint(positions[i]);
                body.rotation = transform.rotation * rotations[i];
                body.linearVelocity = body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
            foreach (LabPistol pistol in pistols) pistol.ResetState();
        }
        public void SetRoom(bool visible) { }
        public void Replay() { }
    }
}
