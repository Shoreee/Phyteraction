using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using PhysicsHands.Tracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsHands.Laboratory
{
    /// <summary>Lab session controls; uses the same actions from physical buttons and keyboard.</summary>
    public class LaboratoryController : MonoBehaviour
    {
        public TextMesh StatusLabel;
        private MovableObject[] _props;
        private float _nextStatus;
        private bool _initialGravity;

        private void Start()
        {
            _props = FindObjectsByType<MovableObject>();
            _initialGravity = HoloRig.Instance == null || HoloRig.Instance.GameSettings.GravityActive.Value;
        }
        private void OnDestroy()
        {
            if (HoloRig.Instance != null && HoloRig.Instance.GameSettings != null)
                HoloRig.Instance.GameSettings.GravityActive.Value = _initialGravity;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.backspaceKey.wasPressedThisFrame) ResetExperiment();
                if (keyboard.digit1Key.wasPressedThisFrame) ToggleGravity();
            }
            if (StatusLabel == null || Time.unscaledTime < _nextStatus) return;
            _nextStatus = Time.unscaledTime + 0.25f;
            var input = HandInputManager.Instance;
            bool left = input != null && input.TrackedHandLeft != null && input.TrackedHandLeft.GoodTracking;
            bool right = input != null && input.TrackedHandRight != null && input.TrackedHandRight.GoodTracking;
            StatusLabel.text = $"{(input != null ? input.ActiveSystem.ToString().ToUpperInvariant() : "WAITING")}   /   L {(left ? "READY" : "SEARCHING")}   R {(right ? "READY" : "SEARCHING")}\nGRAVITY {(HoloRig.Instance != null && HoloRig.Instance.GameSettings.GravityActive.Value ? "ON" : "OFF")}";
        }

        public void ResetExperiment()
        {
            if (_props == null) return;
            foreach (MovableObject prop in _props)
            {
                if (prop == null) continue;
                var holdable = prop.GetComponent<Holdable>();
                if (holdable != null) holdable.ForceRelease();
                prop.ResetObject(true, true);
                prop.ResetScale();
            }
            if (HoloRig.Instance != null) HoloRig.Instance.ResetHands();
        }

        public void ToggleGravity()
        {
            var settings = HoloRig.Instance != null ? HoloRig.Instance.GameSettings : null;
            if (settings != null) settings.GravityActive.Value = !settings.GravityActive.Value;
        }
    }
}
