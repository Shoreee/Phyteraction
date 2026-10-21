using UnityEngine;

namespace PhysicsHands.Dynamics
{
    /// <summary>
    /// Frame-averaged linear velocity of a transform (SteamVR-style estimator
    /// used by the original on the tracked targets and throwables).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Dynamics", "PhysicsHands.Runtime", "VelocityEstimator")]
    public class VelocityEstimator : MonoBehaviour
    {
        [Tooltip("How many frames to average over for computing velocity")]
        public int velocityAverageFrames = 5;
        public float maxVelocity = 10f;
        public bool estimateOnAwake = true;
        private Vector3[] _velocitySamples;
        private int _sampleCount;
        private bool _estimating;
        private Vector3 _previousPosition;
        private void Awake()
        {
            _velocitySamples = new Vector3[Mathf.Max(1, velocityAverageFrames)];
            if (estimateOnAwake)
                BeginEstimatingVelocity();
        }

        public void BeginEstimatingVelocity()
        {
            _estimating = true;
            _sampleCount = 0;
            _previousPosition = transform.position;
        }

        private void Update()
        {
            if (!_estimating || Time.deltaTime <= 0f)
                return;
            float dt = Time.deltaTime;
            Vector3 velocity = (transform.position - _previousPosition) / dt;
            _velocitySamples[_sampleCount % _velocitySamples.Length] = Vector3.ClampMagnitude(velocity, maxVelocity);
            _sampleCount++;
            _previousPosition = transform.position;
        }

        public Vector3 GetVelocityEstimate() => Average(_velocitySamples);
        private Vector3 Average(Vector3[] samples)
        {
            int n = Mathf.Min(_sampleCount, samples?.Length ?? 0);
            if (n == 0)
                return Vector3.zero;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < n; i++)
                sum += samples[i];
            return sum / n;
        }
    }
}
