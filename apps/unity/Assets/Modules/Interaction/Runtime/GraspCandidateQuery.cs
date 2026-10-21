using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    internal sealed class GraspCandidateQuery
    {
        readonly Collider[] colliders = new Collider[128];
        internal readonly List<Holdable> Candidates = new();
        internal void Gather(Vector3 point, float radius, IEnumerable<Holdable> reachable)
        {
            Candidates.Clear();
            if (reachable != null)
                Candidates.AddRange(reachable);
            int count = Physics.OverlapSphereNonAlloc(point, radius, colliders, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Holdable h = Holdable.FromCollider(colliders[i]);
                if (h != null && !Candidates.Contains(h))
                    Candidates.Add(h);
                colliders[i] = null;
            }
        }
    }
}
