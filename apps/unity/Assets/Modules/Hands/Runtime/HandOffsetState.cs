using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>Passive diagnostic state; never writes tracking quality or physics targets.</summary>
    public sealed class HandOffsetState
    {
        public int Level { get; private set; }
        public bool TrackingLost { get; private set; }
        public float LostTime { get; private set; }
        float pendingSince = -1;
        public void Reset() { Level = 0; pendingSince = -1; TrackingLost = false; }
        public void Step(bool tracked, float metres, float now)
        {
            if (!tracked)
            {
                if (!TrackingLost) LostTime = now;
                TrackingLost = true;
                pendingSince = -1;
                return;
            }
            TrackingLost = false;
            int next = metres >= .15f ? 3 : metres >= .08f ? 2 : metres >= .03f ? 1 : 0;
            if (next < Level)
            {
                float exit = Level == 3 ? .14f : Level == 2 ? .07f : .02f;
                if (metres >= exit) next = Level;
            }
            if (next == Level) { pendingSince = -1; return; }
            if (next < Level) { Level = next; pendingSince = -1; return; }
            if (Level > 0) { Level = next; pendingSince = -1; return; }
            if (pendingSince < 0) pendingSince = now;
            if (now - pendingSince >= .12f) { Level = next; pendingSince = -1; }
        }
    }
}
