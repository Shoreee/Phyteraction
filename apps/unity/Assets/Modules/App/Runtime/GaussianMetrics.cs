using PhysicsHands.Simulation;
using PhysicsHands.GaussianData;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PhysicsHands.App
{
    // One CSV per activation of a Gaussian simulation; the readout is a convenience view of it.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "GaussianMetrics")]
    public sealed class GaussianMetrics : MonoBehaviour
    {
        // Optional in-world readout; the CSV remains the measurement of record.
        public TextMesh display;
        public string OutputPath
        {
            get;
            private set;
        }

        ISimulationDiagnostics simulation;
        readonly FrameTiming[] timing = new FrameTiming[1];
        readonly StringBuilder rows = new();
        readonly float[] window = new float[256];
        readonly float[] sorted = new float[256];
        int windowCount, windowHead;
        string header;
        StreamWriter writer;
        float nextQuality, nextFlush, nextDisplay, gpuMs, solverMs, reaction;
        void OnEnable()
        {
            simulation = GetComponent<ISimulationDiagnostics>();
            windowCount = 0;
            if (display != null && header == null)
                header = display.text;
        }

        void Open()
        {
            OutputPath = Path.Combine(Application.persistentDataPath, "gaussian-" + name.Replace(' ', '-').ToLowerInvariant() + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".csv");
            writer = new StreamWriter(OutputPath, false, Encoding.UTF8);
            writer.WriteLine("# " + SystemInfo.deviceModel + "; " + SystemInfo.graphicsDeviceName + "; " + Application.unityVersion + "; " + simulation.Provenance);
            writer.WriteLine("# " + simulation.Summary);
            writer.WriteLine("frame,step,realtime_s,frame_ms,cpu_frame_ms,gpu_frame_ms,solver_cpu_ms,contact_sample_age_ms,shapes,contact_pairs,reaction_n,rms_displacement_m,strain,substeps,time_scale");
            Debug.Log("Gaussian metrics: " + OutputPath);
        }

        void LateUpdate()
        {
            if (simulation == null || !simulation.Ready)
                return;
            if (writer == null)
                Open();
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(1, timing);
            double now = Time.realtimeSinceStartupAsDouble;
            string cpu = count > 0 && timing[0].cpuFrameTime > 0 ? F(timing[0].cpuFrameTime) : "";
            string gpu = count > 0 && timing[0].gpuFrameTime > 0 ? F(timing[0].gpuFrameTime) : "";
            if (Time.unscaledTime >= nextQuality)
            {
                nextQuality = Time.unscaledTime + 1;
                simulation.RequestQuality();
            }

            rows.AppendLine($"{Time.frameCount},{simulation.StepId},{F(now)},{F(Time.unscaledDeltaTime * 1000)},{cpu},{gpu},{F(simulation.SolverMs)},{F((now - simulation.ContactSampleTime) * 1000)},{simulation.ShapeCount},{simulation.PairCount},{F(simulation.ReactionForce)},{F(simulation.RmsDisplacement)},{F(simulation.StrainMeasure)},{simulation.Substeps},{F(simulation.TimeScale)}");
            if (Time.unscaledTime >= nextFlush)
            {
                nextFlush = Time.unscaledTime + 2;
                writer.Write(rows);
                rows.Clear();
                writer.Flush();
            }

            if (display != null)
                UpdateDisplay(count > 0 ? (float)timing[0].gpuFrameTime : 0);
        }

        void UpdateDisplay(float gpu)
        {
            window[windowHead] = Time.unscaledDeltaTime * 1000;
            windowHead = (windowHead + 1) % window.Length;
            windowCount = Mathf.Min(windowCount + 1, window.Length);
            if (gpu > 0)
                gpuMs = gpu;
            solverMs = Mathf.Max(solverMs * .95f, (float)simulation.SolverMs);
            reaction = Mathf.Max(reaction * .9f, simulation.ReactionForce);
            if (Time.unscaledTime < nextDisplay || windowCount == 0)
                return;
            nextDisplay = Time.unscaledTime + .5f;
            Array.Copy(window, sorted, windowCount);
            Array.Sort(sorted, 0, windowCount);
            float median = sorted[windowCount / 2], p95 = sorted[Mathf.Min(windowCount - 1, (int)(windowCount * .95f))];
            display.text = header + string.Format(CultureInfo.InvariantCulture, "\nframe {0:F1} / p95 {1:F1} ms ({2:F0} fps)   gpu {3:F1} ms   solver cpu {4:F2} ms\nshapes {5}   pairs {6}   reaction {7:F3} N   rms {8:F1} mm   strain {9:F3}", median, p95, 1000 / Mathf.Max(median, .01f), gpuMs, solverMs, simulation.ShapeCount, simulation.PairCount, reaction, simulation.RmsDisplacement * 1000, simulation.StrainMeasure);
        }

        static string F(double x) => x.ToString("F6", CultureInfo.InvariantCulture);
        void Close()
        {
            if (writer == null)
                return;
            writer.Write(rows);
            rows.Clear();
            writer.Dispose();
            writer = null;
        }

        void OnDisable()
        {
            Close();
            if (display != null && header != null)
                display.text = header;
        }
    }
}
