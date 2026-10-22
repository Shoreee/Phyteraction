using PhysicsHands.GaussianData;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PhysicsHands.Editor.Import
{
    // Released PhysFlow scene (tools/prepare_physflow.py output + configs/<scene>_config.json) -> MpmModelAsset.
    public static class PhysFlowImporter
    {
        public static string DataRoot => RepositoryPaths.Data("processed", "physflow");
        public static string ConfigRoot => RepositoryPaths.Data("raw", "physflow", "configs");
        public const string AssetFolder = ProjectAssets.GaussianRoot + "/PhysFlow";
        public static readonly string[] Scenes = {"carnation", "alocasia", "hat", "telephone", "fox", "jam", "kitchen", "plane_960", "sandcastle"};
        [Serializable]
        class Report
        {
            public string scene, source_ply, source_sha256, output_sha256;
            public int n_grid, cells, splats_near, sh_degree;
            public float refine, grid_lim, meters_per_sim_unit, height;
            public float[] local_from_sim, up_sim, sh_from_local;
        }

        [Serializable]
        class Bc
        {
            public string type;
            public float[] point, size, velocity, force, normal, half_height_and_radius;
            public float start_time, end_time = 1e3f, rotation_scale, translation_scale, start_position, end_position;
            public int num_dt = 1, num_layers;
        }

        [Serializable]
        class Config
        {
            public string material = "jelly";
            public float E, nu, density, yield_stress, plastic_viscosity, friction_angle, xi, grid_v_damping_scale = 1.1f, substep_dt = 1e-4f, frame_dt = .04f;
            public int frame_num = 50;
            public float[] g;
            public Bc[] boundary_conditions;
        }

        [MenuItem("Physics Hands/Gaussian World/Reimport PhysFlow Assets")]
        public static void ReimportAll()
        {
            foreach (string scene in Scenes)
                if (!Available(scene) || !File.Exists($"{ConfigRoot}/{scene}_config.json"))
                    throw new FileNotFoundException($"Missing prepared source for {scene}; run tools/prepare_physflow.py first.");
            foreach (string scene in Scenes)
                Import(scene);
            AssetDatabase.SaveAssets();
        }

        public static bool Available(string scene) => File.Exists($"{DataRoot}/{scene}.bin") && File.Exists($"{DataRoot}/{scene}.json");
        public static MpmModelAsset Import(string scene)
        {
            string reportText = File.ReadAllText($"{DataRoot}/{scene}.json"), configText = File.ReadAllText($"{ConfigRoot}/{scene}_config.json");
            var report = JsonUtility.FromJson<Report>(reportText);
            var config = JsonUtility.FromJson<Config>(configText);
            var a = ScriptableObject.CreateInstance<MpmModelAsset>();
            a.renderData = ScriptableObject.CreateInstance<GaussianRenderAsset>();
            a.name = scene;
            a.scene = scene;
            using (var r = new BinaryReader(File.OpenRead($"{DataRoot}/{scene}.bin")))
            {
                int n = r.ReadInt32(), nd = r.ReadInt32(), np = r.ReadInt32();
                a.renderData.splats = new Gaussian[n];
                for (int i = 0; i < n; i++)
                    a.renderData.splats[i] = new Gaussian{center = V4(r), axis0 = V4(r), axis1 = V4(r), axis2 = V4(r), color = V4(r)};
                a.dynamicCount = nd;
                a.nearCount = report.splats_near;
                a.binding = new int[nd];
                a.offsets = new Vector3[nd];
                for (int i = 0; i < nd; i++)
                    a.binding[i] = (int)r.ReadUInt32();
                for (int i = 0; i < nd; i++)
                    a.offsets[i] = V3(r);
                a.particles = new Vector3[np];
                for (int i = 0; i < np; i++)
                    a.particles[i] = V3(r);
                a.volume = new float[np];
                for (int i = 0; i < np; i++)
                    a.volume[i] = r.ReadSingle();
                var paperCount = new float[np];
                for (int i = 0; i < np; i++)
                    paperCount[i] = r.ReadSingle();
                float paperDx = report.grid_lim / report.n_grid;
                // PhysFlow impulse: dv = force / m_p * substep_dt per substep, m_p = rho * paperDx^3 / count.
                a.impulseGain = new float[np];
                for (int i = 0; i < np; i++)
                    a.impulseGain[i] = config.substep_dt * paperCount[i] / (config.density * paperDx * paperDx * paperDx);
                a.renderData.sh = new Vector4[report.sh_degree >= 1 ? n * 3 : 0];
                for (int i = 0; i < a.renderData.sh.Length; i++)
                    a.renderData.sh[i] = V4(r);
                if (a.renderData.sh.Length > 0)
                {
                    var f = new Matrix4x4();
                    f[3, 3] = 1;
                    for (int i = 0; i < 9; i++)
                        f[i / 3, i % 3] = report.sh_from_local[i];
                    a.renderData.shFromLocal = f;
                }
            }

            var b = new Bounds(a.renderData.splats[0].center, Vector3.zero);
            foreach (var g in a.renderData.splats)
                b.Encapsulate(g.center);
            a.renderData.bounds = b;
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++)
                m[i / 4, i % 4] = report.local_from_sim[i];
            a.localFromSim = m;
            a.metersPerUnit = report.meters_per_sim_unit;
            a.height = report.height;
            a.up = new Vector3(report.up_sim[0], report.up_sim[1], report.up_sim[2]).normalized;
            // The release has no learned fracture field or hand-only material profile.
            int cells = report.cells;
            a.gridN = (cells + 3) / 4 * 4;
            a.dx = report.grid_lim / cells;
            a.substepDt = config.substep_dt;
            // Enabled only for scenes whose full-Gaussian poke/release comparison
            // stayed within the grid-scale tolerance. Fine structures keep source sampling.
            a.allowInteractionQuadrature = scene is "hat"or "fox"or "jam"or "kitchen";
            a.integrationSubdivisions = 2;
            a.material = config.material switch
            {
            "jelly"or "elastic" => MpmMaterial.Elastic, "metal" => MpmMaterial.Metal, "sand" => MpmMaterial.Sand, "foam" => MpmMaterial.Foam, "plasticine" => MpmMaterial.Plasticine, "non_newtonian" => MpmMaterial.NonNewtonian, _ => throw new NotSupportedException($"{scene}: PhysFlow material {config.material}"), }

            ;
            a.youngs = config.E * 1e7f;
            a.poisson = config.nu;
            a.density = config.density;
            a.yieldStress = config.yield_stress * 1e5f;
            a.viscosity = config.plastic_viscosity * 1e4f;
            a.frictionAngle = config.friction_angle;
            a.xi = config.xi;
            // set_parameters_dict: hardening only when the key is present, forced to 1 for metal and plasticine.
            a.hardening = configText.Contains("\"hardening\"") && (a.material == MpmMaterial.Metal || a.material == MpmMaterial.Plasticine || ConfigInt(configText, "hardening") != 0);
            a.renderTotal = a.material is MpmMaterial.Metal or MpmMaterial.Plasticine or MpmMaterial.Foam;
            // Metric gravity: the config's g in simulation units would fall at g * L m/s^2 in the room.
            var gravity = config.g is
            {
            Length: 3
            }

            ? new Vector3(config.g[0], config.g[1], config.g[2]) : Vector3.zero;
            a.gravity = gravity;
            a.dampingRate = config.grid_v_damping_scale < 1 ? -Mathf.Log(config.grid_v_damping_scale) / config.substep_dt : 0;
            a.replaySeconds = config.frame_dt * config.frame_num;
            var cuboids = new List<MpmCuboid>();
            var modifiers = new List<MpmModifier>();
            var axles = new List<MpmAxle>();
            a.holdTime = new float[a.particles.Length];
            for (int i = 0; i < a.holdTime.Length; i++)
                a.holdTime[i] = -1;
            foreach (var bc in config.boundary_conditions ?? Array.Empty<Bc>())
            {
                switch (bc.type)
                {
                    case "bounding_box":
                        break; // always on in Mpm.compute (padding 3)
                    case "cuboid":
                        cuboids.Add(new MpmCuboid{point = V(bc.point, bc.start_time), size = V(bc.size, bc.end_time), velocity = V(bc.velocity, 0)});
                        break;
                    case "particle_impulse":
                        var f = V(bc.force, 0) * bc.num_dt;
                        modifiers.Add(new MpmModifier{point = V(bc.point ?? new[]{1f, 1, 1}, 0), size = V(bc.size ?? new[]{1f, 1, 1}, bc.start_time), vec = new Vector4(f.x, f.y, f.z, 0)});
                        break;
                    case "enforce_particle_translation":
                        modifiers.Add(new MpmModifier{point = V(bc.point, 1), size = V(bc.size, bc.start_time), vec = V(bc.velocity, bc.end_time)});
                        break;
                    case "enforce_particle_velocity_rotation":
                    {
                        Vector3 n = V(bc.normal, 0);
                        n.Normalize();
                        Vector3 h1 = Vector3.one;
                        if (Mathf.Abs(Vector3.Dot(n, h1)) < .01f)
                            h1 = new Vector3(.72f, .37f, -.67f);
                        h1 = (h1 - Vector3.Dot(h1, n) * n).normalized;
                        Vector3 h2 = Vector3.Cross(h1, n);
                        float hh = bc.half_height_and_radius[0], radius = bc.half_height_and_radius[1];
                        modifiers.Add(new MpmModifier{point = V(bc.point, 2), size = new Vector4(hh, radius, 0, bc.start_time), vec = new Vector4(0, 0, 0, bc.end_time), normal = new Vector4(n.x, n.y, n.z, bc.rotation_scale), h1 = new Vector4(h1.x, h1.y, h1.z, bc.translation_scale), h2 = h2, });
                        // Not in PhysFlow: a bearing on the same cylinder so hands can spin it freely outside the script.
                        if (axles.Count == 0)
                            axles.Add(new MpmAxle{point = V(bc.point, radius), normal = new Vector4(n.x, n.y, n.z, hh)});
                        break;
                    }

                    case "release_particles_sequentially":
                    {
                        // Layer i holds |x_axis - end| < portion (L - i) at zero velocity until (i + 1) T / L.
                        int axis = Array.FindIndex(bc.normal, v => v != 0), layers = bc.num_layers;
                        float portion = Mathf.Abs(bc.start_position - bc.end_position) / layers, slot = bc.end_time / layers;
                        for (int i = 0; i < a.particles.Length; i++)
                        {
                            float d = Mathf.Abs(a.particles[i][axis] - bc.end_position);
                            if (d >= portion * layers)
                                continue;
                            int last = Mathf.CeilToInt(layers - d / portion) - 1;
                            a.holdTime[i] = Mathf.Max(a.holdTime[i], slot * (last + 1));
                        }

                        break;
                    }

                    default:
                        throw new NotSupportedException($"{scene}: boundary condition {bc.type}");
                }
            }

            if (modifiers.Count > 32)
                throw new NotSupportedException($"{scene}: more than 32 particle modifiers");
            a.cuboids = cuboids.ToArray();
            a.modifiers = modifiers.ToArray();
            a.axles = axles.ToArray();
            a.sourceSha256 = report.source_sha256;
            a.provenance = $"PhysFlow (CVPR 2025) release / {scene}: {report.source_ply} sha256 {report.source_sha256[..12]}; config {scene}_config.json (GPT-4 initial material, not the refined values); prepare_physflow.py refine={report.refine}";
            a.materialOrigin = "physflow-author-initialization";
            PhysFlowTrainedMaterial.Apply(a, scene, configText, reportText);
            Directory.CreateDirectory(Path.Combine(RepositoryPaths.UnityRoot, AssetFolder));
            string path = $"{AssetFolder}/{scene}.asset";
            a.renderData = GaussianAssetWriter.SaveRenderData(path, a.renderData);
            var existing = AssetDatabase.LoadAssetAtPath<MpmModelAsset>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(a, path);
                return a;
            }

            EditorUtility.CopySerialized(a, existing);
            UnityEngine.Object.DestroyImmediate(a);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static int ConfigInt(string json, string key)
        {
            int i = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (i < 0)
                return 0;
            i = json.IndexOf(':', i) + 1;
            int j = i;
            while (j < json.Length && (char.IsWhiteSpace(json[j]) || char.IsDigit(json[j]) || json[j] == '-'))
                j++;
            return int.TryParse(json[i..j].Trim(), out int v) ? v : 0;
        }

        static Vector4 V4(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static Vector3 V3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static Vector4 V(float[] v, float w) => v == null ? new Vector4(0, 0, 0, w) : new Vector4(v[0], v[1], v[2], w);
    }
}
