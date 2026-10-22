using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Dynamics
{
    [System.Serializable]
    internal sealed class JointDriverBindings
    {
        public ConfigurableJoint Joint;
        public Transform TargetRotation;
        public Transform TrackingTarget;
        public Transform CustomMassCenter;
        public VelocityEstimator VelocityEstimator;
    }

    [System.Serializable]
    internal sealed class JointDriverSettings
    {
        public bool Enforce = true;
        public bool CreateTarget;
        public bool ReinitializeOnEnable;
        public bool ResetOnDisable;
        public bool UpdateAnchor;
        public bool Kinematic;
        public bool RemoveJoint;
        public bool RotationOnly;
        public bool CancelGravity = true;
        public int SolverIterationCount = 12;
        public bool UseTargetVelocity;
    }

    [System.Serializable]
    internal sealed class TrackedMuscleSettings
    {
        [Min(1)]
        public float TrackingFrequency = 60f;
        [Min(1)]
        public float TrackingAngularFrequency = 50f;
        [Min(1)]
        public float MaxTrackingAcceleration = 180f;
        [Min(1)]
        public float MaxFreeTrackingAcceleration = 240f;
        [Min(1)]
        public float MaxTrackingAngularAcceleration = 18000f;
    }
}
