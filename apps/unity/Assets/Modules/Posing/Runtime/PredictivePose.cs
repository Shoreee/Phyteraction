using UnityEngine;

namespace PhysicsHands.Posing
{
    /// <summary>
    /// Trigger volume that pre-shapes the hand before contact (e.g. flat hand near a
    /// button panel). Detected by the hand's PoserDetector.
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Posing", "PhysicsHands.Runtime", "PredictivePose")]
    public class PredictivePose : MonoBehaviour
    {
        [SerializeField]
        private PredictiveHandPose _predictiveHandPose = PredictiveHandPose.Default;
        [SerializeField]
        private HandPoserManager _handPoserManager;
        [Tooltip("Apply the pose exactly instead of only limiting finger closure.")]
        [SerializeField]
        private bool _overrideHandTracking;
        [SerializeField]
        private PredefinedHandPoseManager _poseLibrary;
        public void BindLibrary(PredefinedHandPoseManager library) => _poseLibrary = library;
        public bool OverrideHandTracking => _overrideHandTracking;
        public HandPoser GetPredictiveHandPoser(HandSide side)
        {
            if (_handPoserManager != null)
                return _handPoserManager.GetHandPoser(side);
            return _poseLibrary != null ? _poseLibrary.GetPose(_predictiveHandPose, side) : null;
        }
    }
}
