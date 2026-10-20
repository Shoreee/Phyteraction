using System;
using System.Collections.Generic;
using System.Diagnostics;
using PhysicsHands.Hands;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PhysicsHands.GaussianWorld
{
    // A PhysFlow scene on the GPU: MLS-MPM in PhysFlow's simulation space (units, seconds and
    // materials as released), hand bones as grid colliders with reactions back to PhysX, and the
    // reconstruction rendered as splats whose moving part rides on the particles.
    [DefaultExecutionOrder(300)]
    public sealed class MpmWorld : MonoBehaviour, IGaussianSimulation
    {
        public MpmSceneAsset asset;
        public ComputeShader solver;
        public ComputeShader sortShader;
        public Shader splatShader;
        public HoloHand[] hands = Array.Empty<HoloHand>();
        public Collider[] probes = Array.Empty<Collider>();
        [Header("Contact")]
        [Range(0, 2)] public float friction = .6f;
        [Tooltip("Contact band around bones, in grid cells.")]
        [Min(0)] public float skinCells = .5f;
        [Tooltip("Safety clamp against tracking jumps into the object.")]
        [Min(0)] public float maxReactionForce = 8;
        [Header("Time stepping")]
        [Range(.05f, .9f)] public float cfl = .45f;
        [Min(1)] public int maxSubsteps = 64;
        [Tooltip("Frame time the solver budget aims for (ms); past it fewer substeps run per step and the simulation slows instead of the frame rate.")]
        [Min(5)] public float targetFrameMs = 12;
        [Tooltip("Substeps per step under hand interaction. Stiffer material than this resolves runs mass-scaled instead of in slow motion, so hands never outrun it.")]
        [Min(2)] public int interactionSubsteps = 12;
        public float MassScale { get; private set; } = 1;
        [Tooltip("Hand speed the step must resolve (m/s); faster tracked bones are clamped.")]
        [Min(.1f)] public float maxHandSpeed = 1.5f;
        [Tooltip("Speed at which penetrated material is pushed out of a bone (m/s).")]
        [Min(.01f)] public float maxPushSpeed = 1;
        [Tooltip("Material speed cap (m/s): a safety net, far above anything a hand imparts.")]
        [Min(.1f)] public float maxMaterialSpeed = 2;
        [Tooltip("Extra velocity damping (1/s) under hand interaction, standing in for air drag on leaves and petals; paper replays use only the config's damping.")]
        [Min(0)] public float interactionDamping = 3;
        [Header("Interaction (outside the paper replay)")]
        [Tooltip("Pieces broken off the anchored part fall under metric gravity onto a floor at the part's base. The intact object stays weightless as in PhysFlow, whose scanned rest shape already carries its own weight.")]
        public bool gravity = true;
        [Tooltip("Stems and leaves snap past the asset's critical stretch.")]
        public bool fracture = true;
        [Tooltip("Runs PhysFlow's scripted interaction (impulses, enforced velocities, release) from the last reset.")]
        public bool replay;
        [Tooltip("Another component (BrickAssembly) drives the splats outside the replay; the solver idles.")]
        public bool externalDrive;
        // Tests: kinematic world-space spheres (xyz, radius) instead of hands.
        public bool useTestContacts;
        public Vector4[] testSpheres = Array.Empty<Vector4>();

        public bool Ready { get; private set; }
        public long StepId { get; private set; }
        public double SolverMs { get; private set; }
        public double ContactSampleTime => contacts.SampleTime;
        public int ShapeCount => contacts.Count;
        public int PairCount { get; private set; }
        public float ReactionForce { get; private set; }
        public float RmsDisplacement { get; private set; }
        public float StrainMeasure { get; private set; }
        // 95th percentile and maximum particle displacement from rest (m).
        public float P95Displacement { get; private set; }
        public float MaxDisplacement { get; private set; }
        // Fraction of particles in pieces broken off the anchored part (or fully damaged).
        public float BrokenFraction { get; private set; }
        public float SimTime { get; private set; }
        public int Substeps { get; private set; }
        // < 1 when the CFL limit needs more than maxSubsteps per fixed step (slow motion, never unstable).
        public float TimeScale { get; private set; } = 1;
        public string Provenance => asset != null ? asset.provenance : "";
        public string Summary => asset == null ? "" : $"mpm {asset.scene} material={asset.material} particles={asset.particles.Length} grid={asset.gridN}^3 dx={asset.dx:F4} splats={asset.splats.Length} dynamic={asset.dynamicCount} substeps={Substeps} time_scale={TimeScale:F2} mass_scale={MassScale:F1}";

        struct ShapeGpu { public Vector4 a, b, va, vb, ax, ay, az, he; }
        struct Particle { public Vector4 x, v, c0, c1, c2, f0, f1, f2, r0, r1, r2; }

        readonly ContactGatherer contacts = new();
        readonly ShapeGpu[] shapes = new ShapeGpu[ContactGatherer.MaxShapes];
        readonly uint[] zeros = new uint[ContactGatherer.MaxShapes * 8];
        readonly Queue<(long step, Rigidbody[] bodies)> pendingBodies = new();
        readonly List<(long step, float[] reaction)> arrived = new();
        readonly Stopwatch watch = new();
        ComputeShader cs;
        CommandBuffer cmd;
        SplatRenderer splats;
        // Benchmark access: the last step's command buffer, the renderer and the particle state.
        public CommandBuffer StepCommands => cmd;
        public SplatRenderer Splats => splats;
        public ComputeBuffer ParticleBuffer => particles;
        ComputeBuffer particles, detached, grid, blockFlag, blockList, counter, args, reaction, shapeBuffer, modifiers, cuboids, axles, splatParticle, splatOffset;
        int kP2G, kArgs, kGrid, kG2P, kClear, kDeform, particleGroups;
        // Fixed-point grid scale: heaviest particle mass and a reference speed; interaction clamps
        // material to maxMaterialSpeed, the paper's scripts can drive faster (m/s).
        const float ReplaySpeed = 10;
        float heaviest;
        Matrix4x4 simFromLocal;
        bool dirty;

        void OnEnable()
        {
            if (asset == null || asset.splats == null || asset.particles == null || asset.particles.Length == 0 || solver == null || sortShader == null || splatShader == null || !SystemInfo.supportsComputeShaders)
            { Debug.LogError("MPM world requires a PhysFlow scene asset and a compute-capable GPU.", this); enabled = false; return; }
            if ((transform.lossyScale - Vector3.one).sqrMagnitude > 1e-6f)
            { Debug.LogError("MPM world must have unit world scale.", this); enabled = false; return; }
            cs = solver; budget = maxSubsteps; lastStepFrame = -1;
            kP2G = cs.FindKernel("P2G"); kArgs = cs.FindKernel("Args"); kGrid = cs.FindKernel("Grid"); kG2P = cs.FindKernel("G2P"); kClear = cs.FindKernel("Clear"); kDeform = cs.FindKernel("Deform");
            int n = asset.particles.Length, nodes = asset.gridN * asset.gridN * asset.gridN, blocks = nodes / 64;
            particles = new ComputeBuffer(n, 176);
            detached = new ComputeBuffer(n, 4); detachedFlags = new uint[n];
            grid = new ComputeBuffer(nodes * 4, 4); grid.SetData(new uint[nodes * 4]);
            blockFlag = new ComputeBuffer(blocks, 4); blockFlag.SetData(new uint[blocks]);
            blockList = new ComputeBuffer(blocks, 4);
            counter = new ComputeBuffer(1, 4); counter.SetData(new uint[1]);
            args = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments); args.SetData(new uint[] { 0, 1, 1 });
            reaction = new ComputeBuffer(zeros.Length, 4);
            shapeBuffer = new ComputeBuffer(ContactGatherer.MaxShapes, 128);
            modifiers = Buffer(asset.modifiers, 96); cuboids = Buffer(asset.cuboids, 48); axles = Buffer(asset.axles, 32);
            var bind = new uint[Mathf.Max(1, asset.dynamicCount)]; var offs = new Vector4[bind.Length];
            for (int i = 0; i < asset.dynamicCount; i++) { bind[i] = (uint)asset.binding[i]; offs[i] = asset.offsets[i]; }
            splatParticle = Buffer(bind, 4); splatOffset = Buffer(offs, 16);
            splats = new SplatRenderer(asset.splats, asset.bounds, sortShader, splatShader, transform, asset.dynamicCount + asset.nearCount, asset.sh, asset.shFromLocal, asset.dynamicCount);
            foreach (int k in new[] { kP2G, kArgs, kGrid, kG2P, kClear, kDeform })
            {
                cs.SetBuffer(k, "_Particles", particles); cs.SetBuffer(k, "_Detached", detached); cs.SetBuffer(k, "_Grid", grid); cs.SetBuffer(k, "_BlockFlag", blockFlag); cs.SetBuffer(k, "_BlockList", blockList);
                cs.SetBuffer(k, "_Counter", counter); cs.SetBuffer(k, "_Args", args); cs.SetBuffer(k, "_Reaction", reaction); cs.SetBuffer(k, "_Shapes", shapeBuffer);
                cs.SetBuffer(k, "_Modifiers", modifiers); cs.SetBuffer(k, "_Cuboids", cuboids); cs.SetBuffer(k, "_Axles", axles);
                cs.SetBuffer(k, "_Source", splats.Source); cs.SetBuffer(k, "_Deformed", splats.Deformed); cs.SetBuffer(k, "_SplatParticle", splatParticle); cs.SetBuffer(k, "_SplatOffset", splatOffset);
            }
            float sinPhi = Mathf.Sin(asset.frictionAngle * Mathf.Deg2Rad);
            cs.SetInt("_ParticleCount", n); cs.SetInt("_GridN", asset.gridN);
            cs.SetInt("_ModifierCount", asset.modifiers.Length); cs.SetInt("_CuboidCount", asset.cuboids.Length); cs.SetInt("_AxleCount", asset.axles.Length);
            cs.SetInt("_Hardening", asset.hardening ? 1 : 0); cs.SetInt("_DynamicCount", asset.dynamicCount);
            cs.SetFloat("_Dx", asset.dx); cs.SetFloat("_InvDx", 1 / asset.dx);
            cs.SetFloat("_Viscosity", asset.viscosity); cs.SetFloat("_FrictionAlpha", Mathf.Sqrt(2f / 3f) * 2 * sinPhi / (3 - sinPhi));
            cs.SetFloat("_Cohesion", asset.cohesion); cs.SetFloat("_Xi", asset.xi);
            cs.SetFloat("_Skin", skinCells * asset.dx); cs.SetFloat("_MaxPush", maxPushSpeed / asset.metersPerUnit);
            cs.SetFloat("_MaxParticleSpeed", maxMaterialSpeed / asset.metersPerUnit);
            float floor = float.MaxValue; foreach (var p in asset.particles) floor = Mathf.Min(floor, Vector3.Dot(p, asset.up));
            cs.SetVector("_FloorNormal", asset.up); cs.SetFloat("_FloorOffset", floor - .5f * asset.dx);
            cs.SetMatrix("_LocalFromSim", asset.localFromSim);
            var basis = asset.localFromSim; basis.SetColumn(3, new Vector4(0, 0, 0, 1));
            for (int c = 0; c < 3; c++) basis.SetColumn(c, basis.GetColumn(c) / asset.metersPerUnit);
            cs.SetMatrix("_Basis", basis);
            simFromLocal = asset.localFromSim.inverse;
            particleGroups = (n + 63) / 64;
            heaviest = 0; foreach (float v in asset.volume) heaviest = Mathf.Max(heaviest, v * asset.density);
            cmd = new CommandBuffer { name = $"MPM {asset.scene}" };
            if (hands == null || hands.Length == 0) hands = FindObjectsByType<HoloHand>(FindObjectsSortMode.None);
            Ready = true; ResetSimulation();
            Debug.Log($"MPM {asset.scene}: {Summary}, wave speed {asset.WaveSpeed:F1} units/s, E {asset.youngs:G3} Pa, nu {asset.poisson}, rho {asset.density}");
        }
        static ComputeBuffer Buffer<T>(T[] values, int stride) where T : struct
        {
            var b = new ComputeBuffer(Mathf.Max(1, values.Length), stride); if (values.Length > 0) b.SetData(values); return b;
        }

        public void ResetSimulation() => Restart(false);
        // PhysFlow's scripted interaction from rest, as in the paper's videos.
        public void Replay() => Restart(true);
        void Restart(bool script)
        {
            if (!Ready) return;
            int n = asset.particles.Length; var p = new Particle[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 x = asset.particles[i]; uint mask = 0;
                for (int k = 0; k < asset.modifiers.Length && k < 32; k++) if (Selected(asset.modifiers[k], x)) mask |= 1u << k;
                p[i] = new Particle
                {
                    x = new Vector4(x.x, x.y, x.z, asset.volume[i]), v = new Vector4(0, 0, 0, asset.volume[i] * asset.density),
                    c0 = new Vector4(0, 0, 0, asset.yieldStress * (script ? 1 : asset.interactionYieldScale)), c1 = new Vector4(0, 0, 0, math.asfloat(mask)), c2 = new Vector4(0, 0, 0, asset.impulseGain[i]),
                    f0 = new Vector4(1, 0, 0, asset.holdTime[i]), f1 = new Vector4(0, 1, 0, 0), f2 = new Vector4(0, 0, 1, 0),
                    r0 = new Vector4(1, 0, 0, 0), r1 = new Vector4(0, 1, 0, 0), r2 = new Vector4(0, 0, 1, 0),
                };
            }
            particles.SetData(p); Array.Clear(detachedFlags, 0, n); detached.SetData(detachedFlags); BrokenFraction = 0; replay = script; SimTime = 0; contacts.Reset(); pendingBodies.Clear(); arrived.Clear(); dirty = true;
        }
        static bool Selected(MpmModifier m, Vector3 x)
        {
            Vector3 o = x - (Vector3)m.point;
            if ((int)m.point.w != 2) return Mathf.Abs(o.x) < m.size.x && Mathf.Abs(o.y) < m.size.y && Mathf.Abs(o.z) < m.size.z;
            Vector3 n = m.normal; float h = Vector3.Dot(o, n);
            return Mathf.Abs(h) < m.size.x && (o - h * n).magnitude < m.size.y;
        }

        void FixedUpdate()
        {
            if (!Ready || (externalDrive && !replay)) return;
            float dt = Time.fixedDeltaTime, meters = asset.metersPerUnit;
            ApplyReactions(dt);
            // At most one step per rendered frame: catch-up steps after a slow frame would add GPU
            // work and slow the next frame further. Skipped time is dropped (slow motion).
            if (Time.frameCount == lastStepFrame) return;
            lastStepFrame = Time.frameCount;
            watch.Restart();
            contacts.Begin(simFromLocal * transform.worldToLocalMatrix, 1 / meters);
            // Scripted paper playback has its own force/velocity boundary conditions.
            // Mixing live hands into it changes the reference trajectory.
            if (!replay)
            {
                if (useTestContacts) for (int i = 0; i < testSpheres.Length; i++) contacts.AddSphere(testSpheres[i], testSpheres[i].w, 1000 + i, dt);
                else { contacts.AddHands(hands, dt); foreach (var c in probes) contacts.AddCollider(c, dt); }
            }
            for (int i = 0; i < contacts.Count; i++)
            {
                var s = contacts.Shapes[i];
                shapes[i] = new ShapeGpu
                {
                    a = new Vector4(s.a.x, s.a.y, s.a.z, s.radius), b = new Vector4(s.b.x, s.b.y, s.b.z, s.isBox),
                    va = Clamp(s.velocityA, maxHandSpeed / meters), vb = Clamp(s.velocityB, maxHandSpeed / meters), ax = (Vector3)s.axisX, ay = (Vector3)s.axisY, az = (Vector3)s.axisZ, he = (Vector3)s.halfExtents,
                };
            }
            // Explicit MPM is stable for dt < cfl dx / max(elastic wave speed, fastest hand).
            cmd.Clear();
            Vector3 shapeLo = Vector3.one * float.MaxValue, shapeHi = -shapeLo;
            for (int i = 0; i < contacts.Count; i++)
            {
                // Boxes: centre a with half extents; capsules: segment a-b with radius. Both swept over the step.
                var s = shapes[i]; bool box = s.b.w != 0;
                Vector3 e = Vector3.one * (box ? ((Vector3)s.he).magnitude : s.a.w), a0 = s.a, a1 = a0 + (Vector3)s.va * dt;
                Vector3 b0 = box ? a0 : (Vector3)s.b, b1 = box ? a1 : b0 + (Vector3)s.vb * dt;
                shapeLo = Vector3.Min(shapeLo, Vector3.Min(Vector3.Min(a0, a1), Vector3.Min(b0, b1)) - e);
                shapeHi = Vector3.Max(shapeHi, Vector3.Max(Vector3.Max(a0, a1), Vector3.Max(b0, b1)) + e);
            }
            cmd.SetComputeVectorParam(cs, "_ShapeLo", shapeLo); cmd.SetComputeVectorParam(cs, "_ShapeHi", shapeHi);
            // Under interaction, material stiffer than the substep budget resolves gets proportionally more
            // inertia (mass scaling) instead of slow motion: c_eff = c / sqrt(s) fits the step, statics unchanged.
            float hand = maxHandSpeed / meters, wave = asset.WaveSpeedFor(replay);
            if (replay) MassScale = 1;
            else
            {
                Substeps = Mathf.Clamp(Mathf.Min(interactionSubsteps, budget), 2, maxSubsteps);
                float allowed = cfl * asset.dx * Substeps / dt;
                MassScale = wave > allowed ? (wave / allowed) * (wave / allowed) : 1;
                wave /= Mathf.Sqrt(MassScale);
            }
            float speed = Mathf.Max(wave, hand, 1e-3f), limit = cfl * asset.dx / speed;
            Substeps = Mathf.Clamp(Mathf.CeilToInt(dt / limit), 1, Mathf.Min(maxSubsteps, budget));
            float h = Mathf.Min(dt / Substeps, limit); TimeScale = h * Substeps / dt;
            var material = asset.MaterialFor(replay);
            cmd.SetComputeIntParam(cs, "_Material", (int)material);
            cmd.SetComputeFloatParam(cs, "_Mu", asset.MuFor(replay)); cmd.SetComputeFloatParam(cs, "_Lambda", asset.LambdaFor(replay));
            cmd.SetComputeFloatParam(cs, "_MassScale", MassScale);
            float fixedMass = (1 << 23) / Mathf.Max(heaviest * MassScale, 1e-30f);
            cmd.SetComputeFloatParam(cs, "_FixedMass", fixedMass);
            cmd.SetComputeFloatParam(cs, "_FixedMomentum", fixedMass * asset.metersPerUnit / (replay ? ReplaySpeed : 1.5f * maxMaterialSpeed));            cmd.SetBufferData(reaction, zeros);
            if (contacts.Count > 0) cmd.SetBufferData(shapeBuffer, shapes, 0, 0, contacts.Count);
            cmd.SetComputeIntParam(cs, "_ShapeCount", contacts.Count);
            cmd.SetComputeIntParam(cs, "_Script", replay ? 1 : 0);
            // The replay is the paper's setup; interaction adds metric gravity, a floor and fracture.
            bool interactive = !replay && gravity;
            cmd.SetComputeVectorParam(cs, "_Gravity", replay ? asset.gravity : -asset.up * (9.81f / meters));
            cmd.SetComputeIntParam(cs, "_GravityMode", replay ? (asset.gravity != Vector3.zero ? 1 : 0) : interactive ? 2 : 0);
            cmd.SetComputeIntParam(cs, "_Floor", interactive ? 1 : 0);
            cmd.SetComputeFloatParam(cs, "_FractureStrain", !replay && fracture && asset.scene == "carnation" ? asset.fractureStrain : 0);
            cmd.SetComputeFloatParam(cs, "_Dt", h);
            cmd.SetComputeFloatParam(cs, "_Damping", Mathf.Exp(-(asset.dampingRate + (replay ? 0 : interactionDamping)) * h));
            for (int s = 0; s < Substeps; s++)
            {
                // Shapes move linearly from their pose at the start of this fixed step.
                cmd.SetComputeFloatParam(cs, "_Time", SimTime + h * s);
                cmd.SetComputeFloatParam(cs, "_ShapeTime", h * s);
                cmd.DispatchCompute(cs, kP2G, particleGroups, 1, 1);
                cmd.DispatchCompute(cs, kArgs, 1, 1, 1);
                cmd.DispatchCompute(cs, kGrid, args, 0);
                cmd.DispatchCompute(cs, kG2P, particleGroups, 1, 1);
                cmd.DispatchCompute(cs, kClear, args, 0);
            }
            long step = StepId;
            if (contacts.Count > 0)
            {
                pendingBodies.Enqueue((step, contacts.Snapshot()));
                cmd.RequestAsyncReadback(reaction, r => { if (!r.hasError && this != null && Ready) arrived.Add((step, r.GetData<float>().ToArray())); });
            }
            Graphics.ExecuteCommandBuffer(cmd);
            SimTime += h * Substeps; StepId++; dirty = true;
            SolverMs = watch.Elapsed.TotalMilliseconds;
        }
        // Tracking glitches report bone speeds no hand reaches; they would fling material.
        static Vector4 Clamp(float3 v, float limit) { float l = math.length(v); return (Vector3)(l > limit ? v * (limit / l) : v); }
        // Reactions arrive one or more frames late; each is applied to the bodies of its own step.
        void ApplyReactions(float dt)
        {
            if (arrived.Count == 0) return;
            float meters = asset.metersPerUnit, scale = meters * meters * meters, total = 0; int pairs = 0;
            foreach (var (step, data) in arrived)
            {
                while (pendingBodies.Count > 0 && pendingBodies.Peek().step < step) pendingBodies.Dequeue();
                if (pendingBodies.Count == 0 || pendingBodies.Peek().step != step) continue;
                var bodies = pendingBodies.Dequeue().bodies;
                for (int i = 0; i < ContactGatherer.MaxShapes; i++)
                {
                    float weight = data[i * 8 + 3]; if (weight <= 0) continue;
                    pairs++;
                    // Simulation momentum (density x units^3 x units/s) to kg m/s: L^3 for mass, L for velocity (in localFromSim).
                    Vector3 local = asset.localFromSim.MultiplyVector(new Vector3(data[i * 8], data[i * 8 + 1], data[i * 8 + 2])) * scale;
                    Vector3 point = asset.localFromSim.MultiplyPoint3x4(new Vector3(data[i * 8 + 4], data[i * 8 + 5], data[i * 8 + 6]) / weight);
                    total += contacts.ApplyReaction(i, bodies, transform.TransformDirection(local), transform.TransformPoint(point), maxReactionForce, dt);
                }
            }
            arrived.Clear(); PairCount = pairs; ReactionForce = total / dt;
        }
        int budget, lastStepFrame;
        float frameMs;
        void Update()
        {
            if (!Ready || (externalDrive && !replay)) return;
            frameMs = Mathf.Lerp(frameMs <= 0 ? targetFrameMs : frameMs, Time.unscaledDeltaTime * 1000, .1f);
            if (frameMs > targetFrameMs * 1.15f) budget = Mathf.Max(6, Mathf.FloorToInt(budget * .9f));
            else if (frameMs < targetFrameMs * .9f) budget = Mathf.Min(maxSubsteps, budget + 1);
            // Pieces only separate under contact; no readback while nothing touches or is broken off.
            if (asset.scene == "carnation" && fracture && (PairCount > 0 || BrokenFraction > 0) && !replay && gravity && Time.unscaledTime >= nextConnectivity && !connectivityPending)
            {
                nextConnectivity = Time.unscaledTime + .2f; connectivityPending = true;
                AsyncGPUReadback.Request(particles, r => { connectivityPending = false; if (!r.hasError && this != null && Ready) Connectivity(r.GetData<Particle>()); });
            }
            if (!dirty) return;
            cs.SetInt("_RenderTotal", asset.RenderTotalFor(replay) ? 1 : 0);
            if (asset.dynamicCount > 0) cs.Dispatch(kDeform, (asset.dynamicCount + 63) / 64, 1, 1);
            dirty = false;
        }
        // Union-find over intact particles closer than half a cell; components touching an anchor
        // (a PhysFlow cuboid) stay attached, the rest are free pieces. Particles already loose in the
        // scan (floaters) never count as broken off; scenes without anchors never detach.
        readonly Dictionary<long, int> cells = new();
        int[] parent;
        uint[] detachedFlags;
        bool[] restFree;
        float nextConnectivity; bool connectivityPending;
        void Connectivity(NativeArray<Particle> p)
        {
            if (asset.cuboids.Length == 0) return;
            restFree ??= Free(asset.particles.Length, i => asset.particles[i], _ => false);
            var free = Free(p.Length, i => (Vector3)p[i].x, i => p[i].f1.w >= 1);
            int count = 0;
            for (int i = 0; i < p.Length; i++) { bool d = p[i].f1.w >= 1 || (free[i] && !restFree[i]); detachedFlags[i] = d ? 1u : 0u; if (d) count++; }
            detached.SetData(detachedFlags); BrokenFraction = count / (float)p.Length;
        }
        bool[] Free(int n, Func<int, Vector3> position, Func<int, bool> broken)
        {
            if (parent == null || parent.Length != n) parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            cells.Clear(); float h = asset.dx * .5f;
            long Key(int x, int y, int z) => ((long)x * 73856093) ^ ((long)y * 19349663) ^ ((long)z * 83492791);
            for (int i = 0; i < n; i++)
            {
                if (broken(i)) continue;
                Vector3 x = position(i); long k = Key(Mathf.FloorToInt(x.x / h), Mathf.FloorToInt(x.y / h), Mathf.FloorToInt(x.z / h));
                if (cells.TryGetValue(k, out int j)) Union(i, j); else cells[k] = i;
            }
            foreach (var (_, i) in cells)
            {
                Vector3 x = position(i); int cx = Mathf.FloorToInt(x.x / h), cy = Mathf.FloorToInt(x.y / h), cz = Mathf.FloorToInt(x.z / h);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    if (cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out int j)) Union(i, j);
            }
            var anchored = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                if (broken(i)) continue; Vector3 x = position(i);
                foreach (var c in asset.cuboids)
                    if (Mathf.Abs(x.x - c.point.x) < c.size.x && Mathf.Abs(x.y - c.point.y) < c.size.y && Mathf.Abs(x.z - c.point.z) < c.size.z) { anchored.Add(Find(i)); break; }
            }
            var free = new bool[n];
            for (int i = 0; i < n; i++) free[i] = broken(i) || !anchored.Contains(Find(i));
            return free;
        }
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

        public void RequestQuality()
        {
            if (!Ready) return;
            AsyncGPUReadback.Request(particles, r =>
            {
                if (r.hasError || this == null || !Ready) return;
                var p = r.GetData<Particle>(); double sum = 0; float strain = 0; var d = new float[p.Length];
                // Deepest particle inside a test sphere, metres (probe contact quality).
                float depth = 0; var toSim = simFromLocal * transform.worldToLocalMatrix;
                if (useTestContacts)
                    foreach (var s in testSpheres)
                    {
                        Vector3 c = toSim.MultiplyPoint3x4(s); float radius = s.w / asset.metersPerUnit;
                        for (int i = 0; i < p.Length; i++) depth = Mathf.Max(depth, radius - Vector3.Distance(p[i].x, c));
                    }
                PenetrationDepth = depth * asset.metersPerUnit;
                for (int i = 0; i < p.Length; i++)
                {
                    float sq = ((Vector3)p[i].x - asset.particles[i]).sqrMagnitude; sum += sq; d[i] = Mathf.Sqrt(sq);
                    var f = new Matrix4x4(p[i].f0, p[i].f1, p[i].f2, new Vector4(0, 0, 0, 1)).transpose;
                    strain = Mathf.Max(strain, Mathf.Abs(f.determinant - 1));
                }
                RmsDisplacement = (float)Math.Sqrt(sum / Math.Max(1, p.Length)) * asset.metersPerUnit; StrainMeasure = strain;
                Array.Sort(d); P95Displacement = d[(int)(.95f * (d.Length - 1))] * asset.metersPerUnit; MaxDisplacement = d[^1] * asset.metersPerUnit;
                QualityResults++;
            });
        }
        public int QualityResults { get; private set; }
        public float PenetrationDepth { get; private set; }
        void OnDisable()
        {
            if (!Ready) return;
            Ready = false;
            splats.Dispose(); cmd.Release();
            foreach (var b in new[] { particles, detached, grid, blockFlag, blockList, counter, args, reaction, shapeBuffer, modifiers, cuboids, axles, splatParticle, splatOffset }) b.Release();
        }
    }
}
