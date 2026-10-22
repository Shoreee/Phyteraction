using UnityEngine;

namespace PhysicsHands.GaussianData
{
    // A provenance-bearing physical prior/result, independent of mesh or Gaussian appearance.
    [CreateAssetMenu(menuName = "Physics Hands/Elastic Material")]
    public sealed class ElasticMaterialAsset : ScriptableObject
    {
        public string provenance, sourceSha256, checkpointSha256;
        public float youngs, poisson, density, simUnitMeters = 1;
        public bool IsTrained => !string.IsNullOrEmpty(checkpointSha256);
        public bool Valid => youngs > 0 && density > 0 && poisson > -1 && poisson < .5f && simUnitMeters > 0;
    }
}
