using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using UnityEngine;

namespace PhysicsHands.App
{
    /// <summary>Object-owned approach cues persist until their clockwise exit finishes.</summary>
    public sealed class PalmGripFeedback : MonoBehaviour
    {
        public Camera head;
        public Material material;
        public PhysicsLabContent lab;
        public WorkbenchInteraction interaction;
        GripAnchor[] anchors;
        HandFeedbackCue[] cues;
        void OnEnable()
        {
            if (head == null || material == null || lab == null || interaction == null)
            {
                Debug.LogError($"{name}: palm grip feedback requires camera, material, lab and interaction bindings.", this);
                enabled = false; return;
            }
            anchors = lab.GetComponentsInChildren<GripAnchor>(true);
            cues = new HandFeedbackCue[anchors.Length];
        }
        void LateUpdate()
        {
            if (cues == null) return;
            bool allowed = lab.isActiveAndEnabled && !interaction.workspace.Dragging;
            for (int i = 0; i < anchors.Length; i++)
            {
                GripAnchor anchor = anchors[i];
                bool show = false;
                if (allowed && anchor != null)
                    foreach (var grabber in interaction.grabbers)
                    {
                        if (grabber == null || !grabber.CanPreviewGrip || !anchor.CanPreview(grabber)) continue;
                        GripAnchor closest = null; float distance = float.PositiveInfinity;
                        foreach (var candidate in anchors)
                        {
                            if (candidate == null || !candidate.CanPreview(grabber)) continue;
                            float d = (candidate.transform.position - grabber.Hand.Palm.position).sqrMagnitude;
                            if (d < distance) { closest = candidate; distance = d; }
                        }
                        show |= closest == anchor;
                    }
                if (show && cues[i] == null) cues[i] = HandFeedbackCue.Create(head, material);
                if (cues[i] != null) cues[i].Set(show, anchor != null ? anchor.transform.position : Vector3.zero, Color.white);
            }
        }
        void OnDisable()
        {
            if (cues != null) foreach (var cue in cues) if (cue != null) cue.Release();
            cues = null; anchors = null;
        }
    }
}
