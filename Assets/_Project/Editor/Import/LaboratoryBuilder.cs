using PhysicsHands.Editor.Setup;
using PhysicsHands.Interaction;
using PhysicsHands.Laboratory;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Editor.Import
{
    public static class LaboratoryBuilder
    {
        private const string Folder = "Assets/_Project/Imported/Materials";
        private static readonly Color Ink = new(0.055f, 0.09f, 0.12f);
        private static readonly Color Teal = new(0.1f, 0.7f, 0.68f);
        private static readonly Color Cream = new(0.82f, 0.87f, 0.86f);
        private static readonly Color Amber = new(1f, 0.58f, 0.2f);

        public static void BuildEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.76f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.4f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.19f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = Ink;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 5f;
            RenderSettings.fogEndDistance = 14f;
            Camera.main.backgroundColor = Ink;
            Camera.main.fieldOfView = 62f;
            var sun = Object.FindAnyObjectByType<Light>();
            sun.shadows = LightShadows.Soft;
            sun.shadowBias = .5f;
            sun.shadowNormalBias = .15f;
            sun.shadowNearPlane = .05f;
            sun.intensity = 1.5f;
            sun.color = new Color(1f, 0.92f, 0.8f);

            Box("Studio floor", new(0,-0.035f,1), new(12,0.07f,12), Material("Floor", new Color(.11f,.16f,.19f)));
            Box("Back wall", new(0,1.7f,3.5f), new(12,3.4f,.1f), Material("Wall", new Color(.075f,.12f,.15f)));
            Box("Bench edge", new(0,.88f,.65f), new(1.65f,.10f,.86f), Material("Frame", Ink));
            Box("Work surface", new(0,.945f,.65f), new(1.6f,.03f,.82f), Material("Surface", Cream));
            Box("Front accent", new(0,.895f,.215f), new(1.42f,.009f,.008f), Material("Accent", Teal));
            foreach (float x in new[] { -.66f, .66f })
                Box("Bench support", new(x,.44f,.66f), new(.07f,.88f,.65f), Material("Frame", Ink));

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * .5f;
                Box("Experiment mat", new(x,.965f,.65f), new(.455f,.008f,.60f), Material("Mat", new Color(.10f,.17f,.19f)));
                Label("Station", $"0{i + 1} / {new[] { "GRASP", "STACK", "ROLL" }[i]}", new(x-.19f,.974f,.90f), .027f, Cream, new Vector3(90,0,0));
            }
            Prop(PrimitiveType.Cube, "Grip block", new(-.62f,1.035f,.56f), new(.085f,.13f,.085f), Teal, "Wood");
            Prop(PrimitiveType.Capsule, "Grip capsule", new(-.41f,1.045f,.64f), new(.065f,.075f,.065f), Amber, "Rubber");
            for (int i = 0; i < 5; i++)
                Prop(PrimitiveType.Cube, $"Stack block {i+1}", new(-.1f + (i%2)*.18f,1.01f+(i/2)*.075f,.61f), Vector3.one*.07f, (i+i/2)%2==0 ? Teal : Cream, "Wood");
            Prop(PrimitiveType.Sphere, "Rubber ball", new(.43f,1.025f,.55f), Vector3.one*.1f, Amber, "Bouncy Ball");
            Prop(PrimitiveType.Sphere, "Steel ball", new(.60f,1.015f,.69f), Vector3.one*.075f, Cream, "Metal");

            // Repeated wall ribs and inset light strips give the room depth without
            // adding dynamic lights or post-processing cost on the headset.
            for (int i = -4; i <= 4; i++)
                Box("Wall rib", new(i*.65f,1.7f,3.41f), new(.025f,3.2f,.05f), Material("Frame", Ink));
            Box("Wall light", new(0,2.7f,3.36f), new(5.2f,.018f,.02f), Material("Accent", Teal));
            Box("Identity panel", new(0,1.54f,1.35f), new(1.65f,.48f,.035f), Material("Frame", Ink));
            Label("Title", "PHYSICS / HAND LAB", new(-.73f,1.68f,1.325f), .075f, Cream);
            Label("Subtitle", "TACTILE EXPERIMENTS    /    VISION PRO", new(-.73f,1.56f,1.324f), .028f, Teal);
            Label("Status", "INITIALIZING", new(-.73f,1.45f,1.323f), .025f, Amber);
            Label("Instructions", "Reach, grasp and release. Build a tower. Test the bounce.", new(-.73f,1.36f,1.322f), .021f, Cream);
            Label("Desktop help", "DESKTOP  /  TAB hand   RMB move   SCROLL depth   G grasp   T pinch\nQ/E roll   R/F pitch   Z/C yaw   X hand reset   BACKSPACE reset all", new(-.76f,.80f,.195f), .021f, Cream);
            MakeButton("RESET", new(-.61f,.985f,.30f), false, Teal);
            MakeButton("GRAVITY", new(.60f,.985f,.30f), true, Amber);
        }

        public static void WireRuntime()
        {
            var controller = new GameObject("Laboratory controls").AddComponent<LaboratoryController>();
            controller.StatusLabel = GameObject.Find("Status").GetComponent<TextMesh>();
            foreach (var button in Object.FindObjectsByType<LaboratoryButton>()) button.Controller = controller;
        }

        private static void MakeButton(string title, Vector3 position, bool gravity, Color color)
        {
            var go = Box(title + " button", position, new(.18f,.026f,.075f), Material(title, color));
            var trigger = new GameObject("Touch sensor");
            trigger.transform.SetPositionAndRotation(position + Vector3.up*.025f, Quaternion.identity);
            var col = trigger.AddComponent<BoxCollider>();
            col.size = new Vector3(.18f,.035f,.075f);
            col.isTrigger = true;
            var body = trigger.AddComponent<Rigidbody>();
            body.isKinematic = true;
            trigger.layer = PhysicsLayers.Desk;
            var button = trigger.AddComponent<LaboratoryButton>();
            button.GravityButton = gravity;
            Label(title + " label", title, position + new Vector3(-.073f,.015f,.018f), .024f, Ink, new Vector3(90,0,0));
        }

        private static GameObject Box(string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;
            go.layer = PhysicsLayers.Desk;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static void Prop(PrimitiveType shape, string name, Vector3 position, Vector3 scale, Color color, string physics)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Material("Prop " + name, color);
            go.GetComponent<Collider>().sharedMaterial = ProjectSetup.LoadPhysicsMaterial(physics);
            var body = go.AddComponent<Rigidbody>();
            body.mass = physics == "Metal" ? .3f : .12f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.solverIterations = 12;
            go.AddComponent<MovableObject>();
            go.AddComponent<Interactable>();
            var holdable = go.AddComponent<Holdable>();
            var so = new SerializedObject(holdable);
            so.FindProperty("_restrictAllowedSnapTypes").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<Telekinable>();
            var respawner = go.AddComponent<Respawner>();
            respawner.ScaleOnRespawn = false;
            respawner.MinHeight = .15f;
            go.AddComponent<LaboratoryProp>();
        }

        private static TextMesh Label(string name, string text, Vector3 position, float size, Color color, Vector3 rotation = default)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 64;
            label.characterSize = size / 6.4f;
            label.color = color;
            label.anchor = TextAnchor.UpperLeft;
            return label;
        }

        private static Material Material(string name, Color color)
        {
            string path = $"{Folder}/Lab_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .32f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
