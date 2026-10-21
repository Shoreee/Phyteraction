namespace PhysicsHands.Simulation
{
    public interface ISimulationControl
    {
        bool SupportsReplay
        {
            get;
        }

        void ResetSimulation();
        void Replay();
    }
}
