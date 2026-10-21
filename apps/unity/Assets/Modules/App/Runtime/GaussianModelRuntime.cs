using System;
using PhysicsHands.Simulation;
using UnityEngine;

namespace PhysicsHands.App
{
    // Owns the dependency order for one model, including failed initialization.
    [DefaultExecutionOrder(250)]
    public sealed class GaussianModelRuntime : MonoBehaviour
    {
        public GaussianRenderBinding rendering;
        public MonoBehaviour simulation;
        ISimulationLifecycle lifecycle;

        void OnEnable()
        {
            try
            {
                lifecycle = simulation as ISimulationLifecycle
                    ?? throw new InvalidOperationException($"{name}: simulation must implement ISimulationLifecycle.");
                // Their configured enabled state is valid even before their Awake callbacks run.
                if (rendering == null || !rendering.enabled || !simulation.enabled || rendering.gameObject != gameObject || simulation.gameObject != gameObject)
                    throw new InvalidOperationException($"{name}: rendering and simulation must be assigned, enabled and attached to this model.");
                if (lifecycle.RenderAsset == null || rendering.asset != lifecycle.RenderAsset)
                    throw new InvalidOperationException($"{name}: rendering and simulation must reference the same Gaussian render asset.");
                rendering.Initialize();
                lifecycle.Initialize(rendering);
            }
            catch (Exception exception)
            {
                Shutdown();
                Debug.LogException(exception, this);
                enabled = false;
            }
        }

        void Shutdown()
        {
            try { lifecycle?.Shutdown(); }
            finally
            {
                lifecycle = null;
                if (rendering != null) rendering.Shutdown();
            }
        }

        void OnDisable() => Shutdown();
        void OnDestroy() => Shutdown();
    }
}
