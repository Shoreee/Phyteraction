using UnityEngine;
using PhysicsHands.Posing;
using UnityEngine.XR.Management;
using UnityEngine.XR.Hands;
using System.Collections.Generic;

namespace PhysicsHands.Tracking
{
    public enum HandTrackingSystem
    {
        Auto = 0,
        XRHands = 25,
        Simulated = 100,
    }

    /// <summary>
    /// Chooses the active tracked-hand source per side. Auto uses XR Hands when an XR
    /// loader is active and falls back to the keyboard/mouse simulator otherwise.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.HandInput)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Tracking", "PhysicsHands.Runtime", "HandInputManager")]
    public class HandInputManager : MonoBehaviour, IHandPoseProvider
    {
        [SerializeField]
        private HandTrackingSystem _handTrackingSystem = HandTrackingSystem.Auto;
        [SerializeField]
        private TrackedHand _xrHandLeft;
        [SerializeField]
        private TrackedHand _xrHandRight;
        [SerializeField]
        private TrackedHand _simulatedHandLeft;
        [SerializeField]
        private TrackedHand _simulatedHandRight;
        public HandTrackingSystem ActiveSystem
        {
            get;
            private set;
        }

        public TrackedHand TrackedHandLeft
        {
            get;
            private set;
        }

        public TrackedHand TrackedHandRight
        {
            get;
            private set;
        }

        private void Awake()
        {
            SelectSystem(ChooseSystem());
        }

        private readonly List<XRHandSubsystem> _subsystems = new();
        private float _nextSourceCheck;
        private HandSide _activeSimulatedSide = HandSide.Right;
        private void SwitchSimulatedHand()
        {
            _activeSimulatedSide = _activeSimulatedSide.Other();
            if (_simulatedHandLeft is TrackedHandSimulated left)
                left.SetActiveSide(_activeSimulatedSide);
            if (_simulatedHandRight is TrackedHandSimulated right)
                right.SetActiveSide(_activeSimulatedSide);
        }

        private HandTrackingSystem ChooseSystem()
        {
            if (_handTrackingSystem != HandTrackingSystem.Auto)
                return _handTrackingSystem;
#if UNITY_VISIONOS && !UNITY_EDITOR
            // Never synthesize tracked hands on the headset while permission or
            // tracking is unavailable.
            return HandTrackingSystem.XRHands;
#else
            _subsystems.Clear();
            SubsystemManager.GetSubsystems(_subsystems);
            return IsXRRunning() || _subsystems.Exists(s => s.running) ? HandTrackingSystem.XRHands : HandTrackingSystem.Simulated;
#endif
        }

        private void Update()
        {
            if (ActiveSystem == HandTrackingSystem.Simulated && UnityEngine.InputSystem.Keyboard.current?.tabKey.wasPressedThisFrame == true)
                SwitchSimulatedHand();
            if (_handTrackingSystem != HandTrackingSystem.Auto || Time.unscaledTime < _nextSourceCheck)
                return;
            _nextSourceCheck = Time.unscaledTime + 0.5f;
            HandTrackingSystem desired = ChooseSystem();
            if (desired != ActiveSystem)
                SelectSystem(desired);
        }

        /// <summary>Explicit scene-level source selection (e.g. standalone desktop demo).</summary>
        public void SetTrackingSystem(HandTrackingSystem system)
        {
            _handTrackingSystem = system;
            SelectSystem(ChooseSystem());
        }

        private void SelectSystem(HandTrackingSystem system)
        {
            ActiveSystem = system;
            bool xr = ActiveSystem == HandTrackingSystem.XRHands;
            TrackedHandLeft = xr ? _xrHandLeft : _simulatedHandLeft;
            TrackedHandRight = xr ? _xrHandRight : _simulatedHandRight;
            SetActive(_xrHandLeft, xr);
            SetActive(_xrHandRight, xr);
            SetActive(_simulatedHandLeft, !xr);
            SetActive(_simulatedHandRight, !xr);
        }

        public IHandPoseSource GetPoseSource(HandSide side) => GetTrackedHand(side);
        public TrackedHand GetTrackedHand(HandSide side) => side == HandSide.Right ? TrackedHandRight : TrackedHandLeft;
        private static void SetActive(TrackedHand hand, bool active)
        {
            if (hand != null)
                hand.gameObject.SetActive(active);
        }

        private static bool IsXRRunning()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            return manager != null && manager.activeLoader != null;
        }
    }
}
