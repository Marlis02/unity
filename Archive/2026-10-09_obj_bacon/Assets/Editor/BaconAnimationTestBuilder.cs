using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

// Animation test for the main character (ActorPrefab: for now the bare base, bacon_base): one take through the
// moves the shorts will need, a second or two each, on the lineup's plain studio. He looks around, walks off to his
// right, turns, runs across, walks back to the middle, jumps, cheers with both arms up, waves, squats, shrugs, bows,
// kicks and points. Poses are set per bone and keyed straight onto the bones (a generic clip, see BakeClip).
// Tools/Characters/Build Bacon Animation Test -> Assets/Scenes/Test_BaconAnimations.unity + Timeline.
public static class BaconAnimationTestBuilder
{
    const float Fps = 30f;
    const string ScenePath = "Assets/Scenes/Test_BaconAnimations.unity";
    const string TimelinePath = "Assets/Timelines/Test_BaconAnimations.playable";
    const string ClipPath = "Assets/Animations/BaconTest_Bacon.anim";
    const string ActorPrefab = ObjBaconBuilder.BasePrefabPath;

    // Beats, seconds
    const float LookFrom = 0.5f, JumpUp = 8.9f, JumpDown = 9.45f, JumpHeight = 0.5f;
    const float CheerFrom = 10f, WaveFrom = 11.8f, SquatFrom = 13.8f, ShrugFrom = 15.4f, BowFrom = 16.8f;
    const float KickFrom = 18.4f, PointFrom = 20f, PointTo = 21.4f, Duration = 22.5f;
    // Travel along the world X (he faces the camera, so his right is the screen's left): from, to, x from, x to, running
    static readonly (float from, float to, float x0, float x1, bool run)[] Legs =
    {
        (2.7f, 4.3f, 0f, -1.3f, false),
        (4.75f, 6.05f, -1.3f, 1.3f, true),
        (6.4f, 8f, 1.3f, 0f, false),
    };
    // Turns of the whole body (yaw of the hips; 0 = facing the camera, +90 = his right): from, to, yaw from, yaw to
    static readonly (float from, float to, float yaw0, float yaw1)[] Turns =
    {
        (2.4f, 2.7f, 0f, 90f), (4.3f, 4.75f, 90f, -90f), (6.05f, 6.4f, -90f, 90f), (8f, 8.3f, 90f, 0f),
    };

    static readonly string[] Bones = ObjBaconBuilder.BoneNames;
    static readonly string[] Sides = { "Left", "Right" };
    static float Out(string side) => side == "Left" ? -1f : 1f; // sign of Z that swings that side's arm out

    static float armRestOut, thigh, shin;
    static readonly Quaternion Facing = Quaternion.Euler(0f, 180f, 0f); // towards the camera, which looks along +Z

    // ---------------------------------------------------------------- the act

    // Local bone rotations in degrees added to the rest pose (body facing +Z): arm out = Z with Out(side), arm forward =
    // -X, knee back = +X, spine/head forward = +X, turn right = +Y. `hips` moves the hips in his frame, `move` the whole
    // of him in the world.
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

    static Pose Act(float t)
    {
        var p = Idle(t);
        p.move.x = X(t);
        p.Rot("Hips", 0f, Yaw(t), 0f);
        p.Add(LookAround(t));
        foreach (var leg in Legs) p.Add(Gait(t, leg.from, leg.to, Mathf.Abs(leg.x1 - leg.x0) / (leg.to - leg.from), leg.run));
        p.Add(Jump(t, JumpUp, JumpDown, JumpHeight));
        p.Add(Cheer(t)).Add(WaveHand(t)).Add(Squat(t)).Add(Shrug(t)).Add(Bow(t)).Add(Kick(t)).Add(Point(t));
        // As main_hero's: an arm lifted out past ShoulderLiftFrom brings its clavicle up with it, taking that much off the
        // arm itself so it still points the same way, and a raised arm comes up out of the shoulder
        foreach (var s in Sides)
        {
            int arm = Array.IndexOf(Bones, s + "UpperArm");
            float lift = Mathf.Clamp((Out(s) * p.e[arm].z - ShoulderLiftFrom) * ShoulderLiftRate, 0f, ShoulderLiftMax);
            p.Rot(s + "Shoulder", 0f, 0f, Out(s) * lift);
            p.e[arm].z -= Out(s) * lift;
        }
        return p;
    }

    const float ShoulderLiftFrom = 70f, ShoulderLiftRate = 0.4f, ShoulderLiftMax = 28f; // degrees

    static float X(float t)
    {
        float x = 0f;
        foreach (var leg in Legs)
            if (t > leg.from) x = Mathf.Lerp(leg.x0, leg.x1, (t - leg.from) / (leg.to - leg.from));
        return x;
    }

    static float Yaw(float t)
    {
        float yaw = 0f;
        foreach (var turn in Turns)
            if (t > turn.from) yaw = Mathf.Lerp(turn.yaw0, turn.yaw1, Smooth(turn.from, turn.to, t));
        return yaw;
    }

    // Standing easy, arms hanging along the body
    static Pose Idle(float t)
    {
        float breathe = Wave(t, 0.4f);
        var p = new Pose().Rot("Spine", 1.5f * breathe, 0f, 0f).Rot("Head", -1.5f * breathe, 0f, 0f);
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", 0f, 0f, Out(s) * (3f - armRestOut + 1f * breathe)).Rot(s + "LowerArm", -2f, 0f, 0f);
        return p;
    }

    // A look to his right, then his left, then back at us
    static Pose LookAround(float t)
    {
        float look = 45f * Smooth(LookFrom, LookFrom + 0.35f, t) - 90f * Smooth(LookFrom + 0.65f, LookFrom + 1.1f, t)
            + 45f * Smooth(LookFrom + 1.4f, LookFrom + 1.75f, t);
        return new Pose().Rot("Head", 0f, look, 0f).Rot("Spine", 0f, 0.3f * look, 0f);
    }

    // Walking or running at `speed` m/s: the thighs swing (forward = -X) at the pace that keeps the planted foot still,
    // the knee bends as the leg swings through, the opposite arm swings with it, the hips drop as the legs part
    static Pose Gait(float t, float from, float to, float speed, bool run)
    {
        var p = new Pose();
        float w = Env(t, from, to, 0.15f);
        if (w <= 0f) return p;
        // A walk keeps the arms almost straight and swinging along the body; a run bends them
        // (a run's knee stops at 65 degrees: further, the R15 shin and the tall foot block fold into a zigzag)
        float amp = run ? 38f : 24f, knee = run ? 65f : 35f, arm = run ? 45f : 18f, elbow = run ? 75f : 3f;
        float cycle = 4f * (thigh + shin) * Mathf.Sin(amp * Mathf.Deg2Rad) * (run ? 1.35f : 1f); // ground per cycle; a run flies part of it
        float phase = (t - from) * speed / cycle;
        float drop = 0f;
        for (int k = 0; k < 2; k++)
        {
            string s = Sides[k];
            float c = (phase + 0.5f * k) * 2f * Mathf.PI;
            float swing = -amp * Mathf.Sin(c);
            float bend = knee * Mathf.Max(0f, Mathf.Cos(c));
            float ankle = Mathf.Clamp(-0.4f * (swing + bend), -AnkleMax, AnkleMax);
            p.Rot(s + "UpperLeg", swing * w, 0f, 0f).Rot(s + "LowerLeg", bend * w, 0f, 0f).Rot(s + "Foot", ankle * w, 0f, 0f);
            p.Rot(s + "UpperArm", -swing * arm / amp * w, 0f, 0f).Rot(s + "LowerArm", -elbow * w, 0f, 0f);
            drop = (thigh + shin) * (1f - Mathf.Cos(swing * Mathf.Deg2Rad));
        }
        p.hips.y -= (run ? 0.6f : 1f) * drop * w;
        return p.Rot("Spine", (run ? 12f : 3f) * w, 0f, 0f);
    }

    // Crouch, spring up with the arms thrown up in a V, land and soak it up
    static Pose Jump(float t, float up, float down, float height)
    {
        var p = new Pose();
        float start = up - 0.25f, settled = down + 0.25f;
        if (t < start || t > settled + 0.3f) return p;
        float air = Mathf.Clamp01((t - up) / (down - up));
        float arms = Smooth(up - 0.08f, up + 0.08f, t) * (1f - Smooth(down - 0.05f, settled, t));
        foreach (var s in Sides) p.Rot(s + "UpperArm", RaisedForward * arms, 0f, Out(s) * 135f * arms).Rot(s + "LowerArm", 0f, 0f, Out(s) * 15f * arms);
        if (t < up) return Crouch(p, 32f * Smooth(start, up - 0.05f, t) * (1f - Smooth(up - 0.05f, up, t)));
        if (t < down)
        {
            p.hips.y += 4f * height * air * (1f - air);
            foreach (var s in Sides) p.Rot(s + "UpperLeg", -30f * Mathf.Sin(air * Mathf.PI), 0f, 0f).Rot(s + "LowerLeg", 50f * Mathf.Sin(air * Mathf.PI), 0f, 0f);
            return p;
        }
        return Crouch(p, 26f * Mathf.Sin(Mathf.Clamp01((t - down) / (settled - down)) * Mathf.PI));
    }

    // The R15 foot is a tall block (the bottom 15 cm of the leg) under a high toe, so the ankle can't bend far: past
    // AnkleMax the block stands out of the shin. Deeper crouches lift the heels instead, the foot pivoting on its ball.
    const float AnkleMax = 25f, AnkleToBall = 0.15f;

    // Thighs forward by a degrees, knees bent by 2a, feet flat up to AnkleMax and then up on the balls; the hips drop
    // and shift back to keep the feet planted
    static Pose Crouch(Pose p, float a)
    {
        float flat = Mathf.Min(a, AnkleMax), tiptoe = a - flat;
        foreach (var s in Sides) p.Rot(s + "UpperLeg", -a, 0f, 0f).Rot(s + "LowerLeg", 2f * a, 0f, 0f).Rot(s + "Foot", -flat, 0f, 0f);
        float r = a * Mathf.Deg2Rad;
        p.hips += new Vector3(0f, -(thigh + shin) * (1f - Mathf.Cos(r)) + AnkleToBall * Mathf.Sin(tiptoe * Mathf.Deg2Rad), -(thigh - shin) * Mathf.Sin(r));
        return p.Rot("Spine", 0.4f * a, 0f, 0f);
    }

    // A raised arm's lean forward. The X turn comes after the arm is raised out to the side (Unity's Euler order is Z, X,
    // Y), so for an arm pointing up a forward lean is +X; -X, forward for a hanging arm, took raised arms back behind
    // the head and showed the flat front of the arm's top.
    const float RaisedForward = 18f;

    // Both arms up in a V (about 135 degrees, a little forward), pumping, a little bounce
    static Pose Cheer(float t)
    {
        float w = Env(t, CheerFrom, WaveFrom, 0.25f), pump = Wave(t, 2.5f);
        var p = new Pose().Rot("Head", -10f * w, 0f, 0f);
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", RaisedForward * w, 0f, Out(s) * (118f + 8f * pump) * w).Rot(s + "LowerArm", 0f, 0f, Out(s) * (20f + 15f * pump) * w);
        p.hips.y += 0.03f * Mathf.Abs(pump) * w;
        return p;
    }

    // The right arm up to the side, the forearm swinging
    static Pose WaveHand(float t)
    {
        float w = Env(t, WaveFrom, SquatFrom, 0.25f);
        return new Pose().Rot("RightUpperArm", RaisedForward * w, 0f, Out("Right") * 135f * w)
            .Rot("RightLowerArm", 0f, 0f, Out("Right") * (15f + 28f * Wave(t, 2.4f)) * w)
            .Rot("RightHand", 0f, PalmForward("Right") * w, 0f).Rot("Head", 0f, 0f, 6f * w);
    }

    // Down into a deep squat, arms forward for balance, and up
    static Pose Squat(float t)
    {
        float w = Env(t, SquatFrom, ShrugFrom, 0.4f);
        var p = Crouch(new Pose(), 60f * w);
        foreach (var s in Sides) p.Rot(s + "UpperArm", -65f * w, 0f, 0f);
        return p;
    }

    // At rest the palms face the thighs. Turning the hand this far about the forearm turns the palm to face where the
    // forearm's front is: the camera for an arm raised to the side (a wave), up for a forearm held out (a shrug).
    static float PalmForward(string side) => Out(side) * 90f;

    // Shoulders up, forearms out, palms up, head tilted
    static Pose Shrug(float t)
    {
        float w = Env(t, ShrugFrom, BowFrom, 0.25f);
        var p = new Pose().Rot("Head", 0f, 0f, 10f * w);
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", -10f * w, 0f, Out(s) * 28f * w).Rot(s + "LowerArm", -70f * w, 0f, 0f).Rot(s + "Hand", 0f, PalmForward(s) * w, 0f);
        return p;
    }

    // A bow from the hips and waist, legs kept upright, arms left hanging
    static Pose Bow(float t)
    {
        float w = Env(t, BowFrom, KickFrom, 0.35f);
        var p = new Pose().Rot("Hips", 20f * w, 0f, 0f).Rot("Spine", 25f * w, 0f, 0f).Rot("Head", 10f * w, 0f, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperLeg", -20f * w, 0f, 0f).Rot(s + "UpperArm", -45f * w, 0f, 0f);
        return p;
    }

    // Right knee up, the leg snaps out straight and comes down; arms out for balance, leaning back
    static Pose Kick(float t)
    {
        float lift = Smooth(KickFrom + 0.1f, KickFrom + 0.45f, t) * (1f - Smooth(KickFrom + 1.05f, KickFrom + 1.45f, t));
        float snap = Smooth(KickFrom + 0.5f, KickFrom + 0.65f, t) * (1f - Smooth(KickFrom + 0.9f, KickFrom + 1.2f, t));
        var p = new Pose().Rot("RightUpperLeg", -(55f + 30f * snap) * lift, 0f, 0f).Rot("RightLowerLeg", 85f * (1f - snap) * lift, 0f, 0f)
            .Rot("RightFoot", -20f * lift, 0f, 0f).Rot("Spine", -12f * lift, 0f, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperArm", 0f, 0f, Out(s) * 35f * lift);
        return p;
    }

    // The right arm straight out at the camera
    static Pose Point(float t)
    {
        float w = Env(t, PointFrom, PointTo, 0.25f);
        return new Pose().Rot("RightUpperArm", -85f * w, 0f, Out("Right") * 8f * w).Rot("RightLowerArm", 8f * w, 0f, 0f)
            .Rot("Spine", 0f, -10f * w, 0f).Rot("Head", 4f * w, 0f, 0f);
    }

    static float Wave(float t, float hz, float phase = 0f) => Mathf.Sin((t * hz + phase) * 2f * Mathf.PI);
    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    static float Env(float t, float start, float end, float fade) => Smooth(start, start + fade, t) * (1f - Smooth(end - fade, end, t));

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Characters/Build Bacon Animation Test")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[BaconTest] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RigUtility.EnsureFolder("Assets/Animations");
        RigUtility.EnsureFolder("Assets/Timelines");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefab);
        if (prefab == null) { Debug.LogError($"[BaconTest] {ActorPrefab} missing: build it first (Tools/Characters)"); return; }
        armRestOut = 3f; // the R15 arms hang as modelled: Idle leaves them where they are
        thigh = Vector3.Distance(Find(prefab, "LeftUpperLeg").position, Find(prefab, "LeftLowerLeg").position);
        shin = Vector3.Distance(Find(prefab, "LeftLowerLeg").position, Find(prefab, "LeftFoot").position);

        AnimationClip clip;
        var stage = EditorSceneManager.NewPreviewScene();
        try { clip = BakeClip(prefab, stage); }
        finally { EditorSceneManager.ClosePreviewScene(stage); }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CharacterLineupBuilder.BuildCamera(4.4f, 2.6f);
        CharacterLineupBuilder.BuildStudio();
        var bacon = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        bacon.name = "Bacon";
        bacon.transform.SetPositionAndRotation(Vector3.zero, Facing);

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }
        foreach (var track in new List<TrackAsset>(timeline.GetOutputTracks())) timeline.DeleteTrack(track);
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.editorSettings.frameRate = Fps;
        timeline.fixedDuration = Duration;

        var director = new GameObject("Timeline_BaconTest").AddComponent<PlayableDirector>();
        var baconTrack = timeline.CreateTrack<AnimationTrack>(null, "Bacon");
        baconTrack.trackOffset = TrackOffset.ApplySceneOffsets; // the clip keys the hips, not the root
        var baconClip = baconTrack.CreateClip(clip);
        baconClip.start = 0;
        baconClip.duration = Duration;
        director.SetGenericBinding(baconTrack, bacon.GetComponent<Animator>());
        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[BaconTest] Built {ScenePath}, {Duration} s");
    }

    // The act posed on a copy of the prefab frame by frame and keyed straight onto the bones (a generic clip: each
    // bone's local rotation, the hips' local position). Not as humanoid muscles: Unity's muscle space spreads each limb's
    // twist over its bones, which turned the R15 bacon's blocky thighs and upper arms 15-35 degrees about themselves.
    // The root is never keyed (the hips are), so the actor stays where the scene puts it.
    static AnimationClip BakeClip(GameObject prefab, Scene stage)
    {
        var go = Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(go, stage);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var bones = Array.ConvertAll(Bones, b => Find(go, b));
        var paths = Array.ConvertAll(bones, b => AnimationUtility.CalculateTransformPath(b, go.transform));
        var hipsRest = bones[0].localPosition;
        var toActor = Quaternion.Inverse(Facing);

        int frames = Mathf.RoundToInt(Duration * Fps);
        var rotations = new Quaternion[bones.Length, frames + 1];
        var hips = new Vector3[frames + 1];
        for (int f = 0; f <= frames; f++)
        {
            var pose = Act(f / Fps);
            for (int b = 0; b < bones.Length; b++)
            {
                var q = Quaternion.Euler(pose.e[b]);
                // Keep each bone's quaternion on the same side as last frame's, so the curves don't flip
                if (f > 0 && Quaternion.Dot(q, rotations[b, f - 1]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                rotations[b, f] = q;
            }
            hips[f] = hipsRest + pose.hips + toActor * pose.move;
        }
        Object.DestroyImmediate(go);

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
        void Key(string path, string property, Func<int, float> value)
        {
            var keys = new Keyframe[frames + 1];
            for (int f = 0; f <= frames; f++)
            {
                int a = Mathf.Max(f - 1, 0), b = Mathf.Min(f + 1, frames);
                float slope = (value(b) - value(a)) * Fps / (b - a);
                keys[f] = new Keyframe(f / Fps, value(f), slope, slope);
            }
            bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), property));
            curves.Add(new AnimationCurve(keys));
        }
        for (int b = 0; b < bones.Length; b++)
        {
            int bone = b;
            Key(paths[b], "m_LocalRotation.x", f => rotations[bone, f].x);
            Key(paths[b], "m_LocalRotation.y", f => rotations[bone, f].y);
            Key(paths[b], "m_LocalRotation.z", f => rotations[bone, f].z);
            Key(paths[b], "m_LocalRotation.w", f => rotations[bone, f].w);
        }
        Key(paths[0], "m_LocalPosition.x", f => hips[f].x);
        Key(paths[0], "m_LocalPosition.y", f => hips[f].y);
        Key(paths[0], "m_LocalPosition.z", f => hips[f].z);
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static Transform Find(GameObject root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        throw new ArgumentException($"No {name} under {root.name}");
    }
}
