using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace PhysicsHands.App
{
    /// <summary>Selects one workbench content while preserving the shared tracking rig.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "PhysicsHands.App", "PhysicsHands.App", "GaussianSceneSelector")]
    public sealed class WorkbenchContentSelector : MonoBehaviour
    {
        [FormerlySerializedAs("scenes")] public GameObject[] models = Array.Empty<GameObject>();
        public Renderer[] buttons = Array.Empty<Renderer>();
        public Renderer backdropButton, paperButton, labButton, treeButton;
        public TreeWorkbenchContent tree;
        public bool InTree { get; private set; }
        public TextMesh labLabel, status;
        public PhysicsLabContent lab;
        public WorkbenchInteraction interaction;
        public int startScene;
        public Color idle = new(.12f, .12f, .12f), active = new(.9f, .9f, .9f), unavailable = new(.035f, .035f, .035f);
        public int Current { get; private set; } = -1;
        public bool InLab { get; private set; }
        public GameObject CurrentScene => InTree ? tree.gameObject : InLab ? lab.gameObject : Current >= 0 ? models[Current] : null;
        IWorkbenchContent[] contents;
        IWorkbenchContent current;
        bool room;
        MaterialPropertyBlock block;
        void Awake()
        {
            if (lab == null || interaction == null)
            {
                Debug.LogError($"{name}: workbench selector requires physics lab and interaction policy bindings.", this);
                enabled = false;
                return;
            }
            contents = new IWorkbenchContent[models.Length];
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] == null) continue;
                contents[i] = models[i].GetComponent<IWorkbenchContent>();
                models[i].SetActive(false);
                if (contents[i] == null) Debug.LogError($"{name}: model {models[i].name} has no workbench content adapter.", models[i]);
            }
            if (lab != null) lab.gameObject.SetActive(false);
            if (tree != null) tree.gameObject.SetActive(false);
        }
        void Start()
        {
            if (!IsModelAvailable(startScene))
            {
                if (lab != null) EnterLab();
                else Debug.LogError($"{name}: no available default content or physics lab.", this);
            }
            else Select(startScene);
            Refresh();
        }
        public bool IsModelAvailable(int index) => contents != null && index >= 0 && index < contents.Length && contents[index]?.Available == true;
        public bool CanRoom => current?.SupportsRoom == true;
        public bool CanPaper => current?.SupportsPaper == true;
        public void Select(int index)
        {
            if (!IsModelAvailable(index) || !InLab && !InTree && Current == index) return;
            Switch(contents[index], false);
            Current = index;
            current.SetRoom(room);
            Refresh();
        }
        void Switch(IWorkbenchContent next, bool isLab)
        {
            interaction.ReleaseHands();
            current?.SetContentActive(false);
            interaction.SetLab(isLab);
            InLab = isLab;
            InTree = next == (IWorkbenchContent)tree;
            current = next;
            current.SetContentActive(true);
        }
        void EnterLab()
        {
            if (lab == null) return;
            Switch(lab, true);
            Refresh();
        }
        public void ToggleTree()
        {
            if (tree == null || !tree.Available) return;
            if (!InTree) { Switch(tree, false); Refresh(); }
            else if (IsModelAvailable(Current)) Select(Current);
            else EnterLab();
        }
        public void ToggleLab()
        {
            if (!InLab) EnterLab();
            else if (IsModelAvailable(Current)) Select(Current);
            else
            {
                for (int i = 0; i < contents.Length; i++) if (IsModelAvailable(i)) { Select(i); return; }
                // With no model installed LAB remains usable; BACK cannot select missing data.
                Refresh();
            }
        }
        public void ResetScene() { interaction.ReleaseHands(); current?.ResetContent(); }
        public void Replay()
        {
            if (!CanPaper) return;
            if (CanRoom) { room = true; current.SetRoom(true); }
            current.Replay(); Refresh();
        }
        public void ToggleBackdrop()
        {
            if (!CanRoom) return;
            room = !room; current.SetRoom(room); Refresh();
        }
        void Tint(Renderer renderer, bool selected, bool enabled)
        {
            if (renderer == null) return;
            block ??= new MaterialPropertyBlock();
            block.SetColor("_BaseColor", enabled ? selected ? active : idle : unavailable);
            renderer.SetPropertyBlock(block);
            TextMesh label = renderer.GetComponentInChildren<TextMesh>();
            if (label != null) label.color = !enabled ? new Color(.3f,.3f,.3f) : selected ? Color.black : Color.white;
        }
        void Refresh()
        {
            for (int i = 0; i < buttons.Length; i++) Tint(buttons[i], !InLab && !InTree && i == Current, IsModelAvailable(i));
            Tint(backdropButton, room && CanRoom, CanRoom);
            Tint(paperButton, false, CanPaper);
            Tint(labButton, InLab, lab != null);
            Tint(treeButton, InTree, tree != null && tree.Available);
            if (labLabel != null) labLabel.text = InLab ? "BACK" : "LAB";
            if (status != null) status.text = current?.Status ?? "NO CONTENT";
        }
        void Update()
        {
            Keyboard k = Keyboard.current;
            if (k == null) return;
            for (int i = 0; i < 10; i++) if (k[i == 9 ? Key.Digit0 : Key.Digit1 + i].wasPressedThisFrame) Select(i);
            if (k.tKey.wasPressedThisFrame) ToggleLab();
            if (k.f10Key.wasPressedThisFrame) ToggleTree();
            if (k.backspaceKey.wasPressedThisFrame) ResetScene();
            if (k.pKey.wasPressedThisFrame) Replay();
            if (k.bKey.wasPressedThisFrame) ToggleBackdrop();
        }
        void OnDisable() { if (interaction != null) { interaction.ReleaseHands(); interaction.SetLab(false); } }
    }
}
