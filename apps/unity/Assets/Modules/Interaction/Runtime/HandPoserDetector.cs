using PhysicsHands.Posing;
using UnityEngine;

namespace PhysicsHands.Interaction
{
    /// <summary>
    /// Forwards trigger contacts of the poser detector (its own kinematic body on the
    /// Grabber layer) to the hand grabber. PredictivePose volumes must be on the Grabbable layer.
    /// </summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.Interaction", "PhysicsHands.Runtime", "HandPoserDetector")]
    public class HandPoserDetector : MonoBehaviour
    {
        public HandGrabber Grabber
        {
            get;
            set;
        }

        private void OnTriggerEnter(Collider other)
        {
            var pose = other.GetComponentInParent<PredictivePose>();
            if (pose != null && Grabber != null)
                Grabber.AddPredictivePose(pose);
        }

        private void OnTriggerExit(Collider other)
        {
            var pose = other.GetComponentInParent<PredictivePose>();
            if (pose != null && Grabber != null)
                Grabber.RemovePredictivePose(pose);
        }
    }
}
