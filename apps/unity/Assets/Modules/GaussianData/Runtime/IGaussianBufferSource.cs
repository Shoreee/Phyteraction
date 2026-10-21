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

        int Count
        {
            get;
        }
    }
}
