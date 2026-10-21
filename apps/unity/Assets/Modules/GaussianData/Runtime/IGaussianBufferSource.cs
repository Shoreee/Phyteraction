using UnityEngine;

namespace PhysicsHands.GaussianData
{
    public interface IGaussianBufferSource
    {
        ComputeBuffer Source
        {
            get;
        }

        ComputeBuffer Deformed
        {
            get;
        }

        // Notify consumers after writing Deformed, including reset and GPU deformation.
        void MarkDeformed();

        int Count
        {
            get;
        }
    }
}
