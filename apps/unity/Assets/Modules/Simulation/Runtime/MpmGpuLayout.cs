using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Simulation
{
    internal struct ShapeGpu
    {
        public Vector4 a, b, va, vb, ax, ay, az, he;
    }

    internal struct Particle
    {
        public Vector4 x, v, c0, c1, c2, f0, f1, f2, r0, r1, r2;
    }

    internal struct Neighbor4
    {
        public uint x, y, z, w;
        public Neighbor4(int a, int b, int c, int d)
        {
            x = (uint)a;
            y = (uint)b;
            z = (uint)c;
            w = (uint)d;
        }
    }
}
