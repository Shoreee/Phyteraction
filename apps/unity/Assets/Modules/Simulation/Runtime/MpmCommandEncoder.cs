using System;
using PhysicsHands.GaussianData;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Simulation
{
    internal static class MpmCommandEncoder
    {
        internal static void Encode(MpmGpuResources gpu, MpmModelAsset asset, ShapeGpu[] shapes, int shapeCount, int bodyCount, float dt, MpmStep step, float simulationTime, float massScale, float momentumReferenceSpeed, bool replay, bool useReferenceConstitutive, bool gravity)
        {
            var cmd = gpu.cmd;
            var cs = gpu.cs;
            float h = step.Duration;
            cmd.Clear();
            cmd.BeginSample("PhysicsHands.MPM.GPU");
            Vector3 shapeLo = Vector3.one * float.MaxValue, shapeHi = -shapeLo;
            for (int i = 0; i < shapeCount; i++)
            {
                // Boxes: centre a with half extents; capsules: segment a-b with radius. Both swept over the step.
                var s = shapes[i];
                bool box = s.b.w != 0;
                Vector3 e = Vector3.one * (box ? ((Vector3)s.he).magnitude : s.a.w), a0 = s.previousA, a1 = s.a;
                Vector3 b0 = box ? a0 : (Vector3)s.previousB, b1 = box ? a1 : (Vector3)s.b;
                shapeLo = Vector3.Min(shapeLo, Vector3.Min(Vector3.Min(a0, a1), Vector3.Min(b0, b1)) - e);
                shapeHi = Vector3.Max(shapeHi, Vector3.Max(Vector3.Max(a0, a1), Vector3.Max(b0, b1)) + e);
            }

            shapeLo -= Vector3.one * (asset.dx * .5f);
            shapeHi += Vector3.one * (asset.dx * .5f);
            cmd.SetComputeVectorParam(cs, "_ShapeLo", shapeLo);
            cmd.SetComputeVectorParam(cs, "_ShapeHi", shapeHi);
            var material = asset.material;
            cmd.SetComputeIntParam(cs, "_Material", (int)material);
            cmd.SetComputeIntParam(cs, "_ReferenceConstitutive", replay || useReferenceConstitutive ? 1 : 0);
            cmd.SetComputeIntParam(cs, "_RenderTotal", asset.UsesTotalDeformation ? 1 : 0);
            cmd.SetComputeFloatParam(cs, "_Mu", asset.Mu);
            cmd.SetComputeFloatParam(cs, "_Lambda", asset.Lambda);
            cmd.SetComputeFloatParam(cs, "_MassScale", massScale);
            float fixedMass = (1 << 23) / Mathf.Max(gpu.heaviest * massScale, 1e-30f);
            cmd.SetComputeFloatParam(cs, "_FixedMass", fixedMass);
            cmd.SetComputeFloatParam(cs, "_FixedMomentum", fixedMass * asset.metersPerUnit / (replay ? 10 : 1.5f * momentumReferenceSpeed));
            cmd.SetBufferData(gpu.reaction, gpu.zeros);
            if (shapeCount > 0)
                cmd.SetBufferData(gpu.shapeBuffer, shapes, 0, 0, shapeCount);
            cmd.SetComputeIntParam(cs, "_ShapeCount", shapeCount);
            cmd.SetComputeIntParam(cs, "_BodyCount", bodyCount);
            cmd.SetComputeIntParam(cs, "_Script", replay ? 1 : 0);
            // Use the source simulation gravity for every particle. The former
            // touched-particle-only rule made sand/jam erupt into isolated strands,
            // and its metric conversion changed PhysFlow's acceleration.
            cmd.SetComputeVectorParam(cs, "_Gravity", asset.gravity);
            cmd.SetComputeIntParam(cs, "_GravityMode", asset.gravity != Vector3.zero ? 1 : 0);
            cmd.SetComputeIntParam(cs, "_Floor", !replay && gravity ? 1 : 0);
            cmd.SetComputeFloatParam(cs, "_Dt", h);
            cmd.SetComputeFloatParam(cs, "_ContactDt", dt);
            cmd.SetComputeFloatParam(cs, "_Damping", Mathf.Exp(-asset.dampingRate * h));
            for (int s = 0; s < step.Count; s++)
            {
                // Shapes move linearly from their pose at the start of this fixed step.
                cmd.SetComputeFloatParam(cs, "_Time", simulationTime + h * s);
                cmd.SetComputeFloatParam(cs, "_ShapeTime", (s + 1f) / step.Count);
                cmd.DispatchCompute(cs, gpu.kP2G, gpu.particleGroups, 1, 1);
                cmd.DispatchCompute(cs, gpu.kArgs, 1, 1, 1);
                if (bodyCount > 0)
                {
                    cmd.DispatchCompute(cs, gpu.kBeginCoupling, (bodyCount + 63) / 64, 1, 1);
                    cmd.DispatchCompute(cs, gpu.kContactBound, gpu.args, 0);
                }
                cmd.DispatchCompute(cs, gpu.kGrid, gpu.args, 0);
                cmd.DispatchCompute(cs, gpu.kG2P, gpu.particleGroups, 1, 1);
                cmd.DispatchCompute(cs, gpu.kClear, gpu.args, 0);
            }
            cmd.EndSample("PhysicsHands.MPM.GPU");
        }
    }
}
