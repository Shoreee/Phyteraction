namespace PhysicsHands.Hands
{
    internal sealed class HandTrackingState
    {
        public bool Good
        {
            get;
            private set;
        }

        public bool Initialized
        {
            get;
            private set;
        }

        public bool Transition(bool good, out bool firstAcquisition)
        {
            firstAcquisition = false;
            if (good == Good)
                return false;
            Good = good;
            if (good && !Initialized)
            {
                Initialized = true;
                firstAcquisition = true;
            }

            return true;
        }
    }
}
