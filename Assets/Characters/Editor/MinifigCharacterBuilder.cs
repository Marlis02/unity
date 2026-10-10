using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Builds the characters on the minifigure body the user sent (Assets/Characters/Minifig/Minifig.fbx: 20 tripo_part_N
// pieces, 1 unit tall, facing +Z) as prefabs in Assets/Characters/Minifig/Prefabs. bacon is the main character since
// 2026-10-09; new characters are new Specs here (other colours, hair, logo). Built the way main_hero was (its builder
// is in Archive/2026-10-09_main_hero_and_shorts): each part its own mesh on its own bone of a 17-bone skeleton named
// like Unity's humanoid one, clavicles included, but no avatar (clips key the bones); every opening capped; elbows and
// knees stitched into one mesh per limb (Stitches); soft elbows, hips, knees and ankles that hand over within
// JointBand of the joint; rigid shoulders, wrists, neck and head; the torso bending at the waist. Each Spec dresses the
// body by region (shirt, sleeves cut straight across, jeans, shoes), puts its logo on the shirt, a RobloxFace on the
// head and its hair on top. Edit Specs and re-run Tools/Characters/Build Characters; prefabs are rebuilt in place.
public static class MinifigCharacterBuilder
{
    public const float Height = 1.8f; // metres; the body is modelled 1 unit tall
    public const string Folder = "Assets/Characters/Minifig";
    public const string PrefabFolder = Folder + "/Prefabs";
    const string BodyPath = Folder + "/Minifig.fbx";
    // The user's logo, a white D with a blue play button (Imports/play_logo_source.png cropped to the D)
    const string LogoPath = Folder + "/PlayLogo.png";
    const string MeshFolder = Folder + "/Meshes";
    const string MaterialFolder = Folder + "/Materials";

    // Joint positions in body units; the body stands on the origin facing +Z, so its left side is -X. Hips pivot at the
    // top of the leg. Each arm hangs from a clavicle (Shoulder, by the neck) at the side of the torso a little below its
    // top, where an arm held out level lines up with the shoulders; animation lifts the clavicle as the arm goes higher,
    // so a raised arm comes up out of the shoulder instead of leaving a gap beside it.
    static readonly (string bone, string parent, Vector3 pos)[] Joints =
    {
        ("Hips", null, new Vector3(0f, 0.425f, -0.01f)),
        ("Spine", "Hips", new Vector3(0f, 0.55f, -0.01f)),
        ("Head", "Spine", new Vector3(0f, 0.77f, -0.01f)),
        ("LeftShoulder", "Spine", new Vector3(-0.045f, 0.75f, -0.028f)),
        ("LeftUpperArm", "LeftShoulder", new Vector3(-0.179f, 0.693f, -0.028f)),
        ("LeftLowerArm", "LeftUpperArm", new Vector3(-0.247f, 0.585f, -0.03f)),
        ("LeftHand", "LeftLowerArm", new Vector3(-0.255f, 0.445f, -0.012f)),
        ("RightShoulder", "Spine", new Vector3(0.045f, 0.75f, -0.028f)),
        ("RightUpperArm", "RightShoulder", new Vector3(0.179f, 0.693f, -0.028f)),
        ("RightLowerArm", "RightUpperArm", new Vector3(0.247f, 0.585f, -0.03f)),
        ("RightHand", "RightLowerArm", new Vector3(0.255f, 0.445f, -0.012f)),
        ("LeftUpperLeg", "Hips", new Vector3(-0.079f, 0.425f, -0.01f)),
        ("LeftLowerLeg", "LeftUpperLeg", new Vector3(-0.08f, 0.222f, -0.01f)),
        ("LeftFoot", "LeftLowerLeg", new Vector3(-0.083f, 0.077f, -0.011f)),
        ("RightUpperLeg", "Hips", new Vector3(0.079f, 0.425f, -0.01f)),
        ("RightLowerLeg", "RightUpperLeg", new Vector3(0.08f, 0.222f, -0.01f)),
        ("RightFoot", "RightLowerLeg", new Vector3(0.083f, 0.077f, -0.011f)),
    };

    enum Region { Skin, Shirt, Sleeve, Forearm, Pants, Shin, Shoe, Wrist }

    // Which bone each tripo_part_<n> rides on and which colour it takes; the torso (part 2) is built separately. The
    // figure's left is -X. Parts 16/17 are the tops of the legs, hidden in the torso; 18/19 the wrists; 10/11 the toes.
    const int TorsoPart = 2;
    static readonly (int part, string name, string bone, Region region)[] Parts =
    {
        (0, "HeadMesh", "Head", Region.Skin),
        (8, "LeftUpperArmMesh", "LeftUpperArm", Region.Sleeve),
        (1, "RightUpperArmMesh", "RightUpperArm", Region.Sleeve),
        (3, "LeftLowerArmMesh", "LeftLowerArm", Region.Forearm),
        (5, "RightLowerArmMesh", "RightLowerArm", Region.Forearm),
        (19, "LeftWrist", "LeftHand", Region.Wrist),
        (18, "RightWrist", "RightHand", Region.Wrist),
        (12, "LeftHandMesh", "LeftHand", Region.Skin),
        (13, "RightHandMesh", "RightHand", Region.Skin),
        (17, "LeftHip", "LeftUpperLeg", Region.Pants),
        (16, "RightHip", "RightUpperLeg", Region.Pants),
        (9, "LeftUpperLegMesh", "LeftUpperLeg", Region.Pants),
        (7, "RightUpperLegMesh", "RightUpperLeg", Region.Pants),
        (15, "LeftLowerLegMesh", "LeftLowerLeg", Region.Shin),
        (14, "RightLowerLegMesh", "RightLowerLeg", Region.Shin),
        (6, "LeftFootMesh", "LeftFoot", Region.Shoe),
        (4, "RightFootMesh", "RightFoot", Region.Shoe),
        (11, "LeftToe", "LeftFoot", Region.Shoe),
        (10, "RightToe", "RightFoot", Region.Shoe),
    };

    // Elbows and knees are stitched (the user: "the elbow looks cut off, there must be no cut in any position"). The
    // parts meet end to end there with rounded ends, which a bend showed as a cut; each is cut where it is still its
    // full size (the upper part at upperCut, the lower at lowerCut, body units) and the gap is bridged, so each arm and
    // each leg is one mesh that bends smoothly across the joint.
    static readonly (string name, int upper, int lower, float upperCut, float lowerCut)[] Stitches =
    {
        ("LeftArmMesh", 8, 3, 0.603f, 0.567f), ("RightArmMesh", 1, 5, 0.603f, 0.567f),
        ("LeftLegMesh", 9, 15, 0.232f, 0.214f), ("RightLegMesh", 7, 14, 0.232f, 0.214f),
    };
    static bool IsStitched(int part) => Stitches.Any(st => st.upper == part || st.lower == part);

    // Soft joints: the bones whose pivot joins two parts that bend there together (body units of hand-over each side)
    const float JointBand = 0.035f;
    static readonly HashSet<string> SoftJoints = new HashSet<string>
    {
        "LeftLowerArm", "RightLowerArm", "LeftUpperLeg", "RightUpperLeg",
        "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot",
    };

    // The sleeves end straight across the upper arm this high (body units, on the arm's centre line); below it the upper
    // arm is skin-coloured like the forearm, hiding the uneven line where the two parts meet
    static readonly int[] UpperArmParts = { 8, 1 };
    const float SleeveHem = 0.605f;

    // The torso: shirt above the waistband (where the chest widens over the hips), jeans below, bending smoothly from
    // Hips to Spine between BendFrom and BendTo
    const float Waistband = 0.548f, BendFrom = 0.5f, BendTo = 0.6f;

    // Face and logo grids (body units): the face a little above the middle of the head, the logo small on the left of the
    // chest, over the heart (the user's wish; it was in the middle: x 0, y 0.665, size 0.11). The figure's left is -X.
    const float FaceCenterY = 0.877f, FaceSize = 0.197f, LogoCenterX = -0.075f, LogoCenterY = 0.69f, LogoSize = 0.07f, DecalLift = 0.0025f;
    const float HairInflate = 1.03f; // room at the corners of the boxy head for the stretched hair
    // The bacon hair of the user's R15 bacon from Roblox Studio (its OBJ, now in Archive/2026-10-09_obj_bacon): kept with
    // that model's head in HairSource (Roblox_Hair_Bacon, Roblox_Head; OBJ studs, Z flipped to face +Z) and stretched
    // from that head onto ours
    const string RobloxBacon = "RobloxBacon", HairSource = Folder + "/HairSource";
    const float RobloxHairInflate = 1.02f;
    const float HairCrease = 40f, HairSmoothness = 0.55f; // sharp corners past this angle; a little shine
    // The front strand over his left eye (StrandX of the head's width, in front of the face) hung down to StrandTip of
    // the head's height, over the eye; it is drawn up towards its root to end at StrandTipTo (the user: "shorten one
    // strand at the front")
    static readonly Vector2 StrandX = new Vector2(0.25f, 0.55f);
    const float StrandRoot = 1.15f, StrandTip = 0.65f, StrandTipTo = 0.845f;

    class Spec
    {
        public string name;
        public Color skin, shirt, pants, shoes;
        public Color? sleeves, forearms, shins; // default to shirt, skin and pants
        public Color? wrists;                   // the wrist rings (fur cuffs on a jacket), default skin
        public Color? collar;                   // a fluffy fur collar round the neck (a winter jacket), or none
        public bool logo;
        public string logoTexture;              // the chest logo, null for the play logo (LogoPath)
        public string hair;                     // a Tripo hair style (Tripo_Hair_<hair>), or null for none
        public Color hairColor;
        public RobloxFace.Emotion face;
        public bool liveFace;                   // a LiveFace (drawn, animated smoothly) instead of RobloxFace decals
        public bool bumps;                      // red bumps over the back of the head (the stadium short's guy)
        public string shirtPattern;             // a tiled texture on the shirt and sleeves (Patterns/<name>.png), or null

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
                case Region.Wrist: return wrists ?? skin;
                default: return shoes;
            }
        }
    }

    static readonly Spec[] Specs =
    {
        // The main character: black T-shirt with the play logo, black jeans, white trainers, blue Roblox bacon hair
        new Spec
        {
            name = "bacon", skin = Hex("EDEDED"), shirt = Hex("1E1E22"), pants = Hex("25272E"), shoes = Hex("F2F2F2"),
            logo = true, hair = RobloxBacon, hairColor = Hex("1F4FD6"), face = RobloxFace.Emotion.Smile, liveFace = true,
        },
        // The stadium short's guy in front of bacon: brown skin, dreads, a tan shirt, the back of his head covered in bumps
        new Spec
        {
            name = "dreads_guy", skin = Hex("8D5A3B"), shirt = Hex("C9A27A"), pants = Hex("1C1C1E"), shoes = Hex("2B2B2E"),
            forearms = Hex("8D5A3B"), hair = Dreads, hairColor = Hex("17120F"), face = RobloxFace.Emotion.Smile, liveFace = true, bumps = true,
            shirtPattern = "Monogram", // our own rings-and-diamonds monogram in the spirit of the clip's designer shirt (no brand logo)
        },
        // Bacon for winter (the frozen tongue short, the user: "put bacon in a jacket"): a brown jacket over his clothes,
        // long sleeves, a cream fur collar and fur cuffs, like the jacket in the clip; the logo on the jacket
        new Spec
        {
            name = "bacon_winter", skin = Hex("EDEDED"), shirt = Hex("5A3B2A"), pants = Hex("25272E"), shoes = Hex("F2F2F2"),
            forearms = Hex("5A3B2A"), wrists = Hex("E6D8BE"), collar = Hex("E6D8BE"),
            logo = true, hair = RobloxBacon, hairColor = Hex("1F4FD6"), face = RobloxFace.Emotion.Smile, liveFace = true,
        },
    };

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static Mesh bumpsMesh, collarMesh;
    const string Dreads = "Dreads";

    [MenuItem("Tools/Characters/Build Characters")]
    public static void BuildAll()
    {
        var faceMaterial = RobloxDecalAtlasBuilder.FaceMaterial();
        if (faceMaterial == null) return;
        foreach (var folder in new[] { PrefabFolder, MeshFolder, MaterialFolder }) RigUtility.EnsureFolder(folder);
        materials.Clear();

        var body = new Dictionary<int, Mesh>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(BodyPath))
            if (asset is Mesh mesh && int.TryParse(mesh.name.Replace("tripo_part_", ""), out int index))
                body[index] = RigUtility.Capped(mesh);
        var torsoMesh = SaveMesh(TorsoMesh(body[TorsoPart]), "Minifig_Torso");
        var faceMesh = SaveMesh(RigUtility.FaceGrid(FrontTriangles(body[0]), FaceCenterY, FaceSize, DecalLift), "Minifig_Face");
        var logoMesh = SaveMesh(RigUtility.FaceGrid(FrontTriangles(body[TorsoPart]), LogoCenterY, LogoSize, DecalLift, LogoCenterX), "Minifig_Logo");
        var partMeshes = new Dictionary<int, Mesh>();
        foreach (var (part, _, bone, _) in Parts)
            if (!IsStitched(part)) partMeshes[part] = SaveMesh(SkinnedPart(body[part], bone, System.Array.IndexOf(UpperArmParts, part) >= 0), "Minifig_Part" + part);
        var limbs = new Dictionary<string, (Mesh mesh, string[] bones, Region[] regions)>();
        foreach (var st in Stitches)
        {
            var (mesh, limbBones, regions) = Stitch(body, st);
            limbs[st.name] = (SaveMesh(mesh, "Minifig_" + st.name), limbBones, regions);
        }
        var hairMeshes = new Dictionary<string, Mesh>();
        foreach (var spec in Specs)
            if (spec.hair != null && !hairMeshes.ContainsKey(spec.hair))
                hairMeshes[spec.hair] = SaveMesh(spec.hair == RobloxBacon ? RobloxHair(body[0].bounds)
                    : spec.hair == Dreads ? DreadsHair(body[0].bounds)
                    : RigUtility.StretchTripoHair(spec.hair, body[0].bounds, HairInflate), "Minifig_Hair_" + spec.hair);
        bumpsMesh = SaveMesh(Bumps(body[0]), "Minifig_Bumps");
        collarMesh = SaveMesh(FurCollar(body[0].bounds), "Minifig_FurCollar");

        // Build in a preview scene so the open scene isn't touched
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var spec in Specs)
                BuildCharacter(spec, partMeshes, limbs, torsoMesh, faceMesh, logoMesh, hairMeshes, faceMaterial, stage);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Minifig] Built {Specs.Length} characters in {PrefabFolder}");
    }

    static void BuildCharacter(Spec s, Dictionary<int, Mesh> partMeshes, Dictionary<string, (Mesh mesh, string[] bones, Region[] regions)> limbs,
        Mesh torsoMesh, Mesh faceMesh, Mesh logoMesh,
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
        skin.sharedMaterials = new[] { ShirtMaterial(s), PartMaterial(s.pants) };

        // Each part is its own skinned mesh on the character origin, scaled to Height like the torso
        foreach (var (part, name, bone, region) in Parts)
        {
            if (IsStitched(part)) continue;
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

        // The stitched arms and legs, the same way
        foreach (var (name, (mesh, limbBones, regions)) in limbs)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * Height;
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = System.Array.ConvertAll(limbBones, b => bones[b]);
            renderer.rootBone = bones[limbBones[0]];
            renderer.sharedMaterials = System.Array.ConvertAll(regions, r => r == Region.Sleeve && s.sleeves == null ? ShirtMaterial(s) : PartMaterial(s.Of(r)));
        }

        // Rigid on the head and chest, modelled in body units like the parts
        var face = Part(bones["Head"], "Face", faceMesh, s.liveFace ? LiveFaceMaterial() : faceMaterial);
        face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        if (s.liveFace) face.AddComponent<LiveFace>();
        else face.AddComponent<RobloxFace>().emotion = (int)s.face;
        if (s.logo)
        {
            var logo = Part(bones["Spine"], "Logo", logoMesh, LogoMaterial(s.logoTexture ?? LogoPath));
            logo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        if (s.hair != null) Part(bones["Head"], "Hair", hairMeshes[s.hair], HairMaterial(s.hairColor, s.hair == Dreads ? 0.2f : HairSmoothness));
        if (s.collar != null)
        {
            var fur = PartMaterial(s.collar.Value);
            Part(bones["Spine"], "FurCollar", collarMesh, fur);
        }
        if (s.bumps)
        {
            var bumps = Part(bones["Head"], "Bumps", bumpsMesh, PartMaterial(Hex("9A3A33")));
            bumps.GetComponent<MeshRenderer>().sharedMaterials = new[] { PartMaterial(Hex("9A3A33")), PartMaterial(Hex("E3CF98")), PartMaterial(Hex("94493B")) };
        }

        // No humanoid avatar: clips key the bones directly. Unity's muscle space moved a hand turned about the forearm (a
        // wave, a shrug) into the forearm itself, which twisted at the elbow, and swung raised upper arms about themselves
        var animator = root.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{s.name}.prefab");
        Object.DestroyImmediate(root);
    }

    // Dreads: thick locks rooted over the crown, falling over the forehead in front, to the ears at the sides and only
    // halfway down the back, which is shaved (the bumps show there). The head is a cylinder, so the crown is a round,
    // high dome (a squarish one read as a box) and each lock a twisted round tube from a root on it, over its edge and down.
    static Mesh DreadsHair(Bounds head)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var random = new System.Random(5);
        float top = head.max.y, dome = 0.065f; // the crown rises this far over the flat top of the head: an oval, not a box
        // A point at angle a round the head, `scale` times its radius out
        Vector2 Round(float a, float scale) => new Vector2(Mathf.Sin(a) * head.extents.x, Mathf.Cos(a) * head.extents.z) * scale;
        // Height of the dome at radius r (0 middle, 1 the head's edge)
        float Dome(float r) => top + dome * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)) - 0.004f;
        // The scalp: a domed cap of dark hair over the top, so the head's top reads round
        {
            const int Around = 32, Up = 6;
            int start = vertices.Count;
            for (int u = 0; u <= Up; u++)
                for (int k = 0; k < Around; k++)
                {
                    float r = 1.04f * (1f - u / (float)Up);
                    var at = Round(k / (float)Around * Mathf.PI * 2f, r);
                    vertices.Add(new Vector3(head.center.x + at.x, Dome(Mathf.Min(r, 1f)) - (u == 0 ? 0.03f : 0f), head.center.z + at.y));
                }
            for (int u = 0; u < Up; u++)
                for (int k = 0; k < Around; k++)
                {
                    int a0 = start + u * Around + k, b0 = start + u * Around + (k + 1) % Around, c0 = a0 + Around, d0 = b0 + Around;
                    triangles.AddRange(new[] { a0, c0, b0, b0, c0, d0 });
                }
        }
        // A lock: a round tube along the path, swelling and narrowing along it like a dread, closed at both ends
        void Tube(List<Vector3> path, float r)
        {
            const int Sides = 8;
            int start = vertices.Count;
            for (int k = 0; k < path.Count; k++)
            {
                var along = (k + 1 < path.Count ? path[k + 1] - path[k] : path[k] - path[k - 1]).normalized;
                var side = Vector3.Cross(along, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(along, Vector3.up).normalized : Vector3.right;
                var up = Vector3.Cross(side, along).normalized;
                float w = r * (1f + 0.12f * Mathf.Sin(k * 2.2f)) * (k == path.Count - 1 ? 0.55f : 1f);
                for (int e = 0; e < Sides; e++)
                {
                    float th = -e * Mathf.PI * 2f / Sides;
                    vertices.Add(path[k] + (side * Mathf.Cos(th) + up * Mathf.Sin(th)) * w);
                }
            }
            for (int k = 0; k + 1 < path.Count; k++)
                for (int e = 0; e < Sides; e++)
                {
                    int a0 = start + k * Sides + e, b0 = start + k * Sides + (e + 1) % Sides, c0 = a0 + Sides, d0 = b0 + Sides;
                    triangles.AddRange(new[] { a0, c0, b0, b0, c0, d0 });
                }
            int end = start + (path.Count - 1) * Sides;
            for (int e = 1; e + 1 < Sides; e++) triangles.AddRange(new[] { end, end + e, end + e + 1, start, start + e + 1, start + e });
        }
        for (int ring = 0; ring < 3; ring++)
        {
            int count = ring == 0 ? 40 : ring == 1 ? 28 : 14;
            float rootR = ring == 0 ? 0.75f : ring == 1 ? 0.45f : 0.15f;
            for (int i = 0; i < count; i++)
            {
                float a = (i + 0.37f * ring) / count * Mathf.PI * 2f + (float)random.NextDouble() * 0.08f;
                float front = Mathf.Cos(a);
                // Down to: over the brow in front, to the ears at the sides, halfway down the back (shaved below)
                float end = front > 0f ? Mathf.Lerp(head.min.y + 0.11f, top - 0.07f, front * front) : Mathf.Lerp(head.min.y + 0.11f, head.center.y + 0.025f, front * front);
                end += (float)(random.NextDouble() - 0.5) * 0.02f;
                float phase = (float)random.NextDouble() * 6f;
                var tangent = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                var path = new List<Vector3>();
                // Over the dome from the root to the edge, then down the side, swaying a little and splaying at the tips
                for (int k = 0; k <= 5; k++)
                {
                    float r = Mathf.Lerp(rootR, 1.02f, k / 5f);
                    var at = Round(a, r * 1.03f);
                    path.Add(new Vector3(head.center.x + at.x, Dome(Mathf.Min(r, 1f)) + (k == 0 ? 0f : 0.006f), head.center.z + at.y));
                }
                for (int k = 1; k <= 7; k++)
                {
                    float f = k / 7f;
                    var edge = Round(a, 1.1f + 0.05f * f * f);
                    path.Add(new Vector3(head.center.x + edge.x, Mathf.Lerp(top - 0.012f, end, f), head.center.z + edge.y)
                        + tangent * (0.004f * Mathf.Sin(phase + k * 1.3f) * f));
                }
                Tube(path, 0.009f);
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Bumps over the shaved back of the head and down onto the neck, scattered over its surface (found by averaging
    // the head's back vertices round each spot): each a low red swelling sunk into the skin (submesh 0) on a flat
    // reddened blotch (submesh 2), the bigger ones sometimes with a small yellowish head (submesh 1). Raised beads
    // with white tips looked like toadstools.
    static Mesh Bumps(Mesh headMesh)
    {
        var head = headMesh.bounds;
        var hv = headMesh.vertices;
        var hn = headMesh.normals;
        var red = new List<int>();
        var pale = new List<int>();
        var sore = new List<int>();
        var vertices = new List<Vector3>();
        var random = new System.Random(11);
        void Dome(Vector3 c, Vector3 normal, float r, float height, List<int> into)
        {
            var rot = Quaternion.FromToRotation(Vector3.back, normal);
            int start = vertices.Count;
            const int Around = 12, Up = 4;
            for (int u = 0; u <= Up; u++)
            {
                float phi = u / (float)Up * Mathf.PI * 0.5f;
                for (int k = 0; k < Around; k++)
                {
                    float th = k / (float)Around * Mathf.PI * 2f;
                    vertices.Add(c + rot * new Vector3(Mathf.Cos(th) * Mathf.Cos(phi) * r, Mathf.Sin(th) * Mathf.Cos(phi) * r, -Mathf.Sin(phi) * r * height));
                }
            }
            for (int u = 0; u < Up; u++)
                for (int k = 0; k < Around; k++)
                {
                    int a0 = start + u * Around + k, b0 = start + u * Around + (k + 1) % Around, c0 = a0 + Around, d0 = b0 + Around;
                    into.AddRange(new[] { a0, c0, b0, b0, c0, d0 });
                }
        }
        int made = 0;
        for (int attempt = 0; attempt < 400 && made < 60; attempt++)
        {
            float x = ((float)random.NextDouble() * 2f - 1f) * head.extents.x * 0.82f;
            float y = Mathf.Lerp(head.min.y + 0.012f, head.center.y + 0.02f, Mathf.Pow((float)random.NextDouble(), 0.8f));
            // The surface there: back vertices near (x, y), weighted by nearness
            var at = Vector3.zero;
            var normal = Vector3.zero;
            float total = 0f;
            for (int v = 0; v < hv.Length; v++)
            {
                if (hv[v].z > head.center.z || hn[v].z > -0.2f) continue;
                float d = new Vector2(hv[v].x - x, hv[v].y - y).magnitude;
                if (d > 0.025f) continue;
                float w = 1f / (d + 0.004f);
                at += hv[v] * w;
                normal += hn[v] * w;
                total += w;
            }
            if (total <= 0f) continue;
            at /= total;
            normal = normal.normalized;
            var c = new Vector3(x, y, at.z);
            float r = 0.003f + 0.0065f * Mathf.Pow((float)random.NextDouble(), 1.8f);
            Dome(c + normal * 0.0002f, normal, r * 2.1f, 0.04f, sore);
            Dome(c - normal * (r * 0.12f), normal, r, 0.45f, red);
            if (r > 0.0058f && random.NextDouble() < 0.4) Dome(c + normal * (r * 0.3f), normal, r * 0.3f, 0.45f, pale);
            made++;
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(red, 0);
        mesh.SetTriangles(pale, 1);
        mesh.SetTriangles(sore, 2);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // A fluffy fur collar: a lumpy ring round the base of the head, resting on the shoulders, dipping a little at the
    // front onto the chest
    static Mesh FurCollar(Bounds head)
    {
        const int Around = 64, Tube = 12;
        float rx = head.extents.x + 0.02f, rz = head.extents.z + 0.016f, y0 = head.min.y + 0.012f;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i < Around; i++)
        {
            float th = i / (float)Around * Mathf.PI * 2f;
            var radial = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
            var c = new Vector3(head.center.x + rx * radial.x, y0 - 0.022f * Mathf.Pow(Mathf.Max(0f, radial.z), 2f), head.center.z + rz * radial.z);
            for (int j = 0; j < Tube; j++)
            {
                float ph = j / (float)Tube * Mathf.PI * 2f;
                // Tufts: the tube swells and shrinks round the ring and round itself
                float r = 0.027f * (1f + 0.14f * Mathf.Sin(th * 11f) + 0.1f * Mathf.Sin(ph * 3f + th * 7f));
                vertices.Add(c + (radial * Mathf.Cos(ph) + Vector3.up * Mathf.Sin(ph)) * r);
            }
        }
        for (int i = 0; i < Around; i++)
            for (int j = 0; j < Tube; j++)
            {
                int a = i * Tube + j, b = (i + 1) % Around * Tube + j, c = i * Tube + (j + 1) % Tube, d = (i + 1) % Around * Tube + (j + 1) % Tube;
                triangles.AddRange(new[] { a, b, c, c, b, d });
            }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // The Roblox bacon hair moved from the R15 head it was made for onto `head`, stretched by their sizes
    static Mesh RobloxHair(Bounds head)
    {
        var hair = AssetDatabase.LoadAssetAtPath<Mesh>($"{HairSource}/Roblox_Hair_Bacon.asset");
        var robloxHead = AssetDatabase.LoadAssetAtPath<Mesh>($"{HairSource}/Roblox_Head.asset").bounds;
        var stretch = new Vector3(head.extents.x / robloxHead.extents.x, head.extents.y / robloxHead.extents.y,
            head.extents.z / robloxHead.extents.z) * RobloxHairInflate;
        var vertices = hair.vertices;
        var normals = hair.normals;
        float squash = (StrandRoot - StrandTipTo) / (StrandRoot - StrandTip);
        for (int i = 0; i < vertices.Length; i++)
        {
            // The strand, in the R15 head's box (0..1 across each way), fading out at its sides and behind the face
            var u = new Vector3((vertices[i].x - robloxHead.min.x) / robloxHead.size.x, (vertices[i].y - robloxHead.min.y) / robloxHead.size.y,
                (vertices[i].z - robloxHead.min.z) / robloxHead.size.z);
            float w = u.y < StrandRoot
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StrandX.x - 0.04f, StrandX.x + 0.02f, u.x))
                  * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StrandX.y + 0.04f, StrandX.y - 0.02f, u.x))
                  * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 0.95f, u.z))
                : 0f;
            float k = Mathf.Lerp(1f, squash, w);
            vertices[i].y = robloxHead.min.y + (StrandRoot - (StrandRoot - u.y) * k) * robloxHead.size.y;
            var n = new Vector3(normals[i].x, normals[i].y / k, normals[i].z);
            vertices[i] = head.center + Vector3.Scale(vertices[i] - robloxHead.center, stretch);
            normals[i] = new Vector3(n.x / stretch.x, n.y / stretch.y, n.z / stretch.z).normalized;
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(hair.triangles, 0);
        mesh.RecalculateBounds();
        return Creased(mesh, HairCrease);
    }

    // Normals smoothed over the strands' gentle bends and kept sharp where faces meet at more than `crease` degrees: the
    // Roblox hair stays square-cornered (the user: "a bit square, with sharp corners, it's a Roblox character") but
    // loses its facets
    static Mesh Creased(Mesh source, float crease)
    {
        var v = source.vertices;
        var t = source.triangles;
        var faceNormals = new Vector3[t.Length / 3]; // by area
        for (int f = 0; f < faceNormals.Length; f++)
            faceNormals[f] = Vector3.Cross(v[t[3 * f + 1]] - v[t[3 * f]], v[t[3 * f + 2]] - v[t[3 * f]]);
        Vector3Int Point(Vector3 p) => Vector3Int.RoundToInt(p * 100000f);
        var corners = new Dictionary<Vector3Int, List<int>>();
        for (int k = 0; k < t.Length; k++)
        {
            if (!corners.TryGetValue(Point(v[t[k]]), out var list)) corners[Point(v[t[k]])] = list = new List<int>();
            list.Add(k);
        }
        float cos = Mathf.Cos(crease * Mathf.Deg2Rad);
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new int[t.Length];
        var made = new Dictionary<(Vector3Int, Vector3Int), int>();
        for (int k = 0; k < t.Length; k++)
        {
            var own = faceNormals[k / 3].normalized;
            var sum = Vector3.zero;
            foreach (int j in corners[Point(v[t[k]])])
                if (Vector3.Dot(faceNormals[j / 3].normalized, own) >= cos) sum += faceNormals[j / 3];
            var normal = sum.sqrMagnitude > 0f ? sum.normalized : own;
            var key = (Point(v[t[k]]), Vector3Int.RoundToInt(normal * 1000f));
            if (!made.TryGetValue(key, out int index))
            {
                made[key] = index = vertices.Count;
                vertices.Add(v[t[k]]);
                normals.Add(normal);
            }
            triangles[k] = index;
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
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

    // An arm or a leg in one mesh: its upper part above upperCut and its lower part below lowerCut, each skinned as
    // SkinnedPart does (so they hand over across the joint half and half at it), the gap between bridged. Bones: both
    // parts' together; one submesh per colour region.
    static (Mesh mesh, string[] bones, Region[] regions) Stitch(Dictionary<int, Mesh> body,
        (string name, int upper, int lower, float upperCut, float lowerCut) st)
    {
        var (_, _, upperBone, upperRegion) = Parts.First(p => p.part == st.upper);
        var (_, _, lowerBone, lowerRegion) = Parts.First(p => p.part == st.lower);
        bool sleeve = System.Array.IndexOf(UpperArmParts, st.upper) >= 0;
        var pieces = new[]
        {
            (mesh: SkinnedPart(Side(body[st.upper], st.upperCut, true), upperBone, sleeve), bone: upperBone,
                regions: sleeve ? new[] { Region.Sleeve, Region.Forearm } : new[] { upperRegion }),
            (mesh: SkinnedPart(Side(body[st.lower], st.lowerCut, false), lowerBone, false), bone: lowerBone, regions: new[] { lowerRegion }),
        };
        var limbBones = PartBones(upperBone).Union(PartBones(lowerBone)).ToArray();
        var regions = pieces.SelectMany(p => p.regions).Distinct().ToList();

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var weights = new List<BoneWeight>();
        var bySubmesh = regions.Select(_ => new List<int>()).ToList();
        var starts = new int[pieces.Length];
        for (int k = 0; k < pieces.Length; k++)
        {
            var (mesh, bone, partRegions) = pieces[k];
            starts[k] = vertices.Count;
            var own = PartBones(bone);
            int Remap(int index) => System.Array.IndexOf(limbBones, own[index]);
            vertices.AddRange(mesh.vertices);
            normals.AddRange(mesh.normals);
            foreach (var w in mesh.boneWeights)
                weights.Add(new BoneWeight
                {
                    boneIndex0 = Remap(w.boneIndex0), weight0 = w.weight0, boneIndex1 = w.weight1 > 0f ? Remap(w.boneIndex1) : 0, weight1 = w.weight1,
                    boneIndex2 = w.weight2 > 0f ? Remap(w.boneIndex2) : 0, weight2 = w.weight2, boneIndex3 = w.weight3 > 0f ? Remap(w.boneIndex3) : 0, weight3 = w.weight3,
                });
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                bySubmesh[regions.IndexOf(partRegions[sub])].AddRange(mesh.GetTriangles(sub).Select(i => i + starts[k]));
        }
        // The bridge takes the lower part's colour
        var all = bySubmesh.SelectMany(t => t).ToList();
        var top = Ring(vertices, all, st.upperCut, starts[0], starts[1]);
        var bottom = Ring(vertices, all, st.lowerCut, starts[1], vertices.Count);
        Bridge(vertices, top, bottom, bySubmesh[regions.IndexOf(lowerRegion)]);

        var limb = new Mesh();
        limb.SetVertices(vertices);
        limb.SetNormals(normals);
        limb.uv = PatternUV(vertices.ToArray());
        limb.subMeshCount = regions.Count;
        for (int sub = 0; sub < regions.Count; sub++) limb.SetTriangles(bySubmesh[sub], sub);
        limb.boneWeights = weights.ToArray();
        limb.bindposes = System.Array.ConvertAll(limbBones, BindPose);
        limb.RecalculateBounds();
        return (limb, limbBones, regions.ToArray());
    }

    // The part of a mesh above (or below) the plane at height y, cut straight along it
    static Mesh Side(Mesh source, float y, bool above)
    {
        var both = ClipInTwo(source, new Vector3(0f, y, 0f), Vector3.up);
        var mesh = new Mesh();
        mesh.SetVertices(both.vertices);
        mesh.SetNormals(both.normals);
        mesh.SetTriangles(both.GetTriangles(above ? 0 : 1), 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // The open edge a cut left at height y, among vertices from..to: one vertex per point, in order round its middle
    static List<int> Ring(List<Vector3> vertices, List<int> triangles, float y, int from, int to)
    {
        Vector3Int Point(int v) => Vector3Int.RoundToInt(vertices[v] * 100000f);
        var uses = new Dictionary<(Vector3Int, Vector3Int), int>();
        (Vector3Int, Vector3Int) Edge(int a, int b)
        {
            var pa = Point(a); var pb = Point(b);
            return pa.x < pb.x || pa.x == pb.x && (pa.y < pb.y || pa.y == pb.y && pa.z < pb.z) ? (pa, pb) : (pb, pa);
        }
        for (int k = 0; k < triangles.Count; k += 3)
            for (int e = 0; e < 3; e++)
            {
                var key = Edge(triangles[k + e], triangles[k + (e + 1) % 3]);
                uses[key] = uses.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        var ring = new Dictionary<Vector3Int, int>();
        for (int k = 0; k < triangles.Count; k += 3)
            for (int e = 0; e < 3; e++)
            {
                int a = triangles[k + e], b = triangles[k + (e + 1) % 3];
                if (a < from || a >= to || uses[Edge(a, b)] != 1) continue;
                if (Mathf.Abs(vertices[a].y - y) > 1e-4f || Mathf.Abs(vertices[b].y - y) > 1e-4f) continue;
                ring[Point(a)] = a;
                ring[Point(b)] = b;
            }
        var middle = Vector3.zero;
        foreach (int v in ring.Values) middle += vertices[v];
        middle /= ring.Count;
        return ring.Values.OrderBy(v => Mathf.Atan2(vertices[v].z - middle.z, vertices[v].x - middle.x)).ToList();
    }

    // Triangles zipping two rings together round their common middle, facing out
    static void Bridge(List<Vector3> vertices, List<int> top, List<int> bottom, List<int> triangles)
    {
        var middle = Vector3.zero;
        foreach (int v in top) middle += vertices[v];
        middle /= top.Count;
        float Angle(List<int> loop, int k) =>
            Mathf.Atan2(vertices[loop[k % loop.Count]].z - middle.z, vertices[loop[k % loop.Count]].x - middle.x) + 2f * Mathf.PI * (k / loop.Count);
        void Face(int a, int b, int c)
        {
            Vector3 pa = vertices[a], pb = vertices[b], pc = vertices[c];
            var centre = (pa + pb + pc) / 3f;
            var outward = new Vector3(centre.x - middle.x, 0f, centre.z - middle.z);
            if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), outward) < 0f) (b, c) = (c, b);
            triangles.AddRange(new[] { a, b, c });
        }
        int i = 0, j = 0;
        while (i < top.Count || j < bottom.Count)
        {
            bool alongTop = j >= bottom.Count || (i < top.Count && Angle(top, i + 1) <= Angle(bottom, j + 1));
            if (alongTop) { Face(top[i % top.Count], bottom[j % bottom.Count], top[(i + 1) % top.Count]); i++; }
            else { Face(top[i % top.Count], bottom[j % bottom.Count], bottom[(j + 1) % bottom.Count]); j++; }
        }
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
        mesh.uv = PatternUV(vertices);
        // The renderer sits on the character origin scaled to Height; the bones are unrotated and unscaled
        mesh.bindposes = new[] { BindPose("Hips"), BindPose("Spine") };
        mesh.RecalculateBounds();
        return mesh;
    }

    // UVs for a tiled pattern: projected flat from the front (x) with the depth (z) folded in, so the sides get it too
    const float PatternTile = 0.07f; // body units per repeat
    static Vector2[] PatternUV(Vector3[] vertices) => System.Array.ConvertAll(vertices, v => new Vector2((v.x + v.z) / PatternTile, v.y / PatternTile));

    static Matrix4x4 BindPose(string bone)
    {
        foreach (var (name, _, pos) in Joints)
            if (name == bone) return Matrix4x4.Translate(-pos * Height) * Matrix4x4.Scale(Vector3.one * Height);
        throw new System.ArgumentException(bone);
    }

    static Mesh SaveMesh(Mesh generated, string name) => RigUtility.SaveMesh(generated, $"{MeshFolder}/{name}.asset");

    // ---------------------------------------------------------------- materials

    // The LiveFace material (Roblox/LiveFace draws the face from LiveFace's numbers), its features FaceFeatureSize times
    // their drawn size (the user chose +18% out of +6/+12/+18%)
    const float FaceFeatureSize = 1.18f;
    // Dark brown lines and cream whites, like the funny-cartoon faces the user showed (black: 0D0D0F)
    static readonly Color FaceInk = Hex("33140F"), FaceSclera = Hex("FFF7E0");
    // The LiveFace material (Roblox/LiveFace draws the face from LiveFace's numbers)
    static Material LiveFaceMaterial()
    {
        const string path = "Assets/Characters/Materials/Face_Live.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Roblox/LiveFace"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetFloat("_Size", FaceFeatureSize);
        material.SetColor("_Ink", FaceInk);
        material.SetColor("_Sclera", FaceSclera);
        EditorUtility.SetDirty(material);
        return material;
    }

    // The shirt: its colour, or its pattern tiled over it
    static Material ShirtMaterial(Spec s)
    {
        if (s.shirtPattern == null) return PartMaterial(s.shirt);
        string path = $"{MaterialFolder}/Minifig_Shirt_{s.shirtPattern}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/Patterns/{s.shirtPattern}.png"));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.3f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // The hair's own material: its colour with a little shine (dreads less)
    static Material HairMaterial(Color color, float smoothness = HairSmoothness)
    {
        string path = $"{MaterialFolder}/Minifig_Hair_{ColorUtility.ToHtmlStringRGB(color)}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material PartMaterial(Color color)
    {
        string key = "Minifig_" + ColorUtility.ToHtmlStringRGB(color);
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
        string path = texturePath == LogoPath ? $"{MaterialFolder}/Minifig_Logo.mat"
            : $"{MaterialFolder}/Minifig_Logo_{System.IO.Path.GetFileNameWithoutExtension(texturePath)}.mat";
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
