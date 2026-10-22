using PhysicsHands.GaussianData;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PhysicsHands.Simulation
{
    // Sparse voxel cage around the occupied Gaussians (VR-GS style embedding), split into tetrahedra
    // that carry the learned Young's modulus with PhysDreamer's Poisson ratio.
    public sealed class ElasticCage
    {
        public const float Poisson = .3f;
        public readonly float spacing;
        public readonly Vector3[] rest;
        public readonly float[] inverseMass;
        public readonly int4[] tets;
        public readonly float3x3[] inverseRest;
        public readonly float[] restVolume, mu, lambda;
        public readonly int[] voxelNodes; // 8 per voxel, corner bit order x | y << 1 | z << 2
        public readonly Vector3[] voxelOrigin;
        public readonly uint[] splatVoxel;
        public readonly int[] sampleVoxel;
        public readonly Vector4[] sampleWeights; // 2 per sample: corners 0-3, 4-7
        public readonly int voxelCount, anchoredNodes;
        public float MaxVolumeChange(Unity.Collections.NativeArray<float3> x)
        {
            float max = 0;
            for (int e = 0; e < tets.Length; e++)
            {
                var t = tets[e];
                float3 x0 = x[t.x];
                max = math.max(max, math.abs(math.determinant(math.mul(new float3x3(x[t.y] - x0, x[t.z] - x0, x[t.w] - x0), inverseRest[e])) - 1));
            }

            return max;
        }

        public readonly float density, youngsMin, youngsMedian, youngsMax;
        static readonly Vector3Int[] Neighbours = BuildNeighbours();
        // Corner bit order x | y << 1 | z << 2. Even: central tet on even-parity corners {0,3,5,6}.
        static readonly int4[] EvenTets = {new(0, 3, 5, 6), new(1, 0, 3, 5), new(2, 0, 3, 6), new(4, 0, 5, 6), new(7, 3, 5, 6)};
        static readonly int4[] OddTets = {new(1, 2, 4, 7), new(0, 1, 2, 4), new(3, 1, 2, 7), new(5, 1, 4, 7), new(6, 2, 4, 7)};
        static readonly Vector3Int[] Faces = {Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back};
        static Vector3Int[] BuildNeighbours()
        {
            var list = new List<Vector3Int>();
            for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                        if (x != 0 || y != 0 || z != 0)
                            list.Add(new Vector3Int(x, y, z));
            return list.ToArray();
        }

        public ElasticCage(ModalModelAsset asset, float voxel, float totalMass, float fallbackYoungs, int minSplats = 2, int samplesPerVoxel = 4)
            : this(asset.renderData.splats, asset.renderData.splats.Length, asset.youngsSim, asset.frozen,
                asset.simUnitMeters, asset.simDensity, Poisson, voxel, totalMass, fallbackYoungs, minSplats, samplesPerVoxel, true) { }

        // Geometry and material coordinates are independent of the rendering backend. A source with
        // no fixed region remains free; only the legacy flower importer requests a bottom anchor.
        public ElasticCage(Gaussian[] splats, int countLimit, float[] youngs, bool[] frozen,
            float simUnitMeters, float simDensity, float poisson, float voxel, float totalMass,
            float fallbackYoungs, int minSplats = 2, int samplesPerVoxel = 4, bool inferBottomAnchor = false, bool solidVolume = false, bool[] attachmentSeeds = null)
        {
            if (countLimit <= 0 || countLimit > splats.Length || voxel <= 0 || totalMass < 0)
                throw new ArgumentException("Invalid elastic cage geometry or mass.");
            spacing = voxel;
            var bounds = new Bounds(splats[0].center, Vector3.zero);
            for (int i = 1; i < countLimit; i++) bounds.Encapsulate(splats[i].center);
            Vector3 origin = bounds.min - Vector3.one * voxel;
            var dims = Vector3Int.CeilToInt(bounds.size / voxel) + new Vector3Int(3, 3, 3);
            int cells = dims.x * dims.y * dims.z;
            int Cell(Vector3Int c) => c.x + dims.x * (c.y + dims.y * c.z);
            Vector3Int Coord(int i) => new(i % dims.x, i / dims.x % dims.y, i / (dims.x * dims.y));
            bool Inside(Vector3Int c) => c.x >= 0 && c.y >= 0 && c.z >= 0 && c.x < dims.x && c.y < dims.y && c.z < dims.z;
            Vector3Int CellOf(Vector3 p) => Vector3Int.FloorToInt((p - origin) / voxel);
            var count = new int[cells];
            var frozenCount = new int[cells];
            var seedCount = new int[cells];
            var youngsSum = new double[cells];
            bool material = youngs != null && youngs.Length == countLimit && simUnitMeters > 0, hasFrozen = frozen != null && frozen.Length == countLimit;
            for (int i = 0; i < countLimit; i++)
            {
                int c = Cell(CellOf(splats[i].center));
                count[c]++;
                if (hasFrozen && frozen[i])
                    frozenCount[c]++;
                if (attachmentSeeds != null && attachmentSeeds[i]) seedCount[c]++;
                if (material)
                    youngsSum[c] += youngs[i];
            }

            var label = new int[cells];
            for (int i = 0; i < cells; i++)
                label[i] = count[i] >= minSplats ? 0 : -1;
            if (solidVolume)
            {
                // Fill only enclosed voids. Open handles, leaf gaps and the outside remain empty.
                var outside = new bool[cells];
                var flood = new Queue<int>();
                for (int i = 0; i < cells; i++)
                {
                    var p = Coord(i);
                    if (label[i] < 0 && (p.x == 0 || p.y == 0 || p.z == 0 || p.x == dims.x - 1 || p.y == dims.y - 1 || p.z == dims.z - 1))
                    { outside[i] = true; flood.Enqueue(i); }
                }
                while (flood.Count > 0)
                {
                    var p = Coord(flood.Dequeue());
                    foreach (var d in Faces)
                    {
                        var q = p + d;
                        if (!Inside(q)) continue;
                        int c = Cell(q);
                        if (label[c] < 0 && !outside[c]) { outside[c] = true; flood.Enqueue(c); }
                    }
                }
                for (int i = 0; i < cells; i++) if (!outside[i]) label[i] = 0;
            }
            // Voxels touching only at an edge or corner form zero-energy hinges. Bridge such diagonal
            // contacts with an axis-aligned path so thin tilted stems and petals stay face-connected.
            var bridges = new List<int>();
            for (int i = 0; i < cells; i++)
            {
                if (label[i] != 0)
                    continue;
                var p = Coord(i);
                foreach (var d in Neighbours)
                {
                    if (Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z) < 2)
                        continue;
                    var q = p + d;
                    if (!Inside(q) || label[Cell(q)] != 0)
                        continue;
                    var step = p;
                    for (int axis = 0; axis < 2; axis++)
                    {
                        if (d[axis] == 0)
                            continue;
                        step[axis] += d[axis];
                        if (step != q && label[Cell(step)] != 0)
                            bridges.Add(Cell(step));
                    }
                }
            }

            foreach (int b in bridges)
                label[b] = 0;
            // Keep face-connected components attached to the boundary condition; floaters would drift away.
            bool Frozen(int c) => (inferBottomAnchor ? frozenCount[c] * 2 > count[c] : frozenCount[c] > 0) && count[c] > 0;
            bool anyFrozen = false;
            for (int i = 0; i < cells; i++)
                anyFrozen |= label[i] == 0 && Frozen(i);
            bool anyAttached = anyFrozen;
            for (int i = 0; i < cells; i++) anyAttached |= label[i] == 0 && seedCount[i] > 0;
            int lowest = int.MaxValue;
            if (!anyFrozen && inferBottomAnchor)
                for (int i = 0; i < cells; i++)
                    if (label[i] == 0)
                        lowest = Mathf.Min(lowest, Coord(i).y);
            bool Anchor(int c) => anyFrozen ? Frozen(c) : inferBottomAnchor && Coord(c).y == lowest;
            var queue = new Queue<int>();
            for (int i = 0; i < cells; i++)
                if (label[i] == 0 && (Anchor(i) || seedCount[i] > 0 || !anyAttached && !inferBottomAnchor))
                {
                    label[i] = 1;
                    queue.Enqueue(i);
                }

            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                var p = Coord(c);
                foreach (var d in Faces)
                {
                    var q = p + d;
                    if (!Inside(q))
                        continue;
                    int n = Cell(q);
                    if (label[n] == 0)
                    {
                        label[n] = 1;
                        queue.Enqueue(n);
                    }
                }
            }

            var voxelOfCell = new int[cells];
            var voxels = new List<int>();
            for (int i = 0; i < cells; i++)
            {
                voxelOfCell[i] = label[i] == 1 ? voxels.Count : -1;
                if (label[i] == 1)
                    voxels.Add(i);
            }

            voxelCount = voxels.Count;
            if (voxelCount == 0)
                throw new InvalidOperationException("No occupied voxels connected to the boundary condition.");
            if (totalMass == 0) totalMass = simDensity * voxelCount * voxel * voxel * voxel;
            // Nearest kept voxel for every cell (multi-source BFS), so floaters stay embedded.
            var nearest = (int[])voxelOfCell.Clone();
            foreach (int c in voxels)
                queue.Enqueue(c);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                var p = Coord(c);
                foreach (var d in Neighbours)
                {
                    var q = p + d;
                    if (!Inside(q))
                        continue;
                    int n = Cell(q);
                    if (nearest[n] < 0)
                    {
                        nearest[n] = nearest[c];
                        queue.Enqueue(n);
                    }
                }
            }

            density = totalMass / (voxelCount * voxel * voxel * voxel);
            // Preserve PhysDreamer's dynamics time scale: E / rho scales with length squared.
            float unit = material ? simUnitMeters : 0;
            var voxelYoungs = new float[voxelCount];
            for (int v = 0; v < voxelCount; v++)
            {
                int c = voxels[v];
                voxelYoungs[v] = material ? (float)(youngsSum[c] / Mathf.Max(1, count[c])) * density / simDensity * unit * unit : fallbackYoungs;
            }

            // Bridge voxels hold no splats; they inherit the mean modulus of their populated neighbours.
            for (int v = 0; v < voxelCount; v++)
            {
                int c = voxels[v];
                if (count[c] > 0)
                    continue;
                var p = Coord(c);
                float sum = 0;
                int n = 0;
                foreach (var d in Neighbours)
                {
                    var q = p + d;
                    if (!Inside(q))
                        continue;
                    int o = voxelOfCell[Cell(q)];
                    if (o >= 0 && count[voxels[o]] > 0)
                    {
                        sum += voxelYoungs[o];
                        n++;
                    }
                }

                voxelYoungs[v] = n > 0 ? sum / n : material ? fallbackYoungs * density / simDensity * unit * unit : fallbackYoungs;
            }

            var sorted = (float[])voxelYoungs.Clone();
            Array.Sort(sorted);
            youngsMin = sorted[0];
            youngsMedian = sorted[sorted.Length / 2];
            youngsMax = sorted[^1];
            var nodeOf = new Dictionary<Vector3Int, int>();
            var restList = new List<Vector3>();
            var massList = new List<float>();
            var anchored = new List<bool>();
            voxelNodes = new int[voxelCount * 8];
            voxelOrigin = new Vector3[voxelCount];
            for (int v = 0; v < voxelCount; v++)
            {
                var p = Coord(voxels[v]);
                voxelOrigin[v] = origin + (Vector3)p * voxel;
                bool anchor = Anchor(voxels[v]);
                for (int corner = 0; corner < 8; corner++)
                {
                    var key = p + new Vector3Int(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
                    if (!nodeOf.TryGetValue(key, out int node))
                    {
                        node = restList.Count;
                        nodeOf[key] = node;
                        restList.Add(origin + (Vector3)key * voxel);
                        massList.Add(0);
                        anchored.Add(false);
                    }

                    voxelNodes[v * 8 + corner] = node;
                    massList[node] += totalMass / voxelCount / 8;
                    anchored[node] |= anchor;
                }
            }

            rest = restList.ToArray();
            inverseMass = new float[rest.Length];
            for (int i = 0; i < rest.Length; i++)
            {
                inverseMass[i] = anchored[i] ? 0 : 1 / massList[i];
                if (anchored[i])
                    anchoredNodes++;
            }

            // Five tetrahedra per voxel, mirrored on odd voxels so face diagonals conform between neighbours.
            var tetList = new List<int4>();
            var inverse = new List<float3x3>();
            var volumes = new List<float>();
            var mus = new List<float>();
            var lambdas = new List<float>();
            for (int v = 0; v < voxelCount; v++)
            {
                var p = Coord(voxels[v]);
                bool odd = ((p.x + p.y + p.z) & 1) != 0;
                float mu = voxelYoungs[v] / (2 * (1 + poisson)), lambda = voxelYoungs[v] * poisson / ((1 + poisson) * (1 - 2 * poisson));
                foreach (var t in odd ? OddTets : EvenTets)
                {
                    var tet = new int4(voxelNodes[v * 8 + t.x], voxelNodes[v * 8 + t.y], voxelNodes[v * 8 + t.z], voxelNodes[v * 8 + t.w]);
                    float3 x0 = rest[tet.x];
                    var dm = new float3x3((float3)rest[tet.y] - x0, (float3)rest[tet.z] - x0, (float3)rest[tet.w] - x0);
                    float det = math.determinant(dm);
                    if (det < 0)
                    {
                        tet = tet.xzyw;
                        dm = new float3x3(dm.c1, dm.c0, dm.c2);
                        det = -det;
                    }

                    tetList.Add(tet);
                    inverse.Add(math.inverse(dm));
                    volumes.Add(det / 6);
                    mus.Add(mu);
                    lambdas.Add(lambda);
                }
            }

            tets = tetList.ToArray();
            inverseRest = inverse.ToArray();
            restVolume = volumes.ToArray();
            this.mu = mus.ToArray();
            this.lambda = lambdas.ToArray();
            splatVoxel = new uint[countLimit];
            if (samplesPerVoxel <= 0)
            {
                for (int i = 0; i < countLimit; i++) splatVoxel[i] = (uint)nearest[Cell(CellOf(splats[i].center))];
                sampleVoxel = Array.Empty<int>(); sampleWeights = Array.Empty<Vector4>();
                return;
            }
            var perVoxel = new List<int>[voxelCount];
            var seen = new int[voxelCount];
            var random = new System.Random(731);
            for (int i = 0; i < countLimit; i++)
            {
                var cell = CellOf(splats[i].center);
                int c = Cell(cell);
                splatVoxel[i] = (uint)nearest[c];
                int v = voxelOfCell[c];
                if (v < 0 || splats[i].color.w < .3f)
                    continue;
                var list = perVoxel[v] ??= new List<int>();
                int n = seen[v]++;
                if (n < samplesPerVoxel)
                    list.Add(i);
                else
                {
                    int k = random.Next(n + 1);
                    if (k < samplesPerVoxel)
                        list[k] = i;
                }
            }

            var sv = new List<int>();
            var sw = new List<Vector4>();
            for (int v = 0; v < voxelCount; v++)
            {
                if (perVoxel[v] == null)
                    continue;
                foreach (int i in perVoxel[v])
                {
                    Vector3 f = ((Vector3)splats[i].center - voxelOrigin[v]) / voxel;
                    var w = new float[8];
                    for (int corner = 0; corner < 8; corner++)
                        w[corner] = ((corner & 1) != 0 ? f.x : 1 - f.x) * ((corner & 2) != 0 ? f.y : 1 - f.y) * ((corner & 4) != 0 ? f.z : 1 - f.z);
                    sv.Add(v);
                    sw.Add(new Vector4(w[0], w[1], w[2], w[3]));
                    sw.Add(new Vector4(w[4], w[5], w[6], w[7]));
                }
            }

            sampleVoxel = sv.ToArray();
            sampleWeights = sw.ToArray();
        }
    }
}
