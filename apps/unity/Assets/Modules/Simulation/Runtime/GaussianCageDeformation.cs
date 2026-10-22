using System;
using PhysicsHands.GaussianData;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace PhysicsHands.Simulation
{
    internal sealed class GaussianCageDeformation : IDisposable
    {
        readonly ComputeBuffer positions, voxelNodes, voxelOrigin, splatVoxel;
        readonly ComputeShader compute;
        readonly int deform, count;
        bool disposed;
        internal GaussianCageDeformation(ElasticCage cage, ComputeShader solver, IGaussianBufferSource splats, bool clampEmbedding = false)
        {
            count = cage.splatVoxel.Length;
            try
            {
                compute = UnityEngine.Object.Instantiate(solver);
                deform = compute.FindKernel("Deform");
                positions = new ComputeBuffer(cage.rest.Length, 16);
                voxelNodes = Buffer(cage.voxelNodes, 4);
                splatVoxel = Buffer(cage.splatVoxel, 4);
                var origins = new Vector4[cage.voxelOrigin.Length];
                for (int i = 0; i < origins.Length; i++)
                    origins[i] = cage.voxelOrigin[i];
                voxelOrigin = Buffer(origins, 16);
                compute.SetBuffer(deform, "_Position", positions);
                compute.SetBuffer(deform, "_VoxelNodes", voxelNodes);
                compute.SetBuffer(deform, "_VoxelOrigin", voxelOrigin);
                compute.SetBuffer(deform, "_SplatVoxel", splatVoxel);
                compute.SetBuffer(deform, "_Source", splats.Source);
                compute.SetBuffer(deform, "_Deformed", splats.Deformed);
                compute.SetInt("_SplatCount", count);
                compute.SetFloat("_Spacing", cage.spacing);
                compute.SetInt("_ClampEmbedding", clampEmbedding ? 1 : 0);
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
            var buffer = new ComputeBuffer(values.Length, stride);
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

        internal void Upload(NativeArray<float4> values)
        {
            positions.SetData(values);
            compute.Dispatch(deform, (count + 63) / 64, 1, 1);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            positions?.Release();
            voxelNodes?.Release();
            voxelOrigin?.Release();
            splatVoxel?.Release();
            UnityEngine.Object.Destroy(compute);
        }
    }
}
