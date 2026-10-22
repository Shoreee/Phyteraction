using UnityEngine;

namespace PhysicsHands.Dynamics
{
    internal sealed class TrackedBoneMuscle
    {
        Vector3 lastPosition;
        Quaternion lastRotation;
        bool hasPose;
        public void Reset() => hasPose = false;
        public void Step(Rigidbody body, Vector3 position, Quaternion rotation, float muscle, float linearFrequency, float angularFrequency, float accelerationLimit, float angularLimit, Vector3 contactNormal, bool hasContactPoint = false, Vector3 contactPoint = default)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 velocity = hasPose ? (position - lastPosition) / dt : Vector3.zero;
            Vector3 omega = hasPose ? RotationVector(rotation * Quaternion.Inverse(lastRotation)) / dt : Vector3.zero;
            omega = Vector3.ClampMagnitude(omega, 30f);
            lastPosition = position;
            lastRotation = rotation;
            hasPose = true;
            // Backward PD: finite, mass-independent pursuit. Contacts and locked
            // bone lengths stay in PhysX; no position/velocity writes bypass them.
            float w = 2f * Mathf.PI * linearFrequency, kp = w * w * muscle, kd = 2f * w * Mathf.Sqrt(muscle);
            float g = 1f / (1f + kd * dt + kp * dt * dt);
            Vector3 offset = rotation * body.centerOfMass;
            Vector3 goal = position + offset;
            Vector3 error = goal - body.worldCenterOfMass;
            Vector3 desiredVelocity = velocity + Vector3.Cross(omega, offset);
            if (contactNormal.sqrMagnitude > .5f)
            {
                // Solver contact defines an admissible half-space. Keep raw targets intact,
                // pursue tangential articulation, and retain damping and outward withdrawal.
                error -= contactNormal * Mathf.Min(0, Vector3.Dot(error, contactNormal));
                desiredVelocity -= contactNormal * Mathf.Min(0, Vector3.Dot(desiredVelocity, contactNormal));
            }
            Vector3 acceleration = (kp * error + (kd + kp * dt) * (desiredVelocity - body.linearVelocity)) * g;
            body.AddForce(Vector3.ClampMagnitude(acceleration, accelerationLimit), ForceMode.Acceleration);
            // Finger orientation needs its own muscle bandwidth: translating the
            // centre alone leaves a hinged chain trailing behind a fast wrist.
            w = 2f * Mathf.PI * angularFrequency;
            kp = w * w * muscle;
            kd = 2 * w * Mathf.Sqrt(muscle);
            g = 1f / (1f + kd * dt + kp * dt * dt);
            Vector3 rotationError = RotationVector(rotation * Quaternion.Inverse(body.rotation));
            if (hasContactPoint)
            {
                // Angular pursuit must obey the same surface half-space as translation.
                // Otherwise a rotating fingertip keeps loading contact friction even after
                // inward COM pursuit has been removed.
                Vector3 axis = Vector3.Cross(contactPoint - body.worldCenterOfMass, contactNormal);
                float lengthSquared = axis.sqrMagnitude;
                if (lengthSquared > 1e-10f)
                {
                    axis /= Mathf.Sqrt(lengthSquared);
                    rotationError -= axis * Mathf.Min(0, Vector3.Dot(rotationError, axis));
                    omega -= axis * Mathf.Min(0, Vector3.Dot(omega, axis));
                }
            }
            Vector3 alpha = (kp * rotationError + (kd + kp * dt) * (omega - body.angularVelocity)) * g;
            alpha = Vector3.ClampMagnitude(alpha, angularLimit);
            // Convert desired world angular acceleration with this bone's inertia.
            Quaternion inertiaFrame = body.rotation * body.inertiaTensorRotation;
            Vector3 torque = inertiaFrame * Vector3.Scale(body.inertiaTensor, Quaternion.Inverse(inertiaFrame) * alpha);
            body.AddTorque(torque, ForceMode.Force);
        }

        private static Vector3 RotationVector(Quaternion q)
        {
            if (q.w < 0)
                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            q.ToAngleAxis(out float angle, out Vector3 axis);
            return axis.sqrMagnitude > 0 && float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad) : Vector3.zero;
        }
    }
}
