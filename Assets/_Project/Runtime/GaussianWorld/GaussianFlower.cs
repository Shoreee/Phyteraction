using System;
using System.Diagnostics;
using PhysicsHands.Hands;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PhysicsHands.GaussianWorld
{
    // PhysDreamer's learned E(x) field on a voxel cage, solved in its lowest vibration modes.
    [DefaultExecutionOrder(300)]
    public sealed class GaussianFlower : MonoBehaviour, IGaussianSimulation
    {
        public GaussianAsset asset;
        public ComputeShader solver;
        public ComputeShader sortShader;
        public Shader splatShader;
        public HoloHand[] hands = Array.Empty<HoloHand>();
        // Extra dynamic colliders that touch the flower (tests, props).
        public Collider[] probes = Array.Empty<Collider>();
        [Header("Cage")]
        [Min(.002f)] public float voxelSize=.008f;
        [Tooltip("Assumed flower mass. Scales forces only; PhysDreamer's E/rho time scale is preserved.")]
        [Min(.001f)] public float totalMass=.02f;
        [Tooltip("Used only when the asset carries no learned Young's modulus field.")]
        [Min(1)] public float fallbackYoungs=2e5f;
        [Header("Solver")]
        [Tooltip("Contact projections per fixed step; the modal dynamics are exact at any step.")]
        [Min(1)] public int substeps=8;
        [Tooltip("Mass-proportional damping (1/s), hand-set; PhysDreamer's MPM has no explicit damping.")]
        [Min(0)] public float damping=1.5f;
        [Range(0,1)] public float friction=.45f;
        [Min(.001f)] public float contactRadius=.004f;
        [Tooltip("Safety clamp against tracking jumps into the flower.")]
        [Min(0)] public float maxReactionForce=5;
        public ElasticCage Cage {get;private set;}
        public ModalSolver Solver {get;private set;}
        public bool Ready {get;private set;}
        public long StepId {get;private set;}
        public double SolverMs {get;private set;}
        public double ContactSampleTime=>contacts.SampleTime;
        public int ShapeCount=>contacts.Count;
        public int PairCount=>Solver?.PairCount??0;
        public float ReactionForce {get;private set;}
        public float RmsDisplacement {get;private set;}
        public float StrainMeasure {get;private set;}
        public string Provenance=>asset!=null?asset.provenance:"";
        public int Substeps=>substeps;
        public float TimeScale=>1;
        public string Summary=>Cage==null?"":$"modal cage voxels={Cage.voxelCount} nodes={Cage.rest.Length} tets={Cage.tets.Length} samples={Cage.sampleVoxel.Length} modes={Solver.ModeCount} substeps={substeps} fixed_dt={Time.fixedDeltaTime:F5}";
        // Tests can inject kinematic flower-local spheres (xyz, radius) without gesture-specific forces.
        public bool useTestContacts;
        public Vector4[] testSpheres=Array.Empty<Vector4>();
        readonly ContactGatherer contacts=new();
        readonly Stopwatch watch=new();
        Rigidbody[] bodies;
        ComputeBuffer positions,voxelNodes,voxelOrigin,splatVoxel;
        ComputeShader compute;
        SplatRenderer splats;
        int deform;
        bool dirty=true;

        void OnEnable()
        {
            if(asset==null || asset.splats==null || asset.splats.Length==0 || solver==null || sortShader==null || splatShader==null || !SystemInfo.supportsComputeShaders)
            {Debug.LogError("Gaussian Flower requires a nonempty asset and compute-capable Metal/D3D GPU.",this);enabled=false;return;}
            if((transform.lossyScale-Vector3.one).sqrMagnitude>1e-6f)
            {Debug.LogError("Gaussian Flower must have unit world scale; bake asset scale during import.",this);enabled=false;return;}
            if(!asset.HasMaterial)Debug.LogWarning("Gaussian asset has no learned material field; using fallbackYoungs.",this);
            Cage=new ElasticCage(asset,voxelSize,totalMass,fallbackYoungs);
            if(!asset.HasModes(Cage.rest.Length,voxelSize,totalMass))
            {Debug.LogError("Gaussian asset modes do not match this cage; rebuild the Gaussian World scene.",this);enabled=false;return;}
            Solver=new ModalSolver(Cage,asset.modeOmega,asset.modeShapes);
            Debug.Log($"Gaussian cage: {Cage.voxelCount} voxels, {Cage.rest.Length} nodes ({Cage.anchoredNodes} anchored), {Cage.tets.Length} tets, {Cage.sampleVoxel.Length} contact samples, {Solver.ModeCount} modes {asset.modeOmega[0]/(2*Mathf.PI):F2}-{asset.modeOmega[^1]/(2*Mathf.PI):F1} Hz, E {Cage.youngsMin:G3}/{Cage.youngsMedian:G3}/{Cage.youngsMax:G3} Pa, rho {Cage.density:F1} kg/m3");
            compute=Instantiate(solver);deform=compute.FindKernel("Deform");
            splats=new SplatRenderer(asset.splats,asset.bounds,sortShader,splatShader,transform,
                shCoefficients:asset.sh,shFromLocal:Matrix4x4.identity);
            positions=new ComputeBuffer(Cage.rest.Length,16);
            voxelNodes=Buffer(Cage.voxelNodes,4);splatVoxel=Buffer(Cage.splatVoxel,4);
            var origins=new Vector4[Cage.voxelOrigin.Length];for(int i=0;i<origins.Length;i++)origins[i]=Cage.voxelOrigin[i];voxelOrigin=Buffer(origins,16);
            compute.SetBuffer(deform,"_Position",positions);compute.SetBuffer(deform,"_VoxelNodes",voxelNodes);compute.SetBuffer(deform,"_VoxelOrigin",voxelOrigin);
            compute.SetBuffer(deform,"_SplatVoxel",splatVoxel);compute.SetBuffer(deform,"_Source",splats.Source);compute.SetBuffer(deform,"_Deformed",splats.Deformed);
            compute.SetInt("_SplatCount",splats.Count);compute.SetFloat("_Spacing",Cage.spacing);
            if(hands==null||hands.Length==0)hands=FindObjectsByType<HoloHand>(FindObjectsSortMode.None);
            contacts.Reset();dirty=true;StepId=0;Ready=true;
        }
        static ComputeBuffer Buffer<T>(T[] values,int stride) where T:struct
        {var b=new ComputeBuffer(values.Length,stride);b.SetData(values);return b;}
        public void ResetSimulation()
        {
            if(!Ready)return;Solver.Reset();contacts.Reset();dirty=true;
        }
        void FixedUpdate()
        {
            if(!Ready)return;
            watch.Restart();
            float dt=Time.fixedDeltaTime;
            contacts.Begin(transform.worldToLocalMatrix,1);
            if(useTestContacts)
                for(int i=0;i<testSpheres.Length;i++)contacts.AddSphere(transform.TransformPoint(testSpheres[i]),testSpheres[i].w,1000+i,dt);
            else
            {
                contacts.AddHands(hands,dt);
                foreach(var c in probes)contacts.AddCollider(c,dt);
            }
            for(int i=0;i<contacts.Count;i++)Solver.Shapes[i]=contacts.Shapes[i];
            Solver.Step(contacts.Count,dt,substeps,damping,friction,contactRadius);
            bodies=contacts.Snapshot(bodies);float total=0;
            for(int i=0;i<contacts.Count;i++)
            {
                float weight=Solver.ImpulseMagnitude[i];if(weight<=0)continue;
                total+=contacts.ApplyReaction(i,bodies,transform.TransformDirection(Solver.Impulse[i]),transform.TransformPoint(Solver.ImpulseMoment[i]/weight),maxReactionForce,dt);
            }
            ReactionForce=total/dt;
            dirty=true;StepId++;
            SolverMs=watch.Elapsed.TotalMilliseconds;
        }
        void Update()
        {
            if(!Ready||!dirty)return;
            positions.SetData(Solver.RenderPositions);compute.Dispatch(deform,(splats.Count+63)/64,1,1);dirty=false;
        }
        public void RequestQuality()
        {
            if(!Ready)return;var x=Solver.Positions;float sum=0;
            for(int i=0;i<x.Length;i++)sum+=math.distancesq(x[i],Cage.rest[i]);
            RmsDisplacement=Mathf.Sqrt(sum/x.Length);StrainMeasure=Cage.MaxVolumeChange(x);
        }
        void OnDisable()
        {
            if(!Ready)return;Ready=false;
            splats.Dispose();positions.Release();voxelNodes.Release();voxelOrigin.Release();splatVoxel.Release();
            Solver.Dispose();Solver=null;Destroy(compute);
        }
    }
}
