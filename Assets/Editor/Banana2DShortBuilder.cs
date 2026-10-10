using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

// Short: a 2D test (2026-10-10; the user: "make a 10 s 2D animation yourself, any subject"). Bacon in flat cut-out
// animation, "the banana peel": he walks along whistling (notes float up), treads on a peel, flips over backwards and
// lands flat, "БАЦ!", stars circling his head; gets up, sees the peel again ahead, steps over it with a high smug step,
// winks at us, takes one more step onto an open manhole, hangs in the air a moment, "o_o", drops in; the cover rolls
// over and falls flat on the hole, "ДЗЫНЬ!", and his eyes blink in its slots. 10 s.
// All drawn here in code (SpritePainter: shapes with flat colours and a dark outline), no packages: each body part is a
// sprite on its own transform (a cut-out puppet), posed frame by frame and baked into one clip, like the 3D shorts.
// Tools/Shorts/Build Banana 2D Short -> Assets/Scenes/Shorts_Banana2D.unity + Timeline; render with ShortsRenderer.
public static class Banana2DShortBuilder
{
    const float Fps = 30f, Duration = 10f;
    const string ScenePath = "Assets/Scenes/Shorts_Banana2D.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_Banana2D.playable";
    const string ClipPath = "Assets/Animations/Banana2D_Stage.anim";
    const string Folder = "Assets/Shorts/Banana2D";
    const string SpriteFolder = Folder + "/Sprites";
    const string FontPath = "Assets/Viewer/Resources/ViewerFont.ttf"; // Liberation Sans, with Cyrillic

    // The beats (seconds): the slip, landing flat, getting up, on again, a look down at the peel, the step over it, the
    // wink, on to the hole, hanging in the air, the drop, the cover tipping, the clang
    const float Slip = 2.6f, Land = 4.0f, GetUp = 5.6f, Up = 6.3f, Walk2 = 6.6f, LookDown = 7.5f, StepOver = 7.8f, Brag = 8.5f,
        Onto = 8.75f, Hang = 9.0f, Drop = 9.2f, Tip = 9.4f, Clang = 9.7f;

    // Layout (units): his feet on Line; the peel, the open manhole and its cover along it
    const float Line = -2.4f, HipH = 1.1f, LieH = 0.42f, PeelLands = 2.2f, HoleX = 4.1f, HoleRx = 0.68f, HoleRy = 0.19f,
        CoverX = 5.35f, CoverR = 0.6f;
    const float CamY = -0.55f, CamSize = 4.3f; // 8.6 units tall, 4.84 wide
    const float Outline = 0.035f;

    static readonly Color Ink = Hex("1C1416"), Skin = Hex("F1F1F1"), Shirt = Hex("2A2A31"), Jeans = Hex("2E3442"), Shoe = Hex("F4F4F4"),
        HairBlue = Hex("2D62E6"), HairLight = Hex("6A95FF"), Mouth = Hex("5A1F2A"), Blush = new Color(1f, 0.55f, 0.62f, 0.5f),
        Back = new Color(0.78f, 0.78f, 0.86f); // the far arm and leg, a shade darker

    static Color Hex(string hex) => RigUtility.Hex(hex);

    [MenuItem("Tools/Shorts/Build Banana 2D Short")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Banana2D] Exit Play Mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var f in new[] { "Assets/Animations", "Assets/Timelines", "Assets/Scenes", Folder, SpriteFolder }) RigUtility.EnsureFolder(f);

        var sprites = DrawSprites();
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/SpriteUnlit.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            AssetDatabase.CreateAsset(material, Folder + "/SpriteUnlit.mat");
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var stage = new GameObject("Stage");
        stage.AddComponent<Animator>();
        var s = new Stage(stage.transform, sprites, material);

        var clip = Bake(s);

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
        var director = new GameObject("Timeline_Banana2D").AddComponent<PlayableDirector>();
        var animTrack = timeline.CreateTrack<AnimationTrack>(null, "Stage");
        animTrack.trackOffset = TrackOffset.ApplySceneOffsets; // the clip keys the stage's children, not the stage
        var c = animTrack.CreateClip(clip);
        c.start = 0;
        c.duration = Duration;
        director.SetGenericBinding(animTrack, stage.GetComponent<Animator>());
        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        director.time = 0;
        director.Evaluate();
        Debug.Log($"[Banana2D] Built {ScenePath}, {Duration} s");
    }

    // ---------------------------------------------------------------- the sprites

    static Dictionary<string, Sprite> DrawSprites()
    {
        var sprites = new Dictionary<string, Sprite>();
        Sprite Save(SpritePainter p, string name) => sprites[name] = p.Save($"{SpriteFolder}/{name}.png");
        const float Ppu = 220f;

        // A leg, from the hip down (its pivot): jeans and a white trainer pointing forward (+X)
        {
            var p = new SpritePainter(new Rect(-0.3f, -1.18f, 0.74f, 1.26f), Ppu);
            var jeans = Shape.Box(new Vector2(0f, -0.45f), new Vector2(0.2f, 0.5f), 0.09f);
            var shoe = Shape.Box(new Vector2(0.07f, -0.99f), new Vector2(0.27f, 0.1f), 0.09f);
            p.Draw(jeans, Jeans, Ink, Outline);
            p.Draw(shoe, Shoe, Ink, Outline);
            p.Fill(shoe.Intersect(Shape.HalfPlane(new Vector2(0f, -1.04f), Vector2.down)), Hex("C9CDD6"));
            Save(p, "Leg");
        }
        // The torso from the hips (its pivot) up: the black T-shirt, the top of the jeans, the logo on the chest
        {
            var p = new SpritePainter(new Rect(-0.62f, -0.25f, 1.24f, 1.5f), Ppu);
            var body = Shape.Box(new Vector2(0f, 0.5f), new Vector2(0.46f, 0.6f), 0.12f);
            p.Draw(body, Shirt, Ink, Outline);
            p.Fill(body.Intersect(Shape.HalfPlane(new Vector2(0f, 0.08f), Vector2.down)), Jeans);
            p.Fill(Shape.Stroke(new[] { new Vector2(-0.45f, 0.08f), new Vector2(0.45f, 0.08f) }, 0.03f), Ink);
            p.Fill(body.Intersect(Shape.HalfPlane(new Vector2(-0.3f, 0f), Vector2.left)), new Color(0f, 0f, 0f, 0.2f));
            p.Draw(Shape.Box(new Vector2(0.17f, 0.78f), new Vector2(0.1f, 0.1f), 0.03f), Color.white, Ink, 0.02f);
            p.Fill(Shape.Polygon(new[] { new Vector2(0.14f, 0.72f), new Vector2(0.14f, 0.84f), new Vector2(0.23f, 0.78f) }), Hex("1F4FD6"));
            Save(p, "Torso");
        }
        // An arm from the shoulder (its pivot): a short sleeve, a blocky arm and hand, Roblox style
        {
            var p = new SpritePainter(new Rect(-0.26f, -1.2f, 0.52f, 1.34f), Ppu);
            p.Draw(Shape.Box(new Vector2(0f, -0.62f), new Vector2(0.145f, 0.36f), 0.07f), Skin, Ink, Outline);
            p.Draw(Shape.Box(new Vector2(0.01f, -1.0f), new Vector2(0.16f, 0.13f), 0.1f), Skin, Ink, Outline);
            p.Draw(Shape.Box(new Vector2(0f, -0.2f), new Vector2(0.165f, 0.27f), 0.09f), Shirt, Ink, Outline);
            Save(p, "Arm");
        }
        // The head from the neck (its pivot): a rounded block, three-quarters on (facing +X), his spiky blue bacon hair
        {
            var p = new SpritePainter(new Rect(-0.74f, -0.1f, 1.46f, 1.5f), Ppu);
            var head = Shape.Box(new Vector2(0f, 0.47f), new Vector2(0.52f, 0.47f), 0.24f);
            p.Draw(head, Skin, Ink, 0.04f);
            p.Fill(head.Intersect(Shape.HalfPlane(new Vector2(-0.36f, 0f), Vector2.left)), new Color(0.72f, 0.76f, 0.88f, 0.35f));
            var hair = Shape.Polygon(new[]
            {
                new Vector2(-0.58f, 0.52f), new Vector2(-0.62f, 0.82f), new Vector2(-0.68f, 1.02f), new Vector2(-0.44f, 0.99f),
                new Vector2(-0.46f, 1.2f), new Vector2(-0.22f, 1.06f), new Vector2(-0.12f, 1.28f), new Vector2(0.06f, 1.08f),
                new Vector2(0.22f, 1.26f), new Vector2(0.32f, 1.03f), new Vector2(0.52f, 1.12f), new Vector2(0.58f, 0.88f),
                new Vector2(0.6f, 0.7f), new Vector2(0.47f, 0.79f), new Vector2(0.42f, 0.6f), new Vector2(0.3f, 0.77f),
                new Vector2(0.14f, 0.62f), new Vector2(0.02f, 0.78f), new Vector2(-0.14f, 0.66f), new Vector2(-0.26f, 0.8f),
                new Vector2(-0.42f, 0.64f), new Vector2(-0.5f, 0.7f),
            });
            p.Draw(hair, HairBlue, Ink, 0.04f);
            foreach (var (a, b) in new[] { (new Vector2(-0.42f, 0.9f), new Vector2(-0.3f, 1.07f)), (new Vector2(-0.1f, 0.92f), new Vector2(-0.06f, 1.15f)),
                         (new Vector2(0.2f, 0.92f), new Vector2(0.24f, 1.12f)), (new Vector2(0.45f, 0.88f), new Vector2(0.5f, 1.0f)) })
                p.Fill(Shape.Capsule(a, b, 0.022f), HairLight);
            Save(p, "Head");
        }
        DrawFaces(Save);

        // The banana peel, lying open (pivot: the middle of its underside)
        {
            var p = new SpritePainter(new Rect(-0.46f, -0.08f, 0.92f, 0.52f), Ppu);
            var peel = Shape.Union(Shape.Ellipse(new Vector2(0f, 0.09f), 0.17f, 0.09f),
                Shape.Capsule(new Vector2(-0.05f, 0.07f), new Vector2(-0.34f, 0.04f), 0.065f),
                Shape.Capsule(new Vector2(0.05f, 0.07f), new Vector2(0.33f, 0.05f), 0.065f),
                Shape.Capsule(new Vector2(0f, 0.12f), new Vector2(0.07f, 0.31f), 0.06f));
            p.Draw(peel, Hex("FFD84A"), Ink, 0.03f);
            p.Fill(Shape.Ellipse(new Vector2(0f, 0.1f), 0.1f, 0.045f), Hex("FFF1B0"));
            p.Draw(Shape.Box(new Vector2(0.08f, 0.37f), new Vector2(0.03f, 0.04f), 0.01f), Hex("7A5230"), Ink, 0.02f);
            foreach (var spot in new[] { new Vector2(-0.22f, 0.05f), new Vector2(0.24f, 0.07f), new Vector2(0.05f, 0.24f) })
                p.Fill(Shape.Circle(spot, 0.016f), Hex("8A6A2A"));
            Save(p, "Peel");
        }
        // A musical note (whistling)
        {
            var p = new SpritePainter(new Rect(-0.22f, -0.2f, 0.44f, 0.54f), Ppu);
            var note = Shape.Union(Shape.Ellipse(new Vector2(-0.04f, -0.06f), 0.075f, 0.055f, 25f),
                Shape.Box(new Vector2(0.024f, 0.1f), new Vector2(0.017f, 0.17f)),
                Shape.Polygon(new[] { new Vector2(0.01f, 0.27f), new Vector2(0.01f, 0.19f), new Vector2(0.13f, 0.1f), new Vector2(0.12f, 0.19f) }));
            p.Draw(note, Hex("2B2440"), Color.white, 0.03f);
            Save(p, "Note");
        }
        {
            var p = new SpritePainter(new Rect(-0.18f, -0.18f, 0.36f, 0.36f), Ppu);
            p.Draw(Shape.Star(Vector2.zero, 0.14f, 0.06f, 5), Hex("FFD23F"), Hex("6B4A00"), 0.025f);
            Save(p, "Star");
        }
        {
            var p = new SpritePainter(new Rect(-0.36f, -0.06f, 0.72f, 0.46f), Ppu);
            p.Draw(Shape.Union(Shape.Circle(new Vector2(-0.15f, 0.12f), 0.12f), Shape.Circle(new Vector2(0.05f, 0.17f), 0.16f),
                Shape.Circle(new Vector2(0.2f, 0.1f), 0.1f)), Hex("EFE9DD"), Hex("B9B0A0"), 0.025f);
            Save(p, "Dust");
        }
        // A comic burst behind the sound words
        {
            var p = new SpritePainter(new Rect(-1.08f, -0.74f, 2.16f, 1.48f), 160f);
            Shape Burst(float outer, float inner) => Shape.Polygon(Enumerable.Range(0, 28).Select(i =>
            {
                float a = i * Mathf.PI / 14f;
                float r = i % 2 == 0 ? outer * (i % 4 == 0 ? 1f : 0.9f) : inner;
                return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.62f);
            }).ToArray());
            p.Draw(Burst(1.0f, 0.72f), Hex("FFE45C"), Ink, 0.045f);
            p.Fill(Burst(0.8f, 0.6f), Hex("FFF3A8"));
            Save(p, "Burst");
        }
        // The manhole cover, face on (pivot: its middle), two slots across it
        {
            var p = new SpritePainter(new Rect(-0.68f, -0.68f, 1.36f, 1.36f), Ppu);
            p.Draw(Shape.Circle(Vector2.zero, CoverR), Hex("5E6470"), Ink, Outline);
            p.Fill(Shape.Stroke(Shape.Arc(Vector2.zero, 0.5f, 0f, 360f, 48), 0.04f), Hex("4A505B"));
            var inside = Shape.Circle(Vector2.zero, 0.44f);
            for (int k = -6; k <= 6; k++)
                foreach (var dir in new[] { 1f, -1f })
                    p.Fill(Shape.Stroke(new[] { new Vector2(-0.6f, k * 0.11f - dir * 0.6f), new Vector2(0.6f, k * 0.11f + dir * 0.6f) }, 0.022f).Intersect(inside), Hex("525864"));
            p.Fill(Shape.Stroke(Shape.Arc(Vector2.zero, 0.56f, 100f, 160f), 0.03f), Hex("8A909C"));
            foreach (float x in new[] { -0.18f, 0.18f }) p.Fill(Shape.Ellipse(new Vector2(x, 0.03f), 0.09f, 0.035f), Hex("17181C"));
            Save(p, "Cover");
        }
        {
            var p = new SpritePainter(new Rect(-0.36f, -0.1f, 0.72f, 0.2f), Ppu);
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                p.Draw(Shape.Ellipse(new Vector2(x, 0f), 0.07f, 0.045f), Color.white, Ink, 0.015f);
                p.Fill(Shape.Circle(new Vector2(x + 0.012f, -0.005f), 0.022f), Ink);
            }
            Save(p, "CoverEyes");
        }
        DrawWorld(Save);
        return sprites;
    }

    // His faces, each a sprite over the front of the head (pivot: the face's middle); eyes and mouth in dark ink
    static void DrawFaces(System.Func<SpritePainter, string, Sprite> save)
    {
        var area = new Rect(-0.42f, -0.32f, 0.84f, 0.62f);
        Vector2 eyeL = new Vector2(-0.16f, 0.05f), eyeR = new Vector2(0.19f, 0.05f);
        const float W = 0.035f;
        SpritePainter New() => new SpritePainter(area, 220f);
        void Cheeks(SpritePainter p)
        {
            p.Fill(Shape.Ellipse(new Vector2(-0.28f, -0.09f), 0.07f, 0.035f), Blush);
            p.Fill(Shape.Ellipse(new Vector2(0.33f, -0.09f), 0.06f, 0.035f), Blush);
        }
        void HalfLid(SpritePainter p, Vector2 e)
        {
            p.Fill(Shape.Circle(e, 0.06f).Intersect(Shape.HalfPlane(e, Vector2.down)), Ink);
            p.Fill(Shape.Stroke(new[] { e + new Vector2(-0.08f, 0.005f), e + new Vector2(0.08f, 0.02f) }, W), Ink);
        }
        Shape Closed(Vector2 e) => Shape.Stroke(Shape.Arc(e + new Vector2(0f, -0.035f), 0.065f, 20f, 160f), W);

        {   // Normal: oval eyes, a smile
            var p = New();
            foreach (var e in new[] { eyeL, eyeR }) p.Fill(Shape.Ellipse(e, 0.045f, 0.07f), Ink);
            p.Fill(Shape.Stroke(Shape.Arc(new Vector2(0.04f, -0.06f), 0.12f, 215f, 325f), W), Ink);
            save(p, "Face_Normal");
        }
        {   // Whistling: eyes shut happily, lips in an "o", cheeks pink
            var p = New();
            Cheeks(p);
            foreach (var e in new[] { eyeL, eyeR }) p.Fill(Closed(e), Ink);
            p.Draw(Shape.Circle(new Vector2(0.08f, -0.17f), 0.04f), Mouth, Ink, 0.025f);
            save(p, "Face_Whistle");
        }
        {   // Shock: round white eyes with tiny pupils, brows up, an open mouth ("o_o")
            var p = New();
            foreach (var e in new[] { eyeL, eyeR })
            {
                p.Draw(Shape.Circle(e, 0.1f), Color.white, Ink, 0.03f);
                p.Fill(Shape.Circle(e, 0.028f), Ink);
                p.Fill(Shape.Stroke(new[] { e + new Vector2(-0.08f, 0.17f), e + new Vector2(0.08f, 0.19f) }, 0.03f), Ink);
            }
            p.Draw(Shape.Ellipse(new Vector2(0.05f, -0.2f), 0.045f, 0.065f), Mouth, Ink, 0.025f);
            save(p, "Face_Shock");
        }
        {   // Dizzy: X eyes, a wobbly mouth
            var p = New();
            foreach (var e in new[] { eyeL, eyeR })
            {
                p.Fill(Shape.Stroke(new[] { e + new Vector2(-0.06f, -0.06f), e + new Vector2(0.06f, 0.06f) }, W), Ink);
                p.Fill(Shape.Stroke(new[] { e + new Vector2(-0.06f, 0.06f), e + new Vector2(0.06f, -0.06f) }, W), Ink);
            }
            p.Fill(Shape.Stroke(Enumerable.Range(0, 13).Select(i => new Vector2(-0.1f + i * 0.024f, -0.17f + 0.025f * Mathf.Sin(i * 1.4f))).ToArray(), 0.03f), Ink);
            save(p, "Face_Dizzy");
        }
        {   // Smug: heavy lids, one brow up, a smirk
            var p = New();
            HalfLid(p, eyeL);
            HalfLid(p, eyeR);
            p.Fill(Shape.Stroke(new[] { eyeL + new Vector2(-0.07f, 0.13f), eyeL + new Vector2(0.07f, 0.12f) }, 0.03f), Ink);
            p.Fill(Shape.Stroke(Shape.Arc(eyeR + new Vector2(0f, 0.08f), 0.08f, 30f, 150f), 0.03f), Ink);
            p.Fill(Shape.Stroke(new[] { new Vector2(-0.08f, -0.17f), new Vector2(0.06f, -0.16f), new Vector2(0.16f, -0.1f) }, W), Ink);
            save(p, "Face_Smug");
        }
        {   // A wink at us: one heavy lid, one eye shut, a wide smirk
            var p = New();
            Cheeks(p);
            HalfLid(p, eyeL);
            p.Fill(Closed(eyeR), Ink);
            p.Fill(Shape.Stroke(Shape.Arc(eyeR + new Vector2(0f, 0.08f), 0.08f, 30f, 150f), 0.03f), Ink);
            p.Fill(Shape.Stroke(new[] { new Vector2(-0.1f, -0.15f), new Vector2(0.05f, -0.17f), new Vector2(0.18f, -0.08f) }, W), Ink);
            save(p, "Face_Wink");
        }
    }

    // The street: sky, far city, trees, the grass and the pavement with the hole (behind him), and the front of the
    // pavement, the kerb and the road with the front of the hole (in front of him, hiding him as he drops in)
    static void DrawWorld(System.Func<SpritePainter, string, Sprite> save)
    {
        Color Paving = Hex("D5DAE2"), Joint = Hex("BCC3CD"), HoleDark = Hex("141518"), Rim = Hex("8E959F");
        const float From = -9f, To = 9f;
        var hole = Shape.Ellipse(new Vector2(HoleX, Line), HoleRx, HoleRy);
        Shape Band(float y0, float y1) => Shape.Box(new Vector2(0f, (y0 + y1) / 2f), new Vector2((To - From) / 2f + 0.1f, (y1 - y0) / 2f));
        {
            var p = new SpritePainter(new Rect(From, Line, To - From, 0.92f), 110f);
            p.Fill(Band(-2.13f, -1.64f), Hex("7CC35E"));
            p.Fill(Shape.Polygon(Enumerable.Range(0, 181).Select(i => new Vector2(From + i * 0.1f, -1.66f + (i % 2 == 0 ? 0.07f : 0f)))
                .Concat(new[] { new Vector2(To, -1.75f), new Vector2(From, -1.75f) }).ToArray()), Hex("7CC35E"));
            p.Fill(Band(-2.13f, -2.04f), Hex("66AC4C"));
            p.Fill(Band(-2.4f, -2.1f), Paving);
            p.Fill(Band(-2.14f, -2.1f), Hex("B8BFC9"));
            for (float x = From; x < To; x += 0.9f) p.Fill(Shape.Stroke(new[] { new Vector2(x, -2.4f), new Vector2(x + 0.1f, -2.12f) }, 0.022f), Joint);
            var up = Shape.HalfPlane(new Vector2(0f, Line), Vector2.up);
            p.Fill(hole.Intersect(up), HoleDark);
            p.Fill(hole.Minus(Shape.Ellipse(new Vector2(HoleX, Line - 0.06f), HoleRx * 0.96f, HoleRy * 0.9f)).Intersect(up), Hex("373A42"));
            p.Fill(Shape.Stroke(Shape.Arc(new Vector2(HoleX, Line), 1f, 0f, 180f, 40).Select(v => new Vector2(HoleX + (v.x - HoleX) * HoleRx, Line + (v.y - Line) * HoleRy)).ToArray(), 0.04f), Rim);
            save(p, "GroundBack");
        }
        {
            var p = new SpritePainter(new Rect(From, -5.2f, To - From, 5.2f + Line), 110f);
            p.Fill(Band(-2.76f, -2.4f), Paving);
            for (float x = From; x < To; x += 0.9f) p.Fill(Shape.Stroke(new[] { new Vector2(x - 0.13f, -2.76f), new Vector2(x, -2.4f) }, 0.022f), Joint);
            p.Fill(Band(-3.02f, -2.76f), Hex("AEB5BF"));
            p.Fill(Band(-2.79f, -2.75f), Hex("E6EAF0"));
            p.Fill(Band(-5.2f, -3.02f), Hex("4B505B"));
            p.Fill(Band(-3.12f, -3.02f), Hex("3E434D"));
            for (float x = From; x < To; x += 1.6f) p.Fill(Shape.Box(new Vector2(x + 0.4f, -4.25f), new Vector2(0.4f, 0.05f)), Hex("E9E9E9"));
            p.Fill(hole.Intersect(Shape.HalfPlane(new Vector2(0f, Line), Vector2.down)), HoleDark);
            p.Fill(Shape.Stroke(Shape.Arc(new Vector2(HoleX, Line), 1f, 180f, 360f, 40).Select(v => new Vector2(HoleX + (v.x - HoleX) * HoleRx, Line + (v.y - Line) * HoleRy)).ToArray(), 0.04f), Rim);
            save(p, "GroundFront");
        }
        // Trees, bushes and lamp posts behind the grass (they move a little slower than the street)
        {
            var p = new SpritePainter(new Rect(-6.6f, -2.0f, 13.2f, 3.9f), 90f);
            Color Leaf = Hex("5FB35A"), LeafDark = Hex("4C9A4B"), LeafLine = Hex("2F5E2C");
            foreach (float x in new[] { -4.3f, 1.2f, 4.4f })
            {
                p.Draw(Shape.Box(new Vector2(x, -0.9f), new Vector2(0.035f, 0.95f)), Hex("2F4A3A"), Ink, 0.02f);
                p.Draw(Shape.Box(new Vector2(x + 0.1f, 0.08f), new Vector2(0.15f, 0.05f), 0.02f), Hex("2F4A3A"), Ink, 0.02f);
                p.Fill(Shape.Ellipse(new Vector2(x + 0.17f, 0.0f), 0.07f, 0.05f), Hex("FFF2B0"));
            }
            foreach (var (x, size) in new[] { (-5.6f, 1.0f), (-2.7f, 1.15f), (0.0f, 0.95f), (2.9f, 1.1f), (5.7f, 1.0f) })
            {
                p.Draw(Shape.Box(new Vector2(x, -1.2f * size), new Vector2(0.1f * size, 0.65f * size), 0.03f), Hex("8A5A3C"), LeafLine, 0.035f);
                var crown = Shape.Union(Shape.Circle(new Vector2(x, 0.05f * size), 0.72f * size), Shape.Circle(new Vector2(x - 0.55f * size, -0.25f * size), 0.48f * size),
                    Shape.Circle(new Vector2(x + 0.55f * size, -0.2f * size), 0.52f * size), Shape.Circle(new Vector2(x + 0.1f * size, 0.55f * size), 0.52f * size));
                p.Draw(crown, Leaf, LeafLine, 0.04f);
                p.Fill(crown.Intersect(Shape.HalfPlane(new Vector2(x + 0.15f * size, -0.1f), new Vector2(1f, -0.7f))), LeafDark);
                p.Fill(Shape.Circle(new Vector2(x - 0.25f * size, 0.42f * size), 0.16f * size), Hex("7ACB72"));
            }
            foreach (float x in new[] { -4.0f, -1.3f, 1.6f, 4.3f })
            {
                var bush = Shape.Union(Shape.Circle(new Vector2(x - 0.3f, -1.6f), 0.3f), Shape.Circle(new Vector2(x, -1.5f), 0.38f), Shape.Circle(new Vector2(x + 0.32f, -1.62f), 0.28f));
                p.Draw(bush, Hex("56A851"), LeafLine, 0.035f);
            }
            save(p, "Trees");
        }
        // The city far off: pale blue blocks with rows of windows
        {
            var p = new SpritePainter(new Rect(-5.6f, -2.0f, 11.2f, 4.6f), 70f);
            var random = new System.Random(3);
            Color[] walls = { Hex("B9C6E8"), Hex("A9B8E0"), Hex("C7D1EF") };
            float x = -5.5f;
            int n = 0;
            while (x < 5.5f)
            {
                float w = 0.7f + (float)random.NextDouble() * 0.8f, h = 1.6f + (float)random.NextDouble() * 2.4f;
                var wall = Shape.Box(new Vector2(x + w / 2f, -1.9f + h / 2f), new Vector2(w / 2f, h / 2f), 0.02f);
                p.Draw(wall, walls[n % 3], Hex("9AA9D3"), 0.02f);
                for (float wy = -1.6f; wy < -1.9f + h - 0.3f; wy += 0.32f)
                    for (float wx = x + 0.15f; wx < x + w - 0.15f; wx += 0.22f)
                        p.Fill(Shape.Box(new Vector2(wx + 0.05f, wy), new Vector2(0.05f, 0.08f)), Hex("DCE4F8"));
                if (n % 2 == 0) p.Fill(Shape.Stroke(new[] { new Vector2(x + w * 0.3f, -1.9f + h), new Vector2(x + w * 0.3f, -1.9f + h + 0.3f) }, 0.025f), Hex("9AA9D3"));
                x += w + 0.05f;
                n++;
            }
            save(p, "City");
        }
        {
            var p = new SpritePainter(new Rect(-0.92f, -0.36f, 1.84f, 0.84f), 120f);
            p.Draw(Shape.Union(Shape.Circle(new Vector2(-0.45f, 0f), 0.28f), Shape.Circle(new Vector2(-0.05f, 0.15f), 0.38f),
                Shape.Circle(new Vector2(0.4f, 0.02f), 0.3f), Shape.Box(new Vector2(0f, -0.1f), new Vector2(0.7f, 0.18f), 0.15f)), Color.white, Hex("C6E4F7"), 0.03f);
            save(p, "Cloud");
        }
        {
            var p = new SpritePainter(new Rect(-0.5f, -0.5f, 1f, 1f), 64f);
            p.VerticalGradient(Hex("E2F4FF"), Hex("6EC6FF"));
            save(p, "Sky");
        }
    }

    // ---------------------------------------------------------------- the stage

    class Stage
    {
        public readonly Transform root, camera, bacon, pelvis, torso, head, legF, legB, armF, armB, peel, cover, coverEyes, bang, clang;
        public readonly Transform trees, city;
        public readonly Transform[] notes = new Transform[5], stars = new Transform[3], dust = new Transform[6], clouds = new Transform[4];
        public readonly Dictionary<string, GameObject> faces = new Dictionary<string, GameObject>();
        readonly Dictionary<string, Sprite> sprites;
        readonly Material material;

        public Stage(Transform root, Dictionary<string, Sprite> sprites, Material material)
        {
            this.root = root;
            this.sprites = sprites;
            this.material = material;

            var cam = new GameObject("Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera = cam.transform;
            camera.SetParent(root, false);
            camera.localPosition = new Vector3(0f, CamY, -10f);
            cam.orthographic = true;
            cam.orthographicSize = CamSize;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("CDEBFF");
            cam.allowMSAA = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            var sky = Part("Sky", camera, "Sky", -30);
            sky.localPosition = new Vector3(0f, 0f, 10f);
            sky.localScale = new Vector3(CamSize * 2f * 9f / 16f + 1f, CamSize * 2f + 1f, 1f);

            for (int i = 0; i < clouds.Length; i++) clouds[i] = Part("Cloud" + i, root, "Cloud", -25);
            city = Part("City", root, "City", -20);
            trees = Part("Trees", root, "Trees", -10);
            Part("GroundBack", root, "GroundBack", 5);
            Part("GroundFront", root, "GroundFront", 30);
            peel = Part("Peel", root, "Peel", 9);

            // Bacon, a cut-out puppet: the root on the ground (moves, squashes), the pelvis at the hips (tilts and
            // flips), the parts on their joints
            bacon = new GameObject("Bacon").transform;
            bacon.SetParent(root, false);
            pelvis = new GameObject("Pelvis").transform;
            pelvis.SetParent(bacon, false);
            legB = Part("LegBack", pelvis, "Leg", 10, new Vector2(-0.06f, 0f), Back);
            legF = Part("LegFront", pelvis, "Leg", 12, new Vector2(0.07f, 0f));
            torso = Part("Torso", pelvis, "Torso", 14);
            armB = Part("ArmBack", torso, "Arm", 11, new Vector2(-0.24f, 0.98f), Back);
            head = Part("Head", torso, "Head", 16, new Vector2(0.02f, 1.07f));
            foreach (var face in new[] { "Normal", "Whistle", "Shock", "Dizzy", "Smug", "Wink" })
                faces[face] = Part("Face_" + face, head, "Face_" + face, 17, new Vector2(0.1f, 0.4f)).gameObject;
            armF = Part("ArmFront", torso, "Arm", 18, new Vector2(0.17f, 0.98f));

            for (int i = 0; i < notes.Length; i++) notes[i] = Part("Note" + i, root, "Note", 20);
            for (int i = 0; i < stars.Length; i++) stars[i] = Part("Star" + i, root, "Star", 21);
            for (int i = 0; i < dust.Length; i++) dust[i] = Part("Dust" + i, root, "Dust", 22);
            cover = Part("Cover", root, "Cover", 35);
            coverEyes = Part("CoverEyes", root, "CoverEyes", 36);
            bang = Word("Bang", "БАЦ!", Hex("E8362B"), 0.04f);
            clang = Word("Clang", "ДЗЫНЬ!", Hex("2E6BFF"), 0.03f);
        }

        Transform Part(string name, Transform parent, string sprite, int order, Vector2 at = default, Color? tint = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprites[sprite];
            r.sharedMaterial = material;
            r.sortingOrder = order;
            r.color = tint ?? Color.white;
            return go.transform;
        }

        // A sound word on a comic burst, in its colour with a dark outline (copies of it nudged all round)
        Transform Word(string name, string text, Color color, float size)
        {
            var burst = Part(name, root, "Burst", 45);
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            for (int k = 0; k <= 8; k++)
            {
                var go = new GameObject(k < 8 ? "Outline" + k : "Text");
                go.transform.SetParent(burst, false);
                float a = k * Mathf.PI / 4f;
                go.transform.localPosition = k < 8 ? new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.035f : Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                var mesh = go.AddComponent<TextMesh>();
                mesh.text = text;
                mesh.font = font;
                mesh.fontSize = 120;
                mesh.characterSize = size;
                mesh.fontStyle = FontStyle.Bold;
                mesh.anchor = TextAnchor.MiddleCenter;
                mesh.alignment = TextAlignment.Center;
                mesh.color = k < 8 ? Ink : color;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = font.material;
                r.sortingOrder = k < 8 ? 46 : 47;
            }
            return burst;
        }
    }

    // ---------------------------------------------------------------- the story, frame by frame

    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    static float Bump(float from, float to, float t) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(Mathf.InverseLerp(from, to, t)));
    static float EaseOutBack(float v) { const float c1 = 1.70158f, c3 = c1 + 1f; v -= 1f; return 1f + c3 * v * v * v + c1 * v * v; }

    // Where his hips are along the street (x) over time
    static float BaconX(float t)
    {
        if (t < Slip) return -5.2f + 1.9f * t;
        if (t < Land) return -0.26f + 0.42f * Smooth(Slip, Land, t);
        if (t < Walk2) return 0.16f;
        if (t < LookDown) return Mathf.Lerp(0.16f, 1.5f, Mathf.Lerp(Mathf.InverseLerp(Walk2, LookDown, t), Smooth(Walk2, LookDown, t), 0.4f));
        if (t < StepOver) return 1.5f;
        if (t < Brag) return Mathf.Lerp(1.5f, 2.9f, Smooth(StepOver, Brag, t));
        if (t < Onto) return 2.9f;
        if (t < Hang) return Mathf.Lerp(2.9f, 3.95f, Mathf.InverseLerp(Onto, Hang, t));
        return 3.95f;
    }

    static bool Walking(float t) => t < Slip || (t >= Walk2 && t < LookDown) || (t >= Onto && t < Hang);

    // Keyed values, one per frame, for every animated transform, object and sprite colour
    class Keys
    {
        public readonly List<Vector2> pos = new List<Vector2>(), scale = new List<Vector2>();
        public readonly List<float> rot = new List<float>();
    }
    static readonly Dictionary<Transform, Keys> keys = new Dictionary<Transform, Keys>();
    static readonly Dictionary<GameObject, List<float>> shown = new Dictionary<GameObject, List<float>>();
    static readonly Dictionary<SpriteRenderer, List<float>> alphas = new Dictionary<SpriteRenderer, List<float>>();

    static void Set(Transform t, Vector2 at, float degrees = 0f, Vector2? scale = null)
    {
        var s = scale ?? Vector2.one;
        t.localPosition = new Vector3(at.x, at.y, t.localPosition.z);
        t.localRotation = Quaternion.Euler(0f, 0f, degrees);
        t.localScale = new Vector3(s.x, s.y, 1f);
        if (!keys.TryGetValue(t, out var k)) keys[t] = k = new Keys();
        k.pos.Add(at);
        k.rot.Add(degrees);
        k.scale.Add(s);
    }

    static void Show(GameObject go, bool on)
    {
        go.SetActive(on);
        if (!shown.TryGetValue(go, out var list)) shown[go] = list = new List<float>();
        list.Add(on ? 1f : 0f);
    }

    static void Alpha(Transform t, float a)
    {
        var r = t.GetComponent<SpriteRenderer>();
        var c = r.color;
        r.color = new Color(c.r, c.g, c.b, a);
        if (!alphas.TryGetValue(r, out var list)) alphas[r] = list = new List<float>();
        list.Add(a);
    }

    static AnimationClip Bake(Stage s)
    {
        keys.Clear();
        shown.Clear();
        alphas.Clear();
        int frames = Mathf.RoundToInt(Duration * Fps);
        var x = new float[frames + 2];
        var walked = new float[frames + 2];
        for (int f = 0; f <= frames + 1; f++)
        {
            x[f] = BaconX(f / Fps);
            walked[f] = f == 0 ? 0f : walked[f - 1] + (Walking(f / Fps) ? Mathf.Abs(x[f] - x[f - 1]) : 0f);
        }
        float peelStart = BaconX(Slip) + 0.3f;
        var mouth = new Vector2[frames + 1];
        var camX = new float[frames + 1];

        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;
            PoseBacon(s, t, x[f], walked[f], f > 0 ? Mathf.Abs(x[f + 1] - x[f - 1]) * Fps / 2f : 0f);
            mouth[f] = s.head.TransformPoint(new Vector3(0.18f, 0.23f, 0f)) - s.root.position;
            var headTop = (Vector2)(s.head.TransformPoint(new Vector3(0f, 0.47f, 0f)) - s.root.position) + new Vector2(0f, 0.75f);

            // The camera follows him a little behind, along the street only
            // (on his back, his head lies a body's length behind his hips: frame the middle of him)
            float lying = Smooth(Slip + 0.3f, Land, t) * (1f - Smooth(GetUp, Up + 0.3f, t));
            float target = Mathf.Clamp(x[f] + 0.3f - 0.9f * lying, -4.6f, 4.35f);
            camX[f] = f == 0 ? target : Mathf.Lerp(camX[f - 1], target, 0.12f);
            Set(s.camera, new Vector2(camX[f], CamY));
            // Further layers move with the camera, so they slide past slower
            Set(s.trees, new Vector2(camX[f] * 0.55f, 0f));
            Set(s.city, new Vector2(camX[f] * 0.82f, 0.05f));
            for (int i = 0; i < s.clouds.Length; i++)
                Set(s.clouds[i], new Vector2(camX[f] * 0.93f + new[] { -2.2f, -0.4f, 1.3f, 2.9f }[i] + 0.06f * t, new[] { 3.1f, 2.5f, 3.4f, 2.8f }[i]), 0f,
                    Vector2.one * new[] { 1f, 0.75f, 0.9f, 0.7f }[i]);

            // The peel: under his foot, kicked up spinning, landing further on
            if (t < Slip) Set(s.peel, new Vector2(peelStart, Line));
            else
            {
                float u = Mathf.Clamp01((t - Slip) / 0.8f);
                Set(s.peel, new Vector2(Mathf.Lerp(peelStart, PeelLands, u), Line + 1.3f * Mathf.Sin(Mathf.PI * u)), -720f * Mathf.SmoothStep(0f, 1f, u));
            }

            // Whistled notes float up from his lips and fade
            for (int i = 0; i < s.notes.Length; i++)
            {
                float born = 0.25f + 0.47f * i, age = t - born;
                bool on = age >= 0f && age <= 1f;
                var from = mouth[Mathf.Clamp(Mathf.RoundToInt(born * Fps), 0, f)];
                Show(s.notes[i].gameObject, on);
                Set(s.notes[i], from + new Vector2(0.3f * age + 0.07f * Mathf.Sin(7f * age), 0.85f * age), 15f * Mathf.Sin(5f * age),
                    Vector2.one * (0.8f + 0.3f * Mathf.Clamp01(age / 0.3f)));
                Alpha(s.notes[i], Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01((1f - age) / 0.3f));
            }

            // Stars round his head while he lies there
            for (int i = 0; i < s.stars.Length; i++)
            {
                float a = 5.5f * t + i * 2.0944f;
                Show(s.stars[i].gameObject, t > Land + 0.05f && t < Up);
                Set(s.stars[i], headTop + new Vector2(0.42f * Mathf.Cos(a), 0.13f * Mathf.Sin(a)), 200f * t, Vector2.one * (1f - 0.15f * Mathf.Sin(a)));
                Alpha(s.stars[i], 1f - Smooth(GetUp, Up, t));
            }

            // Dust where he lands and where the cover lands
            for (int i = 0; i < s.dust.Length; i++)
            {
                bool first = i < 3;
                float at = first ? Land : Clang, age = t - at, life = first ? 0.6f : 0.45f;
                float dx = new[] { -0.9f, -0.1f, 0.7f }[i % 3];
                Vector2 c = first ? new Vector2(x[f] - 0.4f, Line) : new Vector2(HoleX, Line - 0.05f);
                Show(s.dust[i].gameObject, age >= 0f && age <= life);
                Set(s.dust[i], c + new Vector2(dx * (1f + 0.6f * Mathf.Max(0f, age)), 0.25f * Mathf.Max(0f, age)), 0f,
                    Vector2.one * (0.5f + 0.9f * Mathf.Clamp01(age / life)));
                Alpha(s.dust[i], 1f - Mathf.Clamp01(age / life));
            }

            // The sound words pop up
            Pop(s.bang, t, Land, Land + 0.6f, new Vector2(x[f] - 0.6f, Line + 2.0f), -8f);
            Pop(s.clang, t, Clang, Duration + 1f, new Vector2(HoleX + 0.55f, Line + 1.5f), 6f);

            PoseCover(s, t);
        }

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, ClipPath);
        }
        clip.ClearCurves();
        clip.frameRate = Fps;
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        bool Varies(IEnumerable<float> values) { float first = values.First(); return values.Any(v => Mathf.Abs(v - first) >= 1e-6f); }
        void Add(string path, System.Type type, string property, IList<float> values, bool stepped = false, bool always = false)
        {
            if (!always && !Varies(values)) return;
            var k = new Keyframe[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                float slopeIn = i > 0 ? (values[i] - values[i - 1]) * Fps : 0f, slopeOut = i + 1 < values.Count ? (values[i + 1] - values[i]) * Fps : 0f;
                k[i] = stepped ? new Keyframe(i / Fps, values[i], float.PositiveInfinity, float.PositiveInfinity) : new Keyframe(i / Fps, values[i], slopeIn, slopeOut);
            }
            bindings.Add(EditorCurveBinding.FloatCurve(path, type, property));
            curves.Add(new AnimationCurve(k));
        }
        foreach (var (t, k) in keys)
        {
            string path = AnimationUtility.CalculateTransformPath(t, s.root);
            // A vector keyed in part plays with its other parts at 0 (his feet left the ground, the camera came up to
            // z = 0 and saw nothing), so a vector that moves at all is keyed whole
            if (Varies(k.pos.Select(p => p.x)) || Varies(k.pos.Select(p => p.y)))
            {
                Add(path, typeof(Transform), "m_LocalPosition.x", k.pos.Select(p => p.x).ToList(), always: true);
                Add(path, typeof(Transform), "m_LocalPosition.y", k.pos.Select(p => p.y).ToList(), always: true);
                bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.z"));
                curves.Add(AnimationCurve.Constant(0f, Duration, t.localPosition.z));
            }
            if (Varies(k.scale.Select(p => p.x)) || Varies(k.scale.Select(p => p.y)))
            {
                Add(path, typeof(Transform), "m_LocalScale.x", k.scale.Select(p => p.x).ToList(), always: true);
                Add(path, typeof(Transform), "m_LocalScale.y", k.scale.Select(p => p.y).ToList(), always: true);
                bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale.z"));
                curves.Add(AnimationCurve.Constant(0f, Duration, 1f));
            }
            if (k.rot.Any(r => Mathf.Abs(r - k.rot[0]) > 1e-4f))
            {
                // Quaternions from continuous angles, so a spin past 360 never flips sign between frames
                bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.x"));
                curves.Add(AnimationCurve.Constant(0f, Duration, 0f));
                bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.y"));
                curves.Add(AnimationCurve.Constant(0f, Duration, 0f));
                Add(path, typeof(Transform), "m_LocalRotation.z", k.rot.Select(r => Mathf.Sin(r * Mathf.Deg2Rad / 2f)).ToList(), always: true);
                Add(path, typeof(Transform), "m_LocalRotation.w", k.rot.Select(r => Mathf.Cos(r * Mathf.Deg2Rad / 2f)).ToList(), always: true);
            }
        }
        foreach (var (go, list) in shown) Add(AnimationUtility.CalculateTransformPath(go.transform, s.root), typeof(GameObject), "m_IsActive", list, stepped: true);
        foreach (var (r, list) in alphas) Add(AnimationUtility.CalculateTransformPath(r.transform, s.root), typeof(SpriteRenderer), "m_Color.a", list);
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
        EditorUtility.SetDirty(clip);

        // The scene at the first frame
        foreach (var (t, k) in keys)
        {
            t.localPosition = new Vector3(k.pos[0].x, k.pos[0].y, t.localPosition.z);
            t.localRotation = Quaternion.Euler(0f, 0f, k.rot[0]);
            t.localScale = new Vector3(k.scale[0].x, k.scale[0].y, 1f);
        }
        foreach (var (go, list) in shown) go.SetActive(list[0] > 0.5f);
        foreach (var (r, list) in alphas) r.color = new Color(r.color.r, r.color.g, r.color.b, list[0]);
        return clip;
    }

    static void Pop(Transform burst, float t, float from, float to, Vector2 at, float tilt)
    {
        float age = t - from;
        Show(burst.gameObject, age >= 0f && t < to);
        float size = age < 0.1f ? 1.2f * Mathf.Clamp01(age / 0.1f) : 1.2f - 0.2f * Mathf.Clamp01((age - 0.1f) / 0.1f);
        Set(burst, at, tilt + 3f * Mathf.Sin(age * 20f) * Mathf.Exp(-age * 4f), Vector2.one * Mathf.Max(size, 0.001f));
    }

    // His pose at time t: hips at x along the street, `walked` units walked so far (the stride), `speed` units/s
    static void PoseBacon(Stage s, float t, float x, float walked, float speed)
    {
        float phase = walked / 1.5f; // one stride cycle every 1.5 units
        float sw = Mathf.Sin(2f * Mathf.PI * phase), w = Walking(t) ? Mathf.Clamp01(speed / 1.0f) : 0f;
        float legF = 28f * sw * w, legB = -28f * sw * w, armF = -24f * sw * w, armB = 24f * sw * w;
        float hipY = HipH - 0.05f * w * (1f - Mathf.Abs(Mathf.Cos(2f * Mathf.PI * phase)));
        float tilt = -3f * w, headTurn = 0f;
        var squash = Vector2.one;
        string face;

        if (t < Slip)
        {
            face = "Whistle";
            headTurn = 4f * Mathf.Sin(t * 7f);
        }
        else if (t < Land)
        {
            // Off his feet: the front foot shoots up, he flips over backwards and comes down flat on his back
            face = "Shock";
            float u = Mathf.InverseLerp(Slip, Land, t);
            tilt = 450f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.93f, u));
            hipY = Mathf.Lerp(HipH, LieH, Smooth(0.75f, 1f, u)) + 1.5f * Mathf.Sin(Mathf.PI * u);
            float kick = Mathf.Clamp01(u / 0.15f), down = Smooth(0.85f, 1f, u);
            legF = u < 0.15f ? Mathf.Lerp(legF, 95f, kick) : Mathf.Lerp(60f + 30f * Mathf.Sin(24f * t), 18f, down);
            legB = u < 0.15f ? Mathf.Lerp(legB, 30f, kick) : Mathf.Lerp(20f + 30f * Mathf.Sin(24f * t + 2f), 6f, down);
            armF = Mathf.Lerp(Mathf.Lerp(armF, 130f + 40f * Mathf.Sin(26f * t), kick), 100f, down);
            armB = Mathf.Lerp(Mathf.Lerp(armB, -120f + 40f * Mathf.Sin(26f * t + 1.3f), kick), -85f, down);
        }
        else if (t < GetUp)
        {
            // Flat on his back, seeing stars; squashed for a moment as he hits the ground
            face = "Dizzy";
            tilt = 450f;
            hipY = LieH;
            float twitch = 10f * Mathf.Sin((t - Land) * 30f) * Mathf.Exp(-(t - Land) * 8f);
            legF = 18f + twitch;
            legB = 6f - twitch;
            armF = 100f;
            armB = -85f;
            headTurn = 5f * Mathf.Sin(t * 6f);
            float k = Mathf.Clamp01(1f - (t - Land) / 0.25f);
            squash = new Vector2(1f + 0.16f * k * k, 1f - 0.16f * k * k);
        }
        else if (t < Up)
        {
            face = "Dizzy";
            float v = Mathf.InverseLerp(GetUp, Up, t), e = EaseOutBack(v);
            tilt = Mathf.Lerp(450f, 360f, e);
            hipY = Mathf.Lerp(LieH, HipH, Mathf.SmoothStep(0f, 1f, v));
            legF = Mathf.Lerp(18f, 0f, v);
            legB = Mathf.Lerp(6f, 0f, v);
            armF = Mathf.Lerp(100f, 0f, v);
            armB = Mathf.Lerp(-85f, 0f, v);
        }
        else if (t < LookDown)
        {
            // Shakes his head clear, walks on
            face = "Normal";
            tilt += 360f;
            headTurn = t < Walk2 ? 14f * Mathf.Sin((t - Up) * 45f) * (1f - Mathf.InverseLerp(Up, Walk2, t)) : 0f;
        }
        else if (t < StepOver)
        {
            // There it is again: a look down at the peel, a smug look
            face = "Smug";
            tilt = 360f;
            headTurn = -16f * Smooth(LookDown, LookDown + 0.15f, t);
        }
        else if (t < Brag)
        {
            // Over it with a big high step, arms out
            face = "Smug";
            float u = Mathf.InverseLerp(StepOver, Brag, t);
            tilt = 360f + 6f * Bump(0f, 1f, u);
            hipY = HipH + 0.12f * Bump(0f, 1f, u);
            legF = 80f * Bump(0f, 0.55f, u);
            legB = -30f * Bump(0.2f, 0.55f, u) + 70f * Bump(0.5f, 1f, u);
            armF = 75f * Bump(0f, 1f, u);
            armB = -65f * Bump(0f, 1f, u);
            headTurn = -16f * (1f - Smooth(0.6f, 1f, u));
        }
        else if (t < Hang)
        {
            // A wink at us, and on he goes
            face = "Wink";
            tilt += 360f;
            headTurn = 6f;
            if (t < Onto) armF = 35f * Smooth(Brag, Brag + 0.1f, t);
        }
        else
        {
            // Over the hole: hangs there a moment, "o_o", then drops in
            face = "Shock";
            tilt = 360f;
            float drop = Mathf.Max(0f, t - Drop);
            hipY = HipH - 64f * drop * drop;
            float up = Smooth(Drop, Drop + 0.08f, t);
            legF = Mathf.Lerp(22f, 5f, up);
            legB = Mathf.Lerp(-22f, -5f, up);
            armF = Mathf.Lerp(60f * Smooth(Hang, Hang + 0.06f, t), 165f, up);
            armB = Mathf.Lerp(-60f * Smooth(Hang, Hang + 0.06f, t), -165f, up);
        }

        Show(s.bacon.gameObject, t < Drop + 0.3f);
        Set(s.bacon, new Vector2(x, Line), 0f, squash);
        Set(s.pelvis, new Vector2(0f, hipY), tilt);
        Set(s.legF, new Vector2(0.07f, 0f), legF);
        Set(s.legB, new Vector2(-0.06f, 0f), legB);
        Set(s.torso, Vector2.zero, 0f);
        Set(s.armF, new Vector2(0.17f, 0.98f), armF);
        Set(s.armB, new Vector2(-0.24f, 0.98f), armB);
        Set(s.head, new Vector2(0.02f, 1.07f), headTurn);
        foreach (var (name, go) in s.faces) Show(go, name == face);
    }

    // The manhole cover: standing by the hole, it rolls over to it, falls flat on it with a clang and wobbles; then his
    // eyes look out through its slots and blink
    static void PoseCover(Stage s, float t)
    {
        float rollTo = (CoverX - HoleX) / CoverR * Mathf.Rad2Deg, rest = Mathf.Round(rollTo / 180f) * 180f;
        if (t < Tip) Set(s.cover, new Vector2(CoverX, Line + CoverR));
        else
        {
            float u = Mathf.InverseLerp(Tip, Clang, t), roll = Smooth(0f, 0.6f, u), fall = Smooth(0.6f, 1f, u);
            float x = Mathf.Lerp(CoverX, HoleX, roll);
            float wobble = t > Clang ? 4f * Mathf.Sin((t - Clang) * 50f) * Mathf.Exp(-(t - Clang) * 10f) : 0f;
            Set(s.cover, new Vector2(x, Mathf.Lerp(Line + CoverR, Line, fall)), Mathf.Lerp(roll * rollTo, rest, fall) + wobble,
                new Vector2(Mathf.Lerp(1f, HoleRx / CoverR, fall), Mathf.Lerp(1f, HoleRy / CoverR, fall)));
        }
        bool eyes = t > Clang + 0.12f && !(t > Clang + 0.2f && t < Clang + 0.24f);
        Show(s.coverEyes.gameObject, eyes);
        Set(s.coverEyes, new Vector2(HoleX, Line + 0.005f));
    }
}
