using UnityEngine;

namespace PhysicsHands.Dynamics
{
    public static class ConfigurableJointExtensions
    {
        /// <summary>
        /// Sets joint.targetRotation from a rotation expressed in the connected body's space
        /// (or the parent's space when there is no connected body).
        /// </summary>
        public static void SetTargetRotationLocal(this ConfigurableJoint joint, Quaternion targetLocalRotation, Quaternion startLocalRotation)
        {
            SetTargetRotationInternal(joint, targetLocalRotation, startLocalRotation, Space.Self);
        }

        /// <summary>Sets joint.targetRotation from a world-space rotation.</summary>
        public static void SetTargetRotation(this ConfigurableJoint joint, Quaternion targetWorldRotation, Quaternion startWorldRotation)
        {
            SetTargetRotationInternal(joint, targetWorldRotation, startWorldRotation, Space.World);
        }

        private static void SetTargetRotationInternal(ConfigurableJoint joint, Quaternion targetRotation, Quaternion startRotation, Space space)
        {
            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);
            Quaternion result = Quaternion.Inverse(worldToJointSpace);
            result *= space == Space.World ? startRotation * Quaternion.Inverse(targetRotation) : Quaternion.Inverse(targetRotation) * startRotation;
            result *= worldToJointSpace;
            joint.targetRotation = result;
        }

        public static JointDrive Scaled(this JointDrive drive, float springMultiplier, float damperMultiplier)
        {
            drive.positionSpring *= springMultiplier;
            drive.positionDamper *= damperMultiplier;
            return drive;
        }

        public static JointDrive WithSpringDamper(this JointDrive drive, float spring, float damper)
        {
            drive.positionSpring = spring;
            drive.positionDamper = damper;
            return drive;
        }

        public static Vector3 InverseScale(Vector3 a, Vector3 b) => new(b.x != 0f ? a.x / b.x : 0f, b.y != 0f ? a.y / b.y : 0f, b.z != 0f ? a.z / b.z : 0f);
    }
}
