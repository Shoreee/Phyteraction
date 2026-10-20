using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.GaussianWorld
{
    // Back-to-front sorted Gaussian splats of one object. Solvers write `Deformed` (object space);
    // this sorts the visible prefix per camera and draws it with the splat shader.
    public sealed class SplatRenderer : IDisposable
    {
        // Off: only the moving part and its immediate support are drawn over passthrough.
        public static bool Backdrop;
        public readonly ComputeBuffer Source, Deformed;
        public readonly int Count, NearCount;
        public int Visible => Backdrop ? Count : NearCount;
        public ComputeBuffer OrderBuffer => order;
        readonly ComputeBuffer order, keys, buckets, sh;
        readonly ComputeShader sort;
        readonly Material material;
        readonly Transform owner;
        readonly Bounds bounds, depthBounds;
        readonly int kClear, kHistogram, kScan, kScatter;
        const int Buckets = 1 << 16;
        // Share a few power-of-two meshes. Drawing the full room mesh for the flower-only prefix
        // used to process hundreds of thousands of collapsed quads every eye, every frame.
        static readonly System.Collections.Generic.Dictionary<int, Mesh> quadMeshes = new();

        public SplatRenderer(Gaussian[] splats, Bounds bounds, ComputeShader sortShader, Shader splatShader, Transform owner, int nearCount = -1, Vector4[] shCoefficients = null, Matrix4x4 shFromLocal = default, int dynamicCount = -1)
        {
            this.owner = owner; Count = splats.Length; NearCount = nearCount < 0 ? Count : Mathf.Min(nearCount, Count);
            Source = new ComputeBuffer(Count, 80); Source.SetData(splats);
            Deformed = new ComputeBuffer(Count, 80); Deformed.SetData(splats);
            order = new ComputeBuffer(Count, 4); order.SetData(new uint[Count]); keys = new ComputeBuffer(Count, 4); buckets = new ComputeBuffer(Buckets, 4);
            sort = sortShader;
            kClear = sort.FindKernel("Clear"); kHistogram = sort.FindKernel("Histogram"); kScan = sort.FindKernel("Scan"); kScatter = sort.FindKernel("Scatter");
            material = new Material(splatShader) { enableInstancing = true };
            material.SetBuffer("_Deformed", Deformed); material.SetBuffer("_Order", order);
            material.SetInt("_DynamicCount", dynamicCount < 0 ? Count : dynamicCount);
            bool hasSh = shCoefficients != null && shCoefficients.Length == Count * 3;
            sh = new ComputeBuffer(hasSh ? Count * 3 : 3, 16); if (hasSh) sh.SetData(shCoefficients); else sh.SetData(new Vector4[3]);
            material.SetBuffer("_Sh", sh); material.SetInt("_ShEnabled", hasSh ? 1 : 0); material.SetMatrix("_ShFromLocal", hasSh ? shFromLocal : Matrix4x4.identity);
            EnsureQuads(Count);
            EnsureQuads(NearCount);
            // Deformed material can leave the rest bounds; depth keys clamp beyond half a metre.
            depthBounds = bounds; depthBounds.Expand(1);
            bounds.Expand(4); this.bounds = bounds;
            RenderPipelineManager.beginCameraRendering += Render;
        }
        static Mesh EnsureQuads(int count)
        {
            int n = Mathf.NextPowerOfTwo(Mathf.Max(count, 1 << 16));
            if (quadMeshes.TryGetValue(n, out var cached) && cached != null) return cached;
            var quads = new Mesh { name = $"Gaussian quads {n}", hideFlags = HideFlags.DontSave };
            quads.SetVertexBufferParams(n * 4, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.UNorm8, 4));
            quads.SetIndexBufferParams(n * 6, IndexFormat.UInt32);
            using (var indices = new NativeArray<uint>(n * 6, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
            {
                var view = indices;
                for (uint i = 0; i < n; i++)
                {
                    uint v = i * 4; int t = (int)i * 6;
                    view[t] = v; view[t + 1] = v + 1; view[t + 2] = v + 2; view[t + 3] = v; view[t + 4] = v + 2; view[t + 5] = v + 3;
                }
                quads.SetIndexBufferData(indices, 0, 0, n * 6, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            }
            quads.SetSubMesh(0, new SubMeshDescriptor(0, n * 6), MeshUpdateFlags.DontRecalculateBounds);
            quads.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f); quads.UploadMeshData(true);
            quadMeshes[n] = quads;
            return quads;
        }
        void Render(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.Preview || owner == null || !owner.gameObject.activeInHierarchy) return;
            Sort(camera);
            int visible = Visible;
            material.SetInt("_VisibleCount", visible);
            var matrix = owner.localToWorldMatrix;
            var b = GeometryUtility.CalculateBounds(new[] { bounds.min, bounds.max }, matrix);
            Graphics.RenderMesh(new RenderParams(material) { camera = camera, layer = owner.gameObject.layer, worldBounds = b, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, lightProbeUsage = LightProbeUsage.Off },
                EnsureQuads(visible), 0, matrix);
        }
        public void Sort(Camera camera)
        {
            int visible = Visible;
            if (visible == 0) return;
            var toWorld = owner.localToWorldMatrix; Vector3 eye = camera.transform.position, forward = camera.transform.forward;
            Vector3 lo = depthBounds.min, hi = depthBounds.max; float near = float.MaxValue, far = float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) != 0 ? hi.x : lo.x, (c & 2) != 0 ? hi.y : lo.y, (c & 4) != 0 ? hi.z : lo.z);
                float d = Vector3.Dot(toWorld.MultiplyPoint3x4(corner) - eye, forward); near = Mathf.Min(near, d); far = Mathf.Max(far, d);
            }
            foreach (int k in new[] { kClear, kHistogram, kScan, kScatter }) { sort.SetBuffer(k, "_Deformed", Deformed); sort.SetBuffer(k, "_Order", order); sort.SetBuffer(k, "_Keys", keys); sort.SetBuffer(k, "_Buckets", buckets); }
            sort.SetInt("_SplatCount", visible);
            sort.SetFloat("_DepthMin", near); sort.SetFloat("_DepthScale", Buckets / Mathf.Max(far - near, 1e-3f));
            sort.SetMatrix("_LocalToWorld", toWorld);
            sort.SetVector("_CameraPosition", eye); sort.SetVector("_CameraForward", forward);
            int groups = (visible + 255) / 256;
            sort.Dispatch(kClear, Buckets / 256, 1, 1);
            sort.Dispatch(kHistogram, groups, 1, 1);
            sort.Dispatch(kScan, 1, 1, 1);
            sort.Dispatch(kScatter, groups, 1, 1);
        }
        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= Render;
            Source.Release(); Deformed.Release(); order.Release(); keys.Release(); buckets.Release(); sh.Release();
            UnityEngine.Object.Destroy(material);
        }
    }
}
