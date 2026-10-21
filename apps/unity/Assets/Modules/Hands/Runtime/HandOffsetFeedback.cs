using UnityEngine;

namespace PhysicsHands.Hands
{
    /// <summary>One passive, always-visible palm-offset arrow and label per hand.</summary>
    public sealed class HandOffsetFeedback : MonoBehaviour
    {
        public PhysicsHand hand;
        public Camera head;
        public Material material;
        public TextMesh label;
        readonly HandOffsetState state = new();
        HandFeedbackMesh geometry;
        Material textMaterial, originalTextMaterial;
        Vector3 lastTarget;
        bool hasTarget, lastLost;
        int lastCentimetres=-1,lastLevel=-1;
        void OnEnable()
        {
            if(hand==null||head==null||material==null||label==null)
            {
                Debug.LogError($"{name}: hand offset feedback requires hand, head, material and label bindings.",this);
                enabled=false;return;
            }
            geometry=new HandFeedbackMesh("Palm offset arrow",material,420);
            var renderer=label.GetComponent<MeshRenderer>();originalTextMaterial=renderer.sharedMaterial;
            textMaterial=new Material(material){name="Palm feedback text"};
            textMaterial.SetFloat("_UseFont",1);textMaterial.renderQueue=4101;
            RefreshFont(label.font);renderer.sharedMaterial=textMaterial;
            label.gameObject.layer=PhysicsLayers.UIOverlay;Font.textureRebuilt+=RefreshFont;
            state.Reset();hasTarget=false;lastCentimetres=lastLevel=-1;label.gameObject.SetActive(false);
        }
        void RefreshFont(Font font)
        {
            if(font!=null&&font==label.font&&textMaterial!=null)textMaterial.mainTexture=font.material.mainTexture;
        }
        void LateUpdate()
        {
            if(geometry==null||hand.Palm==null)return;
            if(!ApplicationActivity.CanSubmitGpuWork){geometry.Hide();label.gameObject.SetActive(false);return;}
            var source=hand.TrackedHand;
            bool tracked=source!=null&&source.GoodTracking&&source.Palm!=null;
            Vector3 start=hand.Palm.position;
            if(tracked){lastTarget=source.Palm.position;hasTarget=true;}
            float distance=hasTarget?Vector3.Distance(start,lastTarget):0;
            state.Step(tracked,distance,Time.unscaledTime);
            bool show=state.TrackingLost||state.Level>0;label.gameObject.SetActive(show);
            int centimetres=Mathf.RoundToInt(distance*100);
            if(show&&(centimetres!=lastCentimetres||state.Level!=lastLevel||state.TrackingLost!=lastLost))
            {
                label.text=state.TrackingLost?"BAD TRACKING":$"HAND OFFSET  {centimetres} cm";
                lastCentimetres=centimetres;lastLevel=state.Level;lastLost=state.TrackingLost;
            }
            Color color=state.TrackingLost||state.Level==3?Color.red:state.Level==2?Color.yellow:Color.white;
            label.color=color;label.transform.SetPositionAndRotation(start+head.transform.up*.045f,head.transform.rotation);
            if(!show){geometry.Hide();return;}
            geometry.Begin(head.transform);
            // Keep the palm marker visible during real tracking loss, even after the last-target arrow fades.
            geometry.Ring(start,.023f,color);
            float fade=state.TrackingLost?Mathf.Clamp01(1-(Time.unscaledTime-state.LostTime)/.3f):1;
            if(hasTarget&&fade>0&&distance>.035f)
            {
                color.a=fade;Vector3 direction=(lastTarget-start)/distance;
                float shaft=Mathf.Max(0,distance-.055f);Vector3 shaftStart=start+direction*.03f;
                int dashes=Mathf.Clamp(Mathf.CeilToInt(shaft/.03f),1,32);
                for(int i=0;i<dashes;i++)
                    geometry.Dash(shaftStart+direction*(shaft*i/dashes),shaftStart+direction*(shaft*(i+.45f)/dashes),color,.003f);
                Vector3 side=Vector3.Cross(direction,head.transform.forward).normalized;
                if(side.sqrMagnitude<.01f)side=head.transform.right;
                Vector3 neck=lastTarget-direction*Mathf.Min(.022f,distance*.4f);
                geometry.Quad(lastTarget,neck+side*.009f,neck-side*.009f,lastTarget,color);
            }
            geometry.Commit(new Bounds((start+lastTarget)*.5f,Vector3.one*(distance+.2f)));
        }
        void OnDisable()
        {
            Font.textureRebuilt-=RefreshFont;
            if(label!=null){label.gameObject.SetActive(false);if(textMaterial!=null)label.GetComponent<MeshRenderer>().sharedMaterial=originalTextMaterial;}
            if(textMaterial!=null)Destroy(textMaterial);textMaterial=null;
            geometry?.Dispose();geometry=null;state.Reset();
        }
    }
}
