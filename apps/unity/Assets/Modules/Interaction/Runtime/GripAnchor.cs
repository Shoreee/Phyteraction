using System.Collections.Generic;
using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>An object-owned palm frame. Local +Z points into the object; +Y follows the fingers.</summary>
    [DisallowMultipleComponent]
    public sealed class GripAnchor : MonoBehaviour
    {
        public bool leftHand = true, rightHand = true;
        [Min(.001f)] public float captureDistance = .08f;
        [Range(0, 180)] public float captureAngle = 60;
        [Min(1)] public int capacity = 1;
        public Vector3 leftPalmEuler, rightPalmEuler;
        [UnityEngine.Serialization.FormerlySerializedAs("surfaceToAnchor")]
        [Tooltip("Offset from palm to this anchor in the authored palm frame (+Z into the object). Preserves clearance for either hand.")]
        public Vector3 palmToAnchor;
        public GripPoseAsset gripPose;
        public Quaternion PalmRotation(HandSide side) => transform.rotation * Quaternion.Euler(side == HandSide.Left ? leftPalmEuler : rightPalmEuler);
        readonly HashSet<Holder> occupants = new();
        public int OccupantCount => occupants.Count;
        public bool CanCapture(HandGrabber holder) => WithinReach(holder, captureDistance, captureAngle);
        public bool CanPreview(HandGrabber holder) => WithinReach(holder, captureDistance * 1.5f, Mathf.Min(180, captureAngle + 15));
        bool WithinReach(HandGrabber holder, float distance, float angle)
        {
            if (holder == null || holder.Hand == null || holder.Hand.Palm == null || gripPose == null || !isActiveAndEnabled) return false;
            if (holder.Side == HandSide.Left ? !leftHand : !rightHand) return false;
            if (occupants.Count >= capacity && !occupants.Contains(holder)) return false;
            Transform palm = holder.Hand.Palm;
            return Vector3.Distance(palm.position, transform.position) <= distance &&
                   Quaternion.Angle(holder.Hand.GripRotation, PalmRotation(holder.Side)) <= angle;
        }
        internal bool TryOccupy(HandGrabber holder)
        {
            if (!CanCapture(holder)) return false;
            return occupants.Add(holder);
        }
        internal void Vacate(Holder holder) => occupants.Remove(holder);
        void OnDisable()
        {
            // Holdable owns release ordering; copy because releasing removes this entry.
            foreach (Holder holder in new List<Holder>(occupants))
                if (holder != null) holder.ForceRelease();
            occupants.Clear();
        }
    }
}
