using System;
using PhysicsHands.Hands;
using UnityEngine;

namespace PhysicsHands.Powers
{
    /// <summary>Receives pinch-cursor hover and click events (UI panels, far buttons).</summary>
    public interface IPinchCursorTarget
    {
        void OnCursorHover(PinchCursor cursor, RaycastHit hit);
        void OnCursorPinch(PinchCursor cursor, RaycastHit hit);
    }

    /// <summary>
    /// Far pointer: a ray from the chest through the thumb/index pinch point. Shows a
    /// cursor at the hit on UI/Hologram layers and sends a click when the hand pinches.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.Powers)]
    public class PinchCursor : MonoBehaviour
    {
        [SerializeField] private Transform _thumbTip;
        [SerializeField] private Transform _indexTip;
        [SerializeField] private Transform _wristBase;
        [Tooltip("Ray origin; defaults to 0.3 m below the head.")]
        [SerializeField] private Transform _chest;
        [SerializeField] private Transform _aimPoint;
        [SerializeField] private Transform _disc;
        [SerializeField] private LayerMask _layerMask = 131104;
        [SerializeField] private HoloHand _holoHand;
        [SerializeField] private float _minDistance = 0.2f;
        [SerializeField] private float _maxDistance = 3f;
        [SerializeField] private float _cursorScale = 0.02f;

        private bool _cursorVisible = true;
        private float _pinchDistance;
        private bool _wasPinching;

        public event Action<RaycastHit> Pinched;
        public bool CursorVisible => _cursorVisible;
        public float PinchDistance => _pinchDistance;

        private void Awake()
        {
            if (_holoHand == null) _holoHand = GetComponentInParent<HoloHand>();
            if (_holoHand != null)
            {
                if (_thumbTip == null) _thumbTip = _holoHand.ThumbTip;
                if (_indexTip == null) _indexTip = _holoHand.IndexTip;
            }
            Show(false);
        }

        private void Update()
        {
            if (_thumbTip == null || _indexTip == null || _holoHand == null || !_holoHand.GoodTracking)
            {
                Show(false);
                return;
            }

            UpdatePinchCenterPos();
            Vector3 origin = ChestPosition();
            Vector3 direction = (_aimPoint.position - origin).normalized;
            Vector3 start = _aimPoint.position + direction * _minDistance;

            bool pinching = _holoHand.IsIndexPinching();
            if (Physics.Raycast(start, direction, out RaycastHit hit, _maxDistance - _minDistance, _layerMask, QueryTriggerInteraction.Collide))
            {
                Show(true);
                if (_disc != null)
                {
                    _disc.SetPositionAndRotation(hit.point + hit.normal * 0.002f, Quaternion.LookRotation(-hit.normal));
                    _disc.localScale = Vector3.one * (_cursorScale * (pinching ? 0.6f : 1f));
                }

                var target = hit.collider.GetComponentInParent<IPinchCursorTarget>();
                target?.OnCursorHover(this, hit);
                if (pinching && !_wasPinching)
                {
                    target?.OnCursorPinch(this, hit);
                    Pinched?.Invoke(hit);
                }
            }
            else Show(false);

            _wasPinching = pinching;
        }

        private Vector3 ChestPosition()
        {
            if (_chest != null) return _chest.position;
            Camera head = Camera.main;
            return head != null ? head.transform.position + Vector3.down * 0.3f : transform.position;
        }

        private void UpdatePinchCenterPos()
        {
            _pinchDistance = Vector3.Distance(_thumbTip.position, _indexTip.position);
            if (_aimPoint == null)
            {
                _aimPoint = new GameObject("PinchAimPoint").transform;
                _aimPoint.SetParent(transform, false);
            }
            _aimPoint.position = (_thumbTip.position + _indexTip.position) * 0.5f;
        }

        public void Show(bool show)
        {
            if (show == _cursorVisible) return;
            _cursorVisible = show;
            if (_disc != null) _disc.gameObject.SetActive(show);
        }
    }
}
