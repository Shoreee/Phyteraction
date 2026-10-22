using System;
using System.Collections.Generic;
using Unity.Mathematics;
using PhysicsHands.GaussianData;
using UnityEngine;

namespace PhysicsHands.Simulation
{
    public static class PhysFlowCageBuilder
    {
        // A bearing fixes axial/radial coordinates while leaving azimuth free. It must not
        // silently become a welded region when the material discretization changes.
        public static CageSolver CreateSolver(MpmModelAsset source, ElasticCage cage)
        {
            var constraints = new List<CageAxleConstraint>();
            var simFromLocal = source.localFromSim.inverse;
            for (int node = 0; node < cage.rest.Length; node++)
            {
                if (cage.inverseMass[node] == 0) continue;
                Vector3 point = simFromLocal.MultiplyPoint3x4(cage.rest[node]);
                foreach (var axle in source.axles)
                {
                    Vector3 axis = ((Vector3)axle.normal).normalized, delta = point - (Vector3)axle.point;
                    float along = Vector3.Dot(delta, axis);
                    if (Mathf.Abs(along) > axle.normal.w || (delta - axis * along).sqrMagnitude > axle.point.w * axle.point.w) continue;
                    Vector3 centre = source.localFromSim.MultiplyPoint3x4(axle.point);
                    Vector3 localAxis = source.localFromSim.MultiplyVector(axis).normalized;
                    Vector3 offset = cage.rest[node] - centre;
                    float axial = Vector3.Dot(offset, localAxis);
                    constraints.Add(new CageAxleConstraint { node = node, centre = centre, axis = localAxis, axial = axial, radius = (offset - localAxis * axial).magnitude });
                    break;
                }
            }
            return new CageSolver(cage, constraints.ToArray());
        }

        // Spatial accuracy setting, shared across scenes. It never modifies the source point cloud.
        public static ElasticCage Build(MpmModelAsset source, int resolution = 24)
        {
            if (source == null || source.renderData == null || source.dynamicCount <= 0)
                throw new ArgumentException("A complete PhysFlow model is required.");
            var bounds = new Bounds(source.renderData.splats[0].center, Vector3.zero);
            for (int i = 1; i < source.dynamicCount; i++) bounds.Encapsulate(source.renderData.splats[i].center);
            float spacing = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) / resolution;
            var youngs = new float[source.dynamicCount];
            var fixedMask = new bool[source.dynamicCount];
            var attachments = new bool[source.dynamicCount];
            for (int i = 0; i < source.dynamicCount; i++)
            {
                youngs[i] = source.youngs;
                Vector3 p = source.particles[source.binding[i]] + source.offsets[i];
                foreach (var box in source.cuboids)
                {
                    Vector3 d = p - (Vector3)box.point, half = box.size;
                    if (box.point.w <= 0 && box.size.w > 0 && ((Vector3)box.velocity).sqrMagnitude == 0 &&
                        Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y && Mathf.Abs(d.z) <= half.z) fixedMask[i] = true;
                }
                foreach (var axle in source.axles)
                {
                    Vector3 d = p - (Vector3)axle.point, n = ((Vector3)axle.normal).normalized;
                    float along = Vector3.Dot(d, n);
                    if (Mathf.Abs(along) <= axle.normal.w && (d - n * along).sqrMagnitude <= axle.point.w * axle.point.w) attachments[i] = true;
                }
            }
            double volume = 0;
            foreach (float v in source.volume) volume += v;
            float mass = (float)(volume * source.density * Math.Pow(source.metersPerUnit, 3));
            if (mass <= 0 || source.youngs <= 0) throw new InvalidOperationException($"{source.name}: invalid source material/mass.");
            return new ElasticCage(source.renderData.splats, source.dynamicCount, youngs, fixedMask,
                source.metersPerUnit, source.density, source.poisson, spacing, mass, source.youngs,
                minSplats: 1, samplesPerVoxel: 0, solidVolume: true, attachmentSeeds: attachments);
        }
    }
}
