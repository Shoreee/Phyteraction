using System.IO;
using PhysicsHands.GaussianData;
using UnityEditor;
using UnityEngine;

namespace PhysicsHands.Editor.Import
{
    internal static class GaussianAssetWriter
    {
        internal static GaussianRenderAsset SaveRenderData(string modelPath, GaussianRenderAsset incoming)
        {
            string path = Path.ChangeExtension(modelPath, null) + ".render.asset";
            var existing = AssetDatabase.LoadAssetAtPath<GaussianRenderAsset>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(incoming, path);
                return incoming;
            }

            EditorUtility.CopySerialized(incoming, existing);
            Object.DestroyImmediate(incoming);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
