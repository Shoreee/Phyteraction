using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>Interruptible clockwise tick reveal. Reversing preserves every tick's current opacity.</summary>
    public sealed class RadialCueSequence
    {
        public const int Count = 24;
        public const float Duration = .3f;
        public readonly float[] Ticks = new float[Count];
        public bool Complete { get; private set; }
        public bool Empty { get; private set; } = true;
        public bool Visible { get; private set; }
        float elapsed;

        public void Request(bool visible)
        {
            if (Visible == visible) return;
            Visible = visible;
            elapsed = 0;
            Complete = false;
        }

        public void Step(float dt)
        {
            elapsed += dt;
            Complete = Visible;
            Empty = true;
            float interval = Duration / Count;
            for (int i = 0; i < Count; i++)
            {
                if (elapsed >= i * interval)
                    Ticks[i] = Mathf.MoveTowards(Ticks[i], Visible ? 1 : 0, dt / interval);
                Complete &= Ticks[i] >= 1;
                Empty &= Ticks[i] <= 0;
            }
        }
    }
}
