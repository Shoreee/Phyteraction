using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PhysicsHands.GaussianWorld
{
    // Explicit float4 fields keep the CPU/HLSL/Metal buffer layout identical (80 bytes).
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct Gaussian
    {
        public Vector4 center, axis0, axis1, axis2, color;
    }

    [PreferBinarySerialization]
    [CreateAssetMenu(menuName = "Gaussian World/Gaussian Asset")]
    public sealed class GaussianAsset : ScriptableObject
    {
        public string provenance;
        public string sourceSha256;
        public int sourceCount;
        public Gaussian[] splats;
        // First-order spherical harmonics, packed as three float4 per Gaussian (same as MpmSceneAsset).
        public Vector4[] sh = Array.Empty<Vector4>();
        public Bounds bounds;
        // Optional per-splat material, parallel to splats. Empty when the source has no learned field.
        public float[] youngsSim = Array.Empty<float>();
        public bool[] frozen = Array.Empty<bool>();
        // Metres per unit of the solver space youngsSim was optimised in (PhysDreamer: (raw + shift) / scale).
        public float simUnitMeters;
        public float simDensity = 2000;
        public bool HasMaterial => youngsSim != null && youngsSim.Length == splats.Length && simUnitMeters > 0;
        // Mass-normalised vibration modes of the cage built with (modeVoxel, modeMass); node-major [node][axis][mode].
        public float modeVoxel, modeMass;
        public float[] modeOmega = Array.Empty<float>();
        public float[] modeShapes = Array.Empty<float>();
        public bool HasModes(int nodes, float voxel, float mass) =>
            modeOmega.Length > 0 && modeShapes.Length == nodes * 3 * modeOmega.Length && Mathf.Approximately(modeVoxel, voxel) && Mathf.Approximately(modeMass, mass);
    }
}
