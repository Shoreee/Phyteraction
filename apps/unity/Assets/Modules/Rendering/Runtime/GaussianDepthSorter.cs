using System;
using UnityEngine;
using Unity.Profiling;

namespace PhysicsHands.Rendering
{
    internal sealed class GaussianDepthSorter : IDisposable
    {
        internal ComputeBuffer Order => order;
        readonly ComputeBuffer order, keys, buckets;
        readonly ComputeShader sort;
        readonly int kClear, kHistogram, kScan, kScatter;
        readonly int[] kernels;
        static readonly ProfilerMarker sortMarker = new("PhysicsHands.Gaussian.Sort");
        const int Buckets = 1 << 16;
        bool disposed;
        ComputeBuffer boundDeformed;
        internal GaussianDepthSorter(int count, ComputeShader shader)
        {
            try
            {
                order = new ComputeBuffer(count, 4);
                order.SetData(new uint[count]);
                keys = new ComputeBuffer(count, 4);
                buckets = new ComputeBuffer(Buckets, 4);
                sort = UnityEngine.Object.Instantiate(shader);
                kClear = sort.FindKernel("Clear");
                kHistogram = sort.FindKernel("Histogram");
                kScan = sort.FindKernel("Scan");
                kScatter = sort.FindKernel("Scatter");
                kernels = new[]{kClear, kHistogram, kScan, kScatter};
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Sort(Camera camera, Transform owner, Bounds depthBounds, int visible, ComputeBuffer Deformed)
        {
            if (visible == 0)
                return;
            using var sample = sortMarker.Auto();
            var toWorld = owner.localToWorldMatrix;
            Vector3 eye = camera.transform.position, forward = camera.transform.forward;
            Vector3 lo = depthBounds.min, hi = depthBounds.max;
            float near = float.MaxValue, far = float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) != 0 ? hi.x : lo.x, (c & 2) != 0 ? hi.y : lo.y, (c & 4) != 0 ? hi.z : lo.z);
                float d = Vector3.Dot(toWorld.MultiplyPoint3x4(corner) - eye, forward);
                near = Mathf.Min(near, d);
                far = Mathf.Max(far, d);
            }

            if (boundDeformed != Deformed)
            {
                boundDeformed = Deformed;
                foreach (int k in kernels)
                {
                sort.SetBuffer(k, "_Deformed", Deformed);
                sort.SetBuffer(k, "_Order", order);
                sort.SetBuffer(k, "_Keys", keys);
                sort.SetBuffer(k, "_Buckets", buckets);
                }
            }

            sort.SetInt("_SplatCount", visible);
            sort.SetFloat("_DepthMin", near);
            sort.SetFloat("_DepthScale", Buckets / Mathf.Max(far - near, 1e-3f));
            sort.SetMatrix("_LocalToWorld", toWorld);
            sort.SetVector("_CameraPosition", eye);
            sort.SetVector("_CameraForward", forward);
            int groups = (visible + 255) / 256;
            sort.Dispatch(kClear, Buckets / 256, 1, 1);
            sort.Dispatch(kHistogram, groups, 1, 1);
            sort.Dispatch(kScan, 1, 1, 1);
            sort.Dispatch(kScatter, groups, 1, 1);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            order?.Release();
            keys?.Release();
            buckets?.Release();
            UnityEngine.Object.Destroy(sort);
        }
    }
}
