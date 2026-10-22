using System;
using PhysicsHands.Dynamics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PhysicsHands.Simulation
{
    // CPU-owned contact solve: no synchronous GPU readback in the hand physics loop.
    public sealed class CageSolver : IDisposable
    {
        CageStepJob step;
        readonly Rigidbody[] rigidBodies = new Rigidbody[ContactGatherer.MaxShapes];
        int bodyCount, settledTicks;
        readonly float sleepSpeed;
        bool disposed;
        public NativeArray<float3> Positions => step.x;
        public Vector3 PositionAt(int index) => step.x[index];
        public NativeArray<float4> RenderPositions => step.render;
        public int PairCount => step.pairs.Length;
        public int SurfaceCount => step.surface.Length;
        public bool Sleeping { get; private set; } = true;
        public bool DeformationChanged { get; private set; }
        public CageSolver(ElasticCage cage) : this(cage, Array.Empty<CageAxleConstraint>()) { }
        internal CageSolver(ElasticCage cage, CageAxleConstraint[] axles)
        {
            try
            {
                int n = cage.rest.Length, max = ContactGatherer.MaxShapes;
                sleepSpeed = cage.spacing * .01f;
                step.x = new NativeArray<float3>(n, Allocator.Persistent);
                step.rest = new NativeArray<float3>(n, Allocator.Persistent);
                for (int i = 0; i < n; i++) step.rest[i] = cage.rest[i];
                step.previous = new NativeArray<float3>(n, Allocator.Persistent);
                step.velocity = new NativeArray<float3>(n, Allocator.Persistent);
                step.render = new NativeArray<float4>(n, Allocator.Persistent);
                step.inverseMass = Copy(cage.inverseMass);
                step.surface = Copy(CageSurface.Extract(cage));
                step.axles = Copy(axles); step.nodeAxle = new NativeArray<int>(n, Allocator.Persistent);
                for (int i = 0; i < n; i++) step.nodeAxle[i] = -1;
                for (int i = 0; i < axles.Length; i++) step.nodeAxle[axles[i].node] = i;
                step.tets = Copy(cage.tets); step.inverseRest = Copy(cage.inverseRest);
                step.volume = Copy(cage.restVolume); step.mu = Copy(cage.mu); step.lambda = Copy(cage.lambda);
                step.multipliers = new NativeArray<float>(cage.tets.Length, Allocator.Persistent);
                step.shapes = new NativeArray<ContactShape>(max, Allocator.Persistent);
                step.shapeBody = new NativeArray<int>(max, Allocator.Persistent);
                step.bodies = new NativeArray<CageRigidBody>(max, Allocator.Persistent);
                step.bodyOffset = new NativeArray<float3>(max, Allocator.Persistent);
                step.bodyAngle = new NativeArray<float3>(max, Allocator.Persistent);
                step.bodyDeltaVelocity = new NativeArray<float3>(max, Allocator.Persistent);
                step.bodyDeltaOmega = new NativeArray<float3>(max, Allocator.Persistent);
                step.impulse = new NativeArray<float3>(max, Allocator.Persistent);
                step.torque = new NativeArray<float3>(max, Allocator.Persistent);
                step.contactPoint = new NativeArray<float3>(max, Allocator.Persistent);
                step.contactWeight = new NativeArray<float>(max, Allocator.Persistent);
                step.pairs = new NativeList<int2>(math.max(1024, step.surface.Length * 4), Allocator.Persistent);
                Reset();
            }
            catch { Dispose(); throw; }
        }
        static NativeArray<T> Copy<T>(T[] values) where T : struct => new(values, Allocator.Persistent);
        public void Reset()
        {
            step.x.CopyFrom(step.rest);
            for (int i = 0; i < step.x.Length; i++)
            {
                step.previous[i] = step.x[i]; step.velocity[i] = 0;
                step.render[i] = new float4(step.x[i], 1);
            }
            step.pairs.Clear(); Sleeping = true; settledTicks = 0; DeformationChanged = true;
        }
        public void Step(ContactGatherer contacts, Transform frame, float dt, int substeps, int iterations,
            float friction, float radius, float damping, Vector3 gravity)
        {
            bodyCount = 0;
            for (int c = 0; c < contacts.Count; c++)
            {
                step.shapes[c] = contacts.Shapes[c];
                var body = contacts.BodyAt(c);
                int index = -1;
                if (body != null && contacts.Policy?.CanApplyReaction(body) != false)
                {
                    for (int b = 0; b < bodyCount; b++) if (rigidBodies[b] == body) { index = b; break; }
                    if (index < 0)
                    {
                        index = bodyCount++; rigidBodies[index] = body;
                        Quaternion basis = Quaternion.Inverse(frame.rotation) * body.rotation * body.inertiaTensorRotation;
                        var rotation = new float3x3(new quaternion(basis.x, basis.y, basis.z, basis.w));
                        Vector3 inertia = body.inertiaTensor;
                        step.bodies[index] = new CageRigidBody
                        {
                            inverseMass = 1 / body.mass, centre = frame.InverseTransformPoint(body.worldCenterOfMass),
                            velocity = frame.InverseTransformDirection(body.linearVelocity + body.GetAccumulatedForce(dt) * (dt / body.mass)),
                            inverseInertia = math.mul(math.mul(rotation, float3x3.Scale(new float3(1 / inertia.x, 1 / inertia.y, 1 / inertia.z))), math.transpose(rotation))
                        };
                    }
                }
                step.shapeBody[c] = index;
                if (index >= 0)
                {
                    var s = step.shapes[c];
                    float3 deltaV = step.bodies[index].velocity - (float3)frame.InverseTransformDirection(body.linearVelocity);
                    float3 deltaW = math.mul(step.bodies[index].inverseInertia, (float3)frame.InverseTransformDirection(body.GetAccumulatedTorque(dt))) * dt;
                    s.velocityA += deltaV + math.cross(deltaW, s.a - step.bodies[index].centre);
                    s.velocityB += deltaV + math.cross(deltaW, s.b - step.bodies[index].centre);
                    step.shapes[c] = s;
                }
            }
            step.shapeCount = contacts.Count; step.bodyCount = bodyCount;
            if (Sleeping && contacts.Count == 0 && gravity == Vector3.zero) { step.pairs.Clear(); DeformationChanged = false; return; }
            step.dt = dt; step.substeps = math.max(1, substeps); step.iterations = math.max(1, iterations);
            step.friction = friction; step.radius = radius; step.damping = damping; step.gravity = (float3)gravity;
            step.Run();
            Sleeping = false; DeformationChanged = true;
            if (contacts.Count == 0 && gravity == Vector3.zero && AtQuiescentEnergy()) settledTicks++;
            else settledTicks = 0;
            if (settledTicks >= 8)
            {
                for (int i = 0; i < step.velocity.Length; i++) step.velocity[i] = 0;
                Sleeping = true; // Preserve the settled pose, including a freely rotated bearing.
            }
        }
        bool AtQuiescentEnergy()
        {
            for (int i = 0; i < step.velocity.Length; i++)
                if (math.lengthsq(step.velocity[i]) > sleepSpeed * sleepSpeed) return false;
            for (int e = 0; e < step.tets.Length; e++)
            {
                var t = step.tets[e];
                float3 a = step.x[t.x];
                var f = math.mul(new float3x3(step.x[t.y] - a, step.x[t.z] - a, step.x[t.w] - a), step.inverseRest[e]);
                float j = math.determinant(f), i1 = math.lengthsq(f.c0) + math.lengthsq(f.c1) + math.lengthsq(f.c2);
                float energy = .5f * (i1 - 3) - (j - 1) + .5f * (1 + step.lambda[e] / step.mu[e]) * (j - 1) * (j - 1);
                // Numerical rest tolerance, approximately 0.1% strain; never sleep a folded cage.
                if (j <= 0 || energy > 1e-6f) return false;
            }
            return true;
        }
        public float ApplyReactions(ContactGatherer contacts, Transform frame, float dt)
        {
            float total = 0;
            for (int b = 0; b < bodyCount; b++)
            {
                float weight = step.contactWeight[b];
                var body = rigidBodies[b];
                if (weight <= 0 || body == null || body.isKinematic) continue;
                Vector3 reaction = frame.TransformDirection(-step.impulse[b]);
                Vector3 point = frame.TransformPoint(step.contactPoint[b] / weight);
                // The same impulse and full torque solved against this body's finite inertia are
                // applied once. No per-collider clamp, duplicated bone force or stale replay.
                contacts.Policy?.NotifyPhysicalContact(body, reaction.normalized, point);
                body.AddForce(reaction, ForceMode.Impulse);
                body.AddTorque(frame.TransformDirection(-step.torque[b]), ForceMode.Impulse);
                total += reaction.magnitude;
            }
            return total / dt;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Dispose(ref step.x); Dispose(ref step.previous); Dispose(ref step.velocity); Dispose(ref step.rest);
            Dispose(ref step.render); Dispose(ref step.inverseMass); Dispose(ref step.tets); Dispose(ref step.inverseRest);
            Dispose(ref step.axles); Dispose(ref step.nodeAxle); Dispose(ref step.surface); Dispose(ref step.volume); Dispose(ref step.mu); Dispose(ref step.lambda); Dispose(ref step.multipliers);
            Dispose(ref step.shapes); Dispose(ref step.shapeBody); Dispose(ref step.bodies);
            Dispose(ref step.bodyOffset); Dispose(ref step.bodyAngle); Dispose(ref step.bodyDeltaVelocity); Dispose(ref step.bodyDeltaOmega);
            Dispose(ref step.impulse); Dispose(ref step.torque); Dispose(ref step.contactPoint); Dispose(ref step.contactWeight);
            if (step.pairs.IsCreated) step.pairs.Dispose();
            Array.Clear(rigidBodies, 0, rigidBodies.Length);
        }
        static void Dispose<T>(ref NativeArray<T> values) where T : struct { if (values.IsCreated) values.Dispose(); }
    }
}
