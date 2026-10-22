using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PhysicsHands.GaussianData;
using UnityEditor;
using UnityEngine;

namespace PhysicsHands.Editor.Import
{
    /// <summary>Imports the official one-scalar-per-material training result with source identity checks.</summary>
    public static class PhysFlowTrainedMaterial
    {
        [Serializable] sealed class Parameters
        {
            public float E = -1, nu = -1, yield_stress = -1, plastic_viscosity = -1, friction_angle = -1;
        }
        [Serializable] sealed class Training
        {
            public int schema;
            public string scene, material, parameter_origin, training_commit, checkpoint_sha256, config_sha256, source_sha256, prepared_sha256;
            public Parameters parameters;
        }
        [Serializable] sealed class Preparation { public string output_sha256, source_sha256; }
        [Serializable] sealed class Rates { public float[] E, nu, yield_stress, plastic_viscosity, friction_angle; }
        [Serializable] sealed class Config { public string material; public Rates lr; }
        static bool Hex(string value, int length) => value != null && value.Length == length && System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-f]+$");
        static string FileSha(string path)
        {
            using var hash = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static string Sha(string value)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        public static bool Apply(MpmModelAsset asset, string scene, string configText, string preparationText)
        {
            string path = Path.Combine(PhysFlowImporter.DataRoot, scene + ".trained-material.json");
            if (!File.Exists(path)) return false;
            var trained = JsonUtility.FromJson<Training>(File.ReadAllText(path));
            var preparation = JsonUtility.FromJson<Preparation>(preparationText);
            var config = JsonUtility.FromJson<Config>(configText);
            if (trained.schema != 1 || trained.scene != scene || trained.material != config.material ||
                trained.parameter_origin != "physflow-trained-scalars" || trained.config_sha256 != Sha(configText) ||
                trained.source_sha256 != preparation.source_sha256 || trained.source_sha256 != asset.sourceSha256 || trained.prepared_sha256 != preparation.output_sha256 ||
                trained.prepared_sha256 != FileSha(Path.Combine(PhysFlowImporter.DataRoot, scene + ".bin")) ||
                trained.training_commit != "6beba6746fad4590f5b63ce6fa85c3b862b182d9" || !Hex(trained.checkpoint_sha256, 64) || trained.parameters == null)
                throw new InvalidDataException($"{scene}: trained material does not match this source/config/prepared payload.");
            var p = trained.parameters;
            if (config.lr == null || config.lr.E != null && p.E <= 0 || config.lr.nu != null && p.nu < 0 ||
                config.lr.yield_stress != null && p.yield_stress < 0 || config.lr.plastic_viscosity != null && p.plastic_viscosity < 0 ||
                config.lr.friction_angle != null && p.friction_angle < 0)
                throw new InvalidDataException($"{scene}: checkpoint omits a configured optimized parameter.");
            foreach (float value in new[] {p.E, p.nu, p.yield_stress, p.plastic_viscosity, p.friction_angle})
                if (!float.IsFinite(value) || value < 0 && value != -1) throw new InvalidDataException($"{scene}: non-finite trained parameter.");
            if (p.E == 0 || p.nu >= .5f || p.friction_angle >= 90)
                throw new InvalidDataException($"{scene}: trained material is outside the constitutive domain.");
            if (p.E >= 0) asset.youngs = p.E * 1e7f;
            if (p.nu >= 0) asset.poisson = p.nu;
            if (p.yield_stress >= 0) asset.yieldStress = p.yield_stress * 1e5f;
            if (p.plastic_viscosity >= 0) asset.viscosity = p.plastic_viscosity * 1e4f;
            if (p.friction_angle >= 0) asset.frictionAngle = p.friction_angle;
            asset.materialOrigin = "physflow-trained-scalars";
            asset.trainingCommit = trained.training_commit;
            asset.materialCheckpointSha256 = trained.checkpoint_sha256;
            return true;
        }
        [MenuItem("Physics Hands/Gaussian World/Import Trained PhysFlow Materials")]
        public static void ImportAvailable()
        {
            int imported = 0;
            foreach (string scene in PhysFlowImporter.Scenes)
            {
                var asset = AssetDatabase.LoadAssetAtPath<MpmModelAsset>($"{PhysFlowImporter.AssetFolder}/{scene}.asset");
                if (asset == null) continue;
                if (!Apply(asset, scene, File.ReadAllText($"{PhysFlowImporter.ConfigRoot}/{scene}_config.json"), File.ReadAllText($"{PhysFlowImporter.DataRoot}/{scene}.json"))) continue;
                EditorUtility.SetDirty(asset); imported++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Imported {imported} verified PhysFlow trained material results; Gaussian and geometry payloads were retained.");
        }
    }
}
