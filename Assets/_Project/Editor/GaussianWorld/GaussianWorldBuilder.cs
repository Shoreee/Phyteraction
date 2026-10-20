using System;
using System.Collections.Generic;
using System.IO;
using PhysicsHands.GaussianWorld;
using PhysicsHands.Hands;
using PhysicsHands.Editor.Build;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.VisionOS;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PhysicsHands.Editor.GaussianWorld
{
    // The PhysFlow scenes plus the learned-modal carnation in mixed reality, one at a time, on a
    // pedestal behind a small tilted console; a handle carries both.
    public static class GaussianWorldBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/GaussianWorld.unity";
        static readonly Dictionary<string, string> Titles = new()
        {
            ["carnation"] = "Carnation  ·  PhysFlow foam", ["alocasia"] = "Alocasia  ·  foam", ["hat"] = "Hat  ·  elastic", ["telephone"] = "Phone cord  ·  elastic",
            ["fox"] = "Fox  ·  soft elastic", ["jam"] = "Jam  ·  non-Newtonian", ["kitchen"] = "Bulldozer  ·  bricks", ["plane_960"] = "Propeller  ·  metal", ["sandcastle"] = "Sandcastle  ·  sand",
        };
        static readonly Dictionary<string, string> Short = new()
        {
            ["carnation"] = "FOAM", ["alocasia"] = "PLANT", ["hat"] = "HAT", ["telephone"] = "CORD", ["fox"] = "FOX", ["jam"] = "JAM",
            ["kitchen"] = "DOZER", ["plane_960"] = "PROP", ["sandcastle"] = "SAND",
        };
        static readonly Color Graphite = new(.075f, .08f, .09f), Cap = new(.32f, .35f, .37f), Label = new(.78f, .8f, .8f), Teal = new(.1f, .7f, .68f), Blue = new(.36f, .55f, .95f), Amber = new(1f, .58f, .2f);
        // Mixed reality: the lab room goes; passthrough and the reconstruction's own support remain.
        static readonly string[] Hidden =
        {
            "Studio floor", "Back wall", "Wall rib", "Wall light", "Identity panel", "Experiment mat", "Bench support", "Work surface", "Bench edge", "Front accent",
            "Title", "Subtitle", "Status", "Instructions", "Station", "Desktop help", "Laboratory controls", "GRAVITY button", "RESET button",
        };
        // Workspace origin: base of the active object; the console sits in front of it, toward the viewer.
        static readonly Vector3 WorkspaceHome = new(0, .93f, .62f), ConsoleOffset = new(0, -.02f, -.19f);
        const float ConsoleTilt = 22, Pitch = .053f;
        // Largest pedestal radius in front of the workspace origin; wider pedestals move back so they clear the console.
        const float PedestalFront = .095f;

        [MenuItem("Physics Hands/Gaussian World/Create PhysFlow World Scene")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            GaussianDemoBuilder.ImportFlowerAsset();
            var paths = new List<string>();
            foreach (var s in PhysFlowImporter.Scenes) if (PhysFlowImporter.Available(s)) paths.Add(AssetDatabase.GetAssetPath(PhysFlowImporter.Import(s)));
            AssetDatabase.SaveAssets();
            var scene = GaussianDemoBuilder.OpenCleanSandbox(out var hands);
            // Opening the scene unloads unreferenced assets; reload them by path.
            var flowerAsset = AssetDatabase.LoadAssetAtPath<GaussianAsset>(GaussianDemoBuilder.AssetPath);
            var imported = paths.ConvertAll(AssetDatabase.LoadAssetAtPath<MpmSceneAsset>);
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (Array.IndexOf(Hidden, t.name) >= 0 || (t.GetComponent<TextMesh>() != null && t.name.EndsWith("label"))) t.gameObject.SetActive(false);
            if (Camera.main != null)
            {
                // Alpha 0 is passthrough on device; the colour is the editor backdrop.
                Camera.main.clearFlags = CameraClearFlags.SolidColor; Camera.main.backgroundColor = new Color(.11f, .12f, .13f, 0);
                Camera.main.transform.position = new Vector3(0, 1.35f, 0);
            }

            var solver = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Shaders/GaussianWorld/Mpm.compute");
            var sort = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Shaders/GaussianWorld/GaussianSort.compute");
            var splat = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/GaussianWorld/Gaussian.shader");
            var workspace = new GameObject("Workspace").AddComponent<Workspace>(); workspace.transform.position = WorkspaceHome;
            var pedestal = Material("Pedestal", Graphite, .55f);
            var roots = new List<GameObject>(); var titles = new List<string>(); var shortNames = new List<string>();
            foreach (var asset in imported)
            {
                var go = new GameObject("PhysFlow " + asset.scene); go.transform.SetParent(workspace.transform, false);
                var world = go.AddComponent<MpmWorld>(); world.asset = asset; world.solver = solver; world.sortShader = sort; world.splatShader = splat; world.hands = hands;
                // The imported material may flow or bend, but only the flower has
                // topology damage. Keep the authoring state explicit per scene.
                world.fracture = asset.scene == "carnation";
                if (asset.bricks.Length > 0) go.AddComponent<BrickAssembly>().shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Shaders/GaussianWorld/Bricks.compute");
                go.AddComponent<GaussianMetrics>();
                Place(go.transform, asset.splats, asset.dynamicCount + asset.nearCount, pedestal, asset.bricks.Length > 0 ? .32f : 0);
                go.SetActive(false); roots.Add(go); titles.Add(Titles[asset.scene]); shortNames.Add(Short[asset.scene]);
            }
            var flower = GaussianDemoBuilder.AddFlower(flowerAsset, hands, null, Vector3.zero);
            flower.transform.SetParent(workspace.transform, false); Place(flower.transform, flowerAsset.splats, flowerAsset.splats.Length, pedestal);
            flower.gameObject.SetActive(false); roots.Add(flower.gameObject); titles.Add("Carnation  ·  learned elastic hand interaction"); shortNames.Add("FLOWER");

            var selector = new GameObject("Gaussian scene selector").AddComponent<GaussianSceneSelector>();
            selector.scenes = roots.ToArray(); selector.titles = titles.ToArray(); selector.startScene = roots.Count - 1; selector.idle = Cap; selector.active = Amber;
            var console = new GameObject("Console").transform; console.SetParent(workspace.transform, false);
            console.SetLocalPositionAndRotation(ConsoleOffset, Quaternion.Euler(-ConsoleTilt, 0, 0));
            var capMaterial = Material("Scene button", Cap, .45f);
            var renderers = new List<Renderer>();
            float x0 = -(roots.Count - 1) * Pitch / 2;
            for (int i = 0; i < roots.Count; i++)
                renderers.Add(Button(console, shortNames[i], new Vector3(x0 + i * Pitch, 0, .028f), capMaterial, selector, SceneButton.Action.Scene, i));
            selector.buttons = renderers.ToArray();
            Button(console, "RESET", new Vector3(x0, 0, -.038f), Material("Scene reset", Teal, .45f), selector, SceneButton.Action.Reset, 0);
            Button(console, "PAPER", new Vector3(x0 + Pitch, 0, -.038f), Material("Scene replay", Blue, .45f), selector, SceneButton.Action.Replay, 0);
            selector.backdropButton = Button(console, "ROOM", new Vector3(x0 + 2 * Pitch, 0, -.038f), capMaterial, selector, SceneButton.Action.Backdrop, 0);
            var status = Text(console, "Status", new Vector3(x0 + 3 * Pitch - .02f, .001f, -.03f), 92, TextAnchor.MiddleLeft); status.color = Label;
            selector.status = status;
            Panel(workspace, console, new Vector3(roots.Count * Pitch + .03f, .014f, .15f), hands);
            selector.gameObject.AddComponent<GaussianDemoControls>();
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            Debug.Log($"Gaussian World scene created: {ScenePath}; scenes: {string.Join(", ", shortNames)}");
        }

        // Rests the drawn splats on a dark pedestal at the workspace origin. Robust 2 % height and
        // 90 % radius ignore stray splats.
        static void Place(Transform scene, Gaussian[] splats, int count, Material material, float minimumRadius = 0)
        {
            var heights = new List<float>(count); var xz = new List<Vector2>(count);
            for (int i = 0; i < count; i++) { heights.Add(splats[i].center.y); xz.Add(new Vector2(splats[i].center.x, splats[i].center.z)); }
            heights.Sort(); float bottom = heights[(int)(.02f * (count - 1))];
            var mid = Vector2.zero; foreach (var p in xz) mid += p; mid /= count;
            var radii = xz.ConvertAll(p => (p - mid).magnitude); radii.Sort();
            float radius = Mathf.Max(minimumRadius, Mathf.Clamp(radii[(int)(.9f * (count - 1))] + .015f, .07f, .24f));
            scene.localPosition = new Vector3(-mid.x, -bottom, -mid.y + Mathf.Max(0, radius - PedestalFront));
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder); disc.name = "Pedestal"; disc.layer = PhysicsLayers.Desk;
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            var col = disc.AddComponent<MeshCollider>(); col.convex = true; col.sharedMesh = disc.GetComponent<MeshFilter>().sharedMesh;
            disc.transform.SetParent(scene, false); disc.transform.localPosition = new Vector3(mid.x, bottom - .009f, mid.y);
            disc.transform.localScale = new Vector3(2 * radius, .006f, 2 * radius); disc.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Renderer Button(Transform console, string title, Vector3 position, Material material, GaussianSceneSelector selector, SceneButton.Action action, int scene)
        {
            const float size = .04f;
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cap.name = title + " button";
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.transform.SetParent(console, false); cap.transform.localPosition = position + Vector3.up * .004f; cap.transform.localScale = new Vector3(size, .004f, size);
            cap.GetComponent<Renderer>().sharedMaterial = material;
            var trigger = new GameObject(title + " touch sensor") { layer = PhysicsLayers.Desk };
            trigger.transform.SetParent(console, false); trigger.transform.localPosition = position + Vector3.up * .016f;
            var col = trigger.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(size, .022f, size);
            var body = trigger.AddComponent<Rigidbody>(); body.isKinematic = true;
            var b = trigger.AddComponent<SceneButton>(); b.selector = selector; b.action = action; b.scene = scene; b.cap = cap.transform;
            var label = Text(console, title + " label", position + new Vector3(0, .001f, -.026f), 56, TextAnchor.MiddleCenter); label.text = title; label.color = Label;
            return cap.GetComponent<Renderer>();
        }
        static TextMesh Text(Transform parent, string name, Vector3 position, int size, TextAnchor anchor)
        {
            var label = new GameObject(name).AddComponent<TextMesh>();
            label.transform.SetParent(parent, false); label.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(90, 0, 0));
            label.fontSize = size; label.characterSize = .001f; label.anchor = anchor; label.alignment = TextAlignment.Left;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            return label;
        }
        // Plain console slab; looking at it shows a teal rim and a hint, a pinch then drags the workspace.
        static void Panel(Workspace workspace, Transform console, Vector3 size, HoloHand[] hands)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube); slab.name = "Console slab"; slab.layer = PhysicsLayers.Desk;
            slab.transform.SetParent(console, false); slab.transform.localPosition = new Vector3(0, -size.y / 2, 0); slab.transform.localScale = size;
            slab.GetComponent<Renderer>().sharedMaterial = Material("Console", Graphite, .7f);
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cube); rim.name = "Console outline";
            Object.DestroyImmediate(rim.GetComponent<Collider>());
            rim.transform.SetParent(console, false); rim.transform.localPosition = new Vector3(0, -size.y / 2 - .002f, 0); rim.transform.localScale = size + new Vector3(.012f, -.002f, .012f);
            var glow = UnlitMaterial("Console outline", Teal); rim.GetComponent<Renderer>().sharedMaterial = glow;
            var hint = Text(console, "Move hint", new Vector3(0, .012f, size.z / 2 + .004f), 120, TextAnchor.LowerCenter);
            hint.transform.localRotation = Quaternion.Euler(ConsoleTilt, 0, 0); hint.color = Teal; hint.text = "PINCH TO MOVE";
            workspace.panel = slab.GetComponent<Collider>(); workspace.outline = rim.GetComponent<Renderer>(); workspace.hint = hint; workspace.hands = hands;
        }
        static Material UnlitMaterial(string name, Color color)
        {
            string path = $"Assets/_Project/GaussianWorld/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color); EditorUtility.SetDirty(m); return m;
        }
        static Material Material(string name, Color color, float smoothness = .32f)
        {
            Directory.CreateDirectory("Assets/_Project/GaussianWorld/Materials");
            string path = $"Assets/_Project/GaussianWorld/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", smoothness); EditorUtility.SetDirty(m); return m;
        }

        [MenuItem("Physics Hands/Gaussian World/Export PhysFlow World visionOS")]
        public static void ExportVisionOS()
        {
            VisionOSDeviceBuild.Build();
        }
    }
}
