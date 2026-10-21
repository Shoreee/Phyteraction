using System;
using System.Collections.Generic;
using PhysicsHands.Posing;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>
    /// A rigidbody that holders can capture. Hand grabbers detect it through trigger
    /// colliders on the Grabbable layer (auto-generated from the physical colliders when
    /// no container is assigned).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Interaction", "PhysicsHands.Runtime", "Holdable")]
    public class Holdable : MonoBehaviour
    {
        [SerializeField]
        private bool _restrictAllowedHolderTypes;
        [SerializeField]
        private List<HolderType> _allowedHolderTypes = new();
        [SerializeField]
        private bool _restrictAllowedSnapTypes = true;
        [SerializeField]
        private List<SnapType> _allowedSnapTypes = new()
        {SnapType.Grasp};
        [SerializeField]
        private bool _useHandPoserManager = true;
        [SerializeField]
        private bool _allowProceduralPoses = true;
        [SerializeField]
        private bool _captureKinematically;
        [SerializeField]
        private bool _makeSpeculativeOnGrab = true;
        [SerializeField]
        private Transform _collidersContainer;
        [SerializeField]
        private AudioClip _clipGrab;
        [SerializeField]
        private AudioClip _clipRelease;
        [SerializeField]
        private GraspLevel _graspLevel = GraspLevel.Moderate;
        [SerializeField]
        private float _handToObjectReleaseDistance = 0.12f;
        [SerializeField]
        private float _handToTargetReleaseDistance = 0.3f;
        [SerializeField]
        private float _handToTargetVibrationDistance = 0.1f;
        [SerializeField]
        private HandPoserManager _handPoserManager;
        [Tooltip("Extra size of auto-generated grab triggers, in metres.")]
        [SerializeField]
        private float _grabTriggerPadding = 0.01f;
        private static readonly Dictionary<Collider, Holdable> s_ColliderLookup = new();
        private Rigidbody _rb;
        private Interactable _interactable;
        private MovableObject _movableObject;
        private Respawner _respawner;
        private readonly List<Collider> _colliders = new();
        private readonly List<Collider> _grabColliders = new();
        private readonly HashSet<Holder> _holders = new();
        private readonly HashSet<Holder> _focusingHolders = new();
        private Transform _initialParent;
        private bool _initialKinematicState;
        private bool _initialGravityState;
        private CollisionDetectionMode _initialCollisionMode;
        private bool _initialized;
        public event Action<Holdable> OnCaptured;
        public event Action<Holdable> OnReleased;
        public event Action<Holdable> OnFocused;
        public event Action<Holdable> OnUnfocused;
        public Rigidbody Rigidbody => _rb;
        public Interactable Interactable => _interactable;
        public MovableObject MovableObject => _movableObject;
        public Respawner Respawner => _respawner;
        public HandPoserManager HandPoserManager => _useHandPoserManager ? _handPoserManager : null;
        public bool AllowProceduralPoses => _allowProceduralPoses;
        public bool CaptureKinematically => _captureKinematically;
        public GraspLevel GraspLevel => _graspLevel;
        public float HandToObjectReleaseDistance => _handToObjectReleaseDistance;
        public float HandToTargetReleaseDistance => _handToTargetReleaseDistance;
        public float HandToTargetVibrationDistance => _handToTargetVibrationDistance;
        public AudioClip ClipGrab => _clipGrab;
        public AudioClip ClipRelease => _clipRelease;
        public IReadOnlyList<Collider> Colliders => _colliders;
        public bool IsHeld => _holders.Count > 0;
        public bool IsFocused => _focusingHolders.Count > 0;
        public IReadOnlyCollection<Holder> Holders => _holders;
        public static Holdable FromCollider(Collider collider)
        {
            if (collider == null)
                return null;
            if (s_ColliderLookup.TryGetValue(collider, out Holdable holdable))
                return holdable;
            return collider.attachedRigidbody != null ? collider.attachedRigidbody.GetComponent<Holdable>() : null;
        }

        private void Awake() => Initialize();
        private void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;
            _rb = GetComponent<Rigidbody>();
            _interactable = GetComponent<Interactable>();
            _movableObject = GetComponent<MovableObject>();
            _respawner = GetComponent<Respawner>();
            if (_handPoserManager == null)
                _handPoserManager = GetComponentInChildren<HandPoserManager>();
            _initialParent = transform.parent;
            _initialKinematicState = _rb.isKinematic;
            _initialGravityState = _rb.useGravity;
            _initialCollisionMode = _rb.collisionDetectionMode;
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
            {
                if (c.attachedRigidbody != _rb)
                    continue;
                if (_collidersContainer != null && c.transform.IsChildOf(_collidersContainer))
                    _grabColliders.Add(c);
                else if (!c.isTrigger)
                    _colliders.Add(c);
            }

            if (_collidersContainer == null)
                CreateGrabColliders();
            foreach (Collider c in _grabColliders)
                s_ColliderLookup[c] = this;
        }

        private void OnDestroy()
        {
            foreach (Collider c in _grabColliders)
                s_ColliderLookup.Remove(c);
            ForceRelease();
        }

        private void OnDisable() => ForceRelease();
        private void CreateGrabColliders()
        {
            _collidersContainer = new GameObject("GrabColliders").transform;
            _collidersContainer.SetParent(transform, false);
            _collidersContainer.gameObject.layer = PhysicsLayers.Grabbable;
            foreach (Collider source in _colliders)
            {
                var go = new GameObject($"Grab_{source.name}")
                {layer = PhysicsLayers.Grabbable};
                go.transform.SetParent(source.transform, false);
                Vector3 scale = source.transform.lossyScale;
                float padding = _grabTriggerPadding / Mathf.Max(1e-4f, Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)));
                Collider copy = source switch
                {
                BoxCollider box => CopyBox(go, box, padding), SphereCollider sphere => CopySphere(go, sphere, padding), CapsuleCollider capsule => CopyCapsule(go, capsule, padding), MeshCollider mesh => CopyMesh(go, mesh), _ => null, }

                ;
                if (copy == null)
                {
                    Destroy(go);
                    continue;
                }

                copy.isTrigger = true;
                _grabColliders.Add(copy);
            }
        }

        private static Collider CopyBox(GameObject go, BoxCollider box, float padding)
        {
            var c = go.AddComponent<BoxCollider>();
            c.center = box.center;
            c.size = box.size + Vector3.one * (padding * 2f);
            return c;
        }

        private static Collider CopySphere(GameObject go, SphereCollider sphere, float padding)
        {
            var c = go.AddComponent<SphereCollider>();
            c.center = sphere.center;
            c.radius = sphere.radius + padding;
            return c;
        }

        private static Collider CopyCapsule(GameObject go, CapsuleCollider capsule, float padding)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.center = capsule.center;
            c.direction = capsule.direction;
            c.radius = capsule.radius + padding;
            c.height = capsule.height + padding * 2f;
            return c;
        }

        private static Collider CopyMesh(GameObject go, MeshCollider mesh)
        {
            var c = go.AddComponent<MeshCollider>();
            c.sharedMesh = mesh.sharedMesh;
            c.convex = true;
            return c;
        }

        public bool AllowsHolder(HolderType type) => !_restrictAllowedHolderTypes || _allowedHolderTypes.Contains(type);
        public bool AllowsSnap(SnapType type) => !_restrictAllowedSnapTypes || _allowedSnapTypes.Contains(type);
        /// <summary>Distance from a point to the closest physical collider surface.</summary>
        public float DistanceTo(Vector3 point)
        {
            float best = float.PositiveInfinity;
            foreach (Collider c in _colliders)
            {
                if (c == null || !c.enabled)
                    continue;
                Vector3 closest = c is MeshCollider
                {
                convex: false
                }

                ? c.ClosestPointOnBounds(point) : c.ClosestPoint(point);
                best = Mathf.Min(best, Vector3.Distance(point, closest));
            }

            return float.IsInfinity(best) ? Vector3.Distance(point, transform.position) : best;
        }

        public void Focus(Holder holder, bool focus)
        {
            bool wasFocused = IsFocused;
            if (focus)
                _focusingHolders.Add(holder);
            else
                _focusingHolders.Remove(holder);
            if (!wasFocused && IsFocused)
                OnFocused?.Invoke(this);
            else if (wasFocused && !IsFocused)
                OnUnfocused?.Invoke(this);
            if (_interactable != null)
                _interactable.Focus(IsFocused);
        }

        public void Capture(Holder holder)
        {
            bool first = _holders.Count == 0;
            _holders.Add(holder);
            if (!first)
                return;
            if (_makeSpeculativeOnGrab && !_rb.isKinematic)
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (_captureKinematically || holder.CaptureKinematically)
            {
                _rb.isKinematic = true;
                if (holder.HoldableContainer != null)
                    transform.SetParent(holder.HoldableContainer, true);
            }

            if (_handPoserManager != null)
                _handPoserManager.SetGrabbed(true);
            OnCaptured?.Invoke(this);
        }

        public void Release(Holder holder)
        {
            if (!_holders.Remove(holder) || _holders.Count > 0)
                return;
            RefreshPhysics();
            if (_handPoserManager != null)
                _handPoserManager.SetGrabbed(false);
            OnReleased?.Invoke(this);
        }

        /// <summary>Makes every holder drop this object.</summary>
        public void ForceRelease()
        {
            if (_holders.Count == 0)
                return;
            foreach (Holder holder in new List<Holder>(_holders))
                if (holder != null && holder.CapturedObject == this)
                    holder.ForceRelease();
            _holders.Clear();
            RefreshPhysics();
        }

        public void RefreshPhysics()
        {
            if (_rb == null)
                return;
            if (transform.parent != _initialParent)
                transform.SetParent(_initialParent, true);
            _rb.isKinematic = _initialKinematicState;
            _rb.useGravity = _initialGravityState;
            _rb.collisionDetectionMode = _initialCollisionMode;
        }

        public void SetInitialParent(Transform newParent) => _initialParent = newParent;
        /// <summary>Physics state restored on release, for objects that switch between fixed and loose.</summary>
        public void SetRestPhysics(bool kinematic, bool gravity)
        {
            Initialize();
            _initialKinematicState = kinematic;
            _initialGravityState = gravity;
        }

        public void SetAllowedSnaps(params SnapType[] types)
        {
            _restrictAllowedSnapTypes = true;
            _allowedSnapTypes = new List<SnapType>(types);
        }

        public void ShowHandPose(HandSide side, bool show)
        {
            if (_handPoserManager != null)
                _handPoserManager.ShowHandPose(side, show);
        }
    }
}
