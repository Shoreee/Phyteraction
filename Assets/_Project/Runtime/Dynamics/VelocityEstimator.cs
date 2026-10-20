using UnityEngine;

namespace PhysicsHands.Dynamics
{
    /// <summary>
    /// Frame-averaged linear/angular velocity of a transform (SteamVR-style estimator
    /// used by the original on the tracked targets and throwables).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class VelocityEstimator : MonoBehaviour
    {
        [Tooltip("How many frames to average over for computing velocity")]
        public int velocityAverageFrames = 5;
        [Tooltip("How many frames to average over for computing angular velocity")]
        public int angularVelocityAverageFrames = 11;
        public float maxVelocity = 10f;
        public float maxAngularVelocity = 50f;
        public bool estimateOnAwake = true;

        private Vector3[] _velocitySamples;
        private Vector3[] _angularVelocitySamples;
        private int _sampleCount;
        private bool _estimating;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _previousEstimatedVelocity;

        private void Awake()
        {
            _velocitySamples = new Vector3[Mathf.Max(1, velocityAverageFrames)];
            _angularVelocitySamples = new Vector3[Mathf.Max(1, angularVelocityAverageFrames)];
            if (estimateOnAwake) BeginEstimatingVelocity();
        }

        public void BeginEstimatingVelocity()
        {
            _estimating = true;
            _sampleCount = 0;
            _previousPosition = transform.position;
            _previousRotation = transform.rotation;
        }

        public void FinishEstimatingVelocity() => _estimating = false;

        private void Update()
        {
            if (!_estimating || Time.deltaTime <= 0f) return;

            float dt = Time.deltaTime;
            _previousEstimatedVelocity = GetVelocityEstimate();

            Vector3 velocity = (transform.position - _previousPosition) / dt;
            _velocitySamples[_sampleCount % _velocitySamples.Length] = Vector3.ClampMagnitude(velocity, maxVelocity);

            Quaternion delta = transform.rotation * Quaternion.Inverse(_previousRotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 angular = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
            _angularVelocitySamples[_sampleCount % _angularVelocitySamples.Length] = Vector3.ClampMagnitude(angular, maxAngularVelocity);

            _sampleCount++;
            _previousPosition = transform.position;
            _previousRotation = transform.rotation;
        }

        public Vector3 GetVelocityEstimate() => Average(_velocitySamples);

        public Vector3 GetAngularVelocityEstimate() => Average(_angularVelocitySamples);

        public Vector3 GetMaxVelocity()
        {
            Vector3 max = Vector3.zero;
            int n = Mathf.Min(_sampleCount, _velocitySamples.Length);
            for (int i = 0; i < n; i++)
                if (_velocitySamples[i].sqrMagnitude > max.sqrMagnitude) max = _velocitySamples[i];
            return max;
        }

        public Vector3 GetAccelerationEstimate() =>
            Time.deltaTime > 0f ? (GetVelocityEstimate() - _previousEstimatedVelocity) / Time.deltaTime : Vector3.zero;

        private Vector3 Average(Vector3[] samples)
        {
            int n = Mathf.Min(_sampleCount, samples?.Length ?? 0);
            if (n == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < n; i++) sum += samples[i];
            return sum / n;
        }
    }
}
