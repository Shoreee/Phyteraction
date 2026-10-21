using System;
using UnityEngine;

namespace PhysicsHands.GaussianData
{
    [PreferBinarySerialization]
    [CreateAssetMenu(menuName = "Gaussian World/Render Asset")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "GaussianRenderAsset")]
    public sealed class GaussianRenderAsset : ScriptableObject
    {
        public Gaussian[] splats = Array.Empty<Gaussian>();
        public Vector4[] sh = Array.Empty<Vector4>();
        public Bounds bounds;
        public Matrix4x4 shFromLocal = Matrix4x4.identity;
    }
}
