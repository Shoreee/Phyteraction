using UnityEngine;

namespace PhysicsHands
{
    /// <summary>
    /// Physics timing and hand solver quality settings.
    /// </summary>
    [CreateAssetMenu(menuName = "Physics Hands/Physics Setup", fileName = "PhysicsSetup")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Settings", "PhysicsHands.Runtime", "PhysicsSetup")]
    public class PhysicsSetup : ScriptableObject
    {
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
            Time.fixedDeltaTime = Mathf.Clamp(fixedDeltaTime, 1f / 360f, 0.05f);
            Time.maximumDeltaTime = 0.1f;
            Physics.defaultContactOffset = defaultContactOffset;
        }
    }
}
