using System;
using System.IO;
using System.Linq;
using PhysicsHands.GaussianData;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PhysicsHands.Editor.Import
{
    public static class SpeedTreeResearchImporter
    {
        const string DirectoryPath = "Assets/Content/SpeedTree/";
        [MenuItem("Physics Hands/Import/Restore SpeedTree Research Mesh")]
        public static void Import()
        {
            string path = DirectoryPath + "Bush_Desktop.st";
            if (!File.Exists(Path.Combine(RepositoryPaths.UnityRoot, path)))
                throw new FileNotFoundException("Run tools/preprocessing/fetch_speedtree.py first; the official resource is non-commercial research only.", path);
            var importer = AssetImporter.GetAtPath(path) as SpeedTreeImporter;
            if (importer == null) throw new InvalidOperationException("Unity did not recognize the official SpeedTree file.");
            foreach (var key in importer.GetExternalObjectMap().Keys.ToArray()) importer.RemoveRemap(key);
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var source = model.GetComponentsInChildren<MeshFilter>(true).OrderByDescending(m => m.sharedMesh.vertexCount).First().sharedMesh;
            var mesh = Object.Instantiate(source);
            mesh.name = "Official SpeedTree Bush LOD0 - metric research copy";
            var bounds = mesh.bounds;
            float scale = .45f / bounds.size.y;
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)) * scale;
            mesh.vertices = vertices; mesh.RecalculateBounds();
            Save(mesh, DirectoryPath + "ResearchTree.asset");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Official SpeedTree atlas - physical deformation" };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(DirectoryPath + "Textures/Bush_Desktop_Color.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(DirectoryPath + "Textures/Bush_Desktop_Normal.png"));
            var packed = PackMaterialChannels();
            material.SetTexture("_MetallicGlossMap", packed); material.SetTexture("_OcclusionMap", packed);
            material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.EnableKeyword("_OCCLUSIONMAP");
            material.EnableKeyword("_ALPHATEST_ON"); material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .33f);
            material.SetFloat("_Cull", 0); material.SetFloat("_Smoothness", 1); material.SetFloat("_Metallic", 1);
            material.SetOverrideTag("RenderType", "TransparentCutout"); material.renderQueue = (int)RenderQueue.AlphaTest;
            // A standard mesh shader avoids SpeedTree's camera-facing/LOD vertex motion, which
            // would detach the displayed leaves from the material-coordinate collision surface.
            Save(material, DirectoryPath + "ResearchTree.mat");
            const string bindingPath = "Assets/Settings/Project/TreeAppearance.asset";
            var binding = AssetDatabase.LoadAssetAtPath<MeshRenderAsset>(bindingPath);
            if (binding == null) { binding = ScriptableObject.CreateInstance<MeshRenderAsset>(); AssetDatabase.CreateAsset(binding, bindingPath); }
            binding.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(DirectoryPath + "ResearchTree.asset");
            binding.materials = new[] { AssetDatabase.LoadAssetAtPath<Material>(DirectoryPath + "ResearchTree.mat") };
            binding.provenance = "Unity-Technologies/WaterScenes@6032de37ee5cccfe5d86b5cf0959f66747de9342; SpeedTree Bush_Desktop; non-commercial research only";
            EditorUtility.SetDirty(binding);
            AssetDatabase.SaveAssets();
        }
        static Texture2D PackMaterialChannels()
        {
            const string sourcePath = DirectoryPath + "Textures/Bush_Desktop_Extra.png";
            const string packedPath = DirectoryPath + "ResearchTree_PBR.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            bool readable = importer.isReadable;
            Color32[] pixels; int width, height;
            try
            {
                if (!readable) { importer.isReadable = true; importer.SaveAndReimport(); }
                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                width = source.width; height = source.height; pixels = source.GetPixels32();
            }
            finally { if (!readable) { importer.isReadable = false; importer.SaveAndReimport(); } }
            // URP SpeedTree8 documents Extra as smoothness R, metallic G, AO B.
            // URP Lit expects metallic R / smoothness A and occlusion G.
            for (int i = 0; i < pixels.Length; i++) { var p = pixels[i]; pixels[i] = new Color32(p.g, p.b, 0, p.r); }
            var packed = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                packed.SetPixels32(pixels); packed.Apply();
                File.WriteAllBytes(Path.Combine(RepositoryPaths.UnityRoot, packedPath), packed.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(packed); }
            AssetDatabase.ImportAsset(packedPath);
            var targetImporter = (TextureImporter)AssetImporter.GetAtPath(packedPath);
            targetImporter.sRGBTexture = false; targetImporter.mipmapEnabled = true;
            targetImporter.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath);
        }
        static void Save<T>(T value, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) AssetDatabase.CreateAsset(value, path);
            else { EditorUtility.CopySerialized(value, existing); Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); }
        }
    }
}
