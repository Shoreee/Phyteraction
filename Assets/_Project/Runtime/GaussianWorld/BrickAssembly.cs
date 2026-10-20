using System;
using PhysicsHands.Interaction;
using UnityEngine;

namespace PhysicsHands.GaussianWorld
{
    // Lego-style hand interaction for a scanned toy whose moving part is segmented into rigid bricks
    // (asset.bricks). Attached bricks are fixed to the model. A grabbed brick stays put until the hand
    // pulls clutchStretch past it, then comes off into the hand as a free rigid body; a loose brick
    // brought back to its slot beside an attached neighbour (or onto the support) clicks in. The paper
    // replay hands the splats back to the MPM world.
    [DefaultExecutionOrder(310)]
    [RequireComponent(typeof(MpmWorld))]
    public sealed class BrickAssembly : MonoBehaviour
    {
        public ComputeShader shader;
        [Tooltip("How far (m) the hand must pull away from where it grabbed an attached brick before the brick comes off.")]
        [Min(.002f)] public float clutchStretch = .015f;
        [Tooltip("A brick let go of within this distance (m) and angle (deg) of its slot clicks in.")]
        [Min(.001f)] public float snapDistance = .008f;
        [Range(1, 45)] public float snapAngle = 20;
        [Tooltip("A held brick clicks in only this close, so carrying one past its slot does not catch it.")]
        [Min(.001f)] public float heldSnapDistance = .004f;
        [Range(1, 45)] public float heldSnapAngle = 12;
        [Tooltip("Duration of the click into the slot (s).")]
        [Min(0)] public float clickSeconds = .06f;

        public int Count => pieces.Length;
        public int AttachedCount { get; private set; }
        public bool Active => world != null && world.Ready && !world.replay && pieces.Length > 0;

        sealed class Piece
        {
            public Transform t; public Rigidbody body; public Holdable holdable; public BoxCollider box;
            public bool attached = true; public Holder holder; public Vector3 grabLocal;
            public float click = -1; public Vector3 clickFrom; public Quaternion clickFromRotation;
            public Matrix4x4 restInverse;
        }
        Piece[] pieces = Array.Empty<Piece>();
        MpmWorld world;
        Transform root;
        ComputeBuffer poses, splatBrick;
        Matrix4x4[] matrices;
        int kRigid;
        bool wasActive, regrabbing;

        void OnEnable()
        {
            world = GetComponent<MpmWorld>();
            var asset = world.asset;
            if (shader == null || asset == null || asset.bricks.Length == 0 || asset.splatBrick.Length != asset.dynamicCount) { enabled = false; return; }
            world.externalDrive = true;
            root = new GameObject("Bricks").transform; root.SetParent(transform, false);
            pieces = new Piece[asset.bricks.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                var b = asset.bricks[i]; int index = i;
                var go = new GameObject($"Brick {i}") { layer = gameObject.layer };
                go.transform.SetParent(root, false); go.transform.SetLocalPositionAndRotation(b.center, b.rotation);
                var box = go.AddComponent<BoxCollider>(); box.size = 2 * b.halfExtents;
                var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false; body.mass = b.mass;
                body.linearDamping = 1f; body.angularDamping = 2f;
                body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var holdable = go.AddComponent<Holdable>();
                holdable.SetAllowedSnaps(SnapType.Grasp, SnapType.Procedural, SnapType.PinchIndex); holdable.SetRestPhysics(true, false);
                holdable.OnCaptured += _ => Captured(index); holdable.OnReleased += _ => Released(index);
                pieces[i] = new Piece { t = go.transform, body = body, holdable = holdable, box = box, restInverse = Matrix4x4.TRS(b.center, b.rotation, Vector3.one).inverse };
            }
            // Boxes that overlap at rest never collide, so a brick fits back into its slot.
            Physics.SyncTransforms();
            for (int i = 0; i < pieces.Length; i++)
            {
                for (int j = i + 1; j < pieces.Length; j++)
                    if (Physics.ComputePenetration(pieces[i].box, pieces[i].t.position, pieces[i].t.rotation, pieces[j].box, pieces[j].t.position, pieces[j].t.rotation, out _, out _))
                        Physics.IgnoreCollision(pieces[i].box, pieces[j].box);
            }
            kRigid = shader.FindKernel("Rigid"); matrices = new Matrix4x4[pieces.Length];
            poses = new ComputeBuffer(pieces.Length, 64); splatBrick = new ComputeBuffer(asset.dynamicCount, 4); splatBrick.SetData(asset.splatBrick);
            AttachedCount = pieces.Length; wasActive = false;
        }
        void OnDisable()
        {
            if (root == null) return;
            if (world != null) world.externalDrive = false;
            Destroy(root.gameObject); root = null; pieces = Array.Empty<Piece>();
            poses?.Release(); splatBrick?.Release(); poses = splatBrick = null;
        }

        public void ResetBricks()
        {
            for (int i = 0; i < pieces.Length; i++) { Release(pieces[i]); Attach(i, false); }
        }

        void Captured(int i)
        {
            var p = pieces[i]; if (regrabbing) return;
            foreach (var h in p.holdable.Holders) p.holder = h;
            if (p.holder != null) p.grabLocal = p.t.InverseTransformPoint(GrabPoint(p.holder));
        }
        // Letting go of a brick at its slot presses it on; dropped elsewhere it stays loose.
        void Released(int i)
        {
            if (regrabbing) return;
            pieces[i].holder = null;
            if (!pieces[i].attached) TrySnap(i, snapDistance, snapAngle);
        }
        public bool TrySnap(int i) => TrySnap(i, snapDistance, snapAngle);
        bool TrySnap(int i, float distance, float angle)
        {
            if (pieces[i].attached || !Supported(i)) return false;
            var b = world.asset.bricks[i]; var t = pieces[i].t;
            Vector3 local = root.InverseTransformPoint(t.position); Quaternion rotation = Quaternion.Inverse(root.rotation) * t.rotation;
            if (Vector3.Distance(local, b.center) >= distance || Quaternion.Angle(rotation, b.rotation) >= angle) return false;
            Attach(i); return true;
        }
        static Vector3 GrabPoint(Holder h) => h.SnapJointAnchor != null ? h.SnapJointAnchor.position : h.transform.position;
        void Release(Piece p) { if (p.holder != null) { var h = p.holder; p.holder = null; h.Release(); } }

        // Pulled off: the brick follows the hand's pull, becomes dynamic and is re-grabbed by the same hand.
        public void Detach(int i, Vector3 worldOffset = default)
        {
            var p = pieces[i]; if (!p.attached) return;
            p.attached = false; p.click = -1; AttachedCount--;
            p.holdable.SetRestPhysics(false, true);
            p.t.position += worldOffset; p.body.isKinematic = false; p.body.useGravity = true;
            p.body.linearVelocity = p.body.angularVelocity = Vector3.zero;
            if (p.holder == null) return;
            var holder = p.holder; regrabbing = true;
            holder.Release(); holder.Capture(p.holdable);
            regrabbing = false;
        }
        // Back in its slot: kinematic again, eased onto the rest pose (the click).
        public void Attach(int i, bool click = true)
        {
            var p = pieces[i]; var b = world.asset.bricks[i];
            p.holdable.SetRestPhysics(true, false);
            if (!p.attached) { p.attached = true; AttachedCount++; }
            Release(p);
            if (!p.body.isKinematic) { p.body.linearVelocity = p.body.angularVelocity = Vector3.zero; }
            p.body.isKinematic = true; p.body.useGravity = false;
            if (click && clickSeconds > 0) { p.click = Time.time; p.clickFrom = p.t.localPosition; p.clickFromRotation = p.t.localRotation; }
            else { p.click = -1; p.t.SetLocalPositionAndRotation(b.center, b.rotation); }
        }
        public bool IsAttached(int i) => pieces[i].attached;
        public Transform Brick(int i) => pieces[i].t;
        public int[] Neighbors(int i) { var b = world.asset.bricks[i]; return world.asset.brickNeighbors[b.neighborStart..(b.neighborStart + b.neighborCount)]; }

        bool Supported(int i)
        {
            var b = world.asset.bricks[i]; if (b.grounded) return true;
            for (int k = 0; k < b.neighborCount; k++) if (pieces[world.asset.brickNeighbors[b.neighborStart + k]].attached) return true;
            return false;
        }

        void FixedUpdate()
        {
            if (!Active) return;
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i];
                if (p.attached)
                {
                    if (p.holder == null) continue;
                    Vector3 pull = GrabPoint(p.holder) - p.t.TransformPoint(p.grabLocal);
                    // The holder joint already carries the pull. Applying it again to the
                    // newly free piece creates a visible pop and opens seams in the splats.
                    if (pull.magnitude > clutchStretch) Detach(i);
                    continue;
                }
                // A released piece remains loose until deliberately snapped or RESET is pressed.
                // Teleporting fallen pieces home made a partially dismantled toy pop back together.
                if (p.holder != null) TrySnap(i, heldSnapDistance, heldSnapAngle);
            }
        }

        void LateUpdate()
        {
            bool active = Active;
            if (root != null && root.gameObject.activeSelf != active)
            {
                if (!active) foreach (var p in pieces) Release(p);
                root.gameObject.SetActive(active);
            }
            if (!active) { wasActive = false; return; }
            if (!wasActive) ResetBricks();
            bool moving = !wasActive;
            var bricks = world.asset.bricks;
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i];
                if (p.click >= 0)
                {
                    float t = Mathf.SmoothStep(0, 1, (Time.time - p.click) / clickSeconds);
                    p.t.SetLocalPositionAndRotation(Vector3.Lerp(p.clickFrom, bricks[i].center, t), Quaternion.Slerp(p.clickFromRotation, bricks[i].rotation, t));
                    if (t >= 1) p.click = -1;
                    moving = true;
                }
                else if (!p.attached) moving = true;
            }
            wasActive = true;
            if (!moving && !dirty) return;
            dirty = moving;
            var toLocal = transform.worldToLocalMatrix;
            for (int i = 0; i < pieces.Length; i++) matrices[i] = toLocal * pieces[i].t.localToWorldMatrix * pieces[i].restInverse;
            poses.SetData(matrices);
            var splats = world.Splats;
            shader.SetBuffer(kRigid, "_Source", splats.Source); shader.SetBuffer(kRigid, "_Deformed", splats.Deformed);
            shader.SetBuffer(kRigid, "_SplatBrick", splatBrick); shader.SetBuffer(kRigid, "_Poses", poses);
            shader.SetInt("_Count", world.asset.dynamicCount);
            shader.Dispatch(kRigid, (world.asset.dynamicCount + 63) / 64, 1, 1);
        }
        bool dirty;
    }
}
