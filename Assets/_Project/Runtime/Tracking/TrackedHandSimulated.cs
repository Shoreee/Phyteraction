using System.Collections.Generic;
using PhysicsHands.Posing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace PhysicsHands.Tracking
{
    /// <summary>
    /// Keyboard/mouse hand for editor testing without a headset.
    /// Tab: select hand · hold RMB + mouse: move in view plane · wheel: depth ·
    /// Q/E roll · R/F pitch · Z/C yaw · LMB or G: grasp · T: index pinch · Y: middle pinch ·
    /// V: point (grasp without index) · X: reset.
    /// </summary>
    public class TrackedHandSimulated : TrackedHand
    {
        private static HandSide s_ActiveSide = HandSide.Right;
        public static HandSide ActiveSide => s_ActiveSide;
        public static void SwitchHand() => s_ActiveSide = s_ActiveSide.Other();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveSide() => s_ActiveSide = HandSide.Right;

        [SerializeField] private Transform _head;
        [SerializeField] private Vector3 _restOffset = new(0.18f, -0.25f, 0.4f);
        [SerializeField] private float _moveSpeed = 0.0015f;
        [SerializeField] private float _depthSpeed = 0.0005f;
        [SerializeField] private float _rotateSpeed = 120f;
        [SerializeField] private float _curlSpeed = 6f;

        [Header("Finger flexion (OVR skeleton: local Z)")]
        [SerializeField] private Vector3 _curlAxis = Vector3.forward;
        [SerializeField] private float _curlSign = 1f;
        [SerializeField] private float[] _maxBoneCurl = { 80f, 95f, 70f, 60f };
        [SerializeField] private float _thumbCurlMultiplier = 0.5f;

        private readonly List<(Transform bone, Quaternion rest, int finger, int index)> _bones = new();
        private readonly float[] _curl = new float[5];
        private Vector3 _localPosition;
        private Quaternion _localRotation;
        private bool _pinchIndex;
        private bool _pinchMiddle;

        private bool IsActive => s_ActiveSide == Handedness;

        protected override void Start()
        {
            base.Start();
            if (_head == null && Camera.main != null) _head = Camera.main.transform;
            ResetHand();

            if (ReferenceHandPoser == null) return;
            foreach (FingerPoser finger in ReferenceHandPoser.FingerPosers)
                for (int i = 0; i < finger.FingerBones.Count; i++)
                {
                    Transform bone = finger.FingerBones[i].target;
                    if (bone != null) _bones.Add((bone, bone.localRotation, (int)finger.type, i));
                }
        }

        private void ResetHand()
        {
            float side = Handedness == HandSide.Right ? 1f : -1f;
            _localPosition = new Vector3(_restOffset.x * side, _restOffset.y, _restOffset.z);
            _localRotation = Quaternion.identity;
        }

        protected override void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            bool grasp = false, point = false;
            _pinchIndex = _pinchMiddle = false;
            if (IsActive && keyboard != null && mouse != null)
            {
                if (keyboard.xKey.wasPressedThisFrame) ResetHand();
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    _localPosition += new Vector3(delta.x, delta.y, 0f) * _moveSpeed;
                }
                _localPosition.z += mouse.scroll.ReadValue().y * _depthSpeed;

                float dt = Time.deltaTime * _rotateSpeed;
                Vector3 euler = new(
                    Axis(keyboard.rKey, keyboard.fKey) * dt,
                    Axis(keyboard.cKey, keyboard.zKey) * dt,
                    Axis(keyboard.qKey, keyboard.eKey) * dt);
                _localRotation *= Quaternion.Euler(euler);

                grasp = mouse.leftButton.isPressed || keyboard.gKey.isPressed;
                point = keyboard.vKey.isPressed;
                _pinchIndex = keyboard.tKey.isPressed;
                _pinchMiddle = keyboard.yKey.isPressed;
            }

            ApplyWrist();
            ApplyFingers(grasp, point);
            base.Update();
        }

        private static float Axis(KeyControl positive, KeyControl negative) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);

        private void ApplyWrist()
        {
            if (_head == null) return;
            Transform wrist = Wrist;
            Vector3 flatForward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
            Quaternion basis = Quaternion.LookRotation(flatForward, Vector3.up);
            wrist.SetPositionAndRotation(_head.position + basis * _localPosition, basis * _localRotation * BaseWristRotation);
        }

        /// <summary>
        /// OVR wrist rotation for an XR wrist at identity (fingers forward, palm down),
        /// using the same offsets as <see cref="XRHandToOVRHandMapper"/>.
        /// </summary>
        private Quaternion BaseWristRotation => Handedness == HandSide.Right
            ? Quaternion.AngleAxis(-90f, Vector3.up)
            : Quaternion.AngleAxis(180f, new Vector3(-1f, 0f, 1f).normalized);

        private void ApplyFingers(bool grasp, bool point)
        {
            for (int f = 0; f < 5; f++)
            {
                float goal = grasp ? 1f : 0f;
                if (point && f != (int)HandFinger.Index) goal = 1f;
                if (_pinchIndex && (f == (int)HandFinger.Index || f == (int)HandFinger.Thumb)) goal = 0.55f;
                if (_pinchMiddle && (f == (int)HandFinger.Middle || f == (int)HandFinger.Thumb)) goal = 0.6f;
                _curl[f] = Mathf.MoveTowards(_curl[f], goal, Time.deltaTime * _curlSpeed);
            }

            foreach ((Transform bone, Quaternion rest, int finger, int index) in _bones)
            {
                float max = _maxBoneCurl[Mathf.Min(index, _maxBoneCurl.Length - 1)];
                if (finger == (int)HandFinger.Thumb) max *= _thumbCurlMultiplier;
                bone.localRotation = rest * Quaternion.AngleAxis(_curl[finger] * max * _curlSign, _curlAxis);
            }
        }

        public override bool IsTracked() => true;
        public override bool HasHighConfidence() => true;
        public override bool FingerHasHighConfidence(HandFinger finger) => true;
        public override bool IsGrasping() => _curl[(int)HandFinger.Middle] > 0.6f;

        public override bool IsPinching(HandFinger finger) => finger switch
        {
            HandFinger.Index => _pinchIndex,
            HandFinger.Middle => _pinchMiddle,
            _ => false,
        };

        public override float GetPinchingStrength(HandFinger finger) => IsPinching(finger) ? 1f : 0f;
        public override float GetScale() => 1f;
    }
}
