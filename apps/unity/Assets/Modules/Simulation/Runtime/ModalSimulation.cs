using PhysicsHands.GaussianData;
using System;
using System.Diagnostics;
using PhysicsHands.Dynamics;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PhysicsHands.Simulation
{
    // PhysDreamer's learned E(x) field on a voxel cage, solved in its lowest vibration modes.
    [DefaultExecutionOrder(300)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "GaussianFlower")]
    public sealed class ModalSimulation : MonoBehaviour, ISimulationDiagnostics, ISimulationControl, ISimulationLifecycle
    {
        public ModalModelAsset asset;
        public ComputeShader solver;
        public MonoBehaviour contactSource;
        // Extra physical colliders that touch the flower.
        public Collider[] probes = Array.Empty<Collider>();
        [Header("Cage")]
        [Min(.002f)]
        public float voxelSize = .008f;
        [Tooltip("Assumed flower mass. Scales forces only; PhysDreamer's E/rho time scale is preserved.")]
        [Min(.001f)]
        public float totalMass = .02f;
        [Tooltip("Used only when the asset carries no learned Young's modulus field.")]
        [Min(1)]
        public float fallbackYoungs = 2e5f;
        [Header("Solver")]
        [Tooltip("Contact projections per fixed step; the modal dynamics are exact at any step.")]
        [Min(1)]
        public int substeps = 8;
        [Tooltip("Mass-proportional damping (1/s), hand-set; PhysDreamer's MPM has no explicit damping.")]
        [Min(0)]
        public float damping = 1.5f;
        [Range(0, 1)]
        public float friction = .45f;
        [Min(.001f)]
        public float contactRadius = .004f;
        [Tooltip("Safety clamp against tracking jumps into the flower.")]
        [Min(0)]
        public float maxReactionForce = 5;
        public ElasticCage Cage
        {
            get;
            private set;
        }

        public ModalSolver Solver
        {
            get;
            private set;
        }

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
        public int PairCount => Solver?.PairCount ?? 0;
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

        public string Provenance => asset != null ? asset.provenance : "";
        public int Substeps => substeps;
        public float TimeScale => 1;
        public string Summary => Cage == null ? "" : $"modal cage voxels={Cage.voxelCount} nodes={Cage.rest.Length} tets={Cage.tets.Length} samples={Cage.sampleVoxel.Length} modes={Solver.ModeCount} substeps={substeps} fixed_dt={Time.fixedDeltaTime:F5}";
        readonly ContactGatherer contacts = new();
        readonly Stopwatch watch = new();
        Rigidbody[] bodies;
        ModalDeformationOutput deformation;
        IGaussianBufferSource splats;
        bool dirty = true;
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
                if (asset == null || asset.renderData == null || asset.renderData.splats == null || asset.renderData.splats.Length == 0 || solver == null || !SystemInfo.supportsComputeShaders)
                {
                    throw new InvalidOperationException($"{name}: Gaussian Flower requires a nonempty asset and compute-capable Metal/D3D GPU.");
                }

                if (splats.Count != asset.renderData.splats.Length || splats.Source == null || splats.Deformed == null)
                    throw new InvalidOperationException($"{name}: Gaussian buffers do not match the model's render data.");

                if ((transform.lossyScale - Vector3.one).sqrMagnitude > 1e-6f)
                {
                    throw new InvalidOperationException($"{name}: Gaussian Flower must have unit world scale; bake asset scale during import.");
                }

                if (!asset.HasMaterial)
                    Debug.LogWarning("Gaussian asset has no learned material field; using fallbackYoungs.", this);
                Cage = new ElasticCage(asset, voxelSize, totalMass, fallbackYoungs);
                if (!asset.HasModes(Cage.rest.Length, voxelSize, totalMass))
                {
                    throw new InvalidOperationException($"{name}: Gaussian asset modes do not match this cage; use Reimport Learned Flower from the Gaussian World menu.");
                }

                Solver = new ModalSolver(Cage, asset.modeOmega, asset.modeShapes);
                Debug.Log($"Gaussian cage: {Cage.voxelCount} voxels, {Cage.rest.Length} nodes ({Cage.anchoredNodes} anchored), {Cage.tets.Length} tets, {Cage.sampleVoxel.Length} contact samples, {Solver.ModeCount} modes {asset.modeOmega[0] / (2 * Mathf.PI):F2}-{asset.modeOmega[^1] / (2 * Mathf.PI):F1} Hz, E {Cage.youngsMin:G3}/{Cage.youngsMedian:G3}/{Cage.youngsMax:G3} Pa, rho {Cage.density:F1} kg/m3");
                deformation = new ModalDeformationOutput(Cage, solver, splats);
                contacts.Reset();
                dirty = true;
                StepId = 0;
                Ready = true;
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        public bool SupportsReplay => false;
        public void Replay() => ResetSimulation();
        public void ResetSimulation()
        {
            if (!Ready)
                return;
            Solver.Reset();
            contacts.Reset();
            dirty = true;
        }

        void FixedUpdate()
        {
            if (!Ready)
                return;
            watch.Restart();
            float dt = Time.fixedDeltaTime;
            contacts.Policy = contactProvider;
            contacts.Begin(transform.worldToLocalMatrix, 1);
            contactProvider.Gather(contacts, dt);
            foreach (var c in probes)
                contacts.AddCollider(c, dt);
            for (int i = 0; i < contacts.Count; i++)
                Solver.Shapes[i] = contacts.Shapes[i];
            Solver.Step(contacts.Count, dt, substeps, damping, friction, contactRadius);
            bodies = contacts.Snapshot(bodies);
            float total = 0;
            for (int i = 0; i < contacts.Count; i++)
            {
                float weight = Solver.ImpulseMagnitude[i];
                if (weight <= 0)
                    continue;
                total += contacts.ApplyReaction(i, bodies, transform.TransformDirection(Solver.Impulse[i]), transform.TransformPoint(Solver.ImpulseMoment[i] / weight), maxReactionForce, dt);
            }

            ReactionForce = total / dt;
            dirty = true;
            StepId++;
            SolverMs = watch.Elapsed.TotalMilliseconds;
        }

        void Update()
        {
            if (!Ready || !dirty)
                return;
            deformation.Upload(Solver.RenderPositions);
            dirty = false;
        }

        public void RequestQuality()
        {
            if (!Ready)
                return;
            var x = Solver.Positions;
            float sum = 0;
            for (int i = 0; i < x.Length; i++)
                sum += math.distancesq(x[i], Cage.rest[i]);
            RmsDisplacement = Mathf.Sqrt(sum / x.Length);
            StrainMeasure = Cage.MaxVolumeChange(x);
        }

        void OnDisable() => Shutdown();
        void OnDestroy() => Shutdown();

        public void Shutdown()
        {
            Ready = false;
            deformation?.Dispose();
            deformation = null;
            Solver?.Dispose();
            Solver = null;
            Cage = null;
            splats = null;
            contactProvider = null;
        }
    }
}
