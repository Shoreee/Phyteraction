using System.Collections.Generic;
using PhysicsHands.Hands;
using PhysicsHands.Posing;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>
    /// Hand holder. Captures the focused holdable when the tracked hand grasps at or above
    /// the holdable's grasp level (or pinches, for PinchIndex holdables) and releases when
    /// the grasp opens. Also drives predictive poses from nearby <see cref = "PredictivePose"/>s.
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Interaction", "PhysicsHands.Runtime", "HandGrabber")]
    public class HandGrabber : Holder
    {
        [SerializeField]
        private PhysicsHand _hand;
        [SerializeField]
        private Transform _grabDetector;
        [SerializeField]
        private Transform _pinchDetector;
        [Tooltip("Pinch detector follows this point; defaults to the thumb/index tip midpoint.")]
        [SerializeField]
        private Transform _pinchDetectorTarget;
        [SerializeField]
        private Transform _poserDetector;
        [SerializeField]
        private float _pinchCaptureDistance = 0.03f;
        [SerializeField]
        private float _graspCaptureDistance = 0.04f;
        [SerializeField]
        private float _gestureReleaseGrace = 0.07f;
        private float _releaseStarted = -1f;
        private readonly GraspCandidateQuery _candidateQuery = new();
        private readonly PredictivePoseSelection _predictivePoses = new();
        private PredictivePose _closestPredictiveHandPose;
        private bool _canCaptureAgain = true;
        private bool _capturedWithPinch;
        private bool _lockedByTelekinesis;
        public PhysicsHand Hand => _hand;
        public HandSide Side => _hand != null ? _hand.HandType : HandSide.Right;
        protected override void Awake()
        {
            base.Awake();
            _holderType = HolderType.Hand;
            if (_hand == null)
                _hand = GetComponentInParent<PhysicsHand>();
            if (_poserDetector != null)
            {
                if (!_poserDetector.TryGetComponent(out HandPoserDetector forwarder))
                    forwarder = _poserDetector.gameObject.AddComponent<HandPoserDetector>();
                forwarder.Grabber = this;
            }
        }

        protected override void Start()
        {
            base.Start();
            InitializeEvents();
        }

        private void InitializeEvents()
        {
            if (_hand == null)
                return;
            _hand.TrackingLost += ForceRelease;
            _hand.OnStartGrasping += OnStartGrasping;
            _hand.OnStopGrasping += OnStopGrasping;
            _hand.OnStartIndexPinching += OnStartIndexPinching;
            _hand.OnStopIndexPinching += OnStopIndexPinching;
        }

        private void OnDestroy()
        {
            if (_hand == null)
                return;
            _hand.TrackingLost -= ForceRelease;
            _hand.OnStartGrasping -= OnStartGrasping;
            _hand.OnStopGrasping -= OnStopGrasping;
            _hand.OnStartIndexPinching -= OnStartIndexPinching;
            _hand.OnStopIndexPinching -= OnStopIndexPinching;
        }

        private void OnDisable()
        {
            ForceRelease();
            ClearPoses();
        }

        private void OnStartGrasping(GraspLevel level) => HandleGrasping(level, true);
        private void OnStopGrasping(GraspLevel level) => HandleGrasping(level, false);
        private void HandleGrasping(GraspLevel level, bool started)
        {
            if (started)
            {
                if (_capturedObject != null || !_canCaptureAgain || _lockedByTelekinesis)
                    return;
                Holdable target = FindGraspCandidate();
                if (target != null && level >= target.GraspLevel && CanGrasp(target))
                    Capture(target);
                return;
            }

            if (level == GraspLevel.Weak && (_hand == null || !_hand.IsIndexPinching()))
                _canCaptureAgain = true;
        // Sustained opening is checked in Update; a noisy threshold crossing is not a release.
        }

        private bool CanGrasp(Holdable holdable) => holdable.AllowsSnap(SnapType.Grasp) || (holdable.AllowProceduralPoses && holdable.AllowsSnap(SnapType.Procedural));
        private void OnStartIndexPinching(PhysicsHand _) => HandlePinchingIndex(true);
        private void OnStopIndexPinching(PhysicsHand _) => HandlePinchingIndex(false);
        private void HandlePinchingIndex(bool started)
        {
            if (!started)
            {
                // Brief tip occlusion is handled by the release grace in Update.
                return;
            }

            if (_capturedObject != null || !_canCaptureAgain || _lockedByTelekinesis)
                return;
            Vector3 pinchPoint = PinchPoint();
            Holdable best = null;
            float bestDistance = _pinchCaptureDistance;
            QueryCandidates(pinchPoint, _pinchCaptureDistance, false);
            foreach (Holdable h in _candidateQuery.Candidates)
            {
                if (h == null || !h.isActiveAndEnabled || !CheckIfCanHoldThis(h) || !h.AllowsSnap(SnapType.PinchIndex))
                    continue;
                float d = h.DistanceTo(pinchPoint);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = h;
                }
            }

            if (best != null && Capture(best, false))
                _capturedWithPinch = true;
        }

        private void QueryCandidates(Vector3 point, float radius, bool includePalmContacts) => _candidateQuery.Gather(point, radius, includePalmContacts ? _reachableHoldables : null);
        private Holdable FindGraspCandidate()
        {
            if (_hand == null)
                return null;
            Vector3 point = _grabDetector != null ? _grabDetector.position : _snapJointAnchor.position;
            QueryCandidates(point, _graspCaptureDistance, true);
            Holdable best = null;
            float distance = float.PositiveInfinity;
            foreach (Holdable h in _candidateQuery.Candidates)
            {
                if (h == null || !h.isActiveAndEnabled || !CheckIfCanHoldThis(h) || !CanGrasp(h) || !_hand.IsGrasping(3, h.GraspLevel))
                    continue;
                float d = h.DistanceTo(point);
                // Trigger contacts retain their authored detector extent. Fallback queries
                // must be close to a physical surface, not merely an oversized grab trigger.
                if (!_reachableHoldables.Contains(h) && d > _graspCaptureDistance)
                    continue;
                if (d < distance)
                {
                    distance = d;
                    best = h;
                }
            }

            return best;
        }

        private Vector3 PinchPoint()
        {
            if (_hand != null && _hand.ThumbTip != null && _hand.IndexTip != null)
                return (_hand.ThumbTip.position + _hand.IndexTip.position) * 0.5f;
            return _pinchDetector != null ? _pinchDetector.position : transform.position;
        }

        protected override void Update()
        {
            base.Update();
            // Contact often arrives after the tracked hand has already closed.
            // Retry while the gesture remains active, with release hysteresis.
            if (_hand != null && _hand.TrackedHand != null && _hand.TrackedHand.GoodTracking)
            {
                if (!_hand.IsGrasping(3, GraspLevel.Weak) && !_hand.IsIndexPinching())
                    _canCaptureAgain = true;
                if (_capturedObject == null && _canCaptureAgain && !_lockedByTelekinesis)
                {
                    if (_hand.IsIndexPinching())
                        HandlePinchingIndex(true);
                    if (_capturedObject == null)
                    {
                        Holdable grasp = FindGraspCandidate();
                        if (grasp != null)
                            Capture(grasp);
                    }
                }
            }

            if (_capturedObject != null && _hand != null)
            {
                bool holdingGesture = _capturedWithPinch ? _hand.IsIndexPinching() : _hand.IsGrasping(3, _capturedObject.GraspLevel);
                if (holdingGesture)
                    _releaseStarted = -1f;
                else if (_releaseStarted < 0f)
                    _releaseStarted = Time.unscaledTime;
                else if (Time.unscaledTime - _releaseStarted >= _gestureReleaseGrace)
                    Release();
            }

            if (_pinchDetector != null)
                _pinchDetector.position = _pinchDetectorTarget != null ? _pinchDetectorTarget.position : PinchPoint();
            if (_focusedHoldable != null && _capturedObject == null)
                UpdateFocusedPose(_focusedHoldable);
            CheckClosestPredictiveHandPose();
        }

        private void UpdateFocusedPose(Holdable holdable)
        {
            HandPoserManager poser = holdable.HandPoserManager;
            if (poser == null || _hand == null)
                return;
            poser.UpdateSnap(Side, _hand.Wrist);
        }

        internal void AddPredictivePose(PredictivePose pose)
        {
            _predictivePoses.Add(pose);
        }

        internal void RemovePredictivePose(PredictivePose pose) => _predictivePoses.Remove(pose);
        private void CheckClosestPredictiveHandPose()
        {
            if (_hand == null)
                return;
            Vector3 origin = _poserDetector != null ? _poserDetector.position : transform.position;
            var closest = _predictivePoses.Closest(origin, _capturedObject != null);
            if (closest == _closestPredictiveHandPose)
                return;
            _closestPredictiveHandPose = closest;
            _hand.SetPredictivePose(closest != null ? closest.GetPredictiveHandPoser(Side) : null, closest != null && closest.OverrideHandTracking);
        }

        private void ClearPoses()
        {
            _predictivePoses.Clear();
            _closestPredictiveHandPose = null;
            if (_hand != null)
                _hand.SetPredictivePose(null, false);
        }

        public override void Focus(Holdable holdable, bool focus, bool showTarget)
        {
            base.Focus(holdable, focus, showTarget);
            bool highlight = _hand == null || _hand.GameSettings == null || _hand.GameSettings.SnapHighlightActive.Value;
            ShowTarget(holdable, focus && showTarget && highlight);
        }

        public void ShowTarget(Holdable holdable, bool show)
        {
            if (holdable != null)
                holdable.ShowHandPose(Side, show);
        }

        /// <summary>Telekinesis holds the object at a distance; normal grabbing is suspended.</summary>
        public void LockWithTelekinesis(Holdable holdable, bool locked)
        {
            _lockedByTelekinesis = locked;
            if (holdable != null)
                Focus(holdable, locked, false);
        }

        /// <summary>Telekinesis brought the object to the hand: capture with a snap-in tween.</summary>
        public void CaptureWithTelekinesis(Holdable holdable)
        {
            _lockedByTelekinesis = false;
            if (Capture(holdable, true))
                _capturedWithPinch = false;
        }

        public override bool Capture(Holdable holdable, bool distantGrab)
        {
            if (!base.Capture(holdable, distantGrab))
                return false;
            _capturedWithPinch = false;
            _releaseStarted = -1f;
            ShowTarget(holdable, false);
            return true;
        }

        public override void Release()
        {
            if (_capturedObject == null)
                return;
            _canCaptureAgain = _hand == null || (!_hand.IsGrasping(3, GraspLevel.Weak) && !_hand.IsIndexPinching());
            _capturedWithPinch = false;
            _releaseStarted = -1f;
            base.Release();
        }

        public override void ForceRelease()
        {
            base.ForceRelease();
            _canCaptureAgain = false;
        }
    }
}
