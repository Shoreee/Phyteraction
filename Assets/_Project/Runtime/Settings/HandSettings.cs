using UnityEngine;

namespace PhysicsHands.Settings
{
    /// <summary>
    /// Wrist drive tuning. Field names and default values are the original
    /// game's HandSettings asset (sharedassets1, pathID 180816).
    /// Reimplementation assumption (not recovered from native method bodies): each drive term is evaluated as
    /// Base + Delta * error + TargetVelocity * |v_target| + HandVelocity * |v_hand| + HandDeltaVelocity * |v_target - v_hand|
    /// and then converted to joint drive units with the scale factors below.
    /// </summary>
    [CreateAssetMenu(menuName = "Physics Hands/Hand Settings", fileName = "HandSettings")]
    public class HandSettings : ScriptableObject
    {
        [Header("Position spring")]
        public float BasePosSpringMlt = 50f;
        public float DeltaPosSpringMlt = 5f;
        public float TargetVelocityPosSpringMlt = 0f;
        public float HandVelocityPosSpringMlt = 0f;
        public float HandDeltaVelocityPosSpringMlt = 0f;

        [Header("Position damper")]
        public float BasePosDamperMlt = 10f;
        public float DeltaPosDamperMlt = 5f;
        public float TargetVelocityPosDamperMlt = 1000f;
        public float HandVelocityPosDamperMlt = 0f;
        public float HandDeltaVelocityPosDamperMlt = 0f;

        [Header("Rotation spring")]
        public float BaseRotSprintMlt = 10f;
        public float DeltaRotSpringMlt = 0.1f;
        public float DeltaPosRotSpringMlt = 0f;
        public float TargetVelocityRotSpringMlt = 0f;
        public float HandVelocityRotSpringMlt = 0f;
        public float HandDeltaVelocityRotSpringMlt = 0f;

        [Header("Rotation damper")]
        public float BaseRotDamperMlt = 0.001f;
        public float DeltaRotDamperMlt = -50f;
        public float TargetVelocityRotDamperMlt = 0f;
        public float HandVelocityRotDamperMlt = 0f;
        public float HandDeltaVelocityRotDamperMlt = 0f;

        [Header("Unit conversion (reimplementation, not in the original asset)")]
        [Tooltip("positionSpring = spring terms * mass * scale")]
        public float LinearSpringScale = 60f;
        [Tooltip("positionDamper = damper terms * mass * scale")]
        public float LinearDamperScale = 6f;
        [Tooltip("angular spring = spring terms * inertia * scale")]
        public float AngularSpringScale = 160f;
        [Tooltip("Critical-damping ratio added on top of the angular damper terms.")]
        public float AngularDampingRatio = 0.8f;
        [Tooltip("Finger angular spring = joint spring * muscle * humanoid * setup * scale")]
        public float FingerSpringScale = 1f;
        [Tooltip("Finger angular damper = joint damper * muscle * humanoid * setup * scale")]
        public float FingerDamperScale = 0.08f;
        [Tooltip("Lower bound for each summed multiplier (the original contains negative terms).")]
        public float MinimumMultiplier = 0.001f;
        [Tooltip("Cap velocity-dependent damping relative to critical damping; avoids viscous lag.")]
        public float MaximumLinearDampingRatio = 1f;
        public bool EnableTargetVelocityFollow = true;
        [Tooltip("Wrist spring response relative to the previous reimplementation; preserves finite joint forces.")]
        public float WristResponseMultiplier = 1.5f;

        public struct Inputs
        {
            public float PositionError;
            public float AngleError;
            public float TargetSpeed;
            public float HandSpeed;
            public float DeltaSpeed;
        }

        public float PositionSpring(in Inputs i) => Clamp(BasePosSpringMlt + DeltaPosSpringMlt * i.PositionError +
            TargetVelocityPosSpringMlt * i.TargetSpeed + HandVelocityPosSpringMlt * i.HandSpeed +
            HandDeltaVelocityPosSpringMlt * i.DeltaSpeed);

        public float PositionDamper(in Inputs i) => Clamp(BasePosDamperMlt + DeltaPosDamperMlt * i.PositionError +
            TargetVelocityPosDamperMlt * i.TargetSpeed + HandVelocityPosDamperMlt * i.HandSpeed +
            HandDeltaVelocityPosDamperMlt * i.DeltaSpeed);

        public float RotationSpring(in Inputs i) => Clamp(BaseRotSprintMlt + DeltaRotSpringMlt * i.AngleError +
            DeltaPosRotSpringMlt * i.PositionError + TargetVelocityRotSpringMlt * i.TargetSpeed +
            HandVelocityRotSpringMlt * i.HandSpeed + HandDeltaVelocityRotSpringMlt * i.DeltaSpeed);

        public float RotationDamper(in Inputs i) => Clamp(BaseRotDamperMlt + DeltaRotDamperMlt * i.AngleError +
            TargetVelocityRotDamperMlt * i.TargetSpeed + HandVelocityRotDamperMlt * i.HandSpeed +
            HandDeltaVelocityRotDamperMlt * i.DeltaSpeed);

        private float Clamp(float value) => Mathf.Max(MinimumMultiplier, value);
    }
}
