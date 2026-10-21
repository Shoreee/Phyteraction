using PhysicsHands.GaussianData;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Rendering
{
    // Back-to-front sorted Gaussian splats of one object. Solvers write `Deformed` (object space);
    // this sorts the visible prefix per camera and draws it with the splat shader.
    public sealed class SplatRenderer : IDisposable
    {
        // Off: only the moving part and its immediate support are drawn over passthrough.
        public bool Backdrop;
        public bool ReuseParallelEyeSort = true;
        public ComputeBuffer Source => buffers.Source;
        public ComputeBuffer Deformed => buffers.Deformed;
        public readonly int Count, NearCount;
        public int Visible => Backdrop ? Count : NearCount;
        public ComputeBuffer OrderBuffer => sorter.Order;
        readonly GaussianGpuBuffers buffers;
        readonly GaussianDepthSorter sorter;
        readonly Material material;
        readonly Transform owner;
        readonly Bounds bounds, depthBounds;
        // One indexed quad is instanced once per Gaussian. No padded multi-million
        // quad meshes, and no source pruning or quality change.
        static Mesh quad;
        static int quadUsers;
        bool disposed, quadAcquired;
        int sortedCount;
        uint deformationVersion, sortedVersion;
        bool hasSorted;
        public void MarkDeformed() => deformationVersion++;
        Vector3 sortedForward;
        Matrix4x4 sortedTransform;
        public SplatRenderer(Gaussian[] splats, Bounds bounds, ComputeShader sortShader, Shader splatShader, Transform owner, int nearCount = -1, Vector4[] shCoefficients = null, Matrix4x4 shFromLocal = default, int dynamicCount = -1)
        {
            this.owner = owner;
            Count = splats.Length;
            NearCount = nearCount < 0 ? Count : Mathf.Min(nearCount, Count);
            try
            {
                buffers = new GaussianGpuBuffers(splats, shCoefficients);
                sorter = new GaussianDepthSorter(Count, sortShader);
                material = new Material(splatShader)
                {enableInstancing = true};
                material.SetBuffer("_Deformed", Deformed);
                material.SetBuffer("_Order", sorter.Order);
                material.SetInt("_DynamicCount", dynamicCount < 0 ? Count : dynamicCount);
                bool hasSh = buffers.HasSh;
                material.SetBuffer("_Sh", buffers.Sh);
                material.SetInt("_ShEnabled", hasSh ? 1 : 0);
                material.SetMatrix("_ShFromLocal", hasSh ? shFromLocal : Matrix4x4.identity);
                EnsureQuad();
                quadUsers++;
                quadAcquired = true;
                // Deformed material can leave the rest bounds; depth keys clamp beyond half a metre.
                depthBounds = bounds;
                depthBounds.Expand(1);
                bounds.Expand(4);
                this.bounds = bounds;
                RenderPipelineManager.beginCameraRendering += Render;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        static Mesh EnsureQuad()
        {
            if (quad != null)
                return quad;
            quad = new Mesh{name = "Instanced Gaussian quad", hideFlags = HideFlags.DontSave};
            quad.vertices = new[]{Vector3.zero, Vector3.right, Vector3.one, Vector3.up};
            quad.triangles = new[]{0, 1, 2, 0, 2, 3};
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
            quad.UploadMeshData(true);
            return quad;
        }

        void Render(ScriptableRenderContext context, Camera camera)
        {
            if (!ApplicationActivity.CanSubmitGpuWork || camera.cameraType == CameraType.Preview || owner == null || !owner.gameObject.activeInHierarchy)
                return;
            if ((camera.cullingMask & (1 << owner.gameObject.layer)) == 0)
                return;
            int visible = Visible;
            var matrix = owner.localToWorldMatrix;
            // Parallel views differ by a constant depth offset. Reuse only while
            // orientation, transform, count and explicitly versioned deformation are unchanged.
            if (!ReuseParallelEyeSort || !hasSorted || sortedVersion != deformationVersion || sortedCount != visible || !sortedTransform.Equals(matrix) || !sortedForward.Equals(camera.transform.forward))
            {
                Sort(camera);
                hasSorted = true;
                sortedVersion = deformationVersion;
                sortedCount = visible;
                sortedTransform = matrix;
                sortedForward = camera.transform.forward;
            }

            material.SetInt("_VisibleCount", visible);
            material.SetMatrix("_SplatLocalToWorld", matrix);
            material.SetMatrix("_SplatWorldToLocal", owner.worldToLocalMatrix);
            Vector3 e = bounds.extents;
            Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
            var b = new Bounds(matrix.MultiplyPoint3x4(bounds.center), 2 * new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
            Graphics.RenderMeshPrimitives(new RenderParams(material)
            {camera = camera, layer = owner.gameObject.layer, worldBounds = b, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, lightProbeUsage = LightProbeUsage.Off}, EnsureQuad(), 0, visible);
        }

        public void Sort(Camera camera)
        {
            if (ApplicationActivity.CanSubmitGpuWork) sorter.Sort(camera, owner, depthBounds, Visible, Deformed);
        }
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            RenderPipelineManager.beginCameraRendering -= Render;
            buffers?.Dispose();
            sorter?.Dispose();
            UnityEngine.Object.Destroy(material);
            if (quadAcquired && --quadUsers == 0)
            {
                UnityEngine.Object.Destroy(quad);
                quad = null;
            }

            quadAcquired = false;
        }
    }
}
