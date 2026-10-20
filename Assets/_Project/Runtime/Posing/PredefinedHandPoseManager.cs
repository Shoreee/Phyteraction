using UnityEngine;

namespace PhysicsHands.Posing
{
    /// <summary>Scene-wide library of generic hand poses referenced by <see cref="PredictiveHandPose"/>.</summary>
    public class PredefinedHandPoseManager : MonoBehaviour
    {
        public HandPoserManager DefaultHandPose;
        public HandPoserManager OpenHandPose;
        public HandPoserManager FlatHandPose;
        public HandPoserManager PointingHandPose;
        public HandPoserManager GraspHandPoseMiddle;
        public HandPoserManager GraspHandPoseFull;
        public HandPoserManager PinchHandPose;

        public static PredefinedHandPoseManager Instance { get; private set; }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public HandPoser GetPose(PredictiveHandPose pose, HandSide side)
        {
            HandPoserManager manager = pose switch
            {
                PredictiveHandPose.Default => DefaultHandPose,
                PredictiveHandPose.OpenHand => OpenHandPose,
                PredictiveHandPose.FlatHand => FlatHandPose,
                PredictiveHandPose.Pointing => PointingHandPose,
                _ => null,
            };
            return manager != null ? manager.GetHandPoser(side) : null;
        }
    }
}
