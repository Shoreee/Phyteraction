using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>Passive hand-offset feedback. Its independent cue owns the exit animation.</summary>
    public sealed class HandOffsetFeedback : MonoBehaviour
    {
        public PhysicsHand hand;
        public Camera head;
        public Material material;
        public TextMesh label;
        readonly HandOffsetState state = new();
        HandFeedbackCue cue;
        Vector3 lastTarget;
        bool hasTarget;
        int captionCentimetres = -1;
        bool captionLost;
        string caption;
        void OnEnable()
        {
            if (hand == null || head == null || material == null || label == null)
            {
                Debug.LogError($"{name}: hand offset feedback requires hand, head, material and label bindings.", this);
                enabled = false; return;
            }
            cue = HandFeedbackCue.Create(head, material, label);
            state.Reset(); hasTarget = false;
        }
        void LateUpdate()
        {
            if (cue == null || hand.Palm == null) return;
            var source = hand.TrackedHand;
            bool tracked = source != null && source.GoodTracking && source.Palm != null;
            if (tracked) { lastTarget = source.Palm.position; hasTarget = true; }
            Vector3 start = hand.Palm.position;
            float distance = hasTarget ? Vector3.Distance(start, lastTarget) : 0;
            state.Step(tracked, distance, Time.unscaledTime);
            bool show = state.TrackingLost || state.Level > 0;
            Color color = state.TrackingLost || state.Level == 3 ? Color.red : state.Level == 2 ? Color.yellow : Color.white;
            int centimetres = Mathf.RoundToInt(distance * 100);
            if (caption == null || captionCentimetres != centimetres || captionLost != state.TrackingLost)
            {
                captionCentimetres = centimetres; captionLost = state.TrackingLost;
                caption = state.TrackingLost ? "BAD TRACKING" : $"HAND OFFSET  {centimetres} cm";
            }
            cue.Set(show, start, color, caption, tracked && hasTarget, lastTarget);
        }
        void OnDisable() { if (cue != null) cue.Release(); cue = null; state.Reset(); }
    }
}
