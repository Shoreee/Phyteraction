using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PhysicsHands.GaussianWorld;
using UnityEditor;
using UnityEngine;

namespace PhysicsHands.Editor.GaussianWorld
{
    // Standard Graphdeco PLY only: float vertex properties, ASCII or little endian.
    // Retains DC colour and the first view-dependent SH band. Higher bands are not yet rendered.
    public static class GaussianPly
    {
        // sourceUnitsPerSimUnit maps youngs_sim's solver space to the PLY; 0 leaves the material unused.
        public static GaussianAsset Read(string path,int budget=131072,float height=.3f,float sourceUnitsPerSimUnit=0)
        {
            using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
            string Line(){var bytes=new List<byte>();byte b;while((b=reader.ReadByte())!=10)bytes.Add(b);return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');}
            if(Line()!="ply")throw new InvalidDataException("Expected PLY header.");
            string format="",element="";int count=0;var properties=new List<string>();
            for(string line=Line();line!="end_header";line=Line())
            {
                var parts=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length==0)continue;
                if(parts[0]=="format")format=parts[1];
                if(parts[0]=="element"){element=parts[1];if(element=="vertex")count=int.Parse(parts[2],CultureInfo.InvariantCulture);else if(count==0)throw new InvalidDataException("Vertices must be the first element.");}
                if(parts[0]=="property"&&element=="vertex"){
                    if(parts[1]!="float"&&parts[1]!="float32")throw new InvalidDataException("Only float GS vertex properties supported.");properties.Add(parts[2]);}
            }
            if(format!="ascii"&&format!="binary_little_endian")throw new InvalidDataException("Unsupported PLY format "+format);
            if(count<1||count>20000000)throw new InvalidDataException("Invalid vertex count.");
            int Index(string name){int i=properties.IndexOf(name);if(i<0)throw new InvalidDataException("Missing GS property: "+name);return i;}
            int x=Index("x"),y=Index("y"),z=Index("z"),op=Index("opacity");
            int[] sc={Index("scale_0"),Index("scale_1"),Index("scale_2")},rot={Index("rot_0"),Index("rot_1"),Index("rot_2"),Index("rot_3")},dc={Index("f_dc_0"),Index("f_dc_1"),Index("f_dc_2")};
            int restCount=0;while(properties.Contains($"f_rest_{restCount}"))restCount++;
            int perChannel=restCount/3;bool hasSh=restCount>=9&&restCount%3==0;
            // Optional baked material (Research/GaussianWorld/tools/bake_material.py).
            int youngs=properties.IndexOf("youngs_sim"),frozen=properties.IndexOf("frozen");
            var samples=new List<Gaussian>();var material=new List<(float youngs,bool frozen)>();var sh=new List<Vector4>();var values=new float[properties.Count];int seen=0;var rng=new System.Random(20261019);
            for(int i=0;i<count;i++)
            {
                if(format=="ascii") {var p=Line().Split(' ',StringSplitOptions.RemoveEmptyEntries);for(int j=0;j<values.Length;j++)values[j]=float.Parse(p[j],CultureInfo.InvariantCulture);}
                else for(int j=0;j<values.Length;j++)values[j]=reader.ReadSingle();
                bool finite=true;foreach(float v in values)if(float.IsNaN(v)||float.IsInfinity(v)){finite=false;break;}if(!finite)continue;
                // The isolated PhysDreamer flower has 130,812 splats, below the import budget.
                // Preserve its faint edge splats: the shader already clips their negligible pixels.
                float alpha=1/(1+Mathf.Exp(-values[op]));if(alpha<=0)continue;
                var q=new Quaternion(values[rot[1]],values[rot[2]],values[rot[3]],values[rot[0]]).normalized;
                var g=new Gaussian{center=new Vector4(values[x],values[y],values[z],1),
                    axis0=q*Vector3.right*Mathf.Exp(values[sc[0]]),axis1=q*Vector3.up*Mathf.Exp(values[sc[1]]),axis2=q*Vector3.forward*Mathf.Exp(values[sc[2]]),
                    color=new Vector4(Mathf.Clamp01(.5f+.2820947918f*values[dc[0]]),Mathf.Clamp01(.5f+.2820947918f*values[dc[1]]),Mathf.Clamp01(.5f+.2820947918f*values[dc[2]]),alpha)};
                var m=(youngs>=0?values[youngs]:0,frozen>=0&&values[frozen]>.5f);
                Vector4 s0=default,s1=default,s2=default;
                if(hasSh)
                {
                    int r=properties.IndexOf("f_rest_0"),gr=properties.IndexOf($"f_rest_{perChannel}"),b=properties.IndexOf($"f_rest_{2*perChannel}");
                    s0=new Vector4(values[r],values[r+1],values[r+2],values[gr]);
                    s1=new Vector4(values[gr+1],values[gr+2],values[b],values[b+1]);
                    s2=new Vector4(values[b+2],0,0,0);
                }
                seen++;int slot=samples.Count<budget?samples.Count:rng.Next(seen);
                if(slot<budget)
                {
                    if(slot==samples.Count){samples.Add(g);material.Add(m);if(hasSh){sh.Add(s0);sh.Add(s1);sh.Add(s2);}}
                    else{samples[slot]=g;material[slot]=m;if(hasSh){sh[slot*3]=s0;sh[slot*3+1]=s1;sh[slot*3+2]=s2;}}
                }
            }
            if(samples.Count==0)throw new InvalidDataException("No visible Gaussian vertices.");
            var bounds=new Bounds(samples[0].center,Vector3.zero);foreach(var g in samples)bounds.Encapsulate((Vector3)g.center);
            float scale=height/Mathf.Max(bounds.size.y,.0001f);Vector3 origin=new(bounds.center.x,bounds.min.y,bounds.center.z);
            for(int i=0;i<samples.Count;i++){var g=samples[i];g.center=(Vector3)(g.center-(Vector4)origin)*scale;g.center.w=1;g.axis0*=scale;g.axis1*=scale;g.axis2*=scale;samples[i]=g;}
            var asset=ScriptableObject.CreateInstance<GaussianAsset>();asset.splats=samples.ToArray();asset.sh=sh.ToArray();asset.sourceCount=count;
            if(youngs>=0)asset.youngsSim=material.ConvertAll(m=>m.youngs).ToArray();
            if(frozen>=0)asset.frozen=material.ConvertAll(m=>m.frozen).ToArray();
            asset.simUnitMeters=scale*sourceUnitsPerSimUnit;
            asset.bounds=new Bounds(new Vector3(0,height*.5f,0),bounds.size*scale);asset.provenance=Path.GetFileName(path)+"; all finite positive-opacity splats up to budget; first-order SH; Y-up normalized";
            stream.Position=0;using var sha=SHA256.Create();asset.sourceSha256=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();return asset;
        }
        [MenuItem("Physics Hands/Gaussian World/Import Graphdeco PLY")]
        public static void Import()
        {
            string path=EditorUtility.OpenFilePanel("Import isolated, Y-up Gaussian flower","","ply");if(string.IsNullOrEmpty(path))return;
            string output=EditorUtility.SaveFilePanelInProject("Save Gaussian asset","Flower","asset","Choose asset location");if(string.IsNullOrEmpty(output))return;
            AssetDatabase.CreateAsset(Read(path),output);AssetDatabase.SaveAssets();
        }
    }
}
