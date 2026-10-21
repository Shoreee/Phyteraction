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
            get => renderer != null && renderer.Backdrop;
            set
            {
                RequireRenderer().Backdrop = value;
            }
        }

        SplatRenderer renderer;
        public ComputeBuffer Source => RequireRenderer().Source;
        public ComputeBuffer Deformed => RequireRenderer().Deformed;
        public int Count => asset != null && asset.splats != null ? asset.splats.Length : 0;

        SplatRenderer RequireRenderer() => renderer ?? throw new System.InvalidOperationException($"Gaussian render binding on {name} is not initialized.");

        public void Initialize()
        {
            if (renderer != null)
                return;
            if (asset == null || asset.splats == null || asset.splats.Length == 0 || sortShader == null || splatShader == null)
                throw new System.InvalidOperationException($"Incomplete Gaussian render binding on {name}.");
            renderer = new SplatRenderer(asset.splats, asset.bounds, sortShader, splatShader, transform, nearCount, asset.sh, asset.shFromLocal, dynamicCount);
        }

        public void Shutdown()
        {
            renderer?.Dispose();
            renderer = null;
        }

        void OnDestroy() => Shutdown();
    }
}
