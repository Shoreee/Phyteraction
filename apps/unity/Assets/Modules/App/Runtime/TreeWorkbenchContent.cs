using System;
using PhysicsHands.Dynamics;
using PhysicsHands.GaussianData;
using PhysicsHands.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.App
{
    // An imported mesh uses the same material-coordinate cage and finite-body contact solve as
    // the splats. The geometry creates the cage; there is no authored tree rig or gesture response.
    [DefaultExecutionOrder(300)]
    public sealed class TreeWorkbenchContent : MonoBehaviour, IWorkbenchContent
    {
        public MeshFilter visual;
        public MeshRenderAsset appearance;
        public MonoBehaviour contactSource;
        public ElasticMaterialAsset material;
        public int resolution = 24;
        public bool Available => visual != null && appearance != null && appearance.Available && contactSource is ISimulationContactSource && material != null && material.Valid;
        public bool SupportsRoom => false;
        public bool SupportsPaper => false;
        public string Status => "SPEEDTREE  ·  NON-COMMERCIAL RESEARCH  ·  " + (material != null && material.IsTrained ? "CALIBRATED MATERIAL" : "UNTRAINED MATERIAL PRIOR");
        ElasticCage cage;
        CageSolver solver;
        Mesh mesh, original;
        Vector3[] rest, vertices, normals, deformedNormals;
        Vector4[] tangents, deformedTangents;
        readonly ContactGatherer contacts = new();
        bool dirty;
        public void SetContentActive(bool active) => gameObject.SetActive(active);
        public void SetRoom(bool visible) { }
        public void Replay() { }
        public void ResetContent() { if (solver != null) { solver.Reset(); dirty = true; contacts.Reset(); } }
        void OnEnable()
        {
            if (!Available) return;
            try
            {
                original = appearance.mesh;
                visual.GetComponent<MeshRenderer>().sharedMaterials = appearance.materials;
                rest = original.vertices; normals = original.normals; tangents = original.tangents;
                deformedTangents = new Vector4[tangents.Length];
                vertices = new Vector3[rest.Length]; deformedNormals = new Vector3[rest.Length];
                var points = new Gaussian[rest.Length];
                var material = new float[rest.Length]; var fixedMask = new bool[rest.Length];
                Bounds bounds = original.bounds;
                float spacing = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) / resolution;
                for (int i = 0; i < rest.Length; i++)
                {
                    points[i].center = rest[i]; points[i].color = Vector4.one;
                    material[i] = this.material.youngs; fixedMask[i] = rest[i].y <= bounds.min.y + spacing;
                }
                // Occupied-volume mass uses the declared prior density, not the empty AABB volume.
                // The voxelized shell remains an explicit approximation to physical plant thickness.
                float mass = 0;
                cage = new ElasticCage(points, points.Length, material, fixedMask, this.material.simUnitMeters, this.material.density, this.material.poisson,
                    spacing, mass, this.material.youngs, minSplats: 1, samplesPerVoxel: 0, solidVolume: true);
                solver = new CageSolver(cage);
                mesh = Instantiate(original); mesh.name = "SpeedTree material-coordinate deformation";
                mesh.MarkDynamic(); visual.sharedMesh = mesh;
                dirty = false;
            }
            catch { Release(); throw; }
        }
        void FixedUpdate()
        {
            if (solver == null) return;
            float dt = Time.fixedDeltaTime;
            contacts.Policy = (ISimulationContactSource)contactSource;
            var region = new Bounds(cage.rest[0], Vector3.zero);
            for (int i = 0; i < cage.rest.Length; i++) region.Encapsulate(solver.PositionAt(i));
            region.Expand(2 * cage.spacing);
            contacts.Begin(visual.transform.worldToLocalMatrix, 1, region);
            ((ISimulationContactSource)contactSource).Gather(contacts, dt);
            solver.Step(contacts, visual.transform, dt, 1, 2, .6f, cage.spacing * .025f, 0, Vector3.zero);
            solver.ApplyReactions(contacts, visual.transform, dt);
            dirty |= solver.DeformationChanged;
        }
        void LateUpdate()
        {
            if (!dirty || solver == null) return;
            for (int i = 0; i < rest.Length; i++)
            {
                int v = (int)cage.splatVoxel[i];
                Vector3 f = (rest[i] - cage.voxelOrigin[v]) / cage.spacing;
                Vector3 bounded = new(Mathf.Clamp01(f.x), Mathf.Clamp01(f.y), Mathf.Clamp01(f.z));
                Vector3 p = (f - bounded) * cage.spacing; f = bounded;
                Vector3 dx = Vector3.zero, dy = Vector3.zero, dz = Vector3.zero;
                for (int c = 0; c < 8; c++)
                {
                    float x = (c & 1) != 0 ? f.x : 1 - f.x, y = (c & 2) != 0 ? f.y : 1 - f.y, z = (c & 4) != 0 ? f.z : 1 - f.z;
                    Vector3 node = solver.PositionAt(cage.voxelNodes[v * 8 + c]);
                    p += node * (x * y * z);
                    dx += node * (((c & 1) != 0 ? 1 : -1) * y * z / cage.spacing);
                    dy += node * (((c & 2) != 0 ? 1 : -1) * x * z / cage.spacing);
                    dz += node * (((c & 4) != 0 ? 1 : -1) * x * y / cage.spacing);
                }
                vertices[i] = p;
                Vector3 n = normals[i];
                var transported = Vector3.Cross(dy, dz) * n.x + Vector3.Cross(dz, dx) * n.y + Vector3.Cross(dx, dy) * n.z;
                deformedNormals[i] = transported.sqrMagnitude > 1e-12f ? transported.normalized : n;
                if (tangents.Length == rest.Length)
                {
                    Vector4 source = tangents[i];
                    Vector3 tangent = dx * source.x + dy * source.y + dz * source.z;
                    tangent = Vector3.ProjectOnPlane(tangent, deformedNormals[i]).normalized;
                    deformedTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, source.w);
                }
            }
            mesh.vertices = vertices; mesh.normals = deformedNormals;
            if (deformedTangents.Length == rest.Length) mesh.tangents = deformedTangents;
            mesh.RecalculateBounds(); dirty = false;
        }
        void Release()
        {
            solver?.Dispose(); solver = null; cage = null;
            if (visual != null) visual.sharedMesh = null;
            if (mesh != null) Destroy(mesh);
            mesh = null; rest = vertices = normals = deformedNormals = null; tangents = deformedTangents = null;
        }
        void OnDisable() => Release();
        void OnDestroy() => Release();
    }
}
