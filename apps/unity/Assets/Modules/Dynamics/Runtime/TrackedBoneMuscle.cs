using UnityEngine;

namespace PhysicsHands.Dynamics
{
    internal sealed class TrackedBoneMuscle
    {
        Vector3 lastPosition;
        Quaternion lastRotation;
        bool hasPose;
        public void Reset() => hasPose = false;
        public void Step(Rigidbody body, Vector3 position, Quaternion rotation, float muscle, float linearFrequency, float angularFrequency, float accelerationLimit, float angularLimit)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 velocity = hasPose ? Vector3.ClampMagnitude((position - lastPosition) / dt, 5f) : Vector3.zero;
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
            Vector3 acceleration = (kp * (goal - body.worldCenterOfMass) + (kd + kp * dt) * (velocity + Vector3.Cross(omega, offset) - body.linearVelocity)) * g;
            body.AddForce(Vector3.ClampMagnitude(acceleration, accelerationLimit), ForceMode.Acceleration);
            // Finger orientation needs its own muscle bandwidth: translating the
            // centre alone leaves a hinged chain trailing behind a fast wrist.
            w = 2f * Mathf.PI * angularFrequency;
            kp = w * w * muscle;
            kd = 2 * w * Mathf.Sqrt(muscle);
            g = 1f / (1f + kd * dt + kp * dt * dt);
            Vector3 alpha = (kp * RotationVector(rotation * Quaternion.Inverse(body.rotation)) + (kd + kp * dt) * (omega - body.angularVelocity)) * g;
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
