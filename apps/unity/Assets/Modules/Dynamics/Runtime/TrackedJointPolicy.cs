using System;
using UnityEngine;

namespace PhysicsHands.Dynamics
{
    internal sealed class TrackedJointPolicy
    {
        internal bool Active
        {
            get;
            private set;
        }

        internal void Update(bool direct, Rigidbody body, ConfigurableJoint joint, JointRestState rest, int iterations, Action applyDrives)
        {
            if (direct != Active)
            {
                Active = direct;
                applyDrives();
                body.interpolation = direct ? RigidbodyInterpolation.None : rest.Interpolation;
                if (direct && joint != null)
                {
                    // World-space muscles provide the pursuit. Keep only anatomical
                    // limits/lengths in the joint, without a competing high-gain motor
                    // or projection teleports through the contact surface.
                    joint.projectionMode = JointProjectionMode.None;
                    joint.enablePreprocessing = false;
                    body.solverIterations = Mathf.Max(96, iterations);
                    body.solverVelocityIterations = 16;
                }
                else if (joint != null)
                {
                    joint.projectionMode = rest.Projection;
                    joint.enablePreprocessing = rest.Preprocessing;
                    body.solverIterations = iterations;
                    body.solverVelocityIterations = Mathf.Max(4, iterations / 2);
                }
            }
        }
    }
}
