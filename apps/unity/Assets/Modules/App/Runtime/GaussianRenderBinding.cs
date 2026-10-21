using PhysicsHands.GaussianData;
using PhysicsHands.Rendering;
using UnityEngine;

namespace PhysicsHands.App
{
    [DefaultExecutionOrder(250)]
    public sealed class GaussianRenderBinding : MonoBehaviour, IGaussianBufferSource
    {
        public GaussianRenderAsset asset;
        public ComputeShader sortShader;
        public Shader splatShader;
        public int nearCount = -1;
        public int dynamicCount = -1;
        public bool Backdrop
        {
            get => backdrop;
            set
            {
                backdrop = value;
                if (gpuRenderer != null) gpuRenderer.Backdrop = value;
            }
        }

        bool backdrop;
        SplatRenderer gpuRenderer;
        public void MarkDeformed() => RequireRenderer().MarkDeformed();
        public ComputeBuffer Source => RequireRenderer().Source;
        public ComputeBuffer Deformed => RequireRenderer().Deformed;
        public int Count => asset != null && asset.splats != null ? asset.splats.Length : 0;

        SplatRenderer RequireRenderer() => gpuRenderer ?? throw new System.InvalidOperationException($"Gaussian render binding on {name} is not initialized.");

        public void Initialize()
        {
            if (gpuRenderer != null)
                return;
            if (asset == null || asset.splats == null || asset.splats.Length == 0 || sortShader == null || splatShader == null)
                throw new System.InvalidOperationException($"Incomplete Gaussian render binding on {name}.");
            gpuRenderer = new SplatRenderer(asset.splats, asset.bounds, sortShader, splatShader, transform, nearCount, asset.sh, asset.shFromLocal, dynamicCount);
            gpuRenderer.Backdrop = backdrop;
        }

        public void Shutdown()
        {
            gpuRenderer?.Dispose();
            gpuRenderer = null;
        }

        void OnDestroy() => Shutdown();
    }
}
