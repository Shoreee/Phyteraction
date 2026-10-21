using System;
using System.Collections.Generic;
using PhysicsHands.Dynamics;
using PhysicsHands.GaussianData;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Simulation
{
    internal sealed class MpmReactionReadback
    {
        readonly Queue<(long step, Rigidbody[] bodies)> pendingBodies = new();
        readonly List<(long step, float[] reaction)> arrived = new();
        readonly Func<int, bool> isCurrent;
        internal MpmReactionReadback(Func<int, bool> isCurrent) => this.isCurrent = isCurrent;
        internal bool HasArrivals => arrived.Count > 0;
        internal void Reset()
        {
            pendingBodies.Clear();
            arrived.Clear();
        }

        internal void Queue(CommandBuffer commands, ComputeBuffer buffer, long step, int epoch, ContactGatherer contacts)
        {
            pendingBodies.Enqueue((step, contacts.Snapshot()));
            commands.RequestAsyncReadback(buffer, r =>
            {
                if (!r.hasError && isCurrent(epoch))
                    arrived.Add((step, r.GetData<float>().ToArray()));
            }

            );
        }

        internal void Apply(MpmModelAsset asset, Transform transform, ContactGatherer contacts, float maxReactionForce, float dt, out int pairs, out float force)
        {
            float meters = asset.metersPerUnit, scale = meters * meters * meters, total = 0;
            pairs = 0;
            foreach (var(step, data)in arrived)
            {
                while (pendingBodies.Count > 0 && pendingBodies.Peek().step < step)
                    pendingBodies.Dequeue();
                if (pendingBodies.Count == 0 || pendingBodies.Peek().step != step)
                    continue;
                var bodies = pendingBodies.Dequeue().bodies;
                for (int i = 0; i < ContactGatherer.MaxShapes; i++)
                {
                    float weight = data[i * 8 + 3];
                    if (weight <= 0)
                        continue;
                    pairs++;
                    // Simulation momentum (density x units^3 x units/s) to kg m/s: L^3 for mass, L for velocity (in localFromSim).
                    Vector3 local = asset.localFromSim.MultiplyVector(new Vector3(data[i * 8], data[i * 8 + 1], data[i * 8 + 2])) * scale;
                    Vector3 point = asset.localFromSim.MultiplyPoint3x4(new Vector3(data[i * 8 + 4], data[i * 8 + 5], data[i * 8 + 6]) / weight);
                    total += contacts.ApplyReaction(i, bodies, transform.TransformDirection(local), transform.TransformPoint(point), maxReactionForce, dt);
                }
            }

            arrived.Clear();
            force = total / dt;
        }
    }
}
