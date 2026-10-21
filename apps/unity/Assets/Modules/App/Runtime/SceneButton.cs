using System.Collections.Generic;
using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.App
{
    // Console button pressed by an index fingertip; it re-arms when the finger leaves, so a resting
    // palm or knuckle cannot switch scenes. The cap dips while touched.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "SceneButton")]
    public sealed class SceneButton : MonoBehaviour
    {
        public enum Action
        {
            Scene,
            Reset,
            Replay,
            Backdrop,
            Lab
        }

        public WorkbenchContentSelector selector;
        public Workspace workspace;
        public Action action;
        public int scene;
        public Transform cap;
        const float Travel = .004f;
        Vector3 rest;
        Collider sensor;
        void Awake() => sensor = GetComponent<Collider>();
        readonly HashSet<Collider> touching = new();
        float next;
        void Start()
        {
            if (cap != null)
                rest = cap.localPosition;
        }

        public bool Near(PhysicsHand hand)
        {
            foreach (var finger in hand.Fingers)
            {
                if (finger.FingerType != HandFinger.Index || finger.Bones.Length == 0) continue;
                foreach (var collider in finger.Bones[finger.Bones.Length - 1].GetColliders())
                {
                    if (collider == null || !collider.enabled || collider.isTrigger) continue;
                    var onButton = sensor.ClosestPoint(collider.bounds.center);
                    if ((onButton - collider.ClosestPoint(onButton)).sqrMagnitude < .02f * .02f) return true;
                }
            }
            return false;
        }

        static bool Fingertip(Collider collider)
        {
            if (collider.isTrigger) return false;
            var hand = collider.GetComponentInParent<PhysicsHand>();
            var bone = collider.GetComponentInParent<PhysicsFingerBone>();
            if (hand == null || bone == null) return false;
            foreach (var finger in hand.Fingers)
                if (finger.FingerType == HandFinger.Index && finger.Bones.Length > 0)
                    return bone == finger.Bones[finger.Bones.Length - 1];
            return false;
        }

        void OnTriggerEnter(Collider other) => Touch(other);
        void OnTriggerStay(Collider other) => Touch(other);
        void Touch(Collider other)
        {
            if (!Fingertip(other)) return;
            bool first = touching.Count == 0;
            if (!touching.Add(other) || !first) return;
            if (selector == null || workspace != null && workspace.Dragging || Time.unscaledTime < next ||
                (action == Action.Scene && !selector.IsModelAvailable(scene)) ||
                (action == Action.Replay && !selector.CanPaper) || (action == Action.Backdrop && !selector.CanRoom)) return;
            next = Time.unscaledTime + .6f;
            if (action == Action.Scene) selector.Select(scene);
            else if (action == Action.Reset) selector.ResetScene();
            else if (action == Action.Replay) selector.Replay();
            else if (action == Action.Backdrop) selector.ToggleBackdrop();
            else selector.ToggleLab();
        }

        // Exit must remove the collider accepted on entry, even if the hand pose changed.
        void OnTriggerExit(Collider other) => touching.Remove(other);
        void OnDisable() => touching.Clear();
        void Update()
        {
            touching.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            if (cap == null)
                return;
            var target = rest + Vector3.down * (touching.Count > 0 ? Travel / Mathf.Max(1e-4f, cap.lossyScale.y) * cap.localScale.y : 0);
            cap.localPosition = Vector3.Lerp(cap.localPosition, target, 1 - Mathf.Exp(-30 * Time.deltaTime));
        }
    }
}
