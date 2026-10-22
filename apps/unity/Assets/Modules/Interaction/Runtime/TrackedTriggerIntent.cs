using System;
using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>Near-trigger use intent, separate from grip and physical finger obstruction.</summary>
    internal sealed class TrackedTriggerIntent
    {
        HandGrabber[] grabbers = Array.Empty<HandGrabber>();
        PhysicsHand candidate;
        Vector3 releasedTip;
        float releasedCurl;
        bool established;
        public void Bind(HandGrabber[] sources) { grabbers = sources ?? Array.Empty<HandGrabber>(); Reset(); }
        public void Reset() { candidate = null; established = false; }

        public float Sample(Rigidbody gun, Transform grip, Vector3 triggerPoint, Vector3 pullAxis, out PhysicsHand hand)
        {
            hand = null;
            if (grip == null) { Reset(); return 0; }
            foreach (var grabber in grabbers)
            {
                var h = grabber != null ? grabber.Hand : null;
                if (h == null || !h.isActiveAndEnabled || !h.GoodTracking || h.ReferencePoser == null ||
                    grabber.CapturedObject != null && grabber.CapturedObject.Rigidbody != gun) continue;
                var poser = h.ReferencePoser;
                // The index is a use control. The remaining three fingers establish grip intent.
                int closed = 0;
                for (var f = HandFinger.Middle; f <= HandFinger.Pinky; f++) if (poser.GetCurl(f) > .35f) closed++;
                if (closed < 2 || Vector3.Distance(h.Palm.position, grip.position) > .1f) continue;
                var index = poser.GetFinger(HandFinger.Index);
                if (index == null || index.FingerBones.Count == 0 || poser.Wrist == null) continue;
                var tip = index.FingerBones[index.FingerBones.Count - 1].target;
                if (tip == null) continue;
                // Preserve raw articulation but express it about the physical wrist. A blocked
                // palm must not move the use region to the other side of the gun.
                var wrist = h.DetachableHand.Rb;
                Vector3 point = wrist.position + wrist.rotation * Quaternion.Inverse(poser.Wrist.rotation) * (tip.position - poser.Wrist.position);
                if (Vector3.Distance(point, triggerPoint) > .055f) continue;
                hand = h;
                float curl = poser.GetCurl(HandFinger.Index);
                Vector3 local = Quaternion.Inverse(gun.rotation) * (point - gun.position);
                if (candidate != h) { candidate = h; established = false; }
                if (!established)
                {
                    // Taking hold with an already curled index must never fire a shot.
                    if (curl > .5f) return 0;
                    releasedCurl = curl; releasedTip = local; established = true;
                }
                if (curl < releasedCurl + .025f)
                {
                    releasedCurl = curl; releasedTip = local;
                    return 0;
                }
                float closure = (curl - releasedCurl) / .18f;
                float travel = Vector3.Dot(gun.rotation * (local - releasedTip), pullAxis) / .004f;
                return Mathf.Clamp01(Mathf.Min(closure, travel));
            }
            Reset();
            return 0;
        }
    }
}
