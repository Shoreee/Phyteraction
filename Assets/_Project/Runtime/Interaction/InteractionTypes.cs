namespace PhysicsHands.Interaction
{
    /// <summary>Numeric values match the original enums.</summary>
    public enum HolderType
    {
        Undefined = 0,
        Hand = 10,
        Inventory = 50,
        Trash = 100,
    }

    public enum SnapType
    {
        None = 0,
        Procedural = 5,
        Grasp = 10,
        PinchIndex = 15,
    }

    public enum ObjectType
    {
        Undefined = 0,
        Cube = 10,
        Cup = 50,
        Hammer = 100,
        Crowbar = 105,
    }

    public enum InteractorType
    {
        PhysicalPalm = 0,
        PhysicalThumb = 1,
        PhysicalIndex = 2,
        PhysicalMiddle = 3,
        PhysicalRing = 4,
        PhysicalPinky = 5,
        GhostPalm = 10,
        GhostIndex = 11,
        Cursor = 100,
    }
}
