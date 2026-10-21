using UnityEngine;

namespace PhysicsHands.Dynamics
{
    internal static class JointDrivePolicy
    {
        internal static void Apply(ConfigurableJoint joint, JointRestState rest, bool passive, float positionSpring, float positionDamper, float angularSpring, float angularDamper)
        {
            if (joint == null)
                return;
            if (passive)
            {
                var empty = new JointDrive();
                joint.xDrive = empty;
                joint.yDrive = empty;
                joint.zDrive = empty;
                joint.angularXDrive = empty;
                joint.angularYZDrive = empty;
                joint.slerpDrive = empty;
                return;
            }

            joint.xDrive = rest.XDrive.Scaled(positionSpring, positionDamper);
            joint.yDrive = rest.YDrive.Scaled(positionSpring, positionDamper);
            joint.zDrive = rest.ZDrive.Scaled(positionSpring, positionDamper);
            joint.angularXDrive = rest.AngularXDrive.Scaled(angularSpring, angularDamper);
            joint.angularYZDrive = rest.AngularYZDrive.Scaled(angularSpring, angularDamper);
            joint.slerpDrive = rest.SlerpDrive.Scaled(angularSpring, angularDamper);
        }
    }
}
