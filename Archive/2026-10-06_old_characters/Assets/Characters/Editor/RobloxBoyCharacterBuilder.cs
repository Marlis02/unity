using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Builds characters from the grey R15 body in Assets/Characters/RobloxBoy/RobloxBoy.glb ("ROBLOX Boy" by MatiasH290 on
// Sketchfab, CC-BY-4.0: credit the author where the video is published) as prefabs in Assets/Characters/RobloxBoy/Prefabs.
// The body is one mesh in six pieces (head, torso, arms, legs). It is rigged here on a 16-bone skeleton named after
// Unity's humanoid bones, so every prefab gets a Humanoid avatar and plays the same clips as the Tripo characters; each
// piece rides on its own chain of bones, so neck, shoulders and hips turn without stretching. The arms, modelled
// splayed out, are swung in to hang along the body. Each Spec colours the same body by region (cut cleanly across the
// pieces), puts on Tripo's hair fitted to this head and a RobloxFace (RobloxDecalAtlasBuilder) on the front of it.
// Edit Specs and re-run Tools/Characters/Build Roblox Boy Characters; prefabs are rebuilt in place.
public static class RobloxBoyCharacterBuilder
{
    public const float Height = 1.8f; // metres
    const string Folder = "Assets/Characters/RobloxBoy";
    const string ModelPath = Folder + "/RobloxBoy.glb";
    const string PrefabFolder = Folder + "/Prefabs";
    const string MeshFolder = Folder + "/Meshes";
    const string MaterialFolder = Folder + "/Materials";
    const string AvatarFolder = Folder + "/Avatars";

    // Joint positions in metres on the body as modelled (scaled to Height), standing on the origin facing +Z, its left
    // side at -X. Only the middle and the left side are listed; the right side is the mirror image.
    static readonly (string bone, string parent, Vector3 pos)[] Joints =
    {
        ("Hips", null, new Vector3(0f, 0.8f, 0f)),
        ("Spine", "Hips", new Vector3(0f, 0.86f, 0f)),
        ("Chest", "Spine", new Vector3(0f, 1.06f, 0f)),
        ("Head", "Chest", new Vector3(0f, 1.39f, 0f)),
        ("LeftUpperArm", "Chest", new Vector3(-0.4f, 1.27f, -0.02f)),
        ("LeftLowerArm", "LeftUpperArm", new Vector3(-0.4475f, 1.07f, -0.025f)),
        ("LeftHand", "LeftLowerArm", new Vector3(-0.495f, 0.87f, -0.03f)),
        ("LeftUpperLeg", "Hips", new Vector3(-0.16f, 0.76f, 0.01f)),
        ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-0.16f, 0.37f, 0.015f)),
        ("LeftFoot", "LeftLowerLeg", new Vector3(-0.16f, 0.12f, 0f)),
    };

    // Where the colours change, as heights on the body as modelled: the shirt's hem (a crease in the torso), the end
    // of the sleeve, the wrist, the knee and the top of the shoe (creases in the limbs)
    const float Hem = 0.86f, SleeveEnd = 1.1f, Wrist = 0.87f, Knee = 0.37f, ShoeTop = 0.12f;

    // The torso's shoulder line is its highest point within ShoulderReach of the shoulder joints (sideways); a swung-in
    // arm's top sits ShoulderTuck below it
    const float ShoulderReach = 0.12f, ShoulderTuck = 0.004f;

    // Half-widths (metres) of the bands where the weight hands over from bone to bone
    const float WaistBlend = 0.01f, ChestBlend = 0.06f, ElbowBlend = 0.035f, WristBlend = 0.015f, KneeBlend = 0.025f, AnkleBlend = 0.015f;

    // Face grid on the front of the head, a little above its middle; brows tuck under the hair
    const float FaceCenterY = 1.59f, FaceSize = 0.35f, FaceLift = 0.004f;

    enum Piece { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg }

    enum Region { Skin, Shirt, Sleeve, Forearm, Pants, Shin, Shoe }

    enum Hair { None, Bacon, BaconTripo }

    const float HairInflate = 1.03f; // this head is boxier than Tripo's: a little room at the corners of the stretched hair

    class Spec
    {
        public string name;
        public Color skin, shirt, pants, shoes;
        public Color? sleeves, forearms, shins; // default to shirt, skin and pants
        public Hair hair;
        public Color hairColor;
        public RobloxFace.Emotion face;

        public Color Of(Region region)
        {
            switch (region)
            {
                case Region.Skin: return skin;
                case Region.Shirt: return shirt;
                case Region.Sleeve: return sleeves ?? shirt;
                case Region.Forearm: return forearms ?? skin;
                case Region.Pants: return pants;
                case Region.Shin: return shins ?? pants;
                default: return shoes;
            }
        }
    }

    static readonly Spec[] Specs =
    {
        // The bacon hair from the floor-sawing video
        new Spec
        {
            name = "Bacon", skin = Hex("F2C9A0"), shirt = Hex("1E1E22"), pants = Hex("2E3440"), shoes = Hex("F2F2F2"),
            hair = Hair.Bacon, hairColor = Hex("B8642A"), face = RobloxFace.Emotion.Smile,
        },
        // The classic noob: yellow, blue torso, green legs
        new Spec
        {
            name = "Noob", skin = Hex("F5CD30"), shirt = Hex("0D69AC"), sleeves = Hex("F5CD30"), pants = Hex("A4BD47"),
            shoes = Hex("A4BD47"), hair = Hair.None, face = RobloxFace.Emotion.Grin,
        },
    };

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    [MenuItem("Tools/Characters/Build Roblox Boy Characters")]
    public static void BuildAll()
    {
        var faceMaterial = RobloxDecalAtlasBuilder.FaceMaterial();
        if (faceMaterial == null) return;
        foreach (var folder in new[] { PrefabFolder, MeshFolder, MaterialFolder, AvatarFolder }) RigUtility.EnsureFolder(folder);
        materials.Clear();

        var joints = new List<(string bone, string parent, Vector3 pos)>(Joints);
        foreach (var (bone, parent, pos) in Joints)
            if (bone.StartsWith("Left"))
                joints.Add(("Right" + bone.Substring(4), parent.StartsWith("Left") ? "Right" + parent.Substring(4) : parent,
                    new Vector3(-pos.x, pos.y, pos.z)));

        var body = BodyMesh(joints, out var headBounds, out var headTriangles);
        var faceMesh = RigUtility.SaveMesh(RigUtility.FaceGrid(headTriangles, FaceCenterY, FaceSize, FaceLift), $"{MeshFolder}/RobloxBoy_Face.asset");
        var hairMeshes = new Dictionary<Hair, Mesh>();
        foreach (var spec in Specs)
            if (spec.hair != Hair.None && !hairMeshes.ContainsKey(spec.hair))
                hairMeshes[spec.hair] = RigUtility.SaveMesh(RigUtility.StretchTripoHair(spec.hair.ToString(), headBounds, HairInflate),
                    $"{MeshFolder}/RobloxBoy_Hair_{spec.hair}.asset");

        // Build in a preview scene so the open scene isn't touched
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var spec in Specs) BuildCharacter(spec, joints, body, faceMesh, hairMeshes, faceMaterial, stage);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[RobloxBoy] Built {Specs.Length} characters in {PrefabFolder}");
    }

    static void BuildCharacter(Spec s, List<(string bone, string parent, Vector3 pos)> joints, Mesh body, Mesh faceMesh,
        Dictionary<Hair, Mesh> hairMeshes, Material faceMaterial, Scene stage)
    {
        var root = new GameObject("RobloxBoy_" + s.name);
        SceneManager.MoveGameObjectToScene(root, stage);
        var bones = new Dictionary<string, Transform>();
        foreach (var (bone, parent, pos) in joints)
        {
            var t = new GameObject(bone).transform;
            t.SetParent(parent == null ? root.transform : bones[parent], false);
            t.position = pos;
            bones[bone] = t;
        }

        var boneNames = joints.ConvertAll(j => j.bone);
        var bodyObject = new GameObject("Body");
        bodyObject.transform.SetParent(root.transform, false);
        var skin = bodyObject.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = body;
        skin.bones = boneNames.ConvertAll(b => bones[b]).ToArray();
        skin.rootBone = bones["Hips"];
        var regionMaterials = new Material[System.Enum.GetValues(typeof(Region)).Length];
        for (int r = 0; r < regionMaterials.Length; r++) regionMaterials[r] = PartMaterial(s.Of((Region)r));
        skin.sharedMaterials = regionMaterials;

        // Rigid on the head, modelled in character space like the body
        var face = HeadPart(bones["Head"], "Face", faceMesh, faceMaterial);
        face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        face.AddComponent<RobloxFace>().emotion = (int)s.face;
        if (s.hair != Hair.None) HeadPart(bones["Head"], "Hair", hairMeshes[s.hair], PartMaterial(s.hairColor));

        var avatar = RigUtility.SaveAvatar(RigUtility.BuildAvatar(root, bones, boneNames), $"{AvatarFolder}/RobloxBoy_{s.name}_Avatar.asset");
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/RobloxBoy_{s.name}.prefab");
        Object.DestroyImmediate(root);
    }

    static GameObject HeadPart(Transform head, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(head, false);
        go.transform.SetPositionAndRotation(head.root.position, Quaternion.identity);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    // ---------------------------------------------------------------- body

    // The body in character space with one submesh per Region, skinned to `joints`, the arms swung in to hang straight
    // down (their joints in `joints` move with them). Also hands back the head's bounds and triangles (corner triples).
    static Mesh BodyMesh(List<(string bone, string parent, Vector3 pos)> joints, out Bounds headBounds, out List<Vector3> headTriangles)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var filter = model.GetComponentInChildren<MeshFilter>();
        var source = filter.sharedMesh;
        var toModel = filter.transform.localToWorldMatrix;
        var vertices = new List<Vector3>(source.vertices);
        var normals = new List<Vector3>(source.normals);
        float floor = float.PositiveInfinity, top = float.NegativeInfinity;
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] = toModel.MultiplyPoint3x4(vertices[i]);
            normals[i] = toModel.MultiplyVector(normals[i]).normalized;
            floor = Mathf.Min(floor, vertices[i].y);
            top = Mathf.Max(top, vertices[i].y);
        }
        float scale = Height / (top - floor);
        for (int i = 0; i < vertices.Count; i++) vertices[i] = (vertices[i] - Vector3.up * floor) * scale;
        var sourceTriangles = source.triangles;

        var at = new Dictionary<string, Vector3>();
        foreach (var (bone, _, pos) in joints) at[bone] = pos;
        var pieces = Pieces(vertices, sourceTriangles, at);

        // Cut each piece's triangles at its colour lines, sharing the new vertices between neighbouring triangles
        var regions = new List<int>[System.Enum.GetValues(typeof(Region)).Length];
        for (int r = 0; r < regions.Length; r++) regions[r] = new List<int>();
        var crossings = new Dictionary<(int, int, float), int>();
        int Crossing(int a, int b, float y)
        {
            var key = a < b ? (a, b, y) : (b, a, y);
            if (crossings.TryGetValue(key, out int index)) return index;
            float t = (y - vertices[a].y) / (vertices[b].y - vertices[a].y);
            crossings[key] = vertices.Count;
            vertices.Add(Vector3.Lerp(vertices[a], vertices[b], t));
            normals.Add(Vector3.Lerp(normals[a], normals[b], t).normalized);
            pieces.Add(pieces[a]);
            return vertices.Count - 1;
        }
        for (int i = 0; i < sourceTriangles.Length; i += 3)
        {
            var piece = pieces[sourceTriangles[i]];
            var (cuts, bands) = ColourLines(piece);
            var parts = new List<(List<int> polygon, int band)> { (new List<int> { sourceTriangles[i], sourceTriangles[i + 1], sourceTriangles[i + 2] }, 0) };
            foreach (float y in cuts)
            {
                var next = new List<(List<int>, int)>();
                foreach (var (polygon, band) in parts)
                {
                    var above = new List<int>();
                    var below = new List<int>();
                    for (int k = 0; k < polygon.Count; k++)
                    {
                        int from = polygon[k], to = polygon[(k + 1) % polygon.Count];
                        bool fromAbove = vertices[from].y >= y, toAbove = vertices[to].y >= y;
                        (fromAbove ? above : below).Add(from);
                        if (fromAbove == toAbove) continue;
                        int cross = Crossing(from, to, y);
                        above.Add(cross);
                        below.Add(cross);
                    }
                    if (above.Count >= 3) next.Add((above, band));
                    if (below.Count >= 3) next.Add((below, band + 1));
                }
                parts = next;
            }
            foreach (var (polygon, band) in parts)
                for (int k = 1; k + 1 < polygon.Count; k++)
                    regions[(int)bands[band]].AddRange(new[] { polygon[0], polygon[k], polygon[k + 1] });
        }

        // Weighted on the body as modelled, where the creases are level
        var boneNames = joints.ConvertAll(j => j.bone);
        var weights = new BoneWeight[vertices.Count];
        for (int i = 0; i < vertices.Count; i++) weights[i] = Weight(pieces[i], vertices[i], at, boneNames);

        // Swing each arm in about its shoulder until shoulder and wrist line up vertically. That lifts the arm's top
        // corner by the neck above the torso's shoulder, so the arm then drops until its top tucks just under it.
        float shoulderLine = float.NegativeInfinity;
        for (int i = 0; i < vertices.Count; i++)
            if (pieces[i] == Piece.Torso && Mathf.Abs(vertices[i].x) > Mathf.Abs(at["LeftUpperArm"].x) - ShoulderReach)
                shoulderLine = Mathf.Max(shoulderLine, vertices[i].y);
        foreach (var side in new[] { "Left", "Right" })
        {
            var shoulder = at[side + "UpperArm"];
            var reach = at[side + "Hand"] - shoulder;
            var swing = Quaternion.FromToRotation(new Vector3(reach.x, reach.y, 0f), Vector3.down);
            var arm = side == "Left" ? Piece.LeftArm : Piece.RightArm;
            float armTop = float.NegativeInfinity;
            for (int i = 0; i < vertices.Count; i++)
            {
                if (pieces[i] != arm) continue;
                vertices[i] = shoulder + swing * (vertices[i] - shoulder);
                normals[i] = swing * normals[i];
                armTop = Mathf.Max(armTop, vertices[i].y);
            }
            var drop = Vector3.down * Mathf.Max(0f, armTop - (shoulderLine - ShoulderTuck));
            for (int i = 0; i < vertices.Count; i++)
                if (pieces[i] == arm) vertices[i] += drop;
            for (int j = 0; j < joints.Count; j++)
                if (joints[j].bone.StartsWith(side) && (joints[j].bone.EndsWith("Arm") || joints[j].bone.EndsWith("Hand")))
                    joints[j] = (joints[j].bone, joints[j].parent, shoulder + swing * (joints[j].pos - shoulder) + drop);
        }

        headBounds = new Bounds();
        headTriangles = new List<Vector3>();
        bool first = true;
        for (int i = 0; i < vertices.Count; i++)
        {
            if (pieces[i] != Piece.Head) continue;
            if (first) headBounds = new Bounds(vertices[i], Vector3.zero);
            else headBounds.Encapsulate(vertices[i]);
            first = false;
        }
        foreach (int i in regions[(int)Region.Skin])
            if (pieces[i] == Piece.Head) headTriangles.Add(vertices[i]);

        var mesh = new Mesh { indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = regions.Length;
        for (int r = 0; r < regions.Length; r++) mesh.SetTriangles(regions[r], r);
        mesh.boneWeights = weights;
        // The renderer sits on the character origin; the bones are unrotated and unscaled
        mesh.bindposes = joints.ConvertAll(j => Matrix4x4.Translate(-j.pos)).ToArray();
        mesh.RecalculateBounds();
        return RigUtility.SaveMesh(mesh, $"{MeshFolder}/RobloxBoy_Body.asset");
    }

    // Which piece each vertex is on: the mesh's connected parts (welded by position), told apart by where they sit
    static List<Piece> Pieces(List<Vector3> vertices, int[] triangles, Dictionary<string, Vector3> at)
    {
        var ids = new Dictionary<Vector3, int>();
        var weld = new int[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            if (!ids.TryGetValue(vertices[i], out int id)) ids[vertices[i]] = id = ids.Count;
            weld[i] = id;
        }
        var group = new int[ids.Count];
        for (int i = 0; i < group.Length; i++) group[i] = i;
        int Find(int a)
        {
            while (group[a] != a) a = group[a] = group[group[a]];
            return a;
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            group[Find(weld[triangles[i + 1]])] = Find(weld[triangles[i]]);
            group[Find(weld[triangles[i + 2]])] = Find(weld[triangles[i]]);
        }
        var bounds = new Dictionary<int, Bounds>();
        for (int i = 0; i < vertices.Count; i++)
        {
            int g = Find(weld[i]);
            if (bounds.TryGetValue(g, out var b)) b.Encapsulate(vertices[i]);
            else b = new Bounds(vertices[i], Vector3.zero);
            bounds[g] = b;
        }

        var pieces = new List<Piece>(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            var c = bounds[Find(weld[i])].center;
            if (c.y > at["Head"].y) pieces.Add(Piece.Head);
            else if (Mathf.Abs(c.x) > Mathf.Abs(at["LeftUpperArm"].x) * 0.75f) pieces.Add(c.x < 0f ? Piece.LeftArm : Piece.RightArm);
            else if (c.y < at["Hips"].y) pieces.Add(c.x < 0f ? Piece.LeftLeg : Piece.RightLeg);
            else pieces.Add(Piece.Torso);
        }
        return pieces;
    }

    // Heights (top down) where a piece changes colour, and the region of each band between them
    static (float[] cuts, Region[] bands) ColourLines(Piece piece)
    {
        switch (piece)
        {
            case Piece.Torso: return (new[] { Hem }, new[] { Region.Shirt, Region.Pants });
            case Piece.LeftArm:
            case Piece.RightArm: return (new[] { SleeveEnd, Wrist }, new[] { Region.Sleeve, Region.Forearm, Region.Skin });
            case Piece.LeftLeg:
            case Piece.RightLeg: return (new[] { Knee, ShoeTop }, new[] { Region.Pants, Region.Shin, Region.Shoe });
            default: return (new float[0], new[] { Region.Skin });
        }
    }

    // Each piece rides on its own chain of bones, handing over at the creases
    static BoneWeight Weight(Piece piece, Vector3 p, Dictionary<string, Vector3> at, List<string> boneNames)
    {
        var share = new Dictionary<string, float>();
        switch (piece)
        {
            case Piece.Head:
                share["Head"] = 1f;
                break;
            case Piece.Torso:
                RigUtility.Chain(share, 1f, new[] { "Hips", "Spine", "Chest" }, new[]
                {
                    1f - RigUtility.Below(p.y, Hem, WaistBlend),
                    1f - RigUtility.Below(p.y, (at["Spine"].y + at["Chest"].y) / 2f, ChestBlend),
                });
                break;
            case Piece.LeftArm:
            case Piece.RightArm:
            {
                string side = piece == Piece.LeftArm ? "Left" : "Right";
                RigUtility.Chain(share, 1f, new[] { side + "UpperArm", side + "LowerArm", side + "Hand" }, new[]
                {
                    RigUtility.Below(p.y, at[side + "LowerArm"].y, ElbowBlend), RigUtility.Below(p.y, at[side + "Hand"].y, WristBlend),
                });
                break;
            }
            default:
            {
                string side = piece == Piece.LeftLeg ? "Left" : "Right";
                RigUtility.Chain(share, 1f, new[] { side + "UpperLeg", side + "LowerLeg", side + "Foot" }, new[]
                {
                    RigUtility.Below(p.y, at[side + "LowerLeg"].y, KneeBlend), RigUtility.Below(p.y, at[side + "Foot"].y, AnkleBlend),
                });
                break;
            }
        }
        return RigUtility.Pack(share, boneNames);
    }

    // ---------------------------------------------------------------- assets

    static Material PartMaterial(Color color)
    {
        string key = "RobloxBoy_" + ColorUtility.ToHtmlStringRGB(color);
        if (materials.TryGetValue(key, out var material)) return material;
        string path = $"{MaterialFolder}/{key}.mat";
        material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.3f);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        materials[key] = material;
        return material;
    }

    static Color Hex(string hex) => R15CharacterBuilder.Hex(hex);
}
