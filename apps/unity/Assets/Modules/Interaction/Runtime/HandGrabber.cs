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
        private Transform _pinchDetector;
        [Tooltip("Pinch detector follows this point; defaults to the thumb/index tip midpoint.")]
        [SerializeField]
        private Transform _pinchDetectorTarget;
        [SerializeField]
        private Transform _poserDetector;
        [SerializeField]
        private float _gestureReleaseGrace = 0.07f;
        private float _releaseStarted = -1f;
        private IReadOnlyList<Holdable> _gripCandidates;
        public void BindGripCandidates(IReadOnlyList<Holdable> candidates) => _gripCandidates = candidates;
        private readonly GraspCandidateQuery _candidateQuery = new();
        private readonly PredictivePoseSelection _predictivePoses = new();
        private PredictivePose _closestPredictiveHandPose;
        private bool _canCaptureAgain = true;
        private bool _lockedByTelekinesis;
        public System.Func<bool> CaptureAllowed { private get; set; }
        public bool HasNearbyGrip => FindGraspCandidate(false) != null;
        public bool CanPreviewGrip => isActiveAndEnabled && _hand != null && _hand.GoodTracking &&
            _capturedObject == null && _canCaptureAgain && !_lockedByTelekinesis && CaptureAllowed?.Invoke() != false;
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

        private bool PinchIntent => _hand != null && _hand.TrackedHand?.IsPinching(HandFinger.Index) == true;
        private bool GraspIntent(GraspLevel level, float releaseMargin = 0) => _hand != null && _hand.ReferencePoser != null &&
            _hand.ReferencePoser.IsGrasping(3, level, releaseMargin);
        private void OnEnable()
        {
            if (_hand != null) _hand.TrackingLost += ForceRelease;
        }
        private void OnDisable()
        {
            if (_hand != null) _hand.TrackingLost -= ForceRelease;
            ForceRelease();
            ClearPoses();
        }

        private bool CanGrasp(Holdable holdable) => holdable.AllowsSnap(SnapType.Grasp);
        private void TryPinchCapture()
        {
            if (_capturedObject != null || !_canCaptureAgain || _lockedByTelekinesis)
                return;
            Vector3 pinchPoint = PinchPoint();
            Holdable best = null;
            float bestDistance = float.PositiveInfinity;
            QueryCandidates(_hand.Palm.position, .08f, true);
            foreach (Holdable h in _candidateQuery.Candidates)
            {
                if (h == null || !h.isActiveAndEnabled || !CheckIfCanHoldThis(h) || !h.AllowsSnap(SnapType.PinchIndex) || h.FindAnchor(this) == null)
                    continue;
                float d = h.DistanceTo(pinchPoint);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = h;
                }
            }

            if (best != null)
                Capture(best, false);
        }

        private void QueryCandidates(Vector3 point, float radius, bool includePalmContacts)
        {
            if (_gripCandidates == null) _candidateQuery.Gather(point, radius, includePalmContacts ? _reachableHoldables : null);
            else
            {
                _candidateQuery.Candidates.Clear();
                for (int i = 0; i < _gripCandidates.Count; i++) _candidateQuery.Candidates.Add(_gripCandidates[i]);
            }
        }
        private Holdable FindGraspCandidate(bool requireGesture = true)
        {
            if (_hand == null)
                return null;
            Vector3 point = _hand.Palm.position;
            QueryCandidates(point, .08f, true);
            Holdable best = null;
            float distance = float.PositiveInfinity;
            foreach (Holdable h in _candidateQuery.Candidates)
            {
                if (h == null || !h.isActiveAndEnabled || !CheckIfCanHoldThis(h) || !CanGrasp(h) || (requireGesture && !GraspIntent(h.GraspLevel)) || h.FindAnchor(this) == null)
                    continue;
                float d = h.DistanceTo(point);
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
                if (!GraspIntent(GraspLevel.Weak) && !PinchIntent)
                    _canCaptureAgain = true;
                if (_capturedObject == null && _canCaptureAgain && !_lockedByTelekinesis)
                {
                    if (PinchIntent)
                        TryPinchCapture();
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
                bool holdingGesture = PinchIntent || GraspIntent(_capturedObject.GraspLevel, .08f);
                if (holdingGesture)
                    _releaseStarted = -1f;
                else if (_releaseStarted < 0f)
                    _releaseStarted = Time.unscaledTime;
                else if (Time.unscaledTime - _releaseStarted >= _gestureReleaseGrace)
                    Release();
            }

            if (_pinchDetector != null)
                _pinchDetector.position = _pinchDetectorTarget != null ? _pinchDetectorTarget.position : PinchPoint();
            CheckClosestPredictiveHandPose();
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
            Capture(holdable, true);
        }

        public override bool Capture(Holdable holdable, bool distantGrab)
        {
            if (CaptureAllowed?.Invoke() == false || _hand == null || !_hand.GoodTracking ||
                (!distantGrab && (holdable == null || holdable.FindAnchor(this) == null))) return false;
            if (!base.Capture(holdable, distantGrab))
                return false;
            _releaseStarted = -1f;
            return true;
        }

        public override void Release()
        {
            if (_capturedObject == null)
                return;
            _canCaptureAgain = _hand == null || (!GraspIntent(GraspLevel.Weak) && !PinchIntent);
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
