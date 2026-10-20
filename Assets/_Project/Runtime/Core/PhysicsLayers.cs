using UnityEngine;

namespace PhysicsHands
{
    /// <summary>
    /// Layer indices of the original project's TagManager. Kept identical so the
    /// original collision matrix (see Editor/Setup/ProjectSetup.cs) applies unchanged.
    /// </summary>
    public static class PhysicsLayers
    {
        public const int Default = 0;
        public const int UI = 5;
        public const int Flesh = 9;
        public const int Grabber = 10;
        public const int Grabbable = 11;
        public const int RightHand = 12;
        public const int LeftHand = 13;
        public const int Blockable = 15;
        public const int Blocker = 16;
        public const int Hologram = 17;
        public const int HologramInteractor = 18;
        public const int Bone = 20;
        public const int Desk = 21;
        public const int RightHandBlocker = 22;
        public const int LeftHandBlocker = 23;
        public const int PhysicalHologram = 26;

        public static int HandLayer(HandSide side) => side == HandSide.Right ? RightHand : LeftHand;

        public static bool IsHandLayer(int layer) => layer == RightHand || layer == LeftHand || layer == Flesh;

        public static LayerMask Mask(params int[] layers)
        {
            int mask = 0;
            foreach (int l in layers) mask |= 1 << l;
            return mask;
        }
    }
}
