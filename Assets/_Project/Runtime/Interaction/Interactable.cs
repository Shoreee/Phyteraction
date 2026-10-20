using System;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>Common physics setup and focus/highlight state for interactive objects.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Interactable : MonoBehaviour
    {
        [SerializeField] private ObjectType _objectType = ObjectType.Undefined;
        [SerializeField] private float _ringScale = 0.1f;
        [SerializeField] private bool _canBeFocused = true;
        [SerializeField] private Transform _customMassCenter;
        [SerializeField] private Transform _ringPosition;
        [SerializeField] private bool _overrideSolverIterations = true;
        [SerializeField] private int _solverIterations = 6;
        [Tooltip("Optional renderers tinted while focused.")]
        [SerializeField] private Renderer[] _highlightRenderers = Array.Empty<Renderer>();
        [SerializeField] private Color _highlightColor = new(1f, 0.863f, 0f, 1f);

        private static readonly int s_EmissionColor = Shader.PropertyToID("_EmissionColor");
        private Rigidbody _rb;
        private MaterialPropertyBlock _block;
        private bool _focused;
        private bool _highlighted;

        public event Action<Interactable> OnFocused;
        public event Action<Interactable> OnUnfocused;
        public event Action<Interactable> OnHighlight;
        public event Action<Interactable> OnUnhighlight;

        public ObjectType ObjectType => _objectType;
        public float RingScale => _ringScale;
        public Transform RingPosition => _ringPosition != null ? _ringPosition : transform;
        public Rigidbody Rigidbody => _rb;
        public bool Focused => _focused;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_overrideSolverIterations) _rb.solverIterations = _solverIterations;
        }

        private void Start()
        {
            if (_customMassCenter != null) _rb.centerOfMass = transform.InverseTransformPoint(_customMassCenter.position);
        }

        public void Focus(bool focus)
        {
            if (!_canBeFocused || focus == _focused) return;
            _focused = focus;
            (focus ? OnFocused : OnUnfocused)?.Invoke(this);
            if (focus) Highlight();
            else Unhighlight();
        }

        public void Highlight()
        {
            if (_highlighted) return;
            _highlighted = true;
            SetEmission(_highlightColor * 0.25f);
            OnHighlight?.Invoke(this);
        }

        public void Unhighlight()
        {
            if (!_highlighted) return;
            _highlighted = false;
            SetEmission(Color.black);
            OnUnhighlight?.Invoke(this);
        }

        private void SetEmission(Color color)
        {
            _block ??= new MaterialPropertyBlock();
            foreach (Renderer r in _highlightRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(s_EmissionColor, color);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
