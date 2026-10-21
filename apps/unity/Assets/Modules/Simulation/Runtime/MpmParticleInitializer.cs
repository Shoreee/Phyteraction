using System;
using PhysicsHands.GaussianData;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

namespace PhysicsHands.Simulation
{
    internal static class MpmParticleInitializer
    {
        internal static Particle[] Create(MpmModelAsset asset, MpmDiscretization layout)
        {
            int n = layout.positions.Length;
            var p = new Particle[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 x = layout.positions[i];
                uint mask = 0;
                for (int k = 0; k < asset.modifiers.Length && k < 32; k++)
                    if (Selected(asset.modifiers[k], x))
                        mask |= 1u << k;
                p[i] = new Particle{x = new Vector4(x.x, x.y, x.z, layout.volume[i]), v = new Vector4(0, 0, 0, layout.volume[i] * asset.density), c0 = new Vector4(0, 0, 0, asset.yieldStress), c1 = new Vector4(0, 0, 0, math.asfloat(mask)), c2 = new Vector4(0, 0, 0, layout.impulseGain[i]), f0 = new Vector4(1, 0, 0, layout.holdTime[i]), f1 = new Vector4(0, 1, 0, 0), f2 = new Vector4(0, 0, 1, 0), r0 = new Vector4(1, 0, 0, 0), r1 = new Vector4(0, 1, 0, 0), r2 = new Vector4(0, 0, 1, 0), };
            }

            return p;
        }

        static bool Selected(MpmModifier m, Vector3 x)
        {
            Vector3 o = x - (Vector3)m.point;
            if ((int)m.point.w != 2)
                return Mathf.Abs(o.x) < m.size.x && Mathf.Abs(o.y) < m.size.y && Mathf.Abs(o.z) < m.size.z;
            Vector3 n = m.normal;
            float h = Vector3.Dot(o, n);
            return Mathf.Abs(h) < m.size.x && (o - h * n).magnitude < m.size.y;
        }
    }
}
