using System;
using System.Collections;
using System.Collections.Generic;
using PhysicsHands.Dynamics;
using PhysicsHands.Posing;
using PhysicsHands.Settings;
using PhysicsHands.Tracking;
using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>
    /// Per-hand orchestrator. Each frame:
    /// 1. copies the tracked pose into <see cref="ReferencePoser"/> and moves the hand target;
    /// 2. poses <see cref="VisualPoser"/> (the physics targets) from the reference, limited by
    ///    any active grab / predictive pose;
    /// 3. raises grasp and pinch events and updates visual state.
    /// Physics bones (<see cref="DetachableHand"/>, <see cref="HoloFingerBone"/>) then chase these targets.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.HoloHand)]
    public class HoloHand : MonoBehaviour
    {
        public HandSettings HandSettings;
        [SerializeField] private HumanoidSettings _humanoidSettings;
        [SerializeField] private PhysicsSetup _physicsSetup;
        [SerializeField] private GameSettings _gameSettings;

        [SerializeField] private HandSide _handType = HandSide.Right;
        [SerializeField] private PredictiveHandPose _enforcePose = PredictiveHandPose.None;
        [Tooltip("Follows the tracked wrist; the kinematic TargetBody and TargetRotation map to it.")]
        [SerializeField] private Transform _handAnchor;
        [SerializeField] private HandPoser _referencePoser;
        [SerializeField] private HandPoser _visualPoser;
        [SerializeField] private DetachableHand _detachableHand;
        [SerializeField] private Transform _physics;
        [SerializeField] private Transform _ghostHand;

        [Space]
        public Rigidbody _poserDetector;
        [SerializeField] private float _mapLerpTime;

        [Space]
        [SerializeField] private SkinnedMeshRenderer _meshSkin;
        [SerializeField] private SkinnedMeshRenderer _meshBones;
        [SerializeField] private Renderer _ghostRenderer;
        [SerializeField] private Renderer _redOutline;
        [SerializeField] private Material _materialDefault;
        [SerializeField] private Material _materialTrackingLost;
        [SerializeField] private Material _materialGhostHand;

        [Space]
        [SerializeField] private List<HoloFinger> _fingers = new();
        [SerializeField] private Transform _wrist;
        [SerializeField] private Transform _thumbTip;
        [SerializeField] private Transform _indexTip;
        [SerializeField] private Transform _middleTip;
        [SerializeField] private Transform _ringTip;
        [SerializeField] private Transform _pinkyTip;

        [Space]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _fingerTouchClip;

        [Header("Behaviour (reimplementation parameters)")]
        [SerializeField] private float _safetyDistance = 0.5f;
        [SerializeField] private float _ghostShowDistance = 0.025f;
        [SerializeField] private float _badTrackingSpringMultiplier = 0.2f;
        [SerializeField] private float _trackingRecoverTime = 0.3f;
        [SerializeField] private int _graspMinFingers = 3;

        private readonly List<HoloFingerBone> _fingerBones = new();
        private readonly List<Collider> _boneColliders = new();
        private readonly List<Collider> _fingerBonesColliders = new();
        private readonly bool[] _grasping = new bool[3];
        private static readonly GraspLevel[] s_GraspLevels = { GraspLevel.Weak, GraspLevel.Moderate, GraspLevel.Strong };

        private TrackedHand _trackedHand;
        private HoloHand _otherHand;
        private HandPoser _predictivePose;
        private bool _predictivePoseOverride;
        private bool _isIndexPinching;
        private bool _isMiddlePinching;
        private bool _goodTracking;
        private bool _handInitialized;
        private bool _isKinematic;
        private bool _floppy;
        private float _fingerSpringMultiplier = 1f;
        private float _grabAntiGravityMultiplier;
        private float _grabDragMultiplier = 1f;
        private float _defaultScale = 1f;
        private JointEnforcer[] _directTrackingBones;
        private bool _directTracking;
#if UNITY_EDITOR
        // The stability probe drives the same path with synthetic poses on a desktop.
        public bool DirectTrackingValidationOverride { get; set; }
#endif

        public event Action<GraspLevel> OnStartGrasping;
        public event Action<GraspLevel> OnStopGrasping;
        public event Action<HoloHand> OnStartIndexPinching;
        public event Action<HoloHand> OnStopIndexPinching;
        public event Action<HoloHand> OnStartMiddlePinching;
        public event Action<HoloHand> OnStopMiddlePinching;

        public HandSide HandType => _handType;
        public HumanoidSettings HumanoidSettings => _humanoidSettings;
        public PhysicsSetup PhysicsSetup => _physicsSetup;
        public GameSettings GameSettings => _gameSettings;
        public TrackedHand TrackedHand => _trackedHand;
        public HandPoser ReferencePoser => _referencePoser;
        public HandPoser VisualPoser => _visualPoser;
        public DetachableHand DetachableHand => _detachableHand;
        public Transform HandAnchor => _handAnchor;
        public Transform GhostHand => _ghostHand;
        public Material MaterialGhostHand => _materialGhostHand;
        public IReadOnlyList<HoloFinger> Fingers => _fingers;
        public IReadOnlyList<HoloFingerBone> FingerBones => _fingerBones;
        public IReadOnlyList<Collider> BoneColliders => _boneColliders;
        public Transform Wrist => _wrist;
        public Transform ThumbTip => _thumbTip;
        public Transform IndexTip => _indexTip;
        public bool GoodTracking => _goodTracking;
        public bool IsUsingHandTracking => _trackedHand != null;
        public bool DirectTracking => _directTracking;
        public bool IsInitialized() => _handInitialized;
        public HoloHand OtherHand => _otherHand;

        /// <summary>Grab pose applied to the visual hand (set by the hand grabber).</summary>
        public HandPoser CustomPose { get; set; }
        public HandPoser CustomPoseAlternative { get; set; }
        /// <summary>Rigidbody currently held, used for anti-gravity compensation.</summary>
        public Rigidbody HeldRigidbody { get; set; }
        public float MapLerpTime { get => _mapLerpTime; set => _mapLerpTime = value; }

        public float FingerSpringMultiplier =>
            _fingerSpringMultiplier * (_floppy ? 0f : 1f) *
            (_humanoidSettings ? _humanoidSettings.FingerSpringMultiplier : 1f) *
            (_physicsSetup ? _physicsSetup.humanoidFingerSpringMultiplier : 1f) *
            (HandSettings ? HandSettings.FingerSpringScale : 1f);

        public float FingerDamperMultiplier =>
            (_floppy ? 0.2f : 1f) *
            (_humanoidSettings ? _humanoidSettings.FingerDamperMultiplier : 1f) *
            (_physicsSetup ? _physicsSetup.humanoidFingerDamperMultiplier : 1f) *
            (HandSettings ? HandSettings.FingerDamperScale : 1f);

        public float GrabAntiGravityMultiplier { get => _grabAntiGravityMultiplier; set => _grabAntiGravityMultiplier = value; }

        public float GrabDragMultiplier
        {
            get => _grabDragMultiplier;
            set
            {
                _grabDragMultiplier = value;
                _detachableHand.SetDrag(value);
            }
        }

        public void SetGrabDriveMultipliers(float spring, float damper, float angularSpring, float angularDamper) =>
            _detachableHand.SetJointGrabDriveMultiplier(spring, damper, angularSpring, angularDamper);

        public void ResetGrabMultipliers()
        {
            SetGrabDriveMultipliers(1f, 1f, 1f, 1f);
            _grabAntiGravityMultiplier = 0f;
            GrabDragMultiplier = 1f;
            _fingerSpringMultiplier = 1f;
        }

        public void SetFingerSpring(float spring) => _fingerSpringMultiplier = spring;

        /// <summary>Pose that pre-shapes the hand near an object (from a PredictivePose trigger).</summary>
        public void SetPredictivePose(HandPoser pose, bool overrideTracking)
        {
            _predictivePose = pose;
            _predictivePoseOverride = overrideTracking;
        }

        private void Awake()
        {
            // The tracked ghost is a second, translucent hand shell over the physical hand.
            // Keep its transforms for tracking/debug consumers, but draw only the physical hand.
            if (_ghostHand != null)
                foreach (Renderer renderer in _ghostHand.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            foreach (HoloFinger finger in _fingers)
            foreach (HoloFingerBone bone in finger.Bones)
            {
                _fingerBones.Add(bone);
                _fingerBonesColliders.AddRange(bone.GetColliders());
                bone.Touched += OnFingerTouched;
            }

            if (_physics != null)
                foreach (Collider c in _physics.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger) _boneColliders.Add(c);
            // GetComponentsInChildren visits the wrist before each finger's descendants.
            _directTrackingBones = _physics != null ? _physics.GetComponentsInChildren<JointEnforcer>(true) : Array.Empty<JointEnforcer>();
            _defaultScale = transform.localScale.x;
        }

        private void OnEnable()
        {
            if (_gameSettings == null) return;
            _gameSettings.HandPhysicsActive.Changed += OnHandPhysicsChanged;
            _gameSettings.FloppyFingersActive.Changed += MakeFloppy;
            MakeFloppy(_gameSettings.FloppyFingersActive.Value);
        }

        private void OnDisable()
        {
            if (_gameSettings == null) return;
            _gameSettings.HandPhysicsActive.Changed -= OnHandPhysicsChanged;
            _gameSettings.FloppyFingersActive.Changed -= MakeFloppy;
        }

        private void Start()
        {
            ConfigureShadows();
            // The distal bodies are kinematic and overlap their parent capsules.
            // Allowing same-hand collisions creates an unsatisfiable joint chain.
            for (int i = 0; i < _boneColliders.Count; i++)
                for (int j = i + 1; j < _boneColliders.Count; j++)
                    if (_boneColliders[i] != null && _boneColliders[j] != null)
                        Physics.IgnoreCollision(_boneColliders[i], _boneColliders[j], true);
            foreach (HoloHand hand in FindObjectsByType<HoloHand>())
                if (hand != this && hand._handType != _handType) _otherHand = hand;

            if (_physicsSetup != null)
            {
                int iterations = Mathf.RoundToInt(_physicsSetup.solverIterationCountMultiplier *
                    (_humanoidSettings != null ? _humanoidSettings.SolverIterationCountMultiplier : 1f));
                foreach (JointEnforcer enforcer in GetComponentsInChildren<JointEnforcer>(true))
                    enforcer.SetSolverIterations(enforcer.SolverIterationCount * Mathf.Max(1, iterations));
            }
            if (_humanoidSettings != null) SetMassMultiplier(_humanoidSettings.MassMultiplier);
            if (_gameSettings != null && !_gameSettings.HandPhysicsActive.Value) MakeKinematic(true, true);
        }

        private void Update()
        {
            _trackedHand = HandInputManager.Instance != null ? HandInputManager.Instance.GetTrackedHand(_handType) : null;
            bool direct = _trackedHand is TrackedHandXR;
#if UNITY_EDITOR
            direct |= DirectTrackingValidationOverride;
#endif
            if (direct != _directTracking)
            {
                _directTracking = direct;
                // A tracked hand is an input device. Dynamic finger joints can ring for
                // seconds after a fast reversal; kinematic bones carry contacts to the
                // simulated object while their pose remains locked to tracking.
                MakeKinematic(direct || (_gameSettings != null && !_gameSettings.HandPhysicsActive.Value), true);
            }
            CheckTrackingQuality();
            if (!_goodTracking)
            {
                UpdateVisualState();
                return;
            }
            HandleHandPose();
            CheckGrasping();
            CheckPinching();
            UpdateVisualState();
        }

        private void FixedUpdate()
        {
            if (_handInitialized) CheckPosition();
        }

        private void LateUpdate()
        {
            if (!_directTracking || !_goodTracking || _directTrackingBones == null) return;
            // Physics runs at a fixed rate, but the visible hand must use the latest XR
            // sample. Parent-first mapping keeps every finger attached to its palm.
            foreach (JointEnforcer bone in _directTrackingBones)
                if (bone != null && bone.IsKinematic) bone.MapToTarget();
        }

        private void HandleHandPose()
        {
            HandPoser tracked = _trackedHand.ReferenceHandPoser;
            if (tracked == null || _referencePoser == null) return;

            _referencePoser.MapWrist(tracked, 0f);
            bool mirrored = _gameSettings != null && _gameSettings.MirroredFingersActive.Value && _otherHand?._trackedHand != null;
            _referencePoser.ForceMapTo(mirrored ? _otherHand._trackedHand.ReferenceHandPoser : tracked);

            if (_goodTracking && _handAnchor != null)
                _handAnchor.SetPositionAndRotation(_referencePoser.Wrist.position, _referencePoser.Wrist.rotation);

            HandPoser adjusted = CustomPose;
            bool ignoreFreedom = CustomPose != null;
            if (adjusted == null && _enforcePose != PredictiveHandPose.None && PredefinedHandPoseManager.Instance != null)
                adjusted = PredefinedHandPoseManager.Instance.GetPose(_enforcePose, _handType);
            if (adjusted == null)
            {
                adjusted = _predictivePose;
                ignoreFreedom = _predictivePoseOverride;
            }

            if (_visualPoser == null) return;
            _visualPoser.IgnoreFingersFreedom(ignoreFreedom);
            _visualPoser.MapTo(_referencePoser, adjusted, false, true, _mapLerpTime);
        }

        private void CheckTrackingQuality()
        {
            bool good = _trackedHand != null && _trackedHand.GoodTracking;
            if (good == _goodTracking) return;
            _goodTracking = good;

            if (good)
            {
                if (!_handInitialized)
                {
                    _handInitialized = true;
                    if (_handAnchor != null && _referencePoser != null && _referencePoser.Wrist != null)
                        _handAnchor.SetPositionAndRotation(_referencePoser.Wrist.position, _referencePoser.Wrist.rotation);
                    StartCoroutine(InstantMapNextFixedStep());
                }
                _detachableHand.SetTrackingQualityDriveMultiplier(1f, 1f, _trackingRecoverTime);
            }
            else
            {
                foreach (var grabber in GetComponentsInChildren<PhysicsHands.Interaction.HandGrabber>(true))
                    grabber.ForceRelease();
                for (int i = 0; i < _grasping.Length; i++)
                {
                    if (_grasping[i]) OnStopGrasping?.Invoke(s_GraspLevels[i]);
                    _grasping[i] = false;
                }
                if (_isIndexPinching) OnStopIndexPinching?.Invoke(this);
                if (_isMiddlePinching) OnStopMiddlePinching?.Invoke(this);
                _isIndexPinching = _isMiddlePinching = false;
                _detachableHand.SetTrackingQualityDriveMultiplier(_badTrackingSpringMultiplier, 1f, 0f);
            }
        }

        private IEnumerator InstantMapNextFixedStep()
        {
            yield return new WaitForFixedUpdate();
            Reset();
        }

        private void CheckPosition()
        {
            bool safety = _gameSettings == null || _gameSettings.SafetyActive.Value;
            if (safety && _detachableHand.Attached && _detachableHand.Enforcer.GetDistanceFromTarget() > _safetyDistance) Reset();
        }

        private void CheckGrasping()
        {
            for (int i = 0; i < s_GraspLevels.Length; i++)
            {
                bool grasping = _referencePoser.IsGrasping(_graspMinFingers, s_GraspLevels[i], _grasping[i] ? .08f : 0f);
                if (grasping == _grasping[i]) continue;
                _grasping[i] = grasping;
                if (grasping) OnStartGrasping?.Invoke(s_GraspLevels[i]);
                else OnStopGrasping?.Invoke(s_GraspLevels[i]);
            }
        }

        private void CheckPinching()
        {
            bool index = _trackedHand.IsPinching(HandFinger.Index);
            if (index != _isIndexPinching)
            {
                _isIndexPinching = index;
                (index ? OnStartIndexPinching : OnStopIndexPinching)?.Invoke(this);
            }

            bool middle = _trackedHand.IsPinching(HandFinger.Middle);
            if (middle != _isMiddlePinching)
            {
                _isMiddlePinching = middle;
                (middle ? OnStartMiddlePinching : OnStopMiddlePinching)?.Invoke(this);
            }
        }

        public bool IsGrasping(int minFingerCurved, GraspLevel level)
        {
            if (minFingerCurved == _graspMinFingers)
                for (int i = 0; i < s_GraspLevels.Length; i++)
                    if (s_GraspLevels[i] == level) return _grasping[i];
            return _referencePoser != null && _referencePoser.IsGrasping(minFingerCurved, level);
        }

        public GraspLevel GetGraspLevel() => _referencePoser != null ? _referencePoser.GetGraspLevel() : GraspLevel.None;
        public float GetGranularHandGraspLevel() => _referencePoser != null ? _referencePoser.GetGranularGraspAmount() : 0f;
        public bool IsPinching(HandFinger finger) => _trackedHand != null && _trackedHand.IsPinching(finger);
        public bool IsIndexPinching() => _isIndexPinching;
        public bool IsMiddlePinching() => _isMiddlePinching;
        public bool IsFacingHead() => _trackedHand != null && _trackedHand.IsFacingHead();
        public float GetIndexPinchingDistance() => TipDistance(_indexTip);
        public float GetMiddlePinchingDistance() => TipDistance(_middleTip);
        public Transform GetIndexTip() => _indexTip;
        public bool IsIndexFlexed() => _referencePoser.IsIndexFlexed();
        public bool IsMiddleFlexed() => _referencePoser.IsMiddleFlexed();
        public bool IsRingFlexed() => _referencePoser.IsRingFlexed();
        public bool IsPinkyFlexed() => _referencePoser.IsPinkyFlexed();
        public bool IsThumbMiddleFlexed() => _referencePoser.IsThumbMiddleFlexed();
        public float GetIndexProximalAngle() => _referencePoser.GetIndexProximalAngle();
        public Transform GetIndexProximal() => _referencePoser.GetIndexProximal();
        public float GetPinkyProximalAngle() => _referencePoser.GetPinkyProximalAngle();

        public float DistanceFromHead()
        {
            Camera head = Camera.main;
            return head != null && _wrist != null ? Vector3.Distance(head.transform.position, _wrist.position) : 0f;
        }

        private float TipDistance(Transform tip) =>
            _thumbTip != null && tip != null ? Vector3.Distance(_thumbTip.position, tip.position) : float.PositiveInfinity;

        public void UpdateVisualState()
        {
            bool warn = _gameSettings == null || _gameSettings.BadTrackingWarningActive.Value;
            bool lost = warn && _handInitialized && !_goodTracking;
            if (_meshSkin != null)
            {
                Material material = lost && _materialTrackingLost != null ? _materialTrackingLost : _materialDefault;
                if (material != null && _meshSkin.sharedMaterial != material) _meshSkin.sharedMaterial = material;
            }

            bool outlines = _gameSettings == null || _gameSettings.OutlinesActive.Value;
            if (_redOutline != null) _redOutline.enabled = lost && outlines;

            if (_ghostRenderer != null)
            {
                _ghostRenderer.enabled = false;
            }
        }

        /// <summary>Only the visible physical surface casts a hand shadow.</summary>
        public void ConfigureShadows()
        {
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var physical = _meshBones != null && _meshBones.enabled ? _meshBones : _meshSkin;
            if (physical != null)
            {
                physical.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                physical.receiveShadows = true;
            }
        }

        public void SetMaterial(Material material)
        {
            if (_meshSkin != null) _meshSkin.sharedMaterial = material;
        }

        public void ShowBones(bool show)
        {
            if (_meshBones != null) _meshBones.enabled = show;
            ConfigureShadows();
        }

        private void OnHandPhysicsChanged(bool active) => MakeKinematic(_directTracking || !active, true);

        public void MakeKinematic(bool kinematic, bool overrideState)
        {
            if (_directTracking) kinematic = true;
            if (!overrideState && _gameSettings != null && !_gameSettings.HandPhysicsActive.Value) kinematic = true;
            _isKinematic = kinematic;
            foreach (JointEnforcer enforcer in GetComponentsInChildren<JointEnforcer>(true)) enforcer.MakeKinematic(kinematic);
        }

        public bool IsKinematic => _isKinematic;

        public void EnableCollisions(bool enable)
        {
            foreach (Collider c in _boneColliders) c.enabled = enable;
        }

        public void MakeFloppy(bool floppy) => _floppy = floppy;

        public void SetMassMultiplier(float multiplier)
        {
            _detachableHand.SetMassMultiplier(multiplier);
            foreach (HoloFingerBone bone in _fingerBones) bone.SetMassMultiplier(multiplier);
        }

        public void MakeAllBoneCollidersIgnoreColliders(IReadOnlyList<Collider> colliders, bool ignore) =>
            IgnoreColliders(_boneColliders, colliders, ignore);

        public void MakeFingerBoneCollidersIgnoreColliders(IReadOnlyList<Collider> colliders, bool ignore) =>
            IgnoreColliders(_fingerBonesColliders, colliders, ignore);

        public void MakeFingersIgnoreCollidersForDuration(IReadOnlyList<Collider> colliders, float time)
        {
            MakeFingerBoneCollidersIgnoreColliders(colliders, true);
            StartCoroutine(RestoreCollisionsAfter(colliders, time));
        }

        private IEnumerator RestoreCollisionsAfter(IReadOnlyList<Collider> colliders, float time)
        {
            yield return new WaitForSeconds(time);
            MakeFingerBoneCollidersIgnoreColliders(colliders, false);
        }

        private static void IgnoreColliders(List<Collider> own, IReadOnlyList<Collider> others, bool ignore)
        {
            foreach (Collider a in own)
            foreach (Collider b in others)
                if (a != null && b != null) Physics.IgnoreCollision(a, b, ignore);
        }

        private void OnFingerTouched(HoloFingerBone bone, Collision collision)
        {
            bool sound = _gameSettings == null || _gameSettings.FingerTouchSoundActive.Value;
            if (!sound || _audioSource == null || _fingerTouchClip == null) return;
            float volume = Mathf.Clamp01(collision.relativeVelocity.magnitude * 0.5f);
            _audioSource.PlayOneShot(_fingerTouchClip, volume);
        }

        public void SetScale(float scale)
        {
            transform.localScale = Vector3.one * (_defaultScale * scale);
            AdjustJoints();
        }

        public void ResetScale() => SetScale(1f);

        public void AdjustJoints()
        {
            foreach (JointEnforcer enforcer in GetComponentsInChildren<JointEnforcer>(true)) enforcer.AdjustJoint();
        }

        public void InstantAdjustPose()
        {
            if (_visualPoser != null && _referencePoser != null) _visualPoser.ForceMapTo(_referencePoser);
        }

        public void GoInTPose()
        {
            foreach (HoloFinger finger in _fingers) finger.GoInTPose();
        }

        public void DetachFingers()
        {
            foreach (HoloFingerBone bone in _fingerBones) bone.DetachBone();
        }

        public void FreeFingers()
        {
            foreach (HoloFingerBone bone in _fingerBones) bone.FreeMovement();
        }

        public void ResetFingers()
        {
            foreach (HoloFingerBone bone in _fingerBones) bone.ResetMultipliers();
        }

        public void Reattach(bool makeKinematic) => _detachableHand.Reattach(makeKinematic);

        public void Reset()
        {
            InstantAdjustPose();
            if (_detachableHand != null) _detachableHand.InstantMapToTarget();
        }
    }
}
