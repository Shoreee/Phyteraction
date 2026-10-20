using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.GaussianWorld
{
    // Console button pressed by an index fingertip; it re-arms when the finger leaves, so a resting
    // palm or knuckle cannot switch scenes. The cap dips while touched.
    public sealed class SceneButton : MonoBehaviour
    {
        public enum Action { Scene, Reset, Replay, Backdrop }
        public GaussianSceneSelector selector;
        public Action action;
        public int scene;
        public Transform cap;
        const float Travel = .004f;
        Vector3 rest; int touching; float next;

        void Start() { if (cap != null) rest = cap.localPosition; }
        static bool Fingertip(Collider c)
        {
            var hand = c.GetComponentInParent<HoloHand>(); var tip = hand != null ? hand.IndexTip : null;
            return tip != null && (c.ClosestPoint(tip.position) - tip.position).sqrMagnitude < .015f * .015f;
        }
        void OnTriggerEnter(Collider other)
        {
            if (!Fingertip(other) || Workspace.Dragging) return;
            if (touching++ > 0 || selector == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + .6f;
            if (action == Action.Scene) selector.Select(scene);
            else if (action == Action.Reset) selector.ResetScene();
            else if (action == Action.Replay) selector.Replay();
            else selector.ToggleBackdrop();
        }
        void OnTriggerExit(Collider other) { if (Fingertip(other)) touching = Mathf.Max(0, touching - 1); }
        void OnDisable() => touching = 0;
        void Update()
        {
            if (cap == null) return;
            var target = rest + Vector3.down * (touching > 0 ? Travel / Mathf.Max(1e-4f, cap.lossyScale.y) * cap.localScale.y : 0);
            cap.localPosition = Vector3.Lerp(cap.localPosition, target, 1 - Mathf.Exp(-30 * Time.deltaTime));
        }
    }
}
