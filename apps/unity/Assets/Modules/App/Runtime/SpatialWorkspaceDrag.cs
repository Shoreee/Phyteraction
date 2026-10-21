using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_VISIONOS || UNITY_EDITOR
using UnityEngine.XR.VisionOS.InputDevices;
#endif
namespace PhysicsHands.App
{
    internal sealed class SpatialWorkspaceDrag
    {
        int gazeId = -1, lastGazeId = -1;
        Vector3 grabPoint, grabOrigin;
        internal bool Dragging => gazeId >= 0;
        bool Hits(Ray ray, Collider panel, float gazeTolerance)
        {
            if (panel.Raycast(ray, out _, 5))
                return true;
            Vector3 near = panel.ClosestPoint(ray.origin + ray.direction * Vector3.Distance(ray.origin, panel.bounds.center));
            return Vector3.Angle(ray.direction, near - ray.origin) < gazeTolerance;
        }

        // True while a spatial pointer device exists (visionOS); it then owns grabbing.
        internal bool Update(Collider panel, Transform head, Transform workspace, float gazeTolerance)
        {
#if UNITY_VISIONOS || UNITY_EDITOR
            var device = InputSystem.GetDevice<VisionOSSpatialPointerDevice>();
            if (device == null || panel == null) return false;
            // Pointer poses are in session space: the camera's parent (XR origin offset).
            var session = head != null ? head.parent : null;
            Vector3 World(Vector3 p) => session != null ? session.TransformPoint(p) : p;
            Vector3 Direction(Vector3 d) => session != null ? session.TransformDirection(d) : d;
            if (gazeId >= 0)
            {
                bool held = false;
                foreach (var input in device.inputs)
                {
                    if (input.interactionId.ReadValue() != gazeId || !input.isInProgress) continue;
                    held = true; workspace.position = grabOrigin + (World(input.inputDevicePosition.ReadValue()) - grabPoint);
                }
                if (!held) gazeId = -1;
                return true;
            }
            bool pointerActive = false;
            foreach (var input in device.inputs)
            {
                if (!input.isInProgress) continue;
                pointerActive = true;
                int id = input.interactionId.ReadValue();
                if (id == lastGazeId) continue;
                if ((VisionOSSpatialPointerKind)input.kind.ReadValue() != VisionOSSpatialPointerKind.IndirectPinch) continue;
                if (!Hits(new Ray(World(input.startRayOrigin.ReadValue()), Direction(input.startRayDirection.ReadValue())),panel,gazeTolerance)) continue;
                lastGazeId = id;
                gazeId = id; grabPoint = World(input.inputDevicePosition.ReadValue()); grabOrigin = workspace.position;
            }
            // Some device sessions expose a spatial-pointer device without supplying a
            // usable indirect pinch. In that case head gaze plus tracked pinch still works.
            return gazeId >= 0 || pointerActive;
#else
            return false;
#endif
        }
    }
}
