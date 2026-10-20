using System;
using PhysicsHands.Hands;
using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem;
#if UNITY_VISIONOS || UNITY_EDITOR
using UnityEngine.XR.VisionOS.InputDevices;
#endif

namespace PhysicsHands.GaussianWorld
{
    // Console plus object as one unit, dragged 1:1 with the pinching hand (translation only, no
    // physics). On visionOS the grab is gaze + pinch: the system reports where the eyes were when the
    // pinch began (apps never see live gaze), so the outline follows the head as a hint. Without a
    // spatial pointer (editor) a pinch while the head faces the console grabs it. On device the unit
    // is first placed in front of the head so it works seated or standing.
    [DefaultExecutionOrder(-200)]
    public sealed class Workspace : MonoBehaviour
    {
        public static bool Dragging { get; private set; }
        public Collider panel;
        public Renderer outline;
        public TextMesh hint;
        public HoloHand[] hands = Array.Empty<HoloHand>();
        [Tooltip("Object base below the eyes and in front of them on first placement, metres.")]
        public float eyeDrop = .42f, distance = .62f;
        [Tooltip("Head direction within this angle (deg) of the panel counts as looking at it.")]
        public float lookAngle = 6;
        bool placed;
        bool[] wasPinching = Array.Empty<bool>();
        HoloHand dragHand; Vector3 grabPoint, grabOrigin;
        [Tooltip("Gaze rays within this angle (deg) of the console grab it; eye tracking is accurate to about a degree.")]
        public float gazeTolerance = 3;
        int gazeId = -1, lastGazeId = -1;

        void Start()
        {
            if (hands.Length == 0) hands = FindObjectsByType<HoloHand>(FindObjectsSortMode.None);
            wasPinching = new bool[hands.Length];
            Show(false, false);
#if !UNITY_VISIONOS || UNITY_EDITOR
            placed = true;
#endif
        }
        void OnDisable() { Dragging = false; SetHoverQuads(Array.Empty<float>(), 0); }
#if UNITY_VISIONOS && !UNITY_EDITOR
        // Assets/Plugins/visionOS/GazeHover.mm: the system's gaze highlight on the console's top face.
        [DllImport("__Internal", EntryPoint = "GazeHover_SetQuads")] static extern void SetHoverQuads(float[] corners, int count);
#else
        static void SetHoverQuads(float[] corners, int count) { }
#endif
        readonly float[] hoverQuad = new float[12];
        void LateUpdate()
        {
            if (panel is not BoxCollider box) return;
            var session = Camera.main != null ? Camera.main.transform.parent : null;
            Vector3 h = box.size * .5f;
            for (int i = 0; i < 4; i++)
            {
                var local = box.center + new Vector3(i == 0 || i == 3 ? -h.x : h.x, h.y, i < 2 ? -h.z : h.z);
                Vector3 p = box.transform.TransformPoint(local); if (session != null) p = session.InverseTransformPoint(p);
                hoverQuad[i * 3] = p.x; hoverQuad[i * 3 + 1] = p.y; hoverQuad[i * 3 + 2] = p.z;
            }
            SetHoverQuads(hoverQuad, 1);
        }

        public void PlaceInFrontOfHead()
        {
            var head = Camera.main != null ? Camera.main.transform : null; if (head == null) return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            transform.SetPositionAndRotation(head.position + forward * distance + Vector3.down * eyeDrop, Quaternion.LookRotation(forward));
        }

        bool Looking()
        {
            var head = Camera.main != null ? Camera.main.transform : null; if (head == null || panel == null) return false;
            var ray = new Ray(head.position, head.forward);
            if (panel.Raycast(ray, out _, 3)) return true;
            Vector3 near = panel.ClosestPoint(head.position + head.forward * Vector3.Distance(head.position, panel.bounds.center));
            return Vector3.Angle(head.forward, near - head.position) < lookAngle;
        }
        static Vector3 Pinch(HoloHand hand) => (hand.ThumbTip.position + hand.IndexTip.position) * .5f;
        void Show(bool on, bool moving)
        {
            if (outline != null) outline.enabled = on;
            if (hint != null) { hint.gameObject.SetActive(on); hint.text = moving ? "MOVING" : "PINCH TO MOVE"; }
        }

        bool Hits(Ray ray)
        {
            if (panel.Raycast(ray, out _, 5)) return true;
            Vector3 near = panel.ClosestPoint(ray.origin + ray.direction * Vector3.Distance(ray.origin, panel.bounds.center));
            return Vector3.Angle(ray.direction, near - ray.origin) < gazeTolerance;
        }
        // True while a spatial pointer device exists (visionOS); it then owns grabbing.
        bool GazePinch()
        {
#if UNITY_VISIONOS || UNITY_EDITOR
            var device = InputSystem.GetDevice<VisionOSSpatialPointerDevice>();
            if (device == null || panel == null) return false;
            // Pointer poses are in session space: the camera's parent (XR origin offset).
            var session = Camera.main != null ? Camera.main.transform.parent : null;
            Vector3 World(Vector3 p) => session != null ? session.TransformPoint(p) : p;
            Vector3 Direction(Vector3 d) => session != null ? session.TransformDirection(d) : d;
            if (gazeId >= 0)
            {
                bool held = false;
                foreach (var input in device.inputs)
                {
                    if (input.interactionId.ReadValue() != gazeId || !input.isInProgress) continue;
                    held = true; transform.position = grabOrigin + (World(input.inputDevicePosition.ReadValue()) - grabPoint);
                }
                if (!held) gazeId = -1;
                return true;
            }
            foreach (var input in device.inputs)
            {
                if (!input.isInProgress) continue;
                int id = input.interactionId.ReadValue();
                if (id == lastGazeId) continue;
                lastGazeId = id;
                if ((VisionOSSpatialPointerKind)input.kind.ReadValue() != VisionOSSpatialPointerKind.IndirectPinch) continue;
                if (!Hits(new Ray(World(input.startRayOrigin.ReadValue()), Direction(input.startRayDirection.ReadValue())))) continue;
                gazeId = id; grabPoint = World(input.inputDevicePosition.ReadValue()); grabOrigin = transform.position;
            }
            return true;
#else
            return false;
#endif
        }

        void Update()
        {
            if (!placed && Camera.main != null && Camera.main.transform.position.sqrMagnitude > 1e-4f && Time.timeSinceLevelLoad > 1)
            { placed = true; PlaceInFrontOfHead(); }

            bool looking = Looking();
            if (GazePinch())
            {
                Dragging = gazeId >= 0;
                Show(looking || Dragging, Dragging);
                Keys();
                return;
            }
            if (dragHand != null)
            {
                if (!dragHand.GoodTracking || !dragHand.IsIndexPinching()) dragHand = null;
                else transform.position = grabOrigin + (Pinch(dragHand) - grabPoint);
            }
            for (int i = 0; i < hands.Length; i++)
            {
                var hand = hands[i];
                bool pinching = hand != null && hand.isActiveAndEnabled && hand.GoodTracking && hand.IsIndexPinching() && hand.ThumbTip != null && hand.IndexTip != null;
                if (pinching && !wasPinching[i] && looking && dragHand == null) { dragHand = hand; grabPoint = Pinch(hand); grabOrigin = transform.position; }
                wasPinching[i] = pinching;
            }
            Dragging = dragHand != null;
            Show(looking || Dragging, Dragging);
            Keys();
        }
        void Keys()
        {
            var k = Keyboard.current; if (k == null) return;
            float dy = (k.pageUpKey.isPressed ? 1 : 0) - (k.pageDownKey.isPressed ? 1 : 0);
            if (dy != 0) transform.position += Vector3.up * dy * .25f * Time.deltaTime;
            if (k.homeKey.wasPressedThisFrame) PlaceInFrontOfHead();
        }
    }
}
