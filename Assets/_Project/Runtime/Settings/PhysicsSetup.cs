using UnityEngine;

namespace PhysicsHands.Settings
{
    public enum PhysicsSetupType
    {
        Performant = 1,
        Default = 2,
        Accurate = 3,
    }

    /// <summary>
    /// Global physics quality preset. The three original assets
    /// (PhysicsSetup_Performant/Default/Accurate) are recreated by ProjectSetup.
    /// </summary>
    [CreateAssetMenu(menuName = "Physics Hands/Physics Setup", fileName = "PhysicsSetup")]
    public class PhysicsSetup : ScriptableObject
    {
        public PhysicsSetupType physicsSetupType = PhysicsSetupType.Accurate;

        [Space]
        public float fixedDeltaTime = 0.01f;
        public float defaultContactOffset = 0.005f;
        public float humanoidWristSpringMultiplier = 1f;
        public float humanoidWristDamperMultiplier = 1f;
        public float humanoidFingerSpringMultiplier = 2f;
        public float humanoidFingerDamperMultiplier = 2f;
        public float solverIterationCountMultiplier = 3f;

        public void ApplyGlobal()
        {
            Time.fixedDeltaTime = Mathf.Clamp(fixedDeltaTime, 0.005f, 0.05f);
            Time.maximumDeltaTime = 0.1f;
            Physics.defaultContactOffset = defaultContactOffset;
        }
    }
}
