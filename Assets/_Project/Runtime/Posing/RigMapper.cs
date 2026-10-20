using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Posing
{
    /// <summary>
    /// Makes a skinned skeleton follow another skeleton by bone name. Used for the visual
    /// hand following the physics bones, and the ghost hand following tracking.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.VisualMapping)]
    public class RigMapper : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private bool _includeWrist = true;
        [SerializeField] private bool _localMapping;
        [SerializeField] private bool _mapPosition;
        [SerializeField] private bool _mapScale;
        [SerializeField] private bool _autoMap = true;
        [SerializeField] private bool _useLerp;
        [SerializeField] private float _lerpTime = 0.05f;

        private readonly List<(Transform bone, Transform target)> _boneMappers = new();

        public Transform Target
        {
            get => _target;
            set
            {
                _target = value;
                BuildMappers();
            }
        }

        private void Start() => BuildMappers();

        private void BuildMappers()
        {
            _boneMappers.Clear();
            if (_target == null) return;

            var targets = new Dictionary<string, Transform>();
            foreach (Transform t in _target.GetComponentsInChildren<Transform>(true))
                targets.TryAdd(t.name, t);

            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (bone == transform && !_includeWrist) continue;
                string key = bone == transform ? _target.name : bone.name;
                if (targets.TryGetValue(key, out Transform target)) _boneMappers.Add((bone, target));
            }
        }

        private void LateUpdate()
        {
            if (_autoMap) MapToTarget(_useLerp ? _lerpTime : 0f);
        }

        public void MapToTarget(float lerpTime)
        {
            float t = lerpTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / lerpTime);
            foreach ((Transform bone, Transform target) in _boneMappers)
            {
                if (target == null) continue;
                if (_localMapping)
                {
                    bone.localRotation = Quaternion.Slerp(bone.localRotation, target.localRotation, t);
                    if (_mapPosition) bone.localPosition = Vector3.Lerp(bone.localPosition, target.localPosition, t);
                }
                else
                {
                    bone.rotation = Quaternion.Slerp(bone.rotation, target.rotation, t);
                    if (_mapPosition) bone.position = Vector3.Lerp(bone.position, target.position, t);
                }
                if (_mapScale) bone.localScale = target.localScale;
            }
        }
    }
}
