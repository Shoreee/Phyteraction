using UnityEngine;

namespace PhysicsHands.GaussianData
{
    // Optional licensed payload bindings can be restored without editing the application scene.
    public sealed class MeshRenderAsset : ScriptableObject
    {
        public Mesh mesh;
        public Material[] materials = System.Array.Empty<Material>();
        public string provenance;
        public bool Available => mesh != null && materials.Length > 0 && materials[0] != null && materials[0].mainTexture != null;
    }
}
