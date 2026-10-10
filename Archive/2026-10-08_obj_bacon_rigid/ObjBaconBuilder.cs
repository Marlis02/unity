using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// The bacon from Roblox Studio (Imports/bacon.obj, an R15 avatar saved with Export Selection; it came without its .mtl,
// so no colours or face). Its 15 body parts and the hair go on a humanoid skeleton named like main_hero's (no
// clavicles), so the shorts' posing code drives it: each part on its own bone, softened at the joints (JointBand). The OBJ is in studs, right-handed, facing -Z: here
// it is scaled by StudToMetre, mirrored to Unity's left-handed axes and turned to face +Z, standing on the origin.
// The main character since 2026-10-08. Skin and hair stay the plain grey the model came in (the user found that the
// most Roblox-like); he wears a black T-shirt with the play logo on the chest, sleeves to just over the elbows, dark
// blue jeans and white trainers. The RobloxFace grid on the head is built but switched off (WithFace) while he is
// faceless; the hair can be taken off the same way (WithHair). Rebuild with
// Tools/Characters/Build OBJ Bacon.
public static class ObjBaconBuilder
{
    const string Source = "Imports/bacon.obj";
    public const string Folder = "Assets/Characters/ObjBacon";
    public const string PrefabPath = Folder + "/Prefabs/obj_bacon.prefab";
    const string MeshFolder = Folder + "/Meshes", MaterialFolder = Folder + "/Materials";
    const float StudToMetre = 0.33f;

    // Which bone each OBJ group rides on, and its colour. Roblox's character faces -Z with its right on +X, so the
    // right limbs are the groups on the +X side.
    static readonly (string group, string bone, string color)[] Parts =
    {
        ("Rig10", "Head", Skin), ("Handle1", "Head", Hair),
        ("Rig15", "Spine", Shirt), ("Rig14", "Hips", Jeans),
        ("Rig9", "LeftUpperArm", Shirt), ("Rig8", "LeftLowerArm", Skin), ("Rig7", "LeftHand", Skin),
        ("Rig13", "RightUpperArm", Shirt), ("Rig12", "RightLowerArm", Skin), ("Rig11", "RightHand", Skin),
        ("Rig6", "LeftUpperLeg", Jeans), ("Rig5", "LeftLowerLeg", Jeans), ("Rig4", "LeftFoot", Shoes),
        ("Rig3", "RightUpperLeg", Jeans), ("Rig2", "RightLowerLeg", Jeans), ("Rig1", "RightFoot", Shoes),
    };
    // R15 parts run far into each other (a forearm 15 cm up into the upper arm, a shin 23 cm up into the thigh). Where
    // the colour changes, both parts meeting there are cut straight across on one plane, so the line is a clean sleeve
    // end or hem whichever surface is outside, not the ragged line where the two meshes cut through each other: the
    // sleeves end SleeveAboveElbow over the elbow, the T-shirt comes down over the waist to the bottom of the upper
    // torso. Not the trainers: the shins reach so far into them that they would turn blue.
    const float SleeveAboveElbow = 0.03f;
    // Soft joints, as on main_hero: within JointBand of the waist, elbows, wrists, hips and knees the two parts meeting
    // there hand over to each other's bone, half and half at the joint, so their overlapping ends bend together like
    // rubber instead of the body showing its cuts. Shoulders and neck stay rigid, Roblox-style, and so do the ankles:
    // the trainers' long triangles stretched into white shards poking out of the jeans. A soft joint is named by the
    // bone below it; Chain is the bone below each one, along the body.
    const float JointBand = 0.05f;
    static readonly HashSet<string> SoftJoints = new HashSet<string>
    {
        "Spine", "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand", "LeftUpperLeg", "RightUpperLeg",
        "LeftLowerLeg", "RightLowerLeg",
    };
    static readonly Dictionary<string, string> Chain = new Dictionary<string, string>
    {
        ["Hips"] = "Spine", ["Spine"] = "Head",
        ["LeftUpperArm"] = "LeftLowerArm", ["LeftLowerArm"] = "LeftHand", ["RightUpperArm"] = "RightLowerArm", ["RightLowerArm"] = "RightHand",
        ["LeftUpperLeg"] = "LeftLowerLeg", ["LeftLowerLeg"] = "LeftFoot", ["RightUpperLeg"] = "RightLowerLeg", ["RightLowerLeg"] = "RightFoot",
    };
    // Our first colouring (short-4 test) was skin EBC79E, hair A64F17, jeans 2A2B31
    const string Skin = "C7C7C7", Hair = "C7C7C7", Shirt = "1E1E22", Jeans = "223A64", Shoes = "F2F2F2";
    const bool WithFace = false, WithHair = true;
    const string LogoPath = Folder + "/PlayLogo.png"; // from Imports/play_logo_source.png, cropped to the rounded square
    const float LogoWidth = 0.36f, LogoHeight = 0.67f; // of the upper torso's width; centre up its height

    public static readonly string[] BoneNames =
    {
        "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    };

    [MenuItem("Tools/Characters/Build OBJ Bacon")]
    public static void Build()
    {
        foreach (var folder in new[] { Folder + "/Prefabs", MeshFolder, MaterialFolder }) RigUtility.EnsureFolder(folder);
        var faceMaterial = RobloxDecalAtlasBuilder.FaceMaterial();
        var groups = ReadObj(Source);

        // Joints from where the parts overlap: each R15 part runs a little into the next
        Bounds B(string g) => groups[g].bounds;
        var joints = new Dictionary<string, Vector3>();
        foreach (var (side, upperArm, lowerArm, hand, upperLeg, lowerLeg, foot) in new[]
        {
            ("Left", "Rig9", "Rig8", "Rig7", "Rig6", "Rig5", "Rig4"),
            ("Right", "Rig13", "Rig12", "Rig11", "Rig3", "Rig2", "Rig1"),
        })
        {
            joints[side + "UpperArm"] = new Vector3(B(upperArm).center.x, B(upperArm).max.y - 0.09f, B(upperArm).center.z);
            joints[side + "LowerArm"] = new Vector3(B(lowerArm).center.x, (B(upperArm).min.y + B(lowerArm).max.y) / 2f, B(lowerArm).center.z);
            joints[side + "Hand"] = new Vector3(B(hand).center.x, (B(lowerArm).min.y + B(hand).max.y) / 2f, B(lowerArm).center.z);
            joints[side + "UpperLeg"] = new Vector3(B(upperLeg).center.x, B(upperLeg).max.y - 0.08f, B(upperLeg).center.z);
            joints[side + "LowerLeg"] = new Vector3(B(lowerLeg).center.x, (B(upperLeg).min.y + B(lowerLeg).max.y) / 2f, B(lowerLeg).center.z);
            joints[side + "Foot"] = new Vector3(B(lowerLeg).center.x, (B(lowerLeg).min.y + B(foot).max.y) / 2f, B(lowerLeg).center.z);
        }
        joints["Hips"] = new Vector3(0f, joints["LeftUpperLeg"].y, B("Rig14").center.z);
        joints["Spine"] = new Vector3(0f, (B("Rig14").max.y + B("Rig15").min.y) / 2f, B("Rig15").center.z);
        joints["Head"] = new Vector3(0f, (B("Rig15").max.y + B("Rig10").min.y) / 2f, B("Rig10").center.z);
        var parents = new Dictionary<string, string>
        {
            ["Hips"] = null, ["Spine"] = "Hips", ["Head"] = "Spine",
            ["LeftUpperArm"] = "Spine", ["LeftLowerArm"] = "LeftUpperArm", ["LeftHand"] = "LeftLowerArm",
            ["RightUpperArm"] = "Spine", ["RightLowerArm"] = "RightUpperArm", ["RightHand"] = "RightLowerArm",
            ["LeftUpperLeg"] = "Hips", ["LeftLowerLeg"] = "LeftUpperLeg", ["LeftFoot"] = "LeftLowerLeg",
            ["RightUpperLeg"] = "Hips", ["RightLowerLeg"] = "RightUpperLeg", ["RightFoot"] = "RightLowerLeg",
        };

        var root = new GameObject("obj_bacon");
        var bones = new Dictionary<string, Transform>();
        foreach (var name in BoneNames)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parents[name] == null ? root.transform : bones[parents[name]], false);
            t.position = joints[name];
            bones[name] = t;
        }

        // Hems: (part, height, colour below, colour above)
        // (the T-shirt's hem a centimetre under the upper torso's bottom rim, where the two surfaces nearly meet and flicker)
        var hems = new List<(string group, float y, string below, string above)> { ("Rig14", B("Rig15").min.y - 0.01f, Jeans, Shirt) };
        foreach (var (side, upperArm, lowerArm) in new[] { ("Left", "Rig9", "Rig8"), ("Right", "Rig13", "Rig12") })
        {
            float sleeve = joints[side + "LowerArm"].y + SleeveAboveElbow;
            hems.Add((upperArm, sleeve, Skin, Shirt));
            hems.Add((lowerArm, sleeve, Skin, Shirt));
        }

        // Each part on its own bone, its mesh relative to the bone; skinned where it has a soft joint
        foreach (var (group, bone, color) in Parts)
        {
            var mesh = Offset(groups[group], -joints[bone]);
            var colors = new List<Material> { ColorMaterial(color) };
            foreach (var hem in hems)
                if (hem.group == group)
                {
                    SplitAbove(mesh, hem.y - joints[bone].y);
                    colors = new List<Material> { ColorMaterial(hem.below), ColorMaterial(hem.above) };
                }
            var part = new GameObject(group == "Handle1" ? "Hair" : bone + "Mesh");
            part.transform.SetParent(bones[bone], false);
            var across = group == "Handle1" ? new List<(string, Vector3, Vector3)>() : Across(bone, joints, parents, groups[group].bounds.center);
            if (across.Count == 0)
            {
                part.AddComponent<MeshFilter>().sharedMesh = RigUtility.SaveMesh(mesh, $"{MeshFolder}/ObjBacon_{group}.asset");
                part.AddComponent<MeshRenderer>().sharedMaterials = colors.ToArray();
            }
            else
            {
                var names = new List<string> { bone };
                foreach (var (other, _, _) in across) names.Add(other);
                Soften(mesh, joints[bone], across, names);
                mesh.bindposes = names.ConvertAll(n => bones[n].worldToLocalMatrix * bones[bone].localToWorldMatrix).ToArray();
                var skin = part.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = RigUtility.SaveMesh(mesh, $"{MeshFolder}/ObjBacon_{group}.asset");
                skin.bones = names.ConvertAll(n => bones[n]).ToArray();
                skin.rootBone = bones[bone];
                skin.sharedMaterials = colors.ToArray();
            }
            if (group == "Handle1") part.SetActive(WithHair);
        }

        // The logo wrapped onto the chest of the T-shirt
        var torso = groups["Rig15"];
        var logoMesh = RigUtility.FaceGrid(FrontTriangles(torso), torso.bounds.min.y + torso.bounds.size.y * LogoHeight,
            torso.bounds.size.x * LogoWidth, 0.003f);
        var logo = new GameObject("Logo");
        logo.transform.SetParent(bones["Spine"], false);
        logo.transform.localPosition = -joints["Spine"];
        logo.AddComponent<MeshFilter>().sharedMesh = RigUtility.SaveMesh(logoMesh, $"{MeshFolder}/ObjBacon_Logo.asset");
        var logoRenderer = logo.AddComponent<MeshRenderer>();
        logoRenderer.sharedMaterial = LogoMaterial();
        logoRenderer.shadowCastingMode = ShadowCastingMode.Off;

        // The face wrapped onto the front of the head, as wide as most of it, centred on it
        var head = groups["Rig10"];
        var faceMesh = RigUtility.FaceGrid(FrontTriangles(head), head.bounds.center.y, head.bounds.size.x * 0.84f, 0.003f);
        var face = new GameObject("Face");
        face.transform.SetParent(bones["Head"], false);
        face.transform.localPosition = -joints["Head"];
        face.AddComponent<MeshFilter>().sharedMesh = RigUtility.SaveMesh(faceMesh, $"{MeshFolder}/ObjBacon_Face.asset");
        var faceRenderer = face.AddComponent<MeshRenderer>();
        faceRenderer.sharedMaterial = faceMaterial;
        faceRenderer.shadowCastingMode = ShadowCastingMode.Off;
        face.AddComponent<RobloxFace>().emotion = (int)RobloxFace.Emotion.Smile;
        face.SetActive(WithFace);

        var avatar = RigUtility.SaveAvatar(RigUtility.BuildAvatar(root, bones, BoneNames), $"{Folder}/Prefabs/ObjBacon_Avatar.asset");
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ObjBacon] Built {PrefabPath}, {(B("Rig10").max.y):F2} m to the top of the head");
    }

    // One mesh per OBJ group, in metres, standing on the origin facing +Z
    internal static Dictionary<string, Mesh> ReadObj(string path)
    {
        var v = new List<Vector3>();
        var vt = new List<Vector2>();
        var vn = new List<Vector3>();
        var faces = new Dictionary<string, List<string[]>>();
        string current = "default";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            var p = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "v": v.Add(new Vector3(F(p[1]), F(p[2]), F(p[3]))); break;
                case "vt": vt.Add(new Vector2(F(p[1]), F(p[2]))); break;
                case "vn": vn.Add(new Vector3(F(p[1]), F(p[2]), F(p[3]))); break;
                case "g": case "o": current = p.Length > 1 ? p[1] : "default"; break;
                case "f":
                    if (!faces.ContainsKey(current)) faces[current] = new List<string[]>();
                    var corners = new string[p.Length - 1];
                    System.Array.Copy(p, 1, corners, 0, corners.Length);
                    faces[current].Add(corners);
                    break;
            }
        }

        var center = ObjCenter(v);
        Vector3 Point(Vector3 p) => ToUnity(p, center);

        var meshes = new Dictionary<string, Mesh>();
        foreach (var kv in faces)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            var index = new Dictionary<string, int>();
            int Corner(string token)
            {
                if (index.TryGetValue(token, out int i)) return i;
                var s = token.Split('/');
                i = vertices.Count;
                vertices.Add(Point(v[int.Parse(s[0]) - 1]));
                uvs.Add(s.Length > 1 && s[1].Length > 0 ? vt[int.Parse(s[1]) - 1] : Vector2.zero);
                var n = s.Length > 2 && s[2].Length > 0 ? vn[int.Parse(s[2]) - 1] : Vector3.up;
                normals.Add(new Vector3(n.x, n.y, -n.z));
                index[token] = i;
                return i;
            }
            foreach (var f in kv.Value)
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    // The flip reverses handedness, so each triangle's winding is reversed too
                    int a = Corner(f[0]), b = Corner(f[k]), c = Corner(f[k + 1]);
                    triangles.AddRange(new[] { a, c, b });
                }
            var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshes[kv.Key] = mesh;
        }
        return meshes;
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    // Where the OBJ stands: centred across and front to back, on its lowest point
    internal static Vector3 ObjCenter(string path)
    {
        var v = new List<Vector3>();
        foreach (var raw in File.ReadLines(path))
        {
            var p = raw.Trim().Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 3 && p[0] == "v") v.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
        }
        return ObjCenter(v);
    }

    static Vector3 ObjCenter(List<Vector3> v)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = -min;
        foreach (var p in v) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        return new Vector3((min.x + max.x) / 2f, min.y, (min.z + max.z) / 2f);
    }

    // An OBJ point (studs, right-handed, Y up, facing -Z) in Unity metres, standing on the origin facing +Z: X mirrored
    // (right- to left-handed) and then turned half round, which comes to flipping Z. A direction: the same, unscaled.
    internal static Vector3 ToUnity(Vector3 p, Vector3 center) { p -= center; return new Vector3(p.x, p.y, -p.z) * StudToMetre; }

    // The soft joints of a part on `bone`: (the bone across the joint, the joint, the way from it into this part)
    static List<(string bone, Vector3 joint, Vector3 into)> Across(string bone, Dictionary<string, Vector3> joints,
        Dictionary<string, string> parents, Vector3 partCenter)
    {
        var across = new List<(string, Vector3, Vector3)>();
        Chain.TryGetValue(bone, out var below);
        if (SoftJoints.Contains(bone))
            across.Add((parents[bone], joints[bone], ((below != null ? joints[below] : partCenter) - joints[bone]).normalized));
        if (below != null && SoftJoints.Contains(below))
            across.Add((below, joints[below], (joints[bone] - joints[below]).normalized));
        return across;
    }

    // Bone weights (bone order as in `names`): all on the part's own bone, except within JointBand of each soft joint,
    // where it hands over to the bone across it: half at the joint, none JointBand inside the part, all JointBand beyond
    static void Soften(Mesh mesh, Vector3 own, List<(string bone, Vector3 joint, Vector3 into)> across, List<string> names)
    {
        var vertices = mesh.vertices;
        var weights = new BoneWeight[vertices.Length];
        var share = new Dictionary<string, float>();
        for (int i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i] + own;
            share.Clear();
            float handed = 0f;
            foreach (var (other, joint, into) in across)
            {
                float x = Mathf.Clamp(Vector3.Dot(p - joint, into) / JointBand, -1f, 1f);
                float h = Mathf.SmoothStep(0f, 1f, (1f - x) / 2f);
                if (h > 0f) share[other] = h;
                handed += h;
            }
            share[names[0]] = Mathf.Max(0f, 1f - handed);
            weights[i] = RigUtility.Pack(share, names);
        }
        mesh.boneWeights = weights;
    }

    // The triangles facing +Z (front), as corner triples
    internal static List<Vector3> FrontTriangles(Mesh mesh)
    {
        var front = new List<Vector3>();
        var v = mesh.vertices;
        var t = mesh.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            if (Vector3.Cross(b - a, c - a).z > 0f) front.AddRange(new[] { a, b, c }); // the cross product is the face normal
        }
        return front;
    }

    internal static Mesh Offset(Mesh source, Vector3 by)
    {
        var mesh = Object.Instantiate(source);
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] += by;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        return mesh;
    }

    // Two submeshes, cut along the plane at height y: the part below it, then the part above; triangles crossing the
    // plane are split along it
    static void SplitAbove(Mesh mesh, float y)
    {
        var v = new List<Vector3>(mesh.vertices);
        var n = new List<Vector3>(mesh.normals);
        var uv = new List<Vector2>(mesh.uv);
        var t = mesh.triangles;
        var below = new List<int>();
        var above = new List<int>();
        var cuts = new Dictionary<(int, int), int>();
        int Cut(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (cuts.TryGetValue(key, out int i)) return i;
            float f = (y - v[a].y) / (v[b].y - v[a].y);
            i = v.Count;
            v.Add(Vector3.Lerp(v[a], v[b], f));
            n.Add(Vector3.Lerp(n[a], n[b], f).normalized);
            uv.Add(Vector2.Lerp(uv[a], uv[b], f));
            cuts[key] = i;
            return i;
        }
        for (int k = 0; k < t.Length; k += 3)
        {
            int[] c = { t[k], t[k + 1], t[k + 2] };
            bool[] up = { v[c[0]].y > y, v[c[1]].y > y, v[c[2]].y > y };
            if (up[0] == up[1] && up[1] == up[2]) { (up[0] ? above : below).AddRange(c); continue; }
            // The corner alone on its side of the plane, then the other two in winding order
            int lone = up[0] != up[1] && up[0] != up[2] ? 0 : up[1] != up[0] ? 1 : 2;
            int p = c[lone], q = c[(lone + 1) % 3], r = c[(lone + 2) % 3];
            int pq = Cut(p, q), pr = Cut(p, r);
            (up[lone] ? above : below).AddRange(new[] { p, pq, pr });
            (up[lone] ? below : above).AddRange(new[] { pq, q, r, pq, r, pr });
        }
        mesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(below, 0);
        mesh.SetTriangles(above, 1);
        mesh.RecalculateBounds();
    }

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    internal static Material ColorMaterial(string hex)
    {
        if (materials.TryGetValue(hex, out var material) && material != null) return material;
        string path = $"{MaterialFolder}/ObjBacon_{hex}.mat";
        material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", RigUtility.Hex(hex));
        material.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(material);
        materials[hex] = material;
        return material;
    }

    // The logo, cut out along its alpha
    static Material LogoMaterial()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(LogoPath);
        if (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        string path = $"{MaterialFolder}/ObjBacon_Logo.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.25f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(material);
        return material;
    }
}
