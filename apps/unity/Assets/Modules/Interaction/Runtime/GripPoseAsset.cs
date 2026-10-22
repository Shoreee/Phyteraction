using System;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>Object-authored local joint goals; independent of capture intent and raw tracking.</summary>
    [CreateAssetMenu(menuName = "Physics Hands/Grip Pose")]
    public sealed class GripPoseAsset : ScriptableObject
    {
        [Serializable] public struct Joint
        {
            public HandFinger finger;
            public int segment;
            public Quaternion left, right;
        }
        public Joint[] joints = Array.Empty<Joint>();
        [Tooltip("Fingers that retain raw physical tracking while this grip is active.")]
        public HandFinger[] freeFingers = Array.Empty<HandFinger>();
        public bool TracksFreely(HandFinger finger) => Array.IndexOf(freeFingers, finger) >= 0;
        public bool TryGet(HandSide side, HandFinger finger, int segment, out Quaternion rotation)
        {
            foreach (var joint in joints)
                if (joint.finger == finger && joint.segment == segment)
                {
                    rotation = side == HandSide.Left ? joint.left : joint.right;
                    return true;
                }
            rotation = Quaternion.identity; return false;
        }
    }
}
