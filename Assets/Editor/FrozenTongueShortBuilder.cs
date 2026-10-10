using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Timeline;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

// Short: the Roblox half of the user's "dog got its tongue stuck" clip (Brookhaven frames, 2026-10-10). Bacon, in his
// winter jacket, walks down a snowy street and stops: a little white dog has its tongue frozen to a metal gate post. He
// squats, grabs it and hauls it back, the tongue stretches and pops free, the dog runs off. He looks round to make sure
// nobody is watching (the user), licks the post himself and is stuck, tugging, his tongue stretched to the post, "o_o".
// The user wants it to look expensive, not slapdash: textured snow, pavement and brick (Textures/, made by a script),
// detailed houses with lit windows, fairy lights and icicles, snowy pines, a wrought-iron fence, falling snow, a sky,
// a low winter sun and graded post. ~11.5 s.
// Tools/Shorts/Build Frozen Tongue Short -> Assets/Scenes/Shorts_FrozenTongue.unity + Timeline; Build(record: true) adds a
// Recorder track: Play writes PNG frames and ShortsFrameEncoder makes Recordings/Shorts_FrozenTongue_<take>.mp4.
public static class FrozenTongueShortBuilder
{
    const float Fps = 30f, Duration = 11.5f;
    const string ScenePath = "Assets/Scenes/Shorts_FrozenTongue.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_FrozenTongue.playable";
    const string ClipFolder = "Assets/Animations";
    const string Folder = "Assets/Shorts/FrozenTongue";
    const string Textures = Folder + "/Textures";
    const string BaconPrefab = MinifigCharacterBuilder.PrefabFolder + "/bacon_winter.prefab"; // in his winter jacket (the user)
    const int VideoWidth = 1080, VideoHeight = 1920;
    const string RecordingFolder = "Recordings/Shorts_FrozenTongue_<Take>" + ShortsFrameEncoder.FramesSuffix; // relative to the project folder

    // The beats (seconds): walking up, "o?", the dog stuck (wide), squat and grab, haul, pop, a look round (nobody
    // about?), up to the post, the lick, stuck and tugging
    const float Stop = 1.2f, DogShot = 1.9f, Grab = 3.2f, Haul = 3.55f, Pop = 4.25f, LookRound = 5.0f, Approach = 6.65f,
        Lick = 7.35f, Touch = 7.65f, Stuck = 7.8f;

    // The gate post stands at the origin at the end of the fence (which runs off along -X); both tongues freeze to its
    // +X face. Bacon and the dog face -X, towards it; the street is on the -Z side, the houses on +Z
    const float PostHalf = 0.05f, PostHeight = 1.6f;
    const float DogX = PostHalf + 0.035f + 0.3f; // the dog's root: its mouth (0.3 ahead of it) just off the post
    const float StartX = 5.6f, StopX = 3.6f;

    static readonly string[] Bones =
    {
        "Hips", "Spine", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    };
    static readonly string[] Sides = { "Left", "Right" };
    static float Out(string side) => side == "Left" ? -1f : 1f; // the sign of Z that swings that arm out to the side

    [MenuItem("Tools/Shorts/Build Frozen Tongue Short")]
    public static void Build() => Build(record: false);

    [MenuItem("Tools/Shorts/Build Frozen Tongue Short (Record)")]
    public static void BuildForRecording() => Build(record: true);

    public static void Build(bool record)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[FrozenTongue] Exit Play Mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var f in new[] { ClipFolder, "Assets/Timelines", Folder }) RigUtility.EnsureFolder(f);
        var baconPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaconPrefab);
        if (baconPrefab == null) { Debug.LogError("[FrozenTongue] Build the characters first (Tools/Characters/Build Characters)"); return; }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildLight();
        BuildStreet();
        var snowfall = BuildSnowfall();

        // Where he squats to reach the dog and where he stands to lick the post: from his own reach
        var rig = new Rig(baconPrefab);
        calibrating = true;
        rig.Apply(BaconPose(Grab + 0.35f));
        var hands = rig.Hands;
        rig.Apply(BaconPose(Touch));
        var mouth = rig.Mouth;
        calibrating = false;
        grabX = DogX + 0.09f - hands.x;
        lickX = PostHalf + 0.012f - mouth.x;
        contact = new Vector3(PostHalf + 0.004f, mouth.y, 0f);
        Path();

        var bacon = (GameObject)PrefabUtility.InstantiatePrefab(baconPrefab, scene);
        bacon.name = "Bacon";
        bacon.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        var props = new GameObject("Props");
        props.AddComponent<Animator>();
        var dog = BuildDog(props.transform);
        Tongue(props.transform, "DogTongue");
        Tongue(props.transform, "BaconTongue");

        var camRig = new GameObject("CameraRig");
        camRig.AddComponent<Animator>();
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.SetParent(camRig.transform, false);
        camera.fieldOfView = 40f;
        camera.nearClipPlane = 0.02f;
        camera.farClipPlane = 300f;
        camera.clearFlags = CameraClearFlags.Skybox;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.stopNaN = true;

        var baconClip = BakeBacon(rig, "FrozenTongue_Bacon");
        Object.DestroyImmediate(rig.go);
        var propsClip = BakeProps(props.transform, dog, "FrozenTongue_Props");
        var cameraClip = CameraClip();

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }
        // Keep the take counter across rebuilds so old recordings aren't overwritten
        int take = 1;
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath))
            if (sub is RecorderSettings old) take = Mathf.Max(take, old.Take + 1);
        foreach (var track in timeline.GetOutputTracks().ToList()) timeline.DeleteTrack(track);
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath))
            if (sub is RecorderSettings orphan)
            {
                AssetDatabase.RemoveObjectFromAsset(orphan);
                Object.DestroyImmediate(orphan, true);
            }
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.editorSettings.frameRate = Fps;
        timeline.fixedDuration = Duration;
        var director = new GameObject("Timeline_FrozenTongue").AddComponent<PlayableDirector>();
        void Track(string name, AnimationClip clip, Animator target)
        {
            var track = timeline.CreateTrack<AnimationTrack>(null, name);
            track.trackOffset = TrackOffset.ApplySceneOffsets; // the clips key children, not the bound root
            var c = track.CreateClip(clip);
            c.start = 0;
            c.duration = Duration;
            director.SetGenericBinding(track, target);
        }
        Track("Bacon", baconClip, bacon.GetComponent<Animator>());
        Track("Props", propsClip, props.GetComponent<Animator>());
        Track("Camera", cameraClip, camRig.GetComponent<Animator>());
        // The snow falls under the Timeline's control, so scrubbing the editor shows it where it will be in the video
        var snowTrack = timeline.CreateTrack<ControlTrack>(null, "Snowfall");
        var snowClip = snowTrack.CreateDefaultClip();
        snowClip.start = 0;
        snowClip.duration = Duration;
        var control = (ControlPlayableAsset)snowClip.asset;
        control.sourceGameObject.exposedName = GUID.Generate().ToString();
        director.SetReferenceValue(control.sourceGameObject.exposedName, snowfall);
        control.updateParticle = true;
        control.particleRandomSeed = 7;
        control.active = false;
        control.updateDirector = false;
        control.updateITimeControl = false;
        if (record) AddRecorderTrack(timeline, take);
        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        director.time = 0; director.Evaluate();
        Debug.Log($"[FrozenTongue] Built {ScenePath}, {Duration} s (grab at x {grabX:F2}, hands {hands.y:F2} high; lick at x {lickX:F2}, mouth {mouth.y:F2} high)"
            + (record ? $": press Play to record take {take}" : ""));
    }

    // Recorder clips only record in Play Mode: pressing Play runs the Timeline (playOnAwake) and writes the frames
    static void AddRecorderTrack(TimelineAsset timeline, int take)
    {
        var track = timeline.CreateTrack<RecorderTrack>(null, "Recorder");
        var clip = track.CreateClip<RecorderClip>();
        clip.start = 0;
        clip.duration = Duration;
        clip.displayName = "1080x1920 30fps -> MP4";
        var settings = ScriptableObject.CreateInstance<ImageRecorderSettings>();
        settings.name = "Shorts Frames";
        settings.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.PNG;
        settings.CaptureAlpha = false;
        settings.imageInputSettings = new GameViewInputSettings { OutputWidth = VideoWidth, OutputHeight = VideoHeight };
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.FrameRate = Fps;
        settings.CapFrameRate = true;
        // Set root/folder/name separately: assigning a "folder/file" string to OutputFile drops the folder
        settings.FileNameGenerator.Root = OutputPath.Root.Project;
        settings.FileNameGenerator.Leaf = RecordingFolder;
        settings.FileNameGenerator.FileName = "frame_<Frame>";
        settings.Take = take;
        AssetDatabase.AddObjectToAsset(settings, timeline);
        ((RecorderClip)clip.asset).settings = settings;
    }

    // ---------------------------------------------------------------- light, sky, post

    // A clear winter day: a low warm sun from the front left (on the faces and the house fronts), a cool sky fill,
    // snow-bright ambient, a procedural sky, and graded post (ACES, a soft bloom on the lights, cool white balance, a
    // little vignette, the far background softly out of focus)
    static void BuildLight()
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.9f, 0.78f);
        sun.intensity = 1.7f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.62f, -0.48f, 0.62f));
        var sky = new GameObject("Sky Fill").AddComponent<Light>();
        sky.type = LightType.Directional;
        sky.color = new Color(0.68f, 0.8f, 1f);
        sky.intensity = 0.45f;
        sky.shadows = LightShadows.None;
        sky.transform.rotation = Quaternion.LookRotation(new Vector3(-0.3f, -0.8f, 0.5f));

        var skybox = Mat("Sky", Color.white, 0f, "Skybox/Procedural");
        skybox.SetFloat("_SunDisk", 2f);
        skybox.SetFloat("_SunSize", 0.035f);
        skybox.SetFloat("_AtmosphereThickness", 0.75f);
        skybox.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.95f));
        skybox.SetColor("_GroundColor", new Color(0.75f, 0.8f, 0.88f));
        skybox.SetFloat("_Exposure", 1.25f);
        RenderSettings.skybox = skybox;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.72f, 0.8f, 0.95f);
        RenderSettings.ambientEquatorColor = new Color(0.66f, 0.7f, 0.78f);
        RenderSettings.ambientGroundColor = new Color(0.62f, 0.64f, 0.7f); // light bounced off the snow
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.78f, 0.85f, 0.95f);
        RenderSettings.fogStartDistance = 25f;
        RenderSettings.fogEndDistance = 120f;

        string path = $"{Folder}/FrozenTongue_Volume.asset";
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
        T Add<T>() where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }
        Add<Tonemapping>().mode.value = TonemappingMode.ACES;
        var bloom = Add<Bloom>();
        bloom.threshold.value = 1.05f;
        bloom.intensity.value = 0.45f;
        bloom.scatter.value = 0.65f;
        var grade = Add<ColorAdjustments>();
        grade.postExposure.value = 0.35f;
        grade.contrast.value = 14f;
        grade.saturation.value = 12f;
        Add<WhiteBalance>().temperature.value = -6f;
        var vignette = Add<Vignette>();
        vignette.intensity.value = 0.22f;
        vignette.smoothness.value = 0.45f;
        var focus = Add<DepthOfField>();
        focus.mode.value = DepthOfFieldMode.Gaussian;
        focus.gaussianStart.value = 7f;
        focus.gaussianEnd.value = 30f;
        focus.gaussianMaxRadius.value = 1.2f;
        focus.highQualitySampling.value = true;
        EditorUtility.SetDirty(profile);
        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }

    // ---------------------------------------------------------------- the street

    static Material snow, snowGround, pavement, asphalt, kerb, metal, brick, siding, trim, glass, door, roofSnow, icicle, bark, pine, plinth;

    // Snow everywhere, an icy road on the -Z side, a grey pavement they stand on, a wrought-iron fence ending in the gate
    // post at the origin, a brick garage and a big white house behind it, more houses and snowy pines down the street
    static void BuildStreet()
    {
        snow = Mat("Snow", new Color(0.97f, 0.98f, 1f), 0.35f, tex: "Snow", tile: 2.5f, normal: "Snow_N");
        snowGround = Mat("SnowGround", new Color(0.97f, 0.98f, 1f), 0.35f, tex: "Snow", tile: 3f, normal: "Snow_N");
        pavement = Mat("Pavement", Color.white, 0.5f, tex: "Pavement", tile: 2.4f);
        asphalt = Mat("Road", Color.white, 0.55f, tex: "Asphalt", tile: 4f);
        kerb = Mat("Kerb", new Color(0.72f, 0.74f, 0.77f), 0.2f);
        metal = Mat("Metal", new Color(0.09f, 0.1f, 0.11f), 0.6f);
        metal.SetFloat("_Metallic", 0.6f);
        brick = Mat("Brick", Color.white, 0.15f, tex: "Brick", tile: 2f);
        siding = Mat("Siding", Color.white, 0.25f, tex: "Siding", tile: 2f);
        trim = Mat("Trim", new Color(0.95f, 0.95f, 0.94f), 0.4f);
        plinth = Mat("Plinth", new Color(0.36f, 0.37f, 0.4f), 0.2f);
        door = Mat("GarageDoor", new Color(0.83f, 0.83f, 0.82f), 0.35f);
        glass = Mat("Window", new Color(1f, 0.82f, 0.55f), 0.9f);
        Glow(glass, new Color(1f, 0.72f, 0.38f), 1.1f);
        roofSnow = snow;
        icicle = Mat("Icicle", new Color(0.82f, 0.9f, 1f), 0.95f);
        bark = Mat("Bark", new Color(0.3f, 0.2f, 0.14f), 0.1f);
        pine = Mat("Pine", new Color(0.09f, 0.24f, 0.16f), 0.15f);

        var root = new GameObject("Street").transform;
        Box(root, "Ground", new Vector3(10f, -0.05f, 0f), new Vector3(160f, 0.1f, 160f), snowGround);
        Box(root, "Road", new Vector3(10f, 0.005f, -4.6f), new Vector3(160f, 0.02f, 5.2f), asphalt);
        var line = Mat("Line", new Color(0.9f, 0.9f, 0.88f), 0.3f);
        for (int i = -40; i <= 50; i++) Box(root, "Dash", new Vector3(i * 2f, 0.017f, -4.6f), new Vector3(1.1f, 0.006f, 0.12f), line);
        Box(root, "Kerb", new Vector3(10f, 0.06f, -1.9f), new Vector3(160f, 0.14f, 0.22f), kerb);
        Box(root, "Pavement", new Vector3(10f, 0.012f, -0.75f), new Vector3(160f, 0.03f, 2.1f), pavement);
        Box(root, "FarKerb", new Vector3(10f, 0.06f, -7.3f), new Vector3(160f, 0.14f, 0.22f), kerb);
        Box(root, "FarPavement", new Vector3(10f, 0.012f, -8.4f), new Vector3(160f, 0.03f, 2f), pavement);
        // Snow banked along the kerbs, ploughed off the road
        var random = new System.Random(3);
        for (int i = 0; i < 70; i++)
        {
            float x = -14f + i * 0.55f + (float)random.NextDouble() * 0.3f;
            Heap(root, new Vector3(x, 0f, -2.15f - (float)random.NextDouble() * 0.1f), new Vector3(0.7f + (float)random.NextDouble() * 0.5f, 0.16f + (float)random.NextDouble() * 0.08f, 0.35f));
        }

        // The fence: posts with spear tips, two rails, snow along the top; the gate post at the origin, taller, with a
        // ball on top under a cap of snow
        for (float x = -6f; x < -0.2f; x += 0.3f)
        {
            Box(root, "Bar", new Vector3(x, 0.55f, 0f), new Vector3(0.028f, 1.1f, 0.028f), metal);
            Spike(root, new Vector3(x, 1.1f, 0f), 0.05f, 0.1f, metal);
        }
        for (float x = -6f; x < 0f; x += 1.8f)
        {
            Box(root, "Post", new Vector3(x, 0.62f, 0f), new Vector3(0.06f, 1.24f, 0.06f), metal);
            Ball(root, new Vector3(x, 1.27f, 0f), 0.075f, metal);
            Ball(root, new Vector3(x, 1.31f, 0f), 0.07f, snow, 0.55f);
        }
        foreach (float y in new[] { 0.16f, 0.95f })
        {
            Box(root, "Rail", new Vector3(-3f, y, 0f), new Vector3(6f, 0.04f, 0.04f), metal);
            Box(root, "RailSnow", new Vector3(-3f, y + 0.035f, 0f), new Vector3(6f, 0.035f, 0.055f), snow);
        }
        Box(root, "GatePost", new Vector3(0f, PostHeight / 2f, 0f), new Vector3(PostHalf * 2f, PostHeight, PostHalf * 2f), metal);
        Box(root, "GatePostCap", new Vector3(0f, PostHeight + 0.015f, 0f), new Vector3(0.14f, 0.03f, 0.14f), metal);
        Ball(root, new Vector3(0f, PostHeight + 0.08f, 0f), 0.1f, metal);
        Ball(root, new Vector3(0f, PostHeight + 0.13f, 0f), 0.11f, snow, 0.5f);
        Box(root, "GatePostSnow", new Vector3(0f, PostHeight + 0.04f, 0f), new Vector3(0.16f, 0.025f, 0.16f), snow);
        for (int i = 0; i < 16; i++)
            Heap(root, new Vector3(-5.8f + i * 0.37f, 0f, 0.12f + (float)random.NextDouble() * 0.25f), new Vector3(0.6f + (float)random.NextDouble() * 0.4f, 0.2f + (float)random.NextDouble() * 0.1f, 0.5f));

        // The garage and the big house behind the fence; more down the street on both sides
        House(root, new Vector3(-2.6f, 0f, 6.5f), new Vector3(5.5f, 3.2f, 5f), brick, garage: true, lights: true);
        House(root, new Vector3(4.6f, 0f, 7.5f), new Vector3(7.5f, 6.6f, 7f), siding, garage: false, lights: true);
        for (int i = 0; i < 6; i++)
        {
            House(root, new Vector3(13.5f + i * 9.5f, 0f, 7.5f), new Vector3(7.5f, 4.2f + (i % 2) * 2.4f, 7f), i % 2 == 0 ? brick : siding, garage: i % 2 == 0, lights: i < 2);
            House(root, new Vector3(6f + i * 9.5f, 0f, -14f), new Vector3(7.5f, 5f + (i % 3) * 1.2f, 7f), i % 2 == 0 ? siding : brick, garage: false, lights: i < 2, facing: -1f);
        }
        House(root, new Vector3(-11f, 0f, 7.5f), new Vector3(7.5f, 5.5f, 7f), siding, garage: false, lights: false);
        // Snowy pines in the yards and along the street
        foreach (var (x, z, s) in new[] { (1.2f, 2.6f, 1f), (-6.2f, 2.8f, 1.2f), (9.2f, 3.2f, 1.3f), (17.8f, 3.0f, 1.1f), (25f, 3.4f, 1.4f),
                     (3.2f, -11f, 1.2f), (12.5f, -10.5f, 1.3f), (21f, -11.2f, 1.1f), (-4f, -10.8f, 1.2f), (30f, 3.1f, 1.2f) })
            Pine(root, new Vector3(x, 0f, z), s);
        // Street lamps along the far side of the pavement
        var lamp = Mat("Lamp", new Color(1f, 0.93f, 0.78f), 0.5f);
        Glow(lamp, new Color(1f, 0.82f, 0.55f), 2.2f);
        for (int i = -1; i < 6; i++)
        {
            float x = 2.5f + i * 8f;
            Box(root, "LampPole", new Vector3(x, 1.6f, -1.65f), new Vector3(0.09f, 3.2f, 0.09f), metal);
            Box(root, "LampHead", new Vector3(x, 3.3f, -1.65f), new Vector3(0.26f, 0.32f, 0.26f), lamp);
            Box(root, "LampRoof", new Vector3(x, 3.5f, -1.65f), new Vector3(0.36f, 0.06f, 0.36f), metal);
            Box(root, "LampSnow", new Vector3(x, 3.545f, -1.65f), new Vector3(0.38f, 0.035f, 0.38f), snow);
        }
    }

    // A house: walls on a dark plinth, a roof slab under a thick overhanging layer of snow with icicles along the front
    // eave, a garage door with panels or a front door with a porch light, framed lit windows with sills of snow, a
    // chimney, and fairy lights along the eave on the nearer ones. `facing` +1: its front faces -Z (the street)
    static void House(Transform root, Vector3 at, Vector3 size, Material walls, bool garage, bool lights, float facing = 1f)
    {
        var house = new GameObject("House").transform;
        house.SetParent(root, false);
        float front = at.z - facing * size.z / 2f;
        Vector3 Front(float x, float y, float outBy) => new Vector3(at.x + x, y, front - facing * outBy);
        Box(house, "Walls", at + new Vector3(0f, size.y / 2f, 0f), size, walls);
        Box(house, "Plinth", at + new Vector3(0f, 0.2f, 0f), new Vector3(size.x + 0.08f, 0.4f, size.z + 0.08f), plinth);
        Box(house, "Roof", at + new Vector3(0f, size.y + 0.1f, 0f), new Vector3(size.x + 0.6f, 0.2f, size.z + 0.6f), trim);
        Box(house, "RoofSnow", at + new Vector3(0f, size.y + 0.32f, 0f), new Vector3(size.x + 0.7f, 0.26f, size.z + 0.7f), roofSnow);
        Box(house, "Chimney", at + new Vector3(size.x * 0.28f, size.y + 0.9f, size.z * 0.15f), new Vector3(0.6f, 1.2f, 0.6f), brick);
        Box(house, "ChimneySnow", at + new Vector3(size.x * 0.28f, size.y + 1.55f, size.z * 0.15f), new Vector3(0.7f, 0.12f, 0.7f), roofSnow);
        var random = new System.Random(Mathf.RoundToInt(at.x * 10f));
        for (float x = -size.x / 2f - 0.3f; x < size.x / 2f + 0.3f; x += 0.22f)
        {
            float length = 0.08f + 0.3f * Mathf.Pow((float)random.NextDouble(), 2f);
            Spike(house, Front(x, size.y + 0.19f, 0.32f), 0.035f, -length, icicle);
        }
        if (lights)
        {
            var colours = new[] { new Color(1f, 0.25f, 0.2f), new Color(1f, 0.75f, 0.25f), new Color(0.25f, 0.85f, 0.35f), new Color(0.3f, 0.55f, 1f) };
            var bulbs = colours.Select((c, i) => Glow(Mat("Bulb" + i, c, 0.6f), c, 3f)).ToArray();
            int n = 0;
            for (float x = -size.x / 2f - 0.2f; x < size.x / 2f + 0.2f; x += 0.24f, n++)
                Ball(house, Front(x, size.y - 0.05f - 0.06f * Mathf.Abs(Mathf.Sin(x * 2.6f)), 0.26f), 0.05f, bulbs[n % bulbs.Length]);
        }
        if (garage)
        {
            float w = size.x * 0.62f;
            Box(house, "GarageFrame", Front(0f, 1.2f, 0.02f), new Vector3(w + 0.24f, 2.4f + 0.12f, 0.06f), trim);
            Box(house, "GarageDoor", Front(0f, 1.15f, 0.05f), new Vector3(w, 2.3f, 0.06f), door);
            for (int i = 1; i < 5; i++) Box(house, "Panel", Front(0f, i * 0.46f, 0.085f), new Vector3(w, 0.025f, 0.02f), plinth);
            foreach (float s in new[] { -1f, 1f })
            {
                var lamp = Mat("PorchLamp", new Color(1f, 0.9f, 0.7f), 0.5f);
                Glow(lamp, new Color(1f, 0.78f, 0.45f), 2.5f);
                Box(house, "WallLamp", Front(s * (w / 2f + 0.45f), 1.9f, 0.08f), new Vector3(0.14f, 0.24f, 0.12f), lamp);
            }
            Box(house, "Driveway", new Vector3(at.x, 0.01f, front - facing * 1.5f), new Vector3(w + 0.3f, 0.02f, 3f), pavement);
        }
        else
        {
            Box(house, "DoorFrame", Front(-size.x * 0.05f, 1.15f, 0.02f), new Vector3(1.3f, 2.4f, 0.06f), trim);
            Box(house, "Door", Front(-size.x * 0.05f, 1.1f, 0.05f), new Vector3(1.05f, 2.2f, 0.06f), Mat("FrontDoor", new Color(0.18f, 0.22f, 0.3f), 0.4f));
            var lamp = Mat("PorchLamp", new Color(1f, 0.9f, 0.7f), 0.5f);
            Box(house, "PorchLamp", Front(-size.x * 0.05f + 0.85f, 2f, 0.08f), new Vector3(0.14f, 0.24f, 0.12f), lamp);
            for (int floor = 0; floor * 2.9f + 1.1f < size.y - 1.2f; floor++)
                foreach (float s in new[] { -1f, 1f })
                {
                    if (floor == 0 && s < 0f) continue; // the door's there
                    float x = s * size.x * 0.28f, y = 1.75f + floor * 2.9f;
                    Box(house, "WindowFrame", Front(x, y, 0.02f), new Vector3(1.5f, 1.5f, 0.07f), trim);
                    Box(house, "Window", Front(x, y, 0.05f), new Vector3(1.3f, 1.3f, 0.04f), glass);
                    Box(house, "MullionV", Front(x, y, 0.08f), new Vector3(0.06f, 1.3f, 0.03f), trim);
                    Box(house, "MullionH", Front(x, y, 0.08f), new Vector3(1.3f, 0.06f, 0.03f), trim);
                    Box(house, "Sill", Front(x, y - 0.8f, 0.1f), new Vector3(1.65f, 0.07f, 0.2f), trim);
                    Box(house, "SillSnow", Front(x, y - 0.74f, 0.1f), new Vector3(1.6f, 0.06f, 0.18f), snow);
                }
        }
        // Snow drifted against the front wall
        for (float x = -size.x / 2f; x < size.x / 2f; x += 0.8f)
            Heap(house, new Vector3(at.x + x + (float)random.NextDouble() * 0.3f, 0f, front - facing * 0.25f), new Vector3(1f, 0.32f, 0.6f));
    }

    // A pine: a trunk and four tiers of dark needles, each under its own cap of snow
    static void Pine(Transform root, Vector3 at, float scale)
    {
        var tree = new GameObject("Pine").transform;
        tree.SetParent(root, false);
        Box(tree, "Trunk", at + new Vector3(0f, 0.4f * scale, 0f), new Vector3(0.22f, 0.8f, 0.22f) * scale, bark);
        for (int i = 0; i < 4; i++)
        {
            float r = (1.15f - 0.24f * i) * scale, h = 1.25f * scale, y = (0.55f + 0.72f * i) * scale;
            ConePart(tree, at + new Vector3(0f, y, 0f), r, h, pine, 30f * i);
            ConePart(tree, at + new Vector3(0f, y + h * 0.42f, 0f), r * 0.74f, h * 0.6f, snow, 30f * i + 15f);
        }
        Heap(tree, at, new Vector3(1.6f, 0.25f, 1.6f) * scale);
    }

    static Material Glow(Material m, Color c, float strength)
    {
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.SetColor("_EmissionColor", c * strength);
        EditorUtility.SetDirty(m);
        return m;
    }

    // A box whose UVs run in metres on every face, so tiled textures keep their size whatever its shape
    static void Box(Transform parent, string name, Vector3 at, Vector3 size, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = at;
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static readonly Dictionary<Vector3, Mesh> boxMeshes = new Dictionary<Vector3, Mesh>();

    static Mesh BoxMesh(Vector3 size)
    {
        if (boxMeshes.TryGetValue(size, out var cached) && cached != null) return cached;
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var tris = new List<int>();
        void Face(Vector3 n, Vector3 u, Vector3 w)
        {
            int i = v.Count;
            float su = Vector3.Scale(u, size).magnitude, sw = Vector3.Scale(w, size).magnitude;
            foreach (var (a, b) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                v.Add(Vector3.Scale(n + u * a + w * b, size) * 0.5f);
                uv.Add(new Vector2(a * 0.5f * su, b * 0.5f * sw));
            }
            bool outward = Vector3.Dot(Vector3.Cross(v[i + 1] - v[i], v[i + 2] - v[i]), n) > 0f;
            tris.AddRange(outward ? new[] { i, i + 1, i + 2, i, i + 2, i + 3 } : new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
        Face(Vector3.right, Vector3.forward, Vector3.up); Face(Vector3.left, Vector3.forward, Vector3.up);
        Face(Vector3.up, Vector3.right, Vector3.forward); Face(Vector3.down, Vector3.right, Vector3.forward);
        Face(Vector3.forward, Vector3.right, Vector3.up); Face(Vector3.back, Vector3.right, Vector3.up);
        var mesh = new Mesh { name = "Box" };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        boxMeshes[size] = mesh;
        return mesh;
    }

    // A cone (8 sides, faceted like a Roblox part) standing on `at`; a negative height hangs it down (an icicle, a spike up)
    static Mesh coneMesh;

    static Mesh Cone()
    {
        if (coneMesh != null) return coneMesh;
        string path = $"{Folder}/Cone.asset";
        var v = new List<Vector3>();
        var tris = new List<int>();
        const int Sides = 8;
        for (int k = 0; k < Sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / Sides, a1 = (k + 1) * Mathf.PI * 2f / Sides;
            var p0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * 0.5f;
            var p1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * 0.5f;
            var apex = Vector3.up;
            var outward = (p0 + p1).normalized + Vector3.up * 0.5f;
            Tri(v, tris, p0, p1, apex, outward);
            Tri(v, tris, p0, p1, Vector3.zero, Vector3.down);
        }
        var mesh = new Mesh { name = "Cone" };
        mesh.SetVertices(v);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        coneMesh = RigUtility.SaveMesh(mesh, path);
        return coneMesh;
    }

    static void Tri(List<Vector3> v, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        bool ok = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) > 0f;
        tris.AddRange(ok ? new[] { i, i + 1, i + 2 } : new[] { i, i + 2, i + 1 });
    }

    static void ConePart(Transform parent, Vector3 at, float radius, float height, Material material, float turn = 0f)
    {
        var go = new GameObject("Cone");
        go.transform.SetParent(parent, true);
        go.transform.position = at;
        go.transform.rotation = Quaternion.Euler(height < 0f ? 180f : 0f, turn, 0f);
        go.transform.localScale = new Vector3(radius * 2f, Mathf.Abs(height), radius * 2f);
        go.AddComponent<MeshFilter>().sharedMesh = Cone();
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void Spike(Transform parent, Vector3 at, float width, float height, Material material) => ConePart(parent, at, width / 2f, height, material);

    static void Ball(Transform parent, Vector3 at, float diameter, Material material, float squash = 1f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Ball";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, true);
        go.transform.position = at;
        go.transform.localScale = new Vector3(diameter, diameter * squash, diameter);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // A soft heap of snow half sunk into the ground
    static void Heap(Transform parent, Vector3 at, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "SnowHeap";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, true);
        go.transform.position = at;
        go.transform.localScale = size * 2f;
        go.GetComponent<MeshRenderer>().sharedMaterial = snow;
    }

    static Material Mat(string name, Color color, float smoothness = 0.15f, string shader = "Universal Render Pipeline/Lit",
        string tex = null, float tile = 1f, string normal = null)
    {
        string path = $"{Folder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader.name != shader) material.shader = Shader.Find(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (tex != null)
        {
            material.SetTexture("_BaseMap", Texture(tex, false));
            material.SetTextureScale("_BaseMap", Vector2.one / tile);
        }
        if (normal != null)
        {
            material.SetTexture("_BumpMap", Texture(normal, true));
            material.SetFloat("_BumpScale", 0.6f);
            material.EnableKeyword("_NORMALMAP");
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D Texture(string name, bool normalMap)
    {
        string path = $"{Textures}/{name}.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type || importer.anisoLevel != 8 || importer.wrapMode != TextureWrapMode.Repeat)
        {
            importer.textureType = type;
            importer.anisoLevel = 8;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Big soft flakes drifting down over the whole set, always the same (a fixed seed; the Timeline steps it)
    static GameObject BuildSnowfall()
    {
        var go = new GameObject("Snowfall");
        go.transform.position = new Vector3(2.5f, 4.5f, -1.5f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 7;
        var main = ps.main;
        main.duration = 12f;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = 9f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.032f);
        main.startColor = new Color(1f, 1f, 1f, 0.9f);
        main.maxParticles = 8000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        var emission = ps.emission;
        emission.rateOverTime = 650f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(14f, 0.2f, 9f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.12f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.75f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);
        var wobble = ps.noise;
        wobble.enabled = true;
        wobble.strength = 0.3f;
        wobble.frequency = 0.35f;
        wobble.scrollSpeed = 0.2f;
        var flake = Mat("Flake", Color.white, 0f, "Universal Render Pipeline/Particles/Unlit");
        flake.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/Flake.png"));
        flake.SetFloat("_Surface", 1f);
        flake.SetFloat("_Blend", 0f);
        flake.SetOverrideTag("RenderType", "Transparent");
        flake.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        flake.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        flake.SetInt("_ZWrite", 0);
        flake.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        flake.renderQueue = (int)RenderQueue.Transparent;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = flake;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.minParticleSize = 0f;
        return go;
    }

    // ---------------------------------------------------------------- the dog

    // A little white chihuahua out of blocks, Roblox pet style: big ears, big shiny black eyes, a red collar with a gold
    // tag, thin legs, a curled-up tail. Built facing +Z with its feet on the ground; a "Mouth" marker for its tongue
    class Dog { public Transform root, head, mouth, tail; public Transform[] legs; }

    static Dog BuildDog(Transform parent)
    {
        var white = Mat("DogWhite", new Color(0.97f, 0.95f, 0.91f), 0.25f);
        var pink = Mat("DogPink", new Color(0.93f, 0.6f, 0.65f), 0.3f);
        var black = Mat("DogBlack", new Color(0.04f, 0.04f, 0.05f), 0.9f);
        var shine = Mat("DogShine", Color.white, 0.9f);
        var red = Mat("DogCollar", new Color(0.75f, 0.1f, 0.14f), 0.5f);
        var gold = Mat("DogTag", new Color(1f, 0.78f, 0.3f), 0.85f);
        gold.SetFloat("_Metallic", 1f);
        var dog = new Dog { root = new GameObject("Dog").transform };
        dog.root.SetParent(parent, false);
        Transform Part(Transform under, string name, PrimitiveType type, Vector3 at, Vector3 size, Material m, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(under, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }
        Transform Pivot(Transform under, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(under, false);
            t.localPosition = at;
            return t;
        }
        Part(dog.root, "Torso", PrimitiveType.Cube, new Vector3(0f, 0.22f, -0.02f), new Vector3(0.15f, 0.13f, 0.28f), white);
        Part(dog.root, "Chest", PrimitiveType.Sphere, new Vector3(0f, 0.22f, 0.1f), new Vector3(0.15f, 0.14f, 0.1f), white);
        Part(dog.root, "Rump", PrimitiveType.Sphere, new Vector3(0f, 0.22f, -0.15f), new Vector3(0.15f, 0.13f, 0.08f), white);
        Part(dog.root, "Collar", PrimitiveType.Cube, new Vector3(0f, 0.29f, 0.11f), new Vector3(0.155f, 0.03f, 0.075f), red);
        Part(dog.root, "Tag", PrimitiveType.Sphere, new Vector3(0f, 0.262f, 0.15f), new Vector3(0.026f, 0.026f, 0.01f), gold);
        dog.head = Pivot(dog.root, "Head", new Vector3(0f, 0.27f, 0.12f));
        Part(dog.head, "Skull", PrimitiveType.Sphere, new Vector3(0f, 0.08f, 0.06f), new Vector3(0.18f, 0.16f, 0.16f), white);
        Part(dog.head, "Snout", PrimitiveType.Cube, new Vector3(0f, 0.035f, 0.15f), new Vector3(0.075f, 0.06f, 0.07f), white);
        Part(dog.head, "Nose", PrimitiveType.Sphere, new Vector3(0f, 0.056f, 0.186f), new Vector3(0.034f, 0.024f, 0.02f), black);
        foreach (float s in new[] { -1f, 1f })
        {
            Part(dog.head, "Eye", PrimitiveType.Sphere, new Vector3(s * 0.045f, 0.095f, 0.128f), new Vector3(0.052f, 0.056f, 0.03f), black);
            Part(dog.head, "Glint", PrimitiveType.Sphere, new Vector3(s * 0.045f + 0.01f, 0.108f, 0.143f), new Vector3(0.015f, 0.015f, 0.006f), shine);
            var ear = Pivot(dog.head, "Ear", new Vector3(s * 0.055f, 0.14f, 0.04f));
            ear.localRotation = Quaternion.Euler(-10f, 0f, -s * 28f);
            Part(ear, "Outer", PrimitiveType.Cube, new Vector3(0f, 0.065f, 0f), new Vector3(0.075f, 0.14f, 0.02f), white);
            Part(ear, "Inner", PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.011f), new Vector3(0.048f, 0.1f, 0.005f), pink);
        }
        dog.mouth = Pivot(dog.head, "Mouth", new Vector3(0f, 0.01f, 0.18f));
        dog.legs = new Transform[4];
        int k = 0;
        foreach (float z in new[] { 0.09f, -0.12f })
            foreach (float s in new[] { -1f, 1f })
            {
                var leg = Pivot(dog.root, "Leg" + k, new Vector3(s * 0.05f, 0.17f, z));
                Part(leg, "Bone", PrimitiveType.Cube, new Vector3(0f, -0.085f, 0f), new Vector3(0.042f, 0.17f, 0.042f), white);
                Part(leg, "Paw", PrimitiveType.Sphere, new Vector3(0f, -0.165f, 0.01f), new Vector3(0.05f, 0.03f, 0.06f), white);
                dog.legs[k++] = leg;
            }
        dog.tail = Pivot(dog.root, "Tail", new Vector3(0f, 0.26f, -0.16f));
        Part(dog.tail, "Bone", PrimitiveType.Cube, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.03f, 0.12f, 0.03f), white, new Vector3(-35f, 0f, 0f));
        return dog;
    }

    static Transform Tongue(Transform parent, string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.zero;
        go.GetComponent<MeshRenderer>().sharedMaterial = Mat("Tongue", new Color(0.9f, 0.32f, 0.42f), 0.75f);
        return go.transform;
    }

    // ---------------------------------------------------------------- bacon

    class Pose
    {
        public readonly Dictionary<string, Vector3> e = new Dictionary<string, Vector3>();
        public Vector3 hips;          // added to the hips' rest position, in his own frame (his forward +Z is the world's -X)
        public Pose Rot(string bone, float x, float y, float z)
        {
            e[bone] = (e.TryGetValue(bone, out var v) ? v : Vector3.zero) + new Vector3(x, y, z);
            return this;
        }
    }

    // A posable copy of bacon facing -X at the origin: poses it, stands the lower foot on the ground, and says where his
    // mouth and hands are
    class Rig
    {
        public readonly GameObject go;
        public readonly Transform[] bones;
        readonly Vector3 hipsRest, mouthLocal;
        readonly float footRest;
        readonly Transform head, leftHand, rightHand, leftFoot, rightFoot;

        public Rig(GameObject prefab)
        {
            go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform Find(string n) => go.GetComponentsInChildren<Transform>(true).First(x => x.name == n);
            bones = Bones.Select(Find).ToArray();
            hipsRest = bones[0].localPosition;
            head = Find("Head"); leftHand = Find("LeftHand"); rightHand = Find("RightHand"); leftFoot = Find("LeftFoot"); rightFoot = Find("RightFoot");
            footRest = leftFoot.position.y;
            // The mouth: a little below the middle of the face, on the front of the head
            var headBounds = go.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "HeadMesh").bounds;
            mouthLocal = head.InverseTransformPoint(new Vector3(0f, headBounds.center.y - 0.075f, headBounds.max.z - 0.005f));
            go.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        }

        public static Quaternion Rotation(Pose p, int b) => Quaternion.Euler(p.e.TryGetValue(Bones[b], out var v) ? v : Vector3.zero);

        public void Apply(Pose p)
        {
            for (int b = 0; b < bones.Length; b++) bones[b].localRotation = Rotation(p, b);
            bones[0].localPosition = hipsRest + p.hips;
            bones[0].position += Vector3.up * (footRest - Mathf.Min(leftFoot.position.y, rightFoot.position.y));
        }

        public Vector3 HipsLocal => bones[0].localPosition;
        public Vector3 Mouth => head.TransformPoint(mouthLocal);
        public Vector3 Hands => (leftHand.position + rightHand.position) / 2f;
    }

    static bool calibrating;
    static float grabX, lickX;
    static Vector3 contact;
    static readonly List<(float t0, float t1, float x)> path = new List<(float, float, float)>();

    // His walk along the line z = 0 (world x over time): up the street, on to the dog, hauling it back, stumbling back
    // at the pop, (a look round,) up to the post, then pulling back from it in tugs
    static void Path()
    {
        path.Clear();
        path.Add((0f, Stop, StopX));
        path.Add((DogShot + 0.1f, Grab, grabX));
        path.Add((Haul, Pop, grabX + 0.32f));
        path.Add((Pop, Pop + 0.3f, grabX + 0.55f));
        path.Add((Approach, Lick, lickX));
        path.Add((Stuck + 0.15f, Stuck + 0.5f, lickX + 0.1f));
        path.Add((8.75f, 9f, lickX + 0.18f));
        path.Add((9.75f, 10f, lickX + 0.26f));
    }

    static float Ease(float t0, float u) => t0 <= 0f ? 1f - (1f - u) * (1f - u) : Mathf.SmoothStep(0f, 1f, u);

    static float BaconX(float t)
    {
        if (calibrating) return 0f;
        float x = StartX;
        foreach (var (t0, t1, to) in path)
        {
            if (t >= t1) { x = to; continue; }
            if (t > t0) x = Mathf.Lerp(x, to, Ease(t0, (t - t0) / (t1 - t0)));
            break;
        }
        return x;
    }

    // Ground covered so far, for the stride
    static float Distance(float t)
    {
        float x = StartX, d = 0f;
        foreach (var (t0, t1, to) in path)
        {
            if (t >= t1) { d += Mathf.Abs(to - x); x = to; continue; }
            if (t > t0) d += Mathf.Abs(to - x) * Ease(t0, (t - t0) / (t1 - t0));
            break;
        }
        return d;
    }

    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

    // The look round: his head to his right, then all the way to his left, then back; the eyes get there first
    static float HeadYaw(float t) => 55f * Smooth(LookRound + 0.2f, LookRound + 0.45f, t) - 110f * Smooth(LookRound + 0.75f, LookRound + 1.05f, t)
        + 55f * Smooth(LookRound + 1.3f, LookRound + 1.5f, t);

    const float Legs = 0.626f; // thigh + shin

    static Pose BaconPose(float t)
    {
        var p = new Pose();
        p.hips = new Vector3(0f, 0f, -BaconX(t));
        float squat = Smooth(Grab, Grab + 0.3f, t) * (1f - Smooth(Pop, Pop + 0.15f, t));
        float haul = Smooth(Haul, Haul + 0.25f, t) * (1f - Smooth(Pop, Pop + 0.1f, t));
        float fall = Smooth(Pop, Pop + 0.12f, t) * (1f - Smooth(Pop + 0.45f, Pop + 0.85f, t));
        float see = Smooth(Stop, Stop + 0.25f, t) * (1f - Smooth(DogShot + 0.1f, DogShot + 0.4f, t));
        float sneak = Smooth(LookRound, LookRound + 0.3f, t) * (1f - Smooth(Approach - 0.1f, Approach + 0.2f, t));
        float lean = Smooth(Lick, Touch, t);
        float stuck = Smooth(Stuck + 0.1f, Stuck + 0.4f, t);
        float tug = stuck * Mathf.Max(0f, Mathf.Sin((t - Stuck) * 7f));

        // Walking: the thighs swing (forward = -X) by the ground covered, the opposite arm with them
        if (!calibrating)
        {
            float speed = Mathf.Abs(BaconX(t + 0.02f) - BaconX(t - 0.02f)) / 0.04f;
            float w = Mathf.Clamp01(speed / 0.6f) * (1f - 0.6f * squat);
            if (w > 0f)
            {
                const float amp = 28f, knee = 40f, arm = 22f;
                float phase = Distance(t) / (4f * Legs * Mathf.Sin(amp * Mathf.Deg2Rad));
                for (int k = 0; k < 2; k++)
                {
                    float c = (phase + 0.5f * k) * 2f * Mathf.PI;
                    float swing = -amp * Mathf.Sin(c);
                    p.Rot(Sides[k] + "UpperLeg", swing * w, 0f, 0f).Rot(Sides[k] + "LowerLeg", knee * Mathf.Max(0f, Mathf.Cos(c)) * w, 0f, 0f);
                    p.Rot(Sides[k] + "UpperArm", -swing * arm / amp * w, 0f, 0f);
                }
                p.Rot("Spine", 4f * w, 0f, 0f);
            }
        }

        foreach (var s in Sides)
        {
            float o = Out(s);
            // Squatting to the dog, arms reaching forward and in; easing up a little to haul
            p.Rot(s + "UpperLeg", -75f * squat + 15f * haul, 0f, 0f).Rot(s + "LowerLeg", 85f * squat - 15f * haul, 0f, 0f).Rot(s + "Foot", -10f * squat, 0f, 0f);
            p.Rot(s + "UpperArm", -70f * squat, 0f, -o * 22f * squat).Rot(s + "LowerArm", -15f * squat, 0f, 0f);
            // Thrown off balance at the pop: arms fly out
            p.Rot(s + "UpperArm", -20f * fall, 0f, o * 75f * fall);
            // Sneaking a look round: hands held in a little, shoulders up
            p.Rot(s + "UpperArm", -12f * sneak, 0f, -o * 6f * sneak).Rot(s + "LowerArm", -25f * sneak, 0f, 0f);
            // Stuck: arms flapping
            p.Rot(s + "UpperArm", (-35f + 25f * Mathf.Sin(t * 11f + (s == "Left" ? 0f : 2f))) * stuck, 0f, o * (30f + 15f * Mathf.Sin(t * 9f)) * stuck);
        }
        float yaw = HeadYaw(t);
        p.Rot("Spine", 45f * squat - 10f * haul + 5f * haul * Mathf.Sin(t * 20f) - 18f * fall + 4f * sneak + 22f * lean - 8f * stuck - 5f * tug, 0.3f * yaw, 0f);
        p.Rot("Head", 12f * see - 25f * squat + 8f * lean - 4f * stuck, yaw, 8f * see);
        return p;
    }

    // Faces as LiveFace field values over its defaults (Chill Face); each beat's face blends in over a fifth of a second
    static readonly (float from, Dictionary<string, float> face)[] Faces =
    {
        (Stop, Face(("eyeRound", 1f), ("lids", 0f), ("brow", 1f), ("browArc", 1f), ("browRaise", 0.6f), ("smile", 0f), ("smirk", 0f),
            ("mouthOpen", 0.35f), ("mouthWidth", 0.55f), ("mouthD", 0.3f), ("lookY", -0.4f))),
        (DogShot + 0.5f, Face(("eyeRound", 0.6f), ("lids", 0.1f), ("brow", 0.8f), ("browRaise", 0.3f), ("smile", 0.3f), ("smirk", 0.2f), ("lookY", -0.5f))),
        (Grab, Face(("eyeWhite", 1f), ("lids", 0.35f), ("lidTilt", 0.6f), ("brow", 1f), ("browThick", 0.5f), ("browAngle", 0.7f), ("browRaise", -0.3f),
            ("gritted", 1f), ("mouthOpen", 0.45f), ("mouthWidth", 1.3f), ("smile", -0.2f), ("smirk", 0f), ("creases", 0.8f), ("bold", 1f))),
        (Pop, Face(("eyeRound", 1f), ("pupilSmall", 0.5f), ("lids", 0f), ("brow", 1f), ("browArc", 1f), ("browRaise", 0.8f),
            ("mouthOpen", 0.6f), ("mouthD", 0.8f), ("mouthWidth", 0.7f), ("smile", 0.2f), ("smirk", 0f))),
        // Nobody about? Narrowed, shifty eyes, a tight mouth
        (LookRound + 0.1f, Face(("lids", 0.5f), ("lidTilt", 0.25f), ("brow", 1f), ("browAngle", 0.35f), ("browRaise", -0.1f),
            ("smile", 0f), ("smirk", 0.15f), ("mouthOpen", 0f), ("mouthWidth", 0.75f))),
        // ...nobody: a sly grin
        (LookRound + 1.45f, Face(("lids", 0.4f), ("lidTilt", 0.3f), ("brow", 1f), ("browAsym", 0.8f), ("browRaise", 0.2f),
            ("smirk", 0.85f), ("smile", 0.6f), ("mouthOpen", 0.05f))),
        (Lick, Face(("lids", 0.45f), ("eyeHappy", 0.3f), ("tongueOut", 1f), ("mouthOpen", 0.35f), ("smile", 0.3f), ("smirk", 0.2f))),
        (Stuck, Face(("eyeRound", 1f), ("pupilSmall", 0.85f), ("lids", 0f), ("brow", 1f), ("browArc", 1f), ("browAngle", -0.6f), ("browRaise", 0.6f),
            ("sweat", 1f), ("mouthOpen", 0.4f), ("tongueOut", 1f), ("wobble", 0.4f), ("smile", -0.3f), ("smirk", 0f))),
    };

    static Dictionary<string, float> Face(params (string field, float value)[] set) => set.ToDictionary(s => s.field, s => s.value);

    static Dictionary<string, float> faceDefaults;

    static Dictionary<string, float> BaconFace(float t)
    {
        var face = new Dictionary<string, float>(faceDefaults);
        foreach (var (from, set) in Faces)
        {
            float w = Smooth(from - 0.1f, from + 0.12f, t);
            if (w <= 0f) continue;
            foreach (var key in face.Keys.ToList())
            {
                float target = set.TryGetValue(key, out var v) ? v : faceDefaults[key];
                face[key] = Mathf.Lerp(face[key], target, w);
            }
        }
        // "o?": a couple of words at the dog
        if (t > Stop + 0.15f && t < DogShot) face["mouthOpen"] += 0.25f * Mathf.Abs(Mathf.Sin((t - Stop) * 13f));
        // The look round: the eyes go first, the way the head is about to turn (his right is -X on the face)
        if (t > LookRound && t < Approach) face["lookX"] = Mathf.Clamp(-HeadYaw(t + 0.12f) / 55f * 0.9f, -1f, 1f);
        float blink = BlinkAt(t, 0.7f) + BlinkAt(t, LookRound + 1.6f);
        face["eyeOpen"] *= 1f - Mathf.Clamp01(blink);
        return face;
    }

    static float BlinkAt(float t, float at) { float d = t - at; return d < 0f || d > 0.16f ? 0f : d < 0.06f ? d / 0.06f : 1f - (d - 0.06f) / 0.1f; }

    static Vector3[] baconMouth, baconHands;

    static AnimationClip BakeBacon(Rig rig, string name)
    {
        var liveFace = rig.go.GetComponentInChildren<LiveFace>(true);
        string facePath = AnimationUtility.CalculateTransformPath(liveFace.transform, rig.go.transform);
        faceDefaults = typeof(LiveFace).GetFields().Where(f => f.FieldType == typeof(float)).ToDictionary(f => f.Name, f => (float)f.GetValue(liveFace));
        var paths = rig.bones.Select(b => AnimationUtility.CalculateTransformPath(b, rig.go.transform)).ToArray();

        int frames = Mathf.RoundToInt(Duration * Fps);
        var rotations = new Quaternion[rig.bones.Length, frames + 1];
        var hips = new Vector3[frames + 1];
        var faces = new Dictionary<string, float>[frames + 1];
        baconMouth = new Vector3[frames + 1];
        baconHands = new Vector3[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;
            var pose = BaconPose(t);
            rig.Apply(pose);
            for (int b = 0; b < rig.bones.Length; b++)
            {
                var q = Rig.Rotation(pose, b);
                if (f > 0 && Quaternion.Dot(q, rotations[b, f - 1]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                rotations[b, f] = q;
            }
            hips[f] = rig.HipsLocal;
            baconMouth[f] = rig.Mouth;
            baconHands[f] = rig.Hands;
            faces[f] = BaconFace(t);
        }

        var clip = NewClip(name);
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        for (int b = 0; b < rig.bones.Length; b++)
        {
            int bone = b;
            AddCurve(bindings, curves, paths[b], typeof(Transform), "m_LocalRotation.x", frames, f => rotations[bone, f].x);
            AddCurve(bindings, curves, paths[b], typeof(Transform), "m_LocalRotation.y", frames, f => rotations[bone, f].y);
            AddCurve(bindings, curves, paths[b], typeof(Transform), "m_LocalRotation.z", frames, f => rotations[bone, f].z);
            AddCurve(bindings, curves, paths[b], typeof(Transform), "m_LocalRotation.w", frames, f => rotations[bone, f].w);
        }
        AddCurve(bindings, curves, paths[0], typeof(Transform), "m_LocalPosition.x", frames, f => hips[f].x);
        AddCurve(bindings, curves, paths[0], typeof(Transform), "m_LocalPosition.y", frames, f => hips[f].y);
        AddCurve(bindings, curves, paths[0], typeof(Transform), "m_LocalPosition.z", frames, f => hips[f].z);
        foreach (var field in faces[0].Keys.ToList())
            AddCurve(bindings, curves, facePath, typeof(LiveFace), field, frames, f => faces[f][field]);
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
        return clip;
    }

    // ---------------------------------------------------------------- the dog and the tongues

    // The dog: tugging at its frozen tongue, carried back with bacon's hands while he hauls, dropped at the pop, turning
    // and running off; the tongues stretched between a mouth and the post
    static AnimationClip BakeProps(Transform props, Dog dog, string name)
    {
        int frames = Mathf.RoundToInt(Duration * Fps);
        int haulFrame = Mathf.RoundToInt(Haul * Fps);
        var dogStart = new Vector3(DogX, 0f, 0f);
        Vector3 popAt = dogStart;
        var keyed = new List<Transform> { dog.root, dog.head, dog.tail };
        keyed.AddRange(dog.legs);
        var dogTongue = props.Find("DogTongue");
        var baconTongue = props.Find("BaconTongue");
        keyed.Add(dogTongue);
        keyed.Add(baconTongue);
        var pos = keyed.ToDictionary(k => k, k => new Vector3[frames + 1]);
        var rot = keyed.ToDictionary(k => k, k => new Quaternion[frames + 1]);
        var scale = keyed.ToDictionary(k => k, k => new Vector3[frames + 1]);
        Vector3 dogContact = Vector3.zero;

        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;
            // Where the dog is and which way it faces
            Vector3 at;
            float yaw = -90f, headPitch = 0f, run = 0f;
            float jerk = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 9f)), 3f); // sharp little tugs back
            if (t < Haul)
            {
                at = dogStart + new Vector3(0.025f * jerk, 0f, 0f);
                headPitch = -10f * jerk;
            }
            else if (t < Pop)
            {
                at = dogStart + (baconHands[f] - baconHands[haulFrame]);
                at.y = Mathf.Max(0f, at.y);
                headPitch = -15f;
                popAt = at;
            }
            else
            {
                float drop = Mathf.Clamp01((t - Pop) / 0.15f);
                at = popAt + new Vector3(0.08f * drop, 0f, 0f);
                at.y = popAt.y * (1f - drop * drop);
                yaw = Mathf.Lerp(-90f, 90f, Smooth(Pop + 0.15f, Pop + 0.4f, t));
                float dash = Mathf.Clamp01((t - (Pop + 0.35f)) / 0.6f);
                at.x += 3f * dash * dash + 1.2f * dash;
                run = t > Pop + 0.3f ? 1f : 0f;
                at.y += run * 0.03f * Mathf.Abs(Mathf.Sin(t * 22f));
                if (t > LookRound) at = new Vector3(12f, 0f, 0f);
            }
            dog.root.localPosition = at;
            dog.root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            dog.head.localRotation = Quaternion.Euler(headPitch, 0f, 0f);
            // Legs: braced while stuck, dangling while carried, galloping off
            for (int k = 0; k < 4; k++)
            {
                bool front = k < 2;
                float angle = t < Haul ? (front ? -18f : 12f) * (0.6f + 0.4f * jerk)
                    : t < Pop ? 15f * Mathf.Sin(t * 14f + k)
                    : run * 45f * Mathf.Sin(t * 22f + (front ? 0f : 2.2f) + (k % 2) * 0.6f);
                dog.legs[k].localRotation = Quaternion.Euler(angle, 0f, 0f);
            }
            dog.tail.localRotation = Quaternion.Euler(0f, 0f, 28f * Mathf.Sin(t * (t < Pop ? 18f : 26f)));

            // The dog's tongue: frozen to the post until the pop, then snapping back to a little tip
            var mouth = dog.mouth.position;
            if (f == 0) dogContact = new Vector3(PostHalf + 0.004f, mouth.y, 0f);
            if (t < Pop) Strip(dogTongue, mouth, dogContact, 0.03f, 0.008f);
            else
            {
                float back = Mathf.Clamp01((t - Pop) / 0.08f);
                var tip = Vector3.Lerp(dogContact, mouth + dog.mouth.forward * 0.025f + Vector3.down * 0.01f, back);
                if (t > LookRound) tip = mouth;
                Strip(dogTongue, mouth, tip, 0.03f, 0.008f);
            }

            // Bacon's tongue: out to the post as he leans in, then stuck there whatever he does
            var bm = baconMouth[f];
            if (t < Lick + 0.1f) Strip(baconTongue, bm, bm, 0f, 0f);
            else if (t < Touch) Strip(baconTongue, bm, Vector3.Lerp(bm, contact, Smooth(Lick + 0.1f, Touch, t)), 0.055f, 0.014f);
            else Strip(baconTongue, bm, contact, 0.055f, 0.014f);

            foreach (var k in keyed)
            {
                pos[k][f] = k.localPosition;
                var q = k.localRotation;
                if (f > 0 && Quaternion.Dot(q, rot[k][f - 1]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                rot[k][f] = q;
                scale[k][f] = k.localScale;
            }
        }

        var clip = NewClip(name);
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        foreach (var k in keyed)
        {
            var path = AnimationUtility.CalculateTransformPath(k, props);
            var p = pos[k]; var r = rot[k]; var s = scale[k];
            AddCurve(bindings, curves, path, typeof(Transform), "m_LocalPosition.x", frames, f => p[f].x);
            AddCurve(bindings, curves, path, typeof(Transform), "m_LocalPosition.y", frames, f => p[f].y);
            AddCurve(bindings, curves, path, typeof(Transform), "m_LocalPosition.z", frames, f => p[f].z);
            AddCurve(bindings, curves, path, typeof(Transform), "m_LocalRotation.x", frames, f => r[f].x);
            AddCurve(bindings, curves, path, typeof(Transform), "m_LocalRotation.y", frames, f => r[f].y);
            AddCurve(bindings, curves, path, typeof(Transform), "m_LocalRotation.z", frames, f => r[f].z);
            AddCurve(bindings, curves, path, typeof(Transform), "m_LocalRotation.w", frames, f => r[f].w);
            if (k == dogTongue || k == baconTongue)
            {
                AddCurve(bindings, curves, path, typeof(Transform), "m_LocalScale.x", frames, f => s[f].x);
                AddCurve(bindings, curves, path, typeof(Transform), "m_LocalScale.y", frames, f => s[f].y);
                AddCurve(bindings, curves, path, typeof(Transform), "m_LocalScale.z", frames, f => s[f].z);
            }
        }
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
        return clip;
    }

    // A flat tongue (a stretched box) from `from` to `to`, `width` across and `thick` deep; none if they meet
    static void Strip(Transform tongue, Vector3 from, Vector3 to, float width, float thick)
    {
        var d = to - from;
        if (d.magnitude < 0.003f || width <= 0f) { tongue.localScale = Vector3.zero; tongue.localPosition = from; tongue.localRotation = Quaternion.identity; return; }
        tongue.localPosition = (from + to) / 2f;
        tongue.localRotation = Quaternion.LookRotation(d, Vector3.up);
        tongue.localScale = new Vector3(width, thick, d.magnitude + thick);
    }

    // ---------------------------------------------------------------- camera

    // Cuts between the beats; slow drifts and push-ins within them
    static (Vector3 at, Vector3 look) Shot(float t)
    {
        if (t < DogShot) // up the street towards us; he stops and says something at what he sees
            return (Vector3.Lerp(new Vector3(1.3f, 1.42f, -0.32f), new Vector3(1.6f, 1.42f, -0.3f), t / DogShot), new Vector3(StopX + 0.3f, 1.3f, 0f));
        if (t < LookRound) // wide, side on: the dog stuck to the post; bacon comes in, hauls, the tongue pops
        {
            float k = Smooth(DogShot, LookRound, t);
            return (new Vector3(Mathf.Lerp(0.6f, 0.75f, k), 0.95f, -3.7f), new Vector3(Mathf.Lerp(0.6f, 0.75f, k), 0.8f, 0f));
        }
        if (t < Approach) // from in front: he looks round, nobody about
        {
            float bx = grabX + 0.55f;
            return (Vector3.Lerp(new Vector3(bx - 1.95f, 1.45f, -1.1f), new Vector3(bx - 1.75f, 1.45f, -1f), Smooth(LookRound, Approach, t)), new Vector3(bx, 1.3f, 0f));
        }
        if (t < Stuck) // three-quarters from in front: up to the post and the lick
            return (Vector3.Lerp(new Vector3(-1.3f, 1.45f, -1.35f), new Vector3(-1.15f, 1.42f, -1.2f), Smooth(Approach, Stuck, t)), new Vector3(lickX - 0.05f, 1.2f, 0f));
        // Stuck: pushing in slowly on his face and the tongue
        var focus = new Vector3(contact.x + 0.2f, contact.y + 0.05f, 0f);
        return (Vector3.Lerp(new Vector3(-0.8f, 1.45f, -1.15f), new Vector3(-0.45f, 1.42f, -0.8f), Smooth(Stuck, Duration, t)), focus);
    }

    static AnimationClip CameraClip()
    {
        var clip = NewClip("FrozenTongue_Camera");
        int frames = Mathf.RoundToInt(Duration * Fps);
        var pos = new Vector3[frames + 1];
        var rot = new Quaternion[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            var (at, look) = Shot(f / Fps);
            pos[f] = at;
            rot[f] = Quaternion.LookRotation(look - at);
            if (f > 0 && Quaternion.Dot(rot[f], rot[f - 1]) < 0f) rot[f] = new Quaternion(-rot[f].x, -rot[f].y, -rot[f].z, -rot[f].w);
        }
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        void Key(string property, Func<int, float> value) => AddCurve(bindings, curves, "Main Camera", typeof(Transform), property, frames, value, true);
        Key("m_LocalPosition.x", f => pos[f].x); Key("m_LocalPosition.y", f => pos[f].y); Key("m_LocalPosition.z", f => pos[f].z);
        Key("m_LocalRotation.x", f => rot[f].x); Key("m_LocalRotation.y", f => rot[f].y); Key("m_LocalRotation.z", f => rot[f].z); Key("m_LocalRotation.w", f => rot[f].w);
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
        return clip;
    }

    // ---------------------------------------------------------------- baking

    static AnimationClip NewClip(string name)
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
        return clip;
    }

    // One key per frame; `cuts` makes the tangents flat-stepped where a value jumps (a camera cut)
    static void AddCurve(List<EditorCurveBinding> bindings, List<AnimationCurve> curves, string path, Type type, string property,
        int frames, Func<int, float> value, bool cuts = false)
    {
        var keys = new Keyframe[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            int a = Mathf.Max(f - 1, 0), b = Mathf.Min(f + 1, frames);
            float slope = (value(b) - value(a)) * Fps / (b - a);
            if (cuts && (Mathf.Abs(value(b) - value(f)) > 0.15f || Mathf.Abs(value(f) - value(a)) > 0.15f)) slope = 0f;
            keys[f] = new Keyframe(f / Fps, value(f), slope, slope);
        }
        var curve = new AnimationCurve(keys);
        if (cuts)
            for (int f = 0; f < frames; f++)
                if (Mathf.Abs(value(f + 1) - value(f)) > 0.15f) AnimationUtility.SetKeyRightTangentMode(curve, f, AnimationUtility.TangentMode.Constant);
        bindings.Add(EditorCurveBinding.FloatCurve(path, type, property));
        curves.Add(curve);
    }
}
