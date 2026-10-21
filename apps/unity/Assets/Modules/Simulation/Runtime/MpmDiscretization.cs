using PhysicsHands.GaussianData;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Simulation
{
    // Integration samples are independent of render Gaussians. The interactive path
    // integrates occupied sub-cells (half-cell spacing in the validated profiles), conserving
    // volume and its first moment. Paper replay retains every released particle.
    // Separate keys on each authored boundary prevent merging fixed and free material.
    public sealed class MpmDiscretization
    {
        public readonly Vector3[] positions, offsets;
        public readonly float[] volume, impulseGain, holdTime;
        public readonly int[] binding;
        public readonly bool fullSource;
        struct Accumulator
        {
            public double x, y, z, volume, gain;
            public float hold;
        }

        public MpmDiscretization(MpmModelAsset asset, bool fullSource, int subdivisions = 4)
        {
            this.fullSource = fullSource;
            if (fullSource)
            {
                positions = asset.particles;
                volume = asset.volume;
                impulseGain = asset.impulseGain;
                holdTime = asset.holdTime;
                binding = asset.binding;
                offsets = asset.offsets;
                return;
            }

            var cells = new Dictionary<(int, int, int, ulong), int>();
            var sums = new List<Accumulator>();
            var remap = new int[asset.particles.Length];
            subdivisions = Mathf.Clamp(subdivisions, 2, 8);
            float invCell = subdivisions / asset.dx;
            for (int i = 0; i < remap.Length; i++)
            {
                Vector3 p = asset.particles[i];
                ulong mask = 0;
                for (int b = 0; b < asset.cuboids.Length && b < 32; b++)
                {
                    var c = asset.cuboids[b];
                    Vector3 d = p - (Vector3)c.point;
                    if (Mathf.Abs(d.x) < c.size.x && Mathf.Abs(d.y) < c.size.y && Mathf.Abs(d.z) < c.size.z)
                        mask |= 1UL << b;
                }

                for (int b = 0; b < asset.axles.Length && b < 32; b++)
                {
                    var a = asset.axles[b];
                    Vector3 d = p - (Vector3)a.point, n = a.normal;
                    float height = Vector3.Dot(d, n);
                    if (Mathf.Abs(height) < a.normal.w && (d - height * n).magnitude < a.point.w)
                        mask |= 1UL << (b + 32);
                }

                var key = (Mathf.FloorToInt(p.x * invCell), Mathf.FloorToInt(p.y * invCell), Mathf.FloorToInt(p.z * invCell), mask);
                if (!cells.TryGetValue(key, out int sample))
                {
                    sample = sums.Count;
                    cells.Add(key, sample);
                    sums.Add(new Accumulator{hold = -1});
                }

                remap[i] = sample;
                double v = asset.volume[i];
                var sum = sums[sample];
                sum.x += p.x * v;
                sum.y += p.y * v;
                sum.z += p.z * v;
                sum.volume += v;
                sum.gain += asset.impulseGain[i] * v;
                sum.hold = Mathf.Max(sum.hold, asset.holdTime[i]);
                sums[sample] = sum;
            }

            positions = new Vector3[sums.Count];
            volume = new float[sums.Count];
            impulseGain = new float[sums.Count];
            holdTime = new float[sums.Count];
            for (int i = 0; i < sums.Count; i++)
            {
                var s = sums[i];
                if (s.volume <= 0)
                    throw new InvalidOperationException("MPM requires positive sample volumes.");
                positions[i] = new Vector3((float)(s.x / s.volume), (float)(s.y / s.volume), (float)(s.z / s.volume));
                volume[i] = (float)s.volume;
                impulseGain[i] = (float)(s.gain / s.volume);
                holdTime[i] = s.hold;
            }

            binding = new int[asset.dynamicCount];
            offsets = new Vector3[asset.dynamicCount];
            for (int i = 0; i < binding.Length; i++)
            {
                int source = asset.binding[i], sample = remap[source];
                binding[i] = sample;
                offsets[i] = asset.particles[source] + asset.offsets[i] - positions[sample];
            }
        }
    }
}
