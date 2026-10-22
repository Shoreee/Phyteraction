using PhysicsHands.Hands;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.App
{
    /// <summary>Bounded toy rounds: swept ballistic travel, impact impulse and body-local embedded poses.</summary>
    public sealed class LabProjectileSystem : MonoBehaviour
    {
        public Material roundMaterial, trailMaterial;
        public Mesh roundMesh;
        [Range(8, 128)] public int capacity = 48;
        public float radius = .006f, length = .03f, flightLifetime = 4, embeddedLifetime = 30;
        sealed class Round
        {
            internal GameObject visual;
            internal TrailRenderer trail;
            internal LabPistol owner;
            internal Vector3 velocity, localPoint;
            internal Quaternion localRotation;
            internal Transform attachment;
            internal float mass, age;
            internal bool active, embedded;
        }
        Round[] rounds;
        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Collider[] overlaps = new Collider[64];
        int cursor;
        public int ActiveCount { get; private set; }
        public int EmbeddedCount { get; private set; }
        public int Impacts { get; private set; }
        public float LastImpactImpulse { get; private set; }
        void Awake()
        {
            if (roundMaterial == null || trailMaterial == null || roundMesh == null)
                throw new System.InvalidOperationException($"{name}: projectile visual assets are missing.");
            rounds = new Round[capacity];
            for (int i = 0; i < rounds.Length; i++)
            {
                var go = new GameObject("Lab round", typeof(MeshFilter), typeof(MeshRenderer), typeof(TrailRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = roundMesh;
                var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = roundMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                var trail = go.GetComponent<TrailRenderer>(); trail.sharedMaterial = trailMaterial;
                trail.time = .18f; trail.minVertexDistance = .004f; trail.startWidth = .009f; trail.endWidth = 0;
                trail.startColor = new Color(1,.95f,.75f,1); trail.endColor = new Color(1,.95f,.75f,0);
                trail.numCapVertices = 4; trail.emitting = false; trail.shadowCastingMode = ShadowCastingMode.Off;
                rounds[i] = new Round { visual = go, trail = trail }; go.SetActive(false);
            }
        }
        public bool Launch(LabPistol owner, Vector3 position, Vector3 velocity, float mass)
        {
            if (!isActiveAndEnabled || rounds == null || velocity.sqrMagnitude < 1e-6f) return false;
            Round r = rounds[cursor]; cursor = (cursor + 1) % rounds.Length;
            Retire(r); r.owner = owner; r.velocity = velocity; r.mass = mass; r.age = 0;
            r.active = true; r.embedded = false; r.attachment = null; ActiveCount++;
            r.visual.transform.SetPositionAndRotation(position, Quaternion.LookRotation(velocity));
            r.visual.transform.localScale = new Vector3(radius * 2, radius * 2, length);
            r.visual.SetActive(true); r.trail.Clear(); r.trail.emitting = true;
            return true;
        }
        void FixedUpdate()
        {
            if (rounds == null) return;
            float dt = Time.fixedDeltaTime;
            foreach (Round r in rounds)
            {
                if (!r.active) continue;
                r.age += dt;
                if (r.embedded)
                {
                    if (r.attachment == null || !r.attachment.gameObject.activeInHierarchy || r.age >= embeddedLifetime) Retire(r);
                    continue;
                }
                if (r.age >= flightLifetime) { Retire(r); continue; }
                Vector3 start = r.visual.transform.position;
                if (ResolveOverlap(r, start)) continue;
                Vector3 nextVelocity = r.velocity + Physics.gravity * dt;
                Vector3 delta = (r.velocity + nextVelocity) * (.5f * dt);
                float distance = delta.magnitude;
                if (distance < 1e-8f) { r.velocity = nextVelocity; continue; }
                int count = Physics.SphereCastNonAlloc(start, radius, delta / distance, hits, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count == hits.Length)
                {
                    // Extremely dense overlaps are rare. Grow the query only on saturation,
                    // rather than allowing a truncated hit list to tunnel through a target.
                    var complete = Physics.SphereCastAll(start, radius, delta / distance, distance,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    Resolve(r, complete, complete.Length, start + delta, nextVelocity);
                }
                else Resolve(r, hits, count, start + delta, nextVelocity);
            }
        }
        void Resolve(Round r, RaycastHit[] candidates, int count, Vector3 next, Vector3 nextVelocity)
        {
            int selected = -1; float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider collider = candidates[i].collider;
                if (!CanHit(r, collider)) continue;
                if (candidates[i].distance < nearest) { nearest = candidates[i].distance; selected = i; }
            }
            if (selected < 0)
            {
                r.visual.transform.SetPositionAndRotation(next, Quaternion.LookRotation(nextVelocity));
                r.velocity = nextVelocity; return;
            }
            RaycastHit hit = candidates[selected];
            Embed(r, hit.collider, hit.point, nextVelocity);
        }
        static bool CanHit(Round r, Collider collider) => collider != null &&
            (r.owner == null || !collider.transform.IsChildOf(r.owner.transform)) &&
            collider.GetComponentInParent<PhysicsHand>() == null;

        bool ResolveOverlap(Round r, Vector3 position)
        {
            int count = Physics.OverlapSphereNonAlloc(position, radius, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Collider[] candidates = overlaps;
            if (count == overlaps.Length)
            {
                candidates = Physics.OverlapSphere(position, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = candidates.Length;
            }
            Collider selected = null; float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider c = candidates[i];
                if (!CanHit(r, c)) continue;
                float distance = (c.ClosestPoint(position) - position).sqrMagnitude;
                if (distance < nearest) { nearest = distance; selected = c; }
            }
            if (selected == null) return false;
            // Sphere casts omit initial overlaps. Resolve them before advancing so a
            // muzzle touching a target cannot send a round through its back face.
            Embed(r, selected, selected.ClosestPoint(position), r.velocity);
            return true;
        }
        void Embed(Round r, Collider collider, Vector3 point, Vector3 velocity)
        {
            Vector3 direction = velocity.normalized;
            r.visual.transform.SetPositionAndRotation(point + direction * (length * .25f), Quaternion.LookRotation(direction));
            Rigidbody target = collider.attachedRigidbody;
            r.attachment = target != null ? target.transform : collider.transform;
            // Queries report the current physics pose; the Transform may still be at
            // an interpolated rendering pose. Store the hit in physics-local space.
            Matrix4x4 frame = target != null ? Matrix4x4.TRS(target.position, target.rotation, target.transform.lossyScale) : r.attachment.localToWorldMatrix;
            r.localPoint = frame.inverse.MultiplyPoint3x4(r.visual.transform.position);
            r.localRotation = Quaternion.Inverse(target != null ? target.rotation : r.attachment.rotation) * r.visual.transform.rotation;
            r.embedded = true; EmbeddedCount++; Impacts++;
            Vector3 impulse = velocity * r.mass;
            LastImpactImpulse = impulse.magnitude;
            if (target != null && !target.isKinematic)
                {
                target.WakeUp();
                target.AddForceAtPosition(impulse, point, ForceMode.Impulse);
            }
            r.trail.emitting = false; r.velocity = Vector3.zero;
        }
        void LateUpdate()
        {
            if (rounds == null) return;
            foreach (Round r in rounds)
                if (r.active && r.embedded && r.attachment != null)
                    r.visual.transform.SetPositionAndRotation(r.attachment.TransformPoint(r.localPoint), r.attachment.rotation * r.localRotation);
        }
        void Retire(Round r)
        {
            if (!r.active) return;
            ActiveCount--; if (r.embedded) EmbeddedCount--;
            r.active = false; r.owner = null; r.attachment = null;
            r.trail.emitting = false; r.trail.Clear(); r.visual.SetActive(false);
        }
        public void Clear()
        {
            if (rounds != null) foreach (Round r in rounds) Retire(r);
            Impacts = 0; LastImpactImpulse = 0;
        }
        void OnDisable() => Clear();
        void OnDestroy()
        {
            if (rounds == null) return;
            foreach (Round r in rounds) if (r.visual != null) Destroy(r.visual);
            rounds = null;
        }
    }
}
