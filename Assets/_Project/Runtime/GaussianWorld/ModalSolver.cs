using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PhysicsHands.GaussianWorld
{
    // Reduced elastodynamics in the cage's lowest mass-normalised modes (u = Phi q, Phi^T M Phi = I,
    // Phi^T K Phi = diag omega^2) baked offline from the learned stiffness. Each mode is advanced with
    // the exact damped-oscillator solution, so the learned frequencies hold at any step size. Contacts
    // are position projections in mode space; the 3x3 effective mass (Phi_s Phi_s^T)^-1 of each sample
    // gives the impulse handed back to PhysX in the same fixed step.
    public sealed class ModalSolver : IDisposable
    {
        public const int MaxShapes = ContactGatherer.MaxShapes;
        public readonly int ModeCount;
        public NativeArray<float3> Positions, Rest;
        public NativeArray<float4> RenderPositions;
        public NativeArray<float3> Impulse, ImpulseMoment;
        public NativeArray<float> ImpulseMagnitude;
        public NativeArray<ContactShape> Shapes;
        public NativeArray<float> Q, QDot;
        public int PairCount => pairs.Length;
        NativeArray<float> omega, predicted, begin;
        NativeArray<float3> nodeModes, sampleModes, sampleRest;
        NativeArray<float3x3> sampleEffectiveMass;
        NativeArray<int> voxelNodes, sampleVoxel;
        NativeArray<float4> sampleWeights;
        NativeList<int2> pairs;

        public ModalSolver(ElasticCage cage, float[] modeOmega, float[] modeShapes)
        {
            int n = cage.rest.Length, k = ModeCount = modeOmega.Length;
            if (modeShapes.Length != n * 3 * k) throw new ArgumentException("Mode shapes do not match the cage.");
            Rest = new NativeArray<float3>(n, Allocator.Persistent);
            for (int i = 0; i < n; i++) Rest[i] = cage.rest[i];
            Positions = new NativeArray<float3>(Rest, Allocator.Persistent); RenderPositions = new NativeArray<float4>(n, Allocator.Persistent);
            omega = new NativeArray<float>(modeOmega, Allocator.Persistent);
            Q = new NativeArray<float>(k, Allocator.Persistent); QDot = new NativeArray<float>(k, Allocator.Persistent); predicted = new NativeArray<float>(k, Allocator.Persistent); begin = new NativeArray<float>(k, Allocator.Persistent);
            nodeModes = new NativeArray<float3>(n * k, Allocator.Persistent);
            for (int i = 0; i < n; i++) for (int m = 0; m < k; m++)
                nodeModes[i * k + m] = new float3(modeShapes[(i * 3) * k + m], modeShapes[(i * 3 + 1) * k + m], modeShapes[(i * 3 + 2) * k + m]);

            int samples = cage.sampleVoxel.Length;
            voxelNodes = new NativeArray<int>(cage.voxelNodes, Allocator.Persistent); sampleVoxel = new NativeArray<int>(cage.sampleVoxel, Allocator.Persistent);
            sampleWeights = new NativeArray<float4>(cage.sampleWeights.Length, Allocator.Persistent);
            for (int i = 0; i < sampleWeights.Length; i++) sampleWeights[i] = cage.sampleWeights[i];
            sampleModes = new NativeArray<float3>(samples * k, Allocator.Persistent); sampleRest = new NativeArray<float3>(samples, Allocator.Persistent);
            sampleEffectiveMass = new NativeArray<float3x3>(samples, Allocator.Persistent);
            for (int s = 0; s < samples; s++)
            {
                int voxel = cage.sampleVoxel[s]; float3 p = 0;
                for (int corner = 0; corner < 8; corner++)
                {
                    int node = cage.voxelNodes[voxel * 8 + corner]; float w = cage.sampleWeights[2 * s + corner / 4][corner % 4];
                    p += w * (float3)cage.rest[node];
                    for (int m = 0; m < k; m++) sampleModes[s * k + m] += w * nodeModes[node * k + m];
                }
                sampleRest[s] = p;
                float3x3 inverseMass = float3x3.zero;
                for (int m = 0; m < k; m++) { float3 phi = sampleModes[s * k + m]; inverseMass += new float3x3(phi * phi.x, phi * phi.y, phi * phi.z); }
                // Directions the modes cannot move are rigid; regularise so they carry a large but finite mass.
                float trace = inverseMass.c0.x + inverseMass.c1.y + inverseMass.c2.z;
                sampleEffectiveMass[s] = trace > 1e-12f ? math.inverse(inverseMass + float3x3.identity * (1e-3f * trace)) : float3x3.zero;
            }
            Shapes = new NativeArray<ContactShape>(MaxShapes, Allocator.Persistent);
            Impulse = new NativeArray<float3>(MaxShapes, Allocator.Persistent); ImpulseMoment = new NativeArray<float3>(MaxShapes, Allocator.Persistent);
            ImpulseMagnitude = new NativeArray<float>(MaxShapes, Allocator.Persistent);
            pairs = new NativeList<int2>(1024, Allocator.Persistent);
            Reset();
        }
        public void Reset()
        {
            for (int m = 0; m < ModeCount; m++) Q[m] = QDot[m] = 0;
            Positions.CopyFrom(Rest);
            for (int i = 0; i < Rest.Length; i++) RenderPositions[i] = new float4(Rest[i], 1);
        }
        public void Step(int shapeCount, float dt, int substeps, float damping, float friction, float sampleRadius)
        {
            new StepJob
            {
                q = Q, qd = QDot, predicted = predicted, begin = begin, omega = omega, nodeModes = nodeModes, rest = Rest, x = Positions, render = RenderPositions,
                sampleModes = sampleModes, sampleRest = sampleRest, effectiveMass = sampleEffectiveMass,
                voxelNodes = voxelNodes, sampleVoxel = sampleVoxel, weights = sampleWeights, shapes = Shapes, shapeCount = math.min(shapeCount, MaxShapes),
                pairs = pairs, impulse = Impulse, moment = ImpulseMoment, magnitude = ImpulseMagnitude,
                dt = dt, substeps = math.max(1, substeps), damping = damping, friction = friction, radius = sampleRadius,
            }.Run();
        }
        public void Dispose()
        {
            Positions.Dispose(); Rest.Dispose(); RenderPositions.Dispose(); Q.Dispose(); QDot.Dispose(); predicted.Dispose(); begin.Dispose(); omega.Dispose(); nodeModes.Dispose();
            sampleModes.Dispose(); sampleRest.Dispose(); sampleEffectiveMass.Dispose(); voxelNodes.Dispose(); sampleVoxel.Dispose(); sampleWeights.Dispose();
            Shapes.Dispose(); Impulse.Dispose(); ImpulseMoment.Dispose(); ImpulseMagnitude.Dispose(); pairs.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct StepJob : IJob
        {
            public NativeArray<float> q, qd, predicted, begin;
            [ReadOnly] public NativeArray<float> omega;
            [ReadOnly] public NativeArray<float3> nodeModes, rest, sampleModes, sampleRest;
            [ReadOnly] public NativeArray<float3x3> effectiveMass;
            public NativeArray<float3> x;
            public NativeArray<float4> render;
            [ReadOnly] public NativeArray<int> voxelNodes, sampleVoxel;
            [ReadOnly] public NativeArray<float4> weights;
            [ReadOnly] public NativeArray<ContactShape> shapes;
            public NativeList<int2> pairs;
            public NativeArray<float3> impulse, moment;
            public NativeArray<float> magnitude;
            public int shapeCount, substeps;
            public float dt, damping, friction, radius;

            float3 Sample(NativeArray<float> modes, int s)
            {
                int k = modes.Length; float3 p = sampleRest[s];
                for (int m = 0; m < k; m++) p += sampleModes[s * k + m] * modes[m];
                return p;
            }
            void UpdateNodes()
            {
                int k = q.Length;
                for (int i = 0; i < x.Length; i++)
                {
                    float3 p = rest[i];
                    for (int m = 0; m < k; m++) p += nodeModes[i * k + m] * q[m];
                    x[i] = p;
                }
            }

            public void Execute()
            {
                int k = q.Length;
                for (int i = 0; i < impulse.Length; i++) { impulse[i] = 0; moment[i] = 0; magnitude[i] = 0; }
                pairs.Clear();
                const float margin = .01f;
                for (int c = 0; c < shapeCount; c++)
                {
                    var shape = shapes[c]; float3 lo, hi;
                    if (shape.isBox != 0)
                    {
                        float3 extent = math.abs(shape.axisX) * shape.halfExtents.x + math.abs(shape.axisY) * shape.halfExtents.y + math.abs(shape.axisZ) * shape.halfExtents.z;
                        float3 end = shape.a + shape.velocityA * dt; lo = math.min(shape.a, end) - extent; hi = math.max(shape.a, end) + extent;
                    }
                    else
                    {
                        float3 a1 = shape.a + shape.velocityA * dt, b1 = shape.b + shape.velocityB * dt;
                        lo = math.min(math.min(shape.a, shape.b), math.min(a1, b1)) - shape.radius; hi = math.max(math.max(shape.a, shape.b), math.max(a1, b1)) + shape.radius;
                    }
                    lo -= radius + margin; hi += radius + margin;
                    for (int s = 0; s < sampleVoxel.Length; s++)
                    {
                        int voxel = sampleVoxel[s]; float4 a = weights[2 * s], b = weights[2 * s + 1]; float3 p = 0;
                        for (int corner = 0; corner < 4; corner++) p += a[corner] * x[voxelNodes[voxel * 8 + corner]] + b[corner] * x[voxelNodes[voxel * 8 + corner + 4]];
                        if (math.all(p >= lo) && math.all(p <= hi)) pairs.Add(new int2(s, c));
                    }
                }

                float h = dt / substeps, beta = .5f * damping;
                for (int step = 0; step < substeps; step++)
                {
                    float t = (step + 1) * h;
                    for (int m = 0; m < k; m++)
                    {
                        float w = omega[m], q0 = begin[m] = q[m], v0 = qd[m], e = math.exp(-beta * h), d2 = w * w - beta * beta;
                        float c, s, sd; // cos-like, sin/omega_d-like and omega_d*sin-like terms of the damped oscillator
                        if (d2 > 1e-8f) { float wd = math.sqrt(d2); math.sincos(wd * h, out s, out c); sd = wd * s; s /= wd; }
                        else if (d2 < -1e-8f) { float g = math.sqrt(-d2), ep = math.exp(g * h), em = 1 / ep; c = .5f * (ep + em); s = .5f * (ep - em) / g; sd = -g * g * s; }
                        else { c = 1; s = h; sd = 0; }
                        q[m] = e * (q0 * (c + beta * s) + v0 * s);
                        qd[m] = e * (v0 * (c - beta * s) - q0 * (sd + beta * beta * s));
                        predicted[m] = q[m];
                    }
                    for (int p = 0; p < pairs.Length; p++) Contact(pairs[p].x, pairs[p].y, t, h);
                    for (int m = 0; m < k; m++) qd[m] += (q[m] - predicted[m]) / h;
                }
                UpdateNodes();
                for (int i = 0; i < x.Length; i++) render[i] = new float4(x[i], 1);
            }

            void Contact(int s, int c, float t, float h)
            {
                float3x3 meff = effectiveMass[s];
                if (meff.c0.x == 0) return;
                float3 p = Sample(q, s), previous = Sample(begin, s);
                var shape = shapes[c]; float3 n, shapeVelocity; float penetration;
                if (shape.isBox != 0)
                {
                    float3 centre = shape.a + shape.velocityA * t, d = p - centre;
                    float3 local = new(math.dot(d, shape.axisX), math.dot(d, shape.axisY), math.dot(d, shape.axisZ));
                    float3 clamped = math.clamp(local, -shape.halfExtents, shape.halfExtents);
                    shapeVelocity = shape.velocityA;
                    if (math.any(local != clamped))
                    {
                        float3 delta = d - (shape.axisX * clamped.x + shape.axisY * clamped.y + shape.axisZ * clamped.z);
                        float dist = math.length(delta); if (dist >= radius || dist < 1e-9f) return;
                        n = delta / dist; penetration = radius - dist;
                    }
                    else
                    {
                        float3 gap = shape.halfExtents - math.abs(local);
                        int axis = gap.x < gap.y ? (gap.x < gap.z ? 0 : 2) : (gap.y < gap.z ? 1 : 2);
                        float3 direction = axis == 0 ? shape.axisX : axis == 1 ? shape.axisY : shape.axisZ;
                        n = direction * (local[axis] < 0 ? -1 : 1); penetration = gap[axis] + radius;
                    }
                }
                else
                {
                    float3 a = shape.a + shape.velocityA * t, ab = shape.b + shape.velocityB * t - a;
                    float u = math.saturate(math.dot(p - a, ab) / math.max(math.dot(ab, ab), 1e-12f));
                    float3 delta = p - (a + u * ab); float dist = math.length(delta), reach = shape.radius + radius;
                    if (dist >= reach) return;
                    n = dist > 1e-7f ? delta / dist : new float3(0, 1, 0); penetration = reach - dist;
                    shapeVelocity = math.lerp(shape.velocityA, shape.velocityB, u);
                }
                float3 relative = p - previous - shapeVelocity * h, tangent = relative - n * math.dot(relative, n);
                float tangentLength = math.length(tangent);
                float3 correction = n * penetration;
                if (tangentLength > 1e-9f) correction -= tangent * math.min(1, friction * penetration / tangentLength);
                // Minimum-norm mode update moving the sample by `correction`: dq = Phi_s^T Meff correction.
                float3 j = math.mul(meff, correction); int k = q.Length;
                for (int m = 0; m < k; m++) q[m] += math.dot(sampleModes[s * k + m], j);
                j /= h; float magnitudeJ = math.length(j);
                impulse[c] += j; moment[c] += magnitudeJ * p; magnitude[c] += magnitudeJ;
            }
        }
    }
}
