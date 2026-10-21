using UnityEngine;

namespace PhysicsHands.Dynamics
{
    internal sealed class JointRestState
    {
        public float Mass;
        public Vector3 LossyScale;
        public Vector3 ConnectedLossyScale;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Quaternion TargetLocalRotation;
        public Vector3 TargetLocalPosition;
        public Quaternion JointStartLocalRotation;
        public Vector3 Anchor;
        public Vector3 ConnectedAnchor;
        public JointDrive XDrive;
        public JointDrive YDrive;
        public JointDrive ZDrive;
        public JointDrive AngularXDrive;
        public JointDrive AngularYZDrive;
        public JointDrive SlerpDrive;
        public bool CancelGravity;
        public RigidbodyInterpolation Interpolation;
        public JointProjectionMode Projection;
        public bool Preprocessing;
        internal void Capture(ConfigurableJoint joint, Transform space, Transform bone, bool initialized)
        {
            if (joint == null)
                return;
            JointStartLocalRotation = space != null ? Quaternion.Inverse(space.rotation) * bone.rotation : bone.rotation;
            Anchor = joint.anchor;
            ConnectedAnchor = joint.connectedAnchor;
            ConnectedLossyScale = space != null ? space.lossyScale : Vector3.one;
            if (initialized)
                return;
            XDrive = joint.xDrive;
            YDrive = joint.yDrive;
            ZDrive = joint.zDrive;
            AngularXDrive = joint.angularXDrive;
            AngularYZDrive = joint.angularYZDrive;
            SlerpDrive = joint.slerpDrive;
        }
    }
}
