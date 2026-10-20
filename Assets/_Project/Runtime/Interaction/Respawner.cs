using System;
using System.Collections;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>Respawns an object that fell below a height or strayed too far from its origin.</summary>
    [RequireComponent(typeof(MovableObject))]
    public class Respawner : MonoBehaviour
    {
        public float MinHeight = 0.3f;
        public bool CheckLocally;
        public float MaxDistanceFromOrigin = 3f;
        public float RespawnDelay = 1f;
        public bool PlayParticlesOnRespawn = true;
        public bool PlaySoundOnRespawn = true;
        public bool ScaleOnRespawn = true;
        [SerializeField] private float _checkInterval = 0.25f;
        [SerializeField] private float _scaleDuration = 0.3f;
        [SerializeField] private ParticleSystem _respawnParticles;
        [SerializeField] private AudioSource _respawnSound;

        private MovableObject _movableObject;
        private Holdable _holdable;
        private bool _markedForRespawn;
        private Coroutine _checkRoutine;

        public event Action<Respawner> OnDespawned;
        public event Action<Respawner> OnRespawned;

        private void Awake()
        {
            _movableObject = GetComponent<MovableObject>();
            _holdable = GetComponent<Holdable>();
        }

        private void OnEnable() => _checkRoutine = StartCoroutine(CheckLoop());

        private void OnDisable()
        {
            if (_checkRoutine != null) StopCoroutine(_checkRoutine);
            _markedForRespawn = false;
        }

        private IEnumerator CheckLoop()
        {
            var wait = new WaitForSeconds(_checkInterval);
            while (true)
            {
                yield return wait;
                if (!_markedForRespawn && CheckRespawnConditions()) StartCoroutine(RespawnAfterDelay());
            }
        }

        public bool CheckRespawnConditions()
        {
            if (_holdable != null && _holdable.IsHeld) return false;
            float height = CheckLocally && transform.parent != null ? transform.localPosition.y : transform.position.y;
            float distance = CheckLocally ? _movableObject.GetDistanceFromInitialLocalPosition() : _movableObject.GetDistanceFromInitialGlobalPosition();
            return height < MinHeight || distance > MaxDistanceFromOrigin;
        }

        private IEnumerator RespawnAfterDelay()
        {
            _markedForRespawn = true;
            yield return new WaitForSeconds(RespawnDelay);
            if (CheckRespawnConditions()) Respawn();
            _markedForRespawn = false;
        }

        public void Respawn()
        {
            Despawn();
            Spawn(ScaleOnRespawn, PlayParticlesOnRespawn, _scaleDuration, true);
        }

        public void Despawn()
        {
            if (_holdable != null) _holdable.ForceRelease();
            OnDespawned?.Invoke(this);
        }

        public void Spawn(bool scaleUp, bool showSpawnEffect, float duration, bool resetPosition)
        {
            _movableObject.ResetObject(true, resetPosition);
            if (scaleUp) StartCoroutine(ScaleUp(duration));
            if (showSpawnEffect && _respawnParticles != null) _respawnParticles.Play();
            if (PlaySoundOnRespawn && _respawnSound != null) _respawnSound.Play();
            OnRespawned?.Invoke(this);
        }

        private IEnumerator ScaleUp(float duration)
        {
            _movableObject.ResetScale();
            Vector3 target = transform.localScale;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                transform.localScale = target * Mathf.SmoothStep(0.01f, 1f, t / duration);
                yield return null;
            }
            transform.localScale = target;
        }
    }
}
