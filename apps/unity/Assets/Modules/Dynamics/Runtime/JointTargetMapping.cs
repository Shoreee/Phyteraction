using UnityEngine;

namespace PhysicsHands.Dynamics
{
    internal static class JointTargetMapping
    {
        internal static Vector3 LocalPosition(Transform target, Transform bone, JointRestState rest)
        {
            if (target == null)
                return rest.LocalPosition;
            Vector3 targetScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
            Vector3 boneScale = bone.parent != null ? bone.parent.lossyScale : Vector3.one;
            return rest.LocalPosition + Vector3.Scale(target.localPosition - rest.TargetLocalPosition, ConfigurableJointExtensions.InverseScale(targetScale, boneScale));
        }

        internal static Quaternion LocalRotation(Transform target, JointRestState rest) => target == null ? rest.LocalRotation : rest.LocalRotation * Quaternion.Inverse(rest.TargetLocalRotation) * target.localRotation;
        internal static Pose AttachedPose(Transform bone, Transform target, JointRestState rest, bool physicsPose)
        {
            var parent = bone.parent;
            var localPosition = LocalPosition(target, bone, rest);
            var localRotation = LocalRotation(target, rest);
            if (parent == null)
                return new Pose(localPosition, localRotation);
            var body = physicsPose ? parent.GetComponent<Rigidbody>() : null;
            var position = body != null ? body.position : parent.position;
            var rotation = body != null ? body.rotation : parent.rotation;
            return new Pose(position + rotation * Vector3.Scale(parent.lossyScale, localPosition), rotation * localRotation);
        }
    }
}
