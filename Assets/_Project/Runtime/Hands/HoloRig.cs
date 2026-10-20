using PhysicsHands.Settings;
using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>Scene root of the player hands: applies the global physics preset and exposes both hands.</summary>
    [DefaultExecutionOrder(ExecutionOrder.HandInput - 10)]
    public class HoloRig : MonoBehaviour
    {
        [SerializeField] private PhysicsSetup _physicsSetup;
        [SerializeField] private GameSettings _gameSettings;
        [SerializeField] private HoloHand _leftHand;
        [SerializeField] private HoloHand _rightHand;

        public static HoloRig Instance { get; private set; }
        public GameSettings GameSettings => _gameSettings;
        public HoloHand LeftHand => _leftHand;
        public HoloHand RightHand => _rightHand;

        public HoloHand GetHand(HandSide side) => side == HandSide.Right ? _rightHand : _leftHand;

        private void Awake()
        {
            Instance = this;
            if (_physicsSetup != null) _physicsSetup.ApplyGlobal();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ResetHands()
        {
            if (_leftHand != null) _leftHand.Reset();
            if (_rightHand != null) _rightHand.Reset();
        }
    }
}
