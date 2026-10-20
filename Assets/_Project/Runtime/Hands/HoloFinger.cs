using System;
using PhysicsHands.Dynamics;
using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>A physical finger (ordered HoloFingerBones) placed on its first bone.</summary>
    public class HoloFinger : MonoBehaviour
    {
        [SerializeField] private HandFinger _fingerType;
        [SerializeField] private HoloFingerBone[] _bones = Array.Empty<HoloFingerBone>();
        [SerializeField] private LineRenderer _lineRenderer;
        [Tooltip("Wrist spring multiplier reached when this finger is fully pinned against something.")]
        [SerializeField] private float _unpinWristMultiplier = 1f;
        [Tooltip("Angle error (deg) considered a fully pinned finger.")]
        [SerializeField] private float _pinnedAngle = 40f;

        public HoloFingerBone[] Bones => _bones;
        public HandFinger FingerType => _fingerType;
        public float UnpinWristMultiplier => _unpinWristMultiplier;

        private void Start() => ShowLine(false);

        public void ShowLine(bool show)
        {
            if (_lineRenderer != null) _lineRenderer.enabled = show;
        }

        private void Update()
        {
            if (_lineRenderer == null || !_lineRenderer.enabled) return;
            _lineRenderer.positionCount = _bones.Length;
            for (int i = 0; i < _bones.Length; i++) _lineRenderer.SetPosition(i, _bones[i].transform.position);
        }

        /// <summary>
        /// 0 when the finger follows its target, 1 when it is blocked (e.g. pressed on a
        /// table) by at least <see cref="_pinnedAngle"/>.
        /// </summary>
        public float GetFingerPinValue()
        {
            float max = 0f;
            foreach (HoloFingerBone bone in _bones)
            {
                JointEnforcer enforcer = bone.JointEnforcer;
                if (enforcer == null || enforcer.Joint == null) continue;
                max = Mathf.Max(max, enforcer.GetAngleFromTarget() / _pinnedAngle);
            }
            return Mathf.Clamp01(max);
        }

        public void GoInTPose()
        {
            foreach (HoloFingerBone bone in _bones) bone.JointEnforcer.ResetPosition();
        }

        public void InstantMapToTarget()
        {
            foreach (HoloFingerBone bone in _bones) bone.JointEnforcer.InstantMapToTarget();
        }
    }
}
