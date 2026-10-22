using PhysicsHands.GaussianData;
using PhysicsHands.Simulation;
using UnityEngine;

namespace PhysicsHands.App
{
    [RequireComponent(typeof(GaussianRenderBinding))]
    public sealed class GaussianWorkbenchContent : MonoBehaviour, IWorkbenchContent
    {
        public string title;
        GaussianRenderBinding output;
        ISimulationControl simulation;
        Renderer[] pedestals;
        bool room;
        void Bind()
        {
            if (output != null) return;
            output = GetComponent<GaussianRenderBinding>();
            simulation = GetComponent<GaussianModelRuntime>().simulation as ISimulationControl;
            pedestals = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r.name == "Pedestal");
        }
        public bool Available
        {
            get
            {
                Bind();
                if (output == null || output.asset == null || output.asset.splats == null || output.asset.splats.Length == 0) return false;
                var mpm = GetComponent<MpmSimulation>();
                if (mpm != null && mpm.asset == null) return false;
                var modal = GetComponent<ModalSimulation>();
                return modal == null || modal.asset != null;
            }
        }
        public bool SupportsRoom => Available;
        public bool SupportsPaper { get { Bind(); return simulation != null && simulation.SupportsReplay; } }
        public string Status
        {
            get
            {
                Bind();
                if (!Available) return title + "  ·  MODEL NOT INSTALLED";
                int total = output.asset.splats.Length;
                int visible = room || output.dynamicCount < 0 ? total : Mathf.Min(total, output.nearCount);
                return $"{title}  ·  GS {visible:N0}/{total:N0}" + (simulation is CageSimulation ? "  ·  ELASTIC" : "");
            }
        }
        public void SetContentActive(bool active) => gameObject.SetActive(active);
        public void ResetContent() { Bind(); simulation?.ResetSimulation(); }
        public void SetRoom(bool visible)
        {
            Bind(); room = visible; output.Backdrop = visible;
            foreach (Renderer pedestal in pedestals) pedestal.enabled = !visible;
        }
        public void Replay() { Bind(); if (SupportsPaper) simulation.Replay(); }
    }
}
