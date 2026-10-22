using PhysicsHands.GaussianData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using PhysicsHands.Dynamics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
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
        [Header("Time stepping")]
        [Range(.05f, .9f)] public float cfl = .45f;
        public float MassScale
        {
            get;
            private set;
        }

        = 1;
        // Fixed-point momentum quantization range; not a material parameter.
        const float MomentumReferenceSpeed = 2;
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

        // Every fixed step advances its full physical duration.
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
        MpmRigidCoupling reactions;
        readonly Stopwatch watch = new();
        MpmGpuResources gpu;
        IGaussianBufferSource splats;
        readonly MpmStepScheduler scheduler = new();
        Matrix4x4 simFromLocal;
        bool dirty;
        bool pristine;
        Bounds restContactBounds;
        static readonly ProfilerMarker stepMarker = new("PhysicsHands.MPM.Step");
        static readonly ProfilerMarker readbackMarker = new("PhysicsHands.MPM.ContactWait");
        public bool AtUndisturbedRest => pristine && CanElideRest();
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
                reactions ??= new MpmRigidCoupling();
                gpu = new MpmGpuResources(asset, layout, splats, solver, friction, skinCells);
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
            replay = script;
            SimTime = 0;
            contacts.Reset();
            reactions.Reset();
            pristine = true;
            restContactBounds = new Bounds(layout.positions[0], Vector3.zero);
            foreach (var point in layout.positions) restContactBounds.Encapsulate(point);
            // Quadratic particle-to-grid support and the bone contact band.
            Vector3 minimum = restContactBounds.min, maximum = restContactBounds.max;
            float low = 3 * asset.dx, high = (asset.gridN - 4) * asset.dx;
            pristine = minimum.x >= low && minimum.y >= low && minimum.z >= low && maximum.x <= high && maximum.y <= high && maximum.z <= high;
            restContactBounds.Expand(2 * (2 + skinCells) * asset.dx);
            dirty = false; // Reset already restored the exact source Gaussian buffer.
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
            using var stepSample = stepMarker.Auto();
            if (activityEpoch != ApplicationActivity.Epoch)
            {
                activityEpoch = ApplicationActivity.Epoch;
                generation++;
                reactions.Reset();
                contacts.Reset();
            }
            float meters = asset.metersPerUnit;
            float dt = Time.fixedDeltaTime;
            watch.Restart();
            contacts.Policy = contactProvider;
            float extent = asset.gridN * asset.dx;
            var domain = new Bounds(Vector3.one * (extent * .5f), Vector3.one * (extent + 2 * skinCells * asset.dx));
            contacts.Begin(simFromLocal * transform.worldToLocalMatrix, 1 / meters,
                AtUndisturbedRest ? restContactBounds : domain);
            // Scripted paper playback has its own force/velocity boundary conditions.
            // Mixing live hands into it changes the reference trajectory.
            if (!replay)
            {
                contactProvider.Gather(contacts, dt);
                foreach (var c in probes)
                    contacts.AddCollider(c, dt);
            }

            // This is exact rest elision, not a lower solver rate: zero velocity,
            // identity strain and no gravity or moving boundary cannot evolve.
            // Once touched, never sleep a deformed state or discard its remaining motion.
            if (AtUndisturbedRest && contacts.Count == 0)
            {
                Substeps = PairCount = 0;
                ReactionForce = 0;
                SimTime += dt;
                SolverMs = watch.Elapsed.TotalMilliseconds;
                return;
            }
            pristine = false;

            for (int i = 0; i < contacts.Count; i++)
            {
                var s = contacts.Shapes[i];
                // Integrate the same upcoming interval as PhysX. Previous displayed poses
                // describe the past interval and otherwise delay contact by a fixed step.
                Vector3 a = (Vector3)(s.a + dt * s.velocityA), b = (Vector3)(s.b + dt * s.velocityB);
                shapes[i] = new ShapeGpu{a = new Vector4(a.x, a.y, a.z, s.radius), b = new Vector4(b.x, b.y, b.z, s.isBox), va = (Vector3)s.velocityA, vb = (Vector3)s.velocityB, previousA = (Vector3)s.a, previousB = (Vector3)s.b, ax = (Vector3)s.axisX, ay = (Vector3)s.axisY, az = (Vector3)s.axisZ, he = (Vector3)s.halfExtents, };
            }

            // Explicit MPM is stable for dt < cfl dx / max(elastic wave speed, fastest hand).
            MassScale = 1;
            var integration = scheduler.Resolve(asset, dt, cfl, ContactSpeed());
            Substeps = integration.Count;
            TimeScale = integration.Scale;
            float h = integration.Duration;
            int bodyCount = reactions.Prepare(contacts, shapes, asset, transform, dt);
            if (bodyCount > 0) gpu.couplingBodies.SetData(reactions.Bodies, 0, 0, bodyCount);
            MpmCommandEncoder.Encode(gpu, asset, shapes, contacts.Count, bodyCount, dt, integration, SimTime, MassScale, MomentumReferenceSpeed, replay, useReferenceConstitutive, gravity);
            Graphics.ExecuteCommandBuffer(gpu.cmd);
            int pairs; float force;
            using (readbackMarker.Auto()) reactions.Apply(gpu.reaction, asset, transform, contacts, dt, out pairs, out force);
            PairCount = pairs; ReactionForce = force;
            SimTime += h * Substeps;
            StepId++;
            dirty = true;
            SolverMs = watch.Elapsed.TotalMilliseconds;
        }

        bool CanElideRest()
        {
            if (replay || asset == null || asset.yieldStress < 0 || !asset.gravity.Equals(Vector3.zero)) return false;
            foreach (var boundary in asset.cuboids)
                if (!((Vector3)boundary.velocity).Equals(Vector3.zero)) return false;
            return true;
        }

        float ContactSpeed()
        {
            float speed = 0;
            for (int i = 0; i < contacts.Count; i++)
                speed = Mathf.Max(speed, math.length(contacts.Shapes[i].velocityA), math.length(contacts.Shapes[i].velocityB));
            return speed;
        }

        void Update()
        {
            if (!Ready || !ApplicationActivity.CanSubmitGpuWork)
                return;
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
