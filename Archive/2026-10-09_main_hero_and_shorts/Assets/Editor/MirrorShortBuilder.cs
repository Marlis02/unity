using System;
using System.Collections.Generic;
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
using E = RobloxFace.Emotion;
using Object = UnityEngine.Object;
using Random = System.Random;

// The Roblox half of "the reflection that wouldn't copy" (glass.zip): the hero (question_hero) stands at the bathroom
// mirror. He bends to wash his face; his reflection stays up, trollfaces and waves. He looks up: it's still grinning. He
// turns to the camera furious and storms off right, the reflection staying put, smug, with nobody in front of it. Then
// he comes into the mirror behind it, grabs it and drags it away: the mirror is left empty. The mirror is a window onto
// a mirrored copy of the room, the reflection a second actor (the Double) and the hero in the mirror a third (the
// Sneak), so the reflection can do as it likes. Motion is posed bone by bone and baked into humanoid clips (as in
// FloorSawShortBuilder). Builds Shorts_Mirror with a Timeline; Build(record: true) adds a Recorder track writing
// 1080x1920 at 30 fps in Play Mode (PNG frames -> MP4 via ShortsFrameEncoder). Tweak the beats and re-run
// Tools/Shorts/Build Mirror Short.
public static class MirrorShortBuilder
{
    const float Fps = 30f;
    // Beats, seconds
    const float BendDown = 1.2f, StandUp = 4.3f, TurnStart = 5.9f, TurnEnd = 6.4f, WalkOut = 7.6f, WalkOutEnd = 8.9f;
    const float TrollFrom = 1.65f, WaveFrom = 2.2f, WaveTo = 3.85f;
    const float SneakIn = 9.0f, Behind = 10.2f, Notice = 10.55f, Grab = 10.75f, Drag = 11.15f, DragEnd = 11.85f, Duration = 13.2f;

    const string ScenePath = "Assets/Scenes/Shorts_Mirror.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_Mirror.playable";
    const string Folder = "Assets/Shorts/Mirror";
    const string MeshFolder = Folder + "/Meshes";
    const string MaterialFolder = Folder + "/Materials";
    const string TextureFolder = Folder + "/Textures";
    const string ClipFolder = "Assets/Animations";
    const string VolumePath = "Assets/Shorts/Shorts_Volume.asset";
    const string ActorPrefab = HeroPathCharacterBuilder.PrefabFolder + "/question_hero.prefab";
    const int VideoWidth = 1080, VideoHeight = 1920;
    const string RecordingFolder = "Recordings/Shorts_Mirror_<Take>" + ShortsFrameEncoder.FramesSuffix; // relative to the project folder

    // The mirror wall is the plane z = 0: the bathroom in front (+Z), its mirror image behind (-Z)
    const float MirrorHalf = 0.57f, MirrorBottom = 1.0f, MirrorTop = 2.4f, LedWidth = 0.035f;
    const float WallHalf = 2.4f, WallHeight = 3.3f, RoomDepth = 3.2f;
    const float CounterTop = 0.9f, CounterDepth = 0.56f, CounterHalf = 0.95f;
    static readonly Vector3 HeroStart = new Vector3(0f, 0f, 0.8f);       // at the basin, facing the mirror (-Z)
    static readonly Vector3 HeroExit = new Vector3(-1.9f, 0f, 1.3f);     // off the right of the frame
    static readonly Vector3 DoubleStart = new Vector3(0f, 0f, -0.8f);    // his reflection, facing out (+Z)
    static readonly Vector3 SneakStart = new Vector3(-1.75f, 0f, -1.3f); // out of sight in the mirror, on the right
    static readonly Vector3 BehindSpot = new Vector3(-0.16f, 0f, -1.28f); // over the double's shoulder
    static readonly Vector3 GrabSpot = new Vector3(-0.04f, 0f, -1.13f);   // right up against its back (torsos are 0.32 m deep)
    static readonly Vector3 Step = new Vector3(-0.08f, 0f, 0.22f); // his step at the camera, glaring
    const float DragDistance = -2.2f; // to the right of the frame (-X: the camera looks down -Z)
    static readonly Vector3 CameraPosition = new Vector3(-0.86f, 1.82f, 2.6f);
    static readonly Vector3 CameraTarget = new Vector3(0f, 1.42f, 0f);
    const float CameraFov = 52f;

    static readonly string[] Bones =
    {
        "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
        "LeftShoulder", "RightShoulder",
    };
    static readonly string[] Sides = { "Left", "Right" };
    static float Out(string side) => side == "Left" ? -1f : 1f; // sign of Z that swings that side's arm out

    static float armRestOut; // degrees the arms hang out from straight down in the prefab's rest pose
    static bool clavicles;
    static string facePath;
    static Vector3 faceOnHead; // middle of the face, in the head bone's frame
    static Transform[] doubleBones; // a second body posed as the double while the sneak is baked, for GrabHands
    static Vector3 doubleHipsRest;

    // ---------------------------------------------------------------- the act

    // Local bone rotations in degrees added to the rest pose (body facing +Z): arm out = Z with Out(side), arm forward =
    // -X, knee back = +X, spine/head forward = +X, turn right = +Y. `hips` moves the hips in the actor's frame, `move`
    // moves the whole actor in the world.
    class Pose
    {
        public readonly Vector3[] e = new Vector3[Bones.Length];
        public Vector3 hips, move;

        public Pose Rot(string bone, float x, float y, float z)
        {
            e[Array.IndexOf(Bones, bone)] += new Vector3(x, y, z);
            return this;
        }

        public Pose Add(Pose other, float weight = 1f)
        {
            for (int i = 0; i < e.Length; i++) e[i] += other.e[i] * weight;
            hips += other.hips * weight;
            move += other.move * weight;
            return this;
        }
    }

    class Actor
    {
        public string name;
        public Vector3 start;
        public float yaw; // heading at the start, degrees: 0 faces +Z
        public Func<float, Pose> act;
        public Action<GameObject, float> ik; // runs on the posed body each frame, or null
        public (float time, E emotion)[] faces;
    }

    static readonly Actor[] Cast =
    {
        new Actor
        {
            name = "Hero", start = HeroStart, yaw = 180f, act = HeroAct, ik = WashHands,
            faces = new[] { (0f, E.Smile), (BendDown + 0.1f, E.Neutral), (StandUp, E.Suspicious), (TurnStart + 0.2f, E.Angry) },
        },
        new Actor
        {
            name = "Double", start = DoubleStart, yaw = 0f, act = DoubleAct,
            faces = new[] { (0f, E.Smile), (TrollFrom, E.Troll), (TurnStart + 0.45f, E.Evil), (Notice, E.Shocked), (Drag, E.Scared) },
        },
        new Actor
        {
            name = "Sneak", start = SneakStart, yaw = 90f, act = SneakAct, ik = GrabHands,
            faces = new[] { (0f, E.Angry) },
        },
    };

    // At the basin; bends to wash; puzzled at the reflection; turns round to the camera and glares; storms off right
    static Pose HeroAct(float t)
    {
        var p = Idle(t).Add(Wash(t), Env(t, BendDown, StandUp, 0.45f));
        p.Add(new Pose().Rot("Head", -6f, 0f, 10f), Env(t, StandUp + 0.25f, TurnStart + 0.1f, 0.3f));
        // Round to his right, towards the camera, then a little back to his left to walk off
        float yaw = 155f * Smooth(TurnStart, TurnEnd, t);
        yaw = Mathf.Lerp(yaw, 103f, Smooth(WalkOut - 0.25f, WalkOut + 0.2f, t));
        p.Rot("Hips", 0f, yaw, 0f);
        // Glaring: head down, fists clenched at his sides, a step at the camera
        float glare = Env(t, TurnEnd - 0.1f, WalkOut + 0.1f, 0.2f);
        var g = new Pose().Rot("Head", 8f, 0f, 0f).Rot("Spine", 5f, 0f, 0f);
        foreach (var s in Sides) g.Rot(s + "UpperArm", 8f, 0f, Out(s) * 8f).Rot(s + "LowerArm", -25f, 0f, 0f);
        p.Add(g, glare);
        p.move += Step * Smooth(TurnEnd - 0.1f, TurnEnd + 0.4f, t);
        p.Add(Gait(t, WalkOut, WalkOutEnd));
        p.move += (HeroExit - HeroStart - Step) * Along(t, WalkOut, WalkOutEnd);
        return p;
    }

    // Copies him until he bends; then stays up, peers down at him and waves with a trollface; sways grinning while he
    // stares; smug once he has gone; caught from behind and dragged off
    static Pose DoubleAct(float t)
    {
        var p = Idle(t);
        p.Add(new Pose().Rot("Head", 14f, 0f, 0f).Rot("Spine", 4f, 0f, 0f), Env(t, TrollFrom - 0.25f, StandUp - 0.1f, 0.3f));
        // The arm on the screen's left (its right) up in a V, the forearm waggling
        float wave = Env(t, WaveFrom, WaveTo, 0.25f);
        p.Rot("RightUpperArm", -18f * wave, 0f, Out("Right") * 135f * wave)
            .Rot("RightLowerArm", 0f, 0f, Out("Right") * (15f + 28f * Wave(t, 2.4f)) * wave);
        p.Rot("Head", 0f, 0f, 7f * Wave(t, 0.8f) * Env(t, StandUp, Notice, 0.3f));
        // Notices him: a start, head half round to its left, where he is
        float notice = Smooth(Notice, Notice + 0.15f, t);
        p.Rot("Head", -4f * notice, -22f * notice, 0f).Rot("Spine", -4f * notice, 0f, 0f);
        // Hooked under the armpits: arms forced up and out, forearms dangling
        float hooked = Smooth(Grab, Grab + 0.3f, t);
        foreach (var s in Sides) p.Rot(s + "UpperArm", -10f * hooked, 0f, Out(s) * 62f * hooked).Rot(s + "LowerArm", 0f, 0f, -Out(s) * 25f * hooked);
        p.Add(Dragged(t));
        return p;
    }

    // Comes into the mirror from the right behind the double, turns out of the mirror, steps up against its back with his
    // head out past its left shoulder, hooks it under the armpits (GrabHands) and drags it off
    static Pose SneakAct(float t)
    {
        var p = Idle(t).Add(Gait(t, SneakIn, Behind));
        p.move += (BehindSpot - SneakStart) * Along(t, SneakIn, Behind);
        p.Rot("Hips", 0f, -90f * Smooth(Behind - 0.15f, Notice - 0.05f, t), 0f);
        p.move += (GrabSpot - BehindSpot) * Smooth(Notice - 0.1f, Grab + 0.2f, t);
        float peek = Smooth(Behind, Notice, t);
        p.Rot("Head", 6f * peek, 0f, 14f * peek).Rot("Spine", 0f, 0f, 4f * peek);
        p.Add(Dragged(t));
        p.Rot("Spine", -8f * Env(t, Drag, DragEnd + 0.5f, 0.15f), 0f, 0f);
        return p;
    }

    // Both shoot off to the right, the double leaning back, arms flung up
    static Pose Dragged(float t)
    {
        var p = new Pose();
        float s = Mathf.Clamp01((t - Drag) / (DragEnd - Drag));
        p.move.x = DragDistance * s * s;
        float d = Smooth(Drag - 0.1f, Drag + 0.1f, t);
        return p.Add(Gait(t, Drag, DragEnd), 0.6f).Add(new Pose().Rot("Spine", -6f * d, 0f, 0f), 1f);
    }

    // Standing easy, arms hanging along the body
    static Pose Idle(float t)
    {
        float breathe = Wave(t, 0.4f);
        var p = new Pose().Rot("Spine", 1.5f * breathe, 0f, 0f).Rot("Head", -1.5f * breathe, 0f, 0f);
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", 0f, 0f, Out(s) * (3f - armRestOut + 1f * breathe)).Rot(s + "LowerArm", -8f, 0f, Out(s) * 3f);
        return p;
    }

    // Bent over the basin from the hips (legs kept upright); the hands are WashHands'
    static Pose Wash(float t)
    {
        var p = new Pose().Rot("Hips", 32f, 0f, 0f).Rot("Spine", 22f, 0f, 0f).Rot("Head", 12f, 0f, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperLeg", -32f, 0f, 0f);
        return p;
    }

    // Both hands cupped up to the face while he washes, elbows down over the basin, scrubbing up and down in turn
    static void WashHands(GameObject body, float t)
    {
        float w = Env(t, BendDown, StandUp, 0.45f);
        if (w <= 0f) return;
        var head = Find(body, "Head");
        var face = head.TransformPoint(faceOnHead);
        foreach (var s in Sides)
        {
            float scrub = 0.035f * Wave(t, 2.6f) * Out(s);
            var wrist = face + head.forward * 0.13f - head.up * (0.09f - scrub) + head.right * Out(s) * 0.07f;
            var elbows = Vector3.down + head.right * Out(s) * 0.5f;
            ReachFor(Find(body, s + "UpperArm"), Find(body, s + "LowerArm"), Find(body, s + "Hand"), wrist, elbows, w);
        }
    }

    // Hooks both hands under the double's armpits from behind. The double is posed alongside (doubleRef) and its armpits
    // brought from its frame through the world into the sneak's.
    static void GrabHands(GameObject body, float t)
    {
        float w = Smooth(Grab - 0.05f, Grab + 0.3f, t);
        if (w <= 0f) return;
        var d = Array.Find(Cast, a => a.name == "Double");
        var me = Array.Find(Cast, a => a.name == "Sneak");
        var pose = d.act(t);
        LiftShoulders(pose);
        ApplyPose(doubleBones, doubleHipsRest, pose, Quaternion.Inverse(Quaternion.Euler(0f, d.yaw, 0f)));
        var toMe = Matrix4x4.TRS(me.start, Quaternion.Euler(0f, me.yaw, 0f), Vector3.one).inverse
            * Matrix4x4.TRS(d.start, Quaternion.Euler(0f, d.yaw, 0f), Vector3.one);
        var chest = doubleBones[Array.IndexOf(Bones, "Spine")];
        var spine = Find(body, "Spine");
        foreach (var s in Sides)
        {
            // Under its arm joint, out from the torso's side by half a forearm: the forearm runs under the armpit and the
            // hand comes round onto its chest
            var joint = doubleBones[Array.IndexOf(Bones, s + "UpperArm")].position;
            var armpit = joint - chest.up * 0.21f + chest.right * Out(s) * 0.09f - chest.forward * 0.04f;
            var elbows = -spine.up * 0.5f + spine.right * Out(s) * 0.8f - spine.forward * 0.3f;
            ReachFor(Find(body, s + "UpperArm"), Find(body, s + "LowerArm"), Find(body, s + "Hand"), toMe.MultiplyPoint3x4(armpit), elbows, w);
        }
    }

    // Two-bone IK: puts the hand bone on target, the elbow bending towards pole; weight blends from the posed arm
    static void ReachFor(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole, float weight)
    {
        Quaternion upperFrom = upper.localRotation, lowerFrom = lower.localRotation;
        var shoulder = upper.position;
        float a = Vector3.Distance(shoulder, lower.position), b = Vector3.Distance(lower.position, hand.position);
        var toTarget = target - shoulder;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        var dir = toTarget.normalized;
        var bend = Vector3.ProjectOnPlane(pole, dir).normalized;
        float cos = (a * a + d * d - b * b) / (2f * a * d);
        var elbow = shoulder + dir * (a * cos) + bend * (a * Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos)));
        upper.rotation = Quaternion.FromToRotation(lower.position - shoulder, elbow - shoulder) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, shoulder + dir * d - lower.position) * lower.rotation;
        upper.localRotation = Quaternion.Slerp(upperFrom, upper.localRotation, weight);
        lower.localRotation = Quaternion.Slerp(lowerFrom, lower.localRotation, weight);
    }

    // Legs and arms swinging, at full stride between start and end
    static Pose Gait(float t, float start, float end)
    {
        var p = new Pose();
        float gait = Env(t, start, end + 0.1f, 0.12f);
        if (gait <= 0f) return p;
        float phase = (t - start) * 1.7f; // strides per second
        float swing = Mathf.Sin(phase * 2f * Mathf.PI);
        for (int k = 0; k < 2; k++)
        {
            string s = Sides[k];
            float side = k == 0 ? 1f : -1f;
            float knee = Mathf.Max(0f, Mathf.Sin((phase + k * 0.5f) * 2f * Mathf.PI));
            p.Rot(s + "UpperLeg", -26f * swing * side * gait, 0f, 0f).Rot(s + "LowerLeg", 36f * knee * gait, 0f, 0f);
            p.Rot(s + "UpperArm", 18f * swing * side * gait, 0f, 0f);
        }
        p.hips.y += 0.015f * Mathf.Abs(swing) * gait;
        return p;
    }

    // How far along a walk from start to end: eases in, arrives walking
    static float Along(float t, float start, float end)
    {
        float a = Mathf.Clamp01((t - start) / (end - start));
        return a * a * (1.6f - 0.6f * a);
    }

    static float Wave(float t, float hz, float phase = 0f) => Mathf.Sin((t * hz + phase) * 2f * Mathf.PI);
    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    static float Env(float t, float start, float end, float fade) => Smooth(start, start + fade, t) * (1f - Smooth(end - fade, end, t));

    // Arms raised past ShoulderLiftFrom bring their clavicles up with them (taken off the arm so it points the same way)
    const float ShoulderLiftFrom = 70f, ShoulderLiftRate = 0.4f, ShoulderLiftMax = 28f;

    static void LiftShoulders(Pose p)
    {
        if (!clavicles) return;
        foreach (var s in Sides)
        {
            int arm = Array.IndexOf(Bones, s + "UpperArm");
            float lift = Mathf.Clamp((Out(s) * p.e[arm].z - ShoulderLiftFrom) * ShoulderLiftRate, 0f, ShoulderLiftMax);
            p.Rot(s + "Shoulder", 0f, 0f, Out(s) * lift);
            p.e[arm].z -= Out(s) * lift;
        }
    }

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Shorts/Build Mirror Short")]
    public static void Build() => Build(record: false);

    public static void Build(bool record)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Mirror] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var folder in new[] { MeshFolder, MaterialFolder, TextureFolder, ClipFolder, "Assets/Timelines" }) RigUtility.EnsureFolder(folder);
        materials.Clear();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefab);
        if (prefab == null) { Debug.LogError("[Mirror] " + ActorPrefab + " missing: run Tools/Characters/Build main_hero"); return; }
        var hang = Find(prefab, "RightHand").position - Find(prefab, "RightLowerArm").position;
        armRestOut = Mathf.Atan2(Mathf.Abs(hang.x), -hang.y) * Mathf.Rad2Deg;
        clavicles = FindOptional(prefab, "RightShoulder") != null;
        var faceTransform = Find(prefab, "Face");
        facePath = AnimationUtility.CalculateTransformPath(faceTransform, prefab.transform);
        faceOnHead = Find(prefab, "Head").InverseTransformPoint(faceTransform.TransformPoint(faceTransform.GetComponent<MeshFilter>().sharedMesh.bounds.center));

        var clips = new AnimationClip[Cast.Length];
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            var doubleRef = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(doubleRef, stage);
            doubleRef.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            doubleBones = Array.ConvertAll(Bones, b => FindOptional(doubleRef, b));
            doubleHipsRest = doubleBones[0].localPosition;
            for (int i = 0; i < Cast.Length; i++) clips[i] = BakeClip(prefab, stage, Cast[i]);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildStage();
        BuildBathroom();

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

        var director = new GameObject("Timeline_Mirror").AddComponent<PlayableDirector>();
        for (int i = 0; i < Cast.Length; i++)
        {
            var a = Cast[i];
            var rotation = Quaternion.Euler(0f, a.yaw, 0f);
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            actor.name = a.name;
            actor.transform.SetPositionAndRotation(a.start, rotation);
            var track = timeline.CreateTrack<AnimationTrack>(null, a.name);
            track.trackOffset = TrackOffset.ApplyTransformOffsets;
            track.position = a.start;
            track.rotation = rotation;
            var clip = track.CreateClip(clips[i]);
            clip.start = 0;
            clip.duration = Duration;
            ((AnimationPlayableAsset)clip.asset).applyFootIK = false; // the baked clip has no IK goals
            director.SetGenericBinding(track, actor.GetComponent<Animator>());
        }

        if (record) AddRecorderTrack(timeline, take);

        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Mirror] Built {ScenePath}" + (record ? ": press Play to record" : ""));
    }

    // ---------------------------------------------------------------- stage

    // Behind the hero's left shoulder, looking down a little into the mirror. The key light comes from the right almost
    // along the mirror wall, so neither side of it shadows the other through the glass.
    static void BuildStage()
    {
        var rig = new GameObject("Camera Rig");
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.SetParent(rig.transform, false);
        camera.transform.position = CameraPosition;
        camera.transform.LookAt(CameraTarget);
        camera.fieldOfView = CameraFov;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.86f, 0.86f, 0.87f);
        camera.nearClipPlane = 0.05f;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.stopNaN = true; // one stray NaN pixel would bloom into a white blaze

        AddLight("Key Light", new Color(1f, 0.96f, 0.9f), 1.25f, new Vector3(-0.45f, -0.85f, -0.17f), LightShadows.Soft);
        AddLight("Fill Light", new Color(0.88f, 0.92f, 1f), 0.45f, new Vector3(-0.2f, -0.35f, -1f), LightShadows.None);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.8f, 0.8f, 0.82f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.6f, 0.57f);
        RenderSettings.ambientGroundColor = new Color(0.38f, 0.35f, 0.32f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
    }

    // The slatted wooden wall with the LED mirror, the vanity under it, and behind the glass the room's mirror image:
    // the vanity again, a pale room with a door frame and a light switch
    static void BuildBathroom()
    {
        var wood = Lit("Wood Slats", Color.white, 0.25f);
        wood.SetTexture("_BaseMap", WoodTexture());
        var floor = Lit("Floor", Hex("8E8A85"), 0.3f);
        var pale = Lit("Pale Wall", Hex("D8D8DA"), 0.15f);

        var room = new GameObject("Bathroom").transform;
        // The mirror wall, cut round the glass, wood facing the room; it casts no shadow so the mirror side stays lit
        var wall = Part(room, "Mirror Wall", SaveMesh(WallMesh(new[]
        {
            new Rect(-WallHalf, 0f, WallHalf - MirrorHalf, WallHeight), new Rect(MirrorHalf, 0f, WallHalf - MirrorHalf, WallHeight),
            new Rect(-MirrorHalf, 0f, 2f * MirrorHalf, MirrorBottom), new Rect(-MirrorHalf, MirrorTop, 2f * MirrorHalf, WallHeight - MirrorTop),
        }), "Mirror_Wall"), wood);
        wall.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        Box(room, "Floor", new Vector3(2f * WallHalf, 0.05f, RoomDepth), new Vector3(0f, -0.025f, RoomDepth / 2f), floor);

        // LED strip round the glass and the glass itself, a faint sheen over the mirror room
        var led = Emissive("LED", Color.white, 9f);
        float w = LedWidth, h = MirrorTop - MirrorBottom;
        Box(room, "LED Top", new Vector3(2f * MirrorHalf + 2f * w, w, 0.02f), new Vector3(0f, MirrorTop + w / 2f, 0.01f), led);
        Box(room, "LED Bottom", new Vector3(2f * MirrorHalf + 2f * w, w, 0.02f), new Vector3(0f, MirrorBottom - w / 2f, 0.01f), led);
        Box(room, "LED Left", new Vector3(w, h, 0.02f), new Vector3(-MirrorHalf - w / 2f, (MirrorTop + MirrorBottom) / 2f, 0.01f), led);
        Box(room, "LED Right", new Vector3(w, h, 0.02f), new Vector3(MirrorHalf + w / 2f, (MirrorTop + MirrorBottom) / 2f, 0.01f), led);
        var glass = Box(room, "Glass", new Vector3(2f * MirrorHalf, h, 0.002f), new Vector3(0f, (MirrorTop + MirrorBottom) / 2f, -0.002f), Glass());
        glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        BuildVanity(room);

        // The mirror image: the vanity again, flipped through the wall, and the room behind the camera
        var mirrored = new GameObject("Mirror Room").transform;
        mirrored.SetParent(room, false);
        var flip = new GameObject("Vanity Reflection").transform;
        flip.SetParent(mirrored, false);
        flip.localScale = new Vector3(1f, 1f, -1f);
        BuildVanity(flip);
        Box(mirrored, "Floor", new Vector3(2f * WallHalf, 0.05f, RoomDepth), new Vector3(0f, -0.025f, -RoomDepth / 2f), floor);
        Box(mirrored, "Back Wall", new Vector3(2f * WallHalf, WallHeight, 0.05f), new Vector3(0f, WallHeight / 2f, -RoomDepth), pale);
        Box(mirrored, "Left Wall", new Vector3(0.05f, WallHeight, RoomDepth), new Vector3(-WallHalf, WallHeight / 2f, -RoomDepth / 2f), pale);
        Box(mirrored, "Right Wall", new Vector3(0.05f, WallHeight, RoomDepth), new Vector3(WallHalf, WallHeight / 2f, -RoomDepth / 2f), pale);
        var dark = Lit("Door Frame", Hex("2E2C2B"), 0.3f);
        Box(mirrored, "Door Frame", new Vector3(0.09f, 2.15f, 0.06f), new Vector3(0.22f, 1.075f, -RoomDepth + 0.04f), dark);
        Box(mirrored, "Door Head", new Vector3(0.9f, 0.09f, 0.06f), new Vector3(-0.19f, 2.15f, -RoomDepth + 0.04f), dark);
        Box(mirrored, "Switch Plate", new Vector3(0.08f, 0.13f, 0.012f), new Vector3(1.25f, 1.15f, -RoomDepth + 0.03f), Lit("Switch", Hex("F2F2F2"), 0.4f), 0.006f);
        Box(mirrored, "Switch Rocker", new Vector3(0.03f, 0.07f, 0.012f), new Vector3(1.25f, 1.15f, -RoomDepth + 0.04f), Lit("Switch Rocker", Hex("CFCFCF"), 0.4f), 0.004f);
        Box(mirrored, "Towel Rail", new Vector3(0.025f, 0.6f, 0.03f), new Vector3(0.75f, 1.35f, -RoomDepth + 0.05f), dark);
    }

    // Light grey stone top with a sunk basin, a dark wood cabinet with two drawers and black pulls, white towels on the
    // open shelf under it, a black wall tap and two little black dishes
    static void BuildVanity(Transform parent)
    {
        var stone = Lit("Stone", Hex("B9B9BB"), 0.35f);
        var basin = Lit("Basin", Hex("D4D4D6"), 0.45f);
        var cabinet = Lit("Cabinet", Hex("47423D"), 0.3f);
        var black = Lit("Black", Hex("161616"), 0.45f);
        var towel = Lit("Towel", Hex("EDEDED"), 0.05f);
        var v = new GameObject("Vanity").transform;
        v.SetParent(parent, false);
        float z = CounterDepth / 2f;
        Box(v, "Top", new Vector3(2f * CounterHalf, 0.06f, CounterDepth), new Vector3(0f, CounterTop - 0.03f, z), stone, 0.008f);
        Box(v, "Basin", new Vector3(0.7f, 0.004f, 0.36f), new Vector3(-0.12f, CounterTop + 0.001f, z + 0.02f), basin, 0.002f);
        Box(v, "Drain", new Vector3(0.05f, 0.004f, 0.05f), new Vector3(-0.12f, CounterTop + 0.003f, z + 0.04f), black, 0.01f);
        Box(v, "Cabinet", new Vector3(2f * CounterHalf - 0.04f, 0.38f, CounterDepth - 0.04f), new Vector3(0f, CounterTop - 0.06f - 0.19f, z - 0.01f), cabinet);
        Box(v, "Drawer Gap", new Vector3(0.006f, 0.34f, 0.004f), new Vector3(0f, CounterTop - 0.25f, CounterDepth - 0.03f), black);
        Box(v, "Pull Left", new Vector3(0.3f, 0.022f, 0.02f), new Vector3(-0.47f, CounterTop - 0.16f, CounterDepth - 0.02f), black, 0.006f);
        Box(v, "Pull Right", new Vector3(0.3f, 0.022f, 0.02f), new Vector3(0.47f, CounterTop - 0.16f, CounterDepth - 0.02f), black, 0.006f);
        Box(v, "Shelf", new Vector3(2f * CounterHalf - 0.04f, 0.04f, CounterDepth - 0.06f), new Vector3(0f, 0.18f, z - 0.02f), cabinet);
        Box(v, "Side Left", new Vector3(0.04f, CounterTop - 0.06f, CounterDepth - 0.04f), new Vector3(-CounterHalf + 0.04f, (CounterTop - 0.06f) / 2f, z - 0.01f), cabinet);
        Box(v, "Side Right", new Vector3(0.04f, CounterTop - 0.06f, CounterDepth - 0.04f), new Vector3(CounterHalf - 0.04f, (CounterTop - 0.06f) / 2f, z - 0.01f), cabinet);
        for (int i = 0; i < 2; i++)
        {
            Box(v, "Towel " + i, new Vector3(0.4f, 0.09f, 0.32f), new Vector3(-0.5f + i * 0.46f, 0.245f, z), towel, 0.03f);
            Box(v, "Towel Top " + i, new Vector3(0.38f, 0.08f, 0.3f), new Vector3(-0.5f + i * 0.46f, 0.33f, z), towel, 0.03f);
        }
        Box(v, "Tap", new Vector3(0.07f, 0.13f, 0.06f), new Vector3(-0.12f, CounterTop + 0.065f, 0.05f), black, 0.008f);
        Box(v, "Spout", new Vector3(0.04f, 0.035f, 0.14f), new Vector3(-0.12f, CounterTop + 0.11f, 0.13f), black, 0.008f);
        Box(v, "Handle", new Vector3(0.06f, 0.09f, 0.05f), new Vector3(0.02f, CounterTop + 0.045f, 0.05f), black, 0.008f);
        Box(v, "Dish", new Vector3(0.12f, 0.025f, 0.08f), new Vector3(0.24f, CounterTop + 0.0125f, 0.08f), black, 0.006f);
    }

    // ---------------------------------------------------------------- meshes, textures, materials

    // Rectangles in the plane z = 0 facing +Z, UVs in metres along the wall (WoodTexture covers 0.6 x 1.2 m)
    static Mesh WallMesh(Rect[] rects)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        foreach (var r in rects)
        {
            int b = vertices.Count;
            foreach (var c in new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) })
            {
                vertices.Add(new Vector3(c.x, c.y, 0f));
                uvs.Add(new Vector2(c.x / 0.6f, c.y / 1.2f));
            }
            triangles.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Six vertical slats of pale grey-brown wood: each its own tone, a dark groove between them, fine streaky grain
    static Texture2D WoodTexture()
    {
        const int w = 512, h = 1024, slats = 6;
        var rng = new Random(11);
        var tone = new float[slats];
        for (int i = 0; i < slats; i++) tone[i] = 0.92f + 0.16f * (float)rng.NextDouble();
        var streak = new float[w];
        for (int x = 0; x < w; x++) streak[x] = (float)rng.NextDouble();
        var px = new Color32[w * h];
        var baseColor = new Color(0.66f, 0.6f, 0.51f);
        for (int x = 0; x < w; x++)
        {
            int slat = x * slats / w;
            float inSlat = (x * slats % w) / (float)w; // 0..1 across the slat
            float groove = Mathf.Min(inSlat, 1f - inSlat) * w / slats;
            float shade = groove < 3f ? 0.45f + 0.15f * groove : 1f - 0.08f * Mathf.Exp(-(groove - 3f) / 6f);
            float grain = 0.6f * streak[x] + 0.4f * streak[(x + 1) % w];
            for (int y = 0; y < h; y++)
            {
                float wave = 0.5f + 0.5f * Mathf.Sin((y / (float)h * 6f + slat * 1.7f + x * 0.02f) * Mathf.PI);
                float k = tone[slat] * shade * (0.9f + 0.12f * grain * (0.7f + 0.3f * wave));
                var c = baseColor * k;
                px[y * w + x] = new Color(c.r, c.g, c.b, 1f);
            }
        }
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        texture.SetPixels32(px);
        texture.Apply();
        string path = $"{TextureFolder}/Mirror_WoodSlats.png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static readonly Dictionary<string, Mesh> boxes = new Dictionary<string, Mesh>();

    static GameObject Box(Transform parent, string name, Vector3 size, Vector3 position, Material material, float radius = 0f)
    {
        string key = $"Mirror_Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_r{radius:0.###}";
        if (!boxes.TryGetValue(key, out var mesh) || mesh == null)
            boxes[key] = mesh = SaveMesh(RoundedBoxMesh.Create(size, radius, radius > 0f ? 3 : 1), key);
        var go = Part(parent, name, mesh, material);
        go.transform.localPosition = position;
        return go;
    }

    static GameObject Part(Transform parent, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static Mesh SaveMesh(Mesh mesh, string name) => RigUtility.SaveMesh(mesh, $"{MeshFolder}/{name}.asset");

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    static Material Lit(string name, Color color, float smoothness) =>
        SavedMaterial(name, m =>
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
        });

    static Material Emissive(string name, Color color, float intensity) =>
        SavedMaterial(name, m =>
        {
            m.SetColor("_BaseColor", color);
            m.SetColor("_EmissionColor", color * intensity);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });

    // Clear with a faint cool tint and a glossy sheen
    static Material Glass() =>
        SavedMaterial("Glass", m =>
        {
            m.SetColor("_BaseColor", new Color(0.85f, 0.92f, 0.95f, 0.07f));
            m.SetFloat("_Smoothness", 0.95f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        });

    static Material SavedMaterial(string name, Action<Material> setup)
    {
        if (materials.TryGetValue(name, out var material)) return material;
        string path = $"{MaterialFolder}/Mirror_{name.Replace(' ', '_')}.mat";
        material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        setup(material);
        EditorUtility.SetDirty(material);
        materials[name] = material;
        return material;
    }

    static AnimationClip SavedClip(string name)
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

    // ---------------------------------------------------------------- baking

    // Poses a scratch instance frame by frame and reads it back as a humanoid pose: body position/rotation and muscles.
    // `move` is in the world, so it is turned into the actor's frame (it starts facing a.yaw).
    static AnimationClip BakeClip(GameObject prefab, Scene stage, Actor a)
    {
        var go = Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(go, stage);
        // HumanPoseHandler reads the body in world space but writes it relative to the root: pose at the origin
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var bones = Array.ConvertAll(Bones, b => FindOptional(go, b));
        var hipsRest = bones[0].localPosition;
        var toActor = Quaternion.Inverse(Quaternion.Euler(0f, a.yaw, 0f));
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
            float t = f / Fps;
            var pose = a.act(t);
            LiftShoulders(pose);
            ApplyPose(bones, hipsRest, pose, toActor);
            a.ik?.Invoke(go, t);
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

        var clip = SavedClip("Mirror_" + a.name);
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        for (int i = 0; i < names.Count; i++)
        {
            bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), names[i]));
            curves.Add(SampledCurve(samples, i, frames));
        }
        var emotion = new AnimationCurve();
        foreach (var (time, e) in a.faces) emotion.AddKey(StepKey(time, RobloxFace.EmotionKey(e)));
        bindings.Add(EditorCurveBinding.DiscreteCurve(facePath, typeof(RobloxFace), "emotion"));
        curves.Add(emotion);
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

    // Bone rotations as posed; the hips moved in the actor's frame, `move` turned into it from the world
    static void ApplyPose(Transform[] bones, Vector3 hipsRest, Pose pose, Quaternion toActor)
    {
        for (int b = 0; b < bones.Length; b++)
            if (bones[b] != null) bones[b].localRotation = Quaternion.Euler(pose.e[b]);
        bones[0].localPosition = hipsRest + pose.hips + toActor * pose.move;
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

    static Transform FindOptional(GameObject root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    static Transform Find(GameObject root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        throw new ArgumentException($"No {name} under {root.name}");
    }

    static Color Hex(string hex) => RigUtility.Hex(hex);

    static void AddLight(string name, Color color, float intensity, Vector3 direction, LightShadows shadows)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = shadows;
        light.shadowStrength = 0.8f;
        light.transform.rotation = Quaternion.LookRotation(direction.normalized);
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
}
