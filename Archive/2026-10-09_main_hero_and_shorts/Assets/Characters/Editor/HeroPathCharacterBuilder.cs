using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Builds main_hero, the channel's main character, from the plain grey body in parts in
// Assets/Characters/HeroPath/HeroPath.fbx (17 tripo_part_N pieces, 1 unit tall, facing +Z) as a prefab in
// Assets/Characters/HeroPath/Prefabs. Each part stays its own mesh and moves with its own bone of a 17-bone skeleton
// named after Unity's humanoid bones, clavicles included, so every prefab gets a Humanoid avatar and plays humanoid
// clips. Nothing shows through where the parts meet: every opening a part has is capped, and at the elbows, wrists,
// hips, knees and ankles the ends of the two parts meeting there hand over to each other's bone within JointBand of the
// joint, half and half at the joint itself, so both edges move together and the joint bends like cloth instead of
// opening a gap. Shoulders, neck and head stay rigid, Roblox-style; the torso bends at
// the waist. Each Spec dresses the body by region (shirt, sleeves cut straight across, jeans, shoes), puts the logo on
// the shirt, a RobloxFace on the head (RobloxDecalAtlasBuilder) and one of Tripo's hair styles stretched onto it.
// Edit Specs and re-run Tools/Characters/Build main_hero; prefabs are rebuilt in place.
public static class HeroPathCharacterBuilder
{
    public const float Height = 1.8f; // metres; the body is modelled 1 unit tall
    public const string Folder = "Assets/Characters/HeroPath";
    public const string PrefabFolder = Folder + "/Prefabs";
    const string BodyPath = Folder + "/HeroPath.fbx";
    const string LogoPath = Folder + "/DLogo.png";
    const string MeshFolder = Folder + "/Meshes";
    const string MaterialFolder = Folder + "/Materials";
    const string AvatarFolder = Folder + "/Avatars";

    // Joint positions in body units; the body stands on the origin facing +Z, so its left side is -X. Hips pivot at the
    // top of the leg. Each arm hangs from a clavicle (Shoulder, by the neck) at the side of the torso a little below its
    // top, where an arm held out level lines up with the shoulders; animation lifts the clavicle as the arm goes higher,
    // so a raised arm comes up out of the shoulder instead of leaving a gap beside it.
    static readonly (string bone, string parent, Vector3 pos)[] Joints =
    {
        ("Hips", null, new Vector3(0f, 0.44f, -0.01f)),
        ("Spine", "Hips", new Vector3(0f, 0.52f, -0.01f)),
        ("Head", "Spine", new Vector3(0f, 0.79f, -0.012f)),
        ("LeftShoulder", "Spine", new Vector3(-0.045f, 0.765f, -0.017f)),
        ("LeftUpperArm", "LeftShoulder", new Vector3(-0.15f, 0.7f, -0.017f)),
        ("LeftLowerArm", "LeftUpperArm", new Vector3(-0.202f, 0.59f, -0.017f)),
        ("LeftHand", "LeftLowerArm", new Vector3(-0.225f, 0.47f, -0.006f)),
        ("RightShoulder", "Spine", new Vector3(0.045f, 0.765f, -0.017f)),
        ("RightUpperArm", "RightShoulder", new Vector3(0.15f, 0.7f, -0.017f)),
        ("RightLowerArm", "RightUpperArm", new Vector3(0.202f, 0.59f, -0.017f)),
        ("RightHand", "RightLowerArm", new Vector3(0.225f, 0.47f, -0.006f)),
        ("LeftUpperLeg", "Hips", new Vector3(-0.076f, 0.44f, -0.021f)),
        ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-0.085f, 0.245f, -0.021f)),
        ("LeftFoot", "LeftLowerLeg", new Vector3(-0.1f, 0.075f, -0.02f)),
        ("RightUpperLeg", "Hips", new Vector3(0.076f, 0.44f, -0.021f)),
        ("RightLowerLeg", "RightUpperLeg", new Vector3(0.085f, 0.245f, -0.021f)),
        ("RightFoot", "RightLowerLeg", new Vector3(0.1f, 0.075f, -0.02f)),
    };

    enum Region { Skin, Shirt, Sleeve, Forearm, Pants, Shin, Shoe }

    // Which bone each tripo_part_<n> rides on and which colour it takes; the torso (part 1) is built separately
    const int TorsoPart = 1;
    static readonly (int part, string name, string bone, Region region)[] Parts =
    {
        (0, "HeadMesh", "Head", Region.Skin),
        (16, "Neck", "Spine", Region.Skin),
        (6, "LeftUpperArmMesh", "LeftUpperArm", Region.Sleeve),
        (2, "RightUpperArmMesh", "RightUpperArm", Region.Sleeve),
        (9, "LeftLowerArmMesh", "LeftLowerArm", Region.Forearm),
        (14, "LeftCuff", "LeftLowerArm", Region.Forearm),
        (8, "RightLowerArmMesh", "RightLowerArm", Region.Forearm),
        (15, "RightCuff", "RightLowerArm", Region.Forearm),
        (5, "LeftHandMesh", "LeftHand", Region.Skin),
        (7, "RightHandMesh", "RightHand", Region.Skin),
        (12, "LeftUpperLegMesh", "LeftUpperLeg", Region.Pants),
        (13, "RightUpperLegMesh", "RightUpperLeg", Region.Pants),
        (10, "LeftLowerLegMesh", "LeftLowerLeg", Region.Shin),
        (11, "RightLowerLegMesh", "RightLowerLeg", Region.Shin),
        (4, "LeftFootMesh", "LeftFoot", Region.Shoe),
        (3, "RightFootMesh", "RightFoot", Region.Shoe),
    };

    // Soft joints: the bones whose pivot joins two parts that bend there together (body units of hand-over each side)
    const float JointBand = 0.035f;
    static readonly HashSet<string> SoftJoints = new HashSet<string>
    {
        "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand", "LeftUpperLeg", "RightUpperLeg",
        "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
    };

    // The sleeves end straight across the upper arm this high (body units, on the arm's centre line); below it the upper
    // arm is skin-coloured like the forearm, hiding the uneven line where the two parts meet
    static readonly int[] UpperArmParts = { 6, 2 };
    const float SleeveHem = 0.612f;

    // The torso: shirt above the waistband, jeans below, bending smoothly from Hips to Spine between BendFrom and BendTo
    const float Waistband = 0.455f, BendFrom = 0.47f, BendTo = 0.58f;

    // Face and logo grids (body units): the face a little above the middle of the head, the logo on the chest
    const float FaceCenterY = 0.9f, FaceSize = 0.165f, LogoCenterY = 0.665f, LogoSize = 0.13f, DecalLift = 0.0025f;
    const float HairInflate = 1.03f; // room at the corners of the boxy head for the stretched hair

    class Spec
    {
        public string name;
        public Color skin, shirt, pants, shoes;
        public Color? sleeves, forearms, shins; // default to shirt, skin and pants
        public bool logo;
        public string logoTexture;              // the chest logo, null for the D logo (LogoPath)
        public string hair;                     // a Tripo hair style (Tripo_Hair_<hair>), or null for none
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
        // Black T-shirt with the D logo, black jeans, white trainers, blue bacon hair
        new Spec
        {
            name = "main_hero", skin = Hex("EDEDED"), shirt = Hex("1E1E22"), pants = Hex("25272E"), shoes = Hex("F2F2F2"),
            logo = true, hair = "Bacon", hairColor = Hex("1F4FD6"), face = RobloxFace.Emotion.Smile,
        },
        // The mirror short's hero: main_hero's body with beige skin, orange bacon hair, a question mark on the T-shirt,
        // black trainers
        new Spec
        {
            name = "question_hero", skin = Hex("EBC79E"), shirt = Hex("1E1E22"), pants = Hex("2A2B31"), shoes = Hex("1C1C20"),
            logo = true, logoTexture = Folder + "/QuestionLogo.png", hair = "Bacon", hairColor = Hex("A64F17"), face = RobloxFace.Emotion.Smile,
        },
        // The plane short's hero: question_hero with an exclamation mark on the T-shirt
        new Spec
        {
            name = "exclaim_hero", skin = Hex("EBC79E"), shirt = Hex("1E1E22"), pants = Hex("2A2B31"), shoes = Hex("1C1C20"),
            logo = true, logoTexture = Folder + "/ExclaimLogo.png", hair = "Bacon", hairColor = Hex("A64F17"), face = RobloxFace.Emotion.Angry,
        },
        // The plane short's giant (scaled up in the scene): bald under a propeller cap the short adds, light grey T-shirt
        new Spec
        {
            name = "giant", skin = Hex("EBC79E"), shirt = Hex("B7B9BD"), pants = Hex("2A2B31"), shoes = Hex("1E3236"),
            face = RobloxFace.Emotion.Neutral,
        },
    };

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    [MenuItem("Tools/Characters/Build main_hero")]
    public static void BuildAll()
    {
        var faceMaterial = RobloxDecalAtlasBuilder.FaceMaterial();
        if (faceMaterial == null) return;
        foreach (var folder in new[] { PrefabFolder, MeshFolder, MaterialFolder, AvatarFolder }) RigUtility.EnsureFolder(folder);
        materials.Clear();

        var body = new Dictionary<int, Mesh>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(BodyPath))
            if (asset is Mesh mesh && int.TryParse(mesh.name.Replace("tripo_part_", ""), out int index))
                body[index] = RigUtility.Capped(mesh);
        var torsoMesh = SaveMesh(TorsoMesh(body[TorsoPart]), "HeroPath_Torso");
        var faceMesh = SaveMesh(RigUtility.FaceGrid(FrontTriangles(body[0]), FaceCenterY, FaceSize, DecalLift), "HeroPath_Face");
        var logoMesh = SaveMesh(RigUtility.FaceGrid(FrontTriangles(body[TorsoPart]), LogoCenterY, LogoSize, DecalLift), "HeroPath_Logo");
        var partMeshes = new Dictionary<int, Mesh>();
        foreach (var (part, _, bone, _) in Parts) partMeshes[part] = SaveMesh(SkinnedPart(body[part], bone, System.Array.IndexOf(UpperArmParts, part) >= 0), "HeroPath_Part" + part);
        var hairMeshes = new Dictionary<string, Mesh>();
        foreach (var spec in Specs)
            if (spec.hair != null && !hairMeshes.ContainsKey(spec.hair))
                hairMeshes[spec.hair] = SaveMesh(RigUtility.StretchTripoHair(spec.hair, body[0].bounds, HairInflate), "HeroPath_Hair_" + spec.hair);

        // Build in a preview scene so the open scene isn't touched
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var spec in Specs)
                BuildCharacter(spec, partMeshes, torsoMesh, faceMesh, logoMesh, hairMeshes, faceMaterial, stage);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[HeroPath] Built {Specs.Length} characters in {PrefabFolder}");
    }

    static void BuildCharacter(Spec s, Dictionary<int, Mesh> partMeshes, Mesh torsoMesh, Mesh faceMesh, Mesh logoMesh,
        Dictionary<string, Mesh> hairMeshes, Material faceMaterial, Scene stage)
    {
        var root = new GameObject(s.name);
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

        // Each part is its own skinned mesh on the character origin, scaled to Height like the torso
        foreach (var (part, name, bone, region) in Parts)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * Height;
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = partMeshes[part];
            renderer.bones = System.Array.ConvertAll(PartBones(bone), b => bones[b]);
            renderer.rootBone = bones[bone];
            renderer.sharedMaterials = System.Array.IndexOf(UpperArmParts, part) >= 0
                ? new[] { PartMaterial(s.Of(Region.Sleeve)), PartMaterial(s.Of(Region.Forearm)) }
                : new[] { PartMaterial(s.Of(region)) };
        }

        // Rigid on the head and chest, modelled in body units like the parts
        var face = Part(bones["Head"], "Face", faceMesh, faceMaterial);
        face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        face.AddComponent<RobloxFace>().emotion = (int)s.face;
        if (s.logo)
        {
            var logo = Part(bones["Spine"], "Logo", logoMesh, LogoMaterial(s.logoTexture ?? LogoPath));
            logo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        if (s.hair != null) Part(bones["Head"], "Hair", hairMeshes[s.hair], PartMaterial(s.hairColor));

        var boneNames = new List<string>();
        foreach (var (bone, _, _) in Joints) boneNames.Add(bone);
        var avatar = RigUtility.SaveAvatar(RigUtility.BuildAvatar(root, bones, boneNames), $"{AvatarFolder}/HeroPath_{s.name}_Avatar.asset");
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{s.name}.prefab");
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

    // A part's triangles that face the front (+Z), as corner triples, for wrapping a grid onto it
    static List<Vector3> FrontTriangles(Mesh part)
    {
        var vertices = part.vertices;
        var triangles = part.triangles;
        var front = new List<Vector3>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            if (Vector3.Cross(b - a, c - a).z <= 0f) continue;
            front.AddRange(new[] { a, b, c });
        }
        return front;
    }

    // The bones a part on `bone` is skinned to: its own first, then the neighbours it bends with at soft joints (the
    // parent's, where its own pivot is soft, and its child's, where the child's pivot is)
    static string[] PartBones(string bone)
    {
        var list = new List<string> { bone };
        if (SoftJoints.Contains(bone)) list.Add(Parent(bone));
        var child = Child(bone);
        if (child != null && SoftJoints.Contains(child)) list.Add(child);
        return list.ToArray();
    }

    static string Parent(string bone)
    {
        foreach (var (name, parent, _) in Joints) if (name == bone) return parent;
        return null;
    }

    // A limb bone's one child (the trunk bones aren't asked)
    static string Child(string bone)
    {
        foreach (var (name, parent, _) in Joints) if (parent == bone) return name;
        return null;
    }

    static Vector3 Joint(string bone)
    {
        foreach (var (name, _, pos) in Joints) if (name == bone) return pos;
        throw new System.ArgumentException(bone);
    }

    // A part skinned to PartBones(bone): whole on its own bone, except within JointBand of a soft joint, where it hands
    // over to the bone across it, half and half at the joint. The two parts meeting there do the same from either side,
    // so their edges stay together as it bends. An upper arm is also cut straight across at the sleeve's hem: sleeve
    // above as submesh 0, skin below as submesh 1.
    static Mesh SkinnedPart(Mesh part, string bone, bool sleeve)
    {
        var boneNames = PartBones(bone);
        var own = Joint(bone);
        var child = Child(bone);
        // The hem runs square across the arm itself (its middle to the elbow; the shoulder pivot sits off to one side)
        var middle = part.bounds.center;
        var mesh = sleeve
            ? ClipInTwo(part, Vector3.Lerp(middle, Joint(child), Mathf.InverseLerp(middle.y, Joint(child).y, SleeveHem)), (middle - Joint(child)).normalized)
            : Object.Instantiate(part);
        var vertices = mesh.vertices;

        // Along the limb, into this part from each soft joint; a hand or foot has no child, so it heads for its middle
        var down = (child != null ? Joint(child) : part.bounds.center) - own;
        down.Normalize();
        var weights = new BoneWeight[vertices.Length];
        var share = new Dictionary<string, float>();
        for (int i = 0; i < vertices.Length; i++)
        {
            share.Clear();
            float parentShare = 0f, childShare = 0f;
            if (SoftJoints.Contains(bone)) parentShare = Handover(Vector3.Dot(vertices[i] - own, down));
            if (child != null && SoftJoints.Contains(child)) childShare = Handover(Vector3.Dot(vertices[i] - Joint(child), -down));
            share[bone] = Mathf.Max(0f, 1f - parentShare - childShare);
            if (parentShare > 0f) share[Parent(bone)] = parentShare;
            if (childShare > 0f) share[child] = childShare;
            weights[i] = RigUtility.Pack(share, new List<string>(boneNames));
        }
        mesh.boneWeights = weights;
        // The renderer sits on the character origin scaled to Height; the bones are unrotated and unscaled
        mesh.bindposes = System.Array.ConvertAll(boneNames, BindPose);
        mesh.RecalculateBounds();
        return mesh;
    }

    // How much of a vertex `into` body units inside a part (negative: past its end, into the next part) goes to the
    // bone across the joint: half at the joint, none JointBand inside, all of it JointBand beyond
    static float Handover(float into)
    {
        float x = Mathf.Clamp(into / JointBand, -1f, 1f);
        return 0.5f * (1f - Mathf.Sign(x) * Mathf.SmoothStep(0f, 1f, Mathf.Abs(x)));
    }

    // The mesh cut by a plane, the triangles crossing it clipped so the line is straight: submesh 0 on the side the
    // normal points to, submesh 1 on the other
    static Mesh ClipInTwo(Mesh source, Vector3 point, Vector3 normal)
    {
        var sourceVertices = source.vertices;
        var sourceNormals = source.normals;
        var sourceTriangles = source.triangles;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var above = new List<int>();
        var below = new List<int>();
        var corners = new Dictionary<int, int>();
        var crossings = new Dictionary<(int, int), int>();
        float Side(int v) => Vector3.Dot(sourceVertices[v] - point, normal);

        int Corner(int v)
        {
            if (corners.TryGetValue(v, out int index)) return index;
            corners[v] = vertices.Count;
            vertices.Add(sourceVertices[v]);
            normals.Add(sourceNormals[v]);
            return vertices.Count - 1;
        }

        // Where edge a-b crosses the plane; shared by both sides and by the triangles on either side of the edge
        int Crossing(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (crossings.TryGetValue(key, out int index)) return index;
            float t = Side(a) / (Side(a) - Side(b));
            crossings[key] = vertices.Count;
            vertices.Add(Vector3.Lerp(sourceVertices[a], sourceVertices[b], t));
            normals.Add(Vector3.Lerp(sourceNormals[a], sourceNormals[b], t).normalized);
            return vertices.Count - 1;
        }

        // Keeps the part of triangle (a, b, c) on one side of the plane and adds it as a fan
        void Clip(int a, int b, int c, bool keepAbove, List<int> triangles)
        {
            var polygon = new List<int>(4);
            int[] triangle = { a, b, c };
            for (int k = 0; k < 3; k++)
            {
                int from = triangle[k], to = triangle[(k + 1) % 3];
                bool fromIn = Side(from) >= 0f == keepAbove;
                bool toIn = Side(to) >= 0f == keepAbove;
                if (fromIn) polygon.Add(Corner(from));
                if (fromIn != toIn) polygon.Add(Crossing(from, to));
            }
            for (int k = 1; k + 1 < polygon.Count; k++) triangles.AddRange(new[] { polygon[0], polygon[k], polygon[k + 1] });
        }

        for (int i = 0; i < sourceTriangles.Length; i += 3)
        {
            int a = sourceTriangles[i], b = sourceTriangles[i + 1], c = sourceTriangles[i + 2];
            Clip(a, b, c, true, above);
            Clip(a, b, c, false, below);
        }

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(above, 0);
        mesh.SetTriangles(below, 1);
        return mesh;
    }

    // The torso skinned to Hips (bone 0) and Spine (bone 1), cut at the waistband into submesh 0 (shirt, above) and
    // submesh 1 (jeans, below) so the colour line is straight
    static Mesh TorsoMesh(Mesh torso)
    {
        var mesh = ClipInTwo(torso, new Vector3(0f, Waistband, 0f), Vector3.up);
        var vertices = mesh.vertices;
        var weights = new BoneWeight[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            float spine = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BendFrom, BendTo, vertices[i].y));
            // Heavier bone first
            weights[i] = spine >= 0.5f
                ? new BoneWeight { boneIndex0 = 1, weight0 = spine, boneIndex1 = 0, weight1 = 1f - spine }
                : new BoneWeight { boneIndex0 = 0, weight0 = 1f - spine, boneIndex1 = 1, weight1 = spine };
        }
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

    static Mesh SaveMesh(Mesh generated, string name) => RigUtility.SaveMesh(generated, $"{MeshFolder}/{name}.asset");

    // ---------------------------------------------------------------- materials

    static Material PartMaterial(Color color)
    {
        string key = "HeroPath_" + ColorUtility.ToHtmlStringRGB(color);
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

    // The logo, cut out along its alpha
    static Material LogoMaterial(string texturePath)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        if (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        string path = texturePath == LogoPath ? $"{MaterialFolder}/HeroPath_Logo.mat"
            : $"{MaterialFolder}/HeroPath_Logo_{System.IO.Path.GetFileNameWithoutExtension(texturePath)}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.25f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(material);
        return material;
    }

    static Color Hex(string hex) => RigUtility.Hex(hex);
}
