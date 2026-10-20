using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PhysicsHands.GaussianWorld;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PhysicsHands.Editor.GaussianWorld
{
    // GPU cost per PhysFlow scene, measured as wall time of queued work followed by a blocking
    // readback: one MPM step (with a probe touching the object), the splat sort, and one eye of
    // splat drawing at visionOS resolution from a seated and a close viewpoint. Logs/physflow/gpu-bench.json.
    [InitializeOnLoad]
    public static class GpuBenchmark
    {
        const string Key = "GaussianWorld.GpuBenchmark";
        static GpuBenchmark()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) new GameObject("GPU Benchmark").AddComponent<GpuProbe>();
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key, false))
                {
                    SessionState.SetBool(Key, false);
                    if (Application.isBatchMode) EditorApplication.Exit(File.Exists(GpuProbe.Output) ? 0 : 1);
                }
            };
        }
        [MenuItem("Physics Hands/Gaussian World/Run GPU Benchmark")]
        public static void Run() { UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GaussianWorldBuilder.ScenePath); SessionState.SetBool(Key, true); EditorApplication.isPlaying = true; }
    }

    public sealed class GpuProbe : MonoBehaviour
    {
        public const string Output = "Logs/physflow/gpu-bench.json";
        // Per-eye drawable of Apple Vision Pro with foveation off is larger; this is the typical foveated size.
        const int Repeats = 12;
        // GPU_BENCH_SCALE < 1 shrinks the eye: draw time that does not shrink with it is per primitive, not per pixel.
        static readonly float Scale = float.TryParse(Environment.GetEnvironmentVariable("GPU_BENCH_SCALE"), out float s) ? s : 1;
        static readonly int EyeWidth = Mathf.Max(8, (int)(1920 * Scale)), EyeHeight = Mathf.Max(8, (int)(1824 * Scale));
        [Serializable] public class SceneResult
        {
            public string scene; public int splats, visibleSplats, particles, substeps;
            public float stepMs, sortMs, sortBackdropMs, seatedDrawMs, closeDrawMs, seatedBackdropDrawMs, emptyMs;
        }
        [Serializable] public class Result { public string device, note; public SceneResult[] scenes; }

        IEnumerator Start()
        {
            Directory.CreateDirectory("Logs/physflow");
            yield return null;
            var selector = FindAnyObjectByType<GaussianSceneSelector>();
            foreach (var hand in FindObjectsByType<PhysicsHands.Hands.HoloHand>(FindObjectsSortMode.None))
                foreach (var r in hand.GetComponentsInChildren<Renderer>()) r.enabled = false;
            var eye = new GameObject("Benchmark eye").AddComponent<Camera>();
            eye.enabled = false; eye.fieldOfView = 90; eye.nearClipPlane = .02f; eye.clearFlags = CameraClearFlags.SolidColor; eye.backgroundColor = Color.clear;
            var target = new RenderTexture(EyeWidth, EyeHeight, 24, RenderTextureFormat.ARGBHalf); target.Create();
            eye.aspect = EyeWidth / (float)EyeHeight;
            var results = new List<SceneResult>();
            string only = Environment.GetEnvironmentVariable("PHYSFLOW_SCENES");
            for (int i = 0; selector != null && i < selector.scenes.Length; i++)
            {
                if (!selector.scenes[i].TryGetComponent(out MpmWorld world)) continue;
                if (!string.IsNullOrEmpty(only) && Array.IndexOf(only.Split(','), world.asset.scene) < 0) continue;
                selector.Select(i); yield return null; yield return null;
                var a = world.asset; var t = world.transform;
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                foreach (var p in a.particles) { Vector3 w = t.TransformPoint(a.localFromSim.MultiplyPoint3x4(p)); lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w); }
                Vector3 centre = (lo + hi) / 2; float size = (hi - lo).magnitude;
                Vector3 toward = Vector3.ProjectOnPlane(centre, Vector3.up).normalized; if (toward == Vector3.zero) toward = Vector3.forward;
                world.targetFrameMs = 1000; world.useTestContacts = true; world.ResetSimulation();
                // Probe resting against the front of the object so contact work is included.
                Vector3 front = centre - toward * (.5f * size);
                world.testSpheres = new[] { new Vector4(front.x, front.y, front.z, .02f) };
                for (int f = 0; f < 15; f++) yield return null;
                var r = new SceneResult { scene = a.scene, splats = a.splats.Length, particles = a.particles.Length, substeps = world.Substeps };
                var sync = new float[44];
                r.stepMs = Measure(() => Graphics.ExecuteCommandBuffer(world.StepCommands), () => world.ParticleBuffer.GetData(sync, 0, 0, 44));
                world.ResetSimulation(); world.testSpheres = Array.Empty<Vector4>(); world.useTestContacts = false;
                yield return null;
                var splatSync = new float[20];
                Action splatWait = () => world.Splats.OrderBuffer.GetData(splatSync, 0, 0, 20);
                // Seated: the validation pose; close: the eye 10 cm in front of the object's surface.
                Place(eye, centre - toward * Mathf.Max(.35f, 1.3f * size) + Vector3.up * Mathf.Max(.15f, .5f * size), centre);
                SplatRenderer.Backdrop = false; r.visibleSplats = world.Splats.Visible;
                r.sortMs = Measure(() => world.Splats.Sort(eye), splatWait);
                eye.cullingMask = 0; r.emptyMs = Measure(() => Render(eye, target), Wait(target));
                eye.cullingMask = ~0;
                r.seatedDrawMs = Measure(() => Render(eye, target), Wait(target)) - r.emptyMs;
                SplatRenderer.Backdrop = true;
                r.sortBackdropMs = Measure(() => world.Splats.Sort(eye), splatWait);
                r.seatedBackdropDrawMs = Measure(() => Render(eye, target), Wait(target)) - r.emptyMs - (r.sortBackdropMs - r.sortMs);
                SplatRenderer.Backdrop = false;
                Place(eye, centre - toward * (.5f * size + .1f), centre);
                r.closeDrawMs = Measure(() => Render(eye, target), Wait(target)) - r.emptyMs;
                if (Environment.GetEnvironmentVariable("GPU_BENCH_SHOTS") == "1") Save(target, $"Logs/physflow/bench-{a.scene}-close.png");
                results.Add(r);
                UnityEngine.Debug.Log("GPU benchmark " + JsonUtility.ToJson(r));
            }
            target.Release();
            File.WriteAllText(Output, JsonUtility.ToJson(new Result
            {
                device = SystemInfo.graphicsDeviceName, scenes = results.ToArray(),
                note = $"Wall ms per operation over {Repeats} queued repeats; draw = render minus empty camera (sort included in render is subtracted). One eye {EyeWidth}x{EyeHeight}, fov 90."
            }, true));
            EditorApplication.isPlaying = false;
        }
        static void Place(Camera c, Vector3 position, Vector3 look) { c.transform.position = position; c.transform.LookAt(look); }
        static void Render(Camera c, RenderTexture target) => RenderPipeline.SubmitRenderRequest(c, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        static Action Wait(RenderTexture target) => () => AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
        static float Measure(Action work, Action wait)
        {
            work(); wait();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < Repeats; i++) work();
            wait();
            return (float)(watch.Elapsed.TotalMilliseconds / Repeats);
        }
        static void Save(RenderTexture rt, string path)
        {
            var old = RenderTexture.active; RenderTexture.active = rt;
            var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false); shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG()); RenderTexture.active = old; Destroy(shot);
        }
    }
}
