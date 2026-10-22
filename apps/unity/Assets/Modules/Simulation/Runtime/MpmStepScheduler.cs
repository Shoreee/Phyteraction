using UnityEngine;
using PhysicsHands.GaussianData;

namespace PhysicsHands.Simulation
{
    internal readonly struct MpmStep
    {
        internal readonly int Count;
        internal readonly float Duration, Scale;
        internal MpmStep(int count, float duration, float scale) { Count = count; Duration = duration; Scale = scale; }
    }
    internal sealed class MpmStepScheduler
    {
        internal MpmStep Resolve(MpmModelAsset asset, float dt, float cfl, float contactSpeed)
        {
            // The source material controls elastic wave speed. Contact velocity uses
            // the measured shape speed, not an authored per-scene speed cap.
            float stable = cfl * asset.dx / Mathf.Max(asset.WaveSpeed + contactSpeed, 1e-6f);
            float limit = Mathf.Min(stable, asset.substepDt);
            int count = Mathf.Max(1, Mathf.CeilToInt(dt / limit));
            return new MpmStep(count, dt / count, 1);
        }
    }
}
