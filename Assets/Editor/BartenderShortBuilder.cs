using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

// Short: "Bartender level 1000" (2026-10-10, my scenario the user picked): bacon behind a bar catches a bottle behind
// his back without looking, the noob at the counter picks the rainbow cocktail off the menu, bacon nods, flames an
// orange peel through a lighter (sparks), juggles bottles, pours a six-layer rainbow, sets it alight and slides it down
// the counter; the noob blows it out and stirs it into brown sludge; bacon's eye twitches, he tosses the bottle up out
// of frame, and it falls back into the first shot (a loop). The user (part 1, v1): slow and smooth, a relaxing watch;
// show the noob's choice on the menu; hold the bottle and the lighter properly; make the orange obvious. Built in
// parts; this is part 1 (0-13.6 s): the catch, the bottle put down, the menu, the nod, the peel, the flame.
// A warm bar: a glossy wooden counter, lit shelves of bottles (our own labels), a brick wall, pendant lamps, stools.
// Tools/Shorts/Build Bartender Short -> Assets/Scenes/Shorts_Bartender.unity + Timeline; render with ShortsRenderer.
public static class BartenderShortBuilder
{
    const float Fps = 30f, Duration = 13.6f;
    const string ScenePath = "Assets/Scenes/Shorts_Bartender.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_Bartender.playable";
    const string ClipFolder = "Assets/Animations";
    const string Folder = "Assets/Shorts/Bartender";
    const string Textures = Folder + "/Textures";
    const string BaconPrefab = MinifigCharacterBuilder.PrefabFolder + "/bacon_bartender.prefab";
    const string NoobPrefab = MinifigCharacterBuilder.PrefabFolder + "/noob.prefab";

    // The beats (seconds), slow: the bottle drifting down into his hand behind his back, shown off; past the noob's
    // shoulder, put down, the noob reaching for the menu; close on the menu, his hand on the rainbow cocktail; bacon reads
    // it and nods; close on the board, a peel taken from beside the cut orange; the lighter opened, the peel squeezed
    // through the flame, the sparks in slow motion, the lighter shut
    const float FallFrom = 0.2f, Catch = 1.6f, CutB = 3.6f, Release = 4.3f, CutC = 5.4f, Tap1 = 6.0f, Tap2 = 6.45f, CutD = 7.0f,
        CutE = 8.4f, Pick = 9.0f, CutF = 10.0f, Open = 10.7f, Squeeze = 11.7f, Close = 13.1f;

    // The counter runs along X, its top at CounterH; bacon stands behind it facing -Z, the noob sits on a stool in front
    const float CounterH = 1.05f;
    static readonly Vector3 BaconAt = new Vector3(0.05f, 0f, 0.62f), NoobAt = new Vector3(-0.5f, 0.04f, -0.78f);
    static readonly Vector3 CardAt = new Vector3(-0.42f, CounterH, -0.17f), BottleRest = new Vector3(-0.58f, CounterH, 0.2f);
    // The bar station in front of him: a board with half an orange and a dish of cut peels
    static readonly Vector3 BoardAt = new Vector3(0.04f, CounterH, 0.13f), DishAt = new Vector3(-0.07f, CounterH + 0.02f, 0.13f),
        OrangeAt = new Vector3(0.13f, CounterH + 0.02f, 0.12f);
    // A Lego hand's C is a tube along the hand's forward axis, 8.5 cm across and 23 cm long, its middle this far from the
    // wrist (at rest); things are held through it, poking out of its ends
    static readonly Vector3 GripLocal = new Vector3(0f, -0.107f, 0.03f);
    const float TubeHalf = 0.115f;
    const float BottleGrip = 0.24f, LighterGrip = -0.08f, PeelGrip = -0.15f; // where along each the middle of the C is
    const float LighterScale = 2.1f;

    static readonly string[] Bones =
    {
        "Hips", "Spine", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    };

    static Color Hex(string hex) => RigUtility.Hex(hex);

    [MenuItem("Tools/Shorts/Build Bartender Short")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Bartender] Exit Play Mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var f in new[] { ClipFolder, "Assets/Timelines", "Assets/Scenes", Folder, Textures }) RigUtility.EnsureFolder(f);
        var baconPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaconPrefab);
        var noobPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NoobPrefab);
        if (baconPrefab == null || noobPrefab == null) { Debug.LogError("[Bartender] Build the characters first (Tools/Characters/Build Characters)"); return; }
        MakeTextures();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildRoom();
        BuildLight();

        var bacon = (GameObject)PrefabUtility.InstantiatePrefab(baconPrefab, scene);
        bacon.name = "Bacon";
        bacon.transform.SetPositionAndRotation(BaconAt, Quaternion.Euler(0f, 180f, 0f));
        var noob = (GameObject)PrefabUtility.InstantiatePrefab(noobPrefab, scene);
        noob.name = "Noob";
        noob.transform.SetPositionAndRotation(NoobAt, Quaternion.identity);

        var baconActor = new Actor(bacon);
        var noobActor = new Actor(noob);
        Solve(baconActor, noobActor);
        var stage = new GameObject("Stage");
        stage.AddComponent<Animator>();
        var s = new Set(stage.transform);
        var takes = Bake(s, baconActor, noobActor);

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }
        foreach (var track in timeline.GetOutputTracks().ToList()) timeline.DeleteTrack(track);
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.editorSettings.frameRate = Fps;
        timeline.fixedDuration = Duration;
        var director = new GameObject("Timeline_Bartender").AddComponent<PlayableDirector>();
        void Track(string name, AnimationClip clip, Animator target)
        {
            var track = timeline.CreateTrack<AnimationTrack>(null, name);
            track.trackOffset = TrackOffset.ApplySceneOffsets; // the clips key children, not the bound root
            var c = track.CreateClip(clip);
            c.start = 0;
            c.duration = Duration;
            director.SetGenericBinding(track, target);
        }
        Track("Bacon", takes.bacon, bacon.GetComponent<Animator>());
        Track("Noob", takes.noob, noob.GetComponent<Animator>());
        Track("Stage", takes.stage, stage.GetComponent<Animator>());
        // The sparks burst at the squeeze, under the Timeline's control so scrubbing shows them where they will be
        var sparkTrack = timeline.CreateTrack<ControlTrack>(null, "Sparks");
        var sparkClip = sparkTrack.CreateDefaultClip();
        sparkClip.start = Squeeze;
        sparkClip.duration = Duration - Squeeze;
        var control = (ControlPlayableAsset)sparkClip.asset;
        control.sourceGameObject.exposedName = GUID.Generate().ToString();
        director.SetReferenceValue(control.sourceGameObject.exposedName, s.sparks.gameObject);
        control.updateParticle = true;
        control.particleRandomSeed = 11;
        control.active = false;
        control.updateDirector = false;
        control.updateITimeControl = false;

        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;
        EditorUtility.SetDirty(timeline);
        director.time = 0;
        director.Evaluate();
        BakeReflections();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Bartender] Built {ScenePath}, {Duration} s");
    }

    // ---------------------------------------------------------------- the bar

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    // A material by name: URP Lit (or `shader`), its colour, shine, metal, an optional tiled texture (metres per tile),
    // a glow, or see-through glass (premultiplied, so its highlights stay bright)
    static Material Mat(string name, Color color, float smoothness = 0.3f, float metallic = 0f, string tex = null, float tile = 1f,
        Color? emission = null, bool glass = false, string shader = "Universal Render Pipeline/Lit")
    {
        string path = $"{Folder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader.name != shader) m.shader = Shader.Find(shader);
        m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (tex != null)
        {
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{tex}.png"));
            m.SetTextureScale("_BaseMap", Vector2.one / tile);
        }
        if (emission != null)
        {
            m.SetColor("_EmissionColor", emission.Value);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        if (glass)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 1f); // premultiply
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        materials[name] = m;
        return m;
    }

    static GameObject Solid(Transform parent, string name, Mesh mesh, Material material, Vector3 at, Quaternion? rotation = null, bool shadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool shadows = true) =>
        Solid(parent, name, BoxMesh(size), material, center, null, shadows);

    static void BuildRoom()
    {
        var room = new GameObject("Bar").transform;
        var counterWood = Mat("CounterTop", Color.white, 0.82f, 0f, "WoodTop", 1.2f);
        var panelWood = Mat("CounterFront", Color.white, 0.45f, 0f, "WoodPanel", 1.0f);
        var darkWood = Mat("BackBar", Hex("8A7A70"), 0.4f, 0f, "WoodPanel", 1.0f);
        var brick = Mat("Brick", Hex("6E5A52"), 0.15f, 0f, "Brick", 1.3f);
        var brass = Mat("Brass", Hex("D9A75A"), 0.82f, 1f);
        var floor = Mat("Floor", Hex("6A5448"), 0.35f, 0f, "WoodTop", 1.6f);

        // The counter: a thick glossy top overhanging a panelled front with a brass foot rail
        Box(room, "CounterTop", new Vector3(0f, CounterH - 0.035f, -0.02f), new Vector3(5f, 0.07f, 0.72f), counterWood);
        Box(room, "CounterFront", new Vector3(0f, (CounterH - 0.07f) / 2f, -0.28f), new Vector3(4.9f, CounterH - 0.07f, 0.06f), panelWood);
        Box(room, "CounterInside", new Vector3(0f, 0.45f, 0.1f), new Vector3(4.9f, 0.9f, 0.4f), darkWood);
        Solid(room, "FootRail", CylinderMesh(0.024f, 4.8f, 16), brass, new Vector3(0f, 0.22f, -0.42f), Quaternion.Euler(0f, 0f, 90f));
        for (float x = -2.2f; x <= 2.2f; x += 1.1f)
            Solid(room, "RailPost", CylinderMesh(0.012f, 0.13f, 10), brass, new Vector3(x, 0.15f, -0.36f), Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 0f));

        // Behind him: a low back counter, a brick wall, three lit shelves of bottles
        Box(room, "BackCounter", new Vector3(0f, 0.47f, 1.32f), new Vector3(5f, 0.94f, 0.46f), darkWood);
        Box(room, "BackCounterTop", new Vector3(0f, 0.955f, 1.3f), new Vector3(5f, 0.03f, 0.5f), counterWood);
        Box(room, "Wall", new Vector3(0f, 1.6f, 1.6f), new Vector3(7f, 3.2f, 0.1f), brick);
        Box(room, "Floor", new Vector3(0f, -0.01f, 0f), new Vector3(8f, 0.02f, 6f), floor);
        var led = Mat("LedStrip", Hex("FFD9A0"), 0.2f, 0f, null, 1f, new Color(4f, 2.3f, 0.9f));
        var shelfWood = Mat("Shelf", Hex("B09080"), 0.5f, 0f, "WoodTop", 1.0f);
        var random = new System.Random(4);
        var shelves = new[] { 1.28f, 1.66f, 2.04f };
        foreach (float y in shelves)
        {
            Box(room, "Shelf", new Vector3(0f, y - 0.015f, 1.44f), new Vector3(3.8f, 0.03f, 0.28f), shelfWood);
            Box(room, "Led", new Vector3(0f, y - 0.036f, 1.31f), new Vector3(3.7f, 0.01f, 0.012f), led, false);
            for (float x = -1.8f; x < 1.8f;)
            {
                float w = PlaceBottle(room, new Vector3(x, y, 1.45f + (float)random.NextDouble() * 0.06f), random);
                x += w + 0.02f + (float)random.NextDouble() * 0.05f;
            }
        }
        // A few on the back counter, a shaker, upturned glasses
        for (float x = -1.6f; x < 1.7f; x += 0.42f + (float)random.NextDouble() * 0.3f)
            if (Mathf.Abs(x - 0.05f) > 0.35f) PlaceBottle(room, new Vector3(x, 0.97f, 1.22f), random);
        var steel = Mat("Steel", Hex("C9CED6"), 0.85f, 1f);
        Solid(room, "Shaker", Lathe(new[] { V(0f, 0f), V(0.04f, 0f), V(0.046f, 0.12f), V(0.05f, 0.16f), V(0.04f, 0.2f), V(0.012f, 0.24f), V(0f, 0.245f) }, 28), steel,
            new Vector3(0.42f, 0.97f, 1.18f));
        var glassMat = Mat("Glass", new Color(0.05f, 0.06f, 0.07f, 0.12f), 0.96f, 0f, null, 1f, null, true);
        foreach (float x in new[] { 0.62f, 0.72f, 0.82f })
            Solid(room, "Glass", Lathe(new[] { V(0f, 0f), V(0.04f, 0f), V(0.036f, 0.09f), V(0f, 0.09f) }, 24), glassMat, new Vector3(x, 0.97f, 1.2f), null, false);
        // Pendant lamps over the counter
        var shade = Mat("LampShade", Hex("1F2124"), 0.6f, 0.6f);
        var bulb = Mat("Bulb", Hex("FFE2B0"), 0.2f, 0f, null, 1f, new Color(6f, 4f, 2f));
        foreach (float x in new[] { -0.8f, 0.9f })
        {
            Solid(room, "Cord", CylinderMesh(0.004f, 1.2f, 6), shade, new Vector3(x, 2.75f, 0.0f));
            Solid(room, "Shade", Lathe(new[] { V(0f, 0.2f), V(0.03f, 0.2f), V(0.05f, 0.16f), V(0.13f, 0.02f), V(0.14f, 0f), V(0.13f, 0.005f), V(0f, 0.12f) }, 32), shade,
                new Vector3(x, 1.98f, 0f));
            Solid(room, "Bulb", SphereMesh(0.035f, 16), bulb, new Vector3(x, 2.03f, 0f), null, false);
            var spot = new GameObject("Pendant Light").AddComponent<Light>();
            spot.transform.SetParent(room, false);
            spot.transform.SetPositionAndRotation(new Vector3(x, 2.0f, 0f), Quaternion.LookRotation(Vector3.down));
            spot.type = LightType.Spot;
            spot.spotAngle = 80f;
            spot.innerSpotAngle = 40f;
            spot.range = 4f;
            spot.intensity = 2.2f;
            spot.color = new Color(1f, 0.8f, 0.58f);
            spot.shadows = LightShadows.None;
        }
        // Stools: a padded seat on a chrome post
        var leather = Mat("Leather", Hex("6B1E1E"), 0.55f);
        foreach (float x in new[] { NoobAt.x, 0.55f })
        {
            Solid(room, "StoolSeat", Lathe(new[] { V(0f, 0.07f), V(0.17f, 0.07f), V(0.2f, 0.05f), V(0.2f, 0.01f), V(0.17f, 0f), V(0f, 0f) }, 28), leather,
                new Vector3(x, 0.7f, -0.78f));
            Solid(room, "StoolPost", CylinderMesh(0.025f, 0.7f, 12), steel, new Vector3(x, 0.35f, -0.78f));
            Solid(room, "StoolBase", Lathe(new[] { V(0f, 0.02f), V(0.2f, 0.01f), V(0.22f, 0f), V(0f, 0f) }, 28), steel, new Vector3(x, 0f, -0.78f));
            Solid(room, "StoolRing", TorusMesh(0.16f, 0.01f, 28, 8), steel, new Vector3(x, 0.3f, -0.78f));
        }
        // The menu on the counter in front of the noob: a tent card, the rainbow cocktail on both sides, big enough to read
        var card = Mat("MenuCard", Color.white, 0.2f, 0f, "Menu");
        var tent = new GameObject("MenuCard").transform;
        tent.SetParent(room, false);
        tent.localPosition = CardAt;
        foreach (float side in new[] { -1f, 1f })
            Solid(tent, "Side", QuadMesh(0.22f, 0.3f), card, new Vector3(0f, 0.146f, side * 0.036f),
                Quaternion.Euler(-side * 14f, 0f, 0f) * Quaternion.Euler(0f, side < 0f ? 180f : 0f, 0f)); // each side faces out, the tops leaning together

        // The bar station: a board, half an orange cut face up, a dish of peels (the one he takes is a prop of its own)
        Box(room, "Board", BoardAt + new Vector3(0f, 0.01f, 0f), new Vector3(0.36f, 0.02f, 0.22f), Mat("Board", Hex("D8B48A"), 0.35f, 0f, "WoodTop", 0.6f));
        var skin = Mat("OrangeSkin", Color.white, 0.55f, 0f, "PeelSkin");
        var half = new GameObject("HalfOrange").transform;
        half.SetParent(room, false);
        half.localPosition = OrangeAt + Vector3.up * 0.058f;
        Solid(half, "Rind", Lathe(Enumerable.Range(0, 9).Select(i => { float a = (-90f + i * 11.25f) * Mathf.Deg2Rad; return V(Mathf.Cos(a) * 0.06f, Mathf.Sin(a) * 0.06f); }).ToArray(), 32), skin, Vector3.zero);
        var cut = Mat("OrangeCut", Color.white, 0.6f, 0f, "OrangeCut");
        cut.SetFloat("_AlphaClip", 1f);
        cut.SetFloat("_Cutoff", 0.5f);
        cut.EnableKeyword("_ALPHATEST_ON");
        Solid(half, "Cut", QuadMesh(0.122f, 0.122f), cut, new Vector3(0f, 0.0005f, 0f), Quaternion.Euler(-90f, 0f, 0f));
        Solid(room, "Dish", Lathe(new[] { V(0f, 0f), V(0.07f, 0f), V(0.09f, 0.018f), V(0.095f, 0.024f), V(0.08f, 0.012f), V(0f, 0.008f) }, 32),
            Mat("Porcelain", Hex("F4F1EA"), 0.8f), DishAt - Vector3.up * 0.02f);
        var peelMat = new[] { skin, Mat("Pith", Hex("FFF6E2"), 0.3f) };
        foreach (var (at, yaw) in new[] { (new Vector3(-0.035f, 0.002f, -0.03f), 30f), (new Vector3(0.03f, 0.004f, 0.028f), -50f) })
        {
            var lying = Solid(room, "Peel", PeelMesh(), skin, DishAt + at, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-90f, 0f, 0f));
            lying.GetComponent<MeshRenderer>().sharedMaterials = peelMat;
        }
    }

    static Vector2 V(float r, float y) => new Vector2(r, y);

    // A bottle of one of a few shapes, coloured glass with a drink inside and its own label; returns its width
    static float PlaceBottle(Transform parent, Vector3 at, System.Random random)
    {
        string[] glassColors = { "2F6B3A", "8A4B12", "C8D8E0", "1E4A8A", "5A2A1A", "3C3C3C" };
        string[] drinks = { "B9771E", "E8E2C8", "7A1F22", "D8A02A", "3E8A3A", "F2F2F2" };
        int kind = random.Next(4), color = random.Next(glassColors.Length), label = random.Next(4);
        float r, body, shoulder, neck, neckR = 0.013f;
        switch (kind)
        {
            case 0: r = 0.038f; body = 0.2f; shoulder = 0.05f; neck = 0.07f; break;           // whisky
            case 1: r = 0.05f; body = 0.12f; shoulder = 0.03f; neck = 0.04f; neckR = 0.016f; break; // liqueur, squat
            case 2: r = 0.033f; body = 0.24f; shoulder = 0.04f; neck = 0.08f; break;          // vodka, tall
            default: r = 0.042f; body = 0.17f; shoulder = 0.06f; neck = 0.05f; break;         // gin
        }
        var bottle = new GameObject("Bottle").transform;
        bottle.SetParent(parent, false);
        bottle.localPosition = at;
        float top = body + shoulder + neck;
        var profile = new[] { V(0f, 0f), V(r * 0.92f, 0f), V(r, 0.012f), V(r, body), V(neckR * 1.15f, body + shoulder), V(neckR, top - 0.012f), V(neckR * 1.25f, top - 0.01f), V(neckR * 1.25f, top), V(0f, top) };
        var glass = Mat("BottleGlass" + color, Hex(glassColors[color]).WithAlpha(0.42f), 0.95f, 0f, null, 1f, null, true);
        Solid(bottle, "Glass", Lathe(profile, 24), glass, Vector3.zero, null, false);
        float fill = body * (0.55f + 0.4f * (float)random.NextDouble());
        var drink = Mat("Drink" + color, Hex(drinks[color]), 0.7f);
        Solid(bottle, "Drink", Lathe(new[] { V(0f, 0.004f), V(r * 0.9f, 0.004f), V(r * 0.9f, fill), V(0f, fill) }, 20), drink, Vector3.zero);
        var labelMat = Mat("Label" + label, Color.white, 0.25f, 0f, "Label" + label);
        float lh = Mathf.Min(0.08f, body * 0.45f);
        Solid(bottle, "Label", Band(r + 0.0015f, lh, 24, 0.75f), labelMat, new Vector3(0f, body * 0.45f, 0f), Quaternion.Euler(0f, 180f + (float)random.NextDouble() * 20f - 10f, 0f));
        var cap = Mat(kind == 1 ? "CapGold" : "CapBlack", kind == 1 ? Hex("C9A04A") : Hex("1A1A1A"), 0.6f, kind == 1 ? 1f : 0f);
        Solid(bottle, "Cap", CylinderMesh(neckR * 1.35f, 0.025f, 14), cap, new Vector3(0f, top + 0.0125f, 0f));
        return r * 2f;
    }

    static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

    // Warm and low: pendant spots on the counter, a key light on bacon from the front, the shelves glowing behind him (a
    // rim of amber light on his back), a dim warm ambient; graded post (ACES, bloom on the lights, warm, a vignette)
    static void BuildLight()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.12f, 0.1f);
        RenderSettings.fog = false;
        Light Add(string name, LightType type, Vector3 at, Vector3 target, Color color, float intensity, float range, bool shadows, float angle = 60f)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = type;
            l.transform.SetPositionAndRotation(at, Quaternion.LookRotation(target - at));
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.5f;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }
        Add("Key", LightType.Spot, new Vector3(0.45f, 2.45f, -1.2f), new Vector3(0.05f, 1.4f, 0.6f), new Color(1f, 0.86f, 0.7f), 6f, 6f, true, 50f);
        Add("Rim", LightType.Spot, new Vector3(-0.9f, 2.2f, 1.35f), new Vector3(0.05f, 1.45f, 0.55f), new Color(1f, 0.62f, 0.3f), 4f, 4f, false, 50f);
        Add("Fill", LightType.Point, new Vector3(0.2f, 1.4f, -1.8f), Vector3.zero, new Color(0.7f, 0.78f, 1f), 0.5f, 5f, false);
        // The customer's light: from over the bar onto the noob's face
        Add("Customer", LightType.Spot, new Vector3(-0.35f, 2.35f, 0.7f), new Vector3(-0.5f, 1.4f, -0.78f), new Color(1f, 0.84f, 0.66f), 5f, 4f, true, 45f);
        foreach (float y in new[] { 1.24f, 1.62f, 2.0f })
            Add("Shelf Light", LightType.Point, new Vector3(0f, y, 1.3f), Vector3.zero, new Color(1f, 0.66f, 0.32f), 1.2f, 2.2f, false);

        string path = $"{Folder}/Bartender_Volume.asset";
        var profile = Profile(path);
        T Comp<T>() where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }
        Comp<Tonemapping>().mode.value = TonemappingMode.ACES;
        var bloom = Comp<Bloom>();
        bloom.threshold.value = 1.0f;
        bloom.intensity.value = 0.7f;
        bloom.scatter.value = 0.7f;
        var grade = Comp<ColorAdjustments>();
        grade.postExposure.value = 0.45f;
        grade.contrast.value = 16f;
        grade.saturation.value = 8f;
        Comp<WhiteBalance>().temperature.value = 10f;
        var vignette = Comp<Vignette>();
        vignette.intensity.value = 0.3f;
        vignette.smoothness.value = 0.5f;
        EditorUtility.SetDirty(profile);
        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }

    static VolumeProfile Profile(string path)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        foreach (var component in profile.components.ToList())
        {
            profile.components.Remove(component);
            Object.DestroyImmediate(component, true);
        }
        return profile;
    }

    // Depth of field for one shot, focused on what it shows
    static GameObject ShotFocus(Transform parent, string name, float distance, float aperture)
    {
        var profile = Profile($"{Folder}/Bartender_{name}.asset");
        var dof = profile.Add<DepthOfField>(true);
        dof.name = "DepthOfField";
        AssetDatabase.AddObjectToAsset(dof, profile);
        dof.mode.value = DepthOfFieldMode.Bokeh;
        dof.focusDistance.value = distance;
        dof.aperture.value = aperture;
        dof.focalLength.value = 50f;
        EditorUtility.SetDirty(profile);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var v = go.AddComponent<Volume>();
        v.isGlobal = true;
        v.priority = 10f;
        v.sharedProfile = profile;
        return go;
    }

    // The counter, the bottles and the glass reflect the bar around them
    static void BakeReflections()
    {
        var probe = new GameObject("Reflection Probe").AddComponent<ReflectionProbe>();
        probe.transform.position = new Vector3(0f, 1.3f, 0.2f);
        probe.size = new Vector3(6f, 3.2f, 3.6f);
        probe.boxProjection = true;
        probe.mode = ReflectionProbeMode.Custom;
        probe.resolution = 256;
        string path = $"{Folder}/BarReflection.exr";
        if (Lightmapping.BakeReflectionProbe(probe, path))
        {
            AssetDatabase.ImportAsset(path);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
        }
        else Debug.LogWarning("[Bartender] The reflection probe did not bake");
    }

    // ---------------------------------------------------------------- the props, the camera, the shots

    class Set
    {
        public readonly Transform root, cameraRig, bottle, lighter, lid, flame, peel;
        public readonly Camera camera;
        public readonly Light glow;
        public readonly ParticleSystem sparks;
        public readonly GameObject[] shots;

        public Set(Transform root)
        {
            this.root = root;
            cameraRig = new GameObject("CameraRig").transform;
            cameraRig.SetParent(root, false);
            camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera.transform.SetParent(cameraRig, false);
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("140E0B");
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.stopNaN = true;
            // Each shot focused on what it shows: bacon, bacon past the noob, the menu, bacon, the board, the flame
            var subjects = new (float t, Vector3 subject, float aperture)[]
            {
                (1.5f, BaconAt + new Vector3(0f, 1.35f, -0.1f), 2.8f), (4.5f, BaconAt + new Vector3(0f, 1.35f, -0.1f), 2.0f),
                (6.2f, new Vector3(-0.47f, 1.35f, -0.55f), 2.8f), (7.7f, BaconAt + new Vector3(0f, 1.5f, -0.1f), 2.8f),
                (9.2f, DishAt, 4f), (11.5f, handsAt + Vector3.up * 0.1f, 4f),
            };
            shots = subjects.Select((x, i) => ShotFocus(root, "Shot_" + (char)('A' + i), Vector3.Distance(Shot(x.t).at, x.subject), x.aperture)).ToArray();

            // The flair bottle: a green liqueur bottle, its body as wide as his hand's C
            bottle = new GameObject("FlairBottle").transform;
            bottle.SetParent(root, false);
            var profile = new[] { V(0f, 0f), V(0.04f, 0f), V(0.043f, 0.012f), V(0.043f, 0.17f), V(0.016f, 0.22f), V(0.014f, 0.28f), V(0.018f, 0.285f), V(0.018f, 0.295f), V(0f, 0.295f) };
            Solid(bottle, "Glass", Lathe(profile, 28), Mat("FlairGlass", Hex("2E7A46").WithAlpha(0.45f), 0.96f, 0f, null, 1f, null, true), Vector3.zero, null, false);
            Solid(bottle, "Drink", Lathe(new[] { V(0f, 0.004f), V(0.038f, 0.004f), V(0.038f, 0.15f), V(0f, 0.15f) }, 20), Mat("FlairDrink", Hex("4FBF4A"), 0.7f), Vector3.zero);
            Solid(bottle, "Label", Band(0.0445f, 0.07f, 28, 0.7f), Mat("Label0", Color.white, 0.25f, 0f, "Label0"), new Vector3(0f, 0.08f, 0f), Quaternion.Euler(0f, 180f, 0f));
            Solid(bottle, "Cap", CylinderMesh(0.021f, 0.03f, 14), Mat("CapGold", Hex("C9A04A"), 0.6f, 1f), new Vector3(0f, 0.31f, 0f));

            // The lighter: a chrome case, its lid on a hinge at the back, a flame over the chimney; cartoon-sized for his
            // big hands
            var chrome = Mat("Chrome", Hex("D8DCE2"), 0.9f, 1f);
            lighter = new GameObject("Lighter").transform;
            lighter.SetParent(root, false);
            lighter.localScale = Vector3.one * LighterScale;
            Box(lighter, "Case", new Vector3(0f, 0.022f, 0f), new Vector3(0.036f, 0.044f, 0.013f), chrome);
            Box(lighter, "Chimney", new Vector3(0f, 0.05f, 0f), new Vector3(0.02f, 0.012f, 0.01f), Mat("Chimney", Hex("8C9097"), 0.5f, 1f));
            lid = new GameObject("Lid").transform;
            lid.SetParent(lighter, false);
            lid.localPosition = new Vector3(0f, 0.044f, -0.0065f);
            Box(lid, "LidCase", new Vector3(0f, 0.011f, 0.0065f), new Vector3(0.036f, 0.022f, 0.013f), chrome);
            // The flame glows: added to what is behind it, never darker
            flame = Solid(lighter, "Flame", Lathe(new[] { V(0f, 0f), V(0.006f, 0.004f), V(0.0075f, 0.012f), V(0.005f, 0.024f), V(0.002f, 0.034f), V(0f, 0.04f) }, 16),
                Glow("Flame", new Color(4f, 1.7f, 0.4f, 1f)), new Vector3(0f, 0.056f, 0f), null, false).transform;
            Solid(flame, "Core", Lathe(new[] { V(0f, 0f), V(0.003f, 0.003f), V(0.0035f, 0.008f), V(0f, 0.014f) }, 12),
                Glow("FlameCore", new Color(0.6f, 1.2f, 4f, 1f)), new Vector3(0f, -0.001f, 0f), null, false);

            // The peel he takes from the dish: orange skin out, white pith in
            peel = Solid(root, "TakenPeel", PeelMesh(), Mat("OrangeSkin", Color.white, 0.55f, 0f, "PeelSkin"), Vector3.zero).transform;
            peel.GetComponent<MeshRenderer>().sharedMaterials = new[] { Mat("OrangeSkin", Color.white, 0.55f, 0f, "PeelSkin"), Mat("Pith", Hex("FFF6E2"), 0.3f) };

            glow = new GameObject("Flame Light").AddComponent<Light>();
            glow.transform.SetParent(root, false);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.58f, 0.22f);
            glow.range = 2.5f;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;

            sparks = BuildSparks(root);
        }
    }

    // Light added on top of the scene (a flame, a spark): URP's unlit particle shader, additive
    static Material Glow(string name, Color color, Texture2D texture = null)
    {
        var m = Mat(name, color, 0f, 0f, null, 1f, null, false, "Universal Render Pipeline/Particles/Unlit");
        if (texture != null) m.SetTexture("_BaseMap", texture);
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.One);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    // The peel's flaming oils: a burst of sparks shooting out through the flame and a short orange fireball
    static ParticleSystem BuildSparks(Transform parent)
    {
        var dot = Mat("SparkDot", Color.white, 0f, 0f, null, 1f, null, false, "Universal Render Pipeline/Particles/Unlit");
        dot.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/SoftDot.png"));
        dot.SetColor("_BaseColor", new Color(3f, 3f, 3f, 1f)); // brighter than white, so the bloom catches it
        dot.SetFloat("_Surface", 1f);
        dot.SetFloat("_Blend", 2f); // additive
        dot.SetOverrideTag("RenderType", "Transparent");
        dot.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        dot.SetInt("_DstBlend", (int)BlendMode.One);
        dot.SetInt("_ZWrite", 0);
        dot.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        dot.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(dot);

        ParticleSystem System(string name, Transform under)
        {
            var go = new GameObject(name);
            go.transform.SetParent(under, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            var main = ps.main;
            main.loop = false;
            main.duration = 1f;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = dot;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.minParticleSize = 0f;
            return ps;
        }
        Gradient Fade(params (float t, Color c, float a)[] keys)
        {
            var g = new Gradient();
            g.SetKeys(keys.Select(k => new GradientColorKey(k.c, k.t)).ToArray(), keys.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
            return g;
        }

        var sparks = System("Sparks", parent);
        sparks.randomSeed = 11;
        var m = sparks.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.65f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 3.2f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
        m.startColor = new Color(1f, 0.85f, 0.5f);
        m.gravityModifier = 0.35f;
        m.simulationSpeed = 0.4f; // slow motion
        m.maxParticles = 400;
        sparks.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 260), new ParticleSystem.Burst(0.05f, 90) });
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.006f;
        var col = sparks.colorOverLifetime;
        col.enabled = true;
        col.color = Fade((0f, new Color(1f, 0.95f, 0.75f), 1f), (0.3f, new Color(1f, 0.6f, 0.15f), 1f), (0.75f, new Color(0.9f, 0.25f, 0.05f), 0.8f), (1f, new Color(0.5f, 0.1f, 0.02f), 0f));
        var drag = sparks.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 1.2f;
        var sr = sparks.GetComponent<ParticleSystemRenderer>();
        sr.renderMode = ParticleSystemRenderMode.Stretch;
        sr.velocityScale = 0.03f;
        sr.lengthScale = 1.5f;

        var fireball = System("Fireball", sparks.transform);
        fireball.randomSeed = 12;
        var fm = fireball.main;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
        fm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
        fm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        fm.startColor = new Color(1f, 0.55f, 0.15f);
        fm.simulationSpeed = 0.4f;
        fireball.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
        var fshape = fireball.shape;
        fshape.shapeType = ParticleSystemShapeType.Cone;
        fshape.angle = 25f;
        fshape.radius = 0.01f;
        var fsize = fireball.sizeOverLifetime;
        fsize.enabled = true;
        fsize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
        var fcol = fireball.colorOverLifetime;
        fcol.enabled = true;
        fcol.color = Fade((0f, new Color(1f, 0.9f, 0.6f), 0.9f), (0.4f, new Color(1f, 0.45f, 0.1f), 0.7f), (1f, new Color(0.6f, 0.15f, 0.05f), 0f));
        return sparks;
    }

    // ---------------------------------------------------------------- the actors

    class Pose
    {
        public readonly Dictionary<string, Vector3> e = new Dictionary<string, Vector3>();
        public readonly Dictionary<string, Quaternion> q = new Dictionary<string, Quaternion>(); // bones set by a rotation (the hands)
        public Vector3 hips;
        public Pose Rot(string bone, Vector3 r)
        {
            e[bone] = (e.TryGetValue(bone, out var v) ? v : Vector3.zero) + r;
            return this;
        }
        public Pose Rot(string bone, float x, float y, float z) => Rot(bone, new Vector3(x, y, z));
        public Pose Turn(string bone, Quaternion r) { q[bone] = r; return this; }
    }

    // A character in the scene: its bones (unrotated at rest) and its LiveFace
    class Actor
    {
        public readonly GameObject go;
        public readonly Dictionary<string, Transform> bones;
        public readonly Vector3 hipsRest;
        public readonly LiveFace face;
        public readonly Dictionary<string, float> faceDefaults;

        public Actor(GameObject go)
        {
            this.go = go;
            var all = go.GetComponentsInChildren<Transform>(true);
            bones = Bones.ToDictionary(b => b, b => all.First(t => t.name == b));
            hipsRest = bones["Hips"].localPosition;
            face = go.GetComponentInChildren<LiveFace>(true);
            faceDefaults = typeof(LiveFace).GetFields().Where(f => f.FieldType == typeof(float)).ToDictionary(f => f.Name, f => (float)f.GetValue(face));
        }

        public void Apply(Pose p)
        {
            foreach (var b in Bones) bones[b].localRotation = p.q.TryGetValue(b, out var r) ? r : Quaternion.Euler(p.e.TryGetValue(b, out var v) ? v : Vector3.zero);
            bones["Hips"].localPosition = hipsRest + p.hips;
        }

        // The middle of a hand's C, and the way its tube runs (what is held there points along it)
        public Vector3 Grip(string side) => bones[side + "Hand"].TransformPoint(GripLocal);
        public Vector3 Axis(string side) => bones[side + "Hand"].rotation * Vector3.forward;
        public Vector3 Forward => go.transform.forward;
    }

    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    static float Bump(float from, float to, float t) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(Mathf.InverseLerp(from, to, t)));

    // Key values over time, eased between keys (a key repeated later holds; keys a moment apart make a cut)
    static Vector3 Keys(float t, params (float at, Vector3 value)[] keys)
    {
        if (t <= keys[0].at) return keys[0].value;
        for (int i = 1; i < keys.Length; i++)
            if (t <= keys[i].at) return Vector3.Lerp(keys[i - 1].value, keys[i].value, Smooth(keys[i - 1].at, keys[i].at, t));
        return keys[keys.Length - 1].value;
    }
    static Quaternion QKeys(float t, params (float at, Quaternion value)[] keys)
    {
        if (t <= keys[0].at) return keys[0].value;
        for (int i = 1; i < keys.Length; i++)
            if (t <= keys[i].at) return Quaternion.Slerp(keys[i - 1].value, keys[i].value, Smooth(keys[i - 1].at, keys[i].at, t));
        return keys[keys.Length - 1].value;
    }
    static (float, Vector3) K(float at, float x, float y = 0f, float z = 0f) => (at, new Vector3(x, y, z));

    // An arm: the upper arm's angles (x < 0 raises it forward), the elbow (x < 0 bends it), the hand's turn at the wrist
    struct Arm
    {
        public Vector3 upper;
        public float lower;
        public Quaternion hand;
        public Arm(Vector3 u, float l) { upper = u; lower = l; hand = Quaternion.identity; }
    }
    static readonly Arm Rest = new Arm(new Vector3(-15f, 0f, 0f), -40f);
    static Arm rCatch, rShow, rPut, rPick, rPeel, lLighter, nRest, nRestL, nMenu;

    static void ArmKeys(Pose p, string side, float t, params (float at, Arm arm)[] keys)
    {
        p.Rot(side + "UpperArm", Keys(t, keys.Select(k => (k.at, k.arm.upper)).ToArray()));
        p.Rot(side + "LowerArm", Keys(t, keys.Select(k => (k.at, new Vector3(k.arm.lower, 0f, 0f))).ToArray()));
        p.Turn(side + "Hand", QKeys(t, keys.Select(k => (k.at, k.arm.hand)).ToArray()));
    }

    // Bacon, slow and smooth: the catch, the bottle shown and put down, reading the menu and a slow nod, a peel taken
    // from the dish, the lighter in his left hand and the peel in his right, a push of the peel at the squeeze
    static Pose BaconPose(float t)
    {
        var p = new Pose();
        const float Cut = 0.001f;
        ArmKeys(p, "Right", t, (0f, Rest), (0.6f, Rest), (1.45f, rCatch), (1.75f, rCatch), (2.8f, rShow), (3.75f, rShow), (4.25f, rPut), (4.45f, rPut),
            (5.2f, Rest), (8.55f, Rest), (Pick - 0.05f, rPick), (Pick + 0.2f, rPick), (CutF - 0.1f, rPeel), (Duration, rPeel));
        ArmKeys(p, "Left", t, (0f, Rest), (CutF - Cut, Rest), (CutF, lLighter), (Duration, lLighter));
        p.Rot("RightLowerArm", -5f * Bump(Squeeze - 0.15f, Squeeze + 0.25f, t), 0f, 0f);
        p.Rot("LeftLowerArm", 6f * Bump(Close - 0.06f, Close + 0.12f, t), 0f, 0f);
        p.Rot("Spine", Keys(t, K(0f, 0f), K(1.5f, 0f), K(2.8f, -4f), K(3.7f, -4f), K(4.25f, 8f), K(4.7f, 4f), K(8.6f, 4f), K(Pick, 12f), K(Pick + 0.3f, 12f),
            K(CutF - 0.1f, 6f)));
        p.Rot("Head", Keys(t, K(0f, 0f), K(1.5f, 0f), K(2.8f, -4f, 0f, 6f), K(3.7f, -4f, 0f, 6f), K(4.25f, 12f), K(4.7f, 6f), K(CutD, 6f), K(CutD + 0.4f, 16f),
            K(8.3f, 4f), K(Pick - 0.2f, 18f), K(Pick + 0.4f, 16f), K(CutF, 12f), K(Squeeze + 0.5f, 10f), K(Squeeze + 0.9f, -3f, 0f, -5f)));
        p.Rot("Head", 6f * Mathf.Sin((t - (CutD + 0.6f)) * 9f) * Bump(CutD + 0.6f, CutD + 1.3f, t), 0f, 0f); // a slow nod
        return p;
    }

    // The noob on his stool, forearms on the counter; he reaches for the menu, taps the rainbow cocktail twice, sits back
    static Pose NoobPose(float t)
    {
        var p = new Pose();
        foreach (var s in new[] { "Left", "Right" }) p.Rot(s + "UpperLeg", -85f, 0f, 0f).Rot(s + "LowerLeg", 85f, 0f, 0f);
        ArmKeys(p, "Left", t, (0f, nRestL));
        ArmKeys(p, "Right", t, (0f, nRest), (4.4f, nRest), (5.5f, nMenu), (6.9f, nMenu), (7.8f, nRest));
        p.Rot("RightUpperArm", 7f * (Bump(Tap1 - 0.1f, Tap1 + 0.15f, t) + Bump(Tap2 - 0.1f, Tap2 + 0.15f, t)), 0f, 0f); // tap, tap
        p.Rot("Spine", Keys(t, K(0f, 6f), K(4.4f, 6f), K(5.3f, 10f), K(6.9f, 10f), K(7.8f, 6f)));
        p.Rot("Head", Keys(t, K(0f, -10f), K(4.6f, -10f), K(5.3f, 14f), K(6.9f, 14f), K(7.6f, -10f)));
        return p;
    }

    static void PoseArm(Actor actor, string side, Func<Pose> body, Arm arm)
    {
        var p = body();
        p.Rot(side + "UpperArm", arm.upper).Rot(side + "LowerArm", arm.lower, 0f, 0f).Turn(side + "Hand", arm.hand);
        actor.Apply(p);
    }

    // An arm reaching a point with the hand's grip (or its wrist): the upper arm's three angles and the elbow found by
    // trying small turns of each, keeping whatever brings it closer, with finer and finer steps
    static Arm Reach(Actor actor, string side, Vector3 target, Func<Pose> body, Arm guess, bool wrist = false)
    {
        float Error(Arm a)
        {
            PoseArm(actor, side, body, a);
            return ((wrist ? actor.bones[side + "Hand"].position : actor.Grip(side)) - target).magnitude;
        }
        var best = guess;
        float error = Error(best);
        for (float step = 16f; step >= 0.25f; step /= 2f)
            for (int round = 0; round < 60; round++)
            {
                bool better = false;
                for (int k = 0; k < 4; k++)
                    foreach (float d in new[] { step, -step })
                    {
                        var a = best;
                        if (k < 3) { var u = a.upper; u[k] += d; a.upper = u; } else a.lower = Mathf.Clamp(a.lower + d, -150f, 5f);
                        float e = Error(a);
                        if (e < error - 1e-5f) { error = e; best = a; better = true; }
                    }
                if (!better) break;
            }
        if (!wrist && error > 0.02f) Debug.LogWarning($"[Bartender] {actor.go.name}'s {side} hand is {error:F3} m from where it should be");
        return best;
    }

    // Holding something along `axis` with the middle of the C at `grip`: the hand turned so its tube runs along the axis
    // (its fingers carrying on from the forearm), and the arm reaching for the wrist that puts the C there
    static Arm Hold(Actor actor, string side, Vector3 grip, Vector3 axis, Func<Pose> body, Arm guess)
    {
        axis.Normalize();
        var arm = guess;
        for (int pass = 0; pass < 3; pass++)
        {
            PoseArm(actor, side, body, arm);
            var lower = actor.bones[side + "LowerArm"];
            var d = Vector3.ProjectOnPlane(actor.bones[side + "Hand"].position - lower.position, axis);
            if (d.sqrMagnitude < 1e-6f) d = Vector3.ProjectOnPlane(Vector3.down, axis);
            var turn = Quaternion.LookRotation(axis, -d.normalized);
            arm.hand = Quaternion.identity;
            arm = Reach(actor, side, grip - turn * GripLocal, body, arm, wrist: true);
            PoseArm(actor, side, body, arm);
            arm.hand = Quaternion.Inverse(lower.rotation) * turn;
        }
        PoseArm(actor, side, body, arm);
        float miss = (actor.Grip(side) - grip).magnitude, twist = Vector3.Angle(actor.Axis(side), axis);
        if (miss > 0.02f || twist > 3f) Debug.LogWarning($"[Bartender] {actor.go.name}'s {side} hand holds {miss:F3} m and {twist:F0}° off");
        return arm;
    }

    // Where the hands go and how they hold things. Points in each one's own frame (+Z forward, +X his right) unless world
    static void Solve(Actor bacon, Actor noob)
    {
        Vector3 B(float x, float y, float z) => bacon.go.transform.TransformPoint(new Vector3(x, y, z));
        Vector3 BDir(float x, float y, float z) => bacon.go.transform.TransformDirection(new Vector3(x, y, z)).normalized;
        Func<Pose> Lean(float spine, float head = 0f) => () => new Pose().Rot("Spine", spine, 0f, 0f).Rot("Head", head, 0f, 0f);
        var up = Vector3.up;
        // The bottle hangs from his hand by the neck: the C round its neck, upright
        rCatch = Hold(bacon, "Right", B(0.42f, 1.05f, -0.08f), up, Lean(0f), new Arm(new Vector3(52f, 0f, 14f), -25f));
        rShow = Hold(bacon, "Right", B(0.24f, 1.42f, 0.22f), up, Lean(-4f), new Arm(new Vector3(-40f, -15f, 10f), -88f));
        rPut = Hold(bacon, "Right", BottleRest + up * BottleGrip, up, Lean(8f), new Arm(new Vector3(-50f, -6f, 6f), -38f));
        // The peel lies in the dish along X; he takes it by its end, then holds it up leaning in towards the flame
        rPick = Hold(bacon, "Right", PeelRest + Vector3.right * PeelGrip, Vector3.right, Lean(12f, 18f), new Arm(new Vector3(-55f, -30f, 0f), -40f));
        rPeel = Hold(bacon, "Right", B(0.15f, 1.08f, 0.32f), BDir(-0.9f, 1f, 0.25f), Lean(6f), new Arm(new Vector3(-38f, -40f, -5f), -85f));
        lLighter = Hold(bacon, "Left", B(-0.06f, 1.04f, 0.38f), up, Lean(6f), new Arm(new Vector3(-30f, 50f, 5f), -80f));
        Func<Pose> Seated(float spine) => () =>
        {
            var p = new Pose().Rot("Spine", spine, 0f, 0f);
            foreach (var s in new[] { "Left", "Right" }) p.Rot(s + "UpperLeg", -85f, 0f, 0f).Rot(s + "LowerLeg", 85f, 0f, 0f);
            return p;
        };
        nRest = Reach(noob, "Right", new Vector3(NoobAt.x + 0.12f, CounterH + 0.1f, -0.34f), Seated(6f), new Arm(new Vector3(-52f, -10f, 0f), -38f));
        nRestL = Reach(noob, "Left", new Vector3(NoobAt.x - 0.12f, CounterH + 0.1f, -0.34f), Seated(6f), new Arm(new Vector3(-52f, 10f, 0f), -38f));
        nMenu = Reach(noob, "Right", CardAt + new Vector3(0.02f, 0.27f, -0.14f), Seated(10f), new Arm(new Vector3(-62f, -14f, 0f), -10f));
        bacon.Apply(BaconPose(11.5f));
        handsAt = (bacon.Grip("Left") + bacon.Grip("Right")) / 2f + Vector3.up * 0.1f;
    }

    static readonly Vector3 PeelRest = DishAt + new Vector3(0.005f, 0.008f, 0.005f); // the peel he takes, lying in the dish

    // Faces: each beat's face blends in over a fifth of a second, over the character's defaults
    static Dictionary<string, float> F(params (string field, float value)[] set) => set.ToDictionary(s => s.field, s => s.value);

    static readonly (float from, Dictionary<string, float> face)[] BaconFaces =
    {
        // Smug: he catches it without looking
        (0f, F(("lids", 0.45f), ("smirk", 0.75f), ("smile", 0.5f), ("browAsym", 0.6f), ("browRaise", 0.1f), ("brow", 0.9f))),
        // Putting it down
        (CutB + 0.4f, F(("lids", 0.35f), ("smirk", 0.4f), ("smile", 0.4f), ("lookY", -0.3f), ("brow", 0.8f))),
        // Reading the menu
        (CutD, F(("lids", 0.3f), ("smirk", 0.3f), ("smile", 0.35f), ("lookY", -0.5f), ("brow", 0.8f))),
        // Good choice
        (CutD + 0.7f, F(("lids", 0.5f), ("browAngle", 0.3f), ("smirk", 0.85f), ("smile", 0.55f), ("brow", 1f), ("lookY", -0.1f))),
        // Focused on his hands
        (CutE, F(("lids", 0.6f), ("browAngle", 0.25f), ("smile", 0.2f), ("smirk", 0.4f), ("lookY", -0.6f), ("brow", 0.9f))),
        // Proud in the glow
        (Squeeze + 0.5f, F(("lids", 0.42f), ("browAsym", 0.7f), ("browRaise", 0.2f), ("smirk", 0.9f), ("smile", 0.6f), ("brow", 1f))),
    };

    static readonly (float from, Dictionary<string, float> face)[] NoobFaces =
    {
        // The classic noob face: black oval eyes, a plain smile
        (0f, F(("eyeWhite", 0f), ("lids", 0f), ("brow", 0f), ("smile", 0.8f), ("smirk", 0f), ("lookY", 0.2f))),
        // Excited about the rainbow
        (CutC - 0.2f, F(("eyeWhite", 0f), ("lids", 0f), ("brow", 0f), ("smile", 1f), ("smirk", 0f), ("mouthOpen", 0.35f), ("mouthD", 0.6f), ("lookY", -0.4f))),
        (CutD + 0.6f, F(("eyeWhite", 0f), ("lids", 0f), ("brow", 0f), ("smile", 0.9f), ("smirk", 0f), ("mouthOpen", 0.15f), ("lookY", 0.3f))),
    };

    static Dictionary<string, float> FaceAt(float t, Actor actor, (float from, Dictionary<string, float> face)[] beats, params float[] blinks)
    {
        var face = new Dictionary<string, float>(actor.faceDefaults);
        foreach (var (from, set) in beats)
        {
            float w = from <= 0f ? 1f : Smooth(from - 0.15f, from + 0.2f, t);
            if (w <= 0f) continue;
            foreach (var key in face.Keys.ToList())
                face[key] = Mathf.Lerp(face[key], set.TryGetValue(key, out var v) ? v : actor.faceDefaults[key], w);
        }
        float blink = 0f;
        foreach (float at in blinks) { float d = t - at; blink += d < 0f || d > 0.2f ? 0f : d < 0.07f ? d / 0.07f : 1f - (d - 0.07f) / 0.13f; }
        face["eyeOpen"] *= 1f - Mathf.Clamp01(blink);
        return face;
    }

    // ---------------------------------------------------------------- the camera

    static Vector3 handsAt; // where his hands are with the lighter and the peel

    // Long shots and slow moves
    static (Vector3 at, Vector3 look, float fov) Shot(float t)
    {
        if (t < CutB) // A: from his left, clear of the noob; wide enough for the bottle drifting down; a slow push in
            return (Vector3.Lerp(new Vector3(0.8f, 1.5f, -2.25f), new Vector3(0.66f, 1.47f, -1.85f), Smooth(0f, CutB, t)), new Vector3(-0.08f, 1.32f, 0.55f), 42f);
        if (t < CutC) // B: in front of him as he puts the bottle down
            return (Vector3.Lerp(new Vector3(0.35f, 1.5f, -1.6f), new Vector3(0.28f, 1.46f, -1.4f), Smooth(CutB, CutC, t)), new Vector3(-0.2f, 1.25f, 0.35f), 40f);
        if (t < CutD) // C: over his right shoulder at the noob, beaming, his hand on the menu's rainbow cocktail
            return (Vector3.Lerp(new Vector3(-0.33f, 1.86f, 1.1f), new Vector3(-0.35f, 1.82f, 1.0f), Smooth(CutC, CutD, t)), new Vector3(-0.45f, 1.3f, -0.5f), 32f);
        if (t < CutE) // D: bacon reads it and nods
            return (Vector3.Lerp(new Vector3(0.28f, 1.55f, -1.2f), new Vector3(0.24f, 1.54f, -1.05f), Smooth(CutD, CutE, t)), new Vector3(0f, 1.45f, 0.55f), 36f);
        if (t < CutF) // E: close on the board, the cut orange, a peel taken from the dish
            return (Vector3.Lerp(new Vector3(0.25f, 1.5f, -0.48f), new Vector3(0.2f, 1.44f, -0.38f), Smooth(CutE, CutF, t)), new Vector3(0.02f, CounterH + 0.06f, 0.13f), 38f);
        // F: the flame and the peel at his hands, his face above, slowly closer
        return (handsAt + Vector3.Lerp(new Vector3(0.55f, 0.16f, -0.95f), new Vector3(0.46f, 0.14f, -0.8f), Smooth(CutF, Duration, t)), handsAt + new Vector3(0f, 0.12f, 0f), 36f);
    }

    // ---------------------------------------------------------------- baking

    // Every frame of one animated root: transforms (position, rotation, scale), objects shown or hidden, and float
    // properties (a face's fields, a light's intensity), turned into one clip
    class Take
    {
        readonly Transform root;
        readonly List<Transform> transforms = new List<Transform>();
        readonly Dictionary<Transform, List<(Vector3 p, Quaternion r, Vector3 s)>> poses = new Dictionary<Transform, List<(Vector3, Quaternion, Vector3)>>();
        readonly List<(GameObject go, Func<bool> on, List<float> values)> actives = new List<(GameObject, Func<bool>, List<float>)>();
        readonly List<(string path, Type type, string property, Func<float> value, List<float> values)> floats = new List<(string, Type, string, Func<float>, List<float>)>();

        public Take(Transform root) { this.root = root; }
        public void Transform(Transform t) { transforms.Add(t); poses[t] = new List<(Vector3, Quaternion, Vector3)>(); }
        public void Active(GameObject go, Func<bool> on) => actives.Add((go, on, new List<float>()));
        public void Float(Component c, string property, Func<float> value) =>
            floats.Add((AnimationUtility.CalculateTransformPath(c.transform, root), c.GetType(), property, value, new List<float>()));

        public void Sample()
        {
            foreach (var t in transforms)
            {
                var list = poses[t];
                var q = t.localRotation;
                if (list.Count > 0 && Quaternion.Dot(q, list[list.Count - 1].r) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                list.Add((t.localPosition, q, t.localScale));
            }
            foreach (var a in actives) { bool on = a.on(); a.go.SetActive(on); a.values.Add(on ? 1f : 0f); }
            foreach (var f in floats) f.values.Add(f.value());
        }

        public AnimationClip Save(string name)
        {
            string path = $"{ClipFolder}/{name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = Fps;
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            bool Varies(IList<float> v) => v.Any(x => Mathf.Abs(x - v[0]) > 1e-6f);
            void Add(string p, Type type, string property, IList<float> values, bool stepped = false)
            {
                var keys = new Keyframe[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    int a = Mathf.Max(i - 1, 0), b = Mathf.Min(i + 1, values.Count - 1);
                    float slope = b > a ? (values[b] - values[a]) * Fps / (b - a) : 0f;
                    keys[i] = stepped ? new Keyframe(i / Fps, values[i], float.PositiveInfinity, float.PositiveInfinity) : new Keyframe(i / Fps, values[i], slope, slope);
                }
                bindings.Add(EditorCurveBinding.FloatCurve(p, type, property));
                curves.Add(new AnimationCurve(keys));
            }
            // A vector or quaternion keyed in part plays with its other parts at 0: key whole ones
            foreach (var t in transforms)
            {
                string p = AnimationUtility.CalculateTransformPath(t, root);
                var list = poses[t];
                var px = list.Select(v => v.p.x).ToList(); var py = list.Select(v => v.p.y).ToList(); var pz = list.Select(v => v.p.z).ToList();
                if (Varies(px) || Varies(py) || Varies(pz))
                {
                    Add(p, typeof(Transform), "m_LocalPosition.x", px); Add(p, typeof(Transform), "m_LocalPosition.y", py); Add(p, typeof(Transform), "m_LocalPosition.z", pz);
                }
                var rx = list.Select(v => v.r.x).ToList(); var ry = list.Select(v => v.r.y).ToList(); var rz = list.Select(v => v.r.z).ToList(); var rw = list.Select(v => v.r.w).ToList();
                if (Varies(rx) || Varies(ry) || Varies(rz) || Varies(rw))
                {
                    Add(p, typeof(Transform), "m_LocalRotation.x", rx); Add(p, typeof(Transform), "m_LocalRotation.y", ry);
                    Add(p, typeof(Transform), "m_LocalRotation.z", rz); Add(p, typeof(Transform), "m_LocalRotation.w", rw);
                }
                var sx = list.Select(v => v.s.x).ToList(); var sy = list.Select(v => v.s.y).ToList(); var sz = list.Select(v => v.s.z).ToList();
                if (Varies(sx) || Varies(sy) || Varies(sz))
                {
                    Add(p, typeof(Transform), "m_LocalScale.x", sx); Add(p, typeof(Transform), "m_LocalScale.y", sy); Add(p, typeof(Transform), "m_LocalScale.z", sz);
                }
            }
            foreach (var a in actives)
                if (Varies(a.values)) Add(AnimationUtility.CalculateTransformPath(a.go.transform, root), typeof(GameObject), "m_IsActive", a.values, stepped: true);
            foreach (var f in floats)
                if (Varies(f.values)) Add(f.path, f.type, f.property, f.values);
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            EditorUtility.SetDirty(clip);

            // The scene as the first frame
            foreach (var t in transforms)
            {
                var first = poses[t][0];
                t.localPosition = first.p;
                t.localRotation = first.r;
                t.localScale = first.s;
            }
            foreach (var a in actives) a.go.SetActive(a.values[0] > 0.5f);
            return clip;
        }
    }

    // Something upright along `axis`, its front (+Z) turned as near `face` as it can
    static Quaternion Along(Vector3 axis, Vector3 face)
    {
        var f = Vector3.ProjectOnPlane(face, axis);
        if (f.sqrMagnitude < 1e-6f) f = Vector3.ProjectOnPlane(Vector3.forward, axis);
        return Quaternion.LookRotation(f, axis);
    }

    static (AnimationClip bacon, AnimationClip noob, AnimationClip stage) Bake(Set s, Actor bacon, Actor noob)
    {
        int frames = Mathf.RoundToInt(Duration * Fps);
        // Where things are at the beats: the catch, the peel picked up
        bacon.Apply(BaconPose(Catch));
        var catchGrip = bacon.Grip("Right");
        var catchAxis = bacon.Axis("Right");
        Debug.Log($"[Bartender] catch at {catchGrip:F2}; flame and peel at {handsAt:F2}");

        var baconTake = new Take(bacon.go.transform);
        var noobTake = new Take(noob.go.transform);
        var stageTake = new Take(s.root);
        foreach (var b in Bones) { baconTake.Transform(bacon.bones[b]); noobTake.Transform(noob.bones[b]); }
        Dictionary<string, float> baconFace = null, noobFace = null;
        foreach (var field in bacon.faceDefaults.Keys) baconTake.Float(bacon.face, field, () => baconFace[field]);
        foreach (var field in noob.faceDefaults.Keys) noobTake.Float(noob.face, field, () => noobFace[field]);
        foreach (var t in new[] { s.cameraRig, s.bottle, s.lighter, s.lid, s.flame, s.peel, s.glow.transform }) stageTake.Transform(t);
        float time = 0f;
        var cuts = new[] { CutB, CutC, CutD, CutE, CutF };
        for (int i = 0; i < s.shots.Length; i++)
        {
            int shot = i;
            stageTake.Active(s.shots[i], () => cuts.Count(c => time >= c) == shot);
        }
        stageTake.Active(s.lighter.gameObject, () => time >= CutF - 0.001f);
        stageTake.Active(s.flame.gameObject, () => time >= Open + 0.03f && time < Close);
        stageTake.Float(s.glow, "m_Intensity", () => s.glow.intensity);
        stageTake.Float(s.camera, "field of view", () => s.camera.fieldOfView);

        for (int f = 0; f <= frames; f++)
        {
            float t = time = f / Fps;
            bacon.Apply(BaconPose(t));
            noob.Apply(NoobPose(t));
            baconFace = FaceAt(t, bacon, BaconFaces, 2.3f, 5.0f, 8.1f, 10.4f, 12.9f);
            noobFace = FaceAt(t, noob, NoobFaces, 1.2f, 4.0f, 7.3f);

            // The bottle: drifting down slowly, tumbling once, into his hand behind his back; hanging from his hand by
            // the neck, label out; put down on the counter
            if (t < Catch)
            {
                float fall = Catch - Mathf.Max(t, FallFrom);
                s.bottle.SetPositionAndRotation(catchGrip - catchAxis * BottleGrip + Vector3.up * 0.8f * fall * fall,
                    Quaternion.AngleAxis(-fall * 260f, Vector3.right) * Along(catchAxis, Vector3.forward));
            }
            else if (t < Release)
            {
                var axis = bacon.Axis("Right");
                s.bottle.SetPositionAndRotation(bacon.Grip("Right") - axis * BottleGrip, Along(axis, Vector3.forward));
            }
            else s.bottle.SetPositionAndRotation(BottleRest, Along(Vector3.up, Vector3.forward));

            // The lighter in his left hand by its bottom, its front to us; the lid flicked open and snapped shut; the
            // flame flickering, flaring at the squeeze
            var la = bacon.Axis("Left");
            s.lighter.SetPositionAndRotation(bacon.Grip("Left") - la * LighterGrip, Along(la, bacon.Forward));
            float open = Smooth(Open - 0.1f, Open, t) * (1f - Smooth(Close, Close + 0.08f, t));
            s.lid.localRotation = Quaternion.Euler(-115f * open, 0f, 0f);
            float flare = t >= Squeeze ? Mathf.Exp(-(t - Squeeze) / 0.3f) : 0f;
            float flicker = 1f + 0.12f * Mathf.Sin(t * 31f) + 0.08f * Mathf.Sin(t * 19f + 1f);
            s.flame.localScale = new Vector3(1f + 0.6f * flare, flicker * (1f + 1.8f * flare), 1f + 0.6f * flare) * 1.2f;
            bool burning = t >= Open + 0.03f && t < Close;
            var flameAt = s.flame.position + Vector3.up * 0.03f;
            s.glow.transform.position = flameAt;
            s.glow.intensity = burning ? 0.8f * flicker + 12f * flare : 0f;

            // The peel: lying in the dish until he takes it by its end, then held up with its skin to the flame; squeezed
            var ra = bacon.Axis("Right");
            var peelAt = bacon.Grip("Right") - ra * PeelGrip;
            if (t < Pick) s.peel.SetPositionAndRotation(PeelRest, Quaternion.LookRotation(Vector3.up, Vector3.right));
            else
            {
                var skin = t < CutF ? Vector3.Slerp(Vector3.up, (bacon.Forward + Vector3.up).normalized, Smooth(Pick + 0.2f, CutF - 0.1f, t)) : flameAt - peelAt;
                s.peel.SetPositionAndRotation(peelAt, Along(ra, skin));
            }
            float squeeze = Bump(Squeeze - 0.08f, Squeeze + 0.3f, t);
            s.peel.localScale = new Vector3(1f - 0.3f * squeeze, 1f, 1f + 0.25f * squeeze);
            if (f == Mathf.RoundToInt(Squeeze * Fps))
                s.sparks.transform.SetPositionAndRotation(peelAt, Quaternion.LookRotation((flameAt - peelAt).normalized * 0.6f + bacon.Forward));

            var (camAt, camLook, fov) = Shot(t);
            s.cameraRig.SetPositionAndRotation(camAt, Quaternion.LookRotation(camLook - camAt));
            s.camera.fieldOfView = fov;

            baconTake.Sample();
            noobTake.Sample();
            stageTake.Sample();
        }
        return (baconTake.Save("Bartender_Bacon"), noobTake.Save("Bartender_Noob"), stageTake.Save("Bartender_Stage"));
    }

    // ---------------------------------------------------------------- meshes

    // A box with its UVs in metres on every face, so tiled textures keep their size whatever its shape
    static readonly Dictionary<Vector3, Mesh> boxes = new Dictionary<Vector3, Mesh>();
    static Mesh BoxMesh(Vector3 size)
    {
        if (boxes.TryGetValue(size, out var cached) && cached != null) return cached;
        var h = size / 2f;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        void Face(Vector3 n, Vector3 u, Vector3 v, float du, float dv)
        {
            int start = vertices.Count;
            var c = Vector3.Scale(n, h);
            foreach (var (a, b) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                vertices.Add(c + u * (a * du / 2f) + v * (b * dv / 2f));
                uvs.Add(new Vector2((a + 1f) / 2f * du, (b + 1f) / 2f * dv));
            }
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }
        Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
        Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
        Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
        Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
        Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
        var mesh = new Mesh { name = "Box" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return boxes[size] = Saved(mesh, $"Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}");
    }

    // A solid of revolution round Y from a profile of (radius, height) points, bottom to top
    static Mesh Lathe(IList<Vector2> profile, int segments)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int j = 0; j < profile.Count; j++)
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Sin(a) * profile[j].x, profile[j].y, Mathf.Cos(a) * profile[j].x));
                uvs.Add(new Vector2(i / (float)segments, j / (float)(profile.Count - 1)));
            }
        int row = segments + 1;
        for (int j = 0; j + 1 < profile.Count; j++)
            for (int i = 0; i < segments; i++)
            {
                int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                triangles.AddRange(new[] { a, b, c, b, d, c }); // outward for a profile going up
            }
        var mesh = new Mesh { name = "Lathe" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return Saved(mesh, "Lathe_" + StableHash(string.Join("_", profile.Select(p => $"{p.x:0.####}-{p.y:0.####}"))) + "_" + segments);
    }

    // A name for a mesh from its shape that is the same every run (string.GetHashCode need not be)
    static string StableHash(string text)
    {
        uint h = 2166136261;
        foreach (char ch in text) h = (h ^ ch) * 16777619;
        return h.ToString("x8");
    }

    static Mesh CylinderMesh(float r, float length, int segments)
    {
        var mesh = Lathe(new[] { V(0f, -length / 2f), V(r, -length / 2f), V(r, length / 2f), V(0f, length / 2f) }, segments);
        return mesh;
    }

    static Mesh SphereMesh(float r, int segments) =>
        Lathe(Enumerable.Range(0, segments / 2 + 1).Select(i => { float a = Mathf.PI * i / (segments / 2) - Mathf.PI / 2f; return V(Mathf.Cos(a) * r, Mathf.Sin(a) * r); }).ToArray(), segments);

    static Mesh TorusMesh(float r, float tube, int segments, int sides) =>
        Lathe(Enumerable.Range(0, sides + 1).Select(i => { float a = Mathf.PI * 2f * i / sides; return V(r + Mathf.Cos(a) * tube, Mathf.Sin(a) * tube); }).ToArray(), segments);

    // An open band round Y (a label), `arc` of the way round, UVs across it
    static Mesh Band(float r, float height, int segments, float arc)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        int n = Mathf.Max(2, Mathf.RoundToInt(segments * arc));
        for (int j = 0; j < 2; j++)
            for (int i = 0; i <= n; i++)
            {
                float a = (i / (float)n - 0.5f) * arc * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Sin(a) * r, (j - 0.5f) * height, Mathf.Cos(a) * r));
                uvs.Add(new Vector2(1f - i / (float)n, j));
            }
        for (int i = 0; i < n; i++) triangles.AddRange(new[] { i, i + 1, i + n + 1, i + 1, i + n + 2, i + n + 1 });
        var mesh = new Mesh { name = "Band" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return Saved(mesh, $"Band_{r:0.####}_{height:0.###}_{segments}_{arc:0.##}");
    }

    // A flat quad facing +Z (a card), UVs over it
    static Mesh QuadMesh(float w, float h)
    {
        var mesh = new Mesh { name = "Quad" };
        mesh.SetVertices(new[] { new Vector3(w / 2f, -h / 2f, 0f), new Vector3(-w / 2f, -h / 2f, 0f), new Vector3(-w / 2f, h / 2f, 0f), new Vector3(w / 2f, h / 2f, 0f) });
        mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return Saved(mesh, $"Quad_{w:0.###}x{h:0.###}");
    }

    // A strip of orange peel curved like the fruit, lens-shaped, 10 cm long (Y) and 5.5 cm wide: orange skin on the
    // outside (+Z, submesh 0), white pith inside and round the edge (submesh 1)
    static Mesh PeelMesh()
    {
        const int nx = 10, ny = 16;
        const float W = 0.055f, Len = 0.1f, Thick = 0.007f;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var skin = new List<int>();
        var pith = new List<int>();
        for (int side = 0; side < 2; side++)
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float u = i / (float)nx - 0.5f, v = j / (float)ny - 0.5f;
                    float x = u * W * Mathf.Sqrt(Mathf.Max(0.02f, 1f - 4f * v * v)), y = v * Len;
                    vertices.Add(new Vector3(x, y, -x * x * 9f - y * y * 2.5f - side * Thick));
                    uvs.Add(new Vector2(u + 0.5f, v + 0.5f));
                }
        int row = nx + 1, back = row * (ny + 1);
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                skin.AddRange(new[] { a, b, c, b, d, c });
                pith.AddRange(new[] { a + back, c + back, b + back, b + back, c + back, d + back });
            }
        // The cut edge all round, joining skin to pith
        var loop = new List<int>();
        for (int i = 0; i < nx; i++) loop.Add(i);
        for (int j = 0; j < ny; j++) loop.Add(j * row + nx);
        for (int i = nx; i > 0; i--) loop.Add(ny * row + i);
        for (int j = ny; j > 0; j--) loop.Add(j * row);
        for (int k = 0; k < loop.Count; k++)
        {
            int p = loop[k], q = loop[(k + 1) % loop.Count];
            pith.AddRange(new[] { p, q + back, q, p, p + back, q + back });
        }
        var mesh = new Mesh { name = "Peel" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(skin, 0);
        mesh.SetTriangles(pith, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return Saved(mesh, "Peel2");
    }

    static Mesh Saved(Mesh mesh, string name) => RigUtility.SaveMesh(mesh, $"{Folder}/Meshes/{name}.asset");

    // ---------------------------------------------------------------- textures

    // Wood, labels, the menu and a soft dot for the sparks, drawn here (PNGs in Textures/)
    static void MakeTextures()
    {
        RigUtility.EnsureFolder(Folder + "/Meshes");
        Wood("WoodTop", 512, 512, Hex("8A4A26"), Hex("4A2210"), 4, false, 3);
        Wood("WoodPanel", 512, 512, Hex("5A3420"), Hex("2A160C"), 6, true, 5);
        Bricks("Brick");
        var dot = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 32f;
                float a = Mathf.Clamp01(Mathf.Exp(-d * d * 6f) - 0.01f);
                dot.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        SaveTexture(dot, "SoftDot", TextureWrapMode.Clamp);

        // Labels: cream, black, red and kraft paper, a border, an emblem and lines of "writing"
        Color[][] labels =
        {
            new[] { Hex("F1E6CC"), Hex("8A1C1C"), Hex("2A2A2A") }, new[] { Hex("1E1E1E"), Hex("D4AF37"), Hex("D4AF37") },
            new[] { Hex("A61E22"), Hex("F2E8D0"), Hex("F2E8D0") }, new[] { Hex("C8A878"), Hex("2E4A2A"), Hex("3A2A1A") },
        };
        for (int k = 0; k < labels.Length; k++)
        {
            var p = new SpritePainter(new Rect(0f, 0f, 2f, 1f), 128f);
            var c = labels[k];
            p.Fill(Shape.Box(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f)), c[0]);
            p.Fill(Shape.Box(new Vector2(1f, 0.5f), new Vector2(0.95f, 0.45f), 0.04f).Minus(Shape.Box(new Vector2(1f, 0.5f), new Vector2(0.92f, 0.42f), 0.03f)), c[1]);
            p.Draw(Shape.Circle(new Vector2(1f, 0.62f), 0.17f), c[1], c[2], 0.02f);
            p.Fill(Shape.Star(new Vector2(1f, 0.62f), 0.1f, 0.045f, 5), c[0]);
            p.Fill(Shape.Box(new Vector2(1f, 0.33f), new Vector2(0.45f, 0.035f), 0.02f), c[2]);
            p.Fill(Shape.Box(new Vector2(1f, 0.22f), new Vector2(0.3f, 0.02f), 0.01f), c[1]);
            p.Save($"{Textures}/Label{k}.png");
        }

        // Orange skin: bright orange with little pits all over it
        {
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var random = new System.Random(6);
            var pits = Enumerable.Range(0, 260).Select(_ => new Vector3((float)random.NextDouble() * 256f, (float)random.NextDouble() * 256f, 1.5f + (float)random.NextDouble() * 2f)).ToArray();
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.04f, y * 0.04f) * 0.12f + Mathf.PerlinNoise(x * 0.2f, y * 0.2f) * 0.06f;
                    float pit = 0f;
                    foreach (var pt in pits)
                    {
                        float dx = Mathf.Abs(x - pt.x), dy = Mathf.Abs(y - pt.y);
                        dx = Mathf.Min(dx, 256f - dx); dy = Mathf.Min(dy, 256f - dy);
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / pt.z;
                        if (d < 1f) pit = Mathf.Max(pit, 1f - d);
                    }
                    var c = Color.Lerp(Hex("F7931E"), Hex("E8700E"), n * 3f) * (1f - 0.18f * pit);
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            SaveTexture(tex, "PeelSkin", TextureWrapMode.Repeat);
        }
        // Half an orange from above: rind, a white ring of pith, ten juicy segments
        {
            var p = new SpritePainter(new Rect(-0.5f, -0.5f, 1f, 1f), 256f);
            p.Fill(Shape.Circle(Vector2.zero, 0.5f), Hex("E8780E"));
            p.Fill(Shape.Circle(Vector2.zero, 0.455f), Hex("FFF1D2"));
            for (int k = 0; k < 10; k++)
            {
                float a0 = k * 36f + 2.5f, a1 = (k + 1) * 36f - 2.5f;
                Vector2 Dir(float a) => new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                var wedge = new List<Vector2> { Dir((a0 + a1) / 2f) * 0.045f };
                for (int i = 0; i <= 6; i++) wedge.Add(Dir(Mathf.Lerp(a0, a1, i / 6f)) * 0.425f);
                p.Fill(Shape.Polygon(wedge), Hex("FF9524"));
                var inner = new List<Vector2> { Dir((a0 + a1) / 2f) * 0.12f };
                for (int i = 0; i <= 6; i++) inner.Add(Dir(Mathf.Lerp(a0 + 6f, a1 - 6f, i / 6f)) * 0.36f);
                p.Fill(Shape.Polygon(inner), Hex("FFB54A"));
            }
            p.Fill(Shape.Circle(Vector2.zero, 0.05f), Hex("FFF1D2"));
            p.Save($"{Textures}/OrangeCut.png");
        }

        // The menu card: the rainbow cocktail in a glass, alight, three stars
        {
            var p = new SpritePainter(new Rect(0f, 0f, 1.1f, 1.5f), 300f);
            p.Fill(Shape.Box(new Vector2(0.55f, 0.75f), new Vector2(0.55f, 0.75f)), Hex("F4ECDA"));
            p.Fill(Shape.Box(new Vector2(0.55f, 0.75f), new Vector2(0.5f, 0.7f), 0.04f).Minus(Shape.Box(new Vector2(0.55f, 0.75f), new Vector2(0.47f, 0.67f), 0.03f)), Hex("2A1A12"));
            p.Fill(Shape.Box(new Vector2(0.55f, 1.3f), new Vector2(0.38f, 0.06f), 0.03f), Hex("2A1A12"));
            var glass = Shape.Polygon(new[] { new Vector2(0.3f, 1.05f), new Vector2(0.8f, 1.05f), new Vector2(0.74f, 0.32f), new Vector2(0.36f, 0.32f) });
            string[] rainbow = { "8E44AD", "2E6BFF", "2EAA4A", "F2D02A", "F28A1A", "E8362B" };
            for (int i = 0; i < rainbow.Length; i++)
            {
                float y0 = 0.34f + i * 0.105f;
                p.Fill(glass.Intersect(Shape.Box(new Vector2(0.55f, y0 + 0.0525f), new Vector2(0.5f, 0.0525f))), Hex(rainbow[i]));
            }
            p.Fill(Shape.Stroke(new[] { new Vector2(0.3f, 1.05f), new Vector2(0.36f, 0.32f), new Vector2(0.74f, 0.32f), new Vector2(0.8f, 1.05f) }, 0.025f), Hex("2A1A12"));
            p.Fill(Shape.Union(Shape.Ellipse(new Vector2(0.55f, 1.12f), 0.12f, 0.08f), Shape.Polygon(new[] { new Vector2(0.45f, 1.12f), new Vector2(0.55f, 1.32f), new Vector2(0.65f, 1.12f) })), Hex("2E8BFF"));
            p.Fill(Shape.Union(Shape.Ellipse(new Vector2(0.55f, 1.1f), 0.06f, 0.045f), Shape.Polygon(new[] { new Vector2(0.5f, 1.1f), new Vector2(0.55f, 1.22f), new Vector2(0.6f, 1.1f) })), Hex("BFE3FF"));
            for (int i = 0; i < 3; i++) p.Fill(Shape.Star(new Vector2(0.4f + i * 0.15f, 0.18f), 0.055f, 0.024f, 5), Hex("E0A81E"));
            p.Save($"{Textures}/Menu.png");
        }
    }

    static void Wood(string name, int w, int h, Color light, Color dark, int planks, bool vertical, int seed)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var random = new System.Random(seed);
        var tints = Enumerable.Range(0, planks).Select(_ => 0.85f + 0.3f * (float)random.NextDouble()).ToArray();
        var offsets = Enumerable.Range(0, planks).Select(_ => (float)random.NextDouble() * 50f).ToArray();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float across = (vertical ? x : y) / (float)(vertical ? w : h), along = (vertical ? y : x) / (float)(vertical ? h : w);
                int plank = Mathf.Min(planks - 1, (int)(across * planks));
                float inPlank = across * planks - plank;
                float o = offsets[plank];
                float grain = Mathf.PerlinNoise(o + inPlank * 2.5f, along * 3f) * 0.65f + Mathf.PerlinNoise(o * 2f + inPlank * 18f, along * 1.2f) * 0.35f;
                float lines = 0.5f + 0.5f * Mathf.Sin((inPlank * 9f + grain * 7f) * Mathf.PI);
                float k = Mathf.Clamp01(0.25f + 0.55f * grain + 0.2f * lines);
                var c = Color.Lerp(dark, light, k) * tints[plank];
                if (inPlank < 0.012f || inPlank > 0.988f) c *= 0.45f;
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        SaveTexture(tex, name, TextureWrapMode.Repeat);
    }

    static void Bricks(string name)
    {
        const int W = 512, H = 512, Rows = 8, Cols = 4;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var random = new System.Random(9);
        var tint = new float[Rows * Cols * 2];
        for (int i = 0; i < tint.Length; i++) tint[i] = 0.8f + 0.35f * (float)random.NextDouble();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int row = y * Rows / H;
                float bx = (x + (row % 2) * W / (Cols * 2f)) * Cols / (float)W;
                int col = (int)bx % Cols;
                float fx = bx - Mathf.Floor(bx), fy = y * Rows / (float)H - row;
                bool mortar = fx < 0.03f || fx > 0.97f || fy < 0.06f || fy > 0.94f;
                float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.3f + Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.15f;
                var c = mortar ? new Color(0.42f, 0.4f, 0.38f) * (0.8f + n) : new Color(0.62f, 0.3f, 0.2f) * tint[row * Cols + col] * (0.8f + n);
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        SaveTexture(tex, name, TextureWrapMode.Repeat);
    }

    static void SaveTexture(Texture2D tex, string name, TextureWrapMode wrap)
    {
        tex.Apply();
        string path = $"{Textures}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = wrap;
        importer.anisoLevel = 8;
        importer.alphaIsTransparency = wrap == TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }
}
