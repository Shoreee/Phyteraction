using PhysicsHands.GaussianData;
using UnityEngine;

namespace PhysicsHands.Editor.Import
{
    [CreateAssetMenu(menuName = "Physics Hands/Flower Bake Settings")]
    public sealed class FlowerBakeSettings : ScriptableObject
    {
        public ModalModelAsset target;
        [Min(.002f)]
        public float voxelSize = .008f;
        [Min(.001f)]
        public float totalMass = .02f;
        [Min(1)]
        public float fallbackYoungs = 2e5f;
        [Min(1)]
        public int modeCount = 48;
        [Tooltip("Python command or repository-relative executable with tools/requirements.txt installed.")]
        public string pythonExecutable = "python3";
    }
}
