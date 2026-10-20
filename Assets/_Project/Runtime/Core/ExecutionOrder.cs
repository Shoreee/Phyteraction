namespace PhysicsHands
{
    /// <summary>
    /// Update order of the hand pipeline: tracking → reference pose → visual pose →
    /// physics targets → grabbing → visuals following physics.
    /// </summary>
    public static class ExecutionOrder
    {
        public const int HandInput = -300;
        public const int TrackedHand = -200;
        public const int HandPoser = -100;
        public const int HoloHand = -50;
        public const int JointEnforcer = 0;
        public const int Holder = 50;
        public const int Powers = 100;
        public const int VisualMapping = 200;
    }
}
