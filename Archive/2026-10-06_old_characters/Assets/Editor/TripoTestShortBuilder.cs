using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Timeline;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

// Test short for the Tripo characters (Tools/Characters/Build Tripo Characters): Hero waves at the camera, Buddy and Sis
// react, then all three dance and jump. Motion is posed bone by bone below and baked into humanoid clips (muscles), so the
// clips play on any humanoid. Builds Shorts_TripoTest with a Timeline to preview in the editor or in Play Mode; nothing is
// recorded unless it's built with Build(record: true), which adds a Recorder track writing 1080x1920 at 30 fps in Play Mode
// (PNG frames -> MP4 via ShortsFrameEncoder). Tweak the acts and re-run Tools/Shorts/Build Tripo Test Short.
public static class TripoTestShortBuilder
{
    const float Duration = 9f;
    const float Fps = 30f;
    const float JumpTime = 7.3f;
    const string ScenePath = "Assets/Scenes/Shorts_TripoTest.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_TripoTest.playable";
    const string ClipFolder = "Assets/Animations";
    const string VolumePath = "Assets/Characters/Lineup_Volume.asset";
    const string FacePath = "Hips/Spine/Head/Face";
    const int VideoWidth = 1080, VideoHeight = 1920;
    const string RecordingFolder = "Recordings/Shorts_TripoTest_<Take>" + ShortsFrameEncoder.FramesSuffix; // relative to the project folder
    static readonly Color Sky = new Color(0.6f, 0.79f, 0.97f);

    static readonly string[] Bones =
    {
        "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    };
    static readonly string[] Sides = { "Left", "Right" };
    static float Out(string side) => side == "Left" ? -1f : 1f; // sign of Z that swings that side's arm out

    static float thigh, shin; // leg segment lengths in metres, read from the prefab

    // Local bone rotations in degrees, added to the rest pose (every bone is unrotated there: the body faces +Z, arms
    // hanging in an A-pose). Arm out to the side = Z (+ right, - left), arm/elbow forward = -X, knee back = +X,
    // spine/head forward = +X, turn to the body's right = +Y. The hips also move (metres).
    class Pose
    {
        public readonly Vector3[] e = new Vector3[Bones.Length];
        public Vector3 hips;

        public Pose Rot(string bone, float x, float y, float z)
        {
            e[Array.IndexOf(Bones, bone)] += new Vector3(x, y, z);
            return this;
        }

        public Pose Add(Pose other, float weight = 1f)
        {
            for (int i = 0; i < e.Length; i++) e[i] += other.e[i] * weight;
            hips += other.hips * weight;
            return this;
        }
    }

    class Actor
    {
        public string name;
        public Vector3 position;
        public float yaw;
        public Func<float, Pose> act;
        public (float time, RobloxFace.Emotion emotion)[] faces;
        public (float start, float end)[] talk = { };
        public AnimationClip clip;
    }

    static Actor[] Cast() => new[]
    {
        new Actor
        {
            name = "Hero", position = new Vector3(0f, 0f, 0.25f), act = HeroAct,
            faces = new[]
            {
                (0f, RobloxFace.Emotion.Smile), (1.2f, RobloxFace.Emotion.Grin), (3.5f, RobloxFace.Emotion.Laugh),
                (JumpTime, RobloxFace.Emotion.Grin), (JumpTime + 0.85f, RobloxFace.Emotion.Laugh),
            },
            talk = new[] { (1.3f, 2.7f) },
        },
        new Actor
        {
            name = "Buddy", position = new Vector3(0.9f, 0f, -0.4f), yaw = -12f, act = BuddyAct,
            faces = new[]
            {
                (0f, RobloxFace.Emotion.Neutral), (2.0f, RobloxFace.Emotion.Surprised), (3.6f, RobloxFace.Emotion.Grin),
                (JumpTime + 0.85f, RobloxFace.Emotion.Laugh),
            },
        },
        new Actor
        {
            name = "Sis", position = new Vector3(-0.9f, 0f, -0.4f), yaw = 12f, act = SisAct,
            faces = new[]
            {
                (0f, RobloxFace.Emotion.Smug), (2.0f, RobloxFace.Emotion.Suspicious), (3.6f, RobloxFace.Emotion.Love),
                (JumpTime + 0.85f, RobloxFace.Emotion.Smile),
            },
        },
    };

    // ---------------------------------------------------------------- acts

    static Pose HeroAct(float t) => Idle(t, 0f)
        .Add(WaveRight(t), Env(t, 1.2f, 2.8f, 0.25f))
        .Add(RunningMan(t), Env(t, 3.3f, JumpTime, 0.3f))
        .Add(Jump(t));

    // Turns to Hero (on his left) when he waves
    static Pose BuddyAct(float t) => Idle(t, 0.3f)
        .Add(Surprised(-1f), Env(t, 2.0f, 3.7f, 0.2f))
        .Add(ArmsUpSway(t), Env(t, 3.6f, JumpTime, 0.3f))
        .Add(Jump(t));

    // Turns to Hero (on her right) when he waves
    static Pose SisAct(float t) => Idle(t, 0.65f)
        .Add(HandsOnHips(1f), Env(t, 2.0f, 3.7f, 0.25f))
        .Add(FistPump(t), Env(t, 3.6f, JumpTime, 0.3f))
        .Add(Jump(t));

    static Pose Idle(float t, float phase)
    {
        float breathe = Wave(t, 0.4f, phase);
        var p = new Pose()
            .Rot("Spine", 1.5f * breathe, 0f, 0f).Rot("Head", -1.5f * breathe, 0f, 0f)
            .Rot("LeftLowerArm", -8f, 0f, 0f).Rot("RightLowerArm", -8f, 0f, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperArm", 0f, 0f, Out(s) * (-5f + 1.5f * breathe)); // a little closer than the A-pose
        return p;
    }

    // Upper arm out level with the shoulder, forearm up beside the head and swinging
    static Pose WaveRight(float t) => new Pose()
        .Rot("RightUpperArm", -15f, 0f, 80f)
        .Rot("RightLowerArm", 0f, 0f, 55f + 30f * Wave(t, 2.2f))
        .Rot("Spine", 0f, 0f, 4f).Rot("Head", 0f, 0f, -7f);

    // Hands up, leaning back, head turned towards `turn` (-1 = the body's left, +1 = right)
    static Pose Surprised(float turn)
    {
        var p = new Pose().Rot("Spine", -4f, 10f * turn, 0f).Rot("Head", -5f, 30f * turn, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperArm", -25f, 0f, Out(s) * 30f).Rot(s + "LowerArm", -95f, 0f, 0f);
        return p;
    }

    static Pose HandsOnHips(float turn)
    {
        var p = new Pose().Rot("Head", 0f, 30f * turn, 8f * turn).Rot("Spine", 0f, 6f * turn, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperArm", 10f, 0f, Out(s) * 40f).Rot(s + "LowerArm", 0f, 0f, -Out(s) * 95f);
        return p;
    }

    // 120 bpm: knees dip on every beat
    static float Beat(float t) => 0.5f + 0.5f * Mathf.Cos(t * 2f * 2f * Mathf.PI);

    static Pose RunningMan(float t)
    {
        float swing = Wave(t, 1f);
        var p = Crouch(new Pose(), 20f * Beat(t))
            .Rot("LeftUpperArm", 40f * swing, 0f, -12f).Rot("RightUpperArm", -40f * swing, 0f, 12f)
            .Rot("LeftLowerArm", -95f, 0f, 0f).Rot("RightLowerArm", -95f, 0f, 0f)
            .Rot("Spine", 0f, 12f * swing, 0f).Rot("Head", 6f * Beat(t), -10f * swing, 0f);
        return p;
    }

    static Pose ArmsUpSway(float t)
    {
        float sway = Wave(t, 1f);
        var p = Crouch(new Pose(), 14f * Beat(t))
            .Rot("LeftUpperArm", -10f, 0f, -140f).Rot("RightUpperArm", -10f, 0f, 140f)
            .Rot("LeftLowerArm", 0f, 0f, -20f - 15f * sway).Rot("RightLowerArm", 0f, 0f, 20f - 15f * sway)
            .Rot("Spine", 0f, 0f, 10f * sway).Rot("Head", 0f, 0f, -6f * sway);
        p.hips.x -= 0.05f * sway; // hips swing against the chest
        return p;
    }

    static Pose FistPump(float t)
    {
        float pump = Beat(t);
        var p = Crouch(new Pose(), 16f * pump)
            .Rot("RightUpperArm", -15f, 0f, 115f + 25f * pump).Rot("RightLowerArm", 0f, 0f, 70f * (1f - pump))
            .Rot("LeftUpperArm", 10f, 0f, -40f).Rot("LeftLowerArm", 0f, 0f, 95f) // hand on hip
            .Rot("Head", 8f * pump, 0f, 0f).Rot("Spine", 0f, -8f * Wave(t, 1f), 0f);
        p.hips.x += 0.04f * Wave(t, 1f);
        return p;
    }

    // Crouch, jump throwing the arms up, land and keep them up; nothing before JumpTime
    static Pose Jump(float t)
    {
        const float height = 0.45f;
        float takeoff = JumpTime + 0.25f, landing = takeoff + 0.5f, settled = landing + 0.3f;
        var p = new Pose();
        float armsUp = Smooth(takeoff - 0.08f, takeoff + 0.12f, t);
        float armsBack = Smooth(JumpTime, takeoff - 0.05f, t) * (1f - armsUp);
        foreach (var s in Sides) p.Rot(s + "UpperArm", 45f * armsBack - 15f * armsUp, 0f, Out(s) * 150f * armsUp).Rot(s + "LowerArm", 0f, 0f, Out(s) * 15f * armsUp);
        p.Rot("Head", -8f * armsUp, 0f, 0f);

        if (t < takeoff) return Crouch(p, 32f * Smooth(JumpTime, takeoff, t));
        if (t < landing)
        {
            // In the air: push off out of the crouch, tuck the legs, reach for the ground again
            float s = (t - takeoff) / (landing - takeoff);
            Crouch(p, 32f * (1f - Smooth(0f, 0.25f, s)) + 28f * Smooth(0.75f, 1f, s));
            BendLegs(p, 20f * Mathf.Sin(s * Mathf.PI));
            p.hips.y += 4f * height * s * (1f - s);
            return p;
        }
        return Crouch(p, 28f * (1f - Smooth(landing, settled, t)));
    }

    // Thighs forward by `a` degrees and knees bent by 2a, feet kept flat; the hips drop and shift back so the feet stay planted
    static Pose Crouch(Pose p, float a)
    {
        BendLegs(p, a);
        float r = a * Mathf.Deg2Rad;
        p.hips += new Vector3(0f, -(thigh + shin) * (1f - Mathf.Cos(r)), -(thigh - shin) * Mathf.Sin(r));
        return p.Rot("Spine", 0.3f * a, 0f, 0f).Rot("Head", -0.3f * a, 0f, 0f);
    }

    static void BendLegs(Pose p, float a)
    {
        foreach (var s in Sides) p.Rot(s + "UpperLeg", -a, 0f, 0f).Rot(s + "LowerLeg", 2f * a, 0f, 0f).Rot(s + "Foot", -a, 0f, 0f);
    }

    static float Wave(float t, float hz, float phase = 0f) => Mathf.Sin((t * hz + phase) * 2f * Mathf.PI);
    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    static float Env(float t, float start, float end, float fade) => Smooth(start, start + fade, t) * (1f - Smooth(end - fade, end, t));

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Shorts/Build Tripo Test Short")]
    public static void Build() => Build(record: false);

    public static void Build(bool record)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[TripoTest] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureFolder(ClipFolder);
        EnsureFolder("Assets/Timelines");

        var cast = Cast();
        var heroPrefab = Prefab("Hero");
        thigh = Vector3.Distance(Find(heroPrefab, "LeftUpperLeg").position, Find(heroPrefab, "LeftLowerLeg").position);
        shin = Vector3.Distance(Find(heroPrefab, "LeftLowerLeg").position, Find(heroPrefab, "LeftFoot").position);

        // Clips are baked on scratch instances in a preview scene
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var actor in cast) actor.clip = BakeClip(actor, Prefab(actor.name), stage);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var brain = BuildStage();
        var animators = new Dictionary<Actor, Animator>();
        foreach (var actor in cast)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(actor.name), scene);
            go.transform.SetPositionAndRotation(actor.position, Quaternion.Euler(0f, actor.yaw, 0f));
            animators[actor] = go.GetComponent<Animator>();
        }

        var wide = Vcam("CM_Wide", new Vector3(0f, 1.3f, 6.6f), new Vector3(0f, 0.95f, -0.1f), 44f);
        // Framed off-centre towards his waving (right, +X) hand
        var wave = Vcam("CM_HeroWave", new Vector3(0.15f, 1.35f, 3.4f), new Vector3(0.25f, 1.3f, 0.25f), 42f);
        var reaction = Vcam("CM_Reaction", new Vector3(0f, 1.55f, 4.3f), new Vector3(0f, 1.35f, -0.3f), 42f);
        var dance = Vcam("CM_Dance", new Vector3(0f, 0.75f, 6.4f), new Vector3(0f, 1.1f, -0.1f), 46f);

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }
        // Keep the take counter across rebuilds so old recordings aren't overwritten
        int take = 1;
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath))
            if (sub is RecorderSettings old) take = Mathf.Max(take, old.Take);
        foreach (var track in new List<TrackAsset>(timeline.GetOutputTracks())) timeline.DeleteTrack(track);
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath))
            if (sub is RecorderSettings orphan)
            {
                AssetDatabase.RemoveObjectFromAsset(orphan);
                Object.DestroyImmediate(orphan, true);
            }
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.editorSettings.frameRate = Fps;
        timeline.fixedDuration = Duration;

        var director = new GameObject("Timeline_TripoTest").AddComponent<PlayableDirector>();
        foreach (var actor in cast)
        {
            var track = timeline.CreateTrack<AnimationTrack>(null, actor.name);
            track.trackOffset = TrackOffset.ApplySceneOffsets; // play where the character stands
            var clip = track.CreateClip(actor.clip);
            clip.start = 0;
            clip.duration = Duration;
            // The baked clips have no IK goal curves; foot IK would pull the feet to the origin
            ((AnimationPlayableAsset)clip.asset).applyFootIK = false;
            director.SetGenericBinding(track, animators[actor]);
        }

        var camTrack = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
        AddShot(camTrack, director, wide, "Wide", 0, 1.2);
        AddShot(camTrack, director, wave, "Hero waves", 1.2, 0.8);
        AddShot(camTrack, director, reaction, "Reaction", 2.0, 1.4);
        AddShot(camTrack, director, dance, "Dance", 3.4, Duration - 3.4);
        director.SetGenericBinding(camTrack, brain);

        if (record) AddRecorderTrack(timeline, take);

        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[TripoTest] Built " + ScenePath + (record ? ": press Play to record" : ""));
    }

    // Camera, lights, sky, post-processing and the baseplate, as in the character lineup
    static CinemachineBrain BuildStage()
    {
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Sky;
        camera.nearClipPlane = 0.1f;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        var brain = camera.gameObject.AddComponent<CinemachineBrain>();

        R15LineupBuilder.AddLight("Key Light", new Color(1f, 0.95f, 0.88f), 1.5f, new Vector3(38f, 205f, 0f), LightShadows.Soft);
        R15LineupBuilder.AddLight("Fill Light", new Color(0.75f, 0.85f, 1f), 0.35f, new Vector3(15f, 140f, 0f), LightShadows.None);
        R15LineupBuilder.AddLight("Rim Light", new Color(1f, 0.95f, 0.9f), 0.8f, new Vector3(25f, 15f, 0f), LightShadows.None);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.57f, 0.6f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.33f, 0.32f);
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 14f;
        RenderSettings.fogEndDistance = 38f;
        RenderSettings.fogColor = Sky;

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);

        R15LineupBuilder.AddBaseplate();
        return brain;
    }

    // ---------------------------------------------------------------- baking

    // Poses a scratch instance frame by frame and reads it back as a humanoid pose: body position/rotation and muscles
    static AnimationClip BakeClip(Actor actor, GameObject prefab, Scene stage)
    {
        var go = Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(go, stage);
        // HumanPoseHandler reads the body in world space but writes it relative to the root: pose at the origin
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var bones = Array.ConvertAll(Bones, b => Find(go, b));
        var hipsRest = bones[0].localPosition;
        var handler = new HumanPoseHandler(go.GetComponent<Animator>().avatar, go.transform);
        var human = new HumanPose();

        // Body muscles only: the hands have no finger bones
        var muscles = new List<int>();
        for (int m = 0; m < HumanTrait.MuscleCount; m++)
        {
            int bone = HumanTrait.BoneFromMuscle(m);
            if (bone < (int)HumanBodyBones.LeftThumbProximal || bone > (int)HumanBodyBones.RightLittleDistal) muscles.Add(m);
        }
        var names = new List<string> { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        foreach (int m in muscles) names.Add(HumanTrait.MuscleName[m]);

        int frames = Mathf.RoundToInt(Duration * Fps);
        var samples = new float[names.Count, frames + 1];
        var previous = Quaternion.identity;
        for (int f = 0; f <= frames; f++)
        {
            var pose = actor.act(f / Fps);
            for (int b = 0; b < bones.Length; b++) bones[b].localRotation = Quaternion.Euler(pose.e[b]);
            bones[0].localPosition = hipsRest + pose.hips;
            handler.GetHumanPose(ref human);
            var q = human.bodyRotation;
            if (Quaternion.Dot(q, previous) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // keep the curves continuous
            previous = q;
            var p = human.bodyPosition;
            float[] root = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
            for (int i = 0; i < root.Length; i++) samples[i, f] = root[i];
            for (int i = 0; i < muscles.Count; i++) samples[root.Length + i, f] = human.muscles[muscles[i]];
        }
        handler.Dispose();
        Object.DestroyImmediate(go);

        string path = $"{ClipFolder}/Tripo_Test_{actor.name}.anim";
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
        for (int i = 0; i < names.Count; i++)
        {
            bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), names[i]));
            curves.Add(SampledCurve(samples, i, frames));
        }

        var emotion = new AnimationCurve();
        foreach (var (time, e) in actor.faces) emotion.AddKey(StepKey(time, RobloxFace.EmotionKey(e)));
        bindings.Add(EditorCurveBinding.DiscreteCurve(FacePath, typeof(RobloxFace), "emotion"));
        curves.Add(emotion);
        // Mouth flaps open and shut while talking
        var talk = new AnimationCurve(StepKey(0f, 0f));
        foreach (var (start, end) in actor.talk)
            for (float t = start; t < end; t += 0.22f)
            {
                talk.AddKey(StepKey(t, 1f));
                talk.AddKey(StepKey(Mathf.Min(t + 0.11f, end), 0f));
            }
        bindings.Add(EditorCurveBinding.FloatCurve(FacePath, typeof(RobloxFace), "talk"));
        curves.Add(talk);
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());

        // Root motion baked into the pose, based on the clip as authored: the body moves exactly as posed, in place
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // One key per frame with central-difference tangents
    static AnimationCurve SampledCurve(float[,] samples, int row, int frames)
    {
        var keys = new Keyframe[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            int a = Mathf.Max(f - 1, 0), b = Mathf.Min(f + 1, frames);
            float slope = (samples[row, b] - samples[row, a]) * Fps / (b - a);
            keys[f] = new Keyframe(f / Fps, samples[row, f], slope, slope);
        }
        return new AnimationCurve(keys);
    }

    static Keyframe StepKey(float time, float value) => new Keyframe(time, value, float.PositiveInfinity, float.PositiveInfinity);

    // ---------------------------------------------------------------- helpers

    static GameObject Prefab(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{TripoCharacterBuilder.PrefabFolder}/Tripo_{name}.prefab");

    static Transform Find(GameObject root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
        throw new ArgumentException($"No {name} under {root.name}");
    }

    static CinemachineCamera Vcam(string name, Vector3 position, Vector3 lookAt, float fov)
    {
        var vcam = new GameObject(name).AddComponent<CinemachineCamera>();
        vcam.transform.position = position;
        vcam.transform.LookAt(lookAt);
        vcam.Lens.FieldOfView = fov;
        return vcam;
    }

    static void AddShot(CinemachineTrack track, PlayableDirector director, CinemachineCamera vcam, string name, double start, double duration)
    {
        var clip = track.CreateClip<CinemachineShot>();
        clip.start = start;
        clip.duration = duration;
        clip.displayName = name;
        var shot = (CinemachineShot)clip.asset;
        shot.DisplayName = name;
        shot.VirtualCamera.exposedName = GUID.Generate().ToString();
        director.SetReferenceValue(shot.VirtualCamera.exposedName, vcam);
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

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'), System.IO.Path.GetFileName(path));
    }
}
