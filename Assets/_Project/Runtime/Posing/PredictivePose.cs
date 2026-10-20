using UnityEngine;

namespace PhysicsHands.Posing
{
    /// <summary>
    /// Trigger volume that pre-shapes the hand before contact (e.g. flat hand near a
    /// button panel). Detected by the hand's PoserDetector.
    /// </summary>
    public class PredictivePose : MonoBehaviour
    {
        [SerializeField] private PredictiveHandPose _predictiveHandPose = PredictiveHandPose.Default;
        [SerializeField] private HandPoserManager _handPoserManager;
        [Tooltip("Apply the pose exactly instead of only limiting finger closure.")]
        [SerializeField] private bool _overrideHandTracking;

        public bool OverrideHandTracking => _overrideHandTracking;

        public HandPoser GetPredictiveHandPoser(HandSide side)
        {
            if (_handPoserManager != null) return _handPoserManager.GetHandPoser(side);
            return PredefinedHandPoseManager.Instance != null
                ? PredefinedHandPoseManager.Instance.GetPose(_predictiveHandPose, side)
                : null;
        }
    }
}
