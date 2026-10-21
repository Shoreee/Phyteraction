using PhysicsHands.Posing;
using UnityEngine;

namespace PhysicsHands.Hands
{
    internal sealed class HandPoseTargets
    {
        readonly HandPoser reference, visual;
        readonly Transform anchor;
        public HandPoseTargets(HandPoser reference, HandPoser visual, Transform anchor)
        {
            this.reference = reference;
            this.visual = visual;
            this.anchor = anchor;
        }

        public void Apply(IHandPoseSource source, IHandPoseSource mirroredSource, bool goodTracking, HandPoser custom, HandPoser enforced, HandPoser predictive, bool predictiveOverride, float smoothing)
        {
            var tracked = source.ReferenceHandPoser;
            if (tracked == null || reference == null)
                return;
            reference.MapWrist(tracked, 0f);
            reference.ForceMapTo(mirroredSource?.ReferenceHandPoser ?? tracked);
            if (goodTracking && anchor != null)
                anchor.SetPositionAndRotation(reference.Wrist.position, reference.Wrist.rotation);
            var adjusted = custom;
            bool ignoreFreedom = custom != null;
            if (adjusted == null)
                adjusted = enforced;
            if (adjusted == null)
            {
                adjusted = predictive;
                ignoreFreedom = predictiveOverride;
            }

            if (visual == null)
                return;
            visual.IgnoreFingersFreedom(ignoreFreedom);
            visual.MapTo(reference, adjusted, false, true, smoothing);
        }
    }
}
