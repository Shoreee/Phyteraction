using PhysicsHands.GaussianData;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.VisionOS;
using UnityEngine;

namespace PhysicsHands.Editor.Build
{
    /// <summary>
    /// Exports the Metal visionOS Xcode project. Play to Device stays on RealityKit and
    /// does not drive these physics hands; the device build is the hand-tracking path.
    /// </summary>
    public static class VisionOSDeviceBuild
    {
        public const string ScenePath = ProjectAssets.MainScene;
        public static string ResultPath => RepositoryPaths.Artifact("reports", "visionos-build-result.txt");
        public static string OutputPath => RepositoryPaths.Artifact("builds", "visionOS");
        [MenuItem("Physics Hands/Build/visionOS Xcode Project")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            try
            {
                File.WriteAllText(ResultPath, "BUILDING");
                // Build the scene already prepared in this local project. Reimporting
                // here would overwrite authored scene and Gaussian asset changes.
                if (!File.Exists(Path.Combine(RepositoryPaths.UnityRoot, ScenePath)))
                    throw new FileNotFoundException("The authored GaussianWorld scene is missing.", ScenePath);
                PlayerSettings.enableFrameTimingStats = true;
                VisionOSSettings settings = VisionOSSettings.GetOrCreateSettings();
                settings.appMode = VisionOSSettings.AppMode.Metal;
                settings.metalImmersionStyle = VisionOSSettings.ImmersionStyle.Mixed;
                settings.handsTrackingUsageDescription = "Your hands interact with physical objects and reconstructed Gaussian scenes.";
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                var options = new BuildPlayerOptions{scenes = new[]{ScenePath}, locationPathName = OutputPath, target = BuildTarget.VisionOS, targetGroup = BuildTargetGroup.VisionOS, options = BuildOptions.Development, };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                bool succeeded = summary.result == BuildResult.Succeeded && summary.totalErrors == 0;
                if (succeeded)
                    File.WriteAllText(ResultPath, "OK\n" + Path.GetFullPath(OutputPath));
                else
                    File.WriteAllText(ResultPath, "FAIL\n" + summary.result + " errors=" + summary.totalErrors);
                Debug.Log("[PhysicsHands] visionOS build " + summary.result + " errors=" + summary.totalErrors);
                if (!succeeded)
                    throw new UnityEditor.Build.BuildFailedException("visionOS export failed: " + summary.result + " errors=" + summary.totalErrors);
            }
            catch (Exception exception)
            {
                File.WriteAllText(ResultPath, "FAIL\n" + exception);
                Debug.LogException(exception);
                throw;
            }
        }
    }
}
