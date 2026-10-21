using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Simulation
{
    internal static class MpmStatistics
    {
        internal static void Measure(Unity.Collections.NativeArray<Particle> p, Vector3[] rest, float meters, out float rms, out float strain)
        {
            double sum = 0;
            strain = 0;
            for (int i = 0; i < p.Length; i++)
            {
                float sq = ((Vector3)p[i].x - rest[i]).sqrMagnitude;
                sum += sq;
                var f = new Matrix4x4(p[i].f0, p[i].f1, p[i].f2, new Vector4(0, 0, 0, 1)).transpose;
                strain = Mathf.Max(strain, Mathf.Abs(f.determinant - 1));
            }

            rms = (float)Math.Sqrt(sum / Math.Max(1, p.Length)) * meters;
        }
    }
}
