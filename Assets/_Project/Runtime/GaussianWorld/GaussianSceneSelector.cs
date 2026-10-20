using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PhysicsHands.GaussianWorld
{
    // One Gaussian scene active at a time; desk buttons and keys 1-9, 0 switch, Backspace resets,
    // P replays the paper's scripted interaction, B toggles the reconstructed surroundings.
    public sealed class GaussianSceneSelector : MonoBehaviour
    {
        public GameObject[] scenes = Array.Empty<GameObject>();
        public string[] titles = Array.Empty<string>();
        public Renderer[] buttons = Array.Empty<Renderer>();
        public Renderer backdropButton;
        public TextMesh status;
        public int startScene;
        public Color idle = new(.18f, .24f, .27f), active = new(.98f, .66f, .20f);
        public int Current { get; private set; } = -1;
        public GameObject CurrentScene => Current >= 0 ? scenes[Current] : null;
        MaterialPropertyBlock block;

        void Start() { SplatRenderer.Backdrop = false; Select(startScene); Tint(backdropButton, false); }
        void Tint(Renderer r, bool on) { if (r == null) return; block ??= new MaterialPropertyBlock(); block.SetColor("_BaseColor", on ? active : idle); r.SetPropertyBlock(block); }
        void SetBackdrop(bool show)
        {
            SplatRenderer.Backdrop = show; Tint(backdropButton, show);
            if (CurrentScene != null)
                foreach (Renderer r in CurrentScene.GetComponentsInChildren<Renderer>(true))
                    if (r.gameObject.name == "Pedestal") r.enabled = !show;
            RefreshStatus();
        }
        public void ToggleBackdrop() => SetBackdrop(!SplatRenderer.Backdrop);
        void RefreshStatus()
        {
            if (status == null || CurrentScene == null) return;
            string title = Current < titles.Length ? titles[Current] : CurrentScene.name;
            if (CurrentScene.TryGetComponent(out MpmWorld mpm) && mpm.asset != null)
            {
                int total = mpm.asset.splats.Length;
                int visible = SplatRenderer.Backdrop ? total : Mathf.Min(total, mpm.asset.dynamicCount + mpm.asset.nearCount);
                title += $"  ·  GS {visible:N0}/{total:N0}";
            }
            else if (CurrentScene.TryGetComponent(out GaussianFlower flower) && flower.asset != null)
                title += $"  ·  GS {flower.asset.splats.Length:N0}";
            status.text = title;
        }
        public void Select(int index)
        {
            if (index < 0 || index >= scenes.Length) return;
            for (int i = 0; i < scenes.Length; i++) if (i != index && scenes[i] != null) scenes[i].SetActive(false);
            scenes[index].SetActive(true); Current = index;
            for (int i = 0; i < buttons.Length; i++) Tint(buttons[i], i == index);
            SetBackdrop(SplatRenderer.Backdrop);
        }
        public void ResetScene()
        {
            var s = CurrentScene; if (s == null) return;
            if (s.TryGetComponent(out MpmWorld mpm)) mpm.ResetSimulation();
            if (s.TryGetComponent(out BrickAssembly bricks)) bricks.ResetBricks();
            if (s.TryGetComponent(out GaussianFlower flower)) flower.ResetSimulation();
        }
        public void Replay()
        {
            var s = CurrentScene; if (s == null) return;
            if (s.TryGetComponent(out MpmWorld mpm))
            {
                // The paper demonstrations include the reconstructed surroundings.
                SetBackdrop(true);
                mpm.Replay();
            }
            else ResetScene();
        }
        void Update()
        {
            var k = Keyboard.current; if (k == null) return;
            for (int i = 0; i < 10; i++) if (k[i == 9 ? Key.Digit0 : Key.Digit1 + i].wasPressedThisFrame) Select(i);
            if (k.backspaceKey.wasPressedThisFrame) ResetScene();
            if (k.pKey.wasPressedThisFrame) Replay();
            if (k.bKey.wasPressedThisFrame) ToggleBackdrop();
        }
    }
}
