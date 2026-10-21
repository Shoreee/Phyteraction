using PhysicsHands.Simulation;
using PhysicsHands.GaussianData;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsHands.App
{
    // One Gaussian scene active at a time; desk buttons and keys 1-9, 0 switch, Backspace resets,
    // P replays the paper's scripted interaction, B toggles the reconstructed surroundings.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.GaussianWorld", "PhysicsHands.Runtime", "GaussianSceneSelector")]
    public sealed class GaussianSceneSelector : MonoBehaviour
    {
        public GameObject[] scenes = Array.Empty<GameObject>();
        public string[] titles = Array.Empty<string>();
        public Renderer[] buttons = Array.Empty<Renderer>();
        public Renderer backdropButton;
        public TextMesh status;
        public int startScene;
        public Color idle = new(.18f, .24f, .27f), active = new(.98f, .66f, .20f);
        public int Current
        {
            get;
            private set;
        }

        = -1;
        public GameObject CurrentScene => Current >= 0 ? scenes[Current] : null;
        bool backdrop;
        MaterialPropertyBlock block;
        void Start()
        {
            backdrop = false;
            Select(startScene);
            Tint(backdropButton, false);
        }

        void Tint(Renderer r, bool on)
        {
            if (r == null)
                return;
            block ??= new MaterialPropertyBlock();
            block.SetColor("_BaseColor", on ? active : idle);
            r.SetPropertyBlock(block);
        }

        void SetBackdrop(bool show)
        {
            backdrop = show;
            Tint(backdropButton, show);
            foreach (var scene in scenes)
                if (scene != null && scene.activeInHierarchy && scene.TryGetComponent(out GaussianRenderBinding output))
                    output.Backdrop = show;
            if (CurrentScene != null)
                foreach (Renderer r in CurrentScene.GetComponentsInChildren<Renderer>(true))
                    if (r.gameObject.name == "Pedestal")
                        r.enabled = !show;
            RefreshStatus();
        }

        public void ToggleBackdrop() => SetBackdrop(!backdrop);
        void RefreshStatus()
        {
            if (status == null || CurrentScene == null)
                return;
            string title = Current < titles.Length ? titles[Current] : CurrentScene.name;
            if (CurrentScene.TryGetComponent(out GaussianRenderBinding output) && output.asset != null)
            {
                int total = output.asset.splats.Length;
                if (output.dynamicCount >= 0)
                {
                    int visible = backdrop ? total : Mathf.Min(total, output.nearCount);
                    title += $"  ·  GS {visible:N0}/{total:N0}";
                }
                else
                    title += $"  ·  GS {total:N0}";
            }

            status.text = title;
        }

        public void Select(int index)
        {
            if (index < 0 || index >= scenes.Length)
                return;
            for (int i = 0; i < scenes.Length; i++)
                if (i != index && scenes[i] != null)
                    scenes[i].SetActive(false);
            scenes[index].SetActive(true);
            Current = index;
            for (int i = 0; i < buttons.Length; i++)
                Tint(buttons[i], i == index);
            SetBackdrop(backdrop);
        }

        public void ResetScene()
        {
            var s = CurrentScene;
            if (s == null)
                return;
            s.GetComponent<ISimulationControl>()?.ResetSimulation();
        }

        public void Replay()
        {
            var s = CurrentScene;
            if (s == null)
                return;
            var simulation = s.GetComponent<ISimulationControl>();
            if (simulation == null)
                return;
            if (simulation.SupportsReplay)
                SetBackdrop(true);
            simulation.Replay();
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k == null)
                return;
            for (int i = 0; i < 10; i++)
                if (k[i == 9 ? Key.Digit0 : Key.Digit1 + i].wasPressedThisFrame)
                    Select(i);
            if (k.backspaceKey.wasPressedThisFrame)
                ResetScene();
            if (k.pKey.wasPressedThisFrame)
                Replay();
            if (k.bKey.wasPressedThisFrame)
                ToggleBackdrop();
        }
    }
}
