using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PhysicsHands.Editor.Setup;
using PhysicsHands.Hands;
using PhysicsHands.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PhysicsHands.Editor.Validation
{
    [InitializeOnLoad]
    public static class LaboratoryShadowRepair
    {
        const string Key = "PhysicsHands.ShadowPreview";
        static LaboratoryShadowRepair()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    new GameObject("Shadow preview regression").AddComponent<ShadowPreviewProbe>();
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key, false);
            };
        }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists("Logs/repair-shadow-rendering")) return;
            File.Delete("Logs/repair-shadow-rendering");
            Repair();
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
        public static void ConfigureHandRenderers(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            foreach (var hand in root.GetComponentsInChildren<HoloHand>(true)) hand.ConfigureShadows();
        }
        [MenuItem("Physics Hands/Setup/Repair Laboratory Shadows")]
        public static void Repair()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/_Project/Settings/Rendering/URP_Pipeline.asset");
            ProjectSetup.ConfigureLaboratoryShadows(pipeline);
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] {"Assets/_Project/Imported/Materials"}))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!material.HasProperty("_Surface") || material.GetFloat("_Surface") < 1) continue;
                material.SetShaderPassEnabled("ShadowCaster", false);
                material.SetFloat("_ReceiveShadows", 0);
                material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                EditorUtility.SetDirty(material);
            }
            const string prefabPath = "Assets/_Project/Prefabs/Hands.prefab";
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try { ConfigureHandRenderers(contents); PrefabUtility.SaveAsPrefabAsset(contents, prefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sandbox.unity");
            foreach (var hand in UnityEngine.Object.FindObjectsByType<HoloHand>()) hand.ConfigureShadows();
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional)
                {
                    light.shadows = LightShadows.Soft;
                    light.shadowBias = .5f;
                    light.shadowNormalBias = .15f;
                    light.shadowNearPlane = .05f;
                    var data = light.GetUniversalAdditionalLightData();
                    data.usePipelineSettings = true;
                    EditorUtility.SetDirty(light);
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
    }
    [DefaultExecutionOrder(500)]
    public class ShadowPreviewProbe : MonoBehaviour
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        float _start;
        HoloHand[] _hands;
        bool _frozen;
        void Start()
        {
            var input = HandInputManager.Instance;
            typeof(HandInputManager).GetField("_handTrackingSystem", Flags).SetValue(input, HandTrackingSystem.Simulated);
            typeof(HandInputManager).GetMethod("SelectSystem", Flags).Invoke(input, new object[] {HandTrackingSystem.Simulated});
            var camera = Camera.main;
            var driver = camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver != null) driver.enabled = false;
            camera.transform.SetLocalPositionAndRotation(new Vector3(0,1.45f,-.1f), Quaternion.Euler(32,0,0));
            _start = Time.unscaledTime;
            _hands = FindObjectsByType<HoloHand>();
        }
        void Update()
        {
            float t = Time.unscaledTime - _start;
            if (t < 2) return;
            if (!_frozen)
            {
                _frozen = true;
                foreach (var hand in _hands) hand.TrackedHand.enabled = false;
            }
            foreach (var hand in _hands)
            {
                float sign = hand.HandType == HandSide.Right ? 1 : -1;
                hand.TrackedHand.Wrist.position = new Vector3(sign*.24f,1.12f,.43f);
            }
            if (t < 4) return;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var skins = _hands[0].transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int physicalCasters = skins.Count(r => r.shadowCastingMode != ShadowCastingMode.Off);
            bool transparentCaster = skins.Any(r => r.shadowCastingMode != ShadowCastingMode.Off &&
                r.sharedMaterials.Any(m => m != null && m.HasProperty("_Surface") && m.GetFloat("_Surface") >= 1));
            bool passed = physicalCasters == 2 && !transparentCaster && pipeline.supportsSoftShadows &&
                pipeline.shadowCascadeCount == 2 && pipeline.shadowDistance <= 8;
            typeof(LaboratoryValidation).GetMethod("CapturePreview", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] {"Logs/shadow-preview.png"});
            File.WriteAllText("Logs/shadow-validation.json", JsonUtility.ToJson(new Result {
                passed=passed,physicalHandCasters=physicalCasters,transparentHandCaster=transparentCaster,
                shadowDistance=pipeline.shadowDistance,cascades=pipeline.shadowCascadeCount,softShadows=pipeline.supportsSoftShadows }, true));
            Debug.Log("[PhysicsHands] Shadow preview " + (passed ? "PASS" : "FAIL"));
            EditorApplication.isPlaying = false; enabled = false;
        }
        [Serializable] class Result
        {
            public bool passed; public int physicalHandCasters; public bool transparentHandCaster;
            public float shadowDistance; public int cascades; public bool softShadows;
        }
    }
}
