using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using PhysicsHands.Editor.Setup;
using PhysicsHands.Hands;
using PhysicsHands.Dynamics;
using PhysicsHands.Interaction;
using PhysicsHands.Posing;
using PhysicsHands.Settings;
using PhysicsHands.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;

namespace PhysicsHands.Editor.Import
{
    /// <summary>
    /// Rebuilds the hand rig, OpenXR tracking hands and grab-pose library from the
    /// static reverse-engineering export. Level, patient and cosmetic systems are skipped.
    /// </summary>
    public static class HplAssetImporter
    {
        private const string ImportedRoot = "Assets/_Project/Imported";
        private const string PrefabRoot = "Assets/_Project/Prefabs";
        private const string ScenePath = "Assets/_Project/Scenes/Sandbox.unity";

        private static readonly (string name, string parent)[] s_Roots =
        {
            ("Hands", "Humanoid"),
            ("TrackedHandOpenXR_L", "HandInputManager"),
            ("TrackedHandOpenXR_R", "HandInputManager"),
            ("Poses", "PredefinedHandPoseManager"),
        };

        private static readonly Dictionary<string, string> s_Aliases = new()
        {
            ["TrackedHandOpenXR"] = "TrackedHandXR",
        };

        private static readonly Dictionary<string, Type> s_Builtins = new()
        {
            ["Rigidbody"] = typeof(Rigidbody),
            ["ConfigurableJoint"] = typeof(ConfigurableJoint),
            ["FixedJoint"] = typeof(FixedJoint),
            ["HingeJoint"] = typeof(HingeJoint),
            ["BoxCollider"] = typeof(BoxCollider),
            ["SphereCollider"] = typeof(SphereCollider),
            ["CapsuleCollider"] = typeof(CapsuleCollider),
            ["MeshCollider"] = typeof(MeshCollider),
            ["MeshFilter"] = typeof(MeshFilter),
            ["MeshRenderer"] = typeof(MeshRenderer),
            ["SkinnedMeshRenderer"] = typeof(SkinnedMeshRenderer),
            ["LineRenderer"] = typeof(LineRenderer),
            ["AudioSource"] = typeof(AudioSource),
        };

        private static readonly string[] s_ExternalFiles =
        {
            null, "globalgamemanagers.assets", "sharedassets0.assets", "resources.assets", "sharedassets1.assets",
        };

        [MenuItem("Physics Hands/Import/Build Scaffold")]
        public static void BuildScaffold()
        {
            ProjectSetup.Apply();
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string exportRoot = Path.Combine(projectRoot, "ReverseEngineering/output/export");
            string rigPath = Path.Combine(projectRoot, "ReverseEngineering/output/rig-level1-PlayerManager.json");

            var assets = ImportExportedAssets(exportRoot);
            var handSettings = ProjectSetup.LoadSetting<HandSettings>("HandSettings");
            assets["sharedassets1_180816"] = handSettings;

            var scriptTypes = BuildScriptTypes();
            var nodes = JArray.Parse(File.ReadAllText(rigPath));
            var byId = new Dictionary<long, JObject>();
            var children = new Dictionary<long, List<long>>();
            foreach (JObject node in nodes)
            {
                long id = node.Value<long>("id");
                byId[id] = node;
                long parent = node["parent"] == null || node["parent"].Type == JTokenType.Null ? 0 : node["parent"].Value<long>();
                if (!children.TryGetValue(parent, out List<long> list))
                    children[parent] = list = new List<long>();
                list.Add(id);
            }

            ProjectSetup.EnsureFolder(PrefabRoot);
            var skipped = new HashSet<string>();
            var prefabs = new Dictionary<string, GameObject>();
            foreach ((string rootName, string parentName) in s_Roots)
            {
                JObject root = FindChildOf(byId, parentName, rootName);
                string path = $"{PrefabRoot}/{rootName}.prefab";
                GameObject instance = BuildHierarchy(root, byId, children, assets, scriptTypes, skipped);
                prefabs[rootName] = SavePrefab(instance, path);
            }

            WireHandSettings(prefabs["Hands"]);
            BuildSandbox(prefabs);
            Debug.Log("[PhysicsHands] Scaffold built. Skipped scripts: " + string.Join(", ", skipped));
        }

        private static Dictionary<string, Type> BuildScriptTypes()
        {
            var map = new Dictionary<string, Type>();
            foreach (Type type in typeof(HoloHand).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                map.TryAdd(type.Name, type);
            }
            return map;
        }

        private static JObject FindChildOf(Dictionary<long, JObject> byId, string parentName, string childName)
        {
            foreach (JObject node in byId.Values)
            {
                if (node.Value<string>("name") != childName) continue;
                if (node["parent"] == null || node["parent"].Type == JTokenType.Null) continue;
                if (!byId.TryGetValue(node["parent"].Value<long>(), out JObject parent)) continue;
                if (parent.Value<string>("name") == parentName) return node;
            }
            throw new InvalidOperationException($"Could not find {parentName}/{childName} in the rig export.");
        }

        private static GameObject SavePrefab(GameObject instance, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        private static GameObject BuildHierarchy(JObject root, Dictionary<long, JObject> byId, Dictionary<long, List<long>> children,
            Dictionary<string, UnityEngine.Object> assets, Dictionary<string, Type> scriptTypes, HashSet<string> skipped)
        {
            var gos = new Dictionary<long, GameObject>();
            var components = new Dictionary<long, Component>();
            var order = new List<JObject>();
            Collect(root.Value<long>("id"), byId, children, order);

            foreach (JObject node in order)
            {
                var go = new GameObject(node.Value<string>("name"));
                long id = node.Value<long>("id");
                gos[id] = go;
                if (node["parent"] != null && node["parent"].Type != JTokenType.Null &&
                    gos.TryGetValue(node["parent"].Value<long>(), out GameObject parent))
                    go.transform.SetParent(parent.transform, false);

                JObject position = (JObject)node["localPosition"];
                JObject rotation = (JObject)node["localRotation"];
                JObject scale = (JObject)node["localScale"];
                go.transform.localPosition = new Vector3(position.Value<float>("x"), position.Value<float>("y"), position.Value<float>("z"));
                go.transform.localRotation = new Quaternion(rotation.Value<float>("x"), rotation.Value<float>("y"), rotation.Value<float>("z"), rotation.Value<float>("w"));
                go.transform.localScale = new Vector3(scale.Value<float>("x"), scale.Value<float>("y"), scale.Value<float>("z"));
                go.layer = Mathf.Clamp(node.Value<int>("layer"), 0, 31);
                go.SetActive(node.Value<bool>("active"));

                var claimed = new HashSet<Component> { go.transform };
                foreach (JObject component in (JArray)node["components"])
                {
                    Component added = AddComponent(go, component, scriptTypes, skipped, claimed);
                    if (added == null) continue;
                    long pathId = PathId(component);
                    if (pathId != 0) components[pathId] = added;
                }
            }

            var context = new RefContext(gos, components, assets);
            foreach (JObject node in order)
            {
                foreach (JObject component in (JArray)node["components"])
                {
                    long pathId = PathId(component);
                    if (pathId == 0 || !components.TryGetValue(pathId, out Component target)) continue;
                    JObject data = (JObject)(component["fields"] ?? component["data"]);
                    if (data != null) ApplyObject(new SerializedObject(target), data, context);
                    if (target is Behaviour behaviour && component["enabled"] != null)
                        behaviour.enabled = component.Value<int>("enabled") != 0;
                }

                if (IsShapeVisual(node))
                    foreach (Renderer renderer in gos[node.Value<long>("id")].GetComponents<Renderer>())
                        renderer.enabled = false;
            }

            GameObject result = gos[root.Value<long>("id")];
            RepairSkinning(result);
            return result;
        }

        // The source deliberately contains unused bone slots. Unity accepts them, but
        // scene replication does not. Only replace unweighted slots; never invent a
        // weighted bone, which would silently deform the hand.
        private static void RepairSkinning(GameObject root)
        {
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    UnityEngine.Object.DestroyImmediate(renderer);
                    continue;
                }
                Transform[] bones = renderer.bones;
                var used = new HashSet<int>();
                foreach (BoneWeight weight in mesh.boneWeights)
                {
                    if (weight.weight0 > 0f) used.Add(weight.boneIndex0);
                    if (weight.weight1 > 0f) used.Add(weight.boneIndex1);
                    if (weight.weight2 > 0f) used.Add(weight.boneIndex2);
                    if (weight.weight3 > 0f) used.Add(weight.boneIndex3);
                }
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null) continue;
                    if (used.Contains(i))
                        throw new InvalidOperationException($"{renderer.name}: weighted bone {i} is missing.");
                    bones[i] = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                }
                renderer.bones = bones;
            }
        }

        private static readonly HashSet<string> s_ShapeClasses = new() { "Disc", "Line", "Rectangle" };

        /// <summary>
        /// Shapes discs and lines are generated quads. Their shader and color live on the
        /// skipped Shapes component, so the leftover mesh renders as a magenta card.
        /// </summary>
        private static bool IsShapeVisual(JObject node)
        {
            foreach (JObject component in (JArray)node["components"])
            {
                string className = component.Value<string>("class");
                if (string.IsNullOrEmpty(className)) continue;
                int dot = className.LastIndexOf('.');
                if (dot >= 0) className = className[(dot + 1)..];
                if (s_ShapeClasses.Contains(className)) return true;
            }
            return false;
        }

        private static long PathId(JObject component)
        {
            JToken token = component["pathID"];
            return token == null || token.Type == JTokenType.Null ? 0 : token.Value<long>();
        }

        private static void Collect(long id, Dictionary<long, JObject> byId, Dictionary<long, List<long>> children, List<JObject> order)
        {
            order.Add(byId[id]);
            if (!children.TryGetValue(id, out List<long> list)) return;
            foreach (long child in list) Collect(child, byId, children, order);
        }

        private static Component AddComponent(GameObject go, JObject component, Dictionary<string, Type> scriptTypes,
            HashSet<string> skipped, HashSet<Component> claimed)
        {
            Type type = null;
            if ((string)component["type"] == "Script")
            {
                string className = (string)component["class"] ?? "";
                int dot = className.LastIndexOf('.');
                if (dot >= 0) className = className[(dot + 1)..];
                if (s_Aliases.TryGetValue(className, out string alias)) className = alias;
                if (!scriptTypes.TryGetValue(className, out type))
                {
                    skipped.Add(className);
                    return null;
                }
            }
            else if (!s_Builtins.TryGetValue((string)component["type"], out type))
            {
                return null;
            }

            foreach (Component existing in go.GetComponents(type))
            {
                if (claimed.Contains(existing)) continue;
                claimed.Add(existing);
                return existing;
            }

            Component created = go.AddComponent(type);
            claimed.Add(created);
            return created;
        }

        private sealed class RefContext
        {
            public readonly Dictionary<long, GameObject> Gos;
            public readonly Dictionary<long, Component> Components;
            public readonly Dictionary<string, UnityEngine.Object> Assets;
            private readonly Dictionary<string, Type> _typeCache = new();

            public RefContext(Dictionary<long, GameObject> gos, Dictionary<long, Component> components, Dictionary<string, UnityEngine.Object> assets)
            {
                Gos = gos;
                Components = components;
                Assets = assets;
            }

            public Type ExpectedType(SerializedProperty property)
            {
                string raw = property.type;
                int open = raw.IndexOf('<');
                int close = raw.IndexOf('>');
                if (open < 0 || close < 0) return typeof(UnityEngine.Object);
                string name = raw.Substring(open + 1, close - open - 1).TrimStart('$');
                if (_typeCache.TryGetValue(name, out Type cached)) return cached;

                Type found = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type direct = assembly.GetType(name) ?? assembly.GetType("UnityEngine." + name);
                    if (direct != null)
                    {
                        found = direct;
                        break;
                    }
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types; }
                    if (types == null) continue;
                    foreach (Type type in types)
                    {
                        if (type != null && type.Name == name)
                        {
                            found = type;
                            break;
                        }
                    }
                    if (found != null) break;
                }
                found ??= typeof(UnityEngine.Object);
                _typeCache[name] = found;
                return found;
            }
        }

        private static void ApplyObject(SerializedObject serialized, JObject data, RefContext context)
        {
            foreach (JProperty property in data.Properties())
            {
                if (property.Name is "m_GameObject" or "m_CorrespondingSourceObject" or "m_PrefabInstance" or "m_PrefabAsset" or "m_Script")
                    continue;
                SerializedProperty target = serialized.FindProperty(property.Name);
                if (target == null) continue;
                try { ApplyToken(target, property.Value, context); }
                catch (Exception ex) { Debug.LogWarning($"[PhysicsHands] {serialized.targetObject.name}.{property.Name}: {ex.Message}"); }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyToken(SerializedProperty property, JToken token, RefContext context)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference) property.objectReferenceValue = null;
                return;
            }

            if (token is JObject obj && obj.ContainsKey("$ref"))
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                    property.objectReferenceValue = Resolve(obj["$ref"], context.ExpectedType(property), context);
                return;
            }

            if (property.name == "_handedness" && property.propertyType == SerializedPropertyType.Enum)
            {
                int value = token is JObject enumObj ? enumObj.Value<int>("value") : token.Value<int>();
                property.intValue = value switch { 2 => (int)HandSide.Right, 1 => (int)HandSide.Left, _ => value };
                return;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                    property.intValue = ReadInt(token);
                    return;
                case SerializedPropertyType.Boolean:
                    property.boolValue = token.Type == JTokenType.Boolean ? token.Value<bool>() : ReadInt(token) != 0;
                    return;
                case SerializedPropertyType.Float:
                    property.floatValue = token.Value<float>();
                    return;
                case SerializedPropertyType.String:
                    property.stringValue = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
                    return;
                case SerializedPropertyType.Color:
                    property.colorValue = ReadColor(token);
                    return;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = ReadVector2(token);
                    return;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = ReadVector3(token);
                    return;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = ReadVector4(token);
                    return;
                case SerializedPropertyType.Quaternion:
                    Vector4 q = ReadVector4(token);
                    property.quaternionValue = new Quaternion(q.x, q.y, q.z, q.w);
                    return;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = token is JObject refObj && refObj["$ref"] != null
                        ? Resolve(refObj["$ref"], context.ExpectedType(property), context)
                        : null;
                    return;
                case SerializedPropertyType.AnimationCurve:
                    if (token is JObject curve && curve["keys"] is JArray keys) property.animationCurveValue = ReadCurve(keys);
                    return;
                case SerializedPropertyType.ArraySize:
                    return;
            }

            if (property.isArray && token is JArray array)
            {
                property.arraySize = array.Count;
                for (int i = 0; i < array.Count; i++) ApplyToken(property.GetArrayElementAtIndex(i), array[i], context);
                return;
            }

            if (token is JObject generic)
            {
                foreach (JProperty child in generic.Properties())
                {
                    SerializedProperty relative = property.FindPropertyRelative(child.Name);
                    if (relative != null) ApplyToken(relative, child.Value, context);
                }
            }
        }

        private static int ReadInt(JToken token)
        {
            if (token is JObject obj)
            {
                if (obj["value"] != null) return obj["value"].Value<int>();
                if (obj["m_Bits"] != null) return obj["m_Bits"].Value<int>();
            }
            return token.Value<int>();
        }

        private static Vector2 ReadVector2(JToken token)
        {
            if (token is JArray array) return new Vector2(array[0].Value<float>(), array[1].Value<float>());
            var o = (JObject)token;
            return new Vector2(o.Value<float>("x"), o.Value<float>("y"));
        }

        private static Vector3 ReadVector3(JToken token)
        {
            if (token is JArray array) return new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
            var o = (JObject)token;
            return new Vector3(o.Value<float>("x"), o.Value<float>("y"), o.Value<float>("z"));
        }

        private static Vector4 ReadVector4(JToken token)
        {
            if (token is JArray array)
                return new Vector4(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), array.Count > 3 ? array[3].Value<float>() : 0f);
            var o = (JObject)token;
            if (o["w"] != null) return new Vector4(o.Value<float>("x"), o.Value<float>("y"), o.Value<float>("z"), o.Value<float>("w"));
            return new Vector4(o.Value<float>("r"), o.Value<float>("g"), o.Value<float>("b"), o.Value<float>("a"));
        }

        private static Color ReadColor(JToken token)
        {
            if (token is JArray array)
                return new Color(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), array.Count > 3 ? array[3].Value<float>() : 1f);
            var o = (JObject)token;
            return new Color(o.Value<float>("r"), o.Value<float>("g"), o.Value<float>("b"), o["a"] != null ? o.Value<float>("a") : 1f);
        }

        private static AnimationCurve ReadCurve(JArray keys)
        {
            var frames = new Keyframe[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                JObject key = (JObject)keys[i];
                frames[i] = new Keyframe(key.Value<float>("time"), key.Value<float>("value"), key.Value<float>("inSlope"), key.Value<float>("outSlope"));
            }
            return new AnimationCurve(frames);
        }

        private static UnityEngine.Object Resolve(JToken reference, Type expected, RefContext context)
        {
            if (reference == null || reference.Type == JTokenType.Null) return null;
            var r = (JObject)reference;
            UnityEngine.Object resolved = null;

            if (r["external"] != null)
            {
                int file = r.Value<int>("external");
                long pathId = r.Value<long>("pathID");
                if (file > 0 && file < s_ExternalFiles.Length && s_ExternalFiles[file] != null)
                {
                    string key = Path.GetFileNameWithoutExtension(s_ExternalFiles[file]) + "_" + pathId;
                    context.Assets.TryGetValue(key, out resolved);
                }
            }
            else if (r["pathID"] != null && context.Components.TryGetValue(r.Value<long>("pathID"), out Component component))
            {
                resolved = component;
            }
            else if (r["go"] != null && context.Gos.TryGetValue(r.Value<long>("go"), out GameObject go))
            {
                string typeName = r.Value<string>("type") ?? "";
                resolved = typeName.EndsWith("Transform") ? go.transform : go;
            }

            return Adapt(resolved, expected);
        }

        private static UnityEngine.Object Adapt(UnityEngine.Object obj, Type expected)
        {
            if (obj == null || expected == null || expected == typeof(UnityEngine.Object) || expected.IsInstanceOfType(obj)) return obj;
            if (obj is Component component)
            {
                if (expected == typeof(GameObject)) return component.gameObject;
                if (expected == typeof(Transform)) return component.transform;
                if (typeof(Component).IsAssignableFrom(expected)) return component.GetComponent(expected);
            }
            if (obj is GameObject go)
            {
                if (expected == typeof(Transform)) return go.transform;
                if (typeof(Component).IsAssignableFrom(expected)) return go.GetComponent(expected);
            }
            return null;
        }

        private static Dictionary<string, UnityEngine.Object> ImportExportedAssets(string exportRoot)
        {
            ProjectSetup.EnsureFolder($"{ImportedRoot}/Meshes");
            ProjectSetup.EnsureFolder($"{ImportedRoot}/Materials");
            ProjectSetup.EnsureFolder($"{ImportedRoot}/Textures");
            ProjectSetup.EnsureFolder($"{ImportedRoot}/PhysicsMaterials");

            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(exportRoot, "manifest.json")));
            var assets = new Dictionary<string, UnityEngine.Object>();
            var textures = new List<(string key, JObject entry)>();
            var meshes = new List<(string key, JObject entry)>();
            var materials = new List<(string key, JObject entry)>();
            var physics = new List<(string key, JObject entry)>();

            foreach (JProperty entry in manifest.Properties())
            {
                var value = (JObject)entry.Value;
                if (value["error"] != null) continue;
                switch (value.Value<string>("type"))
                {
                    case "Texture2D": textures.Add((entry.Name, value)); break;
                    case "Mesh": meshes.Add((entry.Name, value)); break;
                    case "Material": materials.Add((entry.Name, value)); break;
                    case "PhysicsMaterial": physics.Add((entry.Name, value)); break;
                }
            }

            foreach ((string key, JObject entry) in textures)
            {
                string source = Path.Combine(exportRoot, entry.Value<string>("file"));
                string dest = $"{ImportedRoot}/Textures/{key}.png";
                File.Copy(source, Path.Combine(Path.GetDirectoryName(Application.dataPath), dest), true);
                AssetDatabase.ImportAsset(dest);
                assets[key] = AssetDatabase.LoadAssetAtPath<Texture2D>(dest);
            }

            foreach ((string key, JObject entry) in meshes)
            {
                string jsonPath = Path.Combine(exportRoot, entry.Value<string>("file"));
                Mesh mesh = BuildMesh(JObject.Parse(File.ReadAllText(jsonPath)));
                assets[key] = CreateOrReplace($"{ImportedRoot}/Meshes/{key}.asset", mesh);
            }

            foreach ((string key, JObject entry) in physics)
            {
                JObject data = (JObject)entry["data"];
                var material = new PhysicsMaterial(entry.Value<string>("name"))
                {
                    dynamicFriction = data.Value<float>("m_DynamicFriction"),
                    staticFriction = data.Value<float>("m_StaticFriction"),
                    bounciness = data.Value<float>("m_Bounciness"),
                    frictionCombine = (PhysicsMaterialCombine)data.Value<int>("m_FrictionCombine"),
                    bounceCombine = (PhysicsMaterialCombine)data.Value<int>("m_BounceCombine"),
                };
                assets[key] = CreateOrReplace($"{ImportedRoot}/PhysicsMaterials/{key}.physicMaterial", material);
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            foreach ((string key, JObject entry) in materials)
            {
                var material = new Material(shader) { name = entry.Value<string>("name") };
                JObject colors = (JObject)entry["colors"];
                JToken color = colors?["_BaseColor"] ?? colors?["_Color"];
                if (color != null) material.SetColor("_BaseColor", ReadColor(color));
                JObject floats = (JObject)entry["floats"];
                if (floats?["_Smoothness"] != null) material.SetFloat("_Smoothness", floats.Value<float>("_Smoothness"));
                if (floats?["_Metallic"] != null) material.SetFloat("_Metallic", floats.Value<float>("_Metallic"));
                string sourceShader = entry.Value<string>("shader") ?? "";
                bool shapes = sourceShader.StartsWith("Shapes/");
                bool transparent = !shapes && floats?["_Surface"] != null && floats.Value<float>("_Surface") >= 1f;
                if (transparent)
                {
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_ZWrite", floats?["_ZWrite"] != null ? floats.Value<float>("_ZWrite") : 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = 3000;
                }
                if (shapes)
                {
                    material.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = 3000;
                }
                if (transparent || shapes)
                {
                    material.SetShaderPassEnabled("ShadowCaster", false);
                    material.SetFloat("_ReceiveShadows", 0f);
                    material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                }
                JObject texEnvs = (JObject)entry["textures"];
                JToken main = texEnvs?["_BaseMap"] ?? texEnvs?["_MainTex"];
                if (main?["texture"] != null && assets.TryGetValue(main.Value<string>("texture"), out UnityEngine.Object texture))
                    material.SetTexture("_BaseMap", texture as Texture);
                assets[key] = CreateOrReplace($"{ImportedRoot}/Materials/{key}.mat", material);
            }

            AssetDatabase.SaveAssets();
            return assets;
        }

        private static Mesh BuildMesh(JObject data)
        {
            Vector3[] vertices = ReadVector3Array(data["vertices"] as JArray);
            var mesh = new Mesh { name = data.Value<string>("name") ?? "mesh" };
            if (vertices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;

            Vector3[] normals = ReadVector3Array(data["normals"] as JArray);
            if (normals.Length == vertices.Length) mesh.normals = normals;
            Vector4[] tangents = ReadVector4Array(data["tangents"] as JArray);
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            Vector2[] uv = ReadVector2Array(data["uv0"] as JArray);
            if (uv.Length == vertices.Length) mesh.uv = uv;

            var binds = data["bindposes"] as JArray;
            var weights = data["boneWeights"] as JArray;
            var indices = data["boneIndices"] as JArray;
            if (binds != null && binds.Count > 0 && weights != null && indices != null && weights.Count == vertices.Length)
            {
                var boneWeights = new BoneWeight[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    JArray w = (JArray)weights[i];
                    JArray b = (JArray)indices[i];
                    boneWeights[i] = new BoneWeight
                    {
                        weight0 = At(w, 0), weight1 = At(w, 1), weight2 = At(w, 2), weight3 = At(w, 3),
                        boneIndex0 = (int)At(b, 0), boneIndex1 = (int)At(b, 1), boneIndex2 = (int)At(b, 2), boneIndex3 = (int)At(b, 3),
                    };
                }
                mesh.boneWeights = boneWeights;
                var poses = new Matrix4x4[binds.Count];
                for (int i = 0; i < binds.Count; i++)
                {
                    JArray e = (JArray)binds[i];
                    var m = new Matrix4x4();
                    m.m00 = e[0].Value<float>(); m.m01 = e[1].Value<float>(); m.m02 = e[2].Value<float>(); m.m03 = e[3].Value<float>();
                    m.m10 = e[4].Value<float>(); m.m11 = e[5].Value<float>(); m.m12 = e[6].Value<float>(); m.m13 = e[7].Value<float>();
                    m.m20 = e[8].Value<float>(); m.m21 = e[9].Value<float>(); m.m22 = e[10].Value<float>(); m.m23 = e[11].Value<float>();
                    m.m30 = e[12].Value<float>(); m.m31 = e[13].Value<float>(); m.m32 = e[14].Value<float>(); m.m33 = e[15].Value<float>();
                    poses[i] = m;
                }
                mesh.bindposes = poses;
            }

            var submeshes = (JArray)data["submeshes"];
            mesh.subMeshCount = Mathf.Max(1, submeshes?.Count ?? 1);
            if (submeshes != null)
            {
                for (int s = 0; s < submeshes.Count; s++)
                {
                    JArray tris = (JArray)submeshes[s];
                    var idx = new int[tris.Count * 3];
                    for (int t = 0; t < tris.Count; t++)
                    {
                        JArray tri = (JArray)tris[t];
                        idx[t * 3] = tri[0].Value<int>();
                        idx[t * 3 + 1] = tri[1].Value<int>();
                        idx[t * 3 + 2] = tri[2].Value<int>();
                    }
                    mesh.SetTriangles(idx, s);
                }
            }
            if (normals.Length != vertices.Length) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float At(JArray array, int index) =>
            array != null && index < array.Count ? array[index].Value<float>() : 0f;

        private static Vector2[] ReadVector2Array(JArray array)
        {
            if (array == null) return Array.Empty<Vector2>();
            var result = new Vector2[array.Count];
            for (int i = 0; i < array.Count; i++) result[i] = ReadVector2(array[i]);
            return result;
        }

        private static Vector3[] ReadVector3Array(JArray array)
        {
            if (array == null) return Array.Empty<Vector3>();
            var result = new Vector3[array.Count];
            for (int i = 0; i < array.Count; i++) result[i] = ReadVector3(array[i]);
            return result;
        }

        private static Vector4[] ReadVector4Array(JArray array)
        {
            if (array == null) return Array.Empty<Vector4>();
            var result = new Vector4[array.Count];
            for (int i = 0; i < array.Count; i++) result[i] = ReadVector4(array[i]);
            return result;
        }

        private static T CreateOrReplace<T>(string path, T asset) where T : UnityEngine.Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                UnityEngine.Object.DestroyImmediate(asset);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void WireHandSettings(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            HandSettings hands = ProjectSetup.LoadSetting<HandSettings>("HandSettings");
            HumanoidSettings humanoid = ProjectSetup.LoadSetting<HumanoidSettings>("HumanoidSettings");
            PhysicsSetup physics = ProjectSetup.LoadSetting<PhysicsSetup>("PhysicsSetup_Laboratory");
            GameSettings game = ProjectSetup.LoadSetting<GameSettings>("GameSettings");
            foreach (HoloHand hand in contents.GetComponentsInChildren<HoloHand>(true))
            {
                var so = new SerializedObject(hand);
                // The source wrist targets lived under Humanoid/References, outside
                // the exported Hands subtree. Rebuild this dependency explicitly.
                var anchor = new GameObject("Tracked wrist anchor").transform;
                anchor.SetParent(hand.transform, false);
                so.FindProperty("_handAnchor").objectReferenceValue = anchor;
                foreach (KinematicRigidbody target in hand.GetComponentsInChildren<KinematicRigidbody>(true))
                    if (target.name == "TargetBody") target.MapToTransform = anchor;
                foreach (MapTransform target in hand.GetComponentsInChildren<MapTransform>(true))
                    if (target.name == "TargetRotation") target.SetReference(anchor);

                // The second wrist joint belongs to the omitted arm. A null connected
                // body pins that joint to the world instead of removing the arm link.
                foreach (JointEnforcer enforcer in hand.GetComponentsInChildren<JointEnforcer>(true))
                {
                    var enforcerSo = new SerializedObject(enforcer);
                    var joint = enforcerSo.FindProperty("_joint").objectReferenceValue as ConfigurableJoint;
                    if (joint != null && joint.connectedBody == null && !enforcerSo.FindProperty("_enforce").boolValue)
                    {
                        UnityEngine.Object.DestroyImmediate(enforcer);
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }
                foreach (LineRenderer line in hand.GetComponentsInChildren<LineRenderer>(true)) line.enabled = false;
                so.FindProperty("HandSettings").objectReferenceValue = hands;
                so.FindProperty("_humanoidSettings").objectReferenceValue = humanoid;
                so.FindProperty("_physicsSetup").objectReferenceValue = physics;
                so.FindProperty("_gameSettings").objectReferenceValue = game;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            PhysicsHands.Editor.Validation.LaboratoryShadowRepair.ConfigureHandRenderers(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        private static void BuildSandbox(Dictionary<string, GameObject> prefabs)
        {
            ProjectSetup.EnsureFolder("Assets/_Project/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            camera.nearClipPlane = 0.01f;
            cameraGo.transform.SetLocalPositionAndRotation(new Vector3(0f, 1.35f, 0f), Quaternion.Euler(14f, 0f, 0f));
            var poseDriver = cameraGo.AddComponent<TrackedPoseDriver>();
            poseDriver.positionInput = new InputActionProperty(new InputAction("Head position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            poseDriver.rotationInput = new InputActionProperty(new InputAction("Head rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            poseDriver.trackingStateInput = new InputActionProperty(new InputAction("Head tracking", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            LaboratoryBuilder.BuildEnvironment();

            var rigGo = new GameObject("HoloRig");
            HoloRig rig = rigGo.AddComponent<HoloRig>();
            GameObject hands = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Hands"], scene);
            hands.transform.SetParent(rigGo.transform, false);
            HoloHand left = null, right = null;
            foreach (HoloHand hand in hands.GetComponentsInChildren<HoloHand>(true))
            {
                if (hand.HandType == HandSide.Left) left = hand;
                else right = hand;
            }

            var rigSo = new SerializedObject(rig);
            rigSo.FindProperty("_physicsSetup").objectReferenceValue = ProjectSetup.LoadSetting<PhysicsSetup>("PhysicsSetup_Laboratory");
            rigSo.FindProperty("_gameSettings").objectReferenceValue = ProjectSetup.LoadSetting<GameSettings>("GameSettings");
            rigSo.FindProperty("_leftHand").objectReferenceValue = left;
            rigSo.FindProperty("_rightHand").objectReferenceValue = right;
            rigSo.ApplyModifiedPropertiesWithoutUndo();

            var origin = new GameObject("XR Origin").AddComponent<XROrigin>();
            // visionOS reports floor-relative head and hand poses and keeps the
            // camera offset at y = 0. The standing position lives on the origin,
            // so an untracked editor camera at local eye height matches the headset.
            origin.transform.position = new Vector3(0f, 0f, 0f);
            var tracking = new GameObject("Tracking");
            tracking.transform.SetParent(origin.transform, false);
            cameraGo.transform.SetParent(tracking.transform, false);
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = tracking;
            origin.CameraYOffset = 0f;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            GameObject xrLeft = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["TrackedHandOpenXR_L"], scene);
            GameObject xrRight = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["TrackedHandOpenXR_R"], scene);
            xrLeft.transform.SetParent(tracking.transform, false);
            xrRight.transform.SetParent(tracking.transform, false);
            BindTrackingSpace(xrLeft, tracking.transform);
            BindTrackingSpace(xrRight, tracking.transform);
            TrackedHand simLeft = CreateSimulated(xrLeft, HandSide.Left, cameraGo.transform, tracking.transform);
            TrackedHand simRight = CreateSimulated(xrRight, HandSide.Right, cameraGo.transform, tracking.transform);

            HandInputManager input = tracking.AddComponent<HandInputManager>();
            var inputSo = new SerializedObject(input);
            inputSo.FindProperty("_xrHandLeft").objectReferenceValue = xrLeft.GetComponent<TrackedHandXR>();
            inputSo.FindProperty("_xrHandRight").objectReferenceValue = xrRight.GetComponent<TrackedHandXR>();
            inputSo.FindProperty("_simulatedHandLeft").objectReferenceValue = simLeft;
            inputSo.FindProperty("_simulatedHandRight").objectReferenceValue = simRight;
            inputSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject poses = (GameObject)PrefabUtility.InstantiatePrefab(prefabs["Poses"], scene);
            var libraryGo = new GameObject("PoseLibrary");
            PredefinedHandPoseManager library = libraryGo.AddComponent<PredefinedHandPoseManager>();
            library.DefaultHandPose = FindPose(poses, "HandPoserManagerDefault");
            library.OpenHandPose = FindPose(poses, "HandPoserManagerOpen");
            library.FlatHandPose = FindPose(poses, "HandPoserManagerFlat");
            library.PointingHandPose = FindPose(poses, "HandPoserManagerPointing");
            library.GraspHandPoseMiddle = FindPose(poses, "HandPoserManagerGraspMiddle");
            library.GraspHandPoseFull = FindPose(poses, "HandPoserManagerGraspFull");
            library.PinchHandPose = FindPose(poses, "HandPoserManagerPinch");

            int rigidbodies = hands.GetComponentsInChildren<Rigidbody>(true).Length;
            int joints = hands.GetComponentsInChildren<ConfigurableJoint>(true).Length;
            if (rigidbodies < 20 || joints < 20)
                throw new InvalidOperationException($"Hand rig is incomplete: {rigidbodies} rigidbodies, {joints} configurable joints.");
            if (left == null || right == null)
                throw new InvalidOperationException("Sandbox is missing a left or right HoloHand.");

            LaboratoryBuilder.WireRuntime();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!buildScenes.Exists(s => s.path == ScenePath))
                buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[PhysicsHands] Sandbox saved. Hands have {rigidbodies} rigidbodies and {joints} configurable joints. Editor: Tab switches hands, hold RMB to move, G grasps, T pinches.");
        }

        private static void BindTrackingSpace(GameObject hand, Transform trackingSpace)
        {
            var so = new SerializedObject(hand.GetComponent<TrackedHandXR>());
            so.FindProperty("_trackingSpace").objectReferenceValue = trackingSpace;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static HandPoserManager FindPose(GameObject root, string name)
        {
            foreach (HandPoserManager manager in root.GetComponentsInChildren<HandPoserManager>(true))
                if (manager.gameObject.name == name) return manager;
            return null;
        }

        private static TrackedHand CreateSimulated(GameObject xrRoot, HandSide side, Transform head, Transform parent)
        {
            HandPoser source = xrRoot.GetComponentInChildren<HandPoser>(true);
            var root = new GameObject(side == HandSide.Right ? "TrackedHandSimulated_R" : "TrackedHandSimulated_L");
            root.transform.SetParent(parent, false);
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
            clone.name = "ReferencePoser";
            foreach (XRHandToOVRHandMapper mapper in clone.GetComponents<XRHandToOVRHandMapper>())
                UnityEngine.Object.DestroyImmediate(mapper);
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                UnityEngine.Object.DestroyImmediate(renderer);
            foreach (Animator animator in clone.GetComponents<Animator>())
                UnityEngine.Object.DestroyImmediate(animator);

            TrackedHandSimulated simulated = root.AddComponent<TrackedHandSimulated>();
            var so = new SerializedObject(simulated);
            so.FindProperty("_handedness").intValue = (int)side;
            so.FindProperty("_referenceHandPoser").objectReferenceValue = clone.GetComponent<HandPoser>();
            so.FindProperty("_head").objectReferenceValue = head;
            so.ApplyModifiedPropertiesWithoutUndo();
            return simulated;
        }

        private static void CreateStaticBox(string name, Vector3 position, Vector3 scale, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = PropMaterial(name, new Color(0.45f, 0.46f, 0.48f));
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private static void CreateHoldable(PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = 0;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = PropMaterial(name, color);
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = 0.15f;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            go.AddComponent<Holdable>();
            go.AddComponent<Interactable>();
            go.AddComponent<MovableObject>();
        }

        private static Material PropMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            return CreateOrReplace($"{ImportedRoot}/Materials/Prop_{name}.mat", material);
        }
    }
}
