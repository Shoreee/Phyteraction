using System;
using UnityEngine;

namespace PhysicsHands.Posing
{
    [Serializable]
    public struct Finger
    {
        public Transform Bone0;
        public Transform Bone1;
        public Transform Bone2;
        public Transform Bone3;
    }

    /// <summary>Copies local bone rotations from another HandMapper with the same layout.</summary>
    [DefaultExecutionOrder(ExecutionOrder.VisualMapping)]
    public class HandMapper : MonoBehaviour
    {
        public HandMapper Reference;
        public Transform Wrist;
        [SerializeField] private bool _mapWristPosition;
        [SerializeField] private bool _mapWristRotation;
        public Finger Thumb;
        public Finger Index;
        public Finger Middle;
        public Finger Ring;
        public Finger Pinky;

        public bool MapWristPosition { get => _mapWristPosition; set => _mapWristPosition = value; }
        public bool MapWristRotation { get => _mapWristRotation; set => _mapWristRotation = value; }

        public void LateUpdate() => MapToReference();

        public void MapToReference()
        {
            if (Reference != null) MapTo(Reference);
        }

        public void MapTo(HandMapper reference)
        {
            if (Wrist != null && reference.Wrist != null)
            {
                if (_mapWristPosition) Wrist.position = reference.Wrist.position;
                if (_mapWristRotation) Wrist.rotation = reference.Wrist.rotation;
            }
            MapFingerTo(Thumb, reference.Thumb);
            MapFingerTo(Index, reference.Index);
            MapFingerTo(Middle, reference.Middle);
            MapFingerTo(Ring, reference.Ring);
            MapFingerTo(Pinky, reference.Pinky);
        }

        public void MapFingerTo(Finger finger, Finger reference)
        {
            MapBoneTo(finger.Bone0, reference.Bone0);
            MapBoneTo(finger.Bone1, reference.Bone1);
            MapBoneTo(finger.Bone2, reference.Bone2);
            MapBoneTo(finger.Bone3, reference.Bone3);
        }

        public void MapBoneTo(Transform bone, Transform reference)
        {
            if (bone != null && reference != null) bone.localRotation = reference.localRotation;
        }
    }
}
