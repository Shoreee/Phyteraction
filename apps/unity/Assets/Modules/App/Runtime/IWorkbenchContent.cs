namespace PhysicsHands.App
{
    public interface IWorkbenchContent
    {
        bool Available { get; }
        bool SupportsRoom { get; }
        bool SupportsPaper { get; }
        string Status { get; }
        void SetContentActive(bool active);
        void ResetContent();
        void SetRoom(bool visible);
        void Replay();
    }
}
