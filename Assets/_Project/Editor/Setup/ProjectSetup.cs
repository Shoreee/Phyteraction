using System.IO;
using PhysicsHands.Settings;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.VisionOS;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

namespace PhysicsHands.Editor.Setup
{
    /// <summary>
    /// Applies the original project's layers, collision matrix, physics/time settings and
    /// creates the settings assets with their original values. Idempotent.
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsFolder = "Assets/_Project/Settings";
        public const string RenderingFolder = "Assets/_Project/Settings/Rendering";
        public const string PhysicsMaterialsFolder = "Assets/_Project/Settings/PhysicsMaterials";

        private static readonly (int index, string name)[] s_Layers =
        {
            (8, "PostProcessing"), (9, "Flesh"), (10, "Grabber"), (11, "Grabbable"), (12, "RightHand"),
            (13, "LeftHand"), (14, "PuppetIgnore"), (15, "Blockable"), (16, "Blocker"), (17, "Hologram"),
            (18, "HologramInteractor"), (19, "Fire"), (20, "Bone"), (21, "Desk"), (22, "RightHandBlocker"),
            (23, "LeftHandBlocker"), (24, "UIOverlay"), (25, "CharacterController"), (26, "PhysicalHologram"),
            (27, "Teeth"),
        };

        private static readonly string[] s_Tags = { "Grabbable", "Prop", "RoomLight", "Wall", "AnimFollow" };

        /// <summary>m_LayerCollisionMatrix of the original DynamicsManager (row i = layers colliding with i).</summary>
        private static readonly uint[] s_CollisionMatrix =
        {
            2016473853, 1879048392, 1879097593, 2147483647, 1913713661, 1879064829, 2147483647, 2147483647,
            1879077336, 2082517721, 1879050440, 1879049416, 2086712281, 2090906585, 1879097853, 1882288861,
            1879081160, 1879441608, 1879441608, 1879572680, 1881178329, 1915794121, 1879052488, 1879056584,
            1879048392, 1881145560, 1879061192, 2013278921, 2147483647, 2147483647, 2147483647, 0,
        };

        [MenuItem("Physics Hands/Setup/Apply Project Settings")]
        public static void Apply()
        {
            ApplyTagsAndLayers();
            ApplyPhysics();
            ApplyTime();
            ApplyPlayer();
            CreateSettingsAssets();
            CreatePhysicsMaterials();
            ApplyRenderPipeline();
            ApplyXR();
            // This flag is hidden in the PolySpatial settings UI. Leave replication
            // available; Play to Device still only connects in RealityKit mode.
            var poly = AssetDatabase.LoadAssetAtPath<Object>("Assets/Resources/PolySpatialSettings.asset");
            if (poly != null)
            {
                var settings = new SerializedObject(poly);
                settings.FindProperty("m_DisablePolySpatial").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[PhysicsHands] Project settings applied.");
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void ApplyTagsAndLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            foreach ((int index, string name) in s_Layers) layers.GetArrayElementAtIndex(index).stringValue = name;

            SerializedProperty tags = tagManager.FindProperty("tags");
            foreach (string tag in s_Tags)
            {
                bool exists = false;
                for (int i = 0; i < tags.arraySize; i++) exists |= tags.GetArrayElementAtIndex(i).stringValue == tag;
                if (exists) continue;
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyPhysics()
        {
            var dynamics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            dynamics.FindProperty("m_Gravity").vector3Value = new Vector3(0f, -9.81f, 0f);
            dynamics.FindProperty("m_BounceThreshold").floatValue = 1f;
            dynamics.FindProperty("m_DefaultMaxDepenetrationVelocity").floatValue = 5f;
            dynamics.FindProperty("m_SleepThreshold").floatValue = 0.001f;
            dynamics.FindProperty("m_DefaultContactOffset").floatValue = 0.005f;
            dynamics.FindProperty("m_DefaultSolverIterations").intValue = 6;
            dynamics.FindProperty("m_DefaultSolverVelocityIterations").intValue = 1;
            dynamics.FindProperty("m_QueriesHitTriggers").boolValue = true;
            SerializedProperty maxAngular = dynamics.FindProperty("m_DefaultMaxAngularSpeed");
            if (maxAngular != null) maxAngular.floatValue = 100f;

            SerializedProperty matrix = dynamics.FindProperty("m_LayerCollisionMatrix");
            matrix.arraySize = 32;
            for (int i = 0; i < 32; i++) matrix.GetArrayElementAtIndex(i).longValue = s_CollisionMatrix[i];
            dynamics.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyTime()
        {
            Time.fixedDeltaTime = 0.01f;
            Time.maximumDeltaTime = 0.1f;
        }

        private static void ApplyPlayer()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "VP";
            PlayerSettings.productName = "VP Physics Hands";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.VisionOS, "com.vp.physicshands");

            var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            SerializedProperty input = player.FindProperty("activeInputHandler");
            if (input != null) input.intValue = 1;
            player.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void CreateSettingsAssets()
        {
            EnsureFolder(SettingsFolder);
            CreateOrLoad<HandSettings>($"{SettingsFolder}/HandSettings.asset");
            CreateOrLoad<HumanoidSettings>($"{SettingsFolder}/HumanoidSettings.asset");
            CreateOrLoad<GameSettings>($"{SettingsFolder}/GameSettings.asset");

            CreatePhysicsSetup("PhysicsSetup_Accurate", PhysicsSetupType.Accurate, 0.01f, 1f, 1f, 2f, 2f, 3f);
            CreatePhysicsSetup("PhysicsSetup_Default", PhysicsSetupType.Default, 0.02f, 1f, 1f, 1f, 1f, 1f);
            CreatePhysicsSetup("PhysicsSetup_Laboratory", PhysicsSetupType.Default, 1f / 90f, 1f, 1f, 1f, 1f, 1f);
            CreatePhysicsSetup("PhysicsSetup_Performant", PhysicsSetupType.Performant, 0.02f, 2f, 1f, 1f, 1f, 2f);
        }

        private static void CreatePhysicsSetup(string name, PhysicsSetupType type, float fixedDeltaTime, float wristSpring,
            float wristDamper, float fingerSpring, float fingerDamper, float solver)
        {
            var setup = CreateOrLoad<PhysicsSetup>($"{SettingsFolder}/{name}.asset");
            setup.physicsSetupType = type;
            setup.fixedDeltaTime = fixedDeltaTime;
            setup.defaultContactOffset = 0.005f;
            setup.humanoidWristSpringMultiplier = wristSpring;
            setup.humanoidWristDamperMultiplier = wristDamper;
            setup.humanoidFingerSpringMultiplier = fingerSpring;
            setup.humanoidFingerDamperMultiplier = fingerDamper;
            setup.solverIterationCountMultiplier = solver;
            EditorUtility.SetDirty(setup);
        }

        public static T LoadSetting<T>(string name) where T : Object => AssetDatabase.LoadAssetAtPath<T>($"{SettingsFolder}/{name}.asset");

        /// <summary>Physics materials used by the original props (values from sharedassets1).</summary>
        private static void CreatePhysicsMaterials()
        {
            EnsureFolder(PhysicsMaterialsFolder);
            CreatePhysicsMaterial("Normal", 0.4f, 0.4f, 0f, PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Average);
            CreatePhysicsMaterial("Plastic", 0.5f, 1f, 0f, PhysicsMaterialCombine.Maximum, PhysicsMaterialCombine.Average);
            CreatePhysicsMaterial("Rubber", 1f, 1f, 0.3f, PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Average);
            CreatePhysicsMaterial("Wood", 0.3f, 0.5f, 0f, PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Average);
            CreatePhysicsMaterial("Metal", 0.1f, 0.2f, 0f, PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Average);
            CreatePhysicsMaterial("Zero Friction", 0f, 0f, 0f, PhysicsMaterialCombine.Minimum, PhysicsMaterialCombine.Average);
            CreatePhysicsMaterial("Button", 5f, 5f, 0f, PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Average);
            CreatePhysicsMaterial("Bouncy Ball", 0.6f, 0.6f, 0.9f, PhysicsMaterialCombine.Average, PhysicsMaterialCombine.Maximum);
        }

        private static void CreatePhysicsMaterial(string name, float dynamicFriction, float staticFriction, float bounciness,
            PhysicsMaterialCombine friction, PhysicsMaterialCombine bounce)
        {
            string path = $"{PhysicsMaterialsFolder}/{name}.physicMaterial";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                material = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(material, path);
            }
            material.dynamicFriction = dynamicFriction;
            material.staticFriction = staticFriction;
            material.bounciness = bounciness;
            material.frictionCombine = friction;
            material.bounceCombine = bounce;
            EditorUtility.SetDirty(material);
        }

        public static PhysicsMaterial LoadPhysicsMaterial(string name) =>
            AssetDatabase.LoadAssetAtPath<PhysicsMaterial>($"{PhysicsMaterialsFolder}/{name}.physicMaterial");

        public static void ConfigureLaboratoryShadows(UniversalRenderPipelineAsset pipeline)
        {
            pipeline.shadowDistance = 8f;
            pipeline.shadowCascadeCount = 2;
            pipeline.cascade2Split = 0.35f;
            pipeline.shadowDepthBias = 0.5f;
            pipeline.shadowNormalBias = 0.15f;
            var so = new SerializedObject(pipeline);
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowQuality").intValue = 1; // Low: XR-friendly filtering.
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        private static void ApplyRenderPipeline()
        {
            EnsureFolder(RenderingFolder);
            string rendererPath = $"{RenderingFolder}/URP_Renderer.asset";
            string pipelinePath = $"{RenderingFolder}/URP_Pipeline.asset";

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = false;
            ConfigureLaboratoryShadows(pipeline);
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>visionOS: Apple visionOS loader, Metal (fully immersive) app mode, hand tracking permission.</summary>
        private static void ApplyXR()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
            {
                EnsureFolder("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            const BuildTargetGroup group = BuildTargetGroup.VisionOS;
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            XRGeneralSettings general = perTarget.SettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(general.AssignedSettings, "UnityEngine.XR.VisionOS.VisionOSLoader", group);

            VisionOSSettings visionOS = VisionOSSettings.GetOrCreateSettings();
            visionOS.appMode = VisionOSSettings.AppMode.Metal;
            visionOS.metalImmersionStyle = VisionOSSettings.ImmersionStyle.Full;
            visionOS.handsTrackingUsageDescription = "Your hands drive the physics hands.";
            visionOS.upperLimbVisibility = VisionOSSettings.Visibility.Hidden;
            visionOS.worldSensingUsageDescription = "Used to place the play space.";
            EditorUtility.SetDirty(visionOS);
            EditorUtility.SetDirty(perTarget);
        }
    }
}
