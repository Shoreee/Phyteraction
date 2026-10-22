using UnityEngine;

namespace PhysicsHands.Dynamics
{
    public interface IJointTargetSource
    {
        bool TryGetTargetPose(Rigidbody body, out Pose pose);
    }

    public interface IJointDriveContext : IJointTargetSource
    {
        bool DirectTracking
        {
            get;
        }

        bool GoodTracking
        {
            get;
        }

        bool PhysicalContactActive
        {
            get;
        }

        float FingerSpringMultiplier
        {
            get;
        }

        Vector3 GetContactOffset(Rigidbody body);
        bool TryGetContact(Rigidbody body, out Vector3 normal, out Vector3 point);
        bool Owns(Collider collider);
        void NotifyPhysicalContact(Rigidbody body, Collider other);
    }

    public interface IContactBodyPolicy
    {
        bool CanApplyReaction(Rigidbody body);
        void NotifyPhysicalContact(Rigidbody body, Vector3 outwardNormal, Vector3 point);
    }
}
