using PhysicsHands.Dynamics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PhysicsHands.Simulation
{
    [BurstCompile(CompileSynchronously = true)]
    internal struct ModalStepJob : IJob
    {
        public NativeArray<float> q, qd, predicted, begin;
        [ReadOnly]
        public NativeArray<float> omega;
        [ReadOnly]
        public NativeArray<float3> nodeModes, rest, sampleModes, sampleRest;
        [ReadOnly]
        public NativeArray<float3x3> effectiveMass;
        public NativeArray<float3> x;
        public NativeArray<float4> render;
        [ReadOnly]
        public NativeArray<int> voxelNodes, sampleVoxel;
        [ReadOnly]
        public NativeArray<float4> weights;
        [ReadOnly]
        public NativeArray<ContactShape> shapes;
        public NativeList<int2> pairs;
        public NativeArray<float3> impulse, moment;
        public NativeArray<float> magnitude;
        public int shapeCount, substeps;
        public float dt, damping, friction, radius;
        float3 Sample(NativeArray<float> modes, int s)
        {
            int k = modes.Length;
            float3 p = sampleRest[s];
            for (int m = 0; m < k; m++)
                p += sampleModes[s * k + m] * modes[m];
            return p;
        }

        void UpdateNodes()
        {
            int k = q.Length;
            for (int i = 0; i < x.Length; i++)
            {
                float3 p = rest[i];
                for (int m = 0; m < k; m++)
                    p += nodeModes[i * k + m] * q[m];
                x[i] = p;
            }
        }

        public void Execute()
        {
            int k = q.Length;
            for (int i = 0; i < impulse.Length; i++)
            {
                impulse[i] = 0;
                moment[i] = 0;
                magnitude[i] = 0;
            }

            pairs.Clear();
            const float margin = .01f;
            for (int c = 0; c < shapeCount; c++)
            {
                var shape = shapes[c];
                float3 lo, hi;
                if (shape.isBox != 0)
                {
                    float3 extent = math.abs(shape.axisX) * shape.halfExtents.x + math.abs(shape.axisY) * shape.halfExtents.y + math.abs(shape.axisZ) * shape.halfExtents.z;
                    float3 end = shape.a + shape.velocityA * dt;
                    lo = math.min(shape.a, end) - extent;
                    hi = math.max(shape.a, end) + extent;
                }
                else
                {
                    float3 a1 = shape.a + shape.velocityA * dt, b1 = shape.b + shape.velocityB * dt;
                    lo = math.min(math.min(shape.a, shape.b), math.min(a1, b1)) - shape.radius;
                    hi = math.max(math.max(shape.a, shape.b), math.max(a1, b1)) + shape.radius;
                }

                lo -= radius + margin;
                hi += radius + margin;
                for (int s = 0; s < sampleVoxel.Length; s++)
                {
                    int voxel = sampleVoxel[s];
                    float4 a = weights[2 * s], b = weights[2 * s + 1];
                    float3 p = 0;
                    for (int corner = 0; corner < 4; corner++)
                        p += a[corner] * x[voxelNodes[voxel * 8 + corner]] + b[corner] * x[voxelNodes[voxel * 8 + corner + 4]];
                    if (math.all(p >= lo) && math.all(p <= hi))
                        pairs.Add(new int2(s, c));
                }
            }

            float h = dt / substeps, beta = .5f * damping;
            for (int step = 0; step < substeps; step++)
            {
                float t = (step + 1) * h;
                for (int m = 0; m < k; m++)
                {
                    begin[m] = q[m];
                    ModalIntegrator.Advance(omega[m], q[m], qd[m], h, beta, out float qm, out float vm);
                    q[m] = qm;
                    qd[m] = vm;
                    predicted[m] = q[m];
                }

                for (int p = 0; p < pairs.Length; p++)
                    Contact(pairs[p].x, pairs[p].y, t, h);
                for (int m = 0; m < k; m++)
                    qd[m] += (q[m] - predicted[m]) / h;
            }

            UpdateNodes();
            for (int i = 0; i < x.Length; i++)
                render[i] = new float4(x[i], 1);
        }

        void Contact(int s, int c, float t, float h)
        {
            float3x3 meff = effectiveMass[s];
            if (meff.c0.x == 0)
                return;
            float3 p = Sample(q, s), previous = Sample(begin, s);
            if (!ModalContactProjection.TryProject(shapes[c], p, previous, t, h, radius, friction, out var correction))
                return;
            // Minimum-norm mode update moving the sample by `correction`: dq = Phi_s^T Meff correction.
            float3 j = math.mul(meff, correction);
            int k = q.Length;
            for (int m = 0; m < k; m++)
                q[m] += math.dot(sampleModes[s * k + m], j);
            j /= h;
            float magnitudeJ = math.length(j);
            impulse[c] += j;
            moment[c] += magnitudeJ * p;
            magnitude[c] += magnitudeJ;
        }
    }
}
