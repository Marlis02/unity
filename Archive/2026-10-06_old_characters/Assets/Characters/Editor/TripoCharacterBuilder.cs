using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Builds characters from the grey Tripo body (Assets/Characters/Tripo/Hero_Parts.fbx) as prefabs in Assets/Characters/Tripo/Prefabs.
// The body's rigid parts ride on a 15-bone skeleton named after Unity's humanoid bones, so every prefab gets a Humanoid
// avatar and plays Mixamo clips; each Spec recolours the same body. The face is a RobloxFace grid wrapped onto the head,
// showing the Roblox decal atlas (RobloxDecalAtlasBuilder).
// Edit Specs and re-run Tools/Characters/Build Tripo Characters; prefabs are rebuilt in place.
public static class TripoCharacterBuilder
{
    public const float Height = 1.8f; // metres; the body is modelled 1 unit tall
    public const string PrefabFolder = "Assets/Characters/Tripo/Prefabs";
    const string BodyPath = "Assets/Characters/Tripo/Hero_Parts.fbx";
    const string MeshFolder = "Assets/Characters/Tripo/Meshes";
    const string MaterialFolder = "Assets/Characters/Tripo/Materials";
    const string AvatarFolder = "Assets/Characters/Tripo/Avatars";

    // Face grid in body units, centred on the head's front
    const float FaceSize = 0.17f, FaceCenterY = 0.895f, FaceLift = 0.0025f;
    const int FaceGrid = 16;

    // Wristband in body units: how far from elbow to wrist it sits, its width along the forearm, thickness and gap
    const float BraceletAt = 0.82f, BraceletWidth = 0.024f, BraceletThickness = 0.007f, BraceletClearance = 0.001f;
    const int BraceletSides = 48, BraceletRound = 10;

    // The torso (part 2) runs from neck to crotch in one piece: shirt above the waistband, pants below, and it bends
    // smoothly from Hips to Spine between BendFrom and BendTo (body units)
    const int TorsoPart = 2;
    const float Waistband = 0.5f, BendFrom = 0.46f, BendTo = 0.58f;

    // Joint positions in body units; the body stands on the origin facing +Z, so its left side is -X
    static readonly (string bone, string parent, Vector3 pos)[] Joints =
    {
        ("Hips", null, new Vector3(0f, 0.48f, -0.025f)),
        ("Spine", "Hips", new Vector3(0f, 0.515f, -0.02f)),
        ("Head", "Spine", new Vector3(0f, 0.795f, -0.015f)),
        ("LeftUpperArm", "Spine", new Vector3(-0.18f, 0.72f, -0.012f)),
        ("LeftLowerArm", "LeftUpperArm", new Vector3(-0.225f, 0.59f, -0.012f)),
        ("LeftHand", "LeftLowerArm", new Vector3(-0.252f, 0.47f, 0.01f)),
        ("RightUpperArm", "Spine", new Vector3(0.18f, 0.72f, -0.012f)),
        ("RightLowerArm", "RightUpperArm", new Vector3(0.225f, 0.59f, -0.012f)),
        ("RightHand", "RightLowerArm", new Vector3(0.252f, 0.47f, 0.01f)),
        ("LeftUpperLeg", "Hips", new Vector3(-0.08f, 0.44f, -0.028f)),
        ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-0.09f, 0.245f, -0.033f)),
        ("LeftFoot", "LeftLowerLeg", new Vector3(-0.1f, 0.09f, -0.03f)),
        ("RightUpperLeg", "Hips", new Vector3(0.08f, 0.44f, -0.028f)),
        ("RightLowerLeg", "RightUpperLeg", new Vector3(0.09f, 0.245f, -0.033f)),
        ("RightFoot", "RightLowerLeg", new Vector3(0.1f, 0.09f, -0.03f)),
    };

    enum Region { Skin, Shirt, Sleeve, Forearm, Pants, Shin, Shoe }

    enum Hair { None, Bacon, BaconTripo }

    // Hair models (grey, facing +Z, hollow for the head) and how each sits on the head: uniform scale, then offset in body
    // units. Fit so the head doesn't poke through anywhere, looking in from all round; the head is deep front to back.
    static readonly Dictionary<Hair, (string path, float scale, Vector3 offset)> HairModels = new Dictionary<Hair, (string, float, Vector3)>
    {
        [Hair.Bacon] = ("Assets/Characters/Tripo/Hair_Bacon.fbx", 0.16f, new Vector3(0f, 0.94f, 0f)),
        [Hair.BaconTripo] = ("Assets/Characters/Tripo/Hair_BaconTripo.fbx", 0.28f, new Vector3(0f, 0.84f, -0.014f)), // origin at its base
    };

    // Which bone each rigid tripo_part_<n> rides on and which colour it takes. Fills are small joint pieces that sit
    // inside the waist, shoulders and knees.
    static readonly (int part, string name, string bone, Region region)[] Parts =
    {
        (5, "HeadMesh", "Head", Region.Skin),
        (10, "WaistFill", "Hips", Region.Pants),
        (16, "LeftShoulderFill", "Spine", Region.Shirt),
        (25, "LeftShoulderFill", "Spine", Region.Shirt),
        (18, "RightShoulderFill", "Spine", Region.Shirt),
        (26, "RightShoulderFill", "Spine", Region.Shirt),
        (7, "LeftUpperArmMesh", "LeftUpperArm", Region.Sleeve),
        (6, "RightUpperArmMesh", "RightUpperArm", Region.Sleeve),
        (14, "LeftLowerArmMesh", "LeftLowerArm", Region.Forearm),
        (23, "LeftElbow", "LeftLowerArm", Region.Forearm),
        (19, "LeftCuff", "LeftLowerArm", Region.Forearm),
        (12, "RightLowerArmMesh", "RightLowerArm", Region.Forearm),
        (17, "RightCuff", "RightLowerArm", Region.Forearm),
        (4, "LeftHandMesh", "LeftHand", Region.Skin),
        (3, "RightHandMesh", "RightHand", Region.Skin),
        (9, "LeftUpperLegMesh", "LeftUpperLeg", Region.Pants),
        (8, "RightUpperLegMesh", "RightUpperLeg", Region.Pants),
        (20, "LeftKneeFill", "LeftLowerLeg", Region.Shin),
        (24, "LeftKneeFill", "LeftLowerLeg", Region.Shin),
        (21, "RightKneeFill", "RightLowerLeg", Region.Shin),
        (22, "RightKneeFill", "RightLowerLeg", Region.Shin),
        (11, "LeftLowerLegMesh", "LeftLowerLeg", Region.Shin),
        (13, "RightLowerLegMesh", "RightLowerLeg", Region.Shin),
        (15, "RightAnkle", "RightLowerLeg", Region.Shin),
        (1, "LeftFootMesh", "LeftFoot", Region.Shoe),
        (0, "RightFootMesh", "RightFoot", Region.Shoe),
    };

    class Spec
    {
        public string name;
        public Color skin, shirt, pants, shoes;
        public Color? sleeves, forearms, shins; // default to shirt, skin and pants
        public Hair hair;
        public Color hairColor;
        public RobloxFace.Emotion face;
        public (Color band, Color stripe)? bracelet; // a wristband on the right wrist

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
        new Spec
        {
            name = "Hero", skin = Hex("FFCC99"), shirt = Hex("E53935"), pants = Hex("2F4A7F"), shoes = Hex("F2F2F2"),
            hair = Hair.Bacon, hairColor = Hex("8A5530"), face = RobloxFace.Emotion.Smile,
        },
        new Spec
        {
            name = "Buddy", skin = Hex("8C5C40"), shirt = Hex("2E8B57"), forearms = Hex("2E8B57"), pants = Hex("26272B"),
            shoes = Hex("D62828"), hair = Hair.Bacon, hairColor = Hex("2B2220"), face = RobloxFace.Emotion.Grin,
        },
        new Spec
        {
            name = "Sis", skin = Hex("FFE0C7"), shirt = Hex("B57EDC"), pants = Hex("6E8FC9"), shins = Hex("FFE0C7"),
            shoes = Hex("FF8FB3"), hair = Hair.BaconTripo, hairColor = Hex("E8B84A"), face = RobloxFace.Emotion.Smug,
        },
        // Hero with blue hair and a wristband: the floor-sawing short
        new Spec
        {
            name = "Builder", skin = Hex("FFCC99"), shirt = Hex("E53935"), pants = Hex("2F4A7F"), shoes = Hex("F2F2F2"),
            hair = Hair.Bacon, hairColor = Hex("1F4FD6"), face = RobloxFace.Emotion.Smile,
            bracelet = (Hex("1E1E22"), Hex("29B6F6")),
        },
    };

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    [MenuItem("Tools/Characters/Build Tripo Characters")]
    public static void BuildAll()
    {
        var faceMaterial = RobloxDecalAtlasBuilder.FaceMaterial();
        if (faceMaterial == null) return;
        foreach (var folder in new[] { PrefabFolder, MeshFolder, MaterialFolder, AvatarFolder }) EnsureFolder(folder);
        materials.Clear();

        var body = new Dictionary<int, Mesh>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(BodyPath))
            if (asset is Mesh mesh && int.TryParse(mesh.name.Replace("tripo_part_", ""), out int index))
                body[index] = index == TorsoPart ? RigUtility.Capped(mesh) : SaveMesh(RigUtility.Capped(mesh), "Tripo_Part" + index);
        var torsoMesh = SaveMesh(TorsoMesh(body[TorsoPart]), "Tripo_Torso");
        var faceMesh = SaveMesh(FaceMesh(body[5]), "Tripo_Face");
        var braceletMesh = SaveMesh(BraceletMesh(body), "Tripo_Bracelet");
        var hairMeshes = new Dictionary<Hair, Mesh>();
        foreach (var (hair, (path, scale, offset)) in HairModels)
            hairMeshes[hair] = SaveMesh(TripoHairMesh.Fit(AssetDatabase.LoadAssetAtPath<GameObject>(path), scale, offset, body[5]), "Tripo_Hair_" + hair);

        // Build in a preview scene so the open scene isn't touched
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var spec in Specs) BuildCharacter(spec, body, torsoMesh, faceMesh, hairMeshes, braceletMesh, faceMaterial, stage);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Tripo] Built {Specs.Length} characters in {PrefabFolder}");
    }

    static void BuildCharacter(Spec s, Dictionary<int, Mesh> body, Mesh torsoMesh, Mesh faceMesh, Dictionary<Hair, Mesh> hairMeshes,
        Mesh braceletMesh, Material faceMaterial, Scene stage)
    {
        var root = new GameObject("Tripo_" + s.name);
        SceneManager.MoveGameObjectToScene(root, stage);
        var bones = new Dictionary<string, Transform>();
        foreach (var (bone, parent, pos) in Joints)
        {
            var t = new GameObject(bone).transform;
            t.SetParent(parent == null ? root.transform : bones[parent], false);
            t.position = pos * Height;
            bones[bone] = t;
        }

        var torso = new GameObject("Torso");
        torso.transform.SetParent(root.transform, false);
        torso.transform.localScale = Vector3.one * Height;
        var skin = torso.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = torsoMesh;
        skin.bones = new[] { bones["Hips"], bones["Spine"] };
        skin.rootBone = bones["Hips"];
        skin.sharedMaterials = new[] { PartMaterial(s.shirt), PartMaterial(s.pants) };

        foreach (var (part, name, bone, region) in Parts) Part(bones[bone], name, body[part], PartMaterial(s.Of(region)));

        var face = Part(bones["Head"], "Face", faceMesh, faceMaterial);
        face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        face.AddComponent<RobloxFace>().emotion = (int)s.face;
        if (s.hair != Hair.None) Part(bones["Head"], "Hair", hairMeshes[s.hair], PartMaterial(s.hairColor));
        if (s.bracelet.HasValue)
            Part(bones["RightLowerArm"], "Bracelet", braceletMesh, null).GetComponent<MeshRenderer>().sharedMaterials =
                new[] { PartMaterial(s.bracelet.Value.band), PartMaterial(s.bracelet.Value.stripe) };

        var avatar = SaveAvatar(BuildAvatar(root, bones), $"{AvatarFolder}/Tripo_{s.name}_Avatar.asset");
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/Tripo_{s.name}.prefab");
        Object.DestroyImmediate(root);
    }

    // Body meshes are modelled in body space, so each part sits on the character origin scaled to Height, under its bone
    static GameObject Part(Transform bone, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(bone, false);
        go.transform.SetPositionAndRotation(bone.root.position, Quaternion.identity);
        go.transform.localScale = Vector3.one * Height;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    // ---------------------------------------------------------------- meshes

    // The torso skinned to Hips (bone 0) and Spine (bone 1), with its triangles clipped at the waistband into
    // submesh 0 (shirt, above) and submesh 1 (pants, below) so the colour line is straight
    static Mesh TorsoMesh(Mesh torso)
    {
        var sourceVertices = torso.vertices;
        var sourceNormals = torso.normals;
        var sourceTriangles = torso.triangles;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var shirt = new List<int>();
        var pants = new List<int>();
        var corners = new Dictionary<int, int>();
        var crossings = new Dictionary<(int, int), int>();

        int Corner(int v)
        {
            if (corners.TryGetValue(v, out int index)) return index;
            corners[v] = vertices.Count;
            vertices.Add(sourceVertices[v]);
            normals.Add(sourceNormals[v]);
            return vertices.Count - 1;
        }

        // Where edge a-b crosses the waistband; shared by both sides and by the triangles on either side of the edge
        int Crossing(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (crossings.TryGetValue(key, out int index)) return index;
            float t = (Waistband - sourceVertices[a].y) / (sourceVertices[b].y - sourceVertices[a].y);
            crossings[key] = vertices.Count;
            vertices.Add(Vector3.Lerp(sourceVertices[a], sourceVertices[b], t));
            normals.Add(Vector3.Lerp(sourceNormals[a], sourceNormals[b], t).normalized);
            return vertices.Count - 1;
        }

        // Keeps the part of triangle (a, b, c) on one side of the waistband and adds it as a fan
        void Clip(int a, int b, int c, bool above, List<int> triangles)
        {
            var polygon = new List<int>(4);
            int[] triangle = { a, b, c };
            for (int k = 0; k < 3; k++)
            {
                int from = triangle[k], to = triangle[(k + 1) % 3];
                bool fromIn = sourceVertices[from].y >= Waistband == above;
                bool toIn = sourceVertices[to].y >= Waistband == above;
                if (fromIn) polygon.Add(Corner(from));
                if (fromIn != toIn) polygon.Add(Crossing(from, to));
            }
            for (int k = 1; k + 1 < polygon.Count; k++) triangles.AddRange(new[] { polygon[0], polygon[k], polygon[k + 1] });
        }

        for (int i = 0; i < sourceTriangles.Length; i += 3)
        {
            int a = sourceTriangles[i], b = sourceTriangles[i + 1], c = sourceTriangles[i + 2];
            Clip(a, b, c, true, shirt);
            Clip(a, b, c, false, pants);
        }

        var weights = new BoneWeight[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            float spine = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BendFrom, BendTo, vertices[i].y));
            // Heavier bone first
            weights[i] = spine >= 0.5f
                ? new BoneWeight { boneIndex0 = 1, weight0 = spine, boneIndex1 = 0, weight1 = 1f - spine }
                : new BoneWeight { boneIndex0 = 0, weight0 = 1f - spine, boneIndex1 = 1, weight1 = spine };
        }

        var mesh = new Mesh { name = "Tripo_Torso" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(shirt, 0);
        mesh.SetTriangles(pants, 1);
        mesh.boneWeights = weights;
        // The renderer sits on the character origin scaled to Height; the bones are unrotated and unscaled
        mesh.bindposes = new[] { BindPose("Hips"), BindPose("Spine") };
        mesh.RecalculateBounds();
        return mesh;
    }

    static Matrix4x4 BindPose(string bone)
    {
        foreach (var (name, _, pos) in Joints)
            if (name == bone) return Matrix4x4.Translate(-pos * Height) * Matrix4x4.Scale(Vector3.one * Height);
        throw new System.ArgumentException(bone);
    }

    // A grid wrapped onto the front of the head so the face follows its curve; UVs span 0..1 like a quad, for RobloxFace
    static Mesh FaceMesh(Mesh head)
    {
        var headVertices = head.vertices;
        var headTriangles = head.triangles;
        int n = FaceGrid + 1;
        var vertices = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
        {
            float u = c / (float)FaceGrid, v = r / (float)FaceGrid;
            // Seen from the front (+Z), the face's left edge (u = 0) is on the body's right (+X)
            float x = (0.5f - u) * FaceSize, y = FaceCenterY + (v - 0.5f) * FaceSize;
            vertices[r * n + c] = new Vector3(x, y, FrontZ(headVertices, headTriangles, x, y) + FaceLift);
            uvs[r * n + c] = new Vector2(u, v);
        }
        var triangles = new List<int>();
        for (int r = 0; r < FaceGrid; r++)
        for (int c = 0; c < FaceGrid; c++)
        {
            int i = r * n + c;
            triangles.AddRange(new[] { i, i + n, i + n + 1, i, i + n + 1, i + 1 });
        }
        var mesh = new Mesh { name = "Tripo_Face", vertices = vertices, uv = uvs };
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Front-most z of the mesh along the line through (x, y) parallel to Z
    static float FrontZ(Vector3[] v, int[] t, float x, float y)
    {
        float front = float.NegativeInfinity;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-12f) continue;
            float wa = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / d;
            float wb = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / d;
            float wc = 1f - wa - wb;
            if (wa < 0f || wb < 0f || wc < 0f) continue;
            front = Mathf.Max(front, wa * a.z + wb * b.z + wc * c.z);
        }
        return float.IsNegativeInfinity(front) ? 0f : front;
    }

    // A wristband round the right forearm, just above the hand: a band of rounded cross-section hugging the forearm
    // (and its cuff) all round, with a narrower stripe standing proud along its middle as submesh 1
    static Mesh BraceletMesh(Dictionary<int, Mesh> body)
    {
        Vector3 elbow = Joint("RightLowerArm"), wrist = Joint("RightHand");
        var axis = (wrist - elbow).normalized;
        var center = Vector3.Lerp(elbow, wrist, BraceletAt);
        var u = Vector3.Cross(axis, Vector3.forward).normalized;
        var v = Vector3.Cross(axis, u);

        // The forearm's outline round the axis, sampled within the band's width
        var reach = new float[BraceletSides];
        foreach (int part in new[] { 12, 17 })
            foreach (var p in body[part].vertices)
            {
                var d = p - center;
                if (Mathf.Abs(Vector3.Dot(d, axis)) > BraceletWidth) continue;
                float x = Vector3.Dot(d, u), y = Vector3.Dot(d, v);
                int side = Mathf.RoundToInt(Mathf.Atan2(y, x) / (2f * Mathf.PI) * BraceletSides + BraceletSides) % BraceletSides;
                for (int k = -2; k <= 2; k++) // spread to neighbours so sparse vertices leave no dips
                {
                    int i = (side + k + BraceletSides) % BraceletSides;
                    reach[i] = Mathf.Max(reach[i], Mathf.Sqrt(x * x + y * y));
                }
            }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var band = new List<int>();
        var stripe = new List<int>();
        void Ring(float width, float thickness, float lift, List<int> triangles)
        {
            int start = vertices.Count;
            for (int i = 0; i <= BraceletSides; i++)
            {
                float a = i * 2f * Mathf.PI / BraceletSides;
                var outward = Mathf.Cos(a) * u + Mathf.Sin(a) * v;
                float r = reach[i % BraceletSides] + BraceletClearance + lift;
                for (int j = 0; j <= BraceletRound; j++)
                {
                    float b = j * 2f * Mathf.PI / BraceletRound;
                    vertices.Add(center + outward * (r + thickness / 2f * (1f + Mathf.Cos(b))) + axis * (width / 2f * Mathf.Sin(b)));
                    normals.Add((outward * Mathf.Cos(b) / thickness + axis * Mathf.Sin(b) / width).normalized);
                }
            }
            int n = BraceletRound + 1;
            for (int i = 0; i < BraceletSides; i++)
                for (int j = 0; j < BraceletRound; j++)
                {
                    int a = start + i * n + j, b = a + n;
                    triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
        }
        Ring(BraceletWidth, BraceletThickness, 0f, band);
        Ring(BraceletWidth * 0.35f, BraceletThickness, BraceletThickness * 0.35f, stripe);

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(band, 0);
        mesh.SetTriangles(stripe, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 Joint(string bone)
    {
        foreach (var (name, _, pos) in Joints)
            if (name == bone) return pos;
        throw new System.ArgumentException(bone);
    }

    static Mesh SaveMesh(Mesh generated, string name)
    {
        generated.name = name;
        string path = $"{MeshFolder}/{name}.asset";
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

    // ---------------------------------------------------------------- avatar

    // The body is modelled in an A-pose; the avatar's reference pose is a T-pose, so each limb is aimed straight out
    // (arms) or down (legs) here, the lower bone lining up with the upper one
    static Avatar BuildAvatar(GameObject root, Dictionary<string, Transform> bones)
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
        foreach (var (bone, _, _) in Joints)
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
            // Rigid parts have no twist bones: put all of a limb's twist on the bone itself so it turns the elbow/knee
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
        if (!avatar.isValid || !avatar.isHuman) Debug.LogError($"[Tripo] Avatar for {root.name} is not a valid humanoid");
        return avatar;
    }

    static Avatar SaveAvatar(Avatar avatar, string path)
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

    // ---------------------------------------------------------------- assets

    static Material PartMaterial(Color color)
    {
        string key = "Tripo_" + ColorUtility.ToHtmlStringRGB(color);
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

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
