using System;
using PhysicsHands.Hands;
using PhysicsHands.Posing;
using PhysicsHands.Tracking;
using PhysicsHands.Interaction;
using UnityEngine;

namespace PhysicsHands.App
{
    [DefaultExecutionOrder(-1000)]
    public sealed class SceneComposition : MonoBehaviour
    {
        public Camera headCamera;
        public TrackedHand[] sources = Array.Empty<TrackedHand>();
        public PinchCursor[] cursors = Array.Empty<PinchCursor>();
        public Telekinesis[] telekinesis = Array.Empty<Telekinesis>();
        public HandInputManager tracking;
        public PredefinedHandPoseManager poses;
        public PhysicsHand[] hands = Array.Empty<PhysicsHand>();
        public PredictivePose[] predictivePoses = Array.Empty<PredictivePose>();
        void ValidateBindings()
        {
            if (headCamera == null || tracking == null || poses == null)
                throw new InvalidOperationException($"{name}: headCamera, tracking and poses must be assigned.");
            if (hands == null || hands.Length != 2 || hands[0] == null || hands[1] == null || hands[0].HandType == hands[1].HandType)
                throw new InvalidOperationException($"{name}: assign exactly one left and one right physics hand.");
            if (sources == null || sources.Length == 0)
                throw new InvalidOperationException($"{name}: assign the tracked hand sources.");
            foreach (var source in sources)
            {
                if (source == null || source.ReferenceHandPoser == null || source.Wrist == null || source.Palm == null)
                    throw new InvalidOperationException($"{name}: each tracked source must have a reference poser, wrist and tracking palm marker.");
                if (source is TrackedHandXR && headCamera.transform.parent == null)
                    throw new InvalidOperationException($"{name}: XR sources require the camera's tracking-space parent.");
            }
            foreach (var cursor in cursors)
                if (cursor == null) throw new InvalidOperationException($"{name}: cursors contains a missing reference.");
            foreach (var power in telekinesis)
                if (power == null) throw new InvalidOperationException($"{name}: telekinesis contains a missing reference.");
            foreach (var pose in predictivePoses)
                if (pose == null) throw new InvalidOperationException($"{name}: predictivePoses contains a missing reference.");
        }

        void Awake()
        {
            ValidateBindings();
            foreach (var source in sources)
            {
                source.BindHead(headCamera.transform);
                if (source is TrackedHandXR xr)
                    xr.BindTrackingSpace(headCamera.transform.parent);
            }

            foreach (var cursor in cursors)
                cursor.headCamera = headCamera;
            foreach (var power in telekinesis)
                power.headCamera = headCamera;
            foreach (var hand in hands)
            {
                PhysicsHand other = null;
                foreach (var candidate in hands)
                    if (candidate != hand && candidate.HandType != hand.HandType)
                        other = candidate;
                hand.BindTracking(tracking, other, poses);
            }

            foreach (var pose in predictivePoses)
                pose.BindLibrary(poses);
        }
    }
}
