using PhysicsHands.Simulation;
using PhysicsHands.GaussianData;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PhysicsHands.Editor.Import
{
    // Reimport the authored flower asset and its learned modal data; does not generate scenes.
    public static class FlowerImporter
    {
        [Serializable]
        class MaterialBake
        {
            public float sim_scale;
        }

        public const string AssetPath = ProjectAssets.Flower;
        [MenuItem("Physics Hands/Gaussian World/Reimport Learned Flower")]
        public static void Reimport()
        {
            var settings = AssetDatabase.LoadAssetAtPath<FlowerBakeSettings>(ProjectAssets.FlowerBakeSettings);
            if (settings == null || settings.target == null)
                throw new InvalidOperationException("Configure Flower Bake Settings before reimporting.");
            var asset = ImportFlowerAsset(AssetDatabase.GetAssetPath(settings.target));
            BakeModes(asset, settings);
            AssetDatabase.SaveAssets();
        }

        public static ModalModelAsset ImportFlowerAsset(string assetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(RepositoryPaths.UnityRoot, assetPath)));
            AssetDatabase.Refresh();
            string path = RepositoryPaths.Data("processed", "flower", "carnation-baked.ply"), bake = RepositoryPaths.Data("processed", "flower", "material-bake.json");
            if (!File.Exists(path) || !File.Exists(bake))
                throw new FileNotFoundException("Run prepare_flower.py and bake_material.py first.", path);
            var calibration = JsonUtility.FromJson<MaterialBake>(File.ReadAllText(bake));
            var imported = GaussianPly.Read(path, sourceUnitsPerSimUnit: calibration.sim_scale);
            imported.name = Path.GetFileNameWithoutExtension(assetPath);
            imported.provenance = "PhysDreamer / carnations; author GS + clean_object_points mask; first-order SH; learned E field + moving-part boundary from models.zip; hashes in Research/GaussianWorld/data";
            imported.renderData = GaussianAssetWriter.SaveRenderData(assetPath, imported.renderData);
            var asset = AssetDatabase.LoadAssetAtPath<ModalModelAsset>(assetPath);
            if (asset == null)
            {
                asset = imported;
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                EditorUtility.CopySerialized(imported, asset);
                Object.DestroyImmediate(imported);
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        [Serializable]
        class ModeBake
        {
            public string cage_sha256;
            public float[] frequencies_hz;
        }

        // Exports the cage with its learned Lame parameters and solves K phi = omega^2 M phi offline
        // (tools/bake_modes.py); reuses the previous solve when the cage bytes are unchanged.
        public static void BakeModes(ModalModelAsset asset, FlowerBakeSettings settings)
        {
            string cagePath = RepositoryPaths.Data("processed", "flower", "cage.bin"), modesPath = RepositoryPaths.Data("processed", "flower", "modes.bin"), reportPath = RepositoryPaths.Data("processed", "flower", "modes.json");
            Directory.CreateDirectory(Path.GetDirectoryName(cagePath));
            int modes = settings.modeCount;
            var cage = new ElasticCage(asset, settings.voxelSize, settings.totalMass, settings.fallbackYoungs);
            using (var w = new BinaryWriter(File.Create(cagePath)))
            {
                w.Write(cage.rest.Length);
                w.Write(cage.tets.Length);
                foreach (var p in cage.rest)
                {
                    w.Write(p.x);
                    w.Write(p.y);
                    w.Write(p.z);
                }

                foreach (float m in cage.inverseMass)
                    w.Write(m);
                foreach (var t in cage.tets)
                {
                    w.Write(t.x);
                    w.Write(t.y);
                    w.Write(t.z);
                    w.Write(t.w);
                }

                foreach (float m in cage.mu)
                    w.Write(m);
                foreach (float l in cage.lambda)
                    w.Write(l);
            }

            string digest;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                digest = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(cagePath))).Replace("-", "").ToLowerInvariant();
            bool cached = File.Exists(modesPath) && File.Exists(reportPath) && JsonUtility.FromJson<ModeBake>(File.ReadAllText(reportPath)).cage_sha256 == digest && new FileInfo(modesPath).Length == 8 + 4L * modes * (1 + cage.rest.Length * 3);
            if (!cached)
            {
                var start = new System.Diagnostics.ProcessStartInfo(RepositoryPaths.ResolveExecutable(settings.pythonExecutable))
                {UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true};
                start.ArgumentList.Add(RepositoryPaths.Tool("modal", "bake_modes.py"));
                start.ArgumentList.Add(cagePath);
                start.ArgumentList.Add(modesPath);
                start.ArgumentList.Add(modes.ToString());
                using var process = System.Diagnostics.Process.Start(start);
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                string output = stdout.GetAwaiter().GetResult(), error = stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new Exception("bake_modes.py failed: " + error);
                Debug.Log("Gaussian modes baked: " + output.Trim());
            }

            using (var r = new BinaryReader(File.OpenRead(modesPath)))
            {
                int k = r.ReadInt32(), nodes = r.ReadInt32();
                if (nodes != cage.rest.Length)
                    throw new Exception("Mode bake does not match the cage.");
                asset.modeOmega = new float[k];
                for (int i = 0; i < k; i++)
                    asset.modeOmega[i] = r.ReadSingle();
                asset.modeShapes = new float[nodes * 3 * k];
                for (int i = 0; i < asset.modeShapes.Length; i++)
                    asset.modeShapes[i] = r.ReadSingle();
            }

            asset.modeVoxel = settings.voxelSize;
            asset.modeMass = settings.totalMass;
            EditorUtility.SetDirty(asset);
        }
    }
}
