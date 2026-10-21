using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Posing
{
    /// <summary>
    /// A hand skeleton that can be posed. The same component is used for:
    /// the tracked reference hand, the visual/target hand that the physics bones chase,
    /// and authored grab poses on objects.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.HandPoser)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Posing", "PhysicsHands.Runtime", "HandPoser")]
    public class HandPoser : MonoBehaviour
    {
        [SerializeField]
        private Transform _mesh;
        [SerializeField]
        private Transform _visualTarget;
        [SerializeField]
        private Transform _wrist;
        [SerializeField]
        private bool _mapWrist;
        [SerializeField]
        private bool _destroyMesh;
        public Transform ThumbMiddle;
        public Transform IndexProximal;
        public Transform MiddleProximal;
        public Transform RingProximal;
        public Transform PinkyProximal;
        public FingerPoser FingerPoserThumb;
        public FingerPoser FingerPoserIndex;
        public FingerPoser FingerPoserMiddle;
        public FingerPoser FingerPoserRing;
        public FingerPoser FingerPoserPinky;
        public List<FingerPoser> FingerPosers = new();
        public const float WeakGraspThreshold = 0.25f;
        public const float ModerateGraspThreshold = 0.45f;
        public const float StrongGraspThreshold = 0.65f;
        public const float FlexedThreshold = 0.5f;
        private Vector3 _wristInitialLocalPosition;
        private Quaternion _wristInitialLocalRotation;
        public Transform Wrist => _wrist;
        public bool MapsWrist => _mapWrist;
        private void Awake()
        {
            foreach (FingerPoser finger in FingerPosers)
                finger.Initialize();
            if (_wrist != null)
            {
                _wristInitialLocalPosition = _wrist.localPosition;
                _wristInitialLocalRotation = _wrist.localRotation;
            }

            // Mesh nodes may also own transforms used by another skin or poser.
            // Removing the hierarchy invalidates those serialized bone references.
            if (_destroyMesh && _mesh != null)
                foreach (Renderer renderer in _mesh.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
        }

        public FingerPoser GetFinger(HandFinger finger) => finger switch
        {
        HandFinger.Thumb => FingerPoserThumb, HandFinger.Index => FingerPoserIndex, HandFinger.Middle => FingerPoserMiddle, HandFinger.Ring => FingerPoserRing, _ => FingerPoserPinky, }

        ;
        public void Reset()
        {
            foreach (FingerPoser finger in FingerPosers)
                if (finger != null)
                    finger.Reset();
            if (_wrist == null)
                return;
            _wrist.localPosition = _wristInitialLocalPosition;
            _wrist.localRotation = _wristInitialLocalRotation;
        }

        public Transform GetWrist() => _wrist;
        public void MapWrist(HandPoser reference, float time)
        {
            if (_wrist == null || reference?._wrist == null)
                return;
            float t = time <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / time);
            _wrist.SetPositionAndRotation(Vector3.Lerp(_wrist.position, reference._wrist.position, t), Quaternion.Slerp(_wrist.rotation, reference._wrist.rotation, t));
        }

        /// <param name = "reference">Tracked hand.</param>
        /// <param name = "adjusted">Optional pose that limits finger closure (grab/predictive pose).</param>
        public void MapTo(HandPoser reference, HandPoser adjusted, bool mapWrist, bool useConfidence, float time)
        {
            if (reference == null)
                return;
            if (mapWrist)
                MapWrist(reference, time);
            for (int i = 0; i < FingerPosers.Count && i < reference.FingerPosers.Count; i++)
            {
                FingerPoser adjustedFinger = adjusted != null && i < adjusted.FingerPosers.Count ? adjusted.FingerPosers[i] : null;
                FingerPosers[i].MapTo(reference.FingerPosers[i], adjustedFinger, useConfidence, time);
            }
        }

        public void IgnoreFingersFreedom(bool ignore)
        {
            foreach (FingerPoser finger in FingerPosers)
                finger.IgnoreFingersFreedom(ignore);
        }

        public void ForceMapTo(HandPoser reference)
        {
            if (reference == null)
                return;
            for (int i = 0; i < FingerPosers.Count && i < reference.FingerPosers.Count; i++)
                FingerPosers[i].ForceMapTo(reference.FingerPosers[i]);
        }

        public void SetTargetPosition(Vector3 position)
        {
            if (_visualTarget != null)
                _visualTarget.position = position;
        }

        public void ShowTarget(bool show)
        {
            if (_mesh != null)
                _mesh.gameObject.SetActive(show);
        }

        public float GetCurl(HandFinger finger) => GetFinger(finger)?.GetCurlAmount() ?? 0f;
        /// <summary>Average curl of index, middle, ring and pinky.</summary>
        public float GetGranularGraspAmount() => (GetCurl(HandFinger.Index) + GetCurl(HandFinger.Middle) + GetCurl(HandFinger.Ring) + GetCurl(HandFinger.Pinky)) * 0.25f;
        public float GraspingAmount() => GetGranularGraspAmount();
        public GraspLevel GetGraspLevel()
        {
            float amount = GetGranularGraspAmount();
            if (amount >= StrongGraspThreshold)
                return GraspLevel.Strong;
            if (amount >= ModerateGraspThreshold)
                return GraspLevel.Moderate;
            if (amount >= WeakGraspThreshold)
                return GraspLevel.Weak;
            return GraspLevel.None;
        }

        public bool IsGrasping(int minFingerCurved, GraspLevel graspLevel, float releaseMargin = 0f)
        {
            float threshold = graspLevel switch
            {
            GraspLevel.Strong => StrongGraspThreshold, GraspLevel.Moderate => ModerateGraspThreshold, _ => WeakGraspThreshold, }

            ;
            threshold = Mathf.Max(0f, threshold - releaseMargin);
            int curved = 0;
            for (var finger = HandFinger.Index; finger <= HandFinger.Pinky; finger++)
                if (GetCurl(finger) >= threshold)
                    curved++;
            return curved >= minFingerCurved;
        }

        public Transform GetIndexProximal() => IndexProximal;
        public float GetIndexProximalAngle() => FingerPoserIndex != null ? FingerPoserIndex.GetProximalAngle() : 0f;
        public float GetPinkyProximalAngle() => FingerPoserPinky != null ? FingerPoserPinky.GetProximalAngle() : 0f;
        public bool IsIndexFlexed() => GetCurl(HandFinger.Index) > FlexedThreshold;
        public bool IsMiddleFlexed() => GetCurl(HandFinger.Middle) > FlexedThreshold;
        public bool IsRingFlexed() => GetCurl(HandFinger.Ring) > FlexedThreshold;
        public bool IsPinkyFlexed() => GetCurl(HandFinger.Pinky) > FlexedThreshold;
        public bool IsThumbMiddleFlexed() => GetCurl(HandFinger.Thumb) > FlexedThreshold * 0.8f;
    }
}
