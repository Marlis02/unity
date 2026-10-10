using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Shared by the builders that rig a model in code (MinifigCharacterBuilder): skin weights
// handed down chains of bones, the Humanoid avatar for a skeleton named after Unity's humanoid bones, the RobloxFace grid,
// Tripo's hair fitted to other heads and saving generated assets in place. Positions are in character space: metres, standing on the origin facing +Z,
// the character's left side at -X.
public static class RigUtility
{
    const int FaceGridCells = 16;

    // 0 above a soft band of half-width `half` around `joint`, 1 below it
    public static float Below(float y, float joint, float half) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(joint + half, joint - half, y));

    // Shares `amount` out down a chain of bones; handovers[k] (0..1) is how far past the joint between bone k and k + 1
    // the vertex is
    public static void Chain(Dictionary<string, float> share, float amount, string[] chain, float[] handovers)
    {
        float left = amount;
        for (int k = 0; k < chain.Length; k++)
        {
            float past = k < handovers.Length ? handovers[k] : 0f;
            float here = left * (1f - past);
            if (here > 0f) share[chain[k]] = share.TryGetValue(chain[k], out float w) ? w + here : here;
            left -= here;
        }
    }

    // The four heaviest bones, heaviest first, normalised
    public static BoneWeight Pack(Dictionary<string, float> share, List<string> boneNames)
    {
        var top = new List<KeyValuePair<string, float>>(share);
        top.Sort((a, b) => b.Value.CompareTo(a.Value));
        if (top.Count > 4) top.RemoveRange(4, top.Count - 4);
        float total = 0f;
        foreach (var pair in top) total += pair.Value;
        var w = new BoneWeight();
        for (int k = 0; k < top.Count; k++)
        {
            int index = boneNames.IndexOf(top[k].Key);
            float weight = top[k].Value / total;
            switch (k)
            {
                case 0: w.boneIndex0 = index; w.weight0 = weight; break;
                case 1: w.boneIndex1 = index; w.weight1 = weight; break;
                case 2: w.boneIndex2 = index; w.weight2 = weight; break;
                default: w.boneIndex3 = index; w.weight3 = weight; break;
            }
        }
        return w;
    }

    // ---------------------------------------------------------------- parts

    // Models made in parts (Tripo's) leave each part open where it met its neighbour (the torso has holes for the arms,
    // legs and neck), and a turned joint shows the hole. Closes every opening with a fan around its centre: clean
    // boundary loops are walked edge by edge; ragged ones (Tripo's sometimes branch, so the walk never gets back to its
    // start) are gathered into connected pieces, each boundary edge getting its own triangle from the middle, facing
    // away from the part's middle, so the cap meets the ragged edge exactly and nothing shows through between them.
    public static Mesh Capped(Mesh source)
    {
        var vertices = new List<Vector3>(source.vertices);
        var normals = new List<Vector3>(source.normals);
        var triangles = new List<int>(source.triangles);
        var middle = source.bounds.center;

        // Vertices are split along UV/normal seams: weld by position to find the real boundary
        var ids = new Dictionary<Vector3, int>();
        var positions = new List<Vector3>();
        var surfaceNormals = new List<Vector3>(); // the part's own normal at each welded position
        var weld = new int[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            if (!ids.TryGetValue(vertices[i], out int id))
            {
                id = positions.Count;
                ids[vertices[i]] = id;
                positions.Add(vertices[i]);
                surfaceNormals.Add(normals[i]);
            }
            weld[i] = id;
        }
        var edgeUse = new Dictionary<(int, int), int>();
        for (int i = 0; i < triangles.Count; i += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = weld[triangles[i + k]], b = weld[triangles[i + (k + 1) % 3]];
                var key = a < b ? (a, b) : (b, a);
                edgeUse[key] = edgeUse.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        // Boundary edges walked against the triangles' winding, so the caps face outwards
        var next = new Dictionary<int, int>();
        var boundary = new Dictionary<int, List<int>>(); // undirected, for the ragged pieces
        for (int i = 0; i < triangles.Count; i += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = weld[triangles[i + k]], b = weld[triangles[i + (k + 1) % 3]];
                if (edgeUse[a < b ? (a, b) : (b, a)] != 1) continue;
                next[b] = a;
                if (!boundary.TryGetValue(a, out var fromA)) boundary[a] = fromA = new List<int>();
                if (!boundary.TryGetValue(b, out var fromB)) boundary[b] = fromB = new List<int>();
                fromA.Add(b);
                fromB.Add(a);
            }

        // Own vertices with the cap's normal, so the part's smooth shading is left alone
        void Cap(List<int> loop, Vector3 center, Vector3 normal)
        {
            int c = vertices.Count;
            vertices.Add(center);
            normals.Add(normal);
            foreach (int w in loop)
            {
                vertices.Add(positions[w]);
                normals.Add(normal);
            }
            for (int k = 0; k < loop.Count; k++) triangles.AddRange(new[] { c, c + 1 + k, c + 1 + (k + 1) % loop.Count });
        }

        var capped = new HashSet<int>();
        var visited = new HashSet<int>();
        foreach (int start in new List<int>(next.Keys))
        {
            var loop = new List<int>();
            int v = start;
            while (!visited.Contains(v) && next.ContainsKey(v))
            {
                visited.Add(v);
                loop.Add(v);
                v = next[v];
            }
            if (v != start || loop.Count < 3) continue;

            var center = Vector3.zero;
            foreach (int w in loop) center += positions[w];
            center /= loop.Count;
            var normal = Vector3.zero;
            for (int k = 0; k < loop.Count; k++)
                normal += Vector3.Cross(positions[loop[k]] - center, positions[loop[(k + 1) % loop.Count]] - center);
            Cap(loop, center, CapNormal(normal, loop.ConvertAll(w => surfaceNormals[w])));
            capped.UnionWith(loop);
        }

        // What's left is ragged: each connected piece of it is closed facing away from the part's middle
        var seen = new HashSet<int>(capped);
        foreach (int start in boundary.Keys)
        {
            if (!seen.Add(start)) continue;
            var piece = new List<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                piece.Add(v);
                foreach (int w in boundary[v])
                    if (seen.Add(w)) stack.Push(w);
            }
            if (piece.Count < 3) continue;

            var center = Vector3.zero;
            foreach (int w in piece) center += positions[w];
            center /= piece.Count;
            var outward = CapNormal(center - middle, piece.ConvertAll(w => surfaceNormals[w]));
            int c = vertices.Count;
            vertices.Add(center);
            normals.Add(outward);
            var corner = new Dictionary<int, int>();
            foreach (int w in piece)
            {
                corner[w] = vertices.Count;
                vertices.Add(positions[w]);
                normals.Add(outward);
            }
            foreach (int a in piece)
                foreach (int b in boundary[a])
                {
                    if (b < a || !corner.ContainsKey(b)) continue; // each edge once, within this piece
                    // A triangle faces the way the cross product of its edges points: turn it outwards
                    bool outwards = Vector3.Dot(Vector3.Cross(positions[a] - center, positions[b] - center), outward) > 0f;
                    triangles.AddRange(outwards ? new[] { c, corner[a], corner[b] } : new[] { c, corner[b], corner[a] });
                }
        }

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // A cap's normal from the sum of its fan's cross products, or, where that comes to (almost) nothing (a sliver of a
    // hole, or a loop folding back on itself), from the normals of the surface round it. Normalised by hand: Unity's
    // Normalize() zeroes short vectors, and a zero normal lights as NaN, which bloom blows up into a white blaze.
    public static Vector3 CapNormal(Vector3 crossSum, IEnumerable<Vector3> around)
    {
        float length = crossSum.magnitude;
        if (length > 1e-12f) return crossSum / length;
        var sum = Vector3.zero;
        foreach (var n in around) sum += n;
        length = sum.magnitude;
        return length > 1e-12f ? sum / length : Vector3.up;
    }

    // ---------------------------------------------------------------- face

    // A grid wrapped onto the front of the head (triangle corner triples) so the face follows its curve, `lift` metres
    // off it; UVs span 0..1 like a quad, for RobloxFace. The atlas cell's brows sit at v = 0.9, its mouth at 0.12.
    public static Mesh FaceGrid(List<Vector3> head, float centerY, float size, float lift, float centerX = 0f)
    {
        int n = FaceGridCells + 1;
        var vertices = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
        {
            float u = c / (float)FaceGridCells, v = r / (float)FaceGridCells;
            // Seen from the front (+Z), the face's left edge (u = 0) is on the body's right (+X)
            float x = centerX + (0.5f - u) * size, y = centerY + (v - 0.5f) * size;
            vertices[r * n + c] = new Vector3(x, y, FrontZ(head, x, y) + lift);
            uvs[r * n + c] = new Vector2(u, v);
        }
        // Corners past a rounded chin hit nothing: walking out from the middle column, give them their inner neighbour's depth
        int middle = FaceGridCells / 2;
        for (int r = 0; r < n; r++)
        for (int k = 1; k <= middle; k++)
        foreach (int c in new[] { middle - k, middle + k })
        {
            int inner = r * n + (c < middle ? c + 1 : c - 1);
            if (float.IsNaN(vertices[r * n + c].z)) vertices[r * n + c].z = vertices[inner].z;
        }
        var triangles = new List<int>();
        for (int r = 0; r < FaceGridCells; r++)
        for (int c = 0; c < FaceGridCells; c++)
        {
            int i = r * n + c;
            triangles.AddRange(new[] { i, i + n, i + n + 1, i, i + n + 1, i + 1 });
        }
        var mesh = new Mesh { vertices = vertices, uv = uvs };
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Front-most z of the triangles (corner triples) along the line through (x, y) parallel to Z; NaN if none is crossed
    static float FrontZ(List<Vector3> t, float x, float y)
    {
        float front = float.NegativeInfinity;
        for (int i = 0; i < t.Count; i += 3)
        {
            Vector3 a = t[i], b = t[i + 1], c = t[i + 2];
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-12f) continue;
            float wa = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / d;
            float wb = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / d;
            float wc = 1f - wa - wb;
            if (wa < 0f || wb < 0f || wc < 0f) continue;
            front = Mathf.Max(front, wa * a.z + wb * b.z + wc * c.z);
        }
        return float.IsNegativeInfinity(front) ? float.NaN : front;
    }

    // ---------------------------------------------------------------- avatar

    // The bones as built are unrotated, the limbs in whatever pose the model has; the avatar's reference pose is a
    // T-pose, so each limb is aimed straight out (arms) or down (legs) here, the lower bone lining up with the upper one
    public static Avatar BuildAvatar(GameObject root, Dictionary<string, Transform> bones, IEnumerable<string> boneNames)
    {
        Vector3 Segment(string from, string to) => bones[to].position - bones[from].position;
        var tPose = new Dictionary<string, Quaternion>();
        foreach (var n in new[] { "Left", "Right" })
        {
            var outward = n == "Left" ? Vector3.left : Vector3.right;
            tPose[n + "UpperArm"] = Quaternion.FromToRotation(Segment(n + "UpperArm", n + "LowerArm"), outward);
            tPose[n + "LowerArm"] = Quaternion.FromToRotation(Segment(n + "LowerArm", n + "Hand"), Segment(n + "UpperArm", n + "LowerArm"));
            tPose[n + "UpperLeg"] = Quaternion.FromToRotation(Segment(n + "UpperLeg", n + "LowerLeg"), Vector3.down);
            tPose[n + "LowerLeg"] = Quaternion.FromToRotation(Segment(n + "LowerLeg", n + "Foot"), Segment(n + "UpperLeg", n + "LowerLeg"));
        }

        var human = new List<HumanBone>();
        var skeleton = new List<SkeletonBone>
        {
            new SkeletonBone { name = root.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one },
        };
        foreach (var bone in boneNames)
        {
            human.Add(new HumanBone { boneName = bone, humanName = bone, limit = new HumanLimit { useDefaultValues = true } });
            skeleton.Add(new SkeletonBone
            {
                name = bone, position = bones[bone].localPosition,
                rotation = tPose.TryGetValue(bone, out var rotation) ? rotation : Quaternion.identity, scale = Vector3.one,
            });
        }
        var description = new HumanDescription
        {
            human = human.ToArray(),
            skeleton = skeleton.ToArray(),
            // No twist bones: put all of a limb's twist on the bone itself so it turns the elbow/knee
            upperArmTwist = 1f,
            lowerArmTwist = 1f,
            upperLegTwist = 1f,
            lowerLegTwist = 1f,
            armStretch = 0.05f,
            legStretch = 0.05f,
            feetSpacing = 0f,
            hasTranslationDoF = false,
        };
        var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
        if (!avatar.isValid || !avatar.isHuman) Debug.LogError($"[Rig] Avatar for {root.name} is not a valid humanoid");
        return avatar;
    }

    // ---------------------------------------------------------------- hair

    // The hair styles: Tripo_Hair_<name> meshes, each fitted with a scalp onto the round Tripo_Part5 head (both kept from
    // the retired Tripo characters, whose builder made them)
    public const string HairFolder = "Assets/Characters/Hair"; // Tripo hair styles and the Tripo head they fit

    // One of the hair meshes (HairFolder/Tripo_Hair_<name>) stretched box to box onto another head, in that head's space;
    // `inflate` leaves a little room at the corners of a boxier head
    public static Mesh StretchTripoHair(string hair, Bounds head, float inflate)
    {
        var tripo = AssetDatabase.LoadAssetAtPath<Mesh>($"{HairFolder}/Tripo_Hair_{hair}.asset");
        var tripoHead = AssetDatabase.LoadAssetAtPath<Mesh>($"{HairFolder}/Tripo_Part5.asset").bounds;
        var stretch = new Vector3(head.extents.x / tripoHead.extents.x, head.extents.y / tripoHead.extents.y,
            head.extents.z / tripoHead.extents.z) * inflate;
        var vertices = tripo.vertices;
        var normals = tripo.normals;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = head.center + Vector3.Scale(vertices[i] - tripoHead.center, stretch);
            normals[i] = new Vector3(normals[i].x / stretch.x, normals[i].y / stretch.y, normals[i].z / stretch.z).normalized;
        }
        var mesh = new Mesh { indexFormat = tripo.indexFormat };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tripo.triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------- assets

    // A colour from its RRGGBB code
    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    public static Avatar SaveAvatar(Avatar avatar, string path)
    {
        avatar.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<Avatar>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(avatar, path);
            return avatar;
        }
        // Overwrite in place: deleting and recreating the asset leaves prefabs pointing at nothing
        EditorUtility.CopySerialized(avatar, existing);
        Object.DestroyImmediate(avatar);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    public static Mesh SaveMesh(Mesh generated, string path)
    {
        generated.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }
        // Overwrite in place so prefabs keep their mesh references. Copy through the Mesh API: after
        // EditorUtility.CopySerialized, renderers already on screen keep drawing the old geometry
        mesh.Clear();
        mesh.indexFormat = generated.indexFormat;
        mesh.SetVertices(generated.vertices);
        mesh.SetNormals(generated.normals);
        if (generated.uv.Length > 0) mesh.SetUVs(0, generated.uv);
        mesh.boneWeights = generated.boneWeights;
        mesh.bindposes = generated.bindposes;
        mesh.subMeshCount = generated.subMeshCount;
        for (int i = 0; i < generated.subMeshCount; i++) mesh.SetTriangles(generated.GetTriangles(i), i);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        Object.DestroyImmediate(generated);
        return mesh;
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
