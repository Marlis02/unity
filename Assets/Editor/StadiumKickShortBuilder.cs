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
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

// Short: the Roblox half of the stadium clip the user sent (ezgif frames, 2026-10-09). Bacon sits in the stands right
// behind a guy with dreads, smiles, leans in and squints, sees the bumps all over the back of the guy's head, is
// disgusted, and kicks him out of his seat; the guy flies off and the shot ends on the empty seat with the sole of
// bacon's trainer up at the camera and his face going "o_o". First test take, ~7 s, no extras (the user).
// Tools/Shorts/Build Stadium Kick Short -> Assets/Scenes/Shorts_StadiumKick.unity + Timeline; Build(record: true) adds a
// Recorder track: Play writes PNG frames and ShortsFrameEncoder makes Recordings/Shorts_StadiumKick_<take>.mp4.
public static class StadiumKickShortBuilder
{
    const float Fps = 30f, Duration = 7f;
    const string ScenePath = "Assets/Scenes/Shorts_StadiumKick.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_StadiumKick.playable";
    const string ClipFolder = "Assets/Animations";
    const string Folder = "Assets/Shorts/StadiumKick";
    const string BaconPrefab = MinifigCharacterBuilder.PrefabFolder + "/bacon.prefab";
    const string GuyPrefab = MinifigCharacterBuilder.PrefabFolder + "/dreads_guy.prefab";
    const int VideoWidth = 1080, VideoHeight = 1920;
    const string RecordingFolder = "Recordings/Shorts_StadiumKick_<Take>" + ShortsFrameEncoder.FramesSuffix; // relative to the project folder

    // The beats (seconds): 1 smiling, 2 squinting, 3 the bumps, 4 disgust, 5 the kick, 6 the empty seat and the sole
    const float Squint = 1.2f, BumpsShot = 2.2f, Disgust = 3.3f, KickShot = 4.3f, Kick = 4.55f, Hit = 4.7f, Final = 5.3f;

    // The stands: rows step up RowRise and back RowDepth; the guy in row 0 (front), bacon right behind him in row 1
    const float RowRise = 0.45f, RowDepth = 0.95f, SeatTop = 0.27f, SeatWidth = 0.75f;
    static readonly Vector3 GuySeat = new Vector3(0f, 0f, 0f), BaconSeat = new Vector3(0f, RowRise, -RowDepth);

    static readonly string[] Bones =
    {
        "Hips", "Spine", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    };

    [MenuItem("Tools/Shorts/Build Stadium Kick Short")]
    public static void Build() => Build(record: false);

    [MenuItem("Tools/Shorts/Build Stadium Kick Short (Record)")]
    public static void BuildForRecording() => Build(record: true);

    public static void Build(bool record)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[StadiumKick] Exit Play Mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var f in new[] { ClipFolder, "Assets/Timelines", Folder }) RigUtility.EnsureFolder(f);
        var baconPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaconPrefab);
        var guyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuyPrefab);
        if (baconPrefab == null || guyPrefab == null) { Debug.LogError("[StadiumKick] Build the characters first (Tools/Characters/Build Characters)"); return; }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CharacterLineupBuilder.BuildStudio();
        var floor = GameObject.Find("Floor");
        if (floor != null) Object.DestroyImmediate(floor);
        BuildStands();

        var bacon = (GameObject)PrefabUtility.InstantiatePrefab(baconPrefab, scene);
        bacon.name = "Bacon";
        bacon.transform.position = BaconSeat;
        var guy = (GameObject)PrefabUtility.InstantiatePrefab(guyPrefab, scene);
        guy.name = "Guy";
        guy.transform.position = GuySeat;

        var rig = new GameObject("CameraRig");
        rig.AddComponent<Animator>();
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.SetParent(rig.transform, false);
        camera.fieldOfView = 40f;
        camera.nearClipPlane = 0.02f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.62f, 0.78f, 0.95f);
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.stopNaN = true;
        RenderSettings.fog = false;

        var baconClip = BakeActor(baconPrefab, "StadiumKick_Bacon", BaconPose, BaconFace);
        var guyClip = BakeActor(guyPrefab, "StadiumKick_Guy", GuyPose, null);
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
        var director = new GameObject("Timeline_StadiumKick").AddComponent<PlayableDirector>();
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
        Track("Guy", guyClip, guy.GetComponent<Animator>());
        Track("Camera", cameraClip, rig.GetComponent<Animator>());
        if (record) AddRecorderTrack(timeline, take);
        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        director.time = 0; director.Evaluate();
        Debug.Log($"[StadiumKick] Built {ScenePath}, {Duration} s" + (record ? $": press Play to record take {take}" : ""));
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

    // ---------------------------------------------------------------- the stands

    // Concrete steps, a row of blue seats on each, more rows going up behind, a green pitch below in front
    static void BuildStands()
    {
        var root = new GameObject("Stands").transform;
        var concrete = Mat("Concrete", new Color(0.68f, 0.68f, 0.7f));
        var blue = Mat("Seat", new Color(0.1f, 0.25f, 0.8f), 0.45f);
        var grass = Mat("Grass", new Color(0.23f, 0.55f, 0.2f));
        for (int row = -1; row < 16; row++)
        {
            float y = row * RowRise, z = -row * RowDepth;
            Box(root, "Step" + row, new Vector3(0f, y - 0.25f, z), new Vector3(30f, 0.5f, RowDepth), concrete);
            for (int seat = -18; seat <= 18; seat++)
            {
                if (row < 0) continue;
                var at = new Vector3(seat * SeatWidth, y, z);
                Box(root, $"Seat{row}_{seat}", at + new Vector3(0f, SeatTop - 0.06f, -0.05f), new Vector3(0.6f, 0.12f, 0.5f), blue);
                Box(root, $"Back{row}_{seat}", at + new Vector3(0f, SeatTop + 0.3f, -0.33f), new Vector3(0.6f, 0.62f, 0.08f), blue);
                Box(root, $"Leg{row}_{seat}", at + new Vector3(0f, (SeatTop - 0.12f) / 2f, -0.1f), new Vector3(0.12f, SeatTop - 0.12f, 0.12f), concrete);
            }
        }
        Box(root, "Pitch", new Vector3(0f, -3f, 25f), new Vector3(80f, 0.1f, 45f), grass);
        Box(root, "Wall", new Vector3(0f, -1.5f, 1.6f), new Vector3(16f, 3f, 0.3f), concrete);
    }

    static void Box(Transform parent, string name, Vector3 at, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static Material Mat(string name, Color color, float smoothness = 0.15f)
    {
        string path = $"{Folder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------------------------------------------------------------- the act

    class Pose
    {
        public readonly Dictionary<string, Vector3> e = new Dictionary<string, Vector3>();
        public Vector3 hips;          // added to the hips' rest position, in his own frame
        public Vector3 hipsTurn;      // added turn of the hips (degrees)
        public Pose Rot(string bone, float x, float y, float z)
        {
            e[bone] = (e.TryGetValue(bone, out var v) ? v : Vector3.zero) + new Vector3(x, y, z);
            return this;
        }
    }

    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

    // Sitting: thighs forward level, shins straight down, hips down on the seat; arms resting on the thighs
    static Pose Seated(float hipsDrop)
    {
        var p = new Pose();
        foreach (var s in new[] { "Left", "Right" })
            p.Rot(s + "UpperLeg", -90f, 0f, 0f).Rot(s + "LowerLeg", 90f, 0f, 0f)
             .Rot(s + "UpperArm", -35f, 0f, 0f).Rot(s + "LowerArm", -40f, 0f, 0f);
        p.hips = new Vector3(0f, -hipsDrop, 0f);
        return p;
    }

    static float baconDrop, guyDrop;

    static Pose BaconPose(float t)
    {
        var p = Seated(baconDrop);
        float breathe = Mathf.Sin(t * 2.4f) * 1.2f;
        // Leans in to look, sits back in disgust
        float lean = 14f * Smooth(Squint, Squint + 0.4f, t) * (1f - Smooth(Disgust - 0.1f, Disgust + 0.25f, t));
        float back = -8f * Smooth(Disgust - 0.1f, Disgust + 0.25f, t) * (1f - Smooth(KickShot, Kick, t));
        p.Rot("Spine", lean + back + breathe, 0f, 0f).Rot("Head", 0.6f * lean + 6f * Smooth(BumpsShot, BumpsShot + 0.3f, t) * (1f - Smooth(Disgust - 0.1f, Disgust, t)), 0f, 0f);
        // The kick: the right knee draws up, then the leg shoots out straight, and stays out at the camera
        float windUp = Smooth(KickShot + 0.05f, Kick, t) * (1f - Smooth(Kick, Hit, t));
        float strike = Smooth(Kick, Hit, t);
        p.Rot("RightUpperLeg", -35f * windUp - 18f * strike, 0f, 0f).Rot("RightLowerLeg", 30f * windUp - 88f * strike, 0f, 0f);
        p.Rot("Spine", -12f * strike, 0f, 0f);
        // Arms brace on the seat as he kicks
        foreach (var s in new[] { "Left", "Right" })
            p.Rot(s + "UpperArm", 25f * strike, 0f, (s == "Left" ? -1f : 1f) * 18f * strike);
        return p;
    }

    static Pose GuyPose(float t)
    {
        var p = Seated(guyDrop);
        p.Rot("Head", -3f + Mathf.Sin(t * 1.7f) * 2f, Mathf.Sin(t * 0.8f) * 8f, 0f);
        // Kicked: flies forward and up out of his seat, tumbling, gone by the final shot
        float fly = Mathf.Clamp01((t - Hit) / 0.55f);
        if (t > Hit)
        {
            p.hips += new Vector3(0f, 2.6f * fly - 1.2f * fly * fly + 0.4f * fly, 7f * fly);
            p.hipsTurn = new Vector3(540f * fly, 0f, 90f * fly);
            foreach (var s in new[] { "Left", "Right" }) p.Rot(s + "UpperArm", 0f, 0f, (s == "Left" ? -1f : 1f) * 120f * Smooth(Hit, Hit + 0.2f, t));
        }
        if (t > Final - 0.1f) p.hips += new Vector3(0f, 40f, 0f); // well gone
        return p;
    }

    // ---------------------------------------------------------------- bacon's face

    // Faces as LiveFace field values over its defaults (Chill Face); each beat's face blends in over a fifth of a second
    static readonly (float from, Dictionary<string, float> face)[] Faces =
    {
        (0f, Face(("eyeHappy", 0.6f), ("lids", 0f), ("eyeWhite", 0f), ("smile", 0.9f), ("mouthOpen", 0.15f), ("teeth", 0.6f), ("brow", 0.5f), ("browRaise", 0.3f))),
        (Squint, Face(("eyeWhite", 1f), ("lids", 0.65f), ("lidTilt", 0.3f), ("brow", 1f), ("browAngle", 0.4f), ("browAsym", 0.5f), ("smile", 0.1f), ("smirk", 0.3f))),
        (Disgust, Face(("eyeWhite", 1f), ("lids", 0.45f), ("lidTilt", 0.5f), ("brow", 1f), ("browThick", 0.6f), ("browAngle", 0.6f), ("browRaise", -0.2f),
            ("smirk", -0.8f), ("smile", -0.4f), ("wobble", 0.7f), ("mouthOpen", 0.3f), ("mouthWidth", 1.05f), ("tongueOut", 1f), ("creases", 0.7f), ("bold", 1f))),
        (KickShot, Face(("eyeWhite", 1f), ("lids", 0.2f), ("lidTilt", 1f), ("brow", 1f), ("browThick", 1f), ("browAngle", 1f), ("browRaise", -0.5f),
            ("smile", -0.4f), ("mouthOpen", 0.5f), ("mouthWidth", 1.3f), ("gritted", 1f), ("creases", 1f), ("bold", 1f))),
        (Final, Face(("eyeWhite", 1f), ("eyeRound", 1f), ("pupilSmall", 0.7f), ("lids", 0f), ("brow", 1f), ("browArc", 1f), ("browRaise", 0.6f),
            ("smile", 0f), ("mouthOpen", 0.25f), ("mouthD", 0.6f), ("mouthWidth", 0.5f))),
    };

    static Dictionary<string, float> Face(params (string field, float value)[] set) => set.ToDictionary(s => s.field, s => s.value);

    static Dictionary<string, float> faceDefaults;

    static Dictionary<string, float> BaconFace(float t)
    {
        var face = new Dictionary<string, float>(faceDefaults);
        foreach (var (from, set) in Faces)
        {
            float w = from <= 0f ? 1f : Smooth(from - 0.1f, from + 0.12f, t);
            if (w <= 0f) continue;
            foreach (var key in face.Keys.ToList())
            {
                float target = set.TryGetValue(key, out var v) ? v : faceDefaults[key];
                face[key] = Mathf.Lerp(face[key], target, w);
            }
        }
        // Blinks, and the eyes on the guy's head while he looks at it
        float blink = BlinkAt(t, 0.8f) + BlinkAt(t, 2.9f);
        face["eyeOpen"] *= 1f - Mathf.Clamp01(blink);
        return face;
    }

    static float BlinkAt(float t, float at) { float d = t - at; return d < 0f || d > 0.16f ? 0f : d < 0.06f ? d / 0.06f : 1f - (d - 0.06f) / 0.1f; }

    // ---------------------------------------------------------------- camera

    // Cuts between the beats; slow push-ins within them. Positions in the world (characters face +Z, the pitch side)
    static (Vector3 at, Vector3 look) Shot(float t)
    {
        Vector3 head = BaconSeat + new Vector3(0f, 1.15f, 0.05f), guyBack = GuySeat + new Vector3(0f, 1.14f, -0.22f);
        if (t < Squint) // both of them from in front, bacon behind the guy
            return (Vector3.Lerp(new Vector3(0.25f, 1.38f, 1.75f), new Vector3(0.22f, 1.35f, 1.5f), t / Squint), new Vector3(0f, 1.25f, -0.45f));
        if (t < BumpsShot) // closer on bacon leaning in
        {
            float k = (t - Squint) / (BumpsShot - Squint);
            return (Vector3.Lerp(new Vector3(0.9f, 1.6f, 0.85f), new Vector3(0.75f, 1.55f, 0.6f), k), head + new Vector3(0f, -0.08f, 0.1f));
        }
        if (t < Disgust) // bacon's view: the back of the guy's head, pushing in fast
        {
            float k = Smooth(BumpsShot, BumpsShot + 0.5f, t);
            return (Vector3.Lerp(new Vector3(0.05f, 1.3f, -1.05f), new Vector3(0.03f, 1.22f, -0.75f), k), guyBack);
        }
        if (t < KickShot) // bacon disgusted, close
            return (new Vector3(0.12f, 1.72f, 0.45f), head + new Vector3(0f, 0f, 0f));
        if (t < Final) // from the side: the kick and the guy flying off
            return (new Vector3(3.2f, 1.3f, -0.2f), new Vector3(0f, 0.8f, -0.3f));
        // Final: low in the guy's empty seat, the sole of bacon's trainer up at the camera
        return (new Vector3(0.1f, 1.45f, 0.75f), BaconSeat + new Vector3(0f, 1.0f, 0f));
    }

    static AnimationClip CameraClip()
    {
        var clip = NewClip("StadiumKick_Camera");
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

    static AnimationClip BakeActor(GameObject prefab, string name, Func<float, Pose> act, Func<float, Dictionary<string, float>> face)
    {
        var go = Object.Instantiate(prefab);
        go.transform.position = Vector3.zero;
        Transform Find(string n) => go.GetComponentsInChildren<Transform>(true).First(x => x.name == n);
        var bones = Bones.Select(Find).ToArray();
        var paths = bones.Select(b => AnimationUtility.CalculateTransformPath(b, go.transform)).ToArray();
        var hipsRest = bones[0].localPosition;
        // How far the hips drop to sit: the shin and foot stand on the floor, the hips at the knee's height
        float drop = Find("Hips").position.y - Find("LeftLowerLeg").position.y - 0.02f;
        baconDrop = guyDrop = drop;
        var liveFace = go.GetComponentInChildren<LiveFace>(true);
        string facePath = liveFace != null ? AnimationUtility.CalculateTransformPath(liveFace.transform, go.transform) : null;
        if (liveFace != null)
            faceDefaults = typeof(LiveFace).GetFields().Where(f => f.FieldType == typeof(float)).ToDictionary(f => f.Name, f => (float)f.GetValue(liveFace));
        Object.DestroyImmediate(go);

        int frames = Mathf.RoundToInt(Duration * Fps);
        var rotations = new Quaternion[bones.Length, frames + 1];
        var hips = new Vector3[frames + 1];
        var faces = new Dictionary<string, float>[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;
            var pose = act(t);
            for (int b = 0; b < bones.Length; b++)
            {
                var e = pose.e.TryGetValue(Bones[b], out var v) ? v : Vector3.zero;
                var q = Quaternion.Euler(e);
                if (b == 0) q = Quaternion.Euler(pose.hipsTurn) * q;
                if (f > 0 && Quaternion.Dot(q, rotations[b, f - 1]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                rotations[b, f] = q;
            }
            hips[f] = hipsRest + pose.hips;
            if (face != null && facePath != null) faces[f] = face(t);
        }

        var clip = NewClip(name);
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        for (int b = 0; b < bones.Length; b++)
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
        if (face != null && facePath != null)
            foreach (var field in faces[0].Keys.ToList())
                AddCurve(bindings, curves, facePath, typeof(LiveFace), field, frames, f => faces[f][field]);
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
        return clip;
    }

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
