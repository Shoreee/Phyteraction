using System.Collections.Generic;
using PhysicsHands.Posing;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    internal sealed class PredictivePoseSelection
    {
        readonly List<PredictivePose> poses = new();
        internal void Add(PredictivePose pose)
        {
            if (!poses.Contains(pose))
                poses.Add(pose);
        }

        internal void Remove(PredictivePose pose) => poses.Remove(pose);
        internal void Clear() => poses.Clear();
        internal PredictivePose Closest(Vector3 origin, bool captured)
        {
            poses.RemoveAll(p => p == null || !p.isActiveAndEnabled);
            if (captured)
                return null;
            PredictivePose closest = null;
            float best = float.PositiveInfinity;
            foreach (var pose in poses)
            {
                float distance = Vector3.Distance(origin, pose.transform.position);
                if (distance < best)
                {
                    best = distance;
                    closest = pose;
                }
            }

            return closest;
        }
    }
}
