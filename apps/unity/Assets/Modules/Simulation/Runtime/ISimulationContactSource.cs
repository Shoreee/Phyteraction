using PhysicsHands.Dynamics;

namespace PhysicsHands.Simulation
{
    public interface ISimulationContactSource : IContactBodyPolicy
    {
        void Gather(ContactGatherer contacts, float deltaTime);
    }
}
