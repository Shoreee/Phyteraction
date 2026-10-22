using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Hands
{
    internal sealed class HandPresentation
    {
        readonly SkinnedMeshRenderer skin, bones;
        readonly Renderer ghost, outline;
        readonly Material normal, lost;
        readonly Transform root;
        bool showBones = true;
        public HandPresentation(Transform root, SkinnedMeshRenderer skin, SkinnedMeshRenderer bones, Renderer ghost, Renderer outline, Material normal, Material lost)
        {
            this.root = root;
            this.skin = skin;
            this.bones = bones;
            this.ghost = ghost;
            this.outline = outline;
            this.normal = normal;
            this.lost = lost;
        }

        public void Update(bool warn, bool initialized, bool good)
        {
            bool trackingLost = warn && initialized && !good;
            if (skin != null)
            {
                var material = trackingLost && lost != null ? lost : normal;
                if (material != null && skin.sharedMaterial != material)
                    skin.sharedMaterial = material;
            }

            if (outline != null)
                outline.enabled = false; // Loss uses the selected skin and palm feedback, without a second surface.
            if (ghost != null)
                ghost.enabled = false;
        }

        public void ConfigureShadows()
        {
            // One physical skin and one physical skeleton; never enable reference or ghost skins.
            foreach (var physical in new[] { skin, showBones ? bones : null })
                if (physical != null)
                    for (Transform node = physical.transform; node != null && node != root; node = node.parent)
                        node.gameObject.SetActive(true);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool selected = renderer == skin || showBones && renderer == bones;
                renderer.enabled = selected;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (selected) renderer.updateWhenOffscreen = true;
            }
        }

        public void SetMaterial(Material material)
        {
            if (skin != null)
                skin.sharedMaterial = material;
        }

        public void ShowBones(bool show)
        {
            showBones = show;
            ConfigureShadows();
        }
    }
}
