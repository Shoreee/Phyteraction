using System;
using PhysicsHands.Dynamics;
using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>
    /// A physical phalanx. Computes its joint drive every physics step from the bone's
    /// "muscle" multipliers and the hand-wide multipliers (humanoid, physics setup, grab,
    /// floppy fingers).
    /// </summary>
    [RequireComponent(typeof(PhysicsJointDriver))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Hands", "PhysicsHands.Runtime", "HoloFingerBone")]
    public class PhysicsFingerBone : MonoBehaviour
    {
        [SerializeField]
        private float _muscleSpringMultiplier = 1f;
        [SerializeField]
        private float _muscleDamperMultiplier = 1f;
        [SerializeField]
        private Collider[] _colliders = Array.Empty<Collider>();
        [SerializeField]
        private Collider[] _collidersToIgnore = Array.Empty<Collider>();
        [Tooltip("Angle error (deg) at which the bone reaches double drive strength.")]
        [SerializeField]
        private float _distanceStrengthAngle = 45f;
        private PhysicsJointDriver _jointEnforcer;
        private PhysicsHand _hand;
        private float _springMultiplier = 1f;
        private float _damperMultiplier = 1f;
        private float _distanceSpringMultiplier = 1f;
        private float _lastSpring = -1f;
        private float _lastDamper = -1f;
        private float _timeLastSoundPlayed;
        private const float SoundDelay = 0.1f;
        public PhysicsJointDriver PhysicsJointDriver => _jointEnforcer;
        public PhysicsHand PhysicsHand => _hand;
        public Collider[] GetColliders() => _colliders;
        public event Action<PhysicsFingerBone, Collision> Touched;
        private void Awake()
        {
            _jointEnforcer = GetComponent<PhysicsJointDriver>();
            _hand = GetComponentInParent<PhysicsHand>();
            if (_colliders == null || _colliders.Length == 0)
                _colliders = GetComponentsInChildren<Collider>();
        }

        private void Start()
        {
            foreach (Collider own in _colliders)
                foreach (Collider other in _collidersToIgnore)
                    if (own != null && other != null)
                        Physics.IgnoreCollision(own, other, true);
        }

        private void FixedUpdate()
        {
            AdjustJointStrength();
            UpdateJointDrive();
        }

        private void AdjustJointStrength()
        {
            if (_jointEnforcer.Joint == null)
                return;
            _distanceSpringMultiplier = 1f + Mathf.Clamp01(_jointEnforcer.GetAngleFromTarget() / _distanceStrengthAngle);
        }

        private void UpdateJointDrive()
        {
            if (_jointEnforcer.Joint == null || _hand == null)
                return;
            float spring = _muscleSpringMultiplier * _springMultiplier * _distanceSpringMultiplier * _hand.FingerSpringMultiplier;
            float damper = _muscleDamperMultiplier * _damperMultiplier * _hand.FingerDamperMultiplier;
            if (Mathf.Approximately(spring, _lastSpring) && Mathf.Approximately(damper, _lastDamper))
                return;
            _lastSpring = spring;
            _lastDamper = damper;
            _jointEnforcer.SetDriveMultipliers(1f, 1f, spring, damper);
        }

        public void SetSpringDamperMultiplier(float springMultiplier, float damperMultiplier)
        {
            _springMultiplier = springMultiplier;
            _damperMultiplier = damperMultiplier;
        }

        public void ResetMultipliers()
        {
            _springMultiplier = 1f;
            _damperMultiplier = 1f;
        }

        /// <summary>Floppy finger: keeps the joint limits but removes the muscle.</summary>
        public void FreeMovement() => SetSpringDamperMultiplier(0f, 0.2f);
        public void Reset()
        {
            if (_jointEnforcer == null)
                return;
            _jointEnforcer.InstantMapToTarget();
        }

        public void DetachBone()
        {
            if (_jointEnforcer.Joint != null)
                Destroy(_jointEnforcer.Joint);
            _jointEnforcer.Enforce = false;
            _jointEnforcer.CancelGravity = false;
        }

        public void SetMassMultiplier(float multiplier) => _jointEnforcer.SetMassMultiplier(multiplier);
        private void OnCollisionEnter(Collision collision)
        {
            if (Time.time - _timeLastSoundPlayed < SoundDelay)
                return;
            _timeLastSoundPlayed = Time.time;
            Touched?.Invoke(this, collision);
        }
    }
}
