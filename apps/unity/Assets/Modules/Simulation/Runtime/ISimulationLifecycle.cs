using PhysicsHands.GaussianData;

namespace PhysicsHands.Simulation
{
    // Rendering is initialized first. Shutdown also releases partial initialization and is idempotent.
    public interface ISimulationLifecycle
    {
        GaussianRenderAsset RenderAsset { get; }
        void Initialize(IGaussianBufferSource output);
        void Shutdown();
    }
}
