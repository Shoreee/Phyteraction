using System;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_VISIONOS || UNITY_EDITOR
using UnityEngine.XR.VisionOS.InputDevices;
#endif
namespace PhysicsHands.App
{
    internal sealed class SpatialWorkspaceDrag
    {
        int gazeId = -1;
        readonly int[] seenIds = { -1, -1 };
        Vector3 grabPoint, grabOrigin;
        // Keep consumed IDs: cancelling must not start the same held gesture again.
        internal void Reset() => gazeId = -1;
        internal bool Dragging => gazeId >= 0;
        static bool Hits(Ray ray, Collider panel, float gazeTolerance)
        {
            if (ray.direction.sqrMagnitude < .5f) return false;
            if (panel.Raycast(ray, out _, 5)) return true;
            Vector3 near = panel.ClosestPoint(ray.origin + ray.direction * Vector3.Distance(ray.origin, panel.bounds.center));
            return Vector3.Angle(ray.direction, near - ray.origin) < gazeTolerance;
        }

        // A present spatial device owns the entire gesture, including Ended/Cancelled.
        // Falling back on its last frame resurrected a stale skeletal drag origin.
        internal bool Update(Collider panel, Transform head, Transform workspace, float gazeTolerance, Func<Vector3, bool> canStart)
        {
#if UNITY_VISIONOS || UNITY_EDITOR
            var device = InputSystem.GetDevice<VisionOSSpatialPointerDevice>();
            if (device == null) { Reset(); return false; }
            var session = head != null ? head.parent : null;
            Vector3 World(Vector3 p) => session != null ? session.TransformPoint(p) : p;
            Vector3 Direction(Vector3 d) => session != null ? session.TransformDirection(d) : d;
            // The provider reuses pointer IDs across gestures. Only an active gesture
            // is consumed; an observed release arms that pointer for its next pinch.
            for (int i = 0; i < device.inputs.Count; i++)
                if (!device.inputs[i].isInProgress) seenIds[i] = -1;
            if (gazeId >= 0)
            {
                // Other held gestures cannot take over when this owner releases.
                for (int i = 0; i < device.inputs.Count; i++)
                    if (device.inputs[i].isInProgress) seenIds[i] = device.inputs[i].interactionId.ReadValue();
                foreach (var input in device.inputs)
                {
                    if (input.interactionId.ReadValue() != gazeId || !input.isInProgress || !input.isTracked.isPressed) continue;
                    workspace.position = grabOrigin + World(input.inputDevicePosition.ReadValue()) - grabPoint;
                    return true;
                }
                Reset();
                return true;
            }
            for (int i = 0; i < device.inputs.Count; i++)
            {
                var input = device.inputs[i];
                if (!input.isInProgress) continue;
                int id = input.interactionId.ReadValue();
                if (id == seenIds[i]) continue;
                var kind = (VisionOSSpatialPointerKind)input.kind.ReadValue();
                if (kind == 0) continue; // Kind can arrive after Began.
                if (!input.isTracked.isPressed || panel == null) continue;
                Vector3 rayDirection = Direction(input.startRayDirection.ReadValue());
                if (rayDirection.sqrMagnitude < .5f) continue; // Ray metadata can arrive after kind.
                seenIds[i] = id;
                if (kind != VisionOSSpatialPointerKind.IndirectPinch) continue;
                var point = World(input.inputDevicePosition.ReadValue());
                if (canStart?.Invoke(point) == false || !Hits(new Ray(World(input.startRayOrigin.ReadValue()), Direction(input.startRayDirection.ReadValue())), panel, gazeTolerance)) continue;
                gazeId = id;
                grabPoint = point;
                grabOrigin = workspace.position;
                break;
            }
            return true;
#else
            return false;
#endif
        }
    }
}
