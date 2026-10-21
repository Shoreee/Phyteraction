using UnityEngine;

namespace PhysicsHands.Dynamics
{
    public interface IJointDriveContext
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

        bool Owns(Collider collider);
        void NotifyPhysicalContact(Rigidbody body);
    }

    public interface IContactBodyPolicy
    {
        bool UseSampledVelocity(Rigidbody body);
        void NotifyPhysicalContact(Rigidbody body);
    }
}
