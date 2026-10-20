using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PhysicsHands.Editor.GaussianWorld
{
    // Gaze hover (Assets/Plugins/visionOS/GazeHover.mm) needs a tracking-areas texture, which Unity's
    // generated Compositor Services configuration never requests. Re-applied on every export.
    public sealed class GazeHoverBuild : IPostprocessBuildWithReport
    {
        const string Plugin = "Assets/Plugins/visionOS/GazeHover.mm";
        const string Config = "Libraries/com.unity.xr.visionos/Runtime/Plugins/visionos/UnityCompositorServicesConfiguration.swift";
        const string Anchor = "configuration.generateFlippedRasterizationRateMaps = enableFoveation";
        const string Patch = @"
        if #available(visionOS 26.0, *), capabilities.supportedTrackingAreasFormats.contains(.r8Uint) {
            configuration.trackingAreasFormat = .r8Uint
            configuration.trackingAreasUsage = [.renderTarget, .shaderRead]
        }";
        public int callbackOrder => 0;

        // Source plugin compiled into UnityFramework for the device only; call before building.
        public static void ConfigurePlugin()
        {
            if (AssetImporter.GetAtPath(Plugin) is not PluginImporter importer) { Debug.LogError("Gaze hover: plugin missing at " + Plugin); return; }
            if (importer.GetCompatibleWithPlatform(BuildTarget.VisionOS) && !importer.GetCompatibleWithAnyPlatform()) return;
            importer.SetCompatibleWithAnyPlatform(false); importer.SetCompatibleWithEditor(false);
            importer.SetCompatibleWithPlatform(BuildTarget.VisionOS, true); importer.SaveAndReimport();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.VisionOS) return;
            string path = Path.Combine(report.summary.outputPath, Config);
            if (!File.Exists(path)) { Debug.LogError("Gaze hover: compositor configuration not found at " + path); return; }
            string text = File.ReadAllText(path);
            if (text.Contains("trackingAreasFormat")) return;
            if (!text.Contains(Anchor)) { Debug.LogError("Gaze hover: compositor configuration changed; patch not applied."); return; }
            File.WriteAllText(path, text.Replace(Anchor, Anchor + Patch));
        }
    }
}
