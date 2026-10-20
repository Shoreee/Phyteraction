using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PhysicsHands.Editor.GaussianWorld
{
    // Research-only geometry bridge. The ORCA release contains FBX meshes, not editable .st9
    // generators or a trusted botanical branch graph. Export the actual imported topology so
    // downstream structural inference can be audited rather than guessed in a runtime shader.
    public static class SpeedTreeMeshExport
    {
        const string DefaultSource = "Assets/_Project/Imported/SpeedTreeORCA/BostonFern/Boston_Fern_LowPoly.fbx";
        const string DefaultOutput = "Research/SpeedTree/generated/boston_fern_mesh.json";

        [Serializable] sealed class Part
        {
            public string name;
            public string[] materials;
            public Vector3[] vertices;
            public int[] triangles;
            public int[] triangleMaterials;
        }
        [Serializable] sealed class Export
        {
            public string source, license;
            public string units = "Unity imported units; metric scale must be calibrated before physics";
            public Part[] parts;
        }

        [MenuItem("Physics Hands/Gaussian World/Export ORCA Boston Fern Geometry")]
        public static void BostonFern() => ExportAsset(DefaultSource, DefaultOutput, "CC BY-NC-SA 3.0; SpeedTree via NVIDIA ORCA");

        // Batch example: PLANT_ASSET_PATH=Assets/.../model.fbx PLANT_EXPORT_PATH=Research/.../model.json
        //               PLANT_LICENSE='...' Unity -batchmode -executeMethod ...ExportFromEnvironment
        public static void ExportFromEnvironment()
        {
            string source = Environment.GetEnvironmentVariable("PLANT_ASSET_PATH");
            string output = Environment.GetEnvironmentVariable("PLANT_EXPORT_PATH");
            string license = Environment.GetEnvironmentVariable("PLANT_LICENSE");
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(license))
                throw new ArgumentException("PLANT_ASSET_PATH, PLANT_EXPORT_PATH and PLANT_LICENSE are required.");
            ExportAsset(source, output, license);
        }

        public static void ExportAsset(string source, string output, string license)
        {
            var importer = AssetImporter.GetAtPath(source);
            if (importer == null)
                throw new FileNotFoundException("Import the FBX or SpeedTree model first.", source);
            if (importer is ModelImporter model && !model.isReadable) { model.isReadable = true; model.SaveAndReimport(); }
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (root == null) throw new InvalidDataException("Unity did not import the model.");
            var parts = new List<Part>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh; if (mesh == null) continue;
                // Include the FBX root's axis conversion and scale. Stripping that transform
                // produced a sideways, ~42-unit fern even though the Unity prefab is smaller.
                var matrix = filter.transform.localToWorldMatrix;
                if (!mesh.isReadable) throw new InvalidDataException($"{filter.name}: enable Read/Write on the imported mesh before export.");
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                var renderer = filter.GetComponent<MeshRenderer>();
                var names = new string[mesh.subMeshCount];
                for (int i = 0; i < names.Length; i++)
                    names[i] = renderer != null && i < renderer.sharedMaterials.Length && renderer.sharedMaterials[i] != null
                        ? renderer.sharedMaterials[i].name : "(unassigned)";
                var triangles = new List<int>(); var triangleMaterials = new List<int>();
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var indices = mesh.GetTriangles(i);
                    triangles.AddRange(indices);
                    for (int t = 0; t < indices.Length / 3; t++) triangleMaterials.Add(i);
                }
                parts.Add(new Part { name = filter.name, materials = names, vertices = vertices,
                    triangles = triangles.ToArray(), triangleMaterials = triangleMaterials.ToArray() });
            }
            if (parts.Count == 0) throw new InvalidDataException("No mesh parts in imported model.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(new Export { source = source, license = license, parts = parts.ToArray() }, true));
            Debug.Log($"Plant geometry exported: {parts.Count} parts, {output}");
        }
    }
}
