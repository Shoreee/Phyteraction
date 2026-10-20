using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.VisionOS;
using PhysicsHands.Editor.GaussianWorld;
using UnityEngine;

namespace PhysicsHands.Editor.Build
{
    /// <summary>
    /// Exports the Metal visionOS Xcode project. Play to Device stays on RealityKit and
    /// does not drive these physics hands; the device build is the hand-tracking path.
    /// </summary>
    public static class VisionOSDeviceBuild
    {
        public const string RequestPath = "Logs/build-visionos-device";
        public const string ResultPath = "Logs/visionos-build-result.txt";
        public const string OutputPath = "Builds/VisionOS";

        [InitializeOnLoadMethod]
        static void Watch()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!File.Exists(RequestPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                return;
            }

            File.Delete(RequestPath);
            EditorApplication.update -= Poll;
            EditorApplication.delayCall += Build;
        }

        [MenuItem("Physics Hands/Build/visionOS Xcode Project")]
        public static void Build()
        {
            Directory.CreateDirectory("Logs");
            try
            {
                File.WriteAllText(ResultPath, "BUILDING");
                GaussianWorldBuilder.CreateScene();
                PlayerSettings.enableFrameTimingStats = true;
                GazeHoverBuild.ConfigurePlugin();
                VisionOSSettings settings = VisionOSSettings.GetOrCreateSettings();
                settings.appMode = VisionOSSettings.AppMode.Metal;
                settings.metalImmersionStyle = VisionOSSettings.ImmersionStyle.Mixed;
                settings.handsTrackingUsageDescription = "Your hands interact with reconstructed Gaussian scenes.";
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/_Project/Scenes/GaussianWorld.unity" },
                    locationPathName = OutputPath,
                    target = BuildTarget.VisionOS,
                    targetGroup = BuildTargetGroup.VisionOS,
                    options = BuildOptions.Development,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                if (summary.result == BuildResult.Succeeded)
                    File.WriteAllText(ResultPath, "OK\n" + Path.GetFullPath(OutputPath));
                else
                    File.WriteAllText(ResultPath, "FAIL\n" + summary.result + " errors=" + summary.totalErrors);
                Debug.Log("[PhysicsHands] visionOS build " + summary.result + " errors=" + summary.totalErrors);
            }
            catch (Exception exception)
            {
                File.WriteAllText(ResultPath, "FAIL\n" + exception);
                Debug.LogException(exception);
            }
        }
    }
}
