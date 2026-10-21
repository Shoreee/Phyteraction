using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using UnityEngine;

namespace PhysicsHands.App
{
    /// <summary>Application policy joining object grips and workbench placement.</summary>
    [DefaultExecutionOrder(-400)]
    public sealed class WorkbenchInteraction : MonoBehaviour
    {
        public Workspace workspace;
        public PhysicsLabContent labContent;
        Holdable[] labGrips;
        public HandGrabber[] grabbers = System.Array.Empty<HandGrabber>();
        public Behaviour[] remoteAssists = System.Array.Empty<Behaviour>();
        bool[] priorEnabled;
        bool lab;
        void OnEnable()
        {
            foreach (HandGrabber grabber in grabbers)
                if (grabber != null) grabber.CaptureAllowed = CanCapture;
            if (workspace != null) workspace.CanDragHand = CanDrag;
        }
        bool CanCapture() => workspace == null || !workspace.Dragging;
        bool CanDrag(PhysicsHand hand)
        {
            if (lab && hand != null && hand.MovableContactActive) return false;
            foreach (HandGrabber grabber in grabbers)
                if (grabber != null && grabber.Hand == hand && (grabber.IsHolding || lab && grabber.HasNearbyGrip)) return false;
            return true;
        }
        public void ReleaseHands()
        {
            foreach (HandGrabber grabber in grabbers)
                if (grabber != null) grabber.ForceRelease();
        }
        public void SetLab(bool enabled)
        {
            if (lab == enabled) return;
            if (enabled && labGrips == null) labGrips = labContent.GetComponentsInChildren<Holdable>(true);
            foreach (HandGrabber grabber in grabbers)
                if (grabber != null) grabber.BindGripCandidates(enabled ? labGrips : null);
            if (enabled)
            {
                priorEnabled = new bool[remoteAssists.Length];
                for (int i = 0; i < remoteAssists.Length; i++)
                    if (remoteAssists[i] != null) { priorEnabled[i] = remoteAssists[i].enabled; remoteAssists[i].enabled = false; }
            }
            else
            {
                for (int i = 0; i < remoteAssists.Length; i++)
                    if (remoteAssists[i] != null) remoteAssists[i].enabled = priorEnabled[i];
                priorEnabled = null;
            }
            lab = enabled;
        }
        void OnDisable()
        {
            ReleaseHands();
            SetLab(false);
            foreach (HandGrabber grabber in grabbers)
                if (grabber != null) grabber.CaptureAllowed = null;
            if (workspace != null) workspace.CanDragHand = null;
        }
    }
}
