using UnityEngine;

namespace PhysicsHands.Dynamics
{
    /// <summary>Copies pose from a reference transform every Update or FixedUpdate.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Dynamics", "PhysicsHands.Runtime", "MapTransform")]
    public class MapTransform : MonoBehaviour
    {
        [SerializeField]
        private Transform _mapToTransform;
        [SerializeField]
        private bool _local;
        [SerializeField]
        private bool _mapPosition = true;
        [SerializeField]
        private bool _mapRotation = true;
        [SerializeField]
        private bool _mapScale;
        [SerializeField]
        private bool _onFixedUpdate;
        public void SetReference(Transform reference) => _mapToTransform = reference;
        public void SetLocal(bool local) => _local = local;
        public void SetMapPosition(bool mapPosition) => _mapPosition = mapPosition;
        public void SetMapRotation(bool mapRotation) => _mapRotation = mapRotation;
        private void Update()
        {
            if (!_onFixedUpdate)
                Map();
        }

        private void FixedUpdate()
        {
            if (_onFixedUpdate)
                Map();
        }

        private void Map()
        {
            if (_mapToTransform == null)
                return;
            if (_mapPosition)
                MapPosition();
            if (_mapRotation)
                MapRotation();
            if (_mapScale)
                MapScale();
        }

        private void MapPosition()
        {
            if (_local)
                transform.localPosition = _mapToTransform.localPosition;
            else
                transform.position = _mapToTransform.position;
        }

        private void MapRotation()
        {
            if (_local)
                transform.localRotation = _mapToTransform.localRotation;
            else
                transform.rotation = _mapToTransform.rotation;
        }

        private void MapScale() => transform.localScale = _mapToTransform.localScale;
    }
}
