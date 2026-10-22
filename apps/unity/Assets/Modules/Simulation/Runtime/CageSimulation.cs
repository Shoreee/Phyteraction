using System;
using System.Diagnostics;
using PhysicsHands.Dynamics;
using PhysicsHands.GaussianData;
using Unity.Mathematics;
using UnityEngine;
using Unity.Profiling;

namespace PhysicsHands.Simulation
{
    [DefaultExecutionOrder(300)]
    public sealed class CageSimulation : MonoBehaviour, ISimulationLifecycle, ISimulationControl, ISimulationDiagnostics
    {
        public MpmModelAsset source;
        public ComputeShader deformationShader;
        public MonoBehaviour contactSource;
        public Collider[] probes = Array.Empty<Collider>();
        [Range(12, 48)] public int resolution = 24;
        [Range(1, 8)] public int substeps = 1;
        [Range(1, 8)] public int iterations = 2;
        [Range(0, 1)] public float friction = .6f;
        public ElasticCage Cage { get; private set; }
        public CageSolver Solver { get; private set; }
        public bool Ready { get; private set; }
        public GaussianRenderAsset RenderAsset => source != null ? source.renderData : null;
        public bool SupportsReplay => false;
        public long StepId { get; private set; }
        public double SolverMs { get; private set; }
        public double ContactSampleTime => contacts.SampleTime;
        public int ShapeCount => contacts.Count;
        public int PairCount => Solver?.PairCount ?? 0;
        public float ReactionForce { get; private set; }
        public float RmsDisplacement { get; private set; }
        public float StrainMeasure { get; private set; }
        public int Substeps => substeps;
        public float TimeScale => 1;
        public string Provenance => source == null ? "" : source.materialOrigin + "; non-destructive elasticity around the captured, load-balanced rest shape";
        public string Summary => Cage == null ? "" : $"elastic XPBD nodes={Cage.rest.Length} tets={Cage.tets.Length} surface_triangles={Solver.SurfaceCount}; no fracture/plastic flow";
        readonly ContactGatherer contacts = new();
        readonly Stopwatch watch = new();
        static readonly ProfilerMarker stepMarker = new("PhysicsHands.Cage.Step");
        ISimulationContactSource contactProvider;
        IGaussianBufferSource output;
        GaussianCageDeformation deformation;
        bool dirty;
        public void Initialize(IGaussianBufferSource rendering)
        {
            if (Ready) return;
            try
            {
                contactProvider = contactSource as ISimulationContactSource ?? throw new InvalidOperationException($"{name}: missing cage contact source.");
                if (source == null || source.renderData == null || deformationShader == null || rendering == null || rendering.Count != source.renderData.splats.Length)
                    throw new InvalidOperationException($"{name}: incomplete cage model/render binding.");
                if ((transform.lossyScale - Vector3.one).sqrMagnitude > 1e-6f) throw new InvalidOperationException($"{name}: cage requires metric unit world scale.");
                output = rendering;
                Cage = PhysFlowCageBuilder.Build(source, resolution);
                Solver = PhysFlowCageBuilder.CreateSolver(source, Cage);
                deformation = new GaussianCageDeformation(Cage, deformationShader, rendering, clampEmbedding: true);
                contacts.Reset(); StepId = 0; dirty = false; Ready = true;
            }
            catch { Shutdown(); throw; }
        }
        public void ResetSimulation()
        {
            if (!Ready) return;
            Solver.Reset(); contacts.Reset(); dirty = true;
        }
        public void Replay() => ResetSimulation();
        void FixedUpdate()
        {
            if (!Ready || !ApplicationActivity.CanSubmitGpuWork) return;
            using var marker = stepMarker.Auto(); watch.Restart();
            float dt = Time.fixedDeltaTime;
            contacts.Policy = contactProvider;
            var region = new Bounds(Cage.rest[0], Vector3.zero);
            var positions = Solver.Positions;
            for (int i = 0; i < positions.Length; i++) region.Encapsulate((Vector3)positions[i]);
            region.Expand(2 * Cage.spacing);
            contacts.Begin(transform.worldToLocalMatrix, 1, region);
            contactProvider.Gather(contacts, dt);
            foreach (var probe in probes) contacts.AddCollider(probe, dt);
            // Captured anchored geometry is the loaded equilibrium for this approximation.
            // Reapplying the sand replay's collapse load would destroy that equilibrium despite
            // disabling particle release. Free bodies retain the source gravity calibration.
            Vector3 gravity = Cage.anchoredNodes > 0 ? Vector3.zero : source.localFromSim.MultiplyVector(source.gravity);
            Solver.Step(contacts, transform, dt, substeps, iterations, friction, Cage.spacing * .025f, source.dampingRate, gravity);
            ReactionForce = Solver.ApplyReactions(contacts, transform, dt);
            dirty |= Solver.DeformationChanged; StepId++; SolverMs = watch.Elapsed.TotalMilliseconds;
        }
        void Update()
        {
            if (!Ready || !dirty || !ApplicationActivity.CanSubmitGpuWork) return;
            deformation.Upload(Solver.RenderPositions); output.MarkDeformed(); dirty = false;
        }
        public void RequestQuality()
        {
            if (!Ready) return;
            float sum = 0;
            for (int i = 0; i < Cage.rest.Length; i++) sum += math.distancesq(Solver.Positions[i], Cage.rest[i]);
            RmsDisplacement = Mathf.Sqrt(sum / Cage.rest.Length);
            StrainMeasure = Cage.MaxVolumeChange(Solver.Positions);
        }
        public void Shutdown()
        {
            Ready = false; deformation?.Dispose(); deformation = null;
            Solver?.Dispose(); Solver = null; Cage = null; output = null; contactProvider = null;
        }
        void OnDisable() => Shutdown();
        void OnDestroy() => Shutdown();
    }
}
