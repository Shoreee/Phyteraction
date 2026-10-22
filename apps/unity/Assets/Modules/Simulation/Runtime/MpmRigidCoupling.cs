using System;
using PhysicsHands.Dynamics;
using PhysicsHands.GaussianData;
using UnityEngine;

namespace PhysicsHands.Simulation
{
    internal struct CouplingBodyGpu
    {
        public Vector4 centreInvMass, inertia0, inertia1, inertia2, velocity, omega;
    }

    /// <summary>Same-step finite-mass impulse exchange. No force replay, impulse rescaling or stale sample hold.</summary>
    internal sealed class MpmRigidCoupling
    {
        internal const int Stride = 16; // impulse/weight, torque, weighted normal, weighted contact position
        internal readonly CouplingBodyGpu[] Bodies = new CouplingBodyGpu[ContactGatherer.MaxShapes];
        readonly Rigidbody[] bodies = new Rigidbody[ContactGatherer.MaxShapes];
        readonly float[] result = new float[ContactGatherer.MaxShapes * Stride];
        int count;
        internal int Failures { get; private set; }
        internal void Reset() { Array.Clear(bodies, 0, bodies.Length); count = 0; Failures = 0; }

        internal int Prepare(ContactGatherer contacts, ShapeGpu[] shapes, MpmModelAsset asset, Transform owner, float dt)
        {
            count = 0;
            Matrix4x4 worldFromSim = owner.localToWorldMatrix * asset.localFromSim;
            Matrix4x4 simFromWorld = worldFromSim.inverse;
            float length = asset.metersPerUnit, massScale = length * length * length;
            Matrix4x4 basis = simFromWorld;
            basis.SetColumn(3, new Vector4(0, 0, 0, 1));
            for (int c = 0; c < 3; c++) basis.SetColumn(c, basis.GetColumn(c) * length);
            float orientation = Mathf.Sign(basis.determinant);
            for (int i = 0; i < contacts.Count; i++)
            {
                Rigidbody body = contacts.BodyAt(i);
                int index = -1;
                if (body != null && !body.isKinematic)
                {
                    index = 0; while (index < count && bodies[index] != body) index++;
                    if (index == count)
                    {
                        bodies[count++] = body;
                        Quaternion inertia = body.rotation * body.inertiaTensorRotation;
                        Vector3 tensor = body.inertiaTensor;
                        Vector3 Inverse(Vector3 v) => inertia * Vector3.Scale(Quaternion.Inverse(inertia) * v,
                            new Vector3(1 / tensor.x, 1 / tensor.y, 1 / tensor.z));
                        Vector3 Column(Vector3 direction) => basis.MultiplyVector(Inverse(basis.transpose.MultiplyVector(direction))) * (massScale * length * length);
                        Vector3 x = Column(Vector3.right), y = Column(Vector3.up), z = Column(Vector3.forward);
                        Vector3 torque = body.GetAccumulatedTorque(dt);
                        Vector3 velocity = body.linearVelocity + body.GetAccumulatedForce(dt) * (dt / body.mass);
                        Vector3 omega = body.angularVelocity + Inverse(torque) * dt;
                        Bodies[index] = new CouplingBodyGpu {
                            centreInvMass = V(simFromWorld.MultiplyPoint3x4(body.worldCenterOfMass), massScale / body.mass),
                            inertia0 = V(new Vector3(x.x, y.x, z.x)), inertia1 = V(new Vector3(x.y, y.y, z.y)), inertia2 = V(new Vector3(x.z, y.z, z.z)),
                            velocity = V(simFromWorld.MultiplyVector(velocity)), omega = V(basis.MultiplyVector(omega) * orientation) };
                    }
                }
                var shape = shapes[i]; shape.he.w = index + 1; shapes[i] = shape;
            }
            return count;
        }
        static Vector4 V(Vector3 v, float w = 0) => new(v.x, v.y, v.z, w);

        internal void Apply(ComputeBuffer buffer, MpmModelAsset asset, Transform owner, ContactGatherer contacts, float dt, out int pairs, out float force)
        {
            pairs = 0; force = 0;
            if (count == 0) return;
            // Deliberately synchronize the contact boundary before PhysX integrates this step.
            // This costs a GPU/CPU wait, but eliminates an uncontrolled multi-frame coupling delay.
            try { buffer.GetData(result); }
            catch { Failures++; throw; }
            Matrix4x4 world = owner.localToWorldMatrix * asset.localFromSim;
            float length = asset.metersPerUnit, massScale = length * length * length;
            Matrix4x4 basis = world;
            basis.SetColumn(3, new Vector4(0, 0, 0, 1));
            for (int c = 0; c < 3; c++) basis.SetColumn(c, basis.GetColumn(c) / length);
            float handedness = Mathf.Sign(basis.determinant);
            for (int i = 0; i < count; i++)
            {
                int r = i * Stride; if (result[r + 3] <= 0) continue;
                Rigidbody body = bodies[i];
                if (body == null || body.isKinematic || contacts.Policy?.CanApplyReaction(body) == false) continue;
                Vector3 impulse = -world.MultiplyVector(new Vector3(result[r], result[r + 1], result[r + 2])) * massScale;
                Vector3 torque = -basis.MultiplyVector(new Vector3(result[r + 4], result[r + 5], result[r + 6])) * (handedness * massScale * length * length);
                Vector3 normal = basis.MultiplyVector(new Vector3(result[r + 8], result[r + 9], result[r + 10])).normalized;
                Vector3 point = world.MultiplyPoint3x4(new Vector3(result[r + 12], result[r + 13], result[r + 14]) / result[r + 3]);
                contacts.Policy?.NotifyPhysicalContact(body, normal, point);
                body.AddForce(impulse, ForceMode.Impulse);
                body.AddTorque(torque, ForceMode.Impulse);
                force += impulse.magnitude / dt; pairs++;
            }
        }
    }
}
