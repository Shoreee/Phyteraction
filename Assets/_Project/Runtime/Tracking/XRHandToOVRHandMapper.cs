using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace PhysicsHands.Tracking
{
    /// <summary>
    /// Writes XR Hands joint poses onto the OVR-layout skeleton used by every hand rig.
    /// ovr.rotation = xr.rotation * AngleAxis(_angle, _axis). Original values:
    /// right (0,1,0) / -90°, left (-1,0,1) / 180°.
    /// </summary>
    public class XRHandToOVRHandMapper : MonoBehaviour
    {
        [Serializable]
        public struct JointMapper
        {
            public XRHandJointID XrJoint;
            public Transform OvrJoint;
            public bool MapPosition;
        }

        [SerializeField] private Vector3 _axis = Vector3.up;
        [SerializeField] private float _angle = -90f;

        /// <summary>Original serialized joint pair. The importer fills these; <see cref="Awake"/> copies them into <see cref="_jointMappers"/>.</summary>
        [Serializable]
        public struct SourceJoint
        {
            public Transform XrJoint;
            public Transform OvrJoint;
            public Vector3 AngleOffset;
        }

        [SerializeField] private SourceJoint _wristJoint;
        [SerializeField] private SourceJoint _thumbMetacarpalJoint;
        [SerializeField] private SourceJoint _thumbProximalJoint;
        [SerializeField] private SourceJoint _thumbIntermediateJoint;
        [SerializeField] private SourceJoint _thumbDistalJoint;
        [SerializeField] private SourceJoint _indexProximalJoint;
        [SerializeField] private SourceJoint _indexIntermediateJoint;
        [SerializeField] private SourceJoint _indexDistalJoint;
        [SerializeField] private SourceJoint _middleProximalJoint;
        [SerializeField] private SourceJoint _middleIntermediateJoint;
        [SerializeField] private SourceJoint _middleDistalJoint;
        [SerializeField] private SourceJoint _ringProximalJoint;
        [SerializeField] private SourceJoint _ringIntermediateJoint;
        [SerializeField] private SourceJoint _ringDistalJoint;
        [SerializeField] private SourceJoint _pinkyMetacarpalJoint;
        [SerializeField] private SourceJoint _pinkyProximalJoint;
        [SerializeField] private SourceJoint _pinkyIntermediateJoint;
        [SerializeField] private SourceJoint _pinkyDistalJoint;

        [Space]
        [Tooltip("Parent-first order: wrist, then each finger from proximal to distal.")]
        [SerializeField] private JointMapper[] _jointMappers = Array.Empty<JointMapper>();

        [Tooltip("OVR bones used as the reference length for hand-scale estimation.")]
        [SerializeField] private Transform _scaleReferenceFrom;
        [SerializeField] private Transform _scaleReferenceTo;

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
            if (_jointMappers == null || _jointMappers.Length == 0)
                _jointMappers = BuildFromSourceJoints();

            if (_scaleReferenceFrom != null && _scaleReferenceTo != null)
                _initialDistance = Vector3.Distance(_scaleReferenceFrom.position, _scaleReferenceTo.position);

            _restLocalPosition.Clear();
            _restLocalRotation.Clear();
            foreach (JointMapper mapper in _jointMappers)
            {
                if (mapper.OvrJoint == null) continue;
                _restLocalPosition[mapper.OvrJoint] = mapper.OvrJoint.localPosition;
                _restLocalRotation[mapper.OvrJoint] = mapper.OvrJoint.localRotation;
            }
        }

        private JointMapper[] BuildFromSourceJoints()
        {
            var built = new List<JointMapper>();
            Add(built, XRHandJointID.Wrist, _wristJoint, true);
            Add(built, XRHandJointID.ThumbMetacarpal, _thumbMetacarpalJoint, false);
            Add(built, XRHandJointID.ThumbProximal, _thumbProximalJoint, false);
            Add(built, XRHandJointID.ThumbDistal, _thumbIntermediateJoint, false);
            Add(built, XRHandJointID.ThumbTip, _thumbDistalJoint, false);
            Add(built, XRHandJointID.IndexProximal, _indexProximalJoint, false);
            Add(built, XRHandJointID.IndexIntermediate, _indexIntermediateJoint, false);
            Add(built, XRHandJointID.IndexDistal, _indexDistalJoint, false);
            Add(built, XRHandJointID.MiddleProximal, _middleProximalJoint, false);
            Add(built, XRHandJointID.MiddleIntermediate, _middleIntermediateJoint, false);
            Add(built, XRHandJointID.MiddleDistal, _middleDistalJoint, false);
            Add(built, XRHandJointID.RingProximal, _ringProximalJoint, false);
            Add(built, XRHandJointID.RingIntermediate, _ringIntermediateJoint, false);
            Add(built, XRHandJointID.RingDistal, _ringDistalJoint, false);
            Add(built, XRHandJointID.LittleMetacarpal, _pinkyMetacarpalJoint, false);
            Add(built, XRHandJointID.LittleProximal, _pinkyProximalJoint, false);
            Add(built, XRHandJointID.LittleIntermediate, _pinkyIntermediateJoint, false);
            Add(built, XRHandJointID.LittleDistal, _pinkyDistalJoint, false);
            return built.ToArray();
        }

        private static void Add(List<JointMapper> built, XRHandJointID id, SourceJoint source, bool mapPosition)
        {
            if (source.OvrJoint == null) return;
            built.Add(new JointMapper { XrJoint = id, OvrJoint = source.OvrJoint, MapPosition = mapPosition });
        }

        public float GetScale() => _scale;

        /// <param name="trackingSpace">Transform that XR poses are relative to (XR Origin camera offset).</param>
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
                if (_jointMappers[i].OvrJoint == null) continue;
                if (!hand.GetJoint(_jointMappers[i].XrJoint).TryGetPose(out Pose pose)) continue;
                worlds[i] = trackingSpace != null ? pose.GetTransformedBy(trackingSpace) : pose;
                valid[i] = true;
            }

            for (int i = 0; i < _jointMappers.Length; i++)
            {
                if (!valid[i]) continue;
                Transform bone = _jointMappers[i].OvrJoint;
                Quaternion twist = worlds[i].rotation * offset;
                int child = AimChild(i);
                Quaternion rotation = twist;
                Vector3 rest = Vector3.zero;
                Vector3 dir = Vector3.zero;
                if (child >= 0 && valid[child])
                {
                    _restLocalPosition.TryGetValue(_jointMappers[child].OvrJoint, out rest);
                    dir = worlds[child].position - worlds[i].position;
                }
                else if (TryGetTip(hand, _jointMappers[i].XrJoint, trackingSpace, out Pose tip))
                {
                    rest = FirstChildOffset(bone);
                    if (rest.sqrMagnitude < 1e-8f) rest = bone.localPosition;
                    dir = tip.position - worlds[i].position;
                }

                bool aimed = rest.sqrMagnitude > 1e-8f && dir.sqrMagnitude > 1e-8f;
                if (_jointMappers[i].MapPosition)
                {
                    // Wrist keeps the tracked palm roll, then swings its length axis onto the middle knuckle.
                    if (aimed) rotation = SwingOnto(twist, rest, dir);
                    bone.SetPositionAndRotation(worlds[i].position, rotation);
                }
                else if (aimed)
                {
                    // Finger roll comes from the bind pose under the updated parent.
                    // Per-joint tracking roll spins the right hand and the left middle and pinky:
                    // visionOS already aligns those joints so their up axis lies along the bone.
                    Quaternion restLocal = _restLocalRotation.TryGetValue(bone, out Quaternion cached)
                        ? cached
                        : bone.localRotation;
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
            Transform bone = _jointMappers[index].OvrJoint;
            int fallback = -1;
            int middle = -1;
            for (int i = 0; i < _jointMappers.Length; i++)
            {
                Transform other = _jointMappers[i].OvrJoint;
                if (other == null || other.parent != bone) continue;
                if (fallback < 0) fallback = i;
                if (_jointMappers[i].XrJoint == XRHandJointID.MiddleProximal) middle = i;
            }

            if (_jointMappers[index].XrJoint == XRHandJointID.Wrist && middle >= 0) return middle;
            return fallback;
        }

        private static bool TryGetTip(XRHand hand, XRHandJointID joint, Transform trackingSpace, out Pose tip)
        {
            XRHandJointID tipId = joint switch
            {
                XRHandJointID.IndexDistal => XRHandJointID.IndexTip,
                XRHandJointID.MiddleDistal => XRHandJointID.MiddleTip,
                XRHandJointID.RingDistal => XRHandJointID.RingTip,
                XRHandJointID.LittleDistal => XRHandJointID.LittleTip,
                _ => XRHandJointID.Invalid,
            };
            tip = default;
            if (tipId == XRHandJointID.Invalid || !hand.GetJoint(tipId).TryGetPose(out Pose pose)) return false;
            tip = trackingSpace != null ? pose.GetTransformedBy(trackingSpace) : pose;
            return true;
        }

        private static Vector3 FirstChildOffset(Transform bone)
        {
            if (bone.childCount == 0) return Vector3.zero;
            return bone.GetChild(0).localPosition;
        }

        /// <summary>
        /// Points <paramref name="restLocalAxis"/> along <paramref name="worldDirection"/>
        /// with the shortest swing. LookRotation is undefined when the twist up axis
        /// lies on the finger, and that is exactly when those fingers were spinning.
        /// </summary>
        private static Quaternion SwingOnto(Quaternion basis, Vector3 restLocalAxis, Vector3 worldDirection)
        {
            Vector3 from = basis * restLocalAxis.normalized;
            Vector3 to = worldDirection.normalized;
            if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return basis;
            return Quaternion.FromToRotation(from, to) * basis;
        }

        private void UpdateScale(XRHand hand, Transform trackingSpace)
        {
            if (_initialDistance <= 0f) return;
            if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist) ||
                !hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out Pose middle)) return;

            float scale = trackingSpace != null ? trackingSpace.lossyScale.x : 1f;
            _scale = Vector3.Distance(wrist.position, middle.position) * scale / _initialDistance;
        }

        /// <summary>Maps a transform's rotation from one space into another.</summary>
        public static void MapRelative(Transform target, Transform reference, Transform refSpace, Transform targetSpace)
        {
            target.rotation = targetSpace.rotation * Quaternion.Inverse(refSpace.rotation) * reference.rotation;
        }
    }
}
