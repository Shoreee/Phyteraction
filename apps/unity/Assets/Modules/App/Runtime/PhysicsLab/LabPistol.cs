using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using UnityEngine;

namespace PhysicsHands.App
{
    /// <summary>The same physical firing path serves friction and compliant grip variants.</summary>
    public sealed class LabPistol : MonoBehaviour
    {
        public Rigidbody body;
        public PhysicalFingerTrigger trigger;
        public Transform muzzle;
        public LabProjectileSystem projectiles;
        [Min(.001f)] public float projectileMass = .025f;
        [Min(1)] public float projectileSpeed = 24;
        [Min(.01f)] public float minimumShotInterval = .12f;
        float nextShot;
        public int ShotsFired { get; private set; }
        void OnEnable()
        {
            if (body == null || trigger == null || muzzle == null || projectiles == null)
            { Debug.LogError($"{name}: pistol requires body, physical trigger, muzzle and projectile pool.", this); enabled = false; return; }
            trigger.Pulled += Fire;
            ResetState();
        }
        void Fire(PhysicsHand hand)
        {
            // Trigger travel, recoil and cooldown share the physics clock, including
            // catch-up steps and deterministic input replay.
            if (Time.fixedTime < nextShot || !hand.GoodTracking) return;
            // Fire from the physics pose, not the interpolated display transform.
            Quaternion correction = body.rotation * Quaternion.Inverse(body.transform.rotation);
            Vector3 position = body.position + correction * (muzzle.position - body.transform.position);
            Vector3 direction = correction * muzzle.forward;
            Vector3 velocity = direction * projectileSpeed + body.GetPointVelocity(position);
            if (!projectiles.Launch(this, position, velocity, projectileMass)) return;
            body.WakeUp();
            body.AddForceAtPosition(-direction * (projectileMass * projectileSpeed), position, ForceMode.Impulse);
            nextShot = Time.fixedTime + minimumShotInterval;
            ShotsFired++;
        }
        public void ResetState() { nextShot = 0; ShotsFired = 0; if (trigger != null) trigger.ResetPull(); }
        void OnDisable() { if (trigger != null) trigger.Pulled -= Fire; }
    }
}
