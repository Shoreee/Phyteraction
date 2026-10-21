using PhysicsHands.GaussianData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using PhysicsHands.Dynamics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PhysicsHands.Simulation
{
    // A PhysFlow scene on the GPU: MLS-MPM in PhysFlow's simulation space (units, seconds and
    // materials as released), hand bones as grid colliders with reactions back to PhysX, and the
    // reconstruction rendered as splats whose moving part rides on the particles.
    [DefaultExecutionOrder(300)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "MpmWorld")]
    public sealed class MpmSimulation : MonoBehaviour, ISimulationDiagnostics, ISimulationControl, ISimulationLifecycle
    {
        public MpmModelAsset asset;
        public ComputeShader solver;
        public MonoBehaviour contactSource;
        public Collider[] probes = Array.Empty<Collider>();
        [Header("Contact")]
        [Range(0, 2)]
        public float friction = .6f;
        [Tooltip("Contact band around bones, in grid cells.")]
        [Min(0)]
        public float skinCells = .5f;
        [Tooltip("Safety clamp against tracking jumps into the object.")]
        [Min(0)]
        public float maxReactionForce = 8;
        [Header("Time stepping")]
        [Range(.05f, .9f)]
        public float cfl = .45f;
        [Min(1)]
        public int maxSubsteps = 256;
        public bool useAdaptiveBudget = true;
        [Tooltip("Frame time the solver budget aims for (ms); past it fewer substeps run per step and the simulation slows instead of the frame rate.")]
        [Min(5)]
        public float targetFrameMs = 12;
        public float MassScale
        {
            get;
            private set;
        }

        = 1;
        [Tooltip("Hand speed the step must resolve (m/s); faster tracked bones are clamped.")]
        [Min(.1f)]
        public float maxHandSpeed = 1.5f;
        [Tooltip("Speed at which penetrated material is pushed out of a bone (m/s).")]
        [Min(.01f)]
        public float maxPushSpeed = 1;
        [Tooltip("Material speed cap (m/s): a safety net, far above anything a hand imparts.")]
        [Min(.1f)]
        public float maxMaterialSpeed = 2;
        [Header("Interaction (outside the paper replay)")]
        [Tooltip("Adds a support floor in interactive mode. Gravity itself comes from the released scene config.")]
        public bool gravity = true;
        [Tooltip("Runs PhysFlow's scripted interaction (impulses, enforced velocities, release) from the last reset.")]
        public bool replay;
        [Tooltip("Interactive MPM integrates occupied half-cells, preserving volume, boundary membership and all render Gaussians. Disable to compare full source particles. Paper replay always uses the full source.")]
        public bool useInteractionQuadrature = true;
        [NonSerialized]
        public bool useReferenceConstitutive;
        MpmDiscretization layout;
        int generation;
        int activityEpoch;
        bool reportedReadbackFailure;
        public int IntegrationParticleCount => layout?.positions.Length ?? 0;
        public bool Ready
        {
            get;
            private set;
        }

        public long StepId
        {
            get;
            private set;
        }

        public double SolverMs
        {
            get;
            private set;
        }

        public double ContactSampleTime => contacts.SampleTime;
        public int ShapeCount => contacts.Count;
        public int PairCount
        {
            get;
            private set;
        }

        public int ReactionReadbackFailures => reactions?.Failures ?? 0;

        public float ReactionForce
        {
            get;
            private set;
        }

        public float RmsDisplacement
        {
            get;
            private set;
        }

        public float StrainMeasure
        {
            get;
            private set;
        }

        public float SimTime
        {
            get;
            private set;
        }

        public int Substeps
        {
            get;
            private set;
        }

        // < 1 when the CFL limit needs more than maxSubsteps per fixed step (slow motion, never unstable).
        public float TimeScale
        {
            get;
            private set;
        }

        = 1;
        public string Provenance => asset != null ? asset.provenance : "";
        public string Summary => asset == null ? "" : $"mpm {asset.scene} material={asset.material} particles={IntegrationParticleCount} source_particles={asset.particles.Length} grid={asset.gridN}^3 dx={asset.dx:F4} splats={asset.renderData.splats.Length} dynamic={asset.dynamicCount} substeps={Substeps} time_scale={TimeScale:F2} mass_scale={MassScale:F1}";
        readonly ContactGatherer contacts = new();
        readonly ShapeGpu[] shapes = new ShapeGpu[ContactGatherer.MaxShapes];
        MpmReactionReadback reactions;
        readonly Stopwatch watch = new();
        MpmGpuResources gpu;
        IGaussianBufferSource splats;
        readonly MpmTimeBudget timeBudget = new();
        Matrix4x4 simFromLocal;
        bool dirty;
        ISimulationContactSource contactProvider;
        public GaussianRenderAsset RenderAsset => asset != null ? asset.renderData : null;

        public void Initialize(IGaussianBufferSource output)
        {
            if (Ready)
                return;
            try
            {
                contactProvider = contactSource as ISimulationContactSource
                    ?? throw new InvalidOperationException($"{name}: contactSource must implement ISimulationContactSource.");
                splats = output ?? throw new InvalidOperationException($"{name}: Gaussian buffers must be supplied before simulation starts.");
                if (asset == null || asset.renderData == null || asset.renderData.splats == null || asset.particles == null || asset.particles.Length == 0 || solver == null || !SystemInfo.supportsComputeShaders)
                {
                    throw new InvalidOperationException($"{name}: MPM world requires a PhysFlow scene asset and a compute-capable GPU.");
                }

                if (splats.Count != asset.renderData.splats.Length || splats.Source == null || splats.Deformed == null)
                    throw new InvalidOperationException($"{name}: Gaussian buffers do not match the model's render data.");

                if ((transform.lossyScale - Vector3.one).sqrMagnitude > 1e-6f)
                {
                    throw new InvalidOperationException($"{name}: MPM world must have unit world scale.");
                }

                layout = new MpmDiscretization(asset, FullSourceFor(replay), asset.integrationSubdivisions);
                generation++;
                activityEpoch = ApplicationActivity.Epoch;
                reportedReadbackFailure = false;
                reactions ??= new MpmReactionReadback(epoch => this != null && Ready && epoch == generation && activityEpoch == ApplicationActivity.Epoch && ApplicationActivity.CanSubmitGpuWork);
                gpu = new MpmGpuResources(asset, layout, splats, solver, friction, skinCells, maxPushSpeed, maxMaterialSpeed);
                timeBudget.Initialize(layout.positions.Length, maxSubsteps);
                simFromLocal = asset.localFromSim.inverse;
                Ready = true;
                Restart(replay);
                Debug.Log($"MPM {asset.scene}: {Summary}, wave speed {asset.WaveSpeed:F1} units/s, E {asset.youngs:G3} Pa, nu {asset.poisson}, rho {asset.density}");
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        bool FullSourceFor(bool script) => script || !useInteractionQuadrature || !asset.allowInteractionQuadrature;
        public bool SupportsReplay => true;
        public void ResetSimulation() => Restart(false);
        // PhysFlow's scripted interaction from rest, as in the paper's videos.
        public void Replay() => Restart(true);
        void Restart(bool script)
        {
            if (!Ready || !ApplicationActivity.CanSubmitGpuWork)
                return;
            if (layout.fullSource != FullSourceFor(script))
            {
                Rebuild(script);
                return;
            }

            generation++;
            var p = MpmParticleInitializer.Create(asset, layout);
            gpu.particles.SetData(p);
            // Restore the displayed buffer immediately as well as the simulation state.
            // Otherwise reset can leave torn-looking splats on screen until the next update.
            splats.Deformed.SetData(asset.renderData.splats);
            splats.MarkDeformed();
            timeBudget.ResetPending();
            replay = script;
            SimTime = 0;
            contacts.Reset();
            reactions.Reset();
            dirty = true;
        }

        void Rebuild(bool script)
        {
            var output = splats;
            Shutdown();
            replay = script;
            Initialize(output);
        }

        void FixedUpdate()
        {
            if (!Ready || !ApplicationActivity.CanSubmitGpuWork)
                return;
            if (activityEpoch != ApplicationActivity.Epoch)
            {
                activityEpoch = ApplicationActivity.Epoch;
                generation++;
                reactions.Reset();
                contacts.Reset();
                timeBudget.ResetPending();
            }
            float meters = asset.metersPerUnit;
            ApplyReactions(Time.fixedDeltaTime);
            if (!timeBudget.Accumulate(Time.fixedDeltaTime, Time.frameCount, out float dt))
                return;
            watch.Restart();
            contacts.Policy = contactProvider;
            contacts.Begin(simFromLocal * transform.worldToLocalMatrix, 1 / meters);
            // Scripted paper playback has its own force/velocity boundary conditions.
            // Mixing live hands into it changes the reference trajectory.
            if (!replay)
            {
                contactProvider.Gather(contacts, dt);
                foreach (var c in probes)
                    contacts.AddCollider(c, dt);
            }

            for (int i = 0; i < contacts.Count; i++)
            {
                var s = contacts.Shapes[i];
                shapes[i] = new ShapeGpu{a = new Vector4(s.a.x, s.a.y, s.a.z, s.radius), b = new Vector4(s.b.x, s.b.y, s.b.z, s.isBox), va = Clamp(s.velocityA, maxHandSpeed / meters), vb = Clamp(s.velocityB, maxHandSpeed / meters), ax = (Vector3)s.axisX, ay = (Vector3)s.axisY, az = (Vector3)s.axisZ, he = (Vector3)s.halfExtents, };
            }

            // Explicit MPM is stable for dt < cfl dx / max(elastic wave speed, fastest hand).
            MassScale = 1;
            var integration = timeBudget.Resolve(asset, replay, useReferenceConstitutive, layout.fullSource, dt, cfl, maxHandSpeed, maxMaterialSpeed, maxSubsteps, useAdaptiveBudget);
            Substeps = integration.Count;
            TimeScale = integration.Scale;
            float h = integration.Duration;
            MpmCommandEncoder.Encode(gpu, asset, shapes, contacts.Count, dt, integration, SimTime, MassScale, maxMaterialSpeed, replay, useReferenceConstitutive, gravity);
            long step = StepId;
            int epoch = generation;
            if (contacts.Count > 0)
            {
                reactions.Queue(gpu.cmd, gpu.reaction, step, epoch, contacts, h * Substeps, dt);
            }

            Graphics.ExecuteCommandBuffer(gpu.cmd);
            SimTime += h * Substeps;
            StepId++;
            dirty = true;
            SolverMs = watch.Elapsed.TotalMilliseconds;
        }

        // Tracking glitches report bone speeds no hand reaches; they would fling material.
        static Vector4 Clamp(float3 v, float limit)
        {
            float l = math.length(v);
            return (Vector3)(l > limit ? v * (limit / l) : v);
        }

        // Reactions arrive one or more frames late; each is applied to the bodies of its own step.
        void ApplyReactions(float dt)
        {
            if (reactions.Failures > 0 && !reportedReadbackFailure)
            {
                reportedReadbackFailure = true;
                Debug.LogError($"{name}: GPU reaction readback failed; hand feedback is incomplete. Check Metal errors and foreground state.", this);
            }
            if (!reactions.HasFeedback)
            {
                PairCount = 0;
                ReactionForce = 0;
                return;
            }
            reactions.Apply(asset, transform, contacts, maxReactionForce, dt, out int pairs, out float force);
            PairCount = pairs;
            ReactionForce = force;
        }

        void Update()
        {
            if (!Ready || !ApplicationActivity.CanSubmitGpuWork)
                return;
            timeBudget.Adapt(Time.unscaledDeltaTime, targetFrameMs, Time.timeSinceLevelLoad, maxSubsteps);
            if (!dirty)
                return;
            gpu.cs.SetInt("_RenderTotal", asset.UsesTotalDeformation ? 1 : 0);
            if (asset.dynamicCount > 0)
            {
                gpu.cs.Dispatch(gpu.kDeform, (asset.dynamicCount + 63) / 64, 1, 1);
                splats.MarkDeformed();
            }
            dirty = false;
        }

        public void RequestQuality()
        {
            if (!Ready || !ApplicationActivity.CanSubmitGpuWork)
                return;
            int epoch = generation, activity = ApplicationActivity.Epoch;
            AsyncGPUReadback.Request(gpu.particles, r =>
            {
                if (r.hasError || this == null || !Ready || epoch != generation || activity != ApplicationActivity.Epoch || !ApplicationActivity.CanSubmitGpuWork)
                    return;
                MpmStatistics.Measure(r.GetData<Particle>(), layout.positions, asset.metersPerUnit, out float rms, out float strain);
                RmsDisplacement = rms;
                StrainMeasure = strain;
            }

            );
        }

        void OnDisable() => Shutdown();
        void OnDestroy() => Shutdown();

        public void Shutdown()
        {
            Ready = false;
            generation++;
            reactions?.Reset();
            gpu?.Dispose();
            gpu = null;
            layout = null;
            splats = null;
            contactProvider = null;
        }
    }
}
