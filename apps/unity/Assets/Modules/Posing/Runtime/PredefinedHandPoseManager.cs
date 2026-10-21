using UnityEngine;

namespace PhysicsHands.Posing
{
    /// <summary>Scene-wide library of generic hand poses referenced by <see cref = "PredictiveHandPose"/>.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Posing", "PhysicsHands.Runtime", "PredefinedHandPoseManager")]
    public class PredefinedHandPoseManager : MonoBehaviour
    {
        public HandPoserManager DefaultHandPose;
        public HandPoserManager OpenHandPose;
        public HandPoserManager FlatHandPose;
        public HandPoserManager PointingHandPose;
        public HandPoserManager GraspHandPoseMiddle;
        public HandPoserManager GraspHandPoseFull;
        public HandPoserManager PinchHandPose;
        public HandPoser GetPose(PredictiveHandPose pose, HandSide side)
        {
            HandPoserManager manager = pose switch
            {
            PredictiveHandPose.Default => DefaultHandPose, PredictiveHandPose.OpenHand => OpenHandPose, PredictiveHandPose.FlatHand => FlatHandPose, PredictiveHandPose.Pointing => PointingHandPose, _ => null, }

            ;
            return manager != null ? manager.GetHandPoser(side) : null;
        }
    }
}
