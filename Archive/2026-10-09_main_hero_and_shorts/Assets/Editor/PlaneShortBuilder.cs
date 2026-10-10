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

// The Roblox half of "kicking the seat in front" (plane_frames.zip): on a plane, seen side-on from the aisle, the hero
// (exclaim_hero) shoves the seat back in front of him over and over. Its passenger, a giant in a propeller cap, turns
// round angry. The hero gets up into the aisle, ready for a fight; the giant stands up and goes through the ceiling. The
// hero looks up, jumps, and sits back down as good as gold. Motion is posed bone by bone and baked into humanoid clips
// (as in FloorSawShortBuilder); the hero's hands reach the shaken seat back by IK. The giant is the giant prefab scaled
// up in the scene. Builds Shorts_Plane with a Timeline; Build(record: true) adds a Recorder track writing 1080x1920 at
// 30 fps in Play Mode (PNG frames -> MP4 via ShortsFrameEncoder). Tweak the beats and re-run Tools/Shorts/Build Plane Short.
public static class PlaneShortBuilder
{
    const float Fps = 30f;
    // Beats, seconds. CrashTime (the giant's head reaching the ceiling) is measured from his bake.
    const float PushFrom = 0.3f, PushTo = 4.6f, Notice = 2.9f, StandFrom = 4.8f, InAisle = 5.6f;
    const float GiantStand = 6.6f, GiantUp = 7.5f, GiantTurn = 7.7f, Hop = 9.3f, SitFrom = 9.55f, Seated = 10.3f;
    const float PanFrom = 9.6f, PanTo = 11.2f, Duration = 12.4f;
    static float crashTime;

    const string ScenePath = "Assets/Scenes/Shorts_Plane.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_Plane.playable";
    const string Folder = "Assets/Shorts/Plane";
    const string MeshFolder = Folder + "/Meshes";
    const string MaterialFolder = Folder + "/Materials";
    const string TextureFolder = Folder + "/Textures";
    const string ClipFolder = "Assets/Animations";
    const string VolumePath = "Assets/Shorts/Shorts_Volume.asset";
    const string HeroPrefab = HeroPathCharacterBuilder.PrefabFolder + "/exclaim_hero.prefab";
    const string GiantPrefab = HeroPathCharacterBuilder.PrefabFolder + "/giant.prefab";
    const int VideoWidth = 1080, VideoHeight = 1920;
    const string RecordingFolder = "Recordings/Shorts_Plane_<Take>" + ShortsFrameEncoder.FramesSuffix; // relative to the project folder

    // The cabin: seats face +X in rows Pitch apart, the aisle seat at z = 0, the aisle towards the camera (-Z), the
    // window wall at WallZ. A seat's origin is where its sitter's hip joints are, over the floor.
    const float Pitch = 1.0f, SeatTop = 0.33f, SeatWidth = 0.6f, WallZ = 1.62f;
    const float BackPivotX = -0.2f, BackThickness = 0.11f, BackHeight = 0.88f, Recline = 10f; // degrees, top towards -X
    const float BinFrontZ = 0.45f, BinBottom = 1.86f, BinTop = 2.5f, CeilingY = 2.72f;
    const float AisleZ = -0.78f;
    static readonly Vector3 GiantScale = new Vector3(1.25f, 1.9f, 1.25f); // when he stands up: tall and thin
    // Normal size in his seat; grows as he stands (a scaler parent at his seat carries it)
    static Vector3 GiantScaleAt(float t) => Vector3.Lerp(Vector3.one, GiantScale, Smooth(GiantStand + 0.1f, GiantUp + 0.1f, t));
    static readonly Vector3 HeroSeat = Vector3.zero;
    static readonly Vector3 GiantSeat = new Vector3(Pitch, 0f, 0f);
    static readonly Vector3 HeroStep = new Vector3(0f, 0f, AisleZ - 0.02f);  // out of his seat into the aisle
    static readonly Vector3 GiantStep = new Vector3(0f, 0f, AisleZ + 0.12f);
    static readonly Vector3 CameraPosition = new Vector3(0.52f, 1.15f, -5f);
    static readonly Vector3 CameraTarget = new Vector3(0.52f, 1.05f, 0f);
    static readonly Vector3 CameraPan = new Vector3(-0.38f, -0.05f, 0.25f); // drifts left onto the window at the end
    const float CameraFov = 44f;
    const float PushHz = 2.1f, PushAmplitude = 12f; // shoves a second, degrees each shove tips the seat back

    static readonly string[] Bones =
    {
        "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
        "LeftShoulder", "RightShoulder",
    };
    static readonly string[] Sides = { "Left", "Right" };
    static float Out(string side) => side == "Left" ? -1f : 1f; // sign of Z that swings that side's arm out

    // Read from the prefab (both actors share the body): arm hang, leg segments, hip height, head top in the head's frame
    static float armRestOut, thigh, shin, ankle, hipRest;
    static Vector3 headTop;
    static bool clavicles;
    static string facePath;
    static readonly List<float> giantHeadTop = new List<float>(); // per frame, metres, from the giant's bake

    // ---------------------------------------------------------------- the act

    // Local bone rotations in degrees added to the rest pose (body facing +Z): arm out = Z with Out(side), arm forward =
    // -X, knee back = +X, spine/head forward = +X, turn right = +Y. `hips` moves the hips in the actor's frame (actor
    // units), `move` moves the whole actor in the world (metres).
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
        public string name, prefab;
        public Vector3 start;
        public float yaw;                    // heading at the start, degrees: 0 faces +Z
        public Func<float, Vector3> scaleAt; // the instance's scale over time
        public Func<float, Pose> act;
        public Action<GameObject, float> ik; // runs on the posed body each frame (in the actor's frame), or null
        public (float time, E emotion)[] faces;

        // From the world into the frame the actor is baked in (at the origin, unscaled)
        public Matrix4x4 FromWorld => Matrix4x4.TRS(start, Quaternion.Euler(0f, yaw, 0f), scaleAt(0f)).inverse;
    }

    static readonly Actor Hero = new Actor
    {
        name = "Hero", prefab = HeroPrefab, start = HeroSeat, yaw = 90f, scaleAt = t => Vector3.one, act = HeroAct, ik = HeroHands,
        faces = new[] { (0f, E.Angry) }, // the rest is keyed in Build once crashTime is known
    };

    static readonly Actor Giant = new Actor
    {
        name = "Giant", prefab = GiantPrefab, start = GiantSeat, yaw = 90f, scaleAt = GiantScaleAt, act = GiantAct, ik = MeasureGiant,
        faces = new[] { (0f, E.Neutral), (Notice, E.Angry) },
    };

    static readonly Actor[] Cast = { Giant, Hero }; // the giant first: his bake times the crash the hero reacts to

    // Shoves the seat in front, gets up into the aisle and squares up to it; looks up as the giant goes through the
    // ceiling, jumps, and sits back down with his hands on his knees
    static Pose HeroAct(float t)
    {
        float seated = 1f - Smooth(StandFrom, StandFrom + 0.55f, t) + Smooth(SitFrom + 0.2f, Seated, t);
        var p = Idle(t).Add(Sit(1f), seated);
        // Face turned a little to the camera (on his right) so it reads
        p.Rot("Head", 0f, 24f, 0f);
        // Shoving: leaning in with every push (the hands are PushHands')
        float push = Env(t, PushFrom, PushTo, 0.25f);
        p.Rot("Spine", (5f + 7f * Shove(t)) * push, 0f, 0f).Rot("Head", -4f * push, 0f, 0f);
        // Out into the aisle (to his right), then back
        p.move += HeroStep * (Smooth(StandFrom + 0.2f, InAisle, t) - Smooth(SitFrom, Seated - 0.25f, t));
        p.Add(Gait(t, StandFrom + 0.25f, InAisle - 0.1f), 0.5f).Add(Gait(t, SitFrom, SitFrom + 0.45f), 0.5f);
        // Squaring up: fists at his sides, chin up at the giant
        float glare = Env(t, InAisle - 0.2f, Hop, 0.25f);
        var g = new Pose().Rot("Head", -10f, 0f, 0f);
        foreach (var s in Sides) g.Rot(s + "UpperArm", 8f, 0f, Out(s) * 10f).Rot(s + "LowerArm", -35f, 0f, 0f);
        p.Add(g, glare);
        // Looks right up at him as he goes through the ceiling
        float look = Env(t, crashTime - 0.15f, Hop + 0.1f, 0.25f);
        p.Rot("Head", -26f * look, 0f, 0f).Rot("Spine", -5f * look, 0f, 0f);
        // A startled jump
        float hop = Mathf.Clamp01((t - Hop) / 0.32f);
        if (hop > 0f && hop < 1f)
        {
            p.hips.y += 0.14f * Mathf.Sin(hop * Mathf.PI);
            foreach (var s in Sides) p.Rot(s + "UpperArm", -20f * Mathf.Sin(hop * Mathf.PI), 0f, Out(s) * 25f * Mathf.Sin(hop * Mathf.PI));
        }
        // Sitting nicely, back straight (hands on his knees: HeroHands)
        p.Rot("Spine", -3f * Smooth(SitFrom + 0.3f, Seated, t), 0f, 0f);
        return p;
    }

    // Sits jolted by the shoves, turns round glaring at the hero, stands up into the aisle (through the ceiling) and turns to face him
    static Pose GiantAct(float t)
    {
        float seated = 1f - Smooth(GiantStand, GiantUp, t);
        var p = Idle(t).Add(Sit(GiantScaleAt(t).y), seated);
        // Thrown forward with his seat back at every shove, his head snapping after
        float shove = Shove(t) * Env(t, PushFrom, PushTo, 0.25f);
        p.Rot("Spine", PushAmplitude * 1.2f * shove, 0f, 0f).Rot("Head", 6f * Shove(t - 0.08f) * Env(t, PushFrom, PushTo, 0.25f), 0f, 0f);
        p.hips.z += 0.05f * shove;
        float look = Env(t, Notice - 0.25f, GiantStand + 0.2f, 0.3f);
        p.Rot("Head", 14f * look, 62f * look, 0f).Rot("Spine", 0f, 16f * look, 0f);
        p.move += GiantStep * Smooth(GiantStand + 0.1f, GiantUp, t);
        p.Rot("Hips", 0f, 180f * Smooth(GiantTurn, GiantTurn + 0.7f, t), 0f);
        p.Add(Gait(t, GiantTurn, GiantTurn + 0.7f), 0.35f);
        return p;
    }

    // How hard the seat is being shoved at t: 0 between shoves, 1 at the end of each
    static float Shove(float t)
    {
        if (t < PushFrom) return 0f;
        return Mathf.Pow(Mathf.Max(0f, Mathf.Sin((t - PushFrom) * PushHz * 2f * Mathf.PI)), 0.7f);
    }

    // The giant's seat back: reclined, tipped forward by every shove
    static float BackAngle(float t) => Recline - PushAmplitude * Shove(t) * Env(t, PushFrom, PushTo, 0.25f);

    // A point on the giant's seat back (back-local: x from its front face, y up from the pivot) in the world
    static Vector3 OnGiantBack(float t, float x, float y, float z) =>
        GiantSeat + new Vector3(BackPivotX, SeatTop, z) + Quaternion.Euler(0f, 0f, BackAngle(t)) * new Vector3(x, y, 0f);

    // Shoving: both hands flat on the back of the seat in front, elbows out, riding it as it rocks. At the end: hands
    // resting on his knees.
    static void HeroHands(GameObject body, float t)
    {
        float good = Smooth(SitFrom + 0.3f, Seated, t);
        if (good > 0f)
            foreach (var s in Sides)
            {
                var knee = Find(body, s + "LowerLeg").position;
                ReachFor(Find(body, s + "UpperArm"), Find(body, s + "LowerArm"), Find(body, s + "Hand"),
                    knee + new Vector3(0f, 0.17f, -0.1f), new Vector3(Out(s) * 0.6f, -0.2f, -0.6f), good);
            }
        float w = Env(t, PushFrom, PushTo, 0.25f);
        if (w <= 0f) return;
        var toMe = Hero.FromWorld;
        var rear = Quaternion.Euler(0f, 0f, BackAngle(t)) * Vector3.left;
        foreach (var s in Sides)
        {
            float z = s == "Left" ? 0.15f : -0.15f; // facing +X his left is +Z
            var palm = OnGiantBack(t, -BackThickness, 0.62f, z);
            var wrist = palm + rear * 0.2f;
            var elbows = Vector3.down * 0.6f + new Vector3(0f, 0f, z * 4f);
            ReachFor(Find(body, s + "UpperArm"), Find(body, s + "LowerArm"), Find(body, s + "Hand"),
                toMe.MultiplyPoint3x4(wrist), toMe.MultiplyVector(elbows), w);
        }
    }

    // Records how high the giant's head goes (world metres) so the ceiling can break as it gets there
    static void MeasureGiant(GameObject body, float t)
    {
        var top = Find(body, "Head").TransformPoint(headTop);
        giantHeadTop.Add(Giant.start.y + top.y * Giant.scaleAt(t).y);
    }

    // Seated on a cushion at SeatTop: thighs forward (tilted up for a giant on a normal seat), shins down to the floor
    static Pose Sit(float scale)
    {
        float hip = SeatTop / scale + ThighHalf;
        float up = Mathf.Asin(Mathf.Clamp((shin + ankle - hip) / thigh, -1f, 1f)) * Mathf.Rad2Deg;
        var p = new Pose().Rot("Spine", -4f, 0f, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperLeg", -(90f + up), 0f, 0f).Rot(s + "LowerLeg", 90f + up, 0f, 0f);
        p.hips.y = hip - hipRest;
        return p;
    }

    const float ThighHalf = 0.125f; // the hip joint above the underside of the thigh, actor units

    // Standing easy, arms hanging along the body
    static Pose Idle(float t)
    {
        float breathe = Wave(t, 0.4f);
        var p = new Pose().Rot("Spine", 1.5f * breathe, 0f, 0f).Rot("Head", -1.5f * breathe, 0f, 0f);
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", 0f, 0f, Out(s) * (3f - armRestOut + 1f * breathe)).Rot(s + "LowerArm", -8f, 0f, Out(s) * 3f);
        return p;
    }

    // Legs and arms swinging, at full stride between start and end
    static Pose Gait(float t, float start, float end)
    {
        var p = new Pose();
        float gait = Env(t, start, end + 0.1f, 0.12f);
        if (gait <= 0f) return p;
        float phase = (t - start) * 1.7f;
        float swing = Mathf.Sin(phase * 2f * Mathf.PI);
        for (int k = 0; k < 2; k++)
        {
            string s = Sides[k];
            float side = k == 0 ? 1f : -1f;
            float knee = Mathf.Max(0f, Mathf.Sin((phase + k * 0.5f) * 2f * Mathf.PI));
            p.Rot(s + "UpperLeg", -26f * swing * side * gait, 0f, 0f).Rot(s + "LowerLeg", 36f * knee * gait, 0f, 0f);
            p.Rot(s + "UpperArm", 18f * swing * side * gait, 0f, 0f);
        }
        return p;
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

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Shorts/Build Plane Short")]
    public static void Build() => Build(record: false);

    public static void Build(bool record)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Plane] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var folder in new[] { MeshFolder, MaterialFolder, TextureFolder, ClipFolder, "Assets/Timelines" }) RigUtility.EnsureFolder(folder);
        materials.Clear();

        var heroPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPrefab);
        var giantPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GiantPrefab);
        if (heroPrefab == null || giantPrefab == null) { Debug.LogError("[Plane] exclaim_hero/giant prefab missing: run Tools/Characters/Build main_hero"); return; }
        var hang = Find(heroPrefab, "RightHand").position - Find(heroPrefab, "RightLowerArm").position;
        armRestOut = Mathf.Atan2(Mathf.Abs(hang.x), -hang.y) * Mathf.Rad2Deg;
        clavicles = FindOptional(heroPrefab, "RightShoulder") != null;
        facePath = AnimationUtility.CalculateTransformPath(Find(heroPrefab, "Face"), heroPrefab.transform);
        hipRest = Find(heroPrefab, "LeftUpperLeg").position.y;
        thigh = Vector3.Distance(Find(heroPrefab, "LeftUpperLeg").position, Find(heroPrefab, "LeftLowerLeg").position);
        shin = Vector3.Distance(Find(heroPrefab, "LeftLowerLeg").position, Find(heroPrefab, "LeftFoot").position);
        ankle = Find(heroPrefab, "LeftFoot").position.y;
        var headMesh = Find(heroPrefab, "HeadMesh").GetComponent<SkinnedMeshRenderer>().sharedMesh.bounds;
        float headTopY = (headMesh.center.y + headMesh.extents.y) * Find(heroPrefab, "HeadMesh").lossyScale.y;
        var head = Find(heroPrefab, "Head");
        headTop = new Vector3(0f, headTopY - head.position.y, 0f);

        // The giant first: the hero's reactions are keyed to the moment his head reaches the ceiling
        var clips = new Dictionary<Actor, AnimationClip>();
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            giantHeadTop.Clear();
            clips[Giant] = BakeClip(giantPrefab, stage, Giant);
            int hit = giantHeadTop.FindIndex(y => y > CeilingY);
            crashTime = hit < 0 ? GiantUp : hit / Fps;
            Hero.faces = new[]
            {
                (0f, E.Angry), (crashTime - 0.05f, E.Surprised), (crashTime + 0.9f, E.Neutral), (Hop, E.Shocked), (Seated - 0.2f, E.Smile),
            };
            clips[Hero] = BakeClip(heroPrefab, stage, Hero);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = BuildStage();
        var giantBack = BuildCabin();
        var (hole, debris, debrisClip) = BuildCrash();

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

        var director = new GameObject("Timeline_Plane").AddComponent<PlayableDirector>();
        GameObject giant = null, scaler = null;
        foreach (var a in Cast)
        {
            var prefab = a == Giant ? giantPrefab : heroPrefab;
            var rotation = Quaternion.Euler(0f, a.yaw, 0f);
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            actor.name = a.name;
            // The giant stands in a scaler at his seat that grows him; his clip plays from its origin
            var offset = a.start;
            if (a == Giant)
            {
                giant = actor;
                scaler = new GameObject("Giant Scaler");
                scaler.transform.SetParent(new GameObject("Giant Rig").transform, false);
                scaler.transform.position = a.start;
                actor.transform.SetParent(scaler.transform, false);
                offset = Vector3.zero;
            }
            actor.transform.localPosition = offset;
            actor.transform.localRotation = rotation;
            var track = timeline.CreateTrack<AnimationTrack>(null, a.name);
            track.trackOffset = TrackOffset.ApplyTransformOffsets;
            track.position = offset;
            track.rotation = rotation;
            AddClip(track, clips[a]);
            foreach (var c in track.GetClips()) ((AnimationPlayableAsset)c.asset).applyFootIK = false; // the baked clips have no IK goals
            director.SetGenericBinding(track, actor.GetComponent<Animator>());
        }

        var propeller = PropellerCap(Find(giant, "Head"));
        // Generic clips animate a child of the bound object: a clip on the bound object's own transform is taken as root
        // motion and replayed from the track's origin
        Bind(timeline, director, "Propeller", propeller.transform.parent.gameObject, PropellerClip());
        Bind(timeline, director, "Giant growth", scaler.transform.parent.gameObject, GrowthClip());
        Bind(timeline, director, "Giant's seat back", giantBack.transform.parent.gameObject, SeatBackClip());
        Bind(timeline, director, "Camera", camera.transform.parent.gameObject, CameraClip(camera));
        Bind(timeline, director, "Debris", debris, debrisClip);
        Activate(timeline, director, "Hole", hole, crashTime);
        Activate(timeline, director, "Debris shown", debris, crashTime);

        if (record) AddRecorderTrack(timeline, take);

        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Plane] Built {ScenePath} (the giant's head hits the ceiling at {crashTime:F2} s)" + (record ? ": press Play to record" : ""));
    }

    static void AddClip(AnimationTrack track, AnimationClip clip)
    {
        var timelineClip = track.CreateClip(clip);
        timelineClip.start = 0;
        timelineClip.duration = Duration;
    }

    // A generic clip on `target` (given an Animator), over the whole short
    static void Bind(TimelineAsset timeline, PlayableDirector director, string name, GameObject target, AnimationClip clip)
    {
        var animator = target.GetComponent<Animator>();
        if (animator == null) animator = target.AddComponent<Animator>();
        var track = timeline.CreateTrack<AnimationTrack>(null, name);
        AddClip(track, clip);
        director.SetGenericBinding(track, animator);
    }

    // `target` hidden until `from`, then shown to the end
    static void Activate(TimelineAsset timeline, PlayableDirector director, string name, GameObject target, float from)
    {
        var track = timeline.CreateTrack<ActivationTrack>(null, name);
        var clip = track.CreateDefaultClip();
        clip.start = from;
        clip.duration = Duration - from;
        track.postPlaybackState = ActivationTrack.PostPlaybackState.Active;
        director.SetGenericBinding(track, target);
        target.SetActive(false);
    }

    // ---------------------------------------------------------------- stage

    // Side-on from across the aisle, a little below the overhead bins; soft, even cabin light
    static Camera BuildStage()
    {
        var rig = new GameObject("Camera Rig");
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.SetParent(rig.transform, false);
        camera.transform.position = CameraPosition;
        camera.transform.LookAt(CameraTarget);
        camera.fieldOfView = CameraFov;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.9f, 0.9f, 0.91f);
        camera.nearClipPlane = 0.05f;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.stopNaN = true; // one stray NaN pixel would bloom into a white blaze

        AddLight("Cabin Light", new Color(1f, 0.98f, 0.94f), 1.1f, new Vector3(0.25f, -1f, 0.35f), LightShadows.Soft);
        AddLight("Fill Light", new Color(0.9f, 0.93f, 1f), 0.5f, new Vector3(-0.2f, -0.3f, 1f), LightShadows.None);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.82f, 0.83f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.66f, 0.66f, 0.66f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.36f, 0.4f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
        return camera;
    }

    // Eight rows of three seats, the window wall with windows onto the sky, overhead bins with reading-light panels, the
    // ceiling over the aisle and the carpet. Returns the giant's seat back, which rocks.
    static GameObject BuildCabin()
    {
        var cabin = new GameObject("Cabin").transform;
        var white = Lit("Cabin White", Hex("E9EAEC"), 0.35f);
        var bin = Lit("Bin", Hex("F3F3F4"), 0.4f);
        var seam = Lit("Seam", Hex("9EA2A8"), 0.3f);
        var panel = Lit("Panel", Hex("C9CBCF"), 0.3f);
        var carpet = Lit("Carpet", Hex("3B4358"), 0.1f);
        var stripe = Lit("Aisle Stripe", Hex("A9ADB5"), 0.5f);
        var sky = Emissive("Sky", Hex("8FC6F2"), 1.1f);
        var frame = Lit("Window Frame", Hex("D5D7DB"), 0.4f);
        var glow = Emissive("Light Strip", Hex("FFFFFF"), 2.2f);

        const float xFrom = -3.6f, xTo = 4.2f;
        float length = xTo - xFrom, mid = (xFrom + xTo) / 2f;
        Box(cabin, "Floor", new Vector3(length, 0.05f, WallZ + 2.4f), new Vector3(mid, -0.025f, (WallZ - 2.4f) / 2f), carpet);
        Box(cabin, "Aisle Stripe", new Vector3(length, 0.006f, 0.04f), new Vector3(mid, 0.003f, AisleZ - 0.55f), stripe);
        Box(cabin, "Window Wall", new Vector3(length, CeilingY, 0.06f), new Vector3(mid, CeilingY / 2f, WallZ + 0.03f), white);
        Box(cabin, "Ceiling", new Vector3(length, 0.05f, WallZ + 2.4f), new Vector3(mid, CeilingY + 0.025f, (WallZ - 2.4f) / 2f), white);
        Box(cabin, "Light Strip", new Vector3(length, 0.025f, 0.02f), new Vector3(mid, BinBottom - 0.06f, WallZ - 0.02f), glow);

        // Overhead bins: one long white box with seams between the doors and a panel of reading lights under each row
        float binDepth = WallZ - BinFrontZ;
        Box(cabin, "Bins", new Vector3(length, BinTop - BinBottom, binDepth), new Vector3(mid, (BinTop + BinBottom) / 2f, BinFrontZ + binDepth / 2f), bin, 0.04f);
        var dark = Lit("Dark Grey", Hex("5A5E66"), 0.4f);
        for (float x = xFrom + 0.6f; x < xTo; x += 2f * Pitch)
        {
            Box(cabin, "Bin Seam", new Vector3(0.012f, BinTop - BinBottom - 0.04f, 0.01f), new Vector3(x, (BinTop + BinBottom) / 2f, BinFrontZ - 0.002f), seam);
            Box(cabin, "Bin Latch", new Vector3(0.16f, 0.035f, 0.02f), new Vector3(x + Pitch, BinTop - 0.12f, BinFrontZ - 0.008f), dark, 0.008f);
        }
        for (int k = -4; k <= 4; k++)
        {
            float x = k * Pitch;
            Box(cabin, "Reading Panel", new Vector3(0.34f, 0.02f, 0.2f), new Vector3(x + 0.05f, BinBottom - 0.01f, BinFrontZ + 0.18f), panel, 0.006f);
            Box(cabin, "Reading Light", new Vector3(0.05f, 0.01f, 0.05f), new Vector3(x - 0.04f, BinBottom - 0.022f, BinFrontZ + 0.18f), glow, 0.01f);
            Box(cabin, "Reading Light", new Vector3(0.05f, 0.01f, 0.05f), new Vector3(x + 0.04f, BinBottom - 0.022f, BinFrontZ + 0.18f), glow, 0.01f);
            // Window: a pale rounded frame on the wall with the sky in it
            Box(cabin, "Window Frame", new Vector3(0.36f, 0.48f, 0.02f), new Vector3(x - 0.12f, 1.3f, WallZ - 0.005f), frame, 0.12f);
            Box(cabin, "Window", new Vector3(0.27f, 0.38f, 0.02f), new Vector3(x - 0.12f, 1.3f, WallZ - 0.012f), sky, 0.1f);
        }

        GameObject giantBack = null;
        for (int k = -4; k <= 4; k++)
            for (int c = 0; c < 3; c++)
            {
                var back = Seat(cabin, new Vector3(k * Pitch, 0f, c * 0.62f), c == 0);
                if (k == 1 && c == 0) giantBack = back;
            }
        return giantBack;
    }

    // Blue fabric cushion and back, pale plastic armrests and side frame, a tray table on the back, grey legs.
    // Returns the back, whose pivot is the bottom of its front face.
    static GameObject Seat(Transform parent, Vector3 origin, bool aisle)
    {
        var fabric = Lit("Seat Fabric", Hex("2F4C8C"), 0.15f);
        var plastic = Lit("Seat Plastic", Hex("E6DED0"), 0.35f);
        var metal = Lit("Seat Metal", Hex("9EA3A9"), 0.6f);
        var seat = new GameObject("Seat").transform;
        seat.SetParent(parent, false);
        seat.localPosition = origin;
        float w = SeatWidth;
        Box(seat, "Cushion", new Vector3(0.52f, 0.13f, w - 0.04f), new Vector3(0.04f, SeatTop - 0.065f, 0f), fabric, 0.04f);
        Box(seat, "Pan", new Vector3(0.5f, 0.05f, w - 0.06f), new Vector3(0.03f, SeatTop - 0.15f, 0f), plastic, 0.015f);
        foreach (float side in new[] { -1f, 1f })
        {
            Box(seat, "Leg", new Vector3(0.04f, SeatTop - 0.16f, 0.04f), new Vector3(0.18f, (SeatTop - 0.16f) / 2f, side * (w / 2f - 0.06f)), metal);
            Box(seat, "Leg", new Vector3(0.04f, SeatTop - 0.16f, 0.04f), new Vector3(-0.14f, (SeatTop - 0.16f) / 2f, side * (w / 2f - 0.06f)), metal);
            Box(seat, "Foot Rail", new Vector3(0.4f, 0.03f, 0.03f), new Vector3(0.02f, 0.03f, side * (w / 2f - 0.06f)), metal);
        }
        // The armrest on the aisle side is part of a pale side frame running down to the floor
        Box(seat, "Armrest", new Vector3(0.42f, 0.05f, 0.06f), new Vector3(0.02f, SeatTop + 0.2f, -w / 2f), plastic, 0.02f);
        Box(seat, "Armrest Post", new Vector3(0.07f, 0.24f, 0.05f), new Vector3(-0.15f, SeatTop + 0.06f, -w / 2f), plastic, 0.02f);
        if (aisle) Box(seat, "Side Frame", new Vector3(0.5f, 0.2f, 0.04f), new Vector3(0.02f, SeatTop - 0.08f, -w / 2f - 0.01f), plastic, 0.03f);

        var back = new GameObject("Back").transform;
        back.SetParent(seat, false);
        back.localPosition = new Vector3(BackPivotX, SeatTop, 0f);
        back.localRotation = Quaternion.Euler(0f, 0f, Recline);
        Box(back, "Back Cushion", new Vector3(BackThickness, BackHeight, w - 0.04f), new Vector3(-BackThickness / 2f, BackHeight / 2f, 0f), fabric, 0.05f);
        Box(back, "Back Shell", new Vector3(0.03f, BackHeight * 0.62f, w - 0.1f), new Vector3(-BackThickness - 0.01f, BackHeight * 0.36f, 0f), plastic, 0.012f);
        Box(back, "Tray", new Vector3(0.025f, 0.3f, w - 0.16f), new Vector3(-BackThickness - 0.035f, BackHeight * 0.4f, 0f), plastic, 0.01f);
        Box(back, "Headrest", new Vector3(BackThickness + 0.015f, 0.2f, w - 0.1f), new Vector3(-BackThickness / 2f, BackHeight - 0.12f, 0f), fabric, 0.05f);
        return back.gameObject;
    }

    // The hole the giant's head makes in the ceiling over the aisle (shown from crashTime) and bits of ceiling falling
    static (GameObject hole, GameObject debris, AnimationClip clip) BuildCrash()
    {
        var at = GiantSeat + GiantStep;
        var hole = Part(null, "Ceiling Hole", SaveMesh(FlatQuad(1.25f), "Plane_HoleQuad"), HoleMaterial());
        hole.transform.position = new Vector3(at.x, CeilingY - 0.004f, at.z);
        hole.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // facing down
        hole.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var debris = new GameObject("Debris");
        var rng = new Random(5);
        var chip = Lit("Ceiling Chip", Hex("E4E5E8"), 0.3f);
        var chips = new List<(Transform t, Vector3 v, Vector3 spin, float size)>();
        for (int i = 0; i < 9; i++)
        {
            float size = 0.05f + 0.07f * (float)rng.NextDouble();
            var go = Box(debris.transform, "Chip " + i, new Vector3(size, size * 0.35f, size * 0.8f), Vector3.zero, chip, 0.005f);
            var v = new Vector3((float)rng.NextDouble() - 0.5f, -0.2f * (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f) * 1.6f;
            var spin = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()) * 720f - Vector3.one * 360f;
            chips.Add((go.transform, v, spin, size));
        }
        var clip = SavedClip("Plane_Debris");
        var start = new Vector3(at.x, CeilingY - 0.05f, at.z);
        int frames = Mathf.RoundToInt(Duration * Fps);
        string[] axes = { "x", "y", "z" };
        foreach (var (t, v, spin, size) in chips)
        {
            var pos = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
            var rot = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
            float landed = -1f;
            Vector3 rest = Vector3.zero, restSpin = Vector3.zero;
            for (int f = 0; f <= frames; f++)
            {
                float time = f / Fps, s = Mathf.Max(0f, time - crashTime);
                var p = start + new Vector3(v.x * s, v.y * s - 4.9f * s * s, v.z * s) + new Vector3(0.25f * v.x, 0f, 0.25f * v.z);
                var r = spin * s;
                if (landed < 0f && p.y <= size * 0.2f) { landed = time; rest = new Vector3(p.x, size * 0.18f, p.z); restSpin = new Vector3(0f, r.y, 0f); }
                if (landed >= 0f) { p = rest; r = restSpin; }
                for (int a = 0; a < 3; a++) { pos[a].Add(new Keyframe(time, p[a])); rot[a].Add(new Keyframe(time, r[a])); }
            }
            string path = t.name;
            for (int a = 0; a < 3; a++)
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + axes[a]), Linear(pos[a]));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw." + axes[a]), Linear(rot[a]));
            }
        }
        EditorUtility.SetDirty(clip);
        return (hole, debris, clip);
    }

    // A beanie in red, blue, yellow and blue quarters with a grey peak and a propeller on top, on the giant's head.
    // Returns the propeller, which spins.
    static GameObject PropellerCap(Transform head)
    {
        // In the head bone's frame (actor units): the head is a rounded box 0.35 wide, 0.39 deep, its top 0.38 up; the
        // band sits just above the eyes
        const float halfWidth = 0.19f, halfDepth = 0.21f, band = 0.06f, height = 0.17f, baseY = 0.3f;
        var cap = new GameObject("Propeller Cap").transform;
        cap.SetParent(head, false);
        cap.localPosition = new Vector3(0f, baseY, -0.022f);
        cap.localRotation = Quaternion.Euler(0f, 180f, 0f); // worn backwards, as in the original
        var quarters = new[] { Lit("Cap Red", Hex("E3262B"), 0.35f), Lit("Cap Blue", Hex("1F4FD6"), 0.35f), Lit("Cap Yellow", Hex("F2C230"), 0.35f), Lit("Cap Blue", Hex("1F4FD6"), 0.35f) };
        var dome = new GameObject("Dome");
        dome.transform.SetParent(cap, false);
        dome.AddComponent<MeshFilter>().sharedMesh = SaveMesh(BeanieMesh(halfWidth, halfDepth, band, height), "Plane_CapDome");
        dome.AddComponent<MeshRenderer>().sharedMaterials = quarters;
        Part(cap, "Peak", SaveMesh(PeakMesh(halfWidth, halfDepth, 0.13f), "Plane_CapPeak"), Lit("Cap Grey", Hex("8D9096"), 0.3f));
        Box(cap, "Button", new Vector3(0.05f, 0.03f, 0.05f), new Vector3(0f, band + height + 0.01f, 0f), quarters[2], 0.02f);
        var propeller = new GameObject("Propeller");
        propeller.transform.SetParent(cap, false);
        propeller.transform.localPosition = new Vector3(0f, band + height + 0.045f, 0f);
        Box(propeller.transform, "Stem", new Vector3(0.016f, 0.05f, 0.016f), new Vector3(0f, -0.02f, 0f), quarters[2]);
        Box(propeller.transform, "Blade", new Vector3(0.3f, 0.008f, 0.05f), Vector3.zero, quarters[1], 0.004f).transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        Box(propeller.transform, "Blade", new Vector3(0.05f, 0.008f, 0.3f), Vector3.zero, quarters[0], 0.004f).transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        return propeller;
    }

    // A beanie hugging a boxy head: rounded-square in plan (a superellipse), straight sides `band` high, then a dome
    // `height` high; four submeshes, the quarters round the vertical axis
    static Mesh BeanieMesh(float halfWidth, float halfDepth, float band, float height)
    {
        const int perQuarter = 12, rings = 10;
        const float squareness = 4f;
        Vector2 Plan(float lon)
        {
            float c = Mathf.Cos(lon), s = Mathf.Sin(lon);
            return new Vector2(halfWidth * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / squareness),
                halfDepth * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / squareness));
        }
        var vertices = new List<Vector3>();
        var mesh = new Mesh { subMeshCount = 4 };
        var triangles = new List<int>[4];
        for (int q = 0; q < 4; q++)
        {
            triangles[q] = new List<int>();
            int b = vertices.Count;
            for (int i = 0; i <= perQuarter; i++)
            {
                var plan = Plan((q * 90f + i * 90f / perQuarter - 45f) * Mathf.Deg2Rad);
                vertices.Add(new Vector3(plan.x, 0f, plan.y)); // bottom of the band
                for (int j = 0; j <= rings; j++)
                {
                    float lat = j * 90f / rings * Mathf.Deg2Rad;
                    vertices.Add(new Vector3(plan.x * Mathf.Cos(lat), band + height * Mathf.Sin(lat), plan.y * Mathf.Cos(lat)));
                }
            }
            int column = rings + 2;
            for (int i = 0; i < perQuarter; i++)
                for (int j = 0; j < column - 1; j++)
                {
                    int a = b + i * column + j, c = a + column;
                    triangles[q].AddRange(new[] { a, c, a + 1, c, c + 1, a + 1 }); // outward faces front
                }
        }
        mesh.SetVertices(vertices);
        for (int q = 0; q < 4; q++) mesh.SetTriangles(triangles[q], q);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // The peak: out from the front of the band, rounded at the tip, dipping a little; both sides showing
    static Mesh PeakMesh(float halfWidth, float halfDepth, float reach)
    {
        const int n = 16;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= n; i++)
        {
            float u = -0.85f + 1.7f * i / n; // across, in half widths
            vertices.Add(new Vector3(u * halfWidth, 0.004f, halfDepth * 0.97f));
            vertices.Add(new Vector3(u * halfWidth * 1.04f, -0.025f, halfDepth + reach * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u / 0.81f))));
        }
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i;
            triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });  // top
            triangles.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });  // underside
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------- clips

    static AnimationClip PropellerClip()
    {
        var clip = SavedClip("Plane_Propeller");
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Propeller", typeof(Transform), "localEulerAnglesRaw.y"),
            Linear(new List<Keyframe> { new Keyframe(0f, 0f), new Keyframe(Duration, 360f * 2.5f * Duration) }));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimationClip GrowthClip()
    {
        var clip = SavedClip("Plane_GiantGrowth");
        var keys = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
        for (int f = 0; f <= Mathf.RoundToInt(Duration * Fps); f++)
        {
            var s = GiantScaleAt(f / Fps);
            for (int a = 0; a < 3; a++) keys[a].Add(new Keyframe(f / Fps, s[a]));
        }
        string[] axes = { "x", "y", "z" };
        for (int a = 0; a < 3; a++)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Giant Scaler", typeof(Transform), "m_LocalScale." + axes[a]), Linear(keys[a]));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // The giant's seat back (on its seat) tipping with every shove
    static AnimationClip SeatBackClip()
    {
        var clip = SavedClip("Plane_SeatBack");
        var keys = new List<Keyframe>();
        for (int f = 0; f <= Mathf.RoundToInt(Duration * Fps); f++) keys.Add(new Keyframe(f / Fps, BackAngle(f / Fps)));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Back", typeof(Transform), "localEulerAnglesRaw.z"), Linear(keys));
        foreach (var axis in new[] { "x", "y" })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Back", typeof(Transform), "localEulerAnglesRaw." + axis), AnimationCurve.Constant(0f, Duration, 0f));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // A jolt as the ceiling gives, then a slow drift left onto the window as he sits back down
    static AnimationClip CameraClip(Camera camera)
    {
        var rest = camera.transform.localPosition;
        var rng = new Random(9);
        var keys = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
        for (int f = 0; f <= Mathf.RoundToInt(Duration * Fps); f++)
        {
            float t = f / Fps;
            float shake = t < crashTime ? 0f : 0.03f * Mathf.Exp(-(t - crashTime) * 6f);
            var offset = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, 0f) * 2f * shake;
            var p = rest + offset + CameraPan * Smooth(PanFrom, PanTo, t);
            for (int a = 0; a < 3; a++) keys[a].Add(new Keyframe(t, p[a]));
        }
        var clip = SavedClip("Plane_Camera");
        string[] axes = { "x", "y", "z" };
        for (int a = 0; a < 3; a++)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Main Camera", typeof(Transform), "m_LocalPosition." + axes[a]), new AnimationCurve(keys[a].ToArray()));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimationCurve Linear(List<Keyframe> keys)
    {
        var curve = new AnimationCurve(keys.ToArray());
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    // ---------------------------------------------------------------- meshes, textures, materials

    // A square in the XY plane facing -Z, UVs 0..1
    static Mesh FlatQuad(float size)
    {
        float h = size / 2f;
        var mesh = new Mesh();
        mesh.SetVertices(new List<Vector3> { new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(h, h, 0f), new Vector3(-h, h, 0f) });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // A ragged black hole with grey broken edges and cracks running out from it, alpha-blended
    static Material HoleMaterial()
    {
        const int size = 512;
        var rng = new Random(3);
        var px = new Color32[size * size];
        const int spikes = 28;
        var rim = new float[spikes];
        for (int i = 0; i < spikes; i++) rim[i] = 0.5f + 0.32f * (float)rng.NextDouble();
        float Rim(float angle)
        {
            float u = (angle / (2f * Mathf.PI) + 1f) % 1f * spikes;
            int i = (int)u;
            return Mathf.Lerp(rim[i % spikes], rim[(i + 1) % spikes], u - i);
        }
        var cracks = new List<(Vector2 a, Vector2 b)>();
        for (int c = 0; c < 9; c++)
        {
            float angle = (float)(rng.NextDouble() * 2 * Math.PI);
            var p = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Rim(angle) * 0.42f;
            for (int s = 0; s < 6; s++)
            {
                angle += (float)(rng.NextDouble() - 0.5) * 1.1f;
                var q = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (0.03f + 0.03f * (float)rng.NextDouble());
                cracks.Add((p, q));
                p = q;
            }
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var uv = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                float r = uv.magnitude, edge = Rim(Mathf.Atan2(uv.y, uv.x)) * 0.42f;
                Color c = Color.clear;
                if (r < edge) c = new Color(0.03f, 0.03f, 0.035f, 1f);
                else if (r < edge + 0.025f) c = new Color(0.45f, 0.45f, 0.47f, 0.9f);
                foreach (var (a, b) in cracks)
                {
                    var ab = b - a;
                    float k = Mathf.Clamp01(Vector2.Dot(uv - a, ab) / ab.sqrMagnitude);
                    if (Vector2.Distance(uv, a + ab * k) < 0.004f) { c = new Color(0.12f, 0.12f, 0.13f, 1f); break; }
                }
                px[y * size + x] = c;
            }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(px);
        texture.Apply();
        string path = $"{TextureFolder}/Plane_CeilingHole.png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return SavedMaterial("Ceiling Hole", m =>
        {
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.1f);
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
    }

    static readonly Dictionary<string, Mesh> boxes = new Dictionary<string, Mesh>();

    static GameObject Box(Transform parent, string name, Vector3 size, Vector3 position, Material material, float radius = 0f)
    {
        string key = $"Plane_Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_r{radius:0.###}";
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

    static Material SavedMaterial(string name, Action<Material> setup)
    {
        if (materials.TryGetValue(name, out var material)) return material;
        string path = $"{MaterialFolder}/Plane_{name.Replace(' ', '_')}.mat";
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
    // The instance is unscaled at the origin facing +Z: `move` is turned and scaled into that frame.
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
            for (int b = 0; b < bones.Length; b++)
                if (bones[b] != null) bones[b].localRotation = Quaternion.Euler(pose.e[b]);
            var move = toActor * pose.move;
            var scale = a.scaleAt(t);
            bones[0].localPosition = hipsRest + pose.hips + new Vector3(move.x / scale.x, move.y / scale.y, move.z / scale.z);
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

        var clip = SavedClip("Plane_" + a.name);
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
        light.shadowStrength = 0.6f;
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
