using System;
using PhysicsHands.GaussianData;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Simulation
{
    internal readonly struct MpmStep
    {
        internal readonly int Count;
        internal readonly float Duration, Scale;
        internal MpmStep(int count, float duration, float scale)
        {
            Count = count;
            Duration = duration;
            Scale = scale;
        }
    }

    internal sealed class MpmTimeBudget
    {
        int Budget, minimumBudget, lastFrame;
        float frameMs, pending;
        internal void Initialize(int particles, int maxSubsteps)
        {
            Budget = Mathf.Clamp(500000 / Mathf.Max(1, particles), 1, maxSubsteps);
            minimumBudget = Budget;
            frameMs = 0;
            lastFrame = -1;
        }

        internal void ResetPending() => pending = 0;
        internal bool Accumulate(float dt, int frame, out float elapsed)
        {
            pending += dt;
            elapsed = 0;
            if (frame == lastFrame)
                return false;
            lastFrame = frame;
            elapsed = pending;
            pending = 0;
            return true;
        }

        internal void Adapt(float deltaTime, float targetFrameMs, float age, int maxSubsteps)
        {
            frameMs = Mathf.Lerp(frameMs <= 0 ? targetFrameMs : frameMs, deltaTime * 1000, .1f);
            if (age < 3)
                return;
            if (frameMs > targetFrameMs * 1.15f)
                // Total frame cost also contains rendering and XR. When those alone
                // exceed the target, reducing physics to one substep cannot fix the
                // bottleneck and starves interaction. Preserve the initialized work quota.
                Budget = Mathf.Max(minimumBudget, Mathf.FloorToInt(Budget * .8f));
            else if (frameMs < targetFrameMs * 1.02f)
                Budget = Mathf.Min(maxSubsteps, Budget + 1);
        }

        internal MpmStep Resolve(MpmModelAsset asset, bool replay, bool useReferenceConstitutive, bool fullSource, float dt, float cfl, float maxHandSpeed, float maxMaterialSpeed, int maxSubsteps, bool useAdaptiveBudget)
        {
            float meters = asset.metersPerUnit;
            int count;
            float scale;
            // Preserve the configured density and stiffness. When the GPU cannot finish the
            // CFL step, expose the reduced simulation time instead of changing the material.
            float hand = maxHandSpeed / meters, wave = asset.WaveSpeed;
            float speed = Mathf.Max(wave, hand, 1e-3f);
            float sourceStep = asset.substepDt > 0 ? asset.substepDt : 1e-4f;
            // The replay and reference regression keep the release's integrator.
            // Source-density interaction can use a CFL-bounded step, capped at
            // two reference steps; quadrature already fits the fine-step Budget.
            float stable = cfl * asset.dx / (speed + maxMaterialSpeed / meters);
            float cap = !replay && !useReferenceConstitutive && fullSource && (asset.material is MpmMaterial.Foam or MpmMaterial.Elastic) ? Mathf.Clamp(asset.interactionStepMultiplier, 1, 2) * sourceStep : sourceStep;
            float limit = Mathf.Min(stable, cap);
            // Match the released integrator's 1e-4 s substep. The former CFL-only
            // step could be over twice as long and destabilized stiff metal.
            count = Mathf.Max(1, Mathf.CeilToInt(dt / limit));
            // Enforce the exposed GPU Budget. Never enlarge the stable step to catch up.
            // The previous implementation adapted `Budget` but never used it.
            int needed = count;
            count = Mathf.Min(needed, Mathf.Clamp(useAdaptiveBudget ? Budget : maxSubsteps, 1, maxSubsteps));
            float h = dt / needed;
            scale = count / (float)needed;
            return new MpmStep(count, h, scale);
        }
    }
}
