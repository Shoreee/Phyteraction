using System.Collections.Generic;
using PhysicsHands.Dynamics;
using UnityEngine;

namespace PhysicsHands.Hands
{
    internal sealed class PhysicsHandBody
    {
        readonly Transform root;
        readonly DetachableHand wrist;
        readonly List<PhysicsFinger> fingers;
        readonly List<PhysicsFingerBone> bones;
        readonly List<Collider> colliders, fingerColliders;
        readonly float defaultScale;
        internal bool IsKinematic
        {
            get;
            private set;
        }

        internal PhysicsHandBody(Transform root, DetachableHand wrist, List<PhysicsFinger> fingers, List<PhysicsFingerBone> bones, List<Collider> colliders, List<Collider> fingerColliders)
        {
            this.root = root;
            this.wrist = wrist;
            this.fingers = fingers;
            this.bones = bones;
            this.colliders = colliders;
            this.fingerColliders = fingerColliders;
            defaultScale = root.localScale.x;
        }

        internal void MakeKinematic(bool kinematic)
        {
            IsKinematic = kinematic;
            foreach (var driver in root.GetComponentsInChildren<PhysicsJointDriver>(true))
                driver.MakeKinematic(kinematic);
        }

        internal void EnableCollisions(bool enable)
        {
            foreach (var collider in colliders)
                collider.enabled = enable;
        }

        internal void SetMassMultiplier(float multiplier)
        {
            wrist.SetMassMultiplier(multiplier);
            foreach (var bone in bones)
                bone.SetMassMultiplier(multiplier);
        }

        internal void IgnoreAll(IReadOnlyList<Collider> others, bool ignore) => Ignore(colliders, others, ignore);
        internal void IgnoreFingers(IReadOnlyList<Collider> others, bool ignore) => Ignore(fingerColliders, others, ignore);
        static void Ignore(List<Collider> own, IReadOnlyList<Collider> others, bool ignore)
        {
            foreach (var a in own)
                foreach (var other in others)
                    if (a != null && other != null)
                        Physics.IgnoreCollision(a, other, ignore);
        }

        internal void SetScale(float scale)
        {
            root.localScale = Vector3.one * (defaultScale * scale);
            AdjustJoints();
        }

        internal void AdjustJoints()
        {
            foreach (var driver in root.GetComponentsInChildren<PhysicsJointDriver>(true))
                driver.AdjustJoint();
        }

        internal void GoInTPose()
        {
            foreach (var finger in fingers)
                finger.GoInTPose();
        }

        internal void DetachFingers()
        {
            foreach (var bone in bones)
                bone.DetachBone();
        }

        internal void FreeFingers()
        {
            foreach (var bone in bones)
                bone.FreeMovement();
        }

        internal void ResetFingers()
        {
            foreach (var bone in bones)
                bone.ResetMultipliers();
        }
    }
}
