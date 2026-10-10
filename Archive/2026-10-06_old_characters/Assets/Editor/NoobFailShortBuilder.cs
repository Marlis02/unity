using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Timeline;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// Builds the 12 s "Noob fails the obby" short in Shorts_Obby: baked animation clip, two Cinemachine shots, the Timeline
// and a Recorder track that records the whole Timeline (1080x1920, 30 fps) when you press Play.
// Unity's H.264 encoder isn't available on Linux, so the track writes PNG frames and ShortsFrameEncoder turns them into an MP4.
// Tweak the beats / RootPosition below and re-run Tools/Shorts/Build Noob Fail Timeline.
public static class NoobFailShortBuilder
{
    const float Duration = 12f;
    const float Fps = 30f;
    const string ClipPath = "Assets/Animations/Noob_ObbyFail.anim";
    const string TimelinePath = "Assets/Timelines/Shorts_Obby_Fail.playable";
    const string NoobPrefabPath = "Assets/Prefabs/Noob.prefab";
    const float ScaredFaceTime = 6.5f; // smile -> scared when he looks down at the lava
    const int VideoWidth = 1080, VideoHeight = 1920;
    const string RecordingFolder = "Recordings/Shorts_Obby_Fail_<Take>" + ShortsFrameEncoder.FramesSuffix; // relative to the project folder

    const int Root = 0, Neck = 1, LShoulder = 2, RShoulder = 3, LHip = 4, RHip = 5;
    static readonly string[] JointPaths = { "", "Torso/Neck", "Torso/LeftShoulder", "Torso/RightShoulder", "Torso/LeftHip", "Torso/RightHip" };

    // Local Euler angles (degrees) per joint, indexed like JointPaths. Raw Euler, so values past 360 spin.
    struct Pose
    {
        public Vector3[] e;
        public static Pose New() => new Pose { e = new Vector3[JointPaths.Length] };
        public static Pose Lerp(Pose a, Pose b, float w)
        {
            var p = New();
            for (int i = 0; i < p.e.Length; i++) p.e[i] = Vector3.LerpUnclamped(a.e[i], b.e[i], w);
            return p;
        }
    }

    delegate Pose PoseFn(float t);

    readonly struct Beat
    {
        public readonly float start, blend;
        public readonly PoseFn pose;
        public Beat(float start, float blend, PoseFn pose) { this.start = start; this.blend = blend; this.pose = pose; }
    }

    // Body language over time; each beat crossfades from the previous one over `blend` seconds.
    static readonly Beat[] Beats =
    {
        new Beat(0f,    0f,    Idle),
        new Beat(0.8f,  0.15f, Anticipate),
        new Beat(1.0f,  0.12f, Run),
        new Beat(2.2f,  0.1f,  Jump),        // -> Platform_1
        new Beat(2.8f,  0.1f,  Run),
        new Beat(3.5f,  0.1f,  Jump),        // -> Platform_2
        new Beat(4.1f,  0.1f,  Idle),
        new Beat(4.2f,  0.25f, t => FaceCamera(Idle(t))),
        new Beat(4.45f, 0.12f, Celebrate),   // shows off to the camera
        new Beat(4.85f, 0.25f, Idle),
        new Beat(5.1f,  0.1f,  Run),
        new Beat(5.45f, 0.1f,  Jump),        // weak jump towards Platform_3
        new Beat(6.0f,  0.1f,  AirRun),      // cartoon hang
        new Beat(6.35f, 0.15f, UhOh),
        new Beat(7.0f,  0.1f,  Fall),
        new Beat(7.85f, 0.1f,  Sink),
    };

    static float Wave(float t, float hz, float phase = 0f) => Mathf.Sin((t * hz + phase) * 2f * Mathf.PI);
    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

    static Pose Idle(float t)
    {
        var p = Pose.New();
        float breathe = Wave(t, 0.5f) * 3f;
        p.e[LShoulder] = new Vector3(breathe, 0, -4);
        p.e[RShoulder] = new Vector3(breathe, 0, 4);
        return p;
    }

    static Pose Anticipate(float t)
    {
        var p = Pose.New();
        p.e[Root] = new Vector3(10, 0, 0);
        p.e[LShoulder] = new Vector3(35, 0, -8);
        p.e[RShoulder] = new Vector3(35, 0, 8);
        return p;
    }

    static Pose Run(float t)
    {
        var p = Pose.New();
        float s = Wave(t, 2.2f) * 50f;
        p.e[Root] = new Vector3(10, 0, 0);
        p.e[LHip] = new Vector3(s, 0, 0);
        p.e[RHip] = new Vector3(-s, 0, 0);
        p.e[LShoulder] = new Vector3(-s, 0, -4);
        p.e[RShoulder] = new Vector3(s, 0, 4);
        return p;
    }

    // Classic R6 jump: both arms straight up
    static Pose Jump(float t)
    {
        var p = Pose.New();
        p.e[Root] = new Vector3(4, 0, 0);
        p.e[LShoulder] = new Vector3(-165, 0, -10);
        p.e[RShoulder] = new Vector3(-165, 0, 10);
        p.e[LHip] = new Vector3(-20, 0, 0);
        p.e[RHip] = new Vector3(12, 0, 0);
        return p;
    }

    static Pose FaceCamera(Pose p)
    {
        p.e[Root] = new Vector3(0, 160, 0);
        return p;
    }

    static Pose Celebrate(float t)
    {
        var p = Pose.New();
        float pump = Wave(t, 4f) * 12f;
        p.e[Root] = new Vector3(-6, 160, 0);
        p.e[Neck] = new Vector3(-15, 0, 0);
        p.e[LShoulder] = new Vector3(-170 + pump, 0, -25);
        p.e[RShoulder] = new Vector3(-170 + pump, 0, 25);
        return p;
    }

    // Legs keep running in mid-air before he notices
    static Pose AirRun(float t)
    {
        var p = Jump(t);
        float s = Wave(t, 4.5f) * 60f;
        p.e[Root] = new Vector3(-4, 0, 0);
        p.e[LHip] = new Vector3(s, 0, 0);
        p.e[RHip] = new Vector3(-s, 0, 0);
        return p;
    }

    // Freezes, looks down at the lava, then looks at the camera (CM_FailShot)
    static Pose UhOh(float t)
    {
        var p = Pose.New();
        // arms only slightly out: the right arm sits between CM_FailShot and the face
        p.e[LShoulder] = new Vector3(10, 0, -25);
        p.e[RShoulder] = new Vector3(10, 0, 25);
        p.e[LHip] = new Vector3(-8, 0, 0);
        p.e[RHip] = new Vector3(8, 0, 0);
        p.e[Neck] = Vector3.Lerp(new Vector3(35, 0, 0), new Vector3(0, 90, 0), Smooth(6.55f, 6.75f, t));
        return p;
    }

    // Windmills both arms at different speeds and kicks; both arms end pointing up (-890 = -530 = -170 mod 360)
    static Pose Fall(float t)
    {
        var p = Pose.New();
        float u = (t - 7f) / 0.85f;
        float kick = Wave(t, 5f) * 40f;
        p.e[Root] = new Vector3(-12, 0, Wave(t, 3f) * 12f);
        p.e[Neck] = Vector3.Lerp(new Vector3(0, 90, 0), new Vector3(-20, 0, 0), Smooth(7.25f, 7.45f, t));
        p.e[LShoulder] = new Vector3(-890f * u, 0, -20);
        p.e[RShoulder] = new Vector3(-530f * u, 0, 20);
        p.e[LHip] = new Vector3(kick, 0, 0);
        p.e[RHip] = new Vector3(-kick, 0, 0);
        return p;
    }

    // Sinking into the lava, arms flailing above the head
    static Pose Sink(float t)
    {
        var p = Pose.New();
        float l = Wave(t, 3f), r = Wave(t, 3f, 0.3f), kick = Wave(t, 3f) * 30f;
        p.e[Root] = new Vector3(-5, 0, Wave(t, 2.5f) * 8f);
        p.e[Neck] = new Vector3(-25, 0, 0);
        p.e[LShoulder] = new Vector3(-890f + l * 35f, 0, -20 - l * 15f);
        p.e[RShoulder] = new Vector3(-530f - r * 35f, 0, 20 + r * 15f);
        p.e[LHip] = new Vector3(kick, 0, 0);
        p.e[RHip] = new Vector3(-kick, 0, 0);
        return p;
    }

    static Pose PoseAt(float t)
    {
        int i = Beats.Length - 1;
        while (i > 0 && t < Beats[i].start) i--;
        return EvaluateBeat(i, t);
    }

    static Pose EvaluateBeat(int i, float t)
    {
        var b = Beats[i];
        var p = b.pose(t);
        if (i == 0 || b.blend <= 0f || t >= b.start + b.blend) return p;
        float w = Mathf.SmoothStep(0f, 1f, (t - b.start) / b.blend);
        return Pose.Lerp(EvaluateBeat(i - 1, t), p, w);
    }

    static Vector3 Arc(float t, float t0, float t1, Vector3 from, Vector3 to, float height)
    {
        float s = Mathf.InverseLerp(t0, t1, t);
        return Vector3.Lerp(from, to, s) + Vector3.up * (4f * height * s * (1f - s));
    }

    // Noob root (feet) in world space. Platform tops: start 3, P1 3, P2 3.5, P3 4; lava 0.
    static Vector3 RootPosition(float t)
    {
        if (t < 1.0f) return new Vector3(0f, 3f, -2.5f);
        if (t < 2.2f)
        {
            float u = (t - 1f) / 1.2f;
            return new Vector3(0f, 3f + 0.06f * Mathf.Abs(Wave(t, 2.2f)), -2.5f + 5.1f * Mathf.Pow(u, 1.4f));
        }
        if (t < 2.8f) return Arc(t, 2.2f, 2.8f, new Vector3(0f, 3f, 2.6f), new Vector3(0.3f, 3f, 6.3f), 1.4f);
        if (t < 3.5f)
            return Vector3.Lerp(new Vector3(0.3f, 3f, 6.3f), new Vector3(-0.4f, 3f, 8.6f), (t - 2.8f) / 0.7f)
                 + Vector3.up * (0.06f * Mathf.Abs(Wave(t, 2.2f)));
        if (t < 4.1f) return Arc(t, 3.5f, 4.1f, new Vector3(-0.4f, 3f, 8.6f), new Vector3(-1f, 3.5f, 12f), 1.5f);
        if (t < 5.1f)
        {
            float hop = t > 4.45f && t < 4.75f ? Mathf.Sin((t - 4.45f) / 0.3f * Mathf.PI) * 0.35f : 0f;
            return new Vector3(-1f, 3.5f + hop, 12f);
        }
        if (t < 5.45f) return Vector3.Lerp(new Vector3(-1f, 3.5f, 12f), new Vector3(-0.8f, 3.5f, 13.25f), (t - 5.1f) / 0.35f);
        if (t < 6.0f)
        {
            // Too short: decelerates to a mid-air stop in front of Platform_3
            float s = (t - 5.45f) / 0.55f, e = 1f - (1f - s) * (1f - s);
            return Vector3.Lerp(new Vector3(-0.8f, 3.5f, 13.25f), new Vector3(-0.5f, 4.35f, 14.5f), e);
        }
        if (t < 7.0f) return new Vector3(-0.5f, 4.35f + 0.03f * Wave(t, 1.5f), 14.5f);
        if (t < 7.85f)
        {
            float s = (t - 7f) / 0.85f;
            return new Vector3(-0.5f, 4.35f - 4.95f * s * s, 14.5f - 0.1f * s);
        }
        float k = Mathf.Clamp01((t - 7.85f) / 2.55f);
        return new Vector3(-0.5f, -0.6f - 2.5f * k * k * (3f - 2f * k), 14.4f);
    }

    [MenuItem("Tools/Shorts/Build Noob Fail Timeline")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[NoobFailShort] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        var noob = GameObject.Find("Noob");
        if (noob == null) { Debug.LogError("[NoobFailShort] No 'Noob' in the open scene."); return; }

        EnsureFolder("Assets/Animations");
        EnsureFolder("Assets/Timelines");

        var clip = BuildClip();
        var animator = EnsureAnimator(noob);
        noob.transform.SetPositionAndRotation(RootPosition(0f), Quaternion.identity);

        var brain = Camera.main.GetComponent<CinemachineBrain>();
        if (brain == null) brain = Camera.main.gameObject.AddComponent<CinemachineBrain>();

        var followCam = EnsureVcam("CM_Follow");
        followCam.Follow = noob.transform;
        followCam.Priority.Enabled = true;
        followCam.Priority.Value = 10;
        followCam.Lens.FieldOfView = 55f;
        followCam.transform.rotation = Quaternion.Euler(22f, 0f, 0f);
        var body = followCam.GetComponent<CinemachineFollow>();
        if (body == null) body = followCam.gameObject.AddComponent<CinemachineFollow>();
        body.FollowOffset = new Vector3(0f, 4.5f, -7f);
        body.TrackerSettings.BindingMode = BindingMode.WorldSpace;
        body.TrackerSettings.PositionDamping = new Vector3(0.3f, 0.8f, 0.3f);
        followCam.transform.position = noob.transform.position + body.FollowOffset;

        // Side-on static shot of the gap between Platform_2 and Platform_3 (+X side); Noob turns his head to it at ~6.7 s
        var failCam = EnsureVcam("CM_FailShot");
        failCam.Priority.Enabled = true;
        failCam.Priority.Value = 0;
        failCam.Lens.FieldOfView = 50f;
        // At head height so the near arm doesn't cover the face
        failCam.transform.position = new Vector3(9f, 6.5f, 14.4f);
        failCam.transform.LookAt(new Vector3(-0.5f, 3.4f, 14.4f));

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }

        var directorGo = GameObject.Find("Timeline_ObbyFail");
        if (directorGo == null) directorGo = new GameObject("Timeline_ObbyFail");
        var director = directorGo.GetComponent<PlayableDirector>();
        if (director == null) director = directorGo.AddComponent<PlayableDirector>();

        // Keep the take counter across rebuilds so old recordings aren't overwritten
        int take = 1;
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath))
            if (sub is RecorderSettings old) take = Mathf.Max(take, old.Take);

        foreach (var track in new List<TrackAsset>(timeline.GetOutputTracks()))
        {
            director.ClearGenericBinding(track);
            timeline.DeleteTrack(track);
        }
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(TimelinePath))
            if (sub is RecorderSettings orphan)
            {
                AssetDatabase.RemoveObjectFromAsset(orphan);
                Object.DestroyImmediate(orphan, true);
            }
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.editorSettings.frameRate = Fps;
        timeline.fixedDuration = Duration;

        var animTrack = timeline.CreateTrack<AnimationTrack>(null, "Noob");
        animTrack.trackOffset = TrackOffset.ApplyTransformOffsets;
        animTrack.position = Vector3.zero;
        animTrack.rotation = Quaternion.identity;
        var animClip = animTrack.CreateClip(clip);
        animClip.start = 0;
        animClip.duration = Duration;
        animClip.displayName = "Noob_ObbyFail";
        ((AnimationPlayableAsset)animClip.asset).removeStartOffset = false; // clip is in world space

        var camTrack = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
        // Hard cut to the side shot the moment he freezes mid-air
        AddShot(camTrack, director, followCam, "Follow", 0, 6.0);
        AddShot(camTrack, director, failCam, "Fail", 6.0, Duration - 6.0);

        AddRecorderTrack(timeline, take);

        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;
        director.SetGenericBinding(animTrack, animator);
        director.SetGenericBinding(camTrack, brain);

        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(director);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(noob.scene);
        EditorSceneManager.SaveScene(noob.scene);
        Debug.Log("[NoobFailShort] Built " + TimelinePath);
    }

    static AnimationClip BuildClip()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, ClipPath);
        }
        clip.ClearCurves();
        clip.frameRate = Fps;

        string[] axes = { "x", "y", "z" };
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        for (int a = 0; a < 3; a++)
        {
            bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition." + axes[a]));
            curves.Add(new AnimationCurve());
        }
        for (int j = 0; j < JointPaths.Length; j++)
            for (int a = 0; a < 3; a++)
            {
                bindings.Add(EditorCurveBinding.FloatCurve(JointPaths[j], typeof(Transform), "localEulerAnglesRaw." + axes[a]));
                curves.Add(new AnimationCurve());
            }

        // Bake one key per frame with analytic (central difference) tangents
        const float h = 1f / 480f;
        int frames = Mathf.RoundToInt(Duration * Fps);
        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;
            Vector3 pos = RootPosition(t);
            Vector3 vel = (RootPosition(t + h) - RootPosition(t - h)) / (2f * h);
            for (int a = 0; a < 3; a++)
                curves[a].AddKey(new Keyframe(t, pos[a], vel[a], vel[a]));

            Pose q = PoseAt(t), q0 = PoseAt(t - h), q1 = PoseAt(t + h);
            for (int j = 0; j < JointPaths.Length; j++)
                for (int a = 0; a < 3; a++)
                {
                    float d = (q1.e[j][a] - q0.e[j][a]) / (2f * h);
                    curves[3 + j * 3 + a].AddKey(new Keyframe(t, q.e[j][a], d, d));
                }
        }
        // Face swap: stepped on/off curves
        var smile = new AnimationCurve(StepKey(0f, 1f), StepKey(ScaredFaceTime, 0f));
        var scared = new AnimationCurve(StepKey(0f, 0f), StepKey(ScaredFaceTime, 1f));
        bindings.Add(EditorCurveBinding.FloatCurve("Torso/Neck/Head/FaceSmile", typeof(GameObject), "m_IsActive"));
        curves.Add(smile);
        bindings.Add(EditorCurveBinding.FloatCurve("Torso/Neck/Head/FaceScared", typeof(GameObject), "m_IsActive"));
        curves.Add(scared);

        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static Keyframe StepKey(float time, float value) => new Keyframe(time, value, float.PositiveInfinity, float.PositiveInfinity);

    static Animator EnsureAnimator(GameObject noob)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(NoobPrefabPath);
        if (prefabRoot.GetComponent<Animator>() == null)
        {
            prefabRoot.AddComponent<Animator>();
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, NoobPrefabPath);
        }
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        var animator = noob.GetComponent<Animator>();
        if (animator == null) animator = noob.AddComponent<Animator>();
        animator.applyRootMotion = false;
        return animator;
    }

    static CinemachineCamera EnsureVcam(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) go = new GameObject(name);
        var vcam = go.GetComponent<CinemachineCamera>();
        if (vcam == null) vcam = go.AddComponent<CinemachineCamera>();
        return vcam;
    }

    static TimelineClip AddShot(CinemachineTrack track, PlayableDirector director, CinemachineCamera vcam, string name, double start, double duration)
    {
        var clip = track.CreateClip<CinemachineShot>();
        clip.start = start;
        clip.duration = duration;
        clip.displayName = name;
        var shot = (CinemachineShot)clip.asset;
        shot.DisplayName = name;
        shot.VirtualCamera.exposedName = GUID.Generate().ToString();
        director.SetReferenceValue(shot.VirtualCamera.exposedName, vcam);
        return clip;
    }

    // Recorder clips only record in Play Mode: pressing Play runs the Timeline (playOnAwake) and writes the MP4.
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
