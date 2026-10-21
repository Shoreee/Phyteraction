using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using UnityEngine;

namespace PhysicsHands.App
{
    /// <summary>Object-side approach cues. Preview never reserves an anchor or modifies grip state.</summary>
    public sealed class PalmGripFeedback : MonoBehaviour
    {
        public Camera head;
        public Material material;
        public PhysicsLabContent lab;
        public WorkbenchInteraction interaction;
        GripAnchor[] anchors;
        HandFeedbackMesh geometry;
        void OnEnable()
        {
            if (head == null || material == null || lab == null || interaction == null)
            {
                Debug.LogError($"{name}: palm grip feedback requires camera, material, lab and interaction bindings.",this);
                enabled=false;return;
            }
            anchors=lab.GetComponentsInChildren<GripAnchor>(true);
            geometry=new HandFeedbackMesh("Palm grip approach cues",material,Mathf.Max(1, interaction.grabbers.Length) * 108);
        }
        void LateUpdate()
        {
            if(geometry==null)return;
            if(!ApplicationActivity.CanSubmitGpuWork||!lab.isActiveAndEnabled||interaction.workspace.Dragging){geometry.Hide();return;}
            geometry.Begin(head.transform);
            Vector3 low=Vector3.one*float.MaxValue,high=-low;
            GripAnchor previous=null;
            foreach(var grabber in interaction.grabbers)
            {
                if(grabber==null||!grabber.CanPreviewGrip)continue;
                GripAnchor best=null;float closest=float.PositiveInfinity;
                foreach(var anchor in anchors)
                {
                    if(anchor==null||!anchor.CanPreview(grabber))continue;
                    float distance=(anchor.transform.position-grabber.Hand.Palm.position).sqrMagnitude;
                    if(distance<closest){best=anchor;closest=distance;}
                }
                if(best==null||best==previous)continue;
                previous=best;Vector3 point=best.transform.position;
                float approach=Mathf.InverseLerp(best.captureDistance*1.5f,best.captureDistance,Mathf.Sqrt(closest));
                geometry.Ring(point,.023f,new Color(1,1,1,Mathf.Lerp(.35f,1,approach)));
                low=Vector3.Min(low,point);high=Vector3.Max(high,point);
            }
            geometry.Commit(low.x==float.MaxValue?new Bounds(Vector3.zero,Vector3.one):new Bounds((low+high)*.5f,high-low+Vector3.one*.1f));
        }
        void OnDisable(){geometry?.Dispose();geometry=null;anchors=null;}
    }
}
