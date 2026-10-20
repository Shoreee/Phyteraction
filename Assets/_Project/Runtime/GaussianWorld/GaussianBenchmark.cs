using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace PhysicsHands.GaussianWorld
{
    // Opt-in standalone measurement; wall-clock timing, no captureFramerate.
    public sealed class GaussianBenchmark:MonoBehaviour
    {
        public GaussianFlower flower;
        IEnumerator Start()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--gaussian-benchmark")<0)yield break;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=90;
            yield return new WaitForSecondsRealtime(2);
            Vector3 target=Vector3.zero;int count=0;
            foreach(var g in flower.asset.splats)if(g.center.y>flower.asset.bounds.max.y-.06f){target+=(Vector3)g.center;count++;}
            target/=Mathf.Max(1,count);flower.useTestContacts=true;double start=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-start<30)
            {
                double elapsed=Time.realtimeSinceStartupAsDouble-start;float phase=(float)(elapsed%4);
                if(phase<2){Vector3 p=target+Vector3.right*Mathf.Lerp(.08f,.005f,phase/2);flower.testSpheres=new[]{new Vector4(p.x,p.y,p.z,.018f)};}
                else flower.testSpheres=Array.Empty<Vector4>();
                yield return null;
            }
            var metrics=GetComponent<GaussianMetrics>();
            File.WriteAllText(Path.Combine(Application.persistentDataPath,"gaussian-benchmark-latest.txt"),metrics.OutputPath);
            Debug.Log("Gaussian benchmark finished: "+metrics.OutputPath);Application.Quit();
        }
    }
}
