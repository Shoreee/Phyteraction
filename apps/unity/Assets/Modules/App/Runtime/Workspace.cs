using System;
using PhysicsHands.Hands;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_VISIONOS || UNITY_EDITOR
using UnityEngine.XR.VisionOS.InputDevices;
#endif
namespace PhysicsHands.App
{
    // Console plus object as one unit, dragged 1:1 with the pinching hand (translation only, no
    // physics). On visionOS the grab is gaze + pinch: the system reports where the eyes were when the
    // pinch began (apps never see live gaze), so the outline follows the head as a hint. Without a
    // spatial pointer (editor) a pinch while the head faces the console grabs it. On device the unit
    // is first placed in front of the head so it works seated or standing.
    [DefaultExecutionOrder(-200)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "Workspace")]
    public sealed class Workspace : MonoBehaviour
    {
        public bool Dragging
        {
            get;
            private set;
        }

        public Collider panel;
        public Camera headCamera;
        public Renderer outline;
        public TextMesh hint;
        public PhysicsHand[] hands = Array.Empty<PhysicsHand>();
        [Tooltip("Object base below the eyes and in front of them on first placement, metres.")]
        public float eyeDrop = .42f, distance = .62f;
        [Tooltip("Head direction within this angle (deg) of the panel counts as looking at it.")]
        public float lookAngle = 6;
        bool placed;
        bool[] wasPinching = Array.Empty<bool>();
        PhysicsHand dragHand;
        Vector3 grabPoint, grabOrigin;
        [Tooltip("Gaze rays within this angle (deg) of the console grab it; eye tracking is accurate to about a degree.")]
        public float gazeTolerance = 3;
        readonly SpatialWorkspaceDrag spatialInput = new();
        void Start()
        {
            wasPinching = new bool[hands.Length];
            Show(false, false);
#if !UNITY_VISIONOS || UNITY_EDITOR
            placed = true;
#endif
        }

        void OnDisable()
        {
            Dragging = false;
        }

        public void PlaceInFrontOfHead()
        {
            var head = headCamera != null ? headCamera.transform : null;
            if (head == null)
                return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            forward.Normalize();
            transform.SetPositionAndRotation(head.position + forward * distance + Vector3.down * eyeDrop, Quaternion.LookRotation(forward));
        }

        bool Looking()
        {
            var head = headCamera != null ? headCamera.transform : null;
            if (head == null || panel == null)
                return false;
            var ray = new Ray(head.position, head.forward);
            if (panel.Raycast(ray, out _, 3))
                return true;
            Vector3 near = panel.ClosestPoint(head.position + head.forward * Vector3.Distance(head.position, panel.bounds.center));
            return Vector3.Angle(head.forward, near - head.position) < lookAngle;
        }

        static Vector3 Pinch(PhysicsHand hand) => (hand.ThumbTip.position + hand.IndexTip.position) * .5f;
        void Show(bool on, bool moving)
        {
            if (outline != null)
                outline.enabled = on;
            if (hint != null)
            {
                hint.gameObject.SetActive(on);
                hint.text = moving ? "MOVING" : "PINCH TO MOVE";
            }
        }

        void Update()
        {
            if (!placed && headCamera != null && headCamera.transform.position.sqrMagnitude > 1e-4f && Time.timeSinceLevelLoad > 1)
            {
                placed = true;
                PlaceInFrontOfHead();
            }

            bool looking = Looking();
            if (spatialInput.Update(panel, headCamera != null ? headCamera.transform : null, transform, gazeTolerance))
            {
                Dragging = spatialInput.Dragging;
                Show(looking || Dragging, Dragging);
                Keys();
                return;
            }

            if (dragHand != null)
            {
                if (!dragHand.GoodTracking || !dragHand.IsIndexPinching())
                    dragHand = null;
                else
                    transform.position = grabOrigin + (Pinch(dragHand) - grabPoint);
            }

            for (int i = 0; i < hands.Length; i++)
            {
                var hand = hands[i];
                bool pinching = hand != null && hand.isActiveAndEnabled && hand.GoodTracking && hand.IsIndexPinching() && hand.ThumbTip != null && hand.IndexTip != null;
                if (pinching && !wasPinching[i] && looking && dragHand == null)
                {
                    dragHand = hand;
                    grabPoint = Pinch(hand);
                    grabOrigin = transform.position;
                }

                wasPinching[i] = pinching;
            }

            Dragging = dragHand != null;
            Show(looking || Dragging, Dragging);
            Keys();
        }

        void Keys()
        {
            var k = Keyboard.current;
            if (k == null)
                return;
            float dy = (k.pageUpKey.isPressed ? 1 : 0) - (k.pageDownKey.isPressed ? 1 : 0);
            if (dy != 0)
                transform.position += Vector3.up * dy * .25f * Time.deltaTime;
            if (k.homeKey.wasPressedThisFrame)
                PlaceInFrontOfHead();
        }
    }
}
