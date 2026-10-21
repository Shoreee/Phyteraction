using System.IO;
using UnityEngine;

namespace PhysicsHands.Editor
{
    internal static class RepositoryPaths
    {
        internal static string UnityRoot => Directory.GetParent(Application.dataPath).FullName;
        internal static string Root => Path.GetFullPath(Path.Combine(UnityRoot, "../.."));
        internal static string Data(string stage, string dataset, string file = "") => Path.Combine(Root, "data", stage, dataset, file);
        internal static string Artifact(string directory, string file) => Path.Combine(Root, "artifacts", directory, file);
        internal static string Tool(string directory, string file) => Path.Combine(Root, "tools", directory, file);
        internal static string ResolveExecutable(string executable) => executable.Contains("/") || executable.Contains("\\") ? Path.GetFullPath(Path.Combine(Root, executable)) : executable;
    }

    internal static class ProjectAssets
    {
        internal const string GaussianRoot = "Assets/Content/Gaussians";
        internal const string MainScene = "Assets/Scenes/GaussianWorld.unity";
        internal const string Flower = GaussianRoot + "/Flower.asset";
        internal const string FlowerBakeSettings = "Assets/Settings/FlowerBakeSettings.asset";
    }
}
