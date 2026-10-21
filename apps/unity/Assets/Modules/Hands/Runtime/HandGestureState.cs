using System;
using PhysicsHands.Posing;

namespace PhysicsHands.Hands
{
    internal sealed class HandGestureState
    {
        static readonly GraspLevel[] Levels = {GraspLevel.Weak, GraspLevel.Moderate, GraspLevel.Strong};
        readonly bool[] grasping = new bool[3];
        readonly Action<GraspLevel, bool> graspChanged;
        readonly Action<HandFinger, bool> pinchChanged;
        public bool IndexPinching
        {
            get;
            private set;
        }

        public bool MiddlePinching
        {
            get;
            private set;
        }

        public HandGestureState(Action<GraspLevel, bool> graspChanged, Action<HandFinger, bool> pinchChanged)
        {
            this.graspChanged = graspChanged;
            this.pinchChanged = pinchChanged;
        }

        public void UpdateGrasp(HandPoser pose, int minFingers)
        {
            for (int i = 0; i < Levels.Length; i++)
            {
                bool value = pose.IsGrasping(minFingers, Levels[i], grasping[i] ? .08f : 0f);
                if (value == grasping[i])
                    continue;
                grasping[i] = value;
                graspChanged(Levels[i], value);
            }
        }

        public void UpdatePinch(IHandPoseSource source)
        {
            bool index = source.IsPinching(HandFinger.Index);
            if (index != IndexPinching)
            {
                IndexPinching = index;
                pinchChanged(HandFinger.Index, index);
            }

            bool middle = source.IsPinching(HandFinger.Middle);
            if (middle != MiddlePinching)
            {
                MiddlePinching = middle;
                pinchChanged(HandFinger.Middle, middle);
            }
        }

        public bool IsGrasping(HandPoser pose, int minFingers, int cachedMinFingers, GraspLevel level)
        {
            if (minFingers == cachedMinFingers)
                for (int i = 0; i < Levels.Length; i++)
                    if (Levels[i] == level)
                        return grasping[i];
            return pose != null && pose.IsGrasping(minFingers, level);
        }

        public void Reset()
        {
            for (int i = 0; i < Levels.Length; i++)
            {
                if (grasping[i])
                    graspChanged(Levels[i], false);
                grasping[i] = false;
            }

            if (IndexPinching)
                pinchChanged(HandFinger.Index, false);
            if (MiddlePinching)
                pinchChanged(HandFinger.Middle, false);
            IndexPinching = MiddlePinching = false;
        }
    }
}
