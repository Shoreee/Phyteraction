using PhysicsHands.Tracking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace PhysicsHands.GaussianWorld
{
    [DefaultExecutionOrder(-250)]
    public sealed class GaussianDemoControls:MonoBehaviour
    {
        public GaussianFlower flower;
        void Start()
        {
#if !UNITY_VISIONOS || UNITY_EDITOR
            if(HandInputManager.Instance!=null)HandInputManager.Instance.SetTrackingSystem(HandTrackingSystem.Simulated);
            if(Camera.main!=null){var driver=Camera.main.GetComponent<TrackedPoseDriver>();if(driver!=null)driver.enabled=false;
                Camera.main.transform.SetPositionAndRotation(new Vector3(0,1.35f,0),Quaternion.Euler(12,0,0));}
#endif
        }
        void Update(){if(Keyboard.current?.backspaceKey.wasPressedThisFrame==true&&flower!=null)flower.ResetSimulation();}
    }
}
