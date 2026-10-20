using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Posing
{
    /// <summary>One finger of a <see cref="HandPoser"/>: an ordered list of bone transforms.</summary>
    public class FingerPoser : MonoBehaviour
    {
        [Serializable]
        public class FingerBone
        {
            public HandFinger type;
            public Transform target;
            public bool map = true;

            private Vector3 _initialLocalPosition;
            private Quaternion _initialLocalRotation;

            public Quaternion InitialLocalRotation => _initialLocalRotation;

            public void Initialize()
            {
                if (target == null) return;
                _initialLocalPosition = target.localPosition;
                _initialLocalRotation = target.localRotation;
            }

            public void Reset()
            {
                if (target == null) return;
                target.localPosition = _initialLocalPosition;
                target.localRotation = _initialLocalRotation;
            }
        }

        public HandFinger type;
        public List<FingerBone> FingerBones = new();
        public bool MapFinger = true;
        public LineRenderer _line;
        public Transform _boneProximal;
        public Transform _boneMiddle;
        public Transform _boneDistal;
        public float _proximalMaxCurlAngle = 90f;
        public float _middleMaxCurlAngle = 90f;

        private Quaternion _proximalInitialRotation;
        private Quaternion _middleInitialRotation;
        private bool _ignoreFingersFreedom;
        private bool _initialized;

        private void Awake() => Initialize();

        public void Initialize()
        {
            if (_initialized) return;
            foreach (FingerBone bone in FingerBones) bone.Initialize();
            if (_boneProximal != null) _proximalInitialRotation = _boneProximal.localRotation;
            if (_boneMiddle != null) _middleInitialRotation = _boneMiddle.localRotation;
            _initialized = true;
        }

        public void Reset()
        {
            foreach (FingerBone bone in FingerBones) bone.Reset();
        }

        public FingerBone GetBone(int index) => index >= 0 && index < FingerBones.Count ? FingerBones[index] : null;

        /// <summary>
        /// When false (default), a pose can only restrict how far the finger closes: the
        /// finger is free to open more than the pose. When true the pose is applied as is.
        /// </summary>
        public void IgnoreFingersFreedom(bool ignore) => _ignoreFingersFreedom = ignore;

        /// <param name="realReference">Tracked finger.</param>
        /// <param name="adjustedRef">Optional grab/predictive pose finger; null to follow tracking.</param>
        /// <param name="time">Smoothing time constant in seconds; 0 snaps immediately.</param>
        public void MapTo(FingerPoser realReference, FingerPoser adjustedRef, bool useConfidence, float time)
        {
            if (!MapFinger || realReference == null) return;

            float t = time <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / time);
            for (int i = 0; i < FingerBones.Count; i++)
            {
                FingerBone bone = FingerBones[i];
                if (!bone.map || bone.target == null) continue;

                FingerBone real = realReference.GetBone(i);
                if (real?.target == null) continue;

                Quaternion goal = real.target.localRotation;
                FingerBone adjusted = adjustedRef != null ? adjustedRef.GetBone(i) : null;
                if (adjusted?.target != null)
                {
                    Quaternion pose = adjusted.target.localRotation;
                    goal = _ignoreFingersFreedom || Curl(pose, adjusted) <= Curl(goal, real) ? pose : goal;
                }

                bone.target.localRotation = Quaternion.Slerp(bone.target.localRotation, goal, t);
            }
        }

        public void ForceMapTo(FingerPoser reference)
        {
            if (reference == null) return;
            for (int i = 0; i < FingerBones.Count; i++)
            {
                FingerBone other = reference.GetBone(i);
                if (FingerBones[i].target != null && other?.target != null)
                    FingerBones[i].target.localRotation = other.target.localRotation;
            }
        }

        /// <summary>0 = straight, 1 = proximal and middle phalanges at their max curl angle.</summary>
        public float GetCurlAmount()
        {
            if (!_initialized) Initialize();
            float proximal = _boneProximal != null
                ? Quaternion.Angle(_proximalInitialRotation, _boneProximal.localRotation) / Mathf.Max(1f, _proximalMaxCurlAngle)
                : 0f;
            float middle = _boneMiddle != null
                ? Quaternion.Angle(_middleInitialRotation, _boneMiddle.localRotation) / Mathf.Max(1f, _middleMaxCurlAngle)
                : proximal;
            return Mathf.Clamp01((proximal + middle) * 0.5f);
        }

        public float GetProximalAngle() =>
            _boneProximal != null ? Quaternion.Angle(_proximalInitialRotation, _boneProximal.localRotation) : 0f;

        private static float Curl(Quaternion rotation, FingerBone bone) => Quaternion.Angle(bone.InitialLocalRotation, rotation);

        private void LateUpdate()
        {
            if (_line == null || !_line.enabled) return;
            _line.positionCount = FingerBones.Count;
            for (int i = 0; i < FingerBones.Count; i++)
                if (FingerBones[i].target != null) _line.SetPosition(i, FingerBones[i].target.position);
        }
    }
}
