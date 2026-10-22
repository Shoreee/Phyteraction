using UnityEngine;

namespace PhysicsHands
{
    /// <summary>
    /// Interaction-related subset of the original GameSettingsHPL asset. Menu, body,
    /// locomotion, painting and level options were dropped with those systems.
    /// </summary>
    [CreateAssetMenu(menuName = "Physics Hands/Game Settings", fileName = "GameSettings")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Settings", "PhysicsHands.Runtime", "GameSettings")]
    public class GameSettings : ScriptableObject
    {
        public ObservableSetting<bool> SnapHandsActive = new(true);
        public ObservableSetting<bool> SnapHighlightActive = new(true);
        public ObservableSetting<bool> GravityActive = new(true);
        public ObservableSetting<bool> SafetyActive = new(true);
        public ObservableSetting<bool> BadTrackingWarningActive = new(true);
        public ObservableSetting<bool> ResetActive = new(true);
        public ObservableSetting<bool> ShowAxes = new(false);
        public ObservableSetting<bool> VirtualMappingActive = new(true);
        public ObservableSetting<bool> HandPhysicsActive = new(true);
        public ObservableSetting<bool> PrimaryHandRight = new(true);
        public ObservableSetting<bool> MirroredFingersActive = new(false);
        public ObservableSetting<bool> FloppyFingersActive = new(false);
        public ObservableSetting<bool> FingerTouchSoundActive = new(true);
        public ObservableSetting<bool> TelekinesisActive = new(false);
        public ObservableSetting<bool> ForceFieldActive = new(false);
    }
}
