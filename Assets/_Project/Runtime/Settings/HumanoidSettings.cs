using UnityEngine;

namespace PhysicsHands.Settings
{
    /// <summary>Per-rig drive multipliers. Defaults are the original asset values.</summary>
    [CreateAssetMenu(menuName = "Physics Hands/Humanoid Settings", fileName = "HumanoidSettings")]
    public class HumanoidSettings : ScriptableObject
    {
        public float WristSpringMultiplier = 1f;
        public float WristAngularSpringMultiplier = 1f;
        public float WristDamperMultiplier = 1f;
        public float WristAngularDamperMultiplier = 1f;
        public float FingerSpringMultiplier = 2f;
        public float FingerDamperMultiplier = 2f;

        [Space]
        public float SolverIterationCountMultiplier = 3f;
        public float MassMultiplier = 1f;
    }
}
