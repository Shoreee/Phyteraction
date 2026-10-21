using System.Collections.Generic;
using PhysicsHands.Posing;
using UnityEngine;

namespace PhysicsHands.Hands
{
    [System.Serializable]
    internal sealed class HandSettingsProfiles
    {
        public HandSettings Hand;
        public HumanoidSettings Humanoid;
        public PhysicsSetup Physics;
        public GameSettings Game;
    }

    [System.Serializable]
    internal sealed class HandRigBindings
    {
        public Transform HandAnchor;
        public HandPoser ReferencePoser;
        public HandPoser VisualPoser;
        public DetachableHand DetachableHand;
        public Transform Physics;
        public Transform GhostHand;
        public Rigidbody PoserDetector;
        public List<PhysicsFinger> Fingers = new();
        public Transform Wrist;
        public Transform ThumbTip;
        public Transform IndexTip;
        public Transform MiddleTip;
        public Transform RingTip;
        public Transform PinkyTip;
    }

    [System.Serializable]
    internal sealed class HandVisualBindings
    {
        public SkinnedMeshRenderer MeshSkin;
        public SkinnedMeshRenderer MeshBones;
        public Renderer GhostRenderer;
        public Renderer RedOutline;
        public Material MaterialDefault;
        public Material MaterialTrackingLost;
        public Material MaterialGhostHand;
        public AudioSource AudioSource;
        public AudioClip FingerTouchClip;
    }

    [System.Serializable]
    internal sealed class HandBehaviorSettings
    {
        public HandSide HandType = HandSide.Right;
        public PredictiveHandPose EnforcePose = PredictiveHandPose.None;
        public float MapLerpTime;
        public float SafetyDistance = 0.5f;
        public float BadTrackingSpringMultiplier = 0.2f;
        public float TrackingRecoverTime = 0.3f;
        public int GraspMinFingers = 3;
    }
}
