using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Hands
{
    /// <summary>Owned, reusable geometry for palm offsets and object-side palm grip cues.</summary>
    public sealed class HandFeedbackMesh : IDisposable
    {
        readonly Vector3[] vertices;
        readonly Color[] colors;
        readonly Mesh mesh;
        readonly GameObject visual;
        Vector3 right, up, forward;
        int used;
        public HandFeedbackMesh(string name, Material material, int quads)
        {
            vertices = new Vector3[quads * 4]; colors = new Color[quads * 4];
            mesh = new Mesh { name = name }; mesh.MarkDynamic();
            var indices = new int[quads * 6];
            for (int i = 0; i < quads; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+2;
                indices[t+3]=v;indices[t+4]=v+2;indices[t+5]=v+3;
            }
            mesh.vertices=vertices;mesh.triangles=indices;
            visual = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = PhysicsLayers.UIOverlay };
            visual.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=visual.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            visual.SetActive(false);
        }
        public void Begin(Transform view) { used=0;right=view.right;up=view.up;forward=view.forward; }
        public void Ring(Vector3 centre, float radius, Color color, float[] ticks = null)
        {
            // Rectangular radial ticks; shaft dashes retain their rounded ends.
            for (int i = 0; i < 24; i++)
            {
                float alpha = ticks != null ? ticks[i] : 1;
                if (alpha <= 0) continue;
                Color tint = color; tint.a *= alpha;
                float a = Mathf.PI / 2 - i * Mathf.PI / 12;
                Vector3 radial = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                Vector3 side = Vector3.Cross(radial, forward).normalized * .001f;
                Vector3 start = centre + radial * (radius * .85f);
                Vector3 end = centre + radial * (radius * 1.15f);
                Quad(start - side, start + side, end + side, end - side, tint);
            }
        }

        public void Dash(Vector3 a, Vector3 b, Color color, float radius)
        {
            Vector3 along=(b-a).normalized,side=Vector3.Cross(along,forward).normalized;
            if(side.sqrMagnitude<.01f)side=right;
            Quad(a-side*radius,a+side*radius,b+side*radius,b-side*radius,color);
            for(int i=0;i<4;i++)
            {
                float angle=i*Mathf.PI/4,next=(i+1)*Mathf.PI/4;
                Quad(b,b+(side*Mathf.Cos(angle)+along*Mathf.Sin(angle))*radius,b+(side*Mathf.Cos(next)+along*Mathf.Sin(next))*radius,b,color);
                Quad(a,a-(side*Mathf.Cos(angle)+along*Mathf.Sin(angle))*radius,a-(side*Mathf.Cos(next)+along*Mathf.Sin(next))*radius,a,color);
            }
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {
            if(used*4>=vertices.Length)throw new InvalidOperationException("Hand feedback mesh capacity exceeded.");
            int v=used++*4;vertices[v]=a;vertices[v+1]=b;vertices[v+2]=c;vertices[v+3]=d;
            for(int i=v;i<v+4;i++)colors[i]=color;
        }
        public void Commit(Bounds bounds)
        {
            if (used == 0) { Hide(); return; }
            // Keep vertex capacity stable; only populated indices are submitted for drawing.
            mesh.SetVertices(vertices);mesh.SetColors(colors);
            mesh.SetSubMesh(0,new SubMeshDescriptor(0,used*6){vertexCount=used*4,bounds=bounds},MeshUpdateFlags.DontRecalculateBounds);
            mesh.bounds=bounds;visual.SetActive(used>0);
        }
        public void Hide()=>visual.SetActive(false);
        public void Dispose(){UnityEngine.Object.Destroy(visual);UnityEngine.Object.Destroy(mesh);}
    }
}
