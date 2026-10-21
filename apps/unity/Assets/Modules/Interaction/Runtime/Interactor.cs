using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>Receives touches from hand interactors (fingertip / palm trigger spheres).</summary>
    public interface IInteractorTarget
    {
        void OnInteractorEnter(Interactor interactor);
        void OnInteractorExit(Interactor interactor);
        void OnInteractorAction(Interactor interactor);
    }

    /// <summary>
    /// Kinematic trigger on a fingertip or palm (HologramInteractor layer) used for
    /// touch interactions such as buttons and UI.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Interaction", "PhysicsHands.Runtime", "Interactor")]
    public class Interactor : MonoBehaviour
    {
        public InteractorType Type = InteractorType.PhysicalIndex;
        public HandSide Side = HandSide.Right;
        public bool ActivateOnTriggerEnter = true;
        public bool FocusOnTriggerEnter = true;
        public SphereCollider Collider;
        public Transform Tip;
        private IInteractorTarget _current;
        private void Awake()
        {
            if (Collider == null)
                Collider = GetComponent<SphereCollider>();
            Collider.isTrigger = true;
            if (Tip == null)
                Tip = transform;
        }

        public float GetRadius() => Collider.radius * transform.lossyScale.x;
        private void OnTriggerEnter(Collider other)
        {
            var target = other.GetComponentInParent<IInteractorTarget>();
            if (target == null)
                return;
            _current = target;
            if (FocusOnTriggerEnter)
                target.OnInteractorEnter(this);
            if (ActivateOnTriggerEnter)
                target.OnInteractorAction(this);
        }

        private void OnTriggerExit(Collider other)
        {
            var target = other.GetComponentInParent<IInteractorTarget>();
            if (target == null)
                return;
            target.OnInteractorExit(this);
            if (target == _current)
                _current = null;
        }

        public void TriggerAction() => _current?.OnInteractorAction(this);
    }
}
