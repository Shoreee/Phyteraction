using PhysicsHands.Dynamics;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PhysicsHands.Simulation
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
        bool disposed;
        public ModalSolver(ElasticCage cage, float[] modeOmega, float[] modeShapes)
        {
            int n = cage.rest.Length, k = ModeCount = modeOmega.Length;
            if (modeShapes.Length != n * 3 * k)
                throw new ArgumentException("Mode shapes do not match the cage.");
            try
            {
                Rest = new NativeArray<float3>(n, Allocator.Persistent);
                for (int i = 0; i < n; i++)
                    Rest[i] = cage.rest[i];
                Positions = new NativeArray<float3>(Rest, Allocator.Persistent);
                RenderPositions = new NativeArray<float4>(n, Allocator.Persistent);
                omega = new NativeArray<float>(modeOmega, Allocator.Persistent);
                Q = new NativeArray<float>(k, Allocator.Persistent);
                QDot = new NativeArray<float>(k, Allocator.Persistent);
                predicted = new NativeArray<float>(k, Allocator.Persistent);
                begin = new NativeArray<float>(k, Allocator.Persistent);
                nodeModes = new NativeArray<float3>(n * k, Allocator.Persistent);
                for (int i = 0; i < n; i++)
                    for (int m = 0; m < k; m++)
                        nodeModes[i * k + m] = new float3(modeShapes[(i * 3) * k + m], modeShapes[(i * 3 + 1) * k + m], modeShapes[(i * 3 + 2) * k + m]);
                int samples = cage.sampleVoxel.Length;
                voxelNodes = new NativeArray<int>(cage.voxelNodes, Allocator.Persistent);
                sampleVoxel = new NativeArray<int>(cage.sampleVoxel, Allocator.Persistent);
                sampleWeights = new NativeArray<float4>(cage.sampleWeights.Length, Allocator.Persistent);
                for (int i = 0; i < sampleWeights.Length; i++)
                    sampleWeights[i] = cage.sampleWeights[i];
                sampleModes = new NativeArray<float3>(samples * k, Allocator.Persistent);
                sampleRest = new NativeArray<float3>(samples, Allocator.Persistent);
                sampleEffectiveMass = new NativeArray<float3x3>(samples, Allocator.Persistent);
                for (int s = 0; s < samples; s++)
                {
                    int voxel = cage.sampleVoxel[s];
                    float3 p = 0;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        int node = cage.voxelNodes[voxel * 8 + corner];
                        float w = cage.sampleWeights[2 * s + corner / 4][corner % 4];
                        p += w * (float3)cage.rest[node];
                        for (int m = 0; m < k; m++)
                            sampleModes[s * k + m] += w * nodeModes[node * k + m];
                    }

                    sampleRest[s] = p;
                    float3x3 inverseMass = float3x3.zero;
                    for (int m = 0; m < k; m++)
                    {
                        float3 phi = sampleModes[s * k + m];
                        inverseMass += new float3x3(phi * phi.x, phi * phi.y, phi * phi.z);
                    }

                    // Directions the modes cannot move are rigid; regularise so they carry a large but finite mass.
                    float trace = inverseMass.c0.x + inverseMass.c1.y + inverseMass.c2.z;
                    sampleEffectiveMass[s] = trace > 1e-12f ? math.inverse(inverseMass + float3x3.identity * (1e-3f * trace)) : float3x3.zero;
                }

                Shapes = new NativeArray<ContactShape>(MaxShapes, Allocator.Persistent);
                Impulse = new NativeArray<float3>(MaxShapes, Allocator.Persistent);
                ImpulseMoment = new NativeArray<float3>(MaxShapes, Allocator.Persistent);
                ImpulseMagnitude = new NativeArray<float>(MaxShapes, Allocator.Persistent);
                pairs = new NativeList<int2>(1024, Allocator.Persistent);
                Reset();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Reset()
        {
            for (int m = 0; m < ModeCount; m++)
                Q[m] = QDot[m] = 0;
            Positions.CopyFrom(Rest);
            for (int i = 0; i < Rest.Length; i++)
                RenderPositions[i] = new float4(Rest[i], 1);
        }

        public bool DeformationChanged { get; private set; }
        float[] previousQ;

        public void Step(int shapeCount, float dt, int substeps, float damping, float friction, float sampleRadius)
        {
            previousQ ??= new float[ModeCount];
            Q.CopyTo(previousQ);
            new ModalStepJob{q = Q, qd = QDot, predicted = predicted, begin = begin, omega = omega, nodeModes = nodeModes, rest = Rest, x = Positions, render = RenderPositions, sampleModes = sampleModes, sampleRest = sampleRest, effectiveMass = sampleEffectiveMass, voxelNodes = voxelNodes, sampleVoxel = sampleVoxel, weights = sampleWeights, shapes = Shapes, shapeCount = math.min(shapeCount, MaxShapes), pairs = pairs, impulse = Impulse, moment = ImpulseMoment, magnitude = ImpulseMagnitude, dt = dt, substeps = math.max(1, substeps), damping = damping, friction = friction, radius = sampleRadius, }.Run();
            DeformationChanged = false;
            for (int i=0;i<ModeCount;i++) if (Q[i] != previousQ[i]) { DeformationChanged = true; break; }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (Positions.IsCreated)
                Positions.Dispose();
            if (Rest.IsCreated)
                Rest.Dispose();
            if (RenderPositions.IsCreated)
                RenderPositions.Dispose();
            if (Q.IsCreated)
                Q.Dispose();
            if (QDot.IsCreated)
                QDot.Dispose();
            if (predicted.IsCreated)
                predicted.Dispose();
            if (begin.IsCreated)
                begin.Dispose();
            if (omega.IsCreated)
                omega.Dispose();
            if (nodeModes.IsCreated)
                nodeModes.Dispose();
            if (sampleModes.IsCreated)
                sampleModes.Dispose();
            if (sampleRest.IsCreated)
                sampleRest.Dispose();
            if (sampleEffectiveMass.IsCreated)
                sampleEffectiveMass.Dispose();
            if (voxelNodes.IsCreated)
                voxelNodes.Dispose();
            if (sampleVoxel.IsCreated)
                sampleVoxel.Dispose();
            if (sampleWeights.IsCreated)
                sampleWeights.Dispose();
            if (Shapes.IsCreated)
                Shapes.Dispose();
            if (Impulse.IsCreated)
                Impulse.Dispose();
            if (ImpulseMoment.IsCreated)
                ImpulseMoment.Dispose();
            if (ImpulseMagnitude.IsCreated)
                ImpulseMagnitude.Dispose();
            if (pairs.IsCreated)
                pairs.Dispose();
        }
    }
}
