using PhysicsHands.Posing;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace PhysicsHands.Tracking
{
    /// <summary>
    /// Writes XR Hands joint poses onto the authored reference skeleton used by every hand rig.
    /// target.rotation = xr.rotation * AngleAxis(_angle, _axis). Original values:
    /// right (0,1,0) / -90°, left (-1,0,1) / 180°.
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Tracking", "PhysicsHands.Runtime", "XRHandToOVRHandMapper")]
    public class XRHandRetargeter : MonoBehaviour
    {
        [Serializable]
        public struct JointMapper
        {
            public XRHandJointID XrJoint;
            [UnityEngine.Serialization.FormerlySerializedAs("OvrJoint")]
            public Transform Target;
            public bool MapPosition;
        }

        [SerializeField]
        private Vector3 _axis = Vector3.up;
        [SerializeField]
        private float _angle = -90f;
        [SerializeField]
        private JointMapper[] _jointMappers = Array.Empty<JointMapper>();
        [Tooltip("Reference bones used as the reference length for hand-scale estimation.")]
        [SerializeField]
        private Transform _scaleReferenceFrom;
        [SerializeField]
        private Transform _scaleReferenceTo;
        private float _initialDistance;
        private float _scale = 1f;
        private Pose[] _worlds = Array.Empty<Pose>();
        private bool[] _valid = Array.Empty<bool>();
        private readonly Dictionary<Transform, Vector3> _restLocalPosition = new();
        private readonly Dictionary<Transform, Quaternion> _restLocalRotation = new();
        public JointMapper[] JointMappers
        {
            get => _jointMappers;
            set => _jointMappers = value;
        }

        public void Configure(Vector3 axis, float angle)
        {
            _axis = axis;
            _angle = angle;
        }

        private void Awake()
        {
            if (_scaleReferenceFrom != null && _scaleReferenceTo != null)
                _initialDistance = Vector3.Distance(_scaleReferenceFrom.position, _scaleReferenceTo.position);
            _restLocalPosition.Clear();
            _restLocalRotation.Clear();
            foreach (JointMapper mapper in _jointMappers)
            {
                if (mapper.Target == null)
                    continue;
                _restLocalPosition[mapper.Target] = mapper.Target.localPosition;
                _restLocalRotation[mapper.Target] = mapper.Target.localRotation;
            }
        }

        public float GetScale() => _scale;
        /// <param name = "trackingSpace">Transform that XR poses are relative to (XR Origin camera offset).</param>
        public void Map(XRHand hand, Transform trackingSpace)
        {
            Quaternion offset = Quaternion.AngleAxis(_angle, _axis.sqrMagnitude > 0f ? _axis.normalized : Vector3.up);
            if (_worlds.Length != _jointMappers.Length)
            {
                _worlds = new Pose[_jointMappers.Length];
                _valid = new bool[_jointMappers.Length];
            }

            var worlds = _worlds;
            var valid = _valid;
            Array.Clear(valid, 0, valid.Length);
            for (int i = 0; i < _jointMappers.Length; i++)
            {
                if (_jointMappers[i].Target == null)
                    continue;
                if (!hand.GetJoint(_jointMappers[i].XrJoint).TryGetPose(out Pose pose))
                    continue;
                worlds[i] = trackingSpace != null ? pose.GetTransformedBy(trackingSpace) : pose;
                valid[i] = true;
            }

            for (int i = 0; i < _jointMappers.Length; i++)
            {
                if (!valid[i])
                    continue;
                Transform bone = _jointMappers[i].Target;
                Quaternion twist = worlds[i].rotation * offset;
                int child = AimChild(i);
                Quaternion rotation = twist;
                Vector3 rest = Vector3.zero;
                Vector3 dir = Vector3.zero;
                if (child >= 0 && valid[child])
                {
                    _restLocalPosition.TryGetValue(_jointMappers[child].Target, out rest);
                    dir = worlds[child].position - worlds[i].position;
                }
                else if (TryGetTip(hand, _jointMappers[i].XrJoint, trackingSpace, out Pose tip))
                {
                    rest = FirstChildOffset(bone);
                    if (rest.sqrMagnitude < 1e-8f)
                        rest = bone.localPosition;
                    dir = tip.position - worlds[i].position;
                }

                bool aimed = rest.sqrMagnitude > 1e-8f && dir.sqrMagnitude > 1e-8f;
                if (_jointMappers[i].MapPosition)
                {
                    // Wrist keeps the tracked palm roll, then swings its length axis onto the middle knuckle.
                    if (aimed)
                        rotation = SwingOnto(twist, rest, dir);
                    bone.SetPositionAndRotation(worlds[i].position, rotation);
                }
                else if (aimed)
                {
                    // Finger roll comes from the bind pose under the updated parent.
                    // Per-joint tracking roll spins the right hand and the left middle and pinky:
                    // visionOS already aligns those joints so their up axis lies along the bone.
                    Quaternion restLocal = _restLocalRotation.TryGetValue(bone, out Quaternion cached) ? cached : bone.localRotation;
                    Quaternion bindWorld = (bone.parent != null ? bone.parent.rotation : Quaternion.identity) * restLocal;
                    bone.rotation = SwingOnto(bindWorld, rest, dir);
                }
            // If the child/tip pose is temporarily unavailable, retain the
            // last local pose. Falling back to raw XR roll reintroduces the
            // incompatible bone frame exactly during occluded grasps.
            }

            UpdateScale(hand, trackingSpace);
        }

        /// <summary>
        /// Finger length lives in the child's rest local position (mostly +X on this rig).
        /// Point that axis at the next tracked joint instead of copying the raw joint
        /// rotation, which uses a different bone axis and leaves the fingers extended.
        /// </summary>
        private int AimChild(int index)
        {
            Transform bone = _jointMappers[index].Target;
            int fallback = -1;
            int middle = -1;
            for (int i = 0; i < _jointMappers.Length; i++)
            {
                Transform other = _jointMappers[i].Target;
                if (other == null || other.parent != bone)
                    continue;
                if (fallback < 0)
                    fallback = i;
                if (_jointMappers[i].XrJoint == XRHandJointID.MiddleProximal)
                    middle = i;
            }

            if (_jointMappers[index].XrJoint == XRHandJointID.Wrist && middle >= 0)
                return middle;
            return fallback;
        }

        private static bool TryGetTip(XRHand hand, XRHandJointID joint, Transform trackingSpace, out Pose tip)
        {
            XRHandJointID tipId = joint switch
            {
            XRHandJointID.ThumbDistal => XRHandJointID.ThumbTip, XRHandJointID.IndexDistal => XRHandJointID.IndexTip, XRHandJointID.MiddleDistal => XRHandJointID.MiddleTip, XRHandJointID.RingDistal => XRHandJointID.RingTip, XRHandJointID.LittleDistal => XRHandJointID.LittleTip, _ => XRHandJointID.Invalid, }

            ;
            tip = default;
            if (tipId == XRHandJointID.Invalid || !hand.GetJoint(tipId).TryGetPose(out Pose pose))
                return false;
            tip = trackingSpace != null ? pose.GetTransformedBy(trackingSpace) : pose;
            return true;
        }

        private static Vector3 FirstChildOffset(Transform bone)
        {
            if (bone.childCount == 0)
                return Vector3.zero;
            return bone.GetChild(0).localPosition;
        }

        /// <summary>
        /// Points <paramref name = "restLocalAxis"/> along <paramref name = "worldDirection"/>
        /// with the shortest swing. LookRotation is undefined when the twist up axis
        /// lies on the finger, and that is exactly when those fingers were spinning.
        /// </summary>
        private static Quaternion SwingOnto(Quaternion basis, Vector3 restLocalAxis, Vector3 worldDirection)
        {
            Vector3 from = basis * restLocalAxis.normalized;
            Vector3 to = worldDirection.normalized;
            if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f)
                return basis;
            return Quaternion.FromToRotation(from, to) * basis;
        }

        private void UpdateScale(XRHand hand, Transform trackingSpace)
        {
            if (_initialDistance <= 0f)
                return;
            if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist) || !hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out Pose middle))
                return;
            float scale = trackingSpace != null ? trackingSpace.lossyScale.x : 1f;
            _scale = Vector3.Distance(wrist.position, middle.position) * scale / _initialDistance;
        }

    }
}
