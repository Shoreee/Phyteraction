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

        public void Update(bool warn, bool initialized, bool good, bool outlines)
        {
            bool trackingLost = warn && initialized && !good;
            if (skin != null)
            {
                var material = trackingLost && lost != null ? lost : normal;
                if (material != null && skin.sharedMaterial != material)
                    skin.sharedMaterial = material;
            }

            if (outline != null)
                outline.enabled = trackingLost && outlines;
            if (ghost != null)
                ghost.enabled = false;
        }

        public void ConfigureShadows()
        {
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var physical = bones != null && bones.enabled ? bones : skin;
            if (physical != null)
            {
                physical.shadowCastingMode = ShadowCastingMode.On;
                physical.receiveShadows = true;
            }
        }

        public void SetMaterial(Material material)
        {
            if (skin != null)
                skin.sharedMaterial = material;
        }

        public void ShowBones(bool show)
        {
            if (bones != null)
                bones.enabled = show;
            ConfigureShadows();
        }
    }
}
