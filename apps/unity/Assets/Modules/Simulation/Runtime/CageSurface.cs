using System.Collections.Generic;
using Unity.Mathematics;

namespace PhysicsHands.Simulation
{
    internal static class CageSurface
    {
        public static int3[] Extract(ElasticCage cage)
        {
            var faces = new Dictionary<int3, int3>();
            var interior = new HashSet<int3>();
            foreach (var t in cage.tets)
            {
                Add(new int3(t.x,t.z,t.y),t.w); Add(new int3(t.x,t.y,t.w),t.z);
                Add(new int3(t.x,t.w,t.z),t.y); Add(new int3(t.y,t.z,t.w),t.x);
            }
            var result = new List<int3>();
            foreach (var f in faces) if (!interior.Contains(f.Key)) result.Add(f.Value);
            return result.ToArray();
            void Add(int3 face, int opposite)
            {
                int lo=math.min(face.x,math.min(face.y,face.z)), hi=math.max(face.x,math.max(face.y,face.z));
                var key=new int3(lo,face.x+face.y+face.z-lo-hi,hi);
                if(faces.ContainsKey(key)){interior.Add(key);return;}
                float3 a=cage.rest[face.x],b=cage.rest[face.y],c=cage.rest[face.z],d=cage.rest[opposite];
                if(math.dot(math.cross(b-a,c-a),d-a)>0)face=face.xzy;
                faces.Add(key,face);
            }
        }
    }
}
