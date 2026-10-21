using UnityEngine;

namespace PhysicsHands
{
    /// <summary>Foreground permission shared by GPU producers; never requests background GPU access.</summary>
    public sealed class ApplicationActivity : MonoBehaviour
    {
        static bool paused, focused = true;
        public static int Epoch { get; private set; }
        public static bool CanSubmitGpuWork => !paused && focused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() { paused = false; focused = true; Epoch = 0; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var owner = new GameObject("Application activity");
            DontDestroyOnLoad(owner);
            owner.AddComponent<ApplicationActivity>();
        }
        void OnApplicationPause(bool value)
        {
            if (paused == value) return;
            paused = value; Epoch++;
        }
        void OnApplicationFocus(bool value)
        {
#if !UNITY_EDITOR && (UNITY_VISIONOS || UNITY_IOS || UNITY_TVOS)
            if (focused == value) return;
            focused = value; Epoch++;
#endif
        }
    }
}
