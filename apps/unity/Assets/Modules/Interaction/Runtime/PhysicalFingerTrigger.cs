using System;
using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>A spring-return physical trigger. A recent tracked index contact authorizes one pull.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PhysicalFingerTrigger : MonoBehaviour
    {
        public HingeJoint hinge;
        public ConfigurableJoint slider;
        [Min(.001f)] public float slideTravel = .006f;
        [Min(1)] public float hingeTravel = 20;
        public Vector3 slideAxis = Vector3.back;
        [Range(0, 1)] public float pressFraction = .7f, releaseFraction = .2f;
        [Min(0)] public float settleSeconds = .05f;
        [Tooltip("Authored grip centre; assistance requires a nearby gripping palm and independent index closure.")]
        public Transform gripRegion;
        public bool assistTrackedIndex = true;
        readonly TrackedTriggerIntent intent = new();
        public void BindHands(HandGrabber[] grabbers) => intent.Bind(grabbers);
        public event Action<PhysicsHand> Pulled;
        Rigidbody body;
        Vector3 restPosition, restAxis;
        Quaternion restRotation;
        PhysicsHand contactHand;
        float contactTime, releasedSince;
        Vector3 restTriggerPoint;
        bool armed;
        public float Pull
        {
            get
            {
                if (hinge != null)
                {
                    Quaternion relative = Quaternion.Inverse(hinge.connectedBody.rotation) * body.rotation;
                    Quaternion delta = Quaternion.Inverse(restRotation) * relative;
                    float twist = 2 * Mathf.Atan2(Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), hinge.axis.normalized), delta.w);
                    return Mathf.DeltaAngle(0, twist * Mathf.Rad2Deg) / hingeTravel;
                }
                Vector3 local = Quaternion.Inverse(slider.connectedBody.rotation) * (body.position - slider.connectedBody.position);
                return Vector3.Dot(local - restPosition, restAxis) / slideTravel;
            }
        }
        public bool Armed => armed;
        void Awake()
        {
            body = GetComponent<Rigidbody>();
            if ((hinge == null) == (slider == null) ||
                (hinge != null ? hinge.connectedBody == null : slider.connectedBody == null))
                throw new InvalidOperationException($"{name}: assign exactly one connected trigger hinge or slider.");
            if (releaseFraction >= pressFraction || slideTravel <= 0 || hingeTravel <= 0)
                throw new InvalidOperationException($"{name}: trigger travel must be positive and release must precede press.");
            var connected = hinge != null ? hinge.connectedBody : slider.connectedBody;
            restTriggerPoint = Quaternion.Inverse(connected.rotation) * (body.worldCenterOfMass - connected.position);
            if (hinge != null)
                restRotation = Quaternion.Inverse(hinge.connectedBody.rotation) * body.rotation;
            if (slider != null)
            {
                restPosition = Quaternion.Inverse(slider.connectedBody.rotation) * (body.position - slider.connectedBody.position);
                restAxis = slider.connectedBody.transform.InverseTransformDirection(transform.TransformDirection(slideAxis)).normalized;
            }
        }
        void OnEnable() => ResetPull();
        public void ResetPull()
        {
            armed = false; contactHand = null; intent.Reset();
            contactTime = releasedSince = float.NegativeInfinity;
        }
        void FixedUpdate()
        {
            float pull = Pull;
            if (!float.IsFinite(pull)) { ResetPull(); return; }
            var connected = hinge != null ? hinge.connectedBody : slider.connectedBody;
            Vector3 axis = connected.rotation * (hinge != null ? Vector3.back : restAxis);
            PhysicsHand assistedHand = null;
            float assisted = assistTrackedIndex ? intent.Sample(connected, gripRegion,
                connected.position + connected.rotation * restTriggerPoint, axis, out assistedHand) : 0;
            if (assistedHand != null && assisted > pull)
            {
                // A small internal actuator follows use intent, so guard collisions do not
                // require the player to force a tracked finger through the trigger mesh.
                // Its equal/opposite load stays within the two physical gun bodies.
                float error = assisted - Mathf.Clamp01(pull);
                if (hinge != null)
                {
                    Vector3 torque = body.rotation * hinge.axis.normalized * (.08f * error);
                    body.AddTorque(torque); connected.AddTorque(-torque);
                }
                else
                {
                    Vector3 force = axis * (4 * error);
                    body.AddForceAtPosition(force, body.worldCenterOfMass);
                    connected.AddForceAtPosition(-force, body.worldCenterOfMass);
                }
            }
            if (pull <= releaseFraction && assisted <= releaseFraction)
            {
                if (float.IsNegativeInfinity(releasedSince)) releasedSince = Time.fixedTime;
                if (Time.fixedTime - releasedSince >= settleSeconds) armed = true;
            }
            else releasedSince = float.NegativeInfinity;
            if (!armed || Mathf.Max(pull, assisted) < pressFraction) return;
            // Consume every physical pull, including impacts by non-finger objects. They cannot
            // leave a pre-pressed trigger armed for a later fingertip touch.
            armed = false;
            if (assisted >= pressFraction && assistedHand != null)
                Pulled?.Invoke(assistedHand);
            else if (contactHand != null && contactHand.GoodTracking && Time.fixedTime - contactTime <= .06f)
                Pulled?.Invoke(contactHand);
        }
        void OnCollisionEnter(Collision collision) => Contact(collision.collider);
        void OnCollisionStay(Collision collision) => Contact(collision.collider);
        void Contact(Collider collider)
        {
            var bone = collider.GetComponentInParent<PhysicsFingerBone>();
            var hand = bone != null ? bone.PhysicsHand : null;
            if (hand == null || !hand.GoodTracking) return;
            foreach (var finger in hand.Fingers)
                if (finger.FingerType == HandFinger.Index && Array.IndexOf(finger.Bones, bone) >= 0)
                { contactHand = hand; contactTime = Time.fixedTime; return; }
        }
        void OnDisable() => ResetPull();
    }
}
