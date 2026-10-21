using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Posing
{
    /// <summary>
    /// Makes a skinned skeleton follow another skeleton through explicit bone bindings. Used for the visual
    /// hand following the physics bones, and the ghost hand following tracking.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.VisualMapping)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Posing", "PhysicsHands.Runtime", "RigMapper")]
    public class RigMapper : MonoBehaviour
    {
        [SerializeField]
        private bool _localMapping;
        [SerializeField]
        private bool _mapPosition;
        [SerializeField]
        private bool _mapScale;
        [SerializeField]
        private bool _autoMap = true;
        [SerializeField]
        private bool _useLerp;
        [SerializeField]
        private float _lerpTime = 0.05f;
        [Serializable]
        public struct BoneMapping
        {
            public Transform bone, target;
            public bool mapPosition;
        }

        [SerializeField]
        private BoneMapping[] _mappings = Array.Empty<BoneMapping>();
        private void LateUpdate()
        {
            if (_autoMap)
                MapToTarget(_useLerp ? _lerpTime : 0f);
        }

        public void MapToTarget(float lerpTime)
        {
            float t = lerpTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / lerpTime);
            foreach (var mapping in _mappings)
            {
                var bone = mapping.bone;
                var target = mapping.target;
                if (bone == null || target == null)
                    continue;
                if (_localMapping)
                {
                    bone.localRotation = Quaternion.Slerp(bone.localRotation, target.localRotation, t);
                    if (_mapPosition || mapping.mapPosition)
                        bone.localPosition = Vector3.Lerp(bone.localPosition, target.localPosition, t);
                }
                else
                {
                    bone.rotation = Quaternion.Slerp(bone.rotation, target.rotation, t);
                    if (_mapPosition || mapping.mapPosition)
                        bone.position = Vector3.Lerp(bone.position, target.position, t);
                }

                if (_mapScale)
                    bone.localScale = target.localScale;
            }
        }
    }
}
