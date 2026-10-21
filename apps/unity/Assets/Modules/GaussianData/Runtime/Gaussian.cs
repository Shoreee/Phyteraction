using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PhysicsHands.GaussianData
{
    // Explicit float4 fields keep the CPU/HLSL/Metal buffer layout identical (80 bytes).
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct Gaussian
    {
        public const int Stride = 80;
        public Vector4 center, axis0, axis1, axis2, color;
    }
}
