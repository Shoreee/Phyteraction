using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PhysicsHands.GaussianWorld;
using PhysicsHands.Hands;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PhysicsHands.Editor.GaussianWorld
{
    [InitializeOnLoad]
    public static class GaussianValidation
    {
        const string Key="GaussianWorld.Validation";
        static GaussianValidation()
        {
            EditorApplication.playModeStateChanged+=state=>{
                if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false))new GameObject("Gaussian Validation").AddComponent<GaussianProbe>();
                if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(Key,false)){
                    SessionState.SetBool(Key,false);
                    if(Application.isBatchMode)EditorApplication.Exit(File.Exists("Logs/gaussian-validation.json")&&File.ReadAllText("Logs/gaussian-validation.json").Contains("\"passed\": true")?0:1);
                }
            };
        }
        [MenuItem("Physics Hands/Gaussian World/Run GPU Validation")]
        public static void Run(){GaussianDemoBuilder.CreateScene();SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
    }
    public sealed class GaussianProbe:MonoBehaviour
    {
        [Serializable] public class Trial { public string name;public float peakRms,releasedRms,anchorError,peakReactionN;public bool finite,recovered; }
        [Serializable] public class TwoWay { public float initialSpeed,finalSpeedAlongPush,peakReactionN,peakRms;public bool slowed; }
        [Serializable] public class Result
        {
            public bool passed;public string device,asset;public int splats,voxels,nodes,anchoredNodes,tets,samples;
            public float youngsMinPa,youngsMedianPa,youngsMaxPa,densityKgM3,meanSolverMs,maxSolverMs;
            public int modes;public float[] lowestModesHz;
            public float headFrequencyHz,headFrequencyHzDoubleSubsteps,nearestModeHz;
            public Trial[] trials;public TwoWay twoWay;public string[] errors;
            public string limitation="Geometric probes and a rigid-body probe in the editor; not real hand tracking, photon latency or device validation.";
        }
        readonly List<string> errors=new();
        readonly List<double> solverMs=new();
        GaussianFlower flower;
        void Capture(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
        void LateUpdate(){if(flower!=null&&flower.StepId>0)solverMs.Add(flower.SolverMs);}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Capture;
            flower=FindAnyObjectByType<GaussianFlower>();yield return null;
            if(Camera.main!=null){var driver=Camera.main.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();if(driver!=null)driver.enabled=false;Camera.main.transform.SetPositionAndRotation(new Vector3(0,1.35f,0),Quaternion.Euler(12,0,0));}
            if(flower==null||flower.Solver==null){errors.Add("No cage solver.");Finish(new Result());yield break;}
            // Fixed simulated time makes recovery comparable across editor frame rates.
            Time.captureFramerate=90;flower.useTestContacts=true;
            var cage=flower.Cage;
            var result=new Result{device=SystemInfo.graphicsDeviceName,asset=flower.asset.provenance,splats=flower.asset.splats.Length,voxels=cage.voxelCount,nodes=cage.rest.Length,
                anchoredNodes=cage.anchoredNodes,tets=cage.tets.Length,samples=cage.sampleVoxel.Length,youngsMinPa=cage.youngsMin,youngsMedianPa=cage.youngsMedian,youngsMaxPa=cage.youngsMax,densityKgM3=cage.density};
            var bounds=flower.asset.bounds;var trials=new List<Trial>();
            Vector3 head=Vector3.zero;int topCount=0;
            foreach(var g in flower.asset.splats)if(g.center.y>bounds.max.y-.06f){head+=(Vector3)g.center;topCount++;}
            head/=Mathf.Max(1,topCount);
            foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            {
                // Select an actual surface splat in the upper half on the chosen side.
                Vector3 target=bounds.center;float best=float.NegativeInfinity;
                foreach(var g in flower.asset.splats){Vector3 p=g.center;if(p.y<bounds.center.y)continue;float score=Vector3.Dot(p,direction);if(score>best){best=score;target=p;}}
                yield return Push("push "+direction,target,direction,trials);
            }
            yield return Pinch(head,trials);
            int substeps=flower.substeps;
            yield return Frequency(head,f=>result.headFrequencyHz=f);
            flower.substeps=substeps*2;yield return Frequency(head,f=>result.headFrequencyHzDoubleSubsteps=f);flower.substeps=substeps;
            if(result.headFrequencyHz<=0||Mathf.Abs(result.headFrequencyHzDoubleSubsteps/result.headFrequencyHz-1)>.15f)
                errors.Add($"Head frequency not converged in substeps: {result.headFrequencyHz:F2} Hz vs {result.headFrequencyHzDoubleSubsteps:F2} Hz");
            result.modes=flower.Solver.ModeCount;result.lowestModesHz=new float[Mathf.Min(6,result.modes)];
            float nearest=float.MaxValue;
            for(int m=0;m<result.lowestModesHz.Length;m++)
            {
                float hz=result.lowestModesHz[m]=flower.asset.modeOmega[m]/(2*Mathf.PI);
                if(Mathf.Abs(hz-result.headFrequencyHz)<Mathf.Abs(nearest-result.headFrequencyHz))nearest=hz;
            }
            result.nearestModeHz=nearest;
            // Zero-crossing estimates over ~3 s resolve about +-0.2 Hz; damping shifts frequency by < 1%.
            if(Mathf.Abs(result.headFrequencyHz-nearest)>Mathf.Max(.25f,.15f*nearest))
                errors.Add($"Head sway {result.headFrequencyHz:F2} Hz does not match any of the lowest learned-stiffness modes ({string.Join(", ",result.lowestModesHz)} Hz)");
            var twoWay=new TwoWay();yield return RigidProbe(head,twoWay);result.twoWay=twoWay;
            flower.ResetSimulation();flower.useTestContacts=true;flower.testSpheres=Array.Empty<Vector4>();
            yield return new WaitForSeconds(.2f);
            Screenshot();
            yield return null;
            result.trials=trials.ToArray();
            if(solverMs.Count>0){double sum=0,max=0;foreach(var s in solverMs){sum+=s;max=Math.Max(max,s);}result.meanSolverMs=(float)(sum/solverMs.Count);result.maxSolverMs=(float)max;}
            result.passed=errors.Count==0&&twoWay.slowed;
            foreach(var t in trials)result.passed&=t.finite&&t.recovered&&t.anchorError<1e-6f;
            Finish(result);
        }
        IEnumerator Push(string name,Vector3 target,Vector3 direction,List<Trial> trials)
        {
            flower.ResetSimulation();flower.useTestContacts=true;flower.testSpheres=Array.Empty<Vector4>();yield return new WaitForSeconds(.1f);
            var trial=new Trial{name=name,finite=true};
            for(int frame=0;frame<70;frame++){
                Vector3 p=target+direction*Mathf.Lerp(.055f,-.012f,frame/69f);flower.testSpheres=new[]{new Vector4(p.x,p.y,p.z,.018f)};
                yield return null;trial.peakReactionN=Mathf.Max(trial.peakReactionN,flower.ReactionForce);
                if(frame%10==0||frame==69)trial.peakRms=Mathf.Max(trial.peakRms,Measure(trial));
            }
            flower.testSpheres=Array.Empty<Vector4>();yield return new WaitForSeconds(3);
            trial.releasedRms=Measure(trial);trial.recovered=trial.peakRms>.0001f&&trial.releasedRms<trial.peakRms*.35f;
            trials.Add(trial);
        }
        IEnumerator Pinch(Vector3 head,List<Trial> trials)
        {
            // Two opposing geometric contacts compress the head; release uses the same solver.
            flower.ResetSimulation();var pinch=new Trial{name="opposed pinch",finite=true};
            for(int frame=0;frame<80;frame++){
                float separation=Mathf.Lerp(.065f,.026f,frame/79f);Vector3 a=head+Vector3.right*separation,b=head-Vector3.right*separation;
                flower.testSpheres=new[]{new Vector4(a.x,a.y,a.z,.018f),new Vector4(b.x,b.y,b.z,.018f)};
                yield return null;pinch.peakReactionN=Mathf.Max(pinch.peakReactionN,flower.ReactionForce);
                if(frame%10==0)pinch.peakRms=Mathf.Max(pinch.peakRms,Measure(pinch));
            }
            flower.testSpheres=Array.Empty<Vector4>();yield return new WaitForSeconds(3);
            pinch.releasedRms=Measure(pinch);pinch.recovered=pinch.peakRms>.0001f&&pinch.releasedRms<pinch.peakRms*.35f;trials.Add(pinch);
        }
        IEnumerator Frequency(Vector3 head,Action<float> report)
        {
            flower.ResetSimulation();flower.useTestContacts=true;
            for(int frame=0;frame<45;frame++){Vector3 p=head+Vector3.right*Mathf.Lerp(.06f,.02f,frame/44f);flower.testSpheres=new[]{new Vector4(p.x,p.y,p.z,.018f)};yield return null;}
            flower.testSpheres=Array.Empty<Vector4>();
            var cage=flower.Cage;var top=new List<int>();
            for(int i=0;i<cage.rest.Length;i++)if(cage.rest[i].y>flower.asset.bounds.max.y-.06f&&cage.inverseMass[i]>0)top.Add(i);
            var samples=new List<float>();
            for(int frame=0;frame<270;frame++)
            {
                yield return null;float sum=0;var x=flower.Solver.Positions;
                foreach(int i in top)sum+=x[i].x-cage.rest[i].x;samples.Add(sum/Mathf.Max(1,top.Count));
            }
            // Zero crossings of the head's lateral displacement after release.
            int first=-1,last=-1,crossings=0;
            for(int i=1;i<samples.Count;i++)if(samples[i-1]*samples[i]<0){if(first<0)first=i;last=i;crossings++;}
            report(crossings>=3?(crossings-1)*.5f/((last-first)/90f):0);
        }
        IEnumerator RigidProbe(Vector3 head,TwoWay result)
        {
            flower.ResetSimulation();flower.useTestContacts=false;var hands=flower.hands;flower.hands=Array.Empty<HoloHand>();
            var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name="Two-way probe";sphere.transform.localScale=Vector3.one*.036f;
            sphere.transform.position=flower.transform.TransformPoint(head+Vector3.right*.07f);
            var body=sphere.AddComponent<Rigidbody>();body.useGravity=false;body.mass=.03f;body.linearDamping=0;body.angularDamping=0;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            Vector3 push=flower.transform.TransformDirection(Vector3.left);result.initialSpeed=.3f;body.linearVelocity=push*result.initialSpeed;
            flower.probes=new Collider[]{sphere.GetComponent<Collider>()};
            var trial=new Trial{finite=true};
            for(int frame=0;frame<90;frame++)
            {
                yield return null;result.peakReactionN=Mathf.Max(result.peakReactionN,flower.ReactionForce);
                if(frame%10==0)result.peakRms=Mathf.Max(result.peakRms,Measure(trial));
            }
            result.finalSpeedAlongPush=Vector3.Dot(body.linearVelocity,push);
            result.slowed=trial.finite&&result.peakReactionN>0&&result.finalSpeedAlongPush<result.initialSpeed*.9f&&result.peakRms>.0001f;
            flower.probes=Array.Empty<Collider>();flower.hands=hands;Destroy(sphere);
        }
        void Screenshot()
        {
            var camera=Camera.main;var targetTexture=new RenderTexture(1280,960,24,RenderTextureFormat.ARGB32);targetTexture.Create();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=targetTexture});
            var old=RenderTexture.active;RenderTexture.active=targetTexture;var screenshot=new Texture2D(1280,960,TextureFormat.RGB24,false);
            screenshot.ReadPixels(new Rect(0,0,1280,960),0,0);screenshot.Apply();
            File.WriteAllBytes("Logs/gaussian-flower-preview.png",screenshot.EncodeToPNG());RenderTexture.active=old;Destroy(screenshot);targetTexture.Release();Destroy(targetTexture);
        }
        float Measure(Trial trial)
        {
            var x=flower.Solver.Positions;var cage=flower.Cage;float sum=0;
            for(int i=0;i<x.Length;i++){
                float v=math.distance(x[i],cage.rest[i]);if(float.IsNaN(v)||float.IsInfinity(v))trial.finite=false;
                sum+=v*v;if(cage.inverseMass[i]==0)trial.anchorError=Mathf.Max(trial.anchorError,v);
            }
            return Mathf.Sqrt(sum/x.Length);
        }
        void Finish(Result result){Application.logMessageReceived-=Capture;result.errors=errors.ToArray();Directory.CreateDirectory("Logs");File.WriteAllText("Logs/gaussian-validation.json",JsonUtility.ToJson(result,true));Time.captureFramerate=0;EditorApplication.isPlaying=false;}
    }
}
