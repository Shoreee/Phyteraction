using UnityEngine;

namespace PhysicsHands.Powers
{
    /// <summary>Quadratic-bezier LineRenderer between two transforms with smoothed end points.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class BezierPointer : MonoBehaviour
    {
        [SerializeField] private Transform _startPoint;
        [SerializeField] private Transform _endPoint;
        [SerializeField] private float _offsetStart = 0.03f;
        [SerializeField] private float _offsetEnd = 0.03f;
        [SerializeField] private LineRenderer _lineRenderer;
        [SerializeField] private Gradient _color = new();
        [SerializeField] private AnimationCurve _curve = AnimationCurve.Linear(0f, 0.004f, 1f, 0.002f);
        [SerializeField] private float _lerpTimeStartPoint = 0.02f;
        [SerializeField] private float _lerpTimeMiddleStartPoint = 0.05f;
        [SerializeField] private float _lerpTimeMiddleEndPoint = 0.08f;
        [SerializeField] private float _lerpTimeEndPoint = 0.04f;
        [SerializeField] private int _resolution = 24;

        private Vector3 _start;
        private Vector3 _middleStart;
        private Vector3 _middleEnd;
        private Vector3 _end;
        private bool _initialized;

        public Transform StartPoint { get => _startPoint; set => _startPoint = value; }
        public Transform EndPoint { get => _endPoint; set => _endPoint = value; }

        private void Start() => Initialize();

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            if (_lineRenderer == null) _lineRenderer = GetComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = _resolution;
            SetStyle(_color, _curve);
        }

        public void SetStyle(Gradient color, AnimationCurve width)
        {
            Initialize();
            if (color != null) _lineRenderer.colorGradient = color;
            if (width != null) _lineRenderer.widthCurve = width;
        }

        public void Activate(bool activate)
        {
            Initialize();
            if (activate && !_lineRenderer.enabled) InstantUpdate();
            _lineRenderer.enabled = activate;
        }

        private void Update()
        {
            if (_lineRenderer != null && _lineRenderer.enabled) UpdateCurve(true);
        }

        public void InstantUpdate() => UpdateCurve(false);

        public void SetLerpValues(float start, float middleStart, float middleEnd, float end)
        {
            _lerpTimeStartPoint = start;
            _lerpTimeMiddleStartPoint = middleStart;
            _lerpTimeMiddleEndPoint = middleEnd;
            _lerpTimeEndPoint = end;
        }

        private void UpdateCurve(bool doLerp)
        {
            if (_startPoint == null || _endPoint == null) return;

            Vector3 start = _startPoint.position + _startPoint.forward * _offsetStart;
            Vector3 end = _endPoint.position - (_endPoint.position - start).normalized * _offsetEnd;
            float length = Vector3.Distance(start, end);
            Vector3 middle = start + _startPoint.forward * (length * 0.5f);

            _start = Smooth(_start, start, _lerpTimeStartPoint, doLerp);
            _middleStart = Smooth(_middleStart, middle, _lerpTimeMiddleStartPoint, doLerp);
            _middleEnd = Smooth(_middleEnd, middle, _lerpTimeMiddleEndPoint, doLerp);
            _end = Smooth(_end, end, _lerpTimeEndPoint, doLerp);

            for (int i = 0; i < _resolution; i++)
            {
                float t = i / (float)(_resolution - 1);
                Vector3 a = Vector3.Lerp(_start, _middleStart, t);
                Vector3 b = Vector3.Lerp(_middleEnd, _end, t);
                _lineRenderer.SetPosition(i, Vector3.Lerp(a, b, t));
            }
        }

        private static Vector3 Smooth(Vector3 current, Vector3 target, float time, bool doLerp) =>
            !doLerp || time <= 0f ? target : Vector3.Lerp(current, target, 1f - Mathf.Exp(-Time.deltaTime / time));
    }
}
