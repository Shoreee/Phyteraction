namespace PhysicsHands
{
    public enum HandSide
    {
        Left = 0,
        Right = 1,
    }

    public enum HandFinger
    {
        Thumb = 0,
        Index = 1,
        Middle = 2,
        Ring = 3,
        Pinky = 4,
    }

    /// <summary>Numeric values match the original game's GraspLevel enum.</summary>
    public enum GraspLevel
    {
        None = 0,
        Weak = 5,
        Moderate = 10,
        Strong = 15,
    }

    public enum PredictiveHandPose
    {
        None = 0,
        Default = 1,
        OpenHand = 5,
        FlatHand = 7,
        Pointing = 10,
    }

    public static class HandSideExtensions
    {
        public static HandSide Other(this HandSide side) => side == HandSide.Left ? HandSide.Right : HandSide.Left;
    }
}
