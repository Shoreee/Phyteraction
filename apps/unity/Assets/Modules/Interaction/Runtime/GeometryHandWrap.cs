using System.Collections.Generic;
using PhysicsHands.Dynamics;
using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>Contact-constrained finger targets, independent of raw tracked gesture poses.</summary>
    internal sealed class GeometryHandWrap : IJointTargetSource
    {
        sealed class Bone
        {
            internal PhysicsJointDriver driver;
            internal Collider[] colliders;
            internal Quaternion local;
            internal Vector3 localPosition, probe;
            internal Pose pose;
            internal int parent;
            internal bool thumb;
        }
        readonly Bone[] bones;
        readonly Dictionary<Rigidbody, int> indices = new();
        readonly IReadOnlyList<Collider> surfaces;
        readonly PhysicsHand hand;
        readonly Vector3 localScale;
        readonly Collider[] nearby = new Collider[64];
        const float ContactSlop = .00025f;

        internal GeometryHandWrap(PhysicsHand hand, IReadOnlyList<Collider> surfaces)
        {
            this.hand = hand;
            this.surfaces = surfaces;
            localScale = hand.Wrist.lossyScale;
            var list = new List<Bone>();
            foreach (PhysicsFinger finger in hand.Fingers)
                foreach (PhysicsFingerBone phalanx in finger.Bones)
                {
                    PhysicsJointDriver driver = phalanx.PhysicsJointDriver;
                    if (driver == null || driver.Joint == null) continue;
                    Rigidbody parentBody = driver.Joint.connectedBody;
                    int parent = parentBody != null && indices.TryGetValue(parentBody, out int index) ? index : -1;
                    Transform space = parentBody != null ? parentBody.transform : hand.Wrist;
                    indices.Add(driver.Rigidbody, list.Count);
                    list.Add(new Bone { driver = driver, colliders = phalanx.GetColliders(),
                        local = Quaternion.Inverse(space.rotation) * driver.transform.rotation,
                        localPosition = space.InverseTransformPoint(driver.transform.position),
                        probe = Probe(driver, phalanx.GetColliders()),
                        parent = parent, thumb = finger.FingerType == HandFinger.Thumb });
                }
            bones = list.ToArray();
            Rebuild(0);
        }

        static Vector3 Probe(PhysicsJointDriver driver, Collider[] colliders)
        {
            foreach (Collider collider in colliders)
            {
                if (collider == null || collider.isTrigger) continue;
                Vector3 point = collider.bounds.center;
                if (collider is CapsuleCollider capsule)
                {
                    Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                    Vector3 a = capsule.transform.TransformPoint(capsule.center + axis * Mathf.Max(0, capsule.height * .5f - capsule.radius));
                    Vector3 b = capsule.transform.TransformPoint(capsule.center - axis * Mathf.Max(0, capsule.height * .5f - capsule.radius));
                    point = (a-driver.transform.position).sqrMagnitude > (b-driver.transform.position).sqrMagnitude ? a : b;
                }
                return driver.transform.InverseTransformPoint(point);
            }
            return Vector3.right * .015f;
        }
        public bool TryGetTargetPose(Rigidbody body, out Pose pose)
        {
            if (body != null && indices.TryGetValue(body, out int index))
            {
                pose = bones[index].pose;
                return true;
            }
            pose = default;
            return false;
        }

        internal void Step(float dt)
        {
            Rebuild(0);
            for (int i = 0; i < bones.Length; i++)
            {
                Bone bone = bones[i];
                ConfigurableJoint joint = bone.driver.Joint;
                if (joint == null) continue;
                // Small coordinate-descent steps use actual collider surfaces. Both signs are
                // tried, so mirrored skeletons do not require historical bone-name rules.
                float step = Mathf.Min(2, 100 * dt);
                Improve(i, joint.axis, joint.lowAngularXLimit.limit, joint.highAngularXLimit.limit, step);
                if (bone.thumb && (bone.parent < 0 || !bones[bone.parent].thumb))
                    Improve(i, joint.secondaryAxis, -joint.angularYLimit.limit, joint.angularYLimit.limit, step);
            }
        }

        void Improve(int index, Vector3 axis, float low, float high, float step)
        {
            Bone bone = bones[index];
            if (high - low < .01f) return;
            Quaternion original = bone.local;
            float best = SurfaceDistance(index);
            Quaternion selected = original;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Quaternion candidate = original * Quaternion.AngleAxis(step * sign, axis);
                Quaternion delta = Quaternion.Inverse(bone.driver.RestLocalRotation) * candidate;
                Vector3 v = new(delta.x, delta.y, delta.z);
                float angle = Mathf.DeltaAngle(0, 2 * Mathf.Atan2(Vector3.Dot(v, axis.normalized), delta.w) * Mathf.Rad2Deg);
                if (angle < low || angle > high) continue;
                bone.local = candidate;
                Rebuild(index);
                float distance = SurfaceDistance(index);
                if (distance < best - .000001f && !Penetrates(index))
                {
                    best = distance;
                    selected = candidate;
                }
            }
            bone.local = selected;
            Rebuild(index);
        }

        void Rebuild(int from)
        {
            for (int i = from; i < bones.Length; i++)
            {
                Bone b = bones[i];
                Rigidbody connected = b.driver.Joint != null ? b.driver.Joint.connectedBody : null;
                Pose parent = b.parent >= 0 ? bones[b.parent].pose : connected != null ?
                    new Pose(connected.position, connected.rotation) : new Pose(hand.Wrist.position, hand.Wrist.rotation);
                b.pose = new Pose(parent.position + parent.rotation * Vector3.Scale(b.localPosition, localScale), parent.rotation * b.local);
            }
        }

        float SurfaceDistance(int from)
        {
            float sum = 0;
            for (int i = from; i < bones.Length; i++)
            {
                if (i != from && !DescendsFrom(i, from)) continue;
                Bone b = bones[i];
                // The distal end supplies a useful closure gradient even for a proximal bone.
                Vector3 point = b.pose.position + b.pose.rotation * Vector3.Scale(b.probe, localScale);
                float distance = float.PositiveInfinity;
                for (int surfaceIndex = 0; surfaceIndex < surfaces.Count; surfaceIndex++)
                {
                    Collider surface = surfaces[surfaceIndex];
                    if (surface != null && surface.enabled)
                        distance = Mathf.Min(distance, (surface.ClosestPoint(point) - point).sqrMagnitude);
                }
                sum += distance;
            }
            return sum;
        }
        bool DescendsFrom(int bone, int ancestor)
        {
            for (int p = bones[bone].parent; p >= 0; p = bones[p].parent)
                if (p == ancestor) return true;
            return false;
        }
        bool Penetrates(int from)
        {
            for (int i = from; i < bones.Length; i++)
            {
                if (i != from && !DescendsFrom(i, from)) continue;
                Bone b = bones[i];
                foreach (Collider own in b.colliders)
                {
                    if (own == null || own.isTrigger) continue;
                    Quaternion rotation = b.pose.rotation * Quaternion.Inverse(b.driver.transform.rotation) * own.transform.rotation;
                    Vector3 position = b.pose.position + b.pose.rotation * b.driver.transform.InverseTransformPoint(own.transform.position);
                    int count = Physics.OverlapSphereNonAlloc(position, own.bounds.extents.magnitude + .01f, nearby, ~0, QueryTriggerInteraction.Ignore);
                    for (int c = 0; c < count; c++)
                    {
                        Collider other = nearby[c];
                        nearby[c] = null;
                        if (other == null || hand.Owns(other)) continue;
                        if (Physics.ComputePenetration(own, position, rotation, other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > ContactSlop)
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
