using System;
using System.Collections.Generic;
using System.IO;
using PhysicsHands.GaussianWorld;
using PhysicsHands.Hands;
using PhysicsHands.Interaction;
using PhysicsHands.Laboratory;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.VisionOS;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PhysicsHands.Editor.GaussianWorld
{
    public static class GaussianDemoBuilder
    {
        [Serializable] class MaterialBake {public float sim_scale;}
        public const string ScenePath="Assets/_Project/Scenes/GaussianFlower.unity";
        public const string AssetPath="Assets/_Project/GaussianWorld/Flower.asset";
        [MenuItem("Physics Hands/Gaussian World/Create Flower Demo Scene")]
        public static void CreateScene()
        {
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var asset=ImportFlowerAsset();
            var scene=OpenCleanSandbox(out var hands);
            TextMesh readout=null;
            foreach(var label in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
            {
                if(label.name=="Title")label.text="GAUSSIAN / FLOWER LAB";
                else if(label.name=="Subtitle")label.text="ELASTIC CONTACT    /    METAL GPU";
                else if(label.name=="Instructions"){label.text="Push, brush, pinch. Release to recover.";readout=label;}
                else if(label.name=="Status")label.text=asset.provenance.StartsWith("DIAGNOSTIC")?"DIAGNOSTIC SYNTHETIC ASSET":"PHYS DREAMER / CARNATION";
                else if(label.name=="Station"||label.name.EndsWith("label"))label.text="";
            }
            var flower=AddFlower(asset,hands,readout,new Vector3(0,.98f,.62f));
            flower.gameObject.AddComponent<GaussianDemoControls>().flower=flower;
            flower.gameObject.AddComponent<GaussianBenchmark>().flower=flower;
            if(Camera.main!=null)Camera.main.transform.position=new Vector3(0,1.35f,0);
            EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            Debug.Log("Gaussian demo scene created: "+ScenePath+"; "+asset.provenance);
        }
        public static GaussianAsset ImportFlowerAsset()
        {
            Directory.CreateDirectory("Assets/_Project/GaussianWorld");AssetDatabase.Refresh();
            const string path="Research/GaussianWorld/data/carnation-baked.ply",bake="Research/GaussianWorld/data/material-bake.json";
            if(!File.Exists(path)||!File.Exists(bake))throw new FileNotFoundException("Run prepare_flower.py and bake_material.py first.",path);
            var calibration=JsonUtility.FromJson<MaterialBake>(File.ReadAllText(bake));
            var imported=GaussianPly.Read(path,sourceUnitsPerSimUnit:calibration.sim_scale);imported.name=Path.GetFileNameWithoutExtension(AssetPath);
            imported.provenance="PhysDreamer / carnations; author GS + clean_object_points mask; first-order SH; learned E field + moving-part boundary from models.zip; hashes in Research/GaussianWorld/data";
            var asset=AssetDatabase.LoadAssetAtPath<GaussianAsset>(AssetPath);
            if(asset==null){asset=imported;AssetDatabase.CreateAsset(asset,AssetPath);}
            else{EditorUtility.CopySerialized(imported,asset);Object.DestroyImmediate(imported);EditorUtility.SetDirty(asset);}
            return asset;
        }
        // Sandbox lab without props, lab buttons or controller; all hands active.
        public static UnityEngine.SceneManagement.Scene OpenCleanSandbox(out HoloHand[] hands)
        {
            var scene=EditorSceneManager.OpenScene("Assets/_Project/Scenes/Sandbox.unity");
            foreach(var prop in Object.FindObjectsByType<Holdable>(FindObjectsSortMode.None))Object.DestroyImmediate(prop.gameObject);
            foreach(var button in Object.FindObjectsByType<LaboratoryButton>(FindObjectsSortMode.None))Object.DestroyImmediate(button.gameObject);
            foreach(var controller in Object.FindObjectsByType<LaboratoryController>(FindObjectsSortMode.None))Object.DestroyImmediate(controller.gameObject);
            hands=Object.FindObjectsByType<HoloHand>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            foreach(var h in hands)h.gameObject.SetActive(true);
            return scene;
        }
        public static GaussianFlower AddFlower(GaussianAsset asset,HoloHand[] hands,TextMesh readout,Vector3 position)
        {
            var go=new GameObject("Gaussian Flower");go.transform.position=position;
            var flower=go.AddComponent<GaussianFlower>();flower.asset=asset;flower.hands=hands;
            go.AddComponent<GaussianMetrics>().display=readout;
            flower.solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Shaders/GaussianWorld/ElasticGaussian.compute");
            flower.sortShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Project/Shaders/GaussianWorld/GaussianSort.compute");
            flower.splatShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/GaussianWorld/Gaussian.shader");
            BakeModes(asset,flower);
            return flower;
        }
        [Serializable] class ModeBake {public string cage_sha256;public float[] frequencies_hz;}
        // Exports the cage with its learned Lame parameters and solves K phi = omega^2 M phi offline
        // (tools/bake_modes.py); reuses the previous solve when the cage bytes are unchanged.
        public static void BakeModes(GaussianAsset asset,GaussianFlower flower,int modes=48)
        {
            const string cagePath="Research/GaussianWorld/data/cage.bin",modesPath="Research/GaussianWorld/data/modes.bin",reportPath="Research/GaussianWorld/data/modes.json";
            var cage=new ElasticCage(asset,flower.voxelSize,flower.totalMass,flower.fallbackYoungs);
            using(var w=new BinaryWriter(File.Create(cagePath)))
            {
                w.Write(cage.rest.Length);w.Write(cage.tets.Length);
                foreach(var p in cage.rest){w.Write(p.x);w.Write(p.y);w.Write(p.z);}
                foreach(float m in cage.inverseMass)w.Write(m);
                foreach(var t in cage.tets){w.Write(t.x);w.Write(t.y);w.Write(t.z);w.Write(t.w);}
                foreach(float m in cage.mu)w.Write(m);foreach(float l in cage.lambda)w.Write(l);
            }
            string digest;using(var sha=System.Security.Cryptography.SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(cagePath))).Replace("-","").ToLowerInvariant();
            bool cached=File.Exists(modesPath)&&File.Exists(reportPath)&&JsonUtility.FromJson<ModeBake>(File.ReadAllText(reportPath)).cage_sha256==digest
                &&new FileInfo(modesPath).Length==8+4L*modes*(1+cage.rest.Length*3);
            if(!cached)
            {
                var start=new System.Diagnostics.ProcessStartInfo("Research/GaussianWorld/.venv/bin/python",$"Research/GaussianWorld/tools/bake_modes.py {cagePath} {modesPath} {modes}")
                    {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
                using var process=System.Diagnostics.Process.Start(start);
                string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();process.WaitForExit();
                if(process.ExitCode!=0)throw new Exception("bake_modes.py failed: "+error);
                Debug.Log("Gaussian modes baked: "+output.Trim());
            }
            using(var r=new BinaryReader(File.OpenRead(modesPath)))
            {
                int k=r.ReadInt32(),nodes=r.ReadInt32();
                if(nodes!=cage.rest.Length)throw new Exception("Mode bake does not match the cage.");
                asset.modeOmega=new float[k];for(int i=0;i<k;i++)asset.modeOmega[i]=r.ReadSingle();
                asset.modeShapes=new float[nodes*3*k];for(int i=0;i<asset.modeShapes.Length;i++)asset.modeShapes[i]=r.ReadSingle();
            }
            asset.modeVoxel=flower.voxelSize;asset.modeMass=flower.totalMass;EditorUtility.SetDirty(asset);
        }
        public static void ExportBoth(){ExportMac();ExportVisionOS();}
        // gpu_frame_ms in the CSV and readout is empty without frame timing stats.
        static void EnableFrameTiming(){PlayerSettings.enableFrameTimingStats=true;AssetDatabase.SaveAssets();}
        [MenuItem("Physics Hands/Gaussian World/Export Native visionOS")]
        public static void ExportVisionOS()
        {
            GaussianWorldBuilder.ExportVisionOS();
        }
        [MenuItem("Physics Hands/Gaussian World/Build macOS Demo")]
        public static void ExportMac()
        {
            CreateScene();EnableFrameTiming();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="Builds/GaussianFlower.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            File.WriteAllText("Logs/gaussian-macos-build.txt",report.summary.result+" errors="+report.summary.totalErrors);
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Gaussian macOS build failed.");
        }
    }
}
