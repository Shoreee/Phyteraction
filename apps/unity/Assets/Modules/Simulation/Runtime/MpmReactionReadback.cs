using System;
using System.Collections.Generic;
using PhysicsHands.Dynamics;
using PhysicsHands.GaussianData;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicsHands.Simulation
{
    internal sealed class MpmReactionReadback
    {
        sealed class Sample
        {
            internal readonly Rigidbody[] Bodies=new Rigidbody[ContactGatherer.MaxShapes];
            internal readonly float[] Data=new float[ContactGatherer.MaxShapes*8];
            internal readonly Action<AsyncGPUReadbackRequest> Callback;
            internal bool Busy,Ready,Discard;
            internal int Epoch;
            internal long Step;
            internal float Duration, PhysicalDuration;
            internal Sample(MpmReactionReadback owner)
            {
                Callback=request=>
                {
                    if(Discard||!owner.isCurrent(Epoch)){Release();return;}
                    if(request.hasError){owner.Failures++;Release();return;}
                    request.GetData<float>().CopyTo(Data);Ready=true;
                };
            }
            internal void Release(){Busy=Ready=Discard=false;Array.Clear(Bodies,0,Bodies.Length);}
        }
        readonly List<Sample> samples=new(8);
        readonly Func<int,bool> isCurrent;
        readonly Rigidbody[] feedbackBodies = new Rigidbody[ContactGatherer.MaxShapes];
        readonly float[] feedbackData = new float[ContactGatherer.MaxShapes * 8];
        float feedbackDuration, feedbackUntil;
        long feedbackStep = -1;
        internal int Failures {get;private set;}
        internal bool HasFeedback => HasArrivals || feedbackStep >= 0 && Time.fixedTime <= feedbackUntil;
        internal bool HasArrivals
        {
            get {foreach(var sample in samples)if(sample.Ready)return true;return false;}
        }
        internal MpmReactionReadback(Func<int,bool> isCurrent)
        {
            this.isCurrent=isCurrent;
            for(int i=0;i<8;i++)samples.Add(new Sample(this));
        }
        internal void Reset()
        {
            // A still-pending callback owns its sample until completion; never reuse its storage early.
            foreach(var sample in samples)
                if(sample.Busy&&!sample.Ready)sample.Discard=true;
                else sample.Release();
            Failures=0;feedbackStep=-1;Array.Clear(feedbackBodies,0,feedbackBodies.Length);
        }
        internal void Queue(CommandBuffer commands,ComputeBuffer buffer,long step,int epoch,ContactGatherer contacts,float duration,float physicalDuration)
        {
            Sample selected=null;
            foreach(var sample in samples)if(!sample.Busy){selected=sample;break;}
            // Grow only if actual GPU latency exceeds the warm pool; never drop a simulation step or reaction.
            if(selected==null){selected=new Sample(this);samples.Add(selected);}
            selected.Busy=true;selected.Step=step;selected.Epoch=epoch;selected.Duration=duration;selected.PhysicalDuration=physicalDuration;
            contacts.Snapshot(selected.Bodies);
            commands.RequestAsyncReadback(buffer,selected.Callback);
        }
        internal void Apply(MpmModelAsset asset,Transform transform,ContactGatherer contacts,float maxReactionForce,float dt,out int pairs,out float force)
        {
            pairs=0; force=0;
            // Each completed request owns its own snapshot. A late older result cannot
            // overwrite a newer force sample, and cannot discard another request's bodies.
            foreach(var sample in samples)
            {
                if(!sample.Ready)continue;
                if(sample.Step>=feedbackStep)
                {
                    feedbackStep=sample.Step;feedbackDuration=sample.Duration;
                    feedbackUntil=Time.fixedTime+Mathf.Max(.05f,2*sample.PhysicalDuration);
                    Array.Copy(sample.Bodies,feedbackBodies,feedbackBodies.Length);
                    Array.Copy(sample.Data,feedbackData,feedbackData.Length);
                }
                sample.Release();
            }
            if(feedbackStep<0||Time.fixedTime>feedbackUntil)return;
            float meters=asset.metersPerUnit,scale=meters*meters*meters,total=0;
            // Convert simulation impulse to force, then integrate that force at the PhysX
            // cadence. Hold between render-rate samples instead of weakening feedback when
            // rendering is slower than FixedUpdate. The same force clamp remains in effect.
            float physicalScale=dt/Mathf.Max(feedbackDuration,1e-9f);
            for(int i=0;i<ContactGatherer.MaxShapes;i++)
            {
                float weight=feedbackData[i*8+3];if(weight<=0)continue;pairs++;
                Vector3 local=asset.localFromSim.MultiplyVector(new Vector3(feedbackData[i*8],feedbackData[i*8+1],feedbackData[i*8+2]))*(scale*physicalScale);
                Vector3 point=asset.localFromSim.MultiplyPoint3x4(new Vector3(feedbackData[i*8+4],feedbackData[i*8+5],feedbackData[i*8+6])/weight);
                total+=contacts.ApplyReaction(i,feedbackBodies,transform.TransformDirection(local),transform.TransformPoint(point),maxReactionForce,dt);
            }
            force=total/dt;
        }
    }
}
