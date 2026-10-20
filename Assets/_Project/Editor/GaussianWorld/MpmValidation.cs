using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PhysicsHands.GaussianWorld;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PhysicsHands.Editor.GaussianWorld
{
    // Per PhysFlow scene: a kinematic fingertip-sized sphere pokes the moving part from the viewer
    // side and withdraws, then the paper's scripted interaction runs from rest. Screenshots and
    // numbers go to Logs/physflow/.
    [InitializeOnLoad]
    public static class MpmValidation
    {
        const string Key = "GaussianWorld.MpmValidation";
        static MpmValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) new GameObject("MPM Validation").AddComponent<MpmProbe>();
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key, false))
                {
                    SessionState.SetBool(Key, false);
                    if (Application.isBatchMode) EditorApplication.Exit(File.Exists(MpmProbe.Output) && File.ReadAllText(MpmProbe.Output).Contains("\"passed\": true") ? 0 : 1);
                }
            };
        }
        [MenuItem("Physics Hands/Gaussian World/Run PhysFlow Validation")]
        public static void Run() { GaussianWorldBuilder.CreateScene(); SessionState.SetBool(Key, true); EditorApplication.isPlaying = true; }
        // Validation without rebuilding the scene (assets unchanged).
        public static void RunExisting() { UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GaussianWorldBuilder.ScenePath); SessionState.SetBool(Key, true); EditorApplication.isPlaying = true; }
    }

    public sealed class MpmProbe : MonoBehaviour
    {
        public const string Output = "Logs/physflow/validation.json";
        [Serializable] public class SceneResult
        {
            public string scene, material; public int particles, gridN, splats, dynamicSplats, substeps; public float timeScale, dxMeters;
            public float slowPenetrationMm, fastPenetrationMm, meanCpuMs, meanGpuMs, maxGpuMs, peakRmsMm, releasedRmsMm, releasedP95Mm, releasedMaxMm, brokenFraction, peakReactionN, peakStrain, paperRmsMm, paperStrain;
            public bool finite, recovered;
            // Rigid bricks (scenes with a BrickAssembly): pieces, the ones pulled off and settled, splat tracking error, click-in.
            public int bricks, bricksDetached, bricksSettled; public float brickSplatErrorMm, brickRestDiff; public bool brickSnapped, brickFarRejected, brickReset, bricksPassed = true;
        }
        [Serializable] public class Result { public bool passed; public string device; public SceneResult[] scenes; public string[] errors; public string limitation = "Kinematic probe spheres in the editor; not real hand tracking or device timing."; }
        readonly List<string> errors = new();
        readonly FrameTiming[] timing = new FrameTiming[1];
        void Capture(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception) errors.Add(message); }

        IEnumerator Start()
        {
            Application.logMessageReceived += Capture;
            Directory.CreateDirectory("Logs/physflow");
            yield return null;
            var selector = FindAnyObjectByType<GaussianSceneSelector>();
            var camera = Camera.main;
            if (camera != null) { var driver = camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>(); if (driver != null) driver.enabled = false; }
            yield return new WaitForSeconds(.5f);
            var workspace = FindAnyObjectByType<Workspace>();
            if (camera != null && workspace != null) camera.transform.LookAt(workspace.transform.position + Vector3.up * .05f - workspace.transform.forward * .1f);
            Screenshot("Logs/physflow/layout.png");
            if (camera != null && workspace != null && workspace.panel != null)
            {
                var look = camera.transform.rotation; camera.transform.LookAt(workspace.panel.bounds.center);
                yield return null; yield return null; Screenshot("Logs/physflow/panel-hover.png"); camera.transform.rotation = look; yield return null;
            }
            // Simulated hands would block the close-up views.
            foreach (var hand in FindObjectsByType<PhysicsHands.Hands.HoloHand>(FindObjectsSortMode.None))
                foreach (var renderer in hand.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var results = new List<SceneResult>();
            Time.captureFramerate = 90;
            string only = Environment.GetEnvironmentVariable("PHYSFLOW_SCENES");
            for (int i = 0; selector != null && i < selector.scenes.Length; i++)
            {
                if (!selector.scenes[i].TryGetComponent(out MpmWorld world)) continue;
                if (!string.IsNullOrEmpty(only) && Array.IndexOf(only.Split(','), world.asset.scene) < 0) continue;
                selector.Select(i); yield return null;
                var r = new SceneResult { scene = world.asset.scene, material = world.asset.material.ToString(), particles = world.asset.particles.Length, gridN = world.asset.gridN,
                    splats = world.asset.splats.Length, dynamicSplats = world.asset.dynamicCount, dxMeters = world.asset.dx * world.asset.metersPerUnit, finite = true };
                yield return Scene(world, camera, r); results.Add(r);
                Debug.Log("PhysFlow validation " + JsonUtility.ToJson(r));
            }
            Time.captureFramerate = 0;
            var result = new Result { device = SystemInfo.graphicsDeviceName, scenes = results.ToArray(), errors = errors.ToArray() };
            result.passed = results.Count > 0 && errors.Count == 0 && results.TrueForAll(s => s.finite && s.peakRmsMm > 0 && s.bricksPassed);
            File.WriteAllText(Output, JsonUtility.ToJson(result, true));
            Application.logMessageReceived -= Capture;
            EditorApplication.isPlaying = false;
        }

        IEnumerator Scene(MpmWorld world, Camera camera, SceneResult r)
        {
            // The continuum tests below run on MPM; bricks are checked afterwards.
            bool hasBricks = world.TryGetComponent(out BrickAssembly bricks) && bricks.enabled;
            if (hasBricks) { bricks.enabled = false; yield return null; }
            yield return Continuum(world, camera, r);
            if (hasBricks) { bricks.enabled = true; yield return Bricks(world, bricks, r); }
        }

        IEnumerator Bricks(MpmWorld world, BrickAssembly bricks, SceneResult r)
        {
            var a = world.asset; string scene = a.scene;
            yield return Frames(5, world, r);
            Screenshot($"Logs/physflow/{scene}-bricks-rest.png");
            r.brickRestDiff = ImageDiff($"Logs/physflow/{scene}-rest.png", $"Logs/physflow/{scene}-bricks-rest.png");
            r.bricks = bricks.Count;
            // Pull the four topmost bricks off (2 cm up, a sideways toss) and let them fall.
            var order = new List<int>(); for (int i = 0; i < bricks.Count; i++) order.Add(i);
            order.Sort((x, y) => a.bricks[y].center.y.CompareTo(a.bricks[x].center.y));
            var off = order.GetRange(0, Mathf.Min(4, order.Count)); var random = new System.Random(3);
            foreach (int i in off)
            {
                bricks.Detach(i, Vector3.up * .02f);
                bricks.Brick(i).GetComponent<Rigidbody>().linearVelocity = new Vector3((float)random.NextDouble() - .5f, .5f, (float)random.NextDouble() - .5f) * .3f;
            }
            yield return Frames(240, world, r);
            Screenshot($"Logs/physflow/{scene}-bricks-off.png");
            float floor = world.transform.position.y - .03f;
            foreach (int i in off)
            {
                var body = bricks.Brick(i).GetComponent<Rigidbody>();
                Debug.Log($"Brick settle {i}: attached={bricks.IsAttached(i)} position={body.position} speed={body.linearVelocity.magnitude:F3} sleep={body.IsSleeping()}");
                if (!bricks.IsAttached(i)) r.bricksDetached++;
                if (body.position.y > floor && body.linearVelocity.magnitude < .05f && float.IsFinite(body.position.x)) r.bricksSettled++;
            }
            // Splats ride exactly on their brick: Deformed = pose * rest^-1 * Source.
            int probe = off[0]; var brick = bricks.Brick(probe);
            var expected = world.transform.worldToLocalMatrix * brick.localToWorldMatrix * Matrix4x4.TRS(a.bricks[probe].center, a.bricks[probe].rotation, Vector3.one).inverse;
            var request = AsyncGPUReadback.Request(world.Splats.Deformed); request.WaitForCompletion();
            var deformed = request.GetData<Gaussian>(); float error = 0;
            for (int i = 0; i < a.dynamicCount; i++)
                if (a.splatBrick[i] == probe) error = Mathf.Max(error, Vector3.Distance(deformed[i].center, expected.MultiplyPoint3x4(a.splats[i].center)));
            r.brickSplatErrorMm = error * 1000;
            // Put back 3 mm and 5 degrees off its slot it clicks in; 3 cm off it stays loose.
            int near = off[0], far = off[1];
            Place(bricks, world, near, new Vector3(.003f, 0, 0), 5); Place(bricks, world, far, new Vector3(0, .03f, 0), 0);
            yield return Frames(2, world, r);
            // As if the hand let go there.
            r.brickSnapped = bricks.TrySnap(near); r.brickFarRejected = !bricks.TrySnap(far);
            yield return Frames(2, world, r);
            r.brickSnapped &= bricks.IsAttached(near); r.brickFarRejected &= !bricks.IsAttached(far);
            yield return Frames(20, world, r);
            Screenshot($"Logs/physflow/{scene}-bricks-back.png");
            bricks.ResetBricks(); yield return Frames(10, world, r);
            r.brickReset = bricks.AttachedCount == bricks.Count;
            Screenshot($"Logs/physflow/{scene}-bricks-reset.png");
            r.bricksPassed = r.bricks > 10 && r.bricksDetached == off.Count && r.bricksSettled == off.Count && r.brickSplatErrorMm < .1f && r.brickSnapped && r.brickFarRejected && r.brickReset && r.brickRestDiff < .01f;
        }
        static void Place(BrickAssembly bricks, MpmWorld world, int i, Vector3 offset, float degrees)
        {
            var b = world.asset.bricks[i]; var body = bricks.Brick(i).GetComponent<Rigidbody>();
            body.linearVelocity = body.angularVelocity = Vector3.zero; body.useGravity = false;
            body.position = world.transform.TransformPoint(b.center + offset); body.rotation = world.transform.rotation * Quaternion.AngleAxis(degrees, Vector3.up) * b.rotation;
            bricks.Brick(i).SetPositionAndRotation(body.position, body.rotation);
        }
        // Fraction of pixels differing by more than 8/255 in any channel.
        static float ImageDiff(string a, string b)
        {
            if (!File.Exists(a) || !File.Exists(b)) return 1;
            var x = new Texture2D(2, 2); var y = new Texture2D(2, 2); x.LoadImage(File.ReadAllBytes(a)); y.LoadImage(File.ReadAllBytes(b));
            if (x.width != y.width || x.height != y.height) return 1;
            var p = x.GetPixels32(); var q = y.GetPixels32(); int differ = 0;
            for (int i = 0; i < p.Length; i++) if (Mathf.Max(Mathf.Abs(p[i].r - q[i].r), Mathf.Abs(p[i].g - q[i].g), Mathf.Abs(p[i].b - q[i].b)) > 8) differ++;
            Destroy(x); Destroy(y); return differ / (float)p.Length;
        }

        IEnumerator Continuum(MpmWorld world, Camera camera, SceneResult r)
        {
            var a = world.asset; var t = world.transform; frames = 0; cpu = gpu = 0;
            // Front-most moving particle near the line from the viewer at the origin through the part's centre.
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (var p in a.particles) { Vector3 w = t.TransformPoint(a.localFromSim.MultiplyPoint3x4(p)); lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w); }
            Vector3 centre = (lo + hi) / 2, toward = Vector3.ProjectOnPlane(centre, Vector3.up).normalized;
            float best = float.MaxValue; Vector3 front = centre;
            foreach (var p in a.particles)
            {
                Vector3 w = t.TransformPoint(a.localFromSim.MultiplyPoint3x4(p));
                Vector3 lateral = Vector3.ProjectOnPlane(w - centre, toward);
                if (lateral.magnitude > .2f * (hi - lo).magnitude) continue;
                float along = Vector3.Dot(w - centre, toward); if (along < best) { best = along; front = w; }
            }
            if (camera != null)
            {
                Vector3 look = (lo + hi) / 2; float size = (hi - lo).magnitude;
                camera.transform.position = look - toward * Mathf.Max(.35f, 1.3f * size) + Vector3.up * Mathf.Max(.15f, .5f * size); camera.transform.LookAt(look);
            }
            world.targetFrameMs = 1000; world.useTestContacts = true; world.testSpheres = Array.Empty<Vector4>(); world.ResetSimulation();
            yield return Frames(20, world, r);
            Screenshot($"Logs/physflow/{a.scene}-rest.png");
            float radius = .012f, depth = Mathf.Min(.04f, .25f * a.metersPerUnit);
            for (int f = 0; f < 60; f++)
            {
                Vector3 p = front + toward * Mathf.Lerp(-radius - .03f, depth - radius, Mathf.SmoothStep(0, 1, f / 59f));
                world.testSpheres = new[] { new Vector4(p.x, p.y, p.z, radius) };
                yield return Frames(1, world, r);
                r.peakReactionN = Mathf.Max(r.peakReactionN, world.ReactionForce);
                if (f % 10 == 9) { yield return Fresh(world); Track(world, r, ref r.peakRmsMm, ref r.peakStrain); r.slowPenetrationMm = Mathf.Max(r.slowPenetrationMm, world.PenetrationDepth * 1000); }
            }
            Screenshot($"Logs/physflow/{a.scene}-poke.png");
            world.testSpheres = Array.Empty<Vector4>();
            if (Environment.GetEnvironmentVariable("MPM_TRACE") == "1")
                for (int f = 0; f < 18; f++) { yield return Frames(10, world, r); yield return Fresh(world); Debug.Log($"MPM trace {a.scene} frame {f * 10 + 10} rms {world.RmsDisplacement * 1000:F2} mm p95 {world.P95Displacement * 1000:F2} max {world.MaxDisplacement * 1000:F2}"); }
            else yield return Frames(180, world, r);
            yield return Fresh(world); float strain = 0; r.releasedRmsMm = 0; Track(world, r, ref r.releasedRmsMm, ref strain);
            r.releasedP95Mm = world.P95Displacement * 1000; r.releasedMaxMm = world.MaxDisplacement * 1000; r.brokenFraction = world.BrokenFraction;
            r.recovered = r.releasedRmsMm < .5f * r.peakRmsMm;
            Screenshot($"Logs/physflow/{a.scene}-released.png");
            // Fast poke (~0.6 m/s, a quick finger jab): the depth any particle sits inside the probe.
            for (int f = 0; f < 24; f++)
            {
                Vector3 p = front + toward * Mathf.Lerp(-radius - .03f, depth - radius, Mathf.Min(1, f / 8f));
                world.testSpheres = new[] { new Vector4(p.x, p.y, p.z, radius) };
                yield return Frames(1, world, r);
                if (f >= 8 && f % 4 == 0) { yield return Fresh(world); r.fastPenetrationMm = Mathf.Max(r.fastPenetrationMm, world.PenetrationDepth * 1000); }
            }
            Screenshot($"Logs/physflow/{a.scene}-jab.png");
            world.testSpheres = Array.Empty<Vector4>();
            yield return Frames(60, world, r);
            // Paper interaction from rest; the CFL cap may run it in slow motion (timeScale < 1).
            world.useTestContacts = true; world.Replay();
            float shot = Mathf.Min(1, a.replaySeconds * .5f); bool taken = false;
            for (int f = 0; f < 900 && world.SimTime < a.replaySeconds; f++)
            {
                yield return Frames(1, world, r);
                if (!taken && world.SimTime >= shot) { taken = true; Screenshot($"Logs/physflow/{a.scene}-paper-mid.png"); }
                if (f % 30 == 29) { yield return Fresh(world); Track(world, r, ref r.paperRmsMm, ref r.paperStrain); }
            }
            Screenshot($"Logs/physflow/{a.scene}-paper-end.png");
            r.substeps = world.Substeps; r.timeScale = world.TimeScale;
            world.ResetSimulation(); world.useTestContacts = false;
        }
        int frames; double cpu, gpu;
        IEnumerator Frames(int n, MpmWorld world, SceneResult r)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0) { r.maxGpuMs = Mathf.Max(r.maxGpuMs, (float)timing[0].gpuFrameTime); gpu += timing[0].gpuFrameTime; }
                cpu += world.SolverMs; frames++;
                r.meanCpuMs = (float)(cpu / frames); r.meanGpuMs = (float)(gpu / frames);
            }
        }
        // Quality comes from an async particle readback; wait for the answer to this request.
        static IEnumerator Fresh(MpmWorld world)
        {
            int seen = world.QualityResults; world.RequestQuality();
            for (int i = 0; i < 60 && world.QualityResults == seen; i++) yield return null;
        }
        static void Track(MpmWorld world, SceneResult r, ref float rmsMm, ref float strain)
        {
            float rms = world.RmsDisplacement * 1000;
            if (float.IsNaN(rms) || float.IsInfinity(rms) || float.IsNaN(world.StrainMeasure)) { r.finite = false; return; }
            rmsMm = Mathf.Max(rmsMm, rms); strain = Mathf.Max(strain, world.StrainMeasure);
        }
        void Screenshot(string path)
        {
            var camera = Camera.main; if (camera == null) return;
            var target = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32); target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var old = RenderTexture.active; RenderTexture.active = target; var shot = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG()); RenderTexture.active = old; Destroy(shot); target.Release(); Destroy(target);
        }
    }
}
