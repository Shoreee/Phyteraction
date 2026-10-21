using PhysicsHands.Simulation;
using PhysicsHands.Tracking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace PhysicsHands.App
{
    [DefaultExecutionOrder(-250)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "GaussianDemoControls")]
    public sealed class GaussianDemoControls : MonoBehaviour
    {
        public ModalSimulation flower;
        public HandInputManager tracking;
        public Camera headCamera;
        void Start()
        {
#if !UNITY_VISIONOS || UNITY_EDITOR
            if (tracking != null)
                tracking.SetTrackingSystem(HandTrackingSystem.Simulated);
            if (headCamera != null)
            {
                var driver = headCamera.GetComponent<TrackedPoseDriver>();
                if (driver != null)
                    driver.enabled = false;
                headCamera.transform.SetPositionAndRotation(new Vector3(0, 1.35f, 0), Quaternion.Euler(12, 0, 0));
            }
#endif
        }

        void Update()
        {
            if (Keyboard.current?.backspaceKey.wasPressedThisFrame == true && flower != null)
                flower.ResetSimulation();
        }
    }
}
