using System;
using System.Collections.Generic;
using PhysicsHands.Hands;
using Unity.Mathematics;
using UnityEngine;

namespace PhysicsHands.GaussianWorld
{
    // Contact obstacle in solver space. Capsule: segment a-b (sphere: a == b). Box: centre a.
    public struct ContactShape
    {
        public float3 a, b, velocityA, velocityB;
        public float3 axisX, axisY, axisZ, halfExtents;
        public float radius;
        public int isBox;
    }

    // Hand bones and probe colliders as velocity-extrapolated shapes in a solver space related to
    // world by a uniform-scale affine map, plus the reaction path back to PhysX.
    public sealed class ContactGatherer
    {
        public const int MaxShapes = 96;
        public readonly ContactShape[] Shapes = new ContactShape[MaxShapes];
        public int Count { get; private set; }
        public double SampleTime { get; private set; }
        readonly Rigidbody[] bodies = new Rigidbody[MaxShapes];
        readonly Dictionary<int, (Vector3 a, Vector3 b)> last = new();
        Matrix4x4 spaceFromWorld;
        float unitsPerMeter;

        public void Reset() => last.Clear();
        public void Begin(Matrix4x4 spaceFromWorld, float unitsPerMeter)
        {
            this.spaceFromWorld = spaceFromWorld; this.unitsPerMeter = unitsPerMeter; Count = 0; SampleTime = Time.realtimeSinceStartupAsDouble;
        }
        public void AddHands(IEnumerable<HoloHand> hands, float dt)
        {
            foreach (var hand in hands)
            {
                if (hand == null || !hand.isActiveAndEnabled || !hand.GoodTracking) continue;
                foreach (var c in hand.BoneColliders) AddCollider(c, dt);
            }
        }
        public void AddCollider(Collider c, float dt)
        {
            if (Count == MaxShapes || c == null || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) return;
            Vector3 scale = c.transform.lossyScale;
            if (c is CapsuleCollider cap)
            {
                Vector3 axis = cap.direction == 0 ? Vector3.right : cap.direction == 1 ? Vector3.up : Vector3.forward;
                float along = Mathf.Abs(scale[cap.direction]), across = Mathf.Max(Mathf.Abs(scale[(cap.direction + 1) % 3]), Mathf.Abs(scale[(cap.direction + 2) % 3]));
                float r = cap.radius * across; Vector3 center = cap.transform.TransformPoint(cap.center);
                Vector3 offset = cap.transform.TransformDirection(axis) * Mathf.Max(0, cap.height * along * .5f - r);
                AddCapsule(center - offset, center + offset, r, c.attachedRigidbody, c.GetInstanceID(), dt);
            }
            else if (c is SphereCollider sphere)
            {
                Vector3 p = sphere.transform.TransformPoint(sphere.center);
                AddCapsule(p, p, sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)), c.attachedRigidbody, c.GetInstanceID(), dt);
            }
            else if (c is BoxCollider box)
            {
                var t = box.transform; Vector3 centre = t.TransformPoint(box.center);
                Vector3 half = Vector3.Scale(box.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * unitsPerMeter;
                var shape = new ContactShape
                {
                    isBox = 1, a = Point(centre), b = Point(centre), halfExtents = half,
                    axisX = Axis(t.right), axisY = Axis(t.up), axisZ = Axis(t.forward),
                };
                shape.velocityA = shape.velocityB = Velocity(c.attachedRigidbody, centre, centre, c.GetInstanceID(), dt).a;
                Push(shape, c.attachedRigidbody);
            }
        }
        // World-space sphere without a collider (tests).
        public void AddSphere(Vector3 world, float radius, int id, float dt) => AddCapsule(world, world, radius, null, id, dt);
        void AddCapsule(Vector3 a, Vector3 b, float radius, Rigidbody body, int id, float dt)
        {
            var (va, vb) = Velocity(body, a, b, id, dt);
            Push(new ContactShape { a = Point(a), b = Point(b), velocityA = va, velocityB = vb, radius = radius * unitsPerMeter }, body);
        }
        // Dynamic bodies report PhysX velocity; kinematic and static ones are differenced.
        (float3 a, float3 b) Velocity(Rigidbody body, Vector3 a, Vector3 b, int id, float dt)
        {
            float3 la = Point(a), lb = Point(b);
            bool had = last.TryGetValue(id, out var previous); last[id] = (la, lb);
            if (body != null && !body.isKinematic) return (Direction(body.GetPointVelocity(a)), Direction(body.GetPointVelocity(b)));
            return had ? ((la - (float3)previous.a) / dt, (lb - (float3)previous.b) / dt) : (float3.zero, float3.zero);
        }
        void Push(ContactShape shape, Rigidbody body)
        {
            bodies[Count] = body != null && !body.isKinematic ? body : null; Shapes[Count++] = shape;
        }
        float3 Point(Vector3 world) => spaceFromWorld.MultiplyPoint3x4(world);
        float3 Direction(Vector3 world) => spaceFromWorld.MultiplyVector(world);
        float3 Axis(Vector3 world) => math.normalize((float3)spaceFromWorld.MultiplyVector(world));

        // Applies the reaction (-impulse on the solver body) to the shape's rigidbody, clamped as a
        // safety net against tracking jumps; returns the applied impulse magnitude.
        public float ApplyReaction(int shape, Rigidbody[] snapshot, Vector3 worldImpulseOnBody, Vector3 worldPoint, float maxForce, float dt)
        {
            Vector3 reaction = -worldImpulseOnBody; float limit = maxForce * dt;
            if (reaction.magnitude > limit) reaction = reaction.normalized * limit;
            var body = snapshot[shape];
            if (body != null) body.AddForceAtPosition(reaction, worldPoint, ForceMode.Impulse);
            return reaction.magnitude;
        }
        public Rigidbody[] Snapshot(Rigidbody[] into = null)
        {
            into ??= new Rigidbody[MaxShapes]; Array.Copy(bodies, into, MaxShapes); return into;
        }
    }
}
