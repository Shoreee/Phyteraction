using System;
using PhysicsHands.GaussianData;
using PhysicsHands.Dynamics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Simulation
{
    internal sealed class MpmGpuResources : IDisposable
    {
        internal readonly ComputeShader cs;
        internal readonly CommandBuffer cmd;
        internal readonly ComputeBuffer particles, grid, blockFlag, blockList, counter, args, reaction, shapeBuffer, modifiers, cuboids, axles, splatParticle, splatOffset, splatNeighbors, splatWeights, particleRest;
        internal readonly int kP2G, kArgs, kGrid, kG2P, kClear, kDeform, particleGroups;
        internal readonly uint[] zeros = new uint[ContactGatherer.MaxShapes * 8];
        internal readonly float heaviest;
        bool disposed;
        internal MpmGpuResources(MpmModelAsset asset, MpmDiscretization layout, IGaussianBufferSource splats, ComputeShader solver, float friction, float skinCells, float maxPushSpeed, float maxMaterialSpeed)
        {
            try
            {
                cs = UnityEngine.Object.Instantiate(solver);
                cmd = new CommandBuffer{name = $"MPM {asset.scene}"};
                kP2G = cs.FindKernel("P2G");
                kArgs = cs.FindKernel("Args");
                kGrid = cs.FindKernel("Grid");
                kG2P = cs.FindKernel("G2P");
                kClear = cs.FindKernel("Clear");
                kDeform = cs.FindKernel("Deform");
                int n = layout.positions.Length, nodes = asset.gridN * asset.gridN * asset.gridN, blocks = nodes / 64;
                particles = new ComputeBuffer(n, 176);
                grid = new ComputeBuffer(nodes * 4, 4);
                grid.SetData(new uint[nodes * 4]);
                blockFlag = new ComputeBuffer(blocks, 4);
                blockFlag.SetData(new uint[blocks]);
                blockList = new ComputeBuffer(blocks, 4);
                counter = new ComputeBuffer(1, 4);
                counter.SetData(new uint[1]);
                args = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);
                args.SetData(new uint[]{0, 1, 1});
                reaction = new ComputeBuffer(zeros.Length, 4);
                shapeBuffer = new ComputeBuffer(ContactGatherer.MaxShapes, 128);
                modifiers = Buffer(asset.modifiers, 96);
                cuboids = Buffer(asset.cuboids, 48);
                axles = Buffer(asset.axles, 32);
                var bind = new uint[Mathf.Max(1, asset.dynamicCount)];
                var offs = new Vector4[bind.Length];
                for (int i = 0; i < asset.dynamicCount; i++)
                {
                    bind[i] = (uint)layout.binding[i];
                    offs[i] = layout.offsets[i];
                }

                BuildRenderNeighborhood(asset, layout, out var neighbors, out var weights);
                splatNeighbors = Buffer(neighbors, 16);
                splatWeights = Buffer(weights, 16);
                var rest = new Vector4[n];
                for (int i = 0; i < n; i++)
                    rest[i] = layout.positions[i];
                particleRest = Buffer(rest, 16);
                splatParticle = Buffer(bind, 4);
                splatOffset = Buffer(offs, 16);
                foreach (int k in new[]{kP2G, kArgs, kGrid, kG2P, kClear, kDeform})
                {
                    cs.SetBuffer(k, "_Particles", particles);
                    cs.SetBuffer(k, "_Grid", grid);
                    cs.SetBuffer(k, "_BlockFlag", blockFlag);
                    cs.SetBuffer(k, "_BlockList", blockList);
                    cs.SetBuffer(k, "_Counter", counter);
                    cs.SetBuffer(k, "_Args", args);
                    cs.SetBuffer(k, "_Reaction", reaction);
                    cs.SetBuffer(k, "_Shapes", shapeBuffer);
                    cs.SetBuffer(k, "_Modifiers", modifiers);
                    cs.SetBuffer(k, "_Cuboids", cuboids);
                    cs.SetBuffer(k, "_Axles", axles);
                    cs.SetBuffer(k, "_Source", splats.Source);
                    cs.SetBuffer(k, "_Deformed", splats.Deformed);
                    cs.SetBuffer(k, "_SplatParticle", splatParticle);
                    cs.SetBuffer(k, "_SplatOffset", splatOffset);
                    cs.SetBuffer(k, "_SplatNeighbors", splatNeighbors);
                    cs.SetBuffer(k, "_SplatWeights", splatWeights);
                    cs.SetBuffer(k, "_ParticleRest", particleRest);
                }

                float sinPhi = Mathf.Sin(asset.frictionAngle * Mathf.Deg2Rad);
                cs.SetInt("_ParticleCount", n);
                cs.SetInt("_GridN", asset.gridN);
                cs.SetInt("_ModifierCount", asset.modifiers.Length);
                cs.SetInt("_CuboidCount", asset.cuboids.Length);
                cs.SetInt("_AxleCount", asset.axles.Length);
                cs.SetInt("_Hardening", asset.hardening ? 1 : 0);
                cs.SetInt("_DynamicCount", asset.dynamicCount);
                cs.SetFloat("_Dx", asset.dx);
                cs.SetFloat("_InvDx", 1 / asset.dx);
                cs.SetFloat("_Viscosity", asset.viscosity);
                cs.SetFloat("_FrictionAlpha", Mathf.Sqrt(2f / 3f) * 2 * sinPhi / (3 - sinPhi));
                cs.SetFloat("_Cohesion", asset.cohesion);
                cs.SetFloat("_Xi", asset.xi);
                cs.SetFloat("_Friction", friction);
                cs.SetFloat("_Skin", skinCells * asset.dx);
                cs.SetFloat("_MaxPush", maxPushSpeed / asset.metersPerUnit);
                cs.SetFloat("_MaxParticleSpeed", maxMaterialSpeed / asset.metersPerUnit);
                float floor = float.MaxValue;
                foreach (var p in layout.positions)
                    floor = Mathf.Min(floor, Vector3.Dot(p, asset.up));
                cs.SetVector("_FloorNormal", asset.up);
                cs.SetFloat("_FloorOffset", floor - .5f * asset.dx);
                cs.SetMatrix("_LocalFromSim", asset.localFromSim);
                var basis = asset.localFromSim;
                basis.SetColumn(3, new Vector4(0, 0, 0, 1));
                for (int c = 0; c < 3; c++)
                    basis.SetColumn(c, basis.GetColumn(c) / asset.metersPerUnit);
                cs.SetMatrix("_Basis", basis);
                particleGroups = (layout.positions.Length + 63) / 64;
                foreach (float volume in layout.volume)
                    heaviest = Mathf.Max(heaviest, volume * asset.density);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        static ComputeBuffer Buffer<T>(T[] values, int stride)
            where T : struct
        {
            var buffer = new ComputeBuffer(Mathf.Max(1, values.Length), stride);
            try
            {
                if (values.Length > 0)
                    buffer.SetData(values);
                return buffer;
            }
            catch
            {
                buffer.Release();
                throw;
            }
        }

        static void BuildRenderNeighborhood(MpmModelAsset asset, MpmDiscretization layout, out Neighbor4[] neighbors, out Vector4[] weights)
        {
            int count = Mathf.Max(1, asset.dynamicCount);
            neighbors = new Neighbor4[count];
            weights = new Vector4[count];
            for (int i = 0; i < asset.dynamicCount; i++)
            {
                int particle = layout.binding[i];
                neighbors[i] = new Neighbor4(particle, particle, particle, particle);
                weights[i] = new Vector4(1, 0, 0, 0);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            cmd?.Release();
            UnityEngine.Object.Destroy(cs);
            foreach (var buffer in new[]{particles, grid, blockFlag, blockList, counter, args, reaction, shapeBuffer, modifiers, cuboids, axles, splatParticle, splatOffset, splatNeighbors, splatWeights, particleRest})
                buffer?.Release();
        }
    }
}
