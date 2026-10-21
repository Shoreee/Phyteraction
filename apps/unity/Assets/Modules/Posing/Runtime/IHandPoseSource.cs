using UnityEngine;

namespace PhysicsHands.Posing
{
    public interface IHandPoseSource
    {
        HandSide Handedness
        {
            get;
        }

        HandPoser ReferenceHandPoser
        {
            get;
        }

        Transform Wrist
        {
            get;
        }

        Transform Palm { get; }

        bool GoodTracking
        {
            get;
        }

        bool UsesJointTracking
        {
            get;
        }

        bool IsPinching(HandFinger finger);
        bool IsFacingHead();
    }

    public interface IHandPoseProvider
    {
        IHandPoseSource GetPoseSource(HandSide side);
    }
}
