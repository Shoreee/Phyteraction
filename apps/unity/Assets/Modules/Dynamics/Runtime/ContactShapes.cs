using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PhysicsHands.Dynamics
{
    // Contact obstacle in solver space. Capsule: segment a-b (sphere: a == b). Box: centre a.
    public struct ContactShape
    {
        public float3 a, b, previousA, previousB, velocityA, velocityB;
        public float3 axisX, axisY, axisZ, halfExtents;
        public float radius;
        public int isBox;
    }

    // Hand bones and physical colliders as velocity-extrapolated shapes in a solver space related to
    // world by a uniform-scale affine map, plus the reaction path back to PhysX.
    public sealed class ContactGatherer
    {
        public const int MaxShapes = 96;
        public readonly ContactShape[] Shapes = new ContactShape[MaxShapes];
        public int Count
        {
            get;
            private set;
        }

        public double SampleTime
        {
            get;
            private set;
        }

        readonly Rigidbody[] bodies = new Rigidbody[MaxShapes];
        readonly Dictionary<EntityId, (Vector3 a, Vector3 b)> last = new();
        Matrix4x4 spaceFromWorld;
        float unitsPerMeter;
        Bounds? region;
        float stepDuration;
        public void Reset() => last.Clear();
        public void Begin(Matrix4x4 spaceFromWorld, float unitsPerMeter, Bounds? region = null)
        {
            this.spaceFromWorld = spaceFromWorld;
            this.unitsPerMeter = unitsPerMeter;
            this.region = region;
            Count = 0;
            SampleTime = Time.realtimeSinceStartupAsDouble;
        }

        public IContactBodyPolicy Policy
        {
            get;
            set;
        }

        public void AddCollider(Collider c, float dt)
        {
            if (Count == MaxShapes || c == null || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger)
                return;
            Vector3 scale = c.transform.lossyScale;
            // Rigidbody interpolation is a presentation pose, potentially several physics
            // steps behind. Shape geometry and point velocity must use the same PhysX pose.
            Matrix4x4 frame = PhysicsFrame(c);
            Quaternion rotation = c.attachedRigidbody != null
                ? c.attachedRigidbody.rotation * Quaternion.Inverse(c.attachedRigidbody.transform.rotation) * c.transform.rotation
                : c.transform.rotation;
            stepDuration = dt;
            if (c is CapsuleCollider cap)
            {
                Vector3 axis = cap.direction == 0 ? Vector3.right : cap.direction == 1 ? Vector3.up : Vector3.forward;
                float along = Mathf.Abs(scale[cap.direction]), across = Mathf.Max(Mathf.Abs(scale[(cap.direction + 1) % 3]), Mathf.Abs(scale[(cap.direction + 2) % 3]));
                float r = cap.radius * across;
                Vector3 center = frame.MultiplyPoint3x4(cap.center);
                Vector3 offset = rotation * axis * Mathf.Max(0, cap.height * along * .5f - r);
                AddCapsule(center - offset, center + offset, r, c.attachedRigidbody, c.GetEntityId(), dt);
            }
            else if (c is SphereCollider sphere)
            {
                Vector3 p = frame.MultiplyPoint3x4(sphere.center);
                AddCapsule(p, p, sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)), c.attachedRigidbody, c.GetEntityId(), dt);
            }
            else if (c is BoxCollider box)
            {
                Vector3 centre = frame.MultiplyPoint3x4(box.center);
                Vector3 half = Vector3.Scale(box.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * unitsPerMeter;
                var shape = new ContactShape{isBox = 1, a = Point(centre), b = Point(centre), halfExtents = half, axisX = Axis(rotation * Vector3.right), axisY = Axis(rotation * Vector3.up), axisZ = Axis(rotation * Vector3.forward), };
                shape.previousA = shape.previousB = last.TryGetValue(c.GetEntityId(), out var boxPrevious) ? (float3)boxPrevious.a : shape.a;
                shape.velocityA = shape.velocityB = Velocity(c.attachedRigidbody, centre, centre, c.GetEntityId(), dt).a;
                Push(shape, c.attachedRigidbody);
            }
        }

        public static Matrix4x4 PhysicsFrame(Collider collider)
        {
            var body = collider.attachedRigidbody;
            if (body == null) return collider.transform.localToWorldMatrix;
            var displayed = Matrix4x4.TRS(body.transform.position, body.transform.rotation, Vector3.one);
            return Matrix4x4.TRS(body.position, body.rotation, Vector3.one) * displayed.inverse * collider.transform.localToWorldMatrix;
        }

        void AddCapsule(Vector3 a, Vector3 b, float radius, Rigidbody body, EntityId id, float dt)
        {
            bool had = last.TryGetValue(id, out var previous);
            var(va, vb) = Velocity(body, a, b, id, dt);
            Push(new ContactShape{a = Point(a), b = Point(b), previousA = had ? (float3)previous.a : Point(a), previousB = had ? (float3)previous.b : Point(b), velocityA = va, velocityB = vb, radius = radius * unitsPerMeter}, body);
        }

        // Dynamic bodies report PhysX velocity; kinematic and static ones are differenced.
        (float3 a, float3 b) Velocity(Rigidbody body, Vector3 a, Vector3 b, EntityId id, float dt)
        {
            float3 la = Point(a), lb = Point(b);
            bool had = last.TryGetValue(id, out var previous);
            last[id] = (la, lb);
            if (body != null && !body.isKinematic)
                return (Direction(body.GetPointVelocity(a)), Direction(body.GetPointVelocity(b)));
            return had ? ((la - (float3)previous.a) / dt, (lb - (float3)previous.b) / dt) : (float3.zero, float3.zero);
        }

        void Push(ContactShape shape, Rigidbody body)
        {
            if (region.HasValue)
            {
                float3 extent = shape.isBox != 0
                    ? math.abs(shape.axisX) * shape.halfExtents.x + math.abs(shape.axisY) * shape.halfExtents.y + math.abs(shape.axisZ) * shape.halfExtents.z
                    : new float3(shape.radius);
                float3 lo = math.min(math.min(shape.a, shape.b), math.min(shape.previousA, shape.previousB)) - extent;
                float3 hi = math.max(math.max(shape.a, shape.b), math.max(shape.previousA, shape.previousB)) + extent;
                float3 sweep = math.max(math.abs(shape.velocityA), math.abs(shape.velocityB)) * stepDuration;
                if (math.any(hi + sweep < (float3)region.Value.min) || math.any(lo - sweep > (float3)region.Value.max)) return;
            }
            bodies[Count] = body != null && !body.isKinematic ? body : null;
            Shapes[Count++] = shape;
        }

        float3 Point(Vector3 world) => spaceFromWorld.MultiplyPoint3x4(world);
        float3 Direction(Vector3 world) => spaceFromWorld.MultiplyVector(world);
        float3 Axis(Vector3 world) => math.normalize((float3)spaceFromWorld.MultiplyVector(world));
        // Applies the reaction (-impulse on the solver body) to the shape's rigidbody, clamped as a
        // safety net against tracking jumps; returns the applied impulse magnitude.
        public float ApplyReaction(int shape, Rigidbody[] snapshot, Vector3 worldImpulseOnBody, Vector3 worldPoint, float maxForce, float dt)
        {
            Vector3 reaction = -worldImpulseOnBody;
            float limit = maxForce * dt;
            if (reaction.magnitude > limit)
                reaction = reaction.normalized * limit;
            var body = snapshot[shape];
            if (body == null || body.isKinematic || Policy?.CanApplyReaction(body) == false) return 0;
            if (reaction.sqrMagnitude > 0)
            {
                Policy?.NotifyPhysicalContact(body, reaction.normalized, worldPoint);
                body.AddForceAtPosition(reaction, worldPoint, ForceMode.Impulse);
            }

            return reaction.magnitude;
        }

        public Rigidbody BodyAt(int index) => bodies[index];

        public Rigidbody[] Snapshot(Rigidbody[] into = null)
        {
            into ??= new Rigidbody[MaxShapes];
            Array.Copy(bodies, into, MaxShapes);
            return into;
        }
    }
}
