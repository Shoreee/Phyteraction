using System;
using System.Collections.Generic;
using System.Linq;
using PhysicsHands.GaussianWorld;
using UnityEngine;

namespace PhysicsHands.Editor.GaussianWorld
{
    // Splits a scanned toy assembly into rigid bricks. A scan carries no brick boundaries, so pieces are
    // colour-uniform, connected, brick-sized chunks of the surface: voxelise the moving splats, cluster
    // voxel colours in CIELAB (chroma weighted over lightness, so shading does not split a brick),
    // take connected regions per colour, cut regions larger than a brick with k-means on position,
    // fold slivers into their best-connected neighbour, and fit an oriented box to each piece (PCA).
    public static class BrickSegmentation
    {
        const float Voxel = .004f, Density = 1050, Fill = .3f; // ABS; a brick is a ~1.5 mm shell
        const int Colors = 6, TargetVoxels = 300, MinVoxels = 60;

        public static void Segment(MpmSceneAsset a)
        {
            int n = a.dynamicCount;
            var index = new Dictionary<Vector3Int, int>(); var cells = new List<Vector3Int>(); var weight = new List<float>(); var color = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                var g = a.splats[i]; float w = g.color.w; if (w < .1f) continue;
                var c = Cell(g.center);
                if (!index.TryGetValue(c, out int v)) { v = cells.Count; index[c] = v; cells.Add(c); weight.Add(0); color.Add(Vector3.zero); }
                weight[v] += w; color[v] += w * (Vector3)(Vector4)g.color;
            }
            int m = cells.Count; var lab = new Vector3[m];
            for (int v = 0; v < m; v++) lab[v] = Lab(color[v] / weight[v]);

            var label = KMeans(lab, weight, Colors, (p, q) => .25f * (p.x - q.x) * (p.x - q.x) + (p.y - q.y) * (p.y - q.y) + (p.z - q.z) * (p.z - q.z));
            // One majority pass removes single-voxel colour noise.
            var smooth = new int[m];
            for (int v = 0; v < m; v++)
            {
                var votes = new int[Colors]; votes[label[v]] += 2;
                foreach (int u in Neighbors(cells[v], index)) votes[label[u]]++;
                smooth[v] = Array.IndexOf(votes, votes.Max());
            }
            label = smooth;

            // Connected regions per colour, then brick-sized cuts of each.
            var piece = Enumerable.Repeat(-1, m).ToArray(); int pieces = 0;
            for (int v = 0; v < m; v++)
            {
                if (piece[v] >= 0) continue;
                var region = Flood(v, u => label[u] == label[v] && piece[u] < 0, cells, index); foreach (int u in region) piece[u] = -2;
                int k = Mathf.Max(1, Mathf.RoundToInt(region.Count / (float)TargetVoxels));
                var cut = KMeans(region.Select(u => (Vector3)cells[u]).ToArray(), region.Select(_ => 1f).ToList(), k, (p, q) => (p - q).sqrMagnitude);
                // A cut can be disconnected; each connected part is its own piece.
                var local = new Dictionary<int, int>(); for (int r = 0; r < region.Count; r++) local[region[r]] = cut[r];
                foreach (int u in region)
                {
                    if (piece[u] != -2) continue;
                    foreach (int w in Flood(u, x => piece[x] == -2 && local.TryGetValue(x, out int c) && c == local[u], cells, index)) piece[w] = pieces;
                    pieces++;
                }
            }
            MergeSlivers(piece, ref pieces, cells, index);

            // Oriented boxes, mass, support and adjacency.
            var members = new List<int>[pieces]; for (int p = 0; p < pieces; p++) members[p] = new List<int>();
            for (int v = 0; v < m; v++) members[piece[v]].Add(v);
            float bottom = a.splats.Take(n).Select(g => g.center.y).OrderBy(y => y).ElementAt((int)(.02f * (n - 1)));
            var bricks = new BrickPiece[pieces]; var neighbors = new List<int>();
            for (int p = 0; p < pieces; p++)
            {
                var points = members[p].Select(v => Center(cells[v])).ToList();
                var (center, rotation, half) = Box(points);
                var adjacent = new SortedSet<int>();
                foreach (int v in members[p]) foreach (int u in Neighbors(cells[v], index)) if (piece[u] != p) adjacent.Add(piece[u]);
                bricks[p] = new BrickPiece
                {
                    center = center, rotation = rotation, halfExtents = half, mass = Mathf.Max(.001f, 8 * half.x * half.y * half.z * Density * Fill),
                    neighborStart = neighbors.Count, neighborCount = adjacent.Count, grounded = points.Min(x => x.y) < bottom + .006f,
                };
                neighbors.AddRange(adjacent);
            }
            // Every moving splat (faint ones included) rides on the piece of its voxel or the nearest one.
            var splatBrick = new int[n];
            for (int i = 0; i < n; i++)
            {
                var c = Cell(a.splats[i].center); int found = -1; float best = float.MaxValue;
                for (int r = 0; r <= 3 && found < 0; r++)
                    for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++)
                    {
                        if (!index.TryGetValue(c + new Vector3Int(x, y, z), out int v)) continue;
                        float d = (Center(cells[v]) - (Vector3)a.splats[i].center).sqrMagnitude; if (d < best) { best = d; found = piece[v]; }
                    }
                if (found < 0)
                    for (int p = 0; p < pieces; p++) { float d = (bricks[p].center - (Vector3)a.splats[i].center).sqrMagnitude; if (d < best) { best = d; found = p; } }
                splatBrick[i] = found;
            }
            a.bricks = bricks; a.brickNeighbors = neighbors.ToArray(); a.splatBrick = splatBrick;
            Debug.Log($"Bricks {a.scene}: {pieces} pieces from {m} voxels ({Voxel * 1000:F0} mm), median {members.Select(x => x.Count).OrderBy(x => x).ElementAt(pieces / 2)} voxels, {bricks.Count(b => b.grounded)} on the support");
        }

        static Vector3Int Cell(Vector4 p) => new(Mathf.FloorToInt(p.x / Voxel), Mathf.FloorToInt(p.y / Voxel), Mathf.FloorToInt(p.z / Voxel));
        static Vector3 Center(Vector3Int c) => ((Vector3)c + Vector3.one * .5f) * Voxel;
        static IEnumerable<int> Neighbors(Vector3Int c, Dictionary<Vector3Int, int> index)
        {
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if ((x | y | z) != 0 && index.TryGetValue(c + new Vector3Int(x, y, z), out int u)) yield return u;
        }
        static List<int> Flood(int start, Func<int, bool> inside, List<Vector3Int> cells, Dictionary<Vector3Int, int> index)
        {
            var result = new List<int> { start }; var seen = new HashSet<int> { start };
            for (int head = 0; head < result.Count; head++)
                foreach (int u in Neighbors(cells[result[head]], index))
                    if (!seen.Contains(u) && inside(u)) { seen.Add(u); result.Add(u); }
            return result;
        }
        // Pieces under MinVoxels join the neighbour they share the most voxel contacts with.
        static void MergeSlivers(int[] piece, ref int pieces, List<Vector3Int> cells, Dictionary<Vector3Int, int> index)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                var size = new int[pieces]; foreach (int p in piece) size[p]++;
                bool changed = false;
                for (int p = 0; p < pieces; p++)
                {
                    if (size[p] == 0 || size[p] >= MinVoxels) continue;
                    var contact = new Dictionary<int, int>();
                    for (int v = 0; v < piece.Length; v++)
                        if (piece[v] == p) foreach (int u in Neighbors(cells[v], index)) if (piece[u] != p) contact[piece[u]] = contact.GetValueOrDefault(piece[u]) + 1;
                    // Isolated specks join the piece with the nearest voxel.
                    int into = contact.Count > 0 ? contact.OrderByDescending(kv => kv.Value).First().Key : Nearest(p, piece, cells);
                    if (into < 0) continue;
                    for (int v = 0; v < piece.Length; v++) if (piece[v] == p) piece[v] = into;
                    size[into] += size[p]; size[p] = 0; changed = true;
                }
                if (!changed) break;
            }
            var remap = new Dictionary<int, int>(); for (int v = 0; v < piece.Length; v++) { if (!remap.TryGetValue(piece[v], out int r)) remap[piece[v]] = r = remap.Count; piece[v] = r; }
            pieces = remap.Count;
        }
        static int Nearest(int p, int[] piece, List<Vector3Int> cells)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int v = 0; v < piece.Length; v++)
            {
                if (piece[v] != p) continue;
                for (int u = 0; u < piece.Length; u++)
                {
                    if (piece[u] == p) continue;
                    float d = (cells[u] - cells[v]).sqrMagnitude; if (d < bestD) { bestD = d; best = piece[u]; }
                }
            }
            return best;
        }
        // Principal axes of the voxel centres; half extents reach the outermost voxel's far face.
        static (Vector3 center, Quaternion rotation, Vector3 half) Box(List<Vector3> points)
        {
            var mean = Vector3.zero; foreach (var p in points) mean += p; mean /= points.Count;
            var c = new double[3, 3];
            foreach (var p in points) { var d = p - mean; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) c[i, j] += d[i] * d[j]; }
            var axes = Jacobi(c);
            var rotation = Quaternion.LookRotation(axes[2], axes[1]);
            Vector3 lo = Vector3.one * float.MaxValue, hi = -lo;
            foreach (var p in points) { var q = Quaternion.Inverse(rotation) * (p - mean); lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q); }
            var half = Vector3.Max((hi - lo) * .5f + Vector3.one * (Voxel * .5f), Vector3.one * .002f);
            return (mean + rotation * ((lo + hi) * .5f), rotation, half);
        }
        // Eigenvectors of a symmetric 3x3 matrix (cyclic Jacobi), as a right-handed orthonormal frame.
        static Vector3[] Jacobi(double[,] a)
        {
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 32; sweep++)
                for (int p = 0; p < 2; p++) for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-15) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]), t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double cos = 1 / Math.Sqrt(t * t + 1), sin = t * cos;
                    for (int k = 0; k < 3; k++) { double kp = a[k, p], kq = a[k, q]; a[k, p] = cos * kp - sin * kq; a[k, q] = sin * kp + cos * kq; }
                    for (int k = 0; k < 3; k++) { double pk = a[p, k], qk = a[q, k]; a[p, k] = cos * pk - sin * qk; a[q, k] = sin * pk + cos * qk; }
                    for (int k = 0; k < 3; k++) { double kp = v[k, p], kq = v[k, q]; v[k, p] = cos * kp - sin * kq; v[k, q] = sin * kp + cos * kq; }
                }
            var x = new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]).normalized;
            var y = new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]).normalized;
            return new[] { x, y, Vector3.Cross(x, y).normalized };
        }
        // Weighted k-means with k-means++ seeding (fixed seed: the segmentation is reproducible).
        static int[] KMeans(Vector3[] x, List<float> w, int k, Func<Vector3, Vector3, float> distance)
        {
            int n = x.Length; k = Mathf.Min(k, n); var label = new int[n]; if (k <= 1) return label;
            var random = new System.Random(1); var centers = new List<Vector3> { x[random.Next(n)] }; var d = new float[n];
            while (centers.Count < k)
            {
                double total = 0; for (int i = 0; i < n; i++) { d[i] = centers.Min(c => distance(x[i], c)) * w[i]; total += d[i]; }
                double pick = random.NextDouble() * total; int chosen = n - 1;
                for (int i = 0; i < n; i++) { pick -= d[i]; if (pick <= 0) { chosen = i; break; } }
                centers.Add(x[chosen]);
            }
            for (int iteration = 0; iteration < 25; iteration++)
            {
                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    int best = 0; float bestD = float.MaxValue;
                    for (int c = 0; c < k; c++) { float dc = distance(x[i], centers[c]); if (dc < bestD) { bestD = dc; best = c; } }
                    if (label[i] != best) { label[i] = best; moved = true; }
                }
                var sum = new Vector3[k]; var mass = new float[k];
                for (int i = 0; i < n; i++) { sum[label[i]] += w[i] * x[i]; mass[label[i]] += w[i]; }
                for (int c = 0; c < k; c++) if (mass[c] > 0) centers[c] = sum[c] / mass[c];
                if (!moved && iteration > 0) break;
            }
            return label;
        }
        // sRGB (0-1) to CIELAB, D65.
        static Vector3 Lab(Vector3 srgb)
        {
            float L(float c) => c <= .04045f ? c / 12.92f : Mathf.Pow((c + .055f) / 1.055f, 2.4f);
            float r = L(Mathf.Clamp01(srgb.x)), g = L(Mathf.Clamp01(srgb.y)), b = L(Mathf.Clamp01(srgb.z));
            float X = (.4124f * r + .3576f * g + .1805f * b) / .95047f, Y = .2126f * r + .7152f * g + .0722f * b, Z = (.0193f * r + .1192f * g + .9505f * b) / 1.08883f;
            float F(float t) => t > .008856f ? Mathf.Pow(t, 1 / 3f) : 7.787f * t + 16 / 116f;
            return new Vector3(116 * F(Y) - 16, 500 * (F(X) - F(Y)), 200 * (F(Y) - F(Z)));
        }
    }
}
