using System.Collections;
using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>Detached lifetime lets a hand/anchor cue finish its exit after its source is disabled.</summary>
    public sealed class HandFeedbackCue : MonoBehaviour
    {
        readonly RadialCueSequence sequence = new();
        HandFeedbackMesh geometry;
        Camera view;
        TextMesh label;
        Material textMaterial;
        Vector3 centre, target;
        Color color;
        bool arrow, retiring;
        Coroutine exit;
        public bool Empty => sequence.Empty;

        public static HandFeedbackCue Create(Camera view, Material material, TextMesh template = null)
        {
            var owner = new GameObject("Hand feedback cue");
            var cue = owner.AddComponent<HandFeedbackCue>();
            cue.view = view;
            cue.geometry = new HandFeedbackMesh("Hand feedback geometry", material, 340);
            if (template != null)
            {
                var text = new GameObject("Hand feedback label", typeof(TextMesh));
                text.transform.SetParent(owner.transform, false);
                text.layer = PhysicsLayers.UIOverlay;
                cue.label = text.GetComponent<TextMesh>();
                cue.label.font = template.font;
                cue.label.fontSize = template.fontSize;
                cue.label.characterSize = template.characterSize;
                cue.label.anchor = template.anchor;
                cue.label.alignment = template.alignment;
                cue.textMaterial = new Material(material) { name = "Hand feedback font", renderQueue = 4101 };
                cue.textMaterial.SetFloat("_UseFont", 1);
                cue.RefreshFont(cue.label.font);
                cue.label.GetComponent<MeshRenderer>().sharedMaterial = cue.textMaterial;
                Font.textureRebuilt += cue.RefreshFont;
                text.SetActive(false);
                template.gameObject.SetActive(false);
            }
            return cue;
        }

        void RefreshFont(Font font)
        {
            if (label != null && font == label.font && textMaterial != null)
                textMaterial.mainTexture = font.material.mainTexture;
        }

        public void Set(bool visible, Vector3 point, Color tint, string text = null, bool drawArrow = false, Vector3 end = default)
        {
            if (retiring) return;
            // While exiting, retain the last meaningful placement, colour and caption.
            if (visible)
            {
                centre = point; target = end; color = tint;
                if (label != null && label.text != text) label.text = text ?? string.Empty;
            }
            arrow = visible && drawArrow;
            sequence.Request(visible);
        }

        void LateUpdate()
        {
            sequence.Step(Time.unscaledDeltaTime);
            if (view == null || !ApplicationActivity.CanSubmitGpuWork)
            {
                geometry?.Hide();
                if (label != null) label.gameObject.SetActive(false);
                return;
            }
            if (sequence.Empty) { geometry.Hide(); if (label != null) label.gameObject.SetActive(false); return; }
            geometry.Begin(view.transform);
            geometry.Ring(centre, .023f, color, sequence.Ticks);
            float distance = Vector3.Distance(centre, target);
            if (arrow && sequence.Complete && distance > .035f)
            {
                Vector3 direction = (target - centre) / distance;
                float shaft = Mathf.Max(0, distance - .055f);
                Vector3 start = centre + direction * .03f;
                int count = Mathf.Clamp(Mathf.CeilToInt(shaft / .03f), 1, 32);
                for (int i = 0; i < count; i++)
                    geometry.Dash(start + direction * (shaft * i / count), start + direction * (shaft * (i + .45f) / count), color, .003f);
                Vector3 side = Vector3.Cross(direction, view.transform.forward).normalized;
                if (side.sqrMagnitude < .01f) side = view.transform.right;
                Vector3 neck = target - direction * Mathf.Min(.022f, distance * .4f);
                geometry.Quad(target, neck + side * .009f, neck - side * .009f, target, color);
            }
            geometry.Commit(new Bounds((centre + target) * .5f, Vector3.one * (distance + .2f)));
            if (label != null)
            {
                label.gameObject.SetActive(true);
                float alpha = 0; foreach (float tick in sequence.Ticks) alpha = Mathf.Max(alpha, tick);
                var tint = color; tint.a *= alpha; label.color = tint;
                label.transform.SetPositionAndRotation(centre + view.transform.up * .045f, view.transform.rotation);
            }
        }

        public void Release()
        {
            if (retiring) return;
            retiring = true; arrow = false; sequence.Request(false);
            exit = StartCoroutine(FinishExit());
        }

        IEnumerator FinishExit()
        {
            while (!sequence.Empty) yield return null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (exit != null) StopCoroutine(exit);
            Font.textureRebuilt -= RefreshFont;
            geometry?.Dispose(); geometry = null;
            if (textMaterial != null) Destroy(textMaterial);
        }
    }
}
