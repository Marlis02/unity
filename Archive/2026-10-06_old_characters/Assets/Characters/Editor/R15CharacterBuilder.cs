using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Builds Roblox R15-style characters as prefabs in Assets/Characters/Prefabs: rounded-box parts on a 15-bone skeleton
// named after Unity's humanoid bones, so every prefab gets a Humanoid avatar and plays Mixamo clips
// (drop FBX files into Assets/Characters/Mixamo). Faces are a RobloxFace quad showing the face atlas.
// Edit Specs and re-run Tools/Characters/Build R15 Characters; prefabs are rebuilt in place.
public static class R15CharacterBuilder
{
    public const float Stud = 0.35f; // world units per Roblox stud: a 5.25-stud R15 stands ~1.84 m tall
    public const string PrefabFolder = "Assets/Characters/Prefabs";
    const string MeshFolder = "Assets/Characters/Meshes";
    const string MaterialFolder = "Assets/Characters/Materials";
    const string AvatarFolder = "Assets/Characters/Avatars";
    public const string FaceMaterialPath = MaterialFolder + "/R15_Face.mat";
    const float FaceSize = 1.1f; // studs; the head is 1.25

    static readonly Vector3 HeadCenter = new Vector3(0f, 4.625f, 0f);

    // Joint positions in studs; the character stands on the origin facing +Z, so its left side is -X
    static readonly (string bone, string parent, Vector3 pos)[] Joints =
    {
        ("Hips", null, new Vector3(0f, 2.2f, 0f)),
        ("Spine", "Hips", new Vector3(0f, 2.4f, 0f)),
        ("Head", "Spine", new Vector3(0f, 4f, 0f)),
        ("LeftUpperArm", "Spine", new Vector3(-1.5f, 3.65f, 0f)),
        ("LeftLowerArm", "LeftUpperArm", new Vector3(-1.5f, 3.05f, 0f)),
        ("LeftHand", "LeftLowerArm", new Vector3(-1.5f, 2.3f, 0f)),
        ("RightUpperArm", "Spine", new Vector3(1.5f, 3.65f, 0f)),
        ("RightLowerArm", "RightUpperArm", new Vector3(1.5f, 3.05f, 0f)),
        ("RightHand", "RightLowerArm", new Vector3(1.5f, 2.3f, 0f)),
        ("LeftUpperLeg", "Hips", new Vector3(-0.5f, 2f, 0f)),
        ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-0.5f, 1.05f, 0f)),
        ("LeftFoot", "LeftLowerLeg", new Vector3(-0.5f, 0.25f, 0f)),
        ("RightUpperLeg", "Hips", new Vector3(0.5f, 2f, 0f)),
        ("RightLowerLeg", "RightUpperLeg", new Vector3(0.5f, 1.05f, 0f)),
        ("RightFoot", "RightLowerLeg", new Vector3(0.5f, 0.25f, 0f)),
    };

    enum Hair { None, Short, Long, Ponytail }

    class Spec
    {
        public string name;
        public Color skin, shirt, pants, shoes;
        public Color? sole = Hex("F2F2F2");
        public Color? sleeves, forearms, shins; // upper arms / lower arms default to skin, shins to pants
        public Hair hair;
        public Color hairColor, accent;
        public Color? cap;
        public bool capBackwards, glasses, sunglasses, hoodie, belt, collar, necklace;
    }

    static readonly Spec[] Specs =
    {
        new Spec { name = "Noob", skin = Hex("F5CD30"), shirt = Hex("0D69AC"), pants = Hex("A4BD47"), shoes = Hex("A4BD47"), sole = null },
        new Spec
        {
            name = "Pro", skin = Hex("8C5C40"), shirt = Hex("26272B"), sleeves = Hex("26272B"), forearms = Hex("26272B"),
            pants = Hex("3A3D45"), shoes = Hex("F2F2F2"), sole = Hex("D62828"), cap = Hex("D62828"), capBackwards = true,
            sunglasses = true, hoodie = true,
        },
        new Spec
        {
            name = "Mom", skin = Hex("F9D5BB"), shirt = Hex("E8676B"), sleeves = Hex("E8676B"), pants = Hex("3B5B92"),
            shoes = Hex("F2F2F2"), sole = Hex("C9C9C9"), hair = Hair.Long, hairColor = Hex("6B3E26"), necklace = true,
        },
        new Spec
        {
            name = "Dad", skin = Hex("E0AC86"), shirt = Hex("2E8B7A"), sleeves = Hex("2E8B7A"), pants = Hex("C8B08A"),
            shoes = Hex("5A3A22"), sole = Hex("2E2E2E"), hair = Hair.Short, hairColor = Hex("3B2A20"), glasses = true,
            belt = true, collar = true,
        },
        new Spec
        {
            name = "Sister", skin = Hex("FFE0C7"), shirt = Hex("B57EDC"), sleeves = Hex("B57EDC"), pants = Hex("6E8FC9"),
            shins = Hex("FFE0C7"), shoes = Hex("FF8FB3"), hair = Hair.Ponytail, hairColor = Hex("F2C14E"), accent = Hex("FF5C9A"),
        },
    };

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

    [MenuItem("Tools/Characters/Build R15 Characters")]
    public static void BuildAll()
    {
        foreach (var folder in new[] { PrefabFolder, MeshFolder, MaterialFolder, AvatarFolder }) EnsureFolder(folder);
        materials.Clear();
        meshes.Clear();
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(FaceAtlasBuilder.AtlasPath) ?? FaceAtlasBuilder.Build();
        var faceMaterial = FaceMaterial(atlas);

        // Build in a preview scene so the open scene isn't touched
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var spec in Specs) BuildCharacter(spec, faceMaterial, stage);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[R15] Built {Specs.Length} characters in {PrefabFolder}");
    }

    static void BuildCharacter(Spec s, Material faceMaterial, Scene stage)
    {
        var root = new GameObject("R15_" + s.name);
        SceneManager.MoveGameObjectToScene(root, stage);
        var bones = new Dictionary<string, Transform>();
        foreach (var (bone, parent, pos) in Joints)
        {
            var t = new GameObject(bone).transform;
            t.SetParent(parent == null ? root.transform : bones[parent], false);
            t.position = pos * Stud;
            bones[bone] = t;
        }

        Part(bones["Hips"], "LowerTorso", new Vector3(0f, 2.2f, 0f), new Vector3(2f, 0.4f, 1f), s.pants);
        Part(bones["Spine"], "UpperTorso", new Vector3(0f, 3.2f, 0f), new Vector3(2f, 1.6f, 1f), s.shirt);
        Part(bones["Head"], "HeadMesh", HeadCenter, Vector3.one * 1.25f, s.skin, 0.3f);
        foreach (float side in new[] { -1f, 1f })
        {
            string n = side < 0f ? "Left" : "Right";
            float arm = side * 1.5f, leg = side * 0.5f;
            Part(bones[n + "UpperArm"], n + "UpperArmMesh", new Vector3(arm, 3.5f, 0f), Vector3.one, s.sleeves ?? s.skin);
            Part(bones[n + "LowerArm"], n + "LowerArmMesh", new Vector3(arm, 2.67f, 0f), new Vector3(0.96f, 0.86f, 0.96f), s.forearms ?? s.skin);
            Part(bones[n + "Hand"], n + "HandMesh", new Vector3(arm, 2.13f, 0f), new Vector3(0.92f, 0.3f, 0.92f), s.skin, 0.1f);
            Part(bones[n + "UpperLeg"], n + "UpperLegMesh", new Vector3(leg, 1.5f, 0f), Vector3.one, s.pants);
            Part(bones[n + "LowerLeg"], n + "LowerLegMesh", new Vector3(leg, 0.65f, 0f), new Vector3(0.96f, 0.86f, 0.96f), s.shins ?? s.pants);
            Part(bones[n + "Foot"], n + "FootMesh", new Vector3(leg, 0.14f, 0.06f), new Vector3(1f, 0.28f, 1.12f), s.shoes, 0.1f);
            if (s.sole is Color sole)
                Part(bones[n + "Foot"], n + "Sole", new Vector3(leg, 0.035f, 0.06f), new Vector3(1.04f, 0.07f, 1.16f), sole, 0.03f);
        }

        AddFace(bones["Head"], faceMaterial);
        AddHair(bones["Head"], s);
        AddAccessories(bones, s);

        var avatar = SaveAvatar(BuildAvatar(root, bones), $"{AvatarFolder}/R15_{s.name}_Avatar.asset");
        var animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/R15_{s.name}.prefab");
        Object.DestroyImmediate(root);
    }

    static void AddFace(Transform head, Material material)
    {
        var face = new GameObject("Face");
        face.transform.SetParent(head, false);
        face.transform.position = (HeadCenter + new Vector3(0f, 0f, 0.625f + 0.012f)) * Stud;
        face.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // the built-in quad faces -Z
        face.transform.localScale = Vector3.one * FaceSize * Stud;
        face.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var renderer = face.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        face.AddComponent<RobloxFace>();
    }

    static void AddHair(Transform head, Spec s)
    {
        if (s.hair == Hair.None) return;
        var H = HeadCenter;
        var c = s.hairColor;
        Part(head, "HairTop", H + new Vector3(0f, 0.5f, 0f), new Vector3(1.33f, 0.36f, 1.33f), c, 0.15f);
        Part(head, "HairFringe", H + new Vector3(0f, 0.37f, 0.55f), new Vector3(1.33f, 0.18f, 0.25f), c, 0.08f);
        switch (s.hair)
        {
            case Hair.Short:
                Part(head, "HairBack", H + new Vector3(0f, 0.05f, -0.52f), new Vector3(1.33f, 1f, 0.32f), c);
                break;
            case Hair.Long:
                Part(head, "HairBack", H + new Vector3(0f, -0.22f, -0.52f), new Vector3(1.37f, 1.55f, 0.34f), c, 0.15f);
                Part(head, "HairLeft", H + new Vector3(-0.69f, -0.02f, -0.04f), new Vector3(0.2f, 1.05f, 1.05f), c, 0.08f);
                Part(head, "HairRight", H + new Vector3(0.69f, -0.02f, -0.04f), new Vector3(0.2f, 1.05f, 1.05f), c, 0.08f);
                break;
            case Hair.Ponytail:
                Part(head, "HairBack", H + new Vector3(0f, 0.05f, -0.52f), new Vector3(1.33f, 1f, 0.32f), c);
                Part(head, "Ponytail", H + new Vector3(0f, -0.05f, -0.86f), new Vector3(0.42f, 0.9f, 0.42f), c, 0.18f);
                Part(head, "Scrunchie", H + new Vector3(0f, 0.38f, -0.8f), new Vector3(0.5f, 0.14f, 0.5f), s.accent, 0.06f);
                break;
        }
    }

    static void AddAccessories(Dictionary<string, Transform> b, Spec s)
    {
        var head = b["Head"];
        var spine = b["Spine"];
        var H = HeadCenter;
        if (s.cap is Color cap)
        {
            Part(head, "CapCrown", H + new Vector3(0f, 0.55f, 0f), new Vector3(1.36f, 0.42f, 1.36f), cap, 0.18f);
            Part(head, "CapBrim", H + new Vector3(0f, 0.38f, s.capBackwards ? -0.93f : 0.93f), new Vector3(1.1f, 0.07f, 0.65f), cap, 0.03f);
        }

        // Eyes sit at (+-0.3, 0.12) of the face quad
        float eyeX = 0.3f * FaceSize * 0.5f, eyeY = H.y + 0.12f * FaceSize * 0.5f, front = H.z + 0.625f;
        if (s.glasses || s.sunglasses)
        {
            var frame = Hex("1E1E22");
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * eyeX;
                if (s.sunglasses)
                    Part(head, "Lens", new Vector3(x, eyeY + 0.01f, front + 0.045f), new Vector3(0.27f, 0.17f, 0.05f), Hex("0E0E10"), 0.04f, 0.92f);
                else
                {
                    const float w = 0.26f, h = 0.24f, bar = 0.035f;
                    float z = front + 0.04f;
                    Part(head, "RimTop", new Vector3(x, eyeY + (h - bar) * 0.5f, z), new Vector3(w, bar, 0.04f), frame, 0.012f);
                    Part(head, "RimBottom", new Vector3(x, eyeY - (h - bar) * 0.5f, z), new Vector3(w, bar, 0.04f), frame, 0.012f);
                    Part(head, "RimOuter", new Vector3(x + side * (w - bar) * 0.5f, eyeY, z), new Vector3(bar, h, 0.04f), frame, 0.012f);
                    Part(head, "RimInner", new Vector3(x - side * (w - bar) * 0.5f, eyeY, z), new Vector3(bar, h, 0.04f), frame, 0.012f);
                }
                // Temple arms: across the face edge, then back along the side of the head
                float outer = eyeX + 0.135f;
                Part(head, "TempleFront", new Vector3(side * (outer + 0.64f) * 0.5f, eyeY + 0.06f, front + 0.03f), new Vector3(0.64f - outer, 0.035f, 0.035f), frame, 0.012f);
                Part(head, "TempleSide", new Vector3(side * 0.645f, eyeY + 0.06f, front - 0.3f), new Vector3(0.035f, 0.035f, 0.62f), frame, 0.012f);
            }
            Part(head, "Bridge", new Vector3(0f, eyeY + 0.05f, front + 0.045f), new Vector3(0.06f, 0.03f, 0.03f), frame, 0.01f);
        }

        if (s.hoodie)
        {
            var dark = s.shirt * 0.82f;
            dark.a = 1f;
            Part(spine, "Hood", new Vector3(0f, 4.05f, -0.42f), new Vector3(1.6f, 0.5f, 0.55f), dark, 0.2f);
            Part(spine, "Pocket", new Vector3(0f, 2.75f, 0.5f), new Vector3(1.2f, 0.42f, 0.06f), dark, 0.03f);
            foreach (float side in new[] { -1f, 1f })
            {
                Part(spine, "String", new Vector3(side * 0.2f, 3.72f, 0.52f), new Vector3(0.05f, 0.42f, 0.04f), Hex("EDEDED"), 0.015f);
                Part(spine, "StringTip", new Vector3(side * 0.2f, 3.49f, 0.52f), new Vector3(0.07f, 0.08f, 0.06f), Hex("BDBDBD"), 0.02f);
            }
        }
        if (s.collar)
        {
            var light = Color.Lerp(s.shirt, Color.white, 0.25f);
            Part(spine, "Collar", new Vector3(0f, 3.95f, 0.42f), new Vector3(1f, 0.14f, 0.2f), light, 0.05f);
            Part(spine, "Placket", new Vector3(0f, 3.68f, 0.51f), new Vector3(0.18f, 0.5f, 0.04f), light, 0.015f);
            Part(spine, "Button", new Vector3(0f, 3.82f, 0.535f), new Vector3(0.06f, 0.06f, 0.03f), Hex("F5F5F5"), 0.015f);
            Part(spine, "Button", new Vector3(0f, 3.6f, 0.535f), new Vector3(0.06f, 0.06f, 0.03f), Hex("F5F5F5"), 0.015f);
        }
        if (s.necklace)
        {
            var gold = Hex("E8C15A");
            // Chains run from the neck corners (+-0.35, 3.98) down to the pendant (0, 3.66)
            foreach (float side in new[] { -1f, 1f })
                Part(spine, "Chain", new Vector3(side * 0.175f, 3.82f, 0.51f), new Vector3(0.03f, 0.47f, 0.02f), gold, 0.01f, 0.7f,
                    Quaternion.Euler(0f, 0f, -side * 48f));
            Part(spine, "Pendant", new Vector3(0f, 3.66f, 0.52f), new Vector3(0.12f, 0.12f, 0.04f), gold, 0.03f, 0.7f, Quaternion.Euler(0f, 0f, 45f));
        }
        if (s.belt)
        {
            Part(b["Hips"], "Belt", new Vector3(0f, 2.33f, 0f), new Vector3(2.04f, 0.14f, 1.04f), Hex("4A3222"), 0.04f);
            Part(b["Hips"], "Buckle", new Vector3(0f, 2.33f, 0.53f), new Vector3(0.24f, 0.16f, 0.04f), Hex("D4AF37"), 0.015f, 0.7f);
        }
    }

    // A rounded box centered at `center` (studs, character space), parented to a bone
    static GameObject Part(Transform parent, string name, Vector3 center, Vector3 size, Color color,
        float radius = 0.12f, float smoothness = 0.28f, Quaternion? rotation = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center * Stud, rotation ?? Quaternion.identity);
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, radius);
        go.AddComponent<MeshRenderer>().sharedMaterial = PartMaterial(color, smoothness);
        return go;
    }

    // ---------------------------------------------------------------- avatar

    // Arms hang down in the model; the avatar's reference pose is a T-pose, so the upper arms are rotated out here
    static Avatar BuildAvatar(GameObject root, Dictionary<string, Transform> bones)
    {
        var human = new List<HumanBone>();
        var skeleton = new List<SkeletonBone>
        {
            new SkeletonBone { name = root.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one },
        };
        foreach (var (bone, _, _) in Joints)
        {
            human.Add(new HumanBone { boneName = bone, humanName = bone, limit = new HumanLimit { useDefaultValues = true } });
            var rotation = bone == "LeftUpperArm" ? Quaternion.Euler(0f, 0f, -90f)
                : bone == "RightUpperArm" ? Quaternion.Euler(0f, 0f, 90f)
                : Quaternion.identity;
            skeleton.Add(new SkeletonBone { name = bone, position = bones[bone].localPosition, rotation = rotation, scale = Vector3.one });
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
        if (!avatar.isValid || !avatar.isHuman) Debug.LogError($"[R15] Avatar for {root.name} is not a valid humanoid");
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

    static Mesh BoxMesh(Vector3 size, float radius)
    {
        string key = $"Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_r{radius:0.###}";
        if (meshes.TryGetValue(key, out var mesh)) return mesh;
        var generated = RoundedBoxMesh.Create(size * Stud, radius * Stud);
        generated.name = key;
        string path = $"{MeshFolder}/{key}.asset";
        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            AssetDatabase.CreateAsset(generated, path);
            mesh = generated;
        }
        else
        {
            // Overwrite in place so prefabs keep their mesh references
            EditorUtility.CopySerialized(generated, mesh);
            Object.DestroyImmediate(generated);
        }
        meshes[key] = mesh;
        return mesh;
    }

    static Material PartMaterial(Color color, float smoothness)
    {
        string key = $"R15_{ColorUtility.ToHtmlStringRGB(color)}_{Mathf.RoundToInt(smoothness * 100)}";
        if (materials.TryGetValue(key, out var material)) return material;
        string path = $"{MaterialFolder}/{key}.mat";
        material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        materials[key] = material;
        return material;
    }

    // Lit, alpha-blended, so the face picks up the same light as the head
    static Material FaceMaterial(Texture2D atlas)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(FaceMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, FaceMaterialPath);
        }
        material.SetTexture("_BaseMap", atlas);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.2f);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_ReceiveShadows", 1f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
        EditorUtility.SetDirty(material);
        return material;
    }

    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
