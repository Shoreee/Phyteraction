using System;
using UnityEngine;

namespace PhysicsHands.Posing
{

    /// <summary>A left/right pair used by the general hand pose library and predictive poses.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Posing", "PhysicsHands.Runtime", "HandPoserManager")]
    public sealed class HandPoserManager : MonoBehaviour
    {
        [Serializable]
        public struct HandPoseReference { public HandPoser HandPoser; }
        [UnityEngine.Serialization.FormerlySerializedAs("_handSnapRight")]
        [SerializeField] HandPoseReference _rightPose;
        [UnityEngine.Serialization.FormerlySerializedAs("_handSnapLeft")]
        [SerializeField] HandPoseReference _leftPose;
        public HandPoser GetHandPoser(HandSide side) => (side == HandSide.Right ? _rightPose : _leftPose).HandPoser;
        void Start()
        {
            GetHandPoser(HandSide.Left)?.ShowTarget(false);
            GetHandPoser(HandSide.Right)?.ShowTarget(false);
        }
    }
}
