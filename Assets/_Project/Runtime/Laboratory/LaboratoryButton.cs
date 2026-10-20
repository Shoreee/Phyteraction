using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.Laboratory
{
    /// <summary>Debounced physical hand button usable without a controller or ray pointer.</summary>
    public class LaboratoryButton : MonoBehaviour
    {
        public LaboratoryController Controller;
        public bool GravityButton;
        private float _nextPress;
        private void OnTriggerEnter(Collider other)
        {
            if (Controller == null || Time.unscaledTime < _nextPress || other.GetComponentInParent<HoloHand>() == null) return;
            _nextPress = Time.unscaledTime + 1f;
            if (GravityButton) Controller.ToggleGravity();
            else Controller.ResetExperiment();
        }
    }
}
