using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>
    /// Anything that can hold a <see cref = "Holdable"/>: hands, inventory slots, bins.
    /// Tracks reachable holdables through trigger contacts and keeps the closest one focused.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.Holder)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Interaction", "PhysicsHands.Runtime", "Holder")]
    public class Holder : MonoBehaviour
    {
        [SerializeField]
        protected Transform _snapJointAnchor;
        [SerializeField]
        protected Transform _snapPointTranslocator;
        [SerializeField]
        protected Transform _snapPointRotator;
        [SerializeField]
        protected Transform _baseRotator;
        [SerializeField]
        protected Transform _holdableContainer;
        [SerializeField]
        protected HolderType _holderType = HolderType.Undefined;
        [SerializeField]
        protected bool _captureKinematically;
        [SerializeField]
        protected float _releaseDistance = 0.05f;
        [SerializeField]
        protected bool _restrictAllowedObjects;
        [SerializeField]
        protected ObjectType[] _allowedObjectTypes = Array.Empty<ObjectType>();
        [SerializeField]
        protected HoldingLink _holdingLink;
        [SerializeField]
        protected AudioSource _audioSource;
        [SerializeField]
        protected AudioClip _defaultCaptureClip;
        [SerializeField]
        protected AudioClip _defaultReleaseClip;
        protected Holdable _capturedObject;
        protected Holdable _closestHoldable;
        protected Holdable _focusedHoldable;
        protected readonly List<Holdable> _reachableHoldables = new();
        private readonly Dictionary<Holdable, int> _contactCounts = new();
        public event Action<Holdable> OnCaptured;
        public event Action<Holdable> OnReleased;
        public event Action<Holdable> NewClosestDetected;
        public event Action<Holdable> NoMoreObjectDetected;
        public HolderType HolderType => _holderType;
        public Holdable CapturedObject => _capturedObject;
        public Holdable ClosestHoldable => _closestHoldable;
        public HoldingLink HoldingLink => _holdingLink;
        public Transform SnapJointAnchor => _snapJointAnchor;
        public Transform HoldableContainer => _holdableContainer;
        public bool CaptureKinematically => _captureKinematically;
        public bool IsHolding => _capturedObject != null;
        protected virtual void Awake()
        {
            if (_holdingLink == null)
                _holdingLink = GetComponent<HoldingLink>();
            if (_snapJointAnchor == null)
                _snapJointAnchor = transform;
        }

        protected virtual void Start()
        {
        }

        protected virtual void Update()
        {
            CheckIfHoldableActive();
            CheckClosest();
            if (_capturedObject != null && _holderType != HolderType.Hand && Vector3.Distance(_capturedObject.transform.position, _snapJointAnchor.position) > _releaseDistance + 0.5f)
                Release();
        }

        protected virtual void FixedUpdate()
        {
        }

        protected void CheckIfHoldableActive()
        {
            for (int i = _reachableHoldables.Count - 1; i >= 0; i--)
            {
                Holdable h = _reachableHoldables[i];
                if (h == null || !h.isActiveAndEnabled)
                    RemoveReachable(h, true);
            }

            if (_capturedObject != null && !_capturedObject.isActiveAndEnabled)
                ForceRelease();
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            Holdable holdable = Holdable.FromCollider(other);
            if (holdable != null)
                HandleDetectedHoldable(holdable, other);
        }

        protected virtual void HandleDetectedHoldable(Holdable holdable, Collider collider)
        {
            _contactCounts.TryGetValue(holdable, out int count);
            _contactCounts[holdable] = count + 1;
            AddReachable(holdable);
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            Holdable holdable = Holdable.FromCollider(other);
            if (holdable == null || !_contactCounts.TryGetValue(holdable, out int count))
                return;
            if (count > 1)
                _contactCounts[holdable] = count - 1;
            else
                RemoveReachable(holdable, true);
        }

        protected void AddReachable(Holdable holdable)
        {
            if (!_reachableHoldables.Contains(holdable) && CheckIfCanHoldThis(holdable))
                _reachableHoldables.Add(holdable);
        }

        protected void RemoveReachable(Holdable holdable, bool clearContacts)
        {
            _reachableHoldables.Remove(holdable);
            if (clearContacts && holdable != null)
                _contactCounts.Remove(holdable);
            if (_focusedHoldable == holdable)
                Focus(holdable, false, false);
        }

        protected void CheckClosest()
        {
            Holdable closest = null;
            float best = float.PositiveInfinity;
            Vector3 origin = _snapJointAnchor.position;
            foreach (Holdable h in _reachableHoldables)
            {
                if (h == null || h == _capturedObject)
                    continue;
                float d = h.DistanceTo(origin);
                if (d < best)
                {
                    best = d;
                    closest = h;
                }
            }

            if (closest == _closestHoldable)
                return;
            if (_closestHoldable != null)
                Focus(_closestHoldable, false, false);
            _closestHoldable = closest;
            if (closest != null)
            {
                Focus(closest, true, true);
                NewClosestDetected?.Invoke(closest);
            }
            else
                NoMoreObjectDetected?.Invoke(null);
        }

        public virtual void Focus(Holdable holdable, bool focus, bool showTarget)
        {
            if (holdable == null)
                return;
            holdable.Focus(this, focus);
            _focusedHoldable = focus ? holdable : _focusedHoldable == holdable ? null : _focusedHoldable;
        }

        public virtual void Capture(Holdable holdable) => Capture(holdable, false);
        public virtual bool Capture(Holdable holdable, bool distantGrab)
        {
            if (holdable == null || _capturedObject != null || !CheckIfCanHoldThis(holdable))
                return false;
            if (!AttachLink(holdable, distantGrab)) return false;
            _capturedObject = holdable;
            holdable.Capture(this);
            PlayCaptureSound(holdable);
            OnCaptured?.Invoke(holdable);
            return true;
        }

        /// <summary>Captures the currently focused holdable.</summary>
        protected virtual void Capture()
        {
            if (_closestHoldable != null)
                Capture(_closestHoldable);
        }

        protected virtual bool AttachLink(Holdable holdable, bool distantGrab)
        {
            return _holdingLink != null && _holdingLink.Attach(this, holdable, distantGrab);
        }

        public virtual void ForceRelease() => Release();
        public virtual void Release()
        {
            Holdable released = _capturedObject;
            if (released == null)
                return;
            _capturedObject = null;
            if (_holdingLink != null)
                _holdingLink.Release();
            released.Release(this);
            PlayReleaseSound(released);
            OnReleased?.Invoke(released);
        }

        protected bool CheckIfCanHoldThis(Holdable holdable) => holdable != null && holdable.AllowsHolder(_holderType) && CheckObjectAllowed(holdable);
        protected bool CheckObjectAllowed(Holdable holdable)
        {
            if (!_restrictAllowedObjects)
                return true;
            ObjectType type = holdable.Interactable != null ? holdable.Interactable.ObjectType : ObjectType.Undefined;
            return Array.IndexOf(_allowedObjectTypes, type) >= 0;
        }

        protected void PlayCaptureSound(Holdable holdable)
        {
            AudioClip clip = holdable.ClipGrab != null ? holdable.ClipGrab : _defaultCaptureClip;
            if (_audioSource != null && clip != null)
                _audioSource.PlayOneShot(clip);
        }

        protected void PlayReleaseSound(Holdable holdable)
        {
            AudioClip clip = holdable.ClipRelease != null ? holdable.ClipRelease : _defaultReleaseClip;
            if (_audioSource != null && clip != null)
                _audioSource.PlayOneShot(clip);
        }
    }
}
