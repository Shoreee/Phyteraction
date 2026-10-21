using PhysicsHands.Dynamics;
using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>One compliant connection and wrap per holder; never changes object weight or contacts.</summary>
    [DefaultExecutionOrder(-10)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Interaction", "PhysicsHands.Runtime", "HoldingLink")]
    public sealed class HoldingLink : MonoBehaviour
    {
        [SerializeField, Min(0)] float _linearSpring = 1200;
        [SerializeField, Min(0)] float _linearDamper = 45;
        [SerializeField, Min(0)] float _angularSpring = 20;
        [SerializeField, Min(0)] float _angularDamper = 2;
        [SerializeField, Min(0)] float _maximumForce = 80;
        [SerializeField, Min(0)] float _maximumTorque = 6;
        [SerializeField, Min(.01f)] float _alignmentTime = .25f;
        Holder holder;
        PhysicsHand hand;
        Holdable held;
        GripAnchor anchor;
        ConfigurableJoint joint;
        GeometryHandWrap wrap;
        Vector3 startAnchor, endAnchor;
        Quaternion initialRelative, targetRelative;
        float progress;
        public bool IsAttached => joint != null;
        public Holdable Holdable => held;

        public bool Attach(Holder owner, Holdable target, bool distantGrab)
        {
            Release();
            var grabber = owner as HandGrabber;
            PhysicsHand physicalHand = grabber != null ? grabber.Hand : null;
            if (target.Rigidbody.isKinematic || physicalHand == null) return false;
            GripAnchor selected = target.FindAnchor(grabber);
            if (selected == null && !distantGrab) return false;
            if (selected != null && !selected.TryOccupy(grabber)) return false;
            holder = owner;
            hand = physicalHand;
            held = target;
            anchor = selected;
            try
            {
                Rigidbody body = target.Rigidbody;
                Rigidbody palmBody = hand.DetachableHand.Rb;
                Transform palm = hand.Palm;
                joint = body.gameObject.AddComponent<ConfigurableJoint>();
                joint.autoConfigureConnectedAnchor = false;
                joint.connectedBody = palmBody;
                joint.enableCollision = true;
                joint.enablePreprocessing = false;
                joint.rotationDriveMode = RotationDriveMode.Slerp;
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                Vector3 objectPoint = anchor != null ? anchor.transform.position : owner.SnapJointAnchor.position;
                joint.anchor = body.transform.InverseTransformPoint(objectPoint);
                startAnchor = palmBody.transform.InverseTransformPoint(objectPoint);
                endAnchor = palmBody.transform.InverseTransformPoint(palm.position);
                initialRelative = Quaternion.Inverse(palmBody.rotation) * body.rotation;
                // Object-authored palm frames specify the complete alignment, including handedness.
                Quaternion correction = anchor != null ? palm.rotation * Quaternion.Inverse(anchor.PalmRotation(grabber.Side)) : Quaternion.identity;
                targetRelative = Quaternion.Inverse(palmBody.rotation) * correction * body.rotation;
                var linear = new JointDrive { positionSpring = _linearSpring, positionDamper = _linearDamper, maximumForce = _maximumForce };
                joint.xDrive = joint.yDrive = joint.zDrive = linear;
                joint.slerpDrive = new JointDrive { positionSpring = _angularSpring, positionDamper = _angularDamper, maximumForce = _maximumTorque };
                progress = 0;
                ApplyAlignment();
                if (anchor != null)
                {
                    wrap = new GeometryHandWrap(hand, held.Colliders);
                    hand.SetJointTargets(wrap);
                }
                return true;
            }
            catch
            {
                Release();
                throw;
            }
        }
        void FixedUpdate()
        {
            if (joint == null) { if (held != null) holder.Release(); return; }
            progress = Mathf.MoveTowards(progress, 1, Time.fixedDeltaTime / _alignmentTime);
            ApplyAlignment();
            wrap?.Step(Time.fixedDeltaTime);
            Vector3 a = joint.transform.TransformPoint(joint.anchor);
            Vector3 b = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
            // Physical connection failure is independent of tracked-to-physical hand offset.
            if (progress >= 1 && Vector3.Distance(a, b) > held.HandToObjectReleaseDistance)
                holder.Release();
        }
        void ApplyAlignment()
        {
            float t = Mathf.SmoothStep(0, 1, progress);
            joint.connectedAnchor = Vector3.Lerp(startAnchor, endAnchor, t);
            joint.SetTargetRotationLocal(Quaternion.Slerp(initialRelative, targetRelative, t), initialRelative);
        }
        void OnDisable() => holder?.ForceRelease();
        void OnDestroy() => Release();
        public void Release()
        {
            if (hand != null && wrap != null) hand.ClearJointTargets(wrap);
            wrap = null;
            if (anchor != null) anchor.Vacate(holder);
            anchor = null;
            if (joint != null)
            {
                // Destroy is deferred; disconnect immediately so resetting/switching cannot
                // submit one final stale constraint in the current physics step.
                joint.connectedBody = null;
                joint.xDrive = joint.yDrive = joint.zDrive = default;
                joint.slerpDrive = default;
                Destroy(joint);
            }
            joint = null;
            held = null;
            hand = null;
            holder = null;
        }
    }
}
