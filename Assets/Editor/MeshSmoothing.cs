using System.Collections.Generic;
using UnityEngine;

namespace CharacterPlayground.EditorTools
{
    /// <summary>
    /// Turns the coarse, hard-edged boxes of an exported body part into a smooth, rounded
    /// surface: vertices split along hard edges are welded back together, the triangles are
    /// subdivided with Loop's rules (which round every edge), and normals are averaged so the
    /// shading is continuous. Positions are kept in whatever space they come in.
    /// </summary>
    static class MeshSmoothing
    {
        const float WeldGrid = 1e-4f;

        /// <summary>
        /// Merges vertices that sit at the same position (within the weld grid, whichever cell
        /// they fall in), dropping triangles that collapse. Tags, one per triangle, are kept in
        /// step with the triangles.
        /// </summary>
        public static void Weld(List<Vector3> vertices, List<int> triangles, List<int> triangleTags = null)
        {
            var cells = new Dictionary<Vector3Int, List<int>>();
            var remap = new int[vertices.Count];
            var welded = new List<Vector3>();
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 p = vertices[i];
                var cell = new Vector3Int(Mathf.FloorToInt(p.x / WeldGrid), Mathf.FloorToInt(p.y / WeldGrid), Mathf.FloorToInt(p.z / WeldGrid));
                int index = -1;
                for (int dx = -1; dx <= 1 && index < 0; dx++)
                for (int dy = -1; dy <= 1 && index < 0; dy++)
                for (int dz = -1; dz <= 1 && index < 0; dz++)
                {
                    if (!cells.TryGetValue(cell + new Vector3Int(dx, dy, dz), out List<int> near)) continue;
                    foreach (int j in near)
                    {
                        if ((welded[j] - p).sqrMagnitude <= WeldGrid * WeldGrid) { index = j; break; }
                    }
                }
                if (index < 0)
                {
                    index = welded.Count;
                    welded.Add(p);
                    if (!cells.TryGetValue(cell, out List<int> own)) cells[cell] = own = new List<int>(1);
                    own.Add(index);
                }
                remap[i] = index;
            }

            var kept = new List<int>(triangles.Count);
            var keptTags = triangleTags != null ? new List<int>(triangleTags.Count) : null;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = remap[triangles[t]], b = remap[triangles[t + 1]], c = remap[triangles[t + 2]];
                if (a == b || b == c || c == a) continue;
                kept.Add(a); kept.Add(b); kept.Add(c);
                keptTags?.Add(triangleTags[t / 3]);
            }
            vertices.Clear();
            vertices.AddRange(welded);
            triangles.Clear();
            triangles.AddRange(kept);
            if (triangleTags != null)
            {
                triangleTags.Clear();
                triangleTags.AddRange(keptTags);
            }
        }

        /// <summary>
        /// One step of Loop subdivision: every triangle becomes four, every edge gets rounded.
        /// Open rims are subdivided as curves of their own, so a piece cut from a larger surface
        /// keeps its outline.
        /// </summary>
        public static void Subdivide(List<Vector3> vertices, List<int> triangles)
        {
            int count = vertices.Count;
            var neighbours = new HashSet<int>[count];
            var opposite = new Dictionary<long, List<int>>();
            for (int i = 0; i < count; i++) neighbours[i] = new HashSet<int>();
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Link(neighbours, opposite, a, b, c);
                Link(neighbours, opposite, b, c, a);
                Link(neighbours, opposite, c, a, b);
            }

            // An open edge (one triangle only) marks the rim of a piece that is not a closed shell,
            // such as a cut made by a segmentation tool. Rim vertices follow the rim only, so the
            // rim keeps its place instead of being pulled inside and opening a gap to the next piece.
            var rim = new List<int>[count];
            foreach (KeyValuePair<long, List<int>> edge in opposite)
            {
                if (edge.Value.Count != 1) continue;
                int a = (int)(edge.Key >> 32), b = (int)(edge.Key & 0xFFFFFFFF);
                (rim[a] ??= new List<int>(2)).Add(b);
                (rim[b] ??= new List<int>(2)).Add(a);
            }

            // Existing vertices move towards the average of their neighbours (Loop's weights).
            var result = new List<Vector3>(count * 4);
            for (int i = 0; i < count; i++)
            {
                int n = neighbours[i].Count;
                if (n < 2)
                {
                    result.Add(vertices[i]);
                    continue;
                }
                if (rim[i] != null)
                {
                    result.Add(rim[i].Count == 2
                        ? 0.75f * vertices[i] + 0.125f * (vertices[rim[i][0]] + vertices[rim[i][1]])
                        : vertices[i]);
                    continue;
                }
                float beta = n == 3 ? 3f / 16f : 3f / (8f * n);
                Vector3 sum = Vector3.zero;
                foreach (int j in neighbours[i]) sum += vertices[j];
                result.Add((1f - n * beta) * vertices[i] + beta * sum);
            }

            // New vertices on every edge, pulled towards the two triangles that share it.
            var midpoints = new Dictionary<long, int>();
            var split = new List<int>(triangles.Count * 4);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                int ab = Midpoint(vertices, result, opposite, midpoints, a, b);
                int bc = Midpoint(vertices, result, opposite, midpoints, b, c);
                int ca = Midpoint(vertices, result, opposite, midpoints, c, a);
                split.Add(a); split.Add(ab); split.Add(ca);
                split.Add(b); split.Add(bc); split.Add(ab);
                split.Add(c); split.Add(ca); split.Add(bc);
                split.Add(ab); split.Add(bc); split.Add(ca);
            }
            vertices.Clear();
            vertices.AddRange(result);
            triangles.Clear();
            triangles.AddRange(split);
        }

        /// <summary>Area-weighted vertex normals, so shading flows across every edge.</summary>
        public static List<Vector3> Normals(List<Vector3> vertices, List<int> triangles)
        {
            var normals = new Vector3[vertices.Count];
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
            var result = new List<Vector3>(vertices.Count);
            foreach (Vector3 n in normals) result.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
            return result;
        }

        static void Link(HashSet<int>[] neighbours, Dictionary<long, List<int>> opposite, int a, int b, int c)
        {
            neighbours[a].Add(b);
            neighbours[b].Add(a);
            long key = EdgeKey(a, b);
            if (!opposite.TryGetValue(key, out List<int> across)) opposite[key] = across = new List<int>(2);
            across.Add(c);
        }

        static int Midpoint(List<Vector3> old, List<Vector3> result, Dictionary<long, List<int>> opposite, Dictionary<long, int> midpoints, int a, int b)
        {
            long key = EdgeKey(a, b);
            if (midpoints.TryGetValue(key, out int index)) return index;
            List<int> across = opposite[key];
            Vector3 p = across.Count >= 2
                ? 0.375f * (old[a] + old[b]) + 0.125f * (old[across[0]] + old[across[1]])
                : 0.5f * (old[a] + old[b]);
            index = result.Count;
            result.Add(p);
            midpoints[key] = index;
            return index;
        }

        static long EdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }
    }
}
