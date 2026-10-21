using PhysicsHands;
using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>Scene root of the player hands: applies the global physics preset and exposes both hands.</summary>
    [DefaultExecutionOrder(ExecutionOrder.HandInput - 10)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Hands", "PhysicsHands.Runtime", "HoloRig")]
    public class PhysicsHandsRig : MonoBehaviour
    {
        [SerializeField]
        private PhysicsSetup _physicsSetup;
        [SerializeField]
        private GameSettings _gameSettings;
        [SerializeField]
        private PhysicsHand _leftHand;
        [SerializeField]
        private PhysicsHand _rightHand;
        public GameSettings GameSettings => _gameSettings;
        public PhysicsHand LeftHand => _leftHand;
        public PhysicsHand RightHand => _rightHand;
        private void Awake()
        {
            if (_physicsSetup != null)
                _physicsSetup.ApplyGlobal();
        }

    }
}
