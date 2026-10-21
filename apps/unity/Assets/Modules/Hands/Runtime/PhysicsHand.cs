using System;
using System.Collections;
using System.Collections.Generic;
using PhysicsHands.Dynamics;
using PhysicsHands.Posing;
using PhysicsHands;
using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>
    /// Per-hand orchestrator. Each frame:
    /// 1. copies the tracked pose into <see cref = "ReferencePoser"/> and moves the hand target;
    /// 2. poses <see cref = "VisualPoser"/> (the physics targets) from the reference, limited by
    ///    any active grab / predictive pose;
    /// 3. raises grasp and pinch events and updates visual state.
    /// Physics bones (<see cref = "DetachableHand"/>, <see cref = "PhysicsFingerBone"/>) then chase these targets.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.PhysicsHand)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Hands", "PhysicsHands.Runtime", "HoloHand")]
    public class PhysicsHand : MonoBehaviour, IJointDriveContext
    {
        public HandSettings HandSettings => _profiles.Hand;
        [SerializeField]
        private HandBehaviorSettings _behavior = new();
        [SerializeField]
        private HandVisualBindings _visualBindings = new();
        [SerializeField]
        private HandRigBindings _rigBindings = new();
        [SerializeField]
        private HandSettingsProfiles _profiles = new();
        private readonly List<PhysicsFingerBone> _fingerBones = new();
        private readonly List<Collider> _boneColliders = new();
        private readonly List<Collider> _fingerBonesColliders = new();
        private IJointTargetSource _jointTargets;
        public Transform Palm => _rigBindings.DetachableHand.Palm;
        public void SetJointTargets(IJointTargetSource targets) => _jointTargets = targets;
        public void ClearJointTargets(IJointTargetSource owner)
        {
            if (ReferenceEquals(_jointTargets, owner)) _jointTargets = null;
        }
        public bool TryGetTargetPose(Rigidbody body, out Pose pose)
        {
            if (_jointTargets != null && _jointTargets.TryGetTargetPose(body, out pose)) return true;
            pose = default;
            return false;
        }
        readonly Dictionary<Rigidbody, (Vector3 normal, float time)> simulationContacts = new(32);
        public void NotifySimulationContact(Rigidbody body, Vector3 normal)
        {
            if (body == null || normal.sqrMagnitude < .5f) return;
            NotifyPhysicalContact(body);
            simulationContacts[body] = (normal, Time.fixedTime);
        }
        public bool TryGetSimulationContactNormal(Rigidbody body, out Vector3 normal)
        {
            if (simulationContacts.TryGetValue(body, out var contact) && Time.fixedTime - contact.time <= Time.fixedDeltaTime * 3)
            {
                normal = contact.normal;
                return true;
            }
            // A fingertip constraint also bounds wrist translation; otherwise every
            // unblocked bone keeps pulling the shared wrist through the neural surface.
            if (body == _rigBindings.DetachableHand.Rb)
                foreach (var sample in simulationContacts.Values)
                    if (Time.fixedTime - sample.time <= Time.fixedDeltaTime * 3)
                    {
                        normal = sample.normal;
                        return true;
                    }
            normal = Vector3.zero;
            return false;
        }
        public Vector3 GetSimulationContactOffset(Rigidbody body)
        {
            if (body == _rigBindings.DetachableHand.Rb || _rigBindings.ReferencePoser.Wrist == null) return Vector3.zero;
            Vector3 displacement = _rigBindings.DetachableHand.Rb.position - _rigBindings.ReferencePoser.Wrist.position;
            Vector3 shift = Vector3.zero;
            foreach (var sample in simulationContacts.Values)
                if (Time.fixedTime - sample.time <= Time.fixedDeltaTime * 3)
                    shift += sample.normal * Mathf.Max(0, Vector3.Dot(displacement - shift, sample.normal));
            return shift;
        }
        public void ClearSimulationContacts() => simulationContacts.Clear();
        private IHandPoseSource _trackedHand;
        private IHandPoseProvider _poseProvider;
        private PredefinedHandPoseManager _poseLibrary;
        public event Action TrackingLost;
        public void BindTracking(IHandPoseProvider provider, PhysicsHand other, PredefinedHandPoseManager library)
        {
            _poseProvider = provider;
            _otherHand = other;
            _poseLibrary = library;
        }

        public bool Owns(Collider collider) => collider != null && collider.GetComponentInParent<PhysicsHand>() == this;
        private PhysicsHand _otherHand;
        private HandPoser _predictivePose;
        private bool _predictivePoseOverride;
        private readonly HandTrackingState _trackingState = new();
        private bool _goodTracking => _trackingState.Good;
        private bool _handInitialized => _trackingState.Initialized;
        private bool _floppy;
        private bool _directTracking;
        private float _lastPhysicalContactTime = float.NegativeInfinity;
        private float _lastMovableContactTime = float.NegativeInfinity;
        public bool MovableContactActive => Time.fixedTime - _lastMovableContactTime <= Time.fixedDeltaTime * 3;
        public bool PhysicalContactActive => Time.fixedTime - _lastPhysicalContactTime <= Time.fixedDeltaTime * 3;
        public event Action<GraspLevel> OnStartGrasping;
        public event Action<GraspLevel> OnStopGrasping;
        public event Action<PhysicsHand> OnStartIndexPinching;
        public event Action<PhysicsHand> OnStopIndexPinching;
        public event Action<PhysicsHand> OnStartMiddlePinching;
        public event Action<PhysicsHand> OnStopMiddlePinching;
        public HandSide HandType => _behavior.HandType;
        public HumanoidSettings HumanoidSettings => _profiles.Humanoid;
        public PhysicsSetup PhysicsSetup => _profiles.Physics;
        public GameSettings GameSettings => _profiles.Game;
        public IHandPoseSource TrackedHand => _trackedHand;
        public HandPoser ReferencePoser => _rigBindings.ReferencePoser;
        public HandPoser VisualPoser => _rigBindings.VisualPoser;
        public DetachableHand DetachableHand => _rigBindings.DetachableHand;
        public Transform HandAnchor => _rigBindings.HandAnchor;
        public Transform GhostHand => _rigBindings.GhostHand;
        public Material MaterialGhostHand => _visualBindings.MaterialGhostHand;
        public IReadOnlyList<PhysicsFinger> Fingers => _rigBindings.Fingers;
        public IReadOnlyList<PhysicsFingerBone> FingerBones => _fingerBones;
        public IReadOnlyList<Collider> BoneColliders => _boneColliders;
        public Transform Wrist => _rigBindings.Wrist;
        public Transform ThumbTip => _rigBindings.ThumbTip;
        public Transform IndexTip => _rigBindings.IndexTip;
        public bool GoodTracking => _goodTracking;
        public bool IsUsingHandTracking => _trackedHand != null;
        public bool DirectTracking => _directTracking;
        public PhysicsHand OtherHand => _otherHand;
        /// <summary>Grab pose applied to the visual hand (set by the hand grabber).</summary>
        public HandPoser CustomPose
        {
            get;
            set;
        }



        public float MapLerpTime
        {
            get => _behavior.MapLerpTime;
            set => _behavior.MapLerpTime = value;
        }

        public float FingerSpringMultiplier => (_floppy ? 0f : 1f) * (_profiles.Humanoid ? _profiles.Humanoid.FingerSpringMultiplier : 1f) * (_profiles.Physics ? _profiles.Physics.humanoidFingerSpringMultiplier : 1f) * (HandSettings ? HandSettings.FingerSpringScale : 1f);
        public float FingerDamperMultiplier => (_floppy ? 0.2f : 1f) * (_profiles.Humanoid ? _profiles.Humanoid.FingerDamperMultiplier : 1f) * (_profiles.Physics ? _profiles.Physics.humanoidFingerDamperMultiplier : 1f) * (HandSettings ? HandSettings.FingerDamperScale : 1f);


        /// <summary>Pose that pre-shapes the hand near an object (from a PredictivePose trigger).</summary>
        public void SetPredictivePose(HandPoser pose, bool overrideTracking)
        {
            _predictivePose = pose;
            _predictivePoseOverride = overrideTracking;
        }

        private PhysicsHandBody _body;
        private HandPoseTargets _poseTargets;
        private HandGestureState _gestures;
        private HandPresentation _presentation;
        private void Awake()
        {
            _body = new PhysicsHandBody(transform, _rigBindings.DetachableHand, _rigBindings.Fingers, _fingerBones, _boneColliders, _fingerBonesColliders);
            _poseTargets = new HandPoseTargets(_rigBindings.ReferencePoser, _rigBindings.VisualPoser, _rigBindings.HandAnchor);
            _presentation = new HandPresentation(transform, _visualBindings.MeshSkin, _visualBindings.MeshBones, _visualBindings.GhostRenderer, _visualBindings.RedOutline, _visualBindings.MaterialDefault, _visualBindings.MaterialTrackingLost);
            _gestures = new HandGestureState((level, on) =>
            {
                if (on)
                    OnStartGrasping?.Invoke(level);
                else
                    OnStopGrasping?.Invoke(level);
            }

            , (finger, on) =>
            {
                if (finger == HandFinger.Index)
                    (on ? OnStartIndexPinching : OnStopIndexPinching)?.Invoke(this);
                else
                    (on ? OnStartMiddlePinching : OnStopMiddlePinching)?.Invoke(this);
            }

            );
            foreach (var driver in GetComponentsInChildren<PhysicsJointDriver>(true))
                driver.Bind(this);
            // The tracked ghost is a second, translucent hand shell over the physical hand.
            // Keep its transforms for tracking/debug consumers, but draw only the physical hand.
            if (_rigBindings.GhostHand != null)
                foreach (Renderer renderer in _rigBindings.GhostHand.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            foreach (PhysicsFinger finger in _rigBindings.Fingers)
                foreach (PhysicsFingerBone bone in finger.Bones)
                {
                    _fingerBones.Add(bone);
                    _fingerBonesColliders.AddRange(bone.GetColliders());
                    bone.Touched += OnFingerTouched;
                }

            if (_rigBindings.Physics != null)
                foreach (Collider c in _rigBindings.Physics.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger)
                        _boneColliders.Add(c);
        }

        private void OnEnable()
        {
            if (_profiles.Game == null)
                return;
            _profiles.Game.HandPhysicsActive.Changed += OnHandPhysicsChanged;
            _profiles.Game.FloppyFingersActive.Changed += MakeFloppy;
            MakeFloppy(_profiles.Game.FloppyFingersActive.Value);
        }

        private void OnDisable()
        {
            _jointTargets = null;
            simulationContacts.Clear();
            if (_profiles.Game == null)
                return;
            _profiles.Game.HandPhysicsActive.Changed -= OnHandPhysicsChanged;
            _profiles.Game.FloppyFingersActive.Changed -= MakeFloppy;
        }

        private void OnDestroy()
        {
            foreach (var bone in _fingerBones)
                if (bone != null)
                    bone.Touched -= OnFingerTouched;
        }

        private void Start()
        {
            ConfigureShadows();
            // Adjacent physical phalanges overlap at their joint capsules.
            // Allowing same-hand collisions creates an unsatisfiable joint chain.
            for (int i = 0; i < _boneColliders.Count; i++)
                for (int j = i + 1; j < _boneColliders.Count; j++)
                    if (_boneColliders[i] != null && _boneColliders[j] != null)
                        Physics.IgnoreCollision(_boneColliders[i], _boneColliders[j], true);
            if (_profiles.Physics != null)
            {
                int iterations = Mathf.RoundToInt(_profiles.Physics.solverIterationCountMultiplier * (_profiles.Humanoid != null ? _profiles.Humanoid.SolverIterationCountMultiplier : 1f));
                foreach (PhysicsJointDriver enforcer in GetComponentsInChildren<PhysicsJointDriver>(true))
                    enforcer.SetSolverIterations(enforcer.SolverIterationCount * Mathf.Max(1, iterations));
            }

            if (_profiles.Humanoid != null)
                SetMassMultiplier(_profiles.Humanoid.MassMultiplier);
            if (_profiles.Game != null && !_profiles.Game.HandPhysicsActive.Value)
                MakeKinematic(true, true);
        }

        private void Update()
        {
            _trackedHand = _poseProvider?.GetPoseSource(_behavior.HandType);
            bool direct = _trackedHand?.UsesJointTracking == true;
            if (direct != _directTracking)
            {
                _directTracking = direct;
                // Jointed bones remain dynamic so contact impulses displace the skeleton.
                // Distal bodies also retain their anatomical joints.
                MakeKinematic(_profiles.Game != null && !_profiles.Game.HandPhysicsActive.Value, true);
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
            if (_handInitialized)
                CheckPosition();
        }

        // Pursuit happens in PhysicsJointDriver.FixedUpdate. Never teleport a dynamic
        // bone in LateUpdate: a sweep checks translation but misses rotational contact.
        public void NotifyPhysicalContact(Rigidbody body, Collider other = null)
        {
            if (body != null)
                _lastPhysicalContactTime = Time.fixedTime;
            if (other != null && other.attachedRigidbody != null && !other.attachedRigidbody.isKinematic)
                _lastMovableContactTime = Time.fixedTime;
        }

        private void HandleHandPose()
        {
            bool mirrored = _profiles.Game != null && _profiles.Game.MirroredFingersActive.Value;
            var enforced = _behavior.EnforcePose != PredictiveHandPose.None ? _poseLibrary?.GetPose(_behavior.EnforcePose, _behavior.HandType) : null;
            _poseTargets.Apply(_trackedHand, mirrored ? _otherHand?._trackedHand : null, _goodTracking, CustomPose, enforced, _predictivePose, _predictivePoseOverride, _directTracking ? 0f : _behavior.MapLerpTime);
        }

        private void CheckTrackingQuality()
        {
            bool good = _trackedHand != null && _trackedHand.GoodTracking;
            if (!_trackingState.Transition(good, out bool firstAcquisition))
                return;
            if (good)
            {
                if (firstAcquisition)
                {
                    if (_rigBindings.HandAnchor != null && _rigBindings.ReferencePoser != null && _rigBindings.ReferencePoser.Wrist != null)
                        _rigBindings.HandAnchor.SetPositionAndRotation(_rigBindings.ReferencePoser.Wrist.position, _rigBindings.ReferencePoser.Wrist.rotation);
                    StartCoroutine(InstantMapNextFixedStep());
                }

                _rigBindings.DetachableHand.SetTrackingQualityDriveMultiplier(1f, 1f, _behavior.TrackingRecoverTime);
            }
            else
            {
                simulationContacts.Clear();
                TrackingLost?.Invoke();
                _gestures.Reset();
                _rigBindings.DetachableHand.SetTrackingQualityDriveMultiplier(_behavior.BadTrackingSpringMultiplier, 1f, 0f);
            }
        }

        private IEnumerator InstantMapNextFixedStep()
        {
            yield return new WaitForFixedUpdate();
            Reset();
        }

        private void CheckPosition()
        {
            // A tracked hand beyond a wall is an unreachable IK goal, not a reason
            // to reset the physical skeleton onto the other side of the wall.
            if (_directTracking)
                return;
            bool safety = _profiles.Game == null || _profiles.Game.SafetyActive.Value;
            if (safety && _rigBindings.DetachableHand.Attached && _rigBindings.DetachableHand.Enforcer.GetDistanceFromTarget() > _behavior.SafetyDistance)
                Reset();
        }

        private void CheckGrasping() => _gestures.UpdateGrasp(_rigBindings.ReferencePoser, _behavior.GraspMinFingers);
        private void CheckPinching() => _gestures.UpdatePinch(_trackedHand);
        public bool IsGrasping(int minFingerCurved, GraspLevel level) => _gestures.IsGrasping(_rigBindings.ReferencePoser, minFingerCurved, _behavior.GraspMinFingers, level);
        public GraspLevel GetGraspLevel() => _rigBindings.ReferencePoser != null ? _rigBindings.ReferencePoser.GetGraspLevel() : GraspLevel.None;
        public bool IsPinching(HandFinger finger) => _trackedHand != null && _trackedHand.IsPinching(finger);
        public bool IsIndexPinching() => _gestures.IndexPinching;
        public bool IsMiddlePinching() => _gestures.MiddlePinching;
        public bool IsFacingHead() => _trackedHand != null && _trackedHand.IsFacingHead();
        public bool IsIndexFlexed() => _rigBindings.ReferencePoser.IsIndexFlexed();
        public bool IsMiddleFlexed() => _rigBindings.ReferencePoser.IsMiddleFlexed();
        public bool IsRingFlexed() => _rigBindings.ReferencePoser.IsRingFlexed();
        public bool IsPinkyFlexed() => _rigBindings.ReferencePoser.IsPinkyFlexed();
        public bool IsThumbMiddleFlexed() => _rigBindings.ReferencePoser.IsThumbMiddleFlexed();
        public float GetIndexProximalAngle() => _rigBindings.ReferencePoser.GetIndexProximalAngle();
        public Transform GetIndexProximal() => _rigBindings.ReferencePoser.GetIndexProximal();
        public float GetPinkyProximalAngle() => _rigBindings.ReferencePoser.GetPinkyProximalAngle();
        public void UpdateVisualState() => _presentation.Update(_profiles.Game == null || _profiles.Game.BadTrackingWarningActive.Value, _handInitialized, _goodTracking, _profiles.Game == null || _profiles.Game.OutlinesActive.Value);
        public void ConfigureShadows() => _presentation.ConfigureShadows();
        public void SetMaterial(Material material) => _presentation.SetMaterial(material);
        public void ShowBones(bool show) => _presentation.ShowBones(show);
        private void OnHandPhysicsChanged(bool active) => MakeKinematic(!active, true);
        public void MakeKinematic(bool kinematic, bool overrideState)
        {
            if (!overrideState && _profiles.Game != null && !_profiles.Game.HandPhysicsActive.Value)
                kinematic = true;
            _body.MakeKinematic(kinematic);
        }

        public bool IsKinematic => _body.IsKinematic;
        public void EnableCollisions(bool enable) => _body.EnableCollisions(enable);
        public void MakeFloppy(bool floppy) => _floppy = floppy;
        public void SetMassMultiplier(float multiplier) => _body.SetMassMultiplier(multiplier);
        public void MakeAllBoneCollidersIgnoreColliders(IReadOnlyList<Collider> colliders, bool ignore) => _body.IgnoreAll(colliders, ignore);
        public void MakeFingerBoneCollidersIgnoreColliders(IReadOnlyList<Collider> colliders, bool ignore) => _body.IgnoreFingers(colliders, ignore);
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

        private void OnFingerTouched(PhysicsFingerBone bone, Collision collision)
        {
            bool sound = _profiles.Game == null || _profiles.Game.FingerTouchSoundActive.Value;
            if (!sound || _visualBindings.AudioSource == null || _visualBindings.FingerTouchClip == null)
                return;
            float volume = Mathf.Clamp01(collision.relativeVelocity.magnitude * 0.5f);
            _visualBindings.AudioSource.PlayOneShot(_visualBindings.FingerTouchClip, volume);
        }

        public void SetScale(float scale) => _body.SetScale(scale);
        public void ResetScale() => SetScale(1f);
        public void AdjustJoints() => _body.AdjustJoints();
        public void InstantAdjustPose()
        {
            if (_rigBindings.VisualPoser != null && _rigBindings.ReferencePoser != null)
                _rigBindings.VisualPoser.ForceMapTo(_rigBindings.ReferencePoser);
        }

        public void GoInTPose() => _body.GoInTPose();
        public void DetachFingers() => _body.DetachFingers();
        public void FreeFingers() => _body.FreeFingers();
        public void ResetFingers() => _body.ResetFingers();
        public void Reattach(bool makeKinematic) => _rigBindings.DetachableHand.Reattach(makeKinematic);
        public void Reset()
        {
            InstantAdjustPose();
            if (_rigBindings.DetachableHand != null)
                _rigBindings.DetachableHand.InstantMapToTarget();
        }
    }
}
