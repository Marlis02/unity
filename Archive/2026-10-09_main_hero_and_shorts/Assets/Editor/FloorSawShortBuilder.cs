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
using Object = UnityEngine.Object;
using Random = System.Random;

// The Roblox half of "Bacon Hair Falls Under the Floor": in a room under renovation the actor (ActorPrefab, main_hero)
// saws a circle in the floor round himself, drops the saw and waits to fall through. Nothing. He jumps, twice: nothing. He gives up,
// steps out of the circle and crashes through the floor where nobody sawed; a beat later the sawn circle, saw and all,
// lags out and drops too. Motion is posed bone by bone and baked into a humanoid clip; the
// floor's part (the sawdust line, the circle, the patch that breaks, the cracks) is one generic clip on FloorFX. Builds
// Shorts_FloorSaw with a Timeline to preview in the editor or in Play Mode; Build(record: true) adds a Recorder track
// writing 1080x1920 at 30 fps in Play Mode (PNG frames -> MP4 via ShortsFrameEncoder). Tweak the beats and re-run
// Tools/Shorts/Build Floor Saw Short.
public static class FloorSawShortBuilder
{
    const float Fps = 30f;
    // Beats, seconds
    const float BendDown = 0.7f, SawStart = 1.25f, SawEnd = 4.45f, StandUp = 5.0f, Release = 5.15f;
    const float LookDown = 5.45f, Jump1 = 6.55f, LookAgain = 7.75f, Jump2 = 8.45f, GiveUp = 9.65f;
    const float WalkStart = 10.55f, Crash = 11.5f, LagStart = 12.9f, LagDrop = 13.2f, Duration = 14.6f;

    const string ScenePath = "Assets/Scenes/Shorts_FloorSaw.unity";
    const string TimelinePath = "Assets/Timelines/Shorts_FloorSaw.playable";
    const string Folder = "Assets/Shorts/FloorSaw";
    const string MeshFolder = Folder + "/Meshes";
    const string MaterialFolder = Folder + "/Materials";
    const string TextureFolder = Folder + "/Textures";
    const string ClipFolder = "Assets/Animations";
    const string VolumePath = "Assets/Shorts/Shorts_Volume.asset";
    const string ActorPrefab = HeroPathCharacterBuilder.PrefabFolder + "/main_hero.prefab";
    const int VideoWidth = 1080, VideoHeight = 1920;
    const string RecordingFolder = "Recordings/Shorts_FloorSaw_<Take>" + ShortsFrameEncoder.FramesSuffix; // relative to the project folder

    // The room (metres): the corner is at (CornerX, BackZ); the side wall runs towards the camera along +Z, the back wall
    // along -X. The camera looks into the corner from in front, a little to the right of it.
    const float CornerX = 1.4f, BackZ = -1.6f, FloorMinX = -2.8f, FloorMaxZ = 3.4f, WallHeight = 2.7f;
    const float SheetSize = 1.22f;         // an OSB sheet; the floor's texture covers one
    const float FloorThickness = 0.025f;
    static readonly Vector3 CircleCenter = new Vector3(0.5f, 0f, -0.45f);
    static readonly Vector3 CrashCenter = new Vector3(0.62f, 0f, 0.58f);
    const float CrashRadius = 0.3f;        // the broken hole's mean radius; it is ragged
    static readonly Vector3 CameraPosition = new Vector3(-0.55f, 2.15f, 3.3f);
    static readonly Vector3 CameraTarget = new Vector3(0.45f, 0.6f, -0.55f);
    const float CameraFov = 56f;
    const float Gravity = 9.8f;

    static readonly string[] Bones =
    {
        "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
        "LeftShoulder", "RightShoulder", // clavicles, where the actor has them
    };
    static readonly string[] Sides = { "Left", "Right" };
    static float Out(string side) => side == "Left" ? -1f : 1f; // sign of Z that swings that side's arm out

    static float thigh, shin;  // leg segment lengths in metres, read from the prefab
    static float armRestOut;   // degrees the arms hang out from straight down in the prefab's rest pose
    static bool clavicles;     // the actor has shoulder bones to lift raised arms with
    static string facePath;    // the RobloxFace, from the prefab's root
    static Vector3 walkLocal;  // circle to crash hole in the actor's frame
    static float walkTurn;     // degrees he turns to walk there

    // ---------------------------------------------------------------- the saw

    // Hand saw in its own frame: the grip at the origin (in the fist), the blade hanging straight down from it (-Y) in
    // the YZ plane, teeth on the +Z edge
    const float SawLength = 0.52f, HandleHalf = 0.06f, HandleThickness = 0.035f;
    static readonly Vector3 SawTip = new Vector3(0f, -SawLength, 0f);
    // How the saw sits in his fist: turned about the blade so the teeth face away from the back of the hand, which puts
    // them down into the floor while he saws (the tip stays where it was)
    static readonly Quaternion InHand = Quaternion.Euler(0f, 180f, 0f);

    // Sawing round: he saws along the circle SawCircle round where he stands (metres), in front of him on his right
    // (SawLead degrees right of where he faces), the blade sloping SawAngle below level with its tip SawDepth under the
    // floor; turning round carries the cut round the circle
    const float SawCircle = 0.55f, SawLead = 30f, SawAngle = 60f, SawDepth = 0.03f;
    const float Turn = -360f; // he turns once round to his left

    static Vector3 gripOffset;                     // the saw's grip in the right hand's frame
    static Vector3 upperArmRest, lowerArmRest;     // right arm segments at rest (shoulder to elbow, elbow to wrist)
    static float worstReach;                       // how far the arm had to stretch for the saw, 1 = straight

    // ---------------------------------------------------------------- poses

    // Local bone rotations in degrees, added to the rest pose (every bone is unrotated there: the body faces +Z, arms
    // hanging in an A-pose). Arm out to the side = Z (+ right, - left), arm/elbow forward = -X, knee back = +X,
    // spine/head forward = +X, turn to the body's right = +Y. The hips also move (metres, in the actor's frame).
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

    static Pose Act(float t)
    {
        var p = Idle(t)
            .Add(Sawing(t), Env(t, BendDown, StandUp, 0.5f))
            .Add(LetGo(), Env(t, Release - 0.1f, Release + 0.45f, 0.12f))
            .Add(LookAtFloor(), Env(t, LookDown, Jump1 + 0.05f, 0.25f))
            .Add(Jump(t, Jump1, 0.42f))
            .Add(LookAtFloor(), Env(t, LookAgain, Jump2 + 0.05f, 0.2f) * 0.8f)
            .Add(Jump(t, Jump2, 0.55f))
            .Add(Shrug(t), Env(t, GiveUp, WalkStart + 0.15f, 0.25f))
            .Add(Walk(t))
            .Add(Fall(t));
        // A whole turn while sawing round; 360 degrees is no turn, so it simply stops counting afterwards
        if (t < SawEnd) p.Rot("Hips", 0f, Turn * SawProgress(t), 0f);
        // An arm lifted out past ShoulderLiftFrom brings its clavicle up with it, taking that much off the arm itself so
        // it still points the same way: raised arms come up out of the shoulder
        if (clavicles)
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

    static float SawProgress(float t) => Smooth(SawStart, SawEnd, t);

    // Standing easy, arms hanging along the body (the right one further out: it holds the saw until he lets it go)
    static Pose Idle(float t)
    {
        float breathe = Wave(t, 0.4f);
        float saw = 1f - Smooth(Release, Release + 0.4f, t);
        var p = new Pose().Rot("Spine", 1.5f * breathe, 0f, 0f).Rot("Head", -1.5f * breathe, 0f, 0f);
        foreach (var s in Sides)
        {
            float outFromDown = s == "Right" ? Mathf.Lerp(3f, 10f, saw) : 3f; // degrees, whatever the rest pose
            p.Rot(s + "UpperArm", 0f, 0f, Out(s) * (outFromDown - armRestOut + 1f * breathe)).Rot(s + "LowerArm", -8f, 0f, Out(s) * 3f);
        }
        return p;
    }

    // Bent double over the floor, the saw arm hanging straight down to it and stroking (its shoulder dropped forward to
    // reach), the other hand on the knee, shuffling round on the spot (the turn itself is in Act)
    static Pose Sawing(float t)
    {
        var p = Crouch(new Pose(), 45f)
            .Rot("Hips", 42f, 0f, 0f)
            .Rot("Spine", 26f, 0f, 0f).Rot("Head", -45f, 0f, 0f)
            .Rot("RightShoulder", 0f, -14f, 0f)
            .Rot("RightUpperArm", -60f, 0f, 0f).Rot("RightLowerArm", -20f, 0f, 0f) // SawArm takes the right arm over
            .Rot("LeftUpperArm", -50f, 0f, 8f).Rot("LeftLowerArm", -45f, 0f, 0f);
        foreach (var s in Sides) p.Rot(s + "UpperLeg", -42f, 0f, 0f); // the legs stay put while the hips tip forward
        float shuffle = Env(t, SawStart, SawEnd, 0.2f);
        for (int k = 0; k < 2; k++)
        {
            float lift = Mathf.Max(0f, Wave(t, 1.3f, k * 0.5f)) * shuffle;
            string s = Sides[k];
            p.Rot(s + "UpperLeg", -16f * lift, 0f, 0f).Rot(s + "LowerLeg", 32f * lift, 0f, 0f).Rot(s + "Foot", -16f * lift, 0f, 0f);
        }
        return p;
    }

    // While sawing, the right arm holds the saw where the cut needs it rather than being posed: the blade lies along
    // the circle, tip first the way the cut goes as he turns, sloping down, and slides to and fro along the line with its
    // tip on it. Lining the blade up with the circle turns the saw out of his own forward by SawLead - 90 degrees; all of
    // that goes into the forearm's twist (which a humanoid arm allows), so the wrist itself only bends. Shoulder, elbow
    // and wrist reach for it (two-bone IK, elbow back and out), blended in and out with the sawing pose.
    static void SawArm(Func<string, Transform> bone, float t)
    {
        float w = Env(t, BendDown, StandUp, 0.5f);
        if (w <= 0f) return;
        float turned = Turn * SawProgress(t);
        Vector3 facing = Direction(turned), right = Direction(turned + 90f);
        var alongCircle = Quaternion.Euler(0f, SawLead + 90f * Mathf.Sign(Turn), 0f); // from his forward to the way the cut goes
        float slope = SawAngle * Mathf.Deg2Rad;
        var ahead = (facing * Mathf.Cos(slope) + Vector3.down * Mathf.Sin(slope)).normalized; // the blade if it pointed straight ahead
        var blade = alongCircle * ahead;
        var tip = Direction(turned + SawLead) * SawCircle + Vector3.down * SawDepth + blade * 0.05f * Wave(t, 2.4f);
        // The hand, which the saw hangs straight down from (InHand only turns it about the blade)
        var handRotation = alongCircle * Quaternion.LookRotation(Vector3.Cross(right, -ahead), -ahead);
        var wrist = tip - blade * SawLength - handRotation * gripOffset;

        Transform upper = bone("RightUpperArm"), lower = bone("RightLowerArm"), hand = bone("RightHand");
        Quaternion poseUpper = upper.localRotation, poseLower = lower.localRotation, poseHand = hand.localRotation;
        float l1 = upperArmRest.magnitude, l2 = lowerArmRest.magnitude;
        var shoulder = upper.position;
        var toWrist = wrist - shoulder;
        if (w > 0.99f) worstReach = Mathf.Max(worstReach, toWrist.magnitude / (l1 + l2));
        float reach = Mathf.Min(toWrist.magnitude, (l1 + l2) * 0.999f);
        var aim = toWrist.normalized;
        float bend = Mathf.Acos(Mathf.Clamp((l1 * l1 + reach * reach - l2 * l2) / (2f * l1 * reach), -1f, 1f));
        var pole = Vector3.ProjectOnPlane(-facing + right * 0.4f + Vector3.up * 0.2f, aim).normalized;
        var elbow = shoulder + (aim * Mathf.Cos(bend) + pole * Mathf.Sin(bend)) * l1;

        var upperWorld = Aim(upperArmRest, elbow - shoulder, facing);
        var lowerWorld = Aim(lowerArmRest, wrist - elbow, alongCircle * facing);
        upper.localRotation = Quaternion.Slerp(poseUpper, Quaternion.Inverse(upper.parent.rotation) * upperWorld, w);
        lower.localRotation = Quaternion.Slerp(poseLower, Quaternion.Inverse(upperWorld) * lowerWorld, w);
        hand.localRotation = Quaternion.Slerp(poseHand, Quaternion.Inverse(lowerWorld) * handRotation, w);
    }

    // Turns a bone from its rest direction (bones are unrotated at rest, front +Z) to point along `to`, its front
    // towards `front`
    static Quaternion Aim(Vector3 rest, Vector3 to, Vector3 front) =>
        Quaternion.LookRotation(to, front) * Quaternion.Inverse(Quaternion.LookRotation(rest, Vector3.forward));

    // Flicks the saw hand open and away from the leg
    static Pose LetGo() => new Pose().Rot("RightUpperArm", -12f, 0f, 9f).Rot("RightLowerArm", -18f, 0f, 0f);

    static Pose LookAtFloor() => new Pose().Rot("Head", 30f, 0f, 0f).Rot("Spine", 8f, 0f, 0f)
        .Rot("LeftLowerArm", -12f, 0f, 0f).Rot("RightLowerArm", -12f, 0f, 0f);

    // Palms up, what gives; head tilted and shaking
    static Pose Shrug(float t)
    {
        var p = new Pose().Rot("Spine", -4f, 0f, 0f).Rot("Head", 4f, 14f * Wave(t, 1.6f) * Smooth(GiveUp + 0.3f, GiveUp + 0.5f, t), 9f);
        foreach (var s in Sides) p.Rot(s + "UpperArm", -12f, 0f, Out(s) * 24f).Rot(s + "LowerArm", -62f, 0f, 0f);
        return p;
    }

    // Turns towards the crash spot and walks there, arriving as the floor gives
    static Pose Walk(float t)
    {
        var p = new Pose();
        if (t < WalkStart - 0.15f) return p;
        p.Rot("Hips", 0f, walkTurn * Smooth(WalkStart - 0.15f, WalkStart + 0.15f, t), 0f);
        float along = Mathf.Clamp01((t - WalkStart) / (Crash - WalkStart));
        along = along * along * (1.6f - 0.6f * along); // eases in, arrives walking
        p.hips += walkLocal * along;
        float gait = Env(t, WalkStart, Crash + 0.1f, 0.12f);
        float phase = (t - WalkStart) * 1.6f; // strides per second
        float swing = Mathf.Sin(phase * 2f * Mathf.PI);
        for (int k = 0; k < 2; k++)
        {
            string s = Sides[k];
            float side = k == 0 ? 1f : -1f;
            float knee = Mathf.Max(0f, Mathf.Sin((phase + k * 0.5f) * 2f * Mathf.PI));
            p.Rot(s + "UpperLeg", -24f * swing * side * gait, 0f, 0f).Rot(s + "LowerLeg", 34f * knee * gait, 0f, 0f);
            p.Rot(s + "UpperArm", 16f * swing * side * gait, 0f, 0f);
        }
        p.Rot("Head", 10f * gait, 0f, 0f);
        return p;
    }

    // Drops straight down the hole, arms flung up and flailing, knees up
    static Pose Fall(float t)
    {
        var p = new Pose();
        if (t < Crash) return p;
        float s = t - Crash;
        float limbs = Smooth(Crash, Crash + 0.15f, t);
        p.hips.y -= Mathf.Min(0.5f * Gravity * s * s, 14f);
        foreach (var side in Sides)
        {
            p.Rot(side + "UpperArm", -20f * limbs, 0f, Out(side) * 135f * limbs)
                .Rot(side + "LowerArm", 0f, 0f, Out(side) * (20f + 25f * Wave(t, 4f)) * limbs)
                .Rot(side + "UpperLeg", -35f * limbs, 0f, 0f).Rot(side + "LowerLeg", 55f * limbs, 0f, 0f);
        }
        p.Rot("Head", -15f * limbs, 0f, 0f);
        return p;
    }

    // Crouch, jump throwing the arms up, land; arms down again after
    static Pose Jump(float t, float start, float height)
    {
        var p = new Pose();
        float takeoff = start + 0.25f, landing = takeoff + 0.5f, settled = landing + 0.3f;
        if (t < start || t > settled + 0.6f) return p;
        // Arms flung up into a V, a little in front so they clear the head, quickly through level on the way
        float armsUp = Smooth(takeoff - 0.06f, takeoff + 0.06f, t) * (1f - Smooth(settled, settled + 0.5f, t));
        float armsBack = Smooth(start, takeoff - 0.05f, t) * (1f - Smooth(takeoff - 0.06f, takeoff + 0.06f, t));
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", 45f * armsBack - 22f * armsUp, 0f, Out(s) * 135f * armsUp).Rot(s + "LowerArm", 0f, 0f, Out(s) * 15f * armsUp);
        p.Rot("Head", -8f * armsUp, 0f, 0f);

        if (t < takeoff) return Crouch(p, 32f * Smooth(start, takeoff, t));
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

    static readonly (float time, RobloxFace.Emotion emotion)[] Faces =
    {
        (0f, RobloxFace.Emotion.Smile), (BendDown, RobloxFace.Emotion.Neutral), (StandUp - 0.3f, RobloxFace.Emotion.Grin),
        (LookDown + 0.15f, RobloxFace.Emotion.Surprised), (Jump1, RobloxFace.Emotion.Grin), (LookAgain, RobloxFace.Emotion.Neutral),
        (Jump2, RobloxFace.Emotion.Angry), (GiveUp, RobloxFace.Emotion.Bruh), (WalkStart, RobloxFace.Emotion.Sad),
        (Crash, RobloxFace.Emotion.Shocked),
    };

    static float Wave(float t, float hz, float phase = 0f) => Mathf.Sin((t * hz + phase) * 2f * Mathf.PI);
    static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    static float Env(float t, float start, float end, float fade) => Smooth(start, start + fade, t) * (1f - Smooth(end - fade, end, t));

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Shorts/Build Floor Saw Short")]
    public static void Build() => Build(record: false);

    public static void Build(bool record)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[FloorSaw] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var folder in new[] { MeshFolder, MaterialFolder, TextureFolder, ClipFolder, "Assets/Timelines" }) RigUtility.EnsureFolder(folder);
        materials.Clear();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefab);
        if (prefab == null) { Debug.LogError("[FloorSaw] " + ActorPrefab + " missing: run Tools/Characters/Build main_hero"); return; }
        thigh = Vector3.Distance(Find(prefab, "LeftUpperLeg").position, Find(prefab, "LeftLowerLeg").position);
        shin = Vector3.Distance(Find(prefab, "LeftLowerLeg").position, Find(prefab, "LeftFoot").position);
        var hang = Find(prefab, "RightHand").position - Find(prefab, "RightLowerArm").position; // the forearm: a clavicle rig's shoulder pivot sits off the arm's axis
        armRestOut = Mathf.Atan2(Mathf.Abs(hang.x), -hang.y) * Mathf.Rad2Deg;
        clavicles = FindOptional(prefab, "RightShoulder") != null;
        facePath = AnimationUtility.CalculateTransformPath(Find(prefab, "Face"), prefab.transform);

        // He faces the camera from the middle of the circle
        var toCamera = CameraPosition - CircleCenter;
        float yaw = Mathf.Atan2(toCamera.x, toCamera.z) * Mathf.Rad2Deg;
        var actorRotation = Quaternion.Euler(0f, yaw, 0f);
        walkLocal = Quaternion.Inverse(actorRotation) * (CrashCenter - CircleCenter);
        walkTurn = Mathf.Atan2(walkLocal.x, walkLocal.z) * Mathf.Rad2Deg;

        upperArmRest = Find(prefab, "RightLowerArm").position - Find(prefab, "RightUpperArm").position;
        lowerArmRest = Find(prefab, "RightHand").position - Find(prefab, "RightLowerArm").position;

        // Bake the act on a scratch instance, measuring where the saw goes on the way
        Vector3 grip;
        float circleRadius = SawCircle, sawFrom = yaw + SawLead, reach;
        Matrix4x4 sawAtRelease;
        AnimationClip clip;
        var stage = EditorSceneManager.NewPreviewScene();
        try
        {
            var scratch = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(scratch, stage);
            scratch.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var hand = Find(scratch, "RightHand");
            grip = hand.InverseTransformPoint(FistCenter(scratch)) + new Vector3(0f, -0.01f, 0.005f);
            gripOffset = grip;
            Object.DestroyImmediate(scratch);
            worstReach = 0f;

            clip = BakeClip(prefab, stage, (posed, t) =>
            {
                var h = Find(posed, "RightHand");
                return h.localToWorldMatrix * Matrix4x4.TRS(grip, InHand, Vector3.one);
            }, out var sawAt);
            // Check: the tip as baked should run round the circle
            var tip = sawAt((SawStart + SawEnd) / 2f).MultiplyPoint3x4(SawTip);
            reach = new Vector2(tip.x, tip.z).magnitude;
            sawAtRelease = Matrix4x4.TRS(CircleCenter, actorRotation, Vector3.one) * sawAt(Release);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(stage);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = BuildStage();
        BuildRoom();
        var fx = BuildFloor(circleRadius, sawFrom, sawAtRelease, actorRotation, out var fxClip);

        var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        actor.transform.SetPositionAndRotation(CircleCenter, actorRotation);
        var sawInHand = Saw(Find(actor, "RightHand"), "Saw");
        sawInHand.transform.localPosition = grip;
        sawInHand.transform.localRotation = InHand;

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

        var director = new GameObject("Timeline_FloorSaw").AddComponent<PlayableDirector>();

        var actorTrack = timeline.CreateTrack<AnimationTrack>(null, "Actor");
        // Play where he stands, in the circle facing the camera, however the Timeline is evaluated (scene offsets are
        // only picked up in some of the ways it can be)
        actorTrack.trackOffset = TrackOffset.ApplyTransformOffsets;
        actorTrack.position = CircleCenter;
        actorTrack.rotation = actorRotation;
        var actorClip = actorTrack.CreateClip(clip);
        actorClip.start = 0;
        actorClip.duration = Duration;
        // The baked clip has no IK goal curves; foot IK would pull the feet to the origin
        ((AnimationPlayableAsset)actorClip.asset).applyFootIK = false;
        director.SetGenericBinding(actorTrack, actor.GetComponent<Animator>());

        var sawTrack = timeline.CreateTrack<ActivationTrack>(null, "Saw in hand");
        var held = sawTrack.CreateDefaultClip();
        held.start = 0;
        held.duration = Release;
        sawTrack.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
        director.SetGenericBinding(sawTrack, sawInHand);

        var fxTrack = timeline.CreateTrack<AnimationTrack>(null, "Floor");
        var floorClip = fxTrack.CreateClip(fxClip);
        floorClip.start = 0;
        floorClip.duration = Duration;
        director.SetGenericBinding(fxTrack, fx.GetComponent<Animator>());

        var shakeTrack = timeline.CreateTrack<AnimationTrack>(null, "Camera shake");
        var shakeClip = shakeTrack.CreateClip(CameraShakeClip(camera));
        shakeClip.start = 0;
        shakeClip.duration = Duration;
        director.SetGenericBinding(shakeTrack, camera.transform.parent.GetComponent<Animator>());

        if (record) AddRecorderTrack(timeline, take);

        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[FloorSaw] Built {ScenePath} (saw tip {reach:F2} m out on a {circleRadius:F2} m circle, arm stretched to {worstReach:P0})" +
            (record ? ": press Play to record" : ""));
    }

    // ---------------------------------------------------------------- stage

    // A phone held at eye height looking down into the corner; daylight from a window behind it on the right
    static Camera BuildStage()
    {
        var rig = new GameObject("Camera Rig");
        rig.AddComponent<Animator>();
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.SetParent(rig.transform, false);
        camera.transform.position = CameraPosition;
        camera.transform.LookAt(CameraTarget);
        camera.fieldOfView = CameraFov;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.8f, 0.79f, 0.76f);
        camera.nearClipPlane = 0.05f;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.stopNaN = true; // one stray NaN pixel would bloom into a white blaze

        AddLight("Window Light", new Color(1f, 0.95f, 0.86f), 1.35f, new Vector3(42f, 150f, 0f), LightShadows.Soft);
        AddLight("Fill Light", new Color(0.82f, 0.88f, 1f), 0.3f, new Vector3(25f, 200f, 0f), LightShadows.None);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.78f, 0.78f, 0.8f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.58f, 0.52f);
        RenderSettings.ambientGroundColor = new Color(0.4f, 0.33f, 0.25f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
        return camera;
    }

    // A short jolt as he crashes through
    static AnimationClip CameraShakeClip(Camera camera)
    {
        var rest = camera.transform.localPosition;
        var rng = new Random(7);
        var keys = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
        for (int f = 0; f <= Mathf.RoundToInt(Duration * Fps); f++)
        {
            float t = f / Fps;
            float shake = t < Crash ? 0f : 0.022f * Mathf.Exp(-(t - Crash) * 7f);
            var offset = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, 0f) * 2f * shake;
            for (int a = 0; a < 3; a++) keys[a].Add(new Keyframe(t, rest[a] + offset[a]));
        }
        var clip = SavedClip("FloorSaw_CameraShake");
        string[] axes = { "x", "y", "z" };
        for (int a = 0; a < 3; a++)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Main Camera", typeof(Transform), "m_LocalPosition." + axes[a]),
                new AnimationCurve(keys[a].ToArray()));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // Two walls meeting in the corner with skirting and a chair rail, a stepladder folded against the back wall, a mitre
    // saw on the floor, and the bits a renovation leaves lying about
    static void BuildRoom()
    {
        var room = new GameObject("Room").transform;
        var wall = Lit("Wall", new Color(0.86f, 0.85f, 0.82f), 0.08f);
        var trim = Lit("Trim", new Color(0.95f, 0.95f, 0.94f), 0.35f);
        float backLength = CornerX - FloorMinX, sideLength = FloorMaxZ - BackZ;
        Box(room, "Back Wall", new Vector3(backLength + 0.2f, WallHeight, 0.1f), new Vector3((CornerX + FloorMinX) / 2f, WallHeight / 2f - 0.05f, BackZ - 0.05f), wall);
        Box(room, "Side Wall", new Vector3(0.1f, WallHeight, sideLength + 0.2f), new Vector3(CornerX + 0.05f, WallHeight / 2f - 0.05f, (BackZ + FloorMaxZ) / 2f), wall);
        foreach (var (height, y, depth) in new[] { (0.1f, 0.05f, 0.016f), (0.045f, 0.9f, 0.022f) })
        {
            Box(room, "Back Trim", new Vector3(backLength, height, depth), new Vector3((CornerX + FloorMinX) / 2f, y, BackZ + depth / 2f), trim, 0.006f);
            Box(room, "Side Trim", new Vector3(depth, height, sideLength), new Vector3(CornerX - depth / 2f, y, (BackZ + FloorMaxZ) / 2f), trim, 0.006f);
        }

        // Stepladder, folded and leaning on the back wall just right of the corner
        var ladder = new GameObject("Stepladder").transform;
        ladder.SetParent(room, false);
        ladder.SetPositionAndRotation(new Vector3(0.78f, 0f, BackZ + 0.36f), Quaternion.Euler(-8.5f, 0f, 0f));
        var blue = Lit("Ladder Blue", Hex("1F6FD1"), 0.45f);
        var steel = Lit("Ladder Steel", new Color(0.72f, 0.73f, 0.75f), 0.55f, 0.6f);
        var rubber = Lit("Rubber", new Color(0.12f, 0.12f, 0.13f), 0.2f);
        var label = Lit("Label Yellow", Hex("F2C230"), 0.3f);
        foreach (float x in new[] { -0.23f, 0.23f })
        {
            Box(ladder, "Rail", new Vector3(0.075f, 2.3f, 0.035f), new Vector3(x, 1.15f, 0f), blue, 0.01f);
            Box(ladder, "Back Rail", new Vector3(0.06f, 2.2f, 0.03f), new Vector3(x * 0.9f, 1.1f, -0.045f), blue, 0.01f);
            Box(ladder, "Foot", new Vector3(0.085f, 0.05f, 0.07f), new Vector3(x, 0.025f, -0.01f), rubber, 0.01f);
        }
        for (int i = 1; i <= 6; i++) Box(ladder, "Step", new Vector3(0.44f, 0.035f, 0.085f), new Vector3(0f, 0.32f * i, 0.025f), steel, 0.008f);
        Box(ladder, "Top", new Vector3(0.52f, 0.07f, 0.16f), new Vector3(0f, 2.31f, -0.03f), rubber, 0.02f);
        Box(ladder, "Label", new Vector3(0.012f, 0.16f, 0.025f), new Vector3(0.27f, 1.35f, 0.006f), label, 0.003f);
        Box(ladder, "Label", new Vector3(0.012f, 0.08f, 0.025f), new Vector3(0.27f, 0.75f, 0.006f), label, 0.003f);

        // Mitre saw, right of the ladder
        var mitre = new GameObject("Mitre Saw").transform;
        mitre.SetParent(room, false);
        mitre.SetPositionAndRotation(new Vector3(0.0f, 0f, BackZ + 0.45f), Quaternion.Euler(0f, -20f, 0f));
        var grey = Lit("Machine Grey", new Color(0.5f, 0.52f, 0.54f), 0.4f, 0.3f);
        var dark = Lit("Machine Dark", new Color(0.18f, 0.19f, 0.2f), 0.3f);
        var lime = Lit("Machine Lime", Hex("A8C82A"), 0.4f);
        var red = Lit("Machine Red", Hex("D62828"), 0.4f);
        Box(mitre, "Base", new Vector3(0.56f, 0.07f, 0.42f), new Vector3(0f, 0.035f, 0f), dark, 0.02f);
        Cylinder(mitre, "Turntable", 0.17f, 0.03f, new Vector3(0f, 0.085f, 0.03f), Quaternion.identity, grey);
        Box(mitre, "Fence", new Vector3(0.52f, 0.07f, 0.025f), new Vector3(0f, 0.135f, -0.09f), grey, 0.008f);
        Box(mitre, "Pivot", new Vector3(0.09f, 0.24f, 0.09f), new Vector3(0f, 0.2f, -0.17f), dark, 0.02f);
        Cylinder(mitre, "Blade Guard", 0.16f, 0.06f, new Vector3(0f, 0.3f, 0.0f), Quaternion.Euler(0f, 0f, 90f), grey);
        Cylinder(mitre, "Motor", 0.07f, 0.2f, new Vector3(0.12f, 0.33f, -0.04f), Quaternion.Euler(0f, 0f, 90f), lime);
        Box(mitre, "Handle", new Vector3(0.045f, 0.16f, 0.045f), new Vector3(0f, 0.47f, 0.08f), lime, 0.015f);
        Box(mitre, "Trigger", new Vector3(0.025f, 0.04f, 0.03f), new Vector3(0f, 0.41f, 0.11f), red, 0.006f);

        // Spare saw, tape measure and hammer in front on the left; a water bottle and a roll of duct tape on the right
        var spare = Saw(room, "Spare Saw");
        spare.transform.SetPositionAndRotation(new Vector3(1.12f, HandleThickness / 2f, 1.35f), Quaternion.LookRotation(Vector3.up, new Vector3(-0.3f, 0f, -1f)));
        var tape = new GameObject("Tape Measure").transform;
        tape.SetParent(room, false);
        tape.SetPositionAndRotation(new Vector3(1.02f, 0f, 1.78f), Quaternion.Euler(0f, 25f, 0f));
        Box(tape, "Case", new Vector3(0.08f, 0.075f, 0.04f), new Vector3(0f, 0.0375f, 0f), label, 0.015f);
        Box(tape, "Grip", new Vector3(0.082f, 0.04f, 0.042f), new Vector3(0f, 0.04f, 0f), rubber, 0.01f);
        var hammer = new GameObject("Hammer").transform;
        hammer.SetParent(room, false);
        hammer.SetPositionAndRotation(new Vector3(1.22f, 0f, 2.05f), Quaternion.Euler(0f, -60f, 0f));
        Box(hammer, "Handle", new Vector3(0.03f, 0.025f, 0.3f), new Vector3(0f, 0.0125f, 0f), Lit("Wood", Hex("B07A45"), 0.3f), 0.01f);
        Box(hammer, "Head", new Vector3(0.11f, 0.035f, 0.035f), new Vector3(0f, 0.0175f, 0.15f), dark, 0.008f);
        var bottle = new GameObject("Water Bottle").transform;
        bottle.SetParent(room, false);
        bottle.position = new Vector3(-0.35f, 0f, -0.2f);
        Cylinder(bottle, "Bottle", 0.035f, 0.2f, new Vector3(0f, 0.1f, 0f), Quaternion.identity, Lit("Bottle", new Color(0.8f, 0.9f, 0.96f), 0.9f));
        Cylinder(bottle, "Cap", 0.017f, 0.025f, new Vector3(0f, 0.212f, 0f), Quaternion.identity, Lit("Cap", Hex("2F6FD1"), 0.5f));
        Cylinder(room, "Duct Tape", 0.055f, 0.05f, new Vector3(-0.42f, 0.025f, -1.05f), Quaternion.identity, Lit("Duct Tape", Hex("C62828"), 0.5f));
    }

    // ---------------------------------------------------------------- the floor

    // The floor with two real holes over a black pit: the sawn circle, filled by a loose disc, and the crash spot,
    // filled by a patch in pieces. FloorFX carries everything that moves, animated by the returned clip.
    static GameObject BuildFloor(float circleRadius, float sawFrom, Matrix4x4 sawAtRelease, Quaternion actorRotation, out AnimationClip clip)
    {
        var osb = Lit("OSB", Color.white, 0.18f);
        osb.SetTexture("_BaseMap", OsbTexture());
        var edge = Lit("OSB Edge", Hex("C9A46E"), 0.15f);

        var rng = new Random(11);
        var crashOutline = new float[96];
        for (int i = 0; i < crashOutline.Length; i++) crashOutline[i] = CrashRadius * (0.82f + 0.3f * (float)rng.NextDouble());
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < crashOutline.Length; i++) // soften, keeping some spikes
                crashOutline[i] = 0.6f * crashOutline[i] + 0.2f * (crashOutline[(i + 1) % crashOutline.Length] + crashOutline[(i + crashOutline.Length - 1) % crashOutline.Length]);
        float CrashEdge(float a) => crashOutline[Mathf.RoundToInt(a / 360f * crashOutline.Length + crashOutline.Length * 4) % crashOutline.Length];
        const float kerf = 0.003f;
        var holes = new[]
        {
            (CircleCenter, new Vector2(circleRadius + 0.06f, circleRadius + 0.06f), (Func<float, float>)(a => circleRadius + kerf)),
            (CrashCenter, new Vector2(CrashRadius * 1.25f + 0.04f, CrashRadius * 1.25f + 0.04f), (Func<float, float>)CrashEdge),
        };

        var floor = new GameObject("Floor");
        floor.AddComponent<MeshFilter>().sharedMesh = SaveMesh(FloorMesh(holes), "FloorSaw_Floor");
        floor.AddComponent<MeshRenderer>().sharedMaterial = osb;
        foreach (var (center, _, outline) in holes)
            Part(floor.transform, "Hole Edge", SaveMesh(HoleWall(center, outline), "FloorSaw_HoleEdge" + (center == CircleCenter ? "Circle" : "Crash")), edge);
        var pit = Box(null, "Pit", new Vector3(CornerX - FloorMinX, 10f, FloorMaxZ - BackZ), new Vector3((CornerX + FloorMinX) / 2f, -5f - FloorThickness - 0.002f, (BackZ + FloorMaxZ) / 2f),
            Unlit("Pit", Color.black));
        pit.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var fx = new GameObject("FloorFX");
        fx.AddComponent<Animator>();

        // The sawn circle: a loose disc of floor filling the hole edge to edge (no gap for the pit to show through
        // before it drops), with the inner half of the sawdust line on it; the outer half stays
        var disc = Part(fx.transform, "Disc", SaveMesh(DiscMesh(circleRadius + kerf), "FloorSaw_Disc"), osb);
        disc.transform.localPosition = CircleCenter;
        disc.GetComponent<MeshRenderer>().sharedMaterials = new[] { osb, edge };
        var sawdust = Lit("Sawdust", Color.white, 0.1f);
        sawdust.SetTexture("_BaseMap", SawdustTexture());
        sawdust.SetFloat("_AlphaClip", 1f);
        sawdust.EnableKeyword("_ALPHATEST_ON");
        sawdust.SetFloat("_Cutoff", 1f);
        var dustOut = Part(fx.transform, "Sawdust", SaveMesh(SawdustRing(circleRadius, circleRadius + 0.035f, sawFrom, 0.5f, 1f), "FloorSaw_SawdustOuter"), sawdust);
        dustOut.transform.localPosition = CircleCenter + Vector3.up * 0.0015f;
        var dustIn = Part(disc.transform, "Sawdust", SaveMesh(SawdustRing(circleRadius - 0.035f, circleRadius, sawFrom, 0f, 0.5f), "FloorSaw_SawdustInner"), sawdust);
        dustIn.transform.localPosition = Vector3.up * 0.0015f;
        foreach (var r in new[] { dustOut, dustIn }) r.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // The dropped saw lies on the disc, so it goes down with it; hidden until he lets go of his
        var dropped = Saw(disc.transform, "Saw");
        dropped.SetActive(false);

        // The crash patch in shards, and the cracks it leaves
        var patch = new GameObject("Patch").transform;
        patch.SetParent(fx.transform, false);
        var shards = Shards(CrashCenter, CrashEdge, 7, new Random(5));
        for (int i = 0; i < shards.Count; i++)
        {
            var shard = Part(patch, "Shard" + i, SaveMesh(shards[i].mesh, "FloorSaw_Shard" + i), osb);
            shard.GetComponent<MeshRenderer>().sharedMaterials = new[] { osb, edge };
            shard.transform.localPosition = shards[i].center;
        }
        var cracks = Lit("Cracks", Color.white, 0.1f);
        cracks.SetTexture("_BaseMap", CrackTexture(CrashEdge));
        cracks.SetFloat("_AlphaClip", 1f);
        cracks.EnableKeyword("_ALPHATEST_ON");
        cracks.SetFloat("_Cutoff", 0.5f);
        var crackDecal = Part(fx.transform, "Cracks", SaveMesh(FlatQuad(CrackSpan), "FloorSaw_Cracks"), cracks);
        crackDecal.transform.localPosition = CrashCenter + Vector3.up * 0.0015f;
        crackDecal.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        crackDecal.SetActive(false);

        clip = FloorClip(disc.transform, dropped.transform, sawAtRelease, actorRotation, circleRadius, shards);
        return fx;
    }

    // The floor's moves: the sawdust line drawn round behind the saw, the dropped saw tipping over onto the disc, the
    // patch breaking under him, and the disc lagging, then dropping
    static AnimationClip FloorClip(Transform disc, Transform saw, Matrix4x4 sawAtRelease, Quaternion actorRotation, float circleRadius,
        List<(Mesh mesh, Vector3 center)> shards)
    {
        var curves = new Dictionary<(string path, Type type, string property), List<Keyframe>>();
        void Key(string path, Type type, string property, float t, float value, bool step = false)
        {
            if (!curves.TryGetValue((path, type, property), out var keys)) curves[(path, type, property)] = keys = new List<Keyframe>();
            keys.Add(step ? new Keyframe(t, value, float.PositiveInfinity, float.PositiveInfinity) : new Keyframe(t, value));
        }
        void KeyTransform(string path, float t, Vector3 position, Quaternion rotation)
        {
            Key(path, typeof(Transform), "m_LocalPosition.x", t, position.x);
            Key(path, typeof(Transform), "m_LocalPosition.y", t, position.y);
            Key(path, typeof(Transform), "m_LocalPosition.z", t, position.z);
            Key(path, typeof(Transform), "m_LocalRotation.x", t, rotation.x);
            Key(path, typeof(Transform), "m_LocalRotation.y", t, rotation.y);
            Key(path, typeof(Transform), "m_LocalRotation.z", t, rotation.z);
            Key(path, typeof(Transform), "m_LocalRotation.w", t, rotation.w);
        }

        // Where the saw comes to rest: flat on the disc in front of his feet, across him, teeth to the camera
        var restInActor = Matrix4x4.TRS(new Vector3(-0.29f + HandleHalf, HandleThickness / 2f, SawRestDepth(circleRadius)),
            Quaternion.LookRotation(Vector3.forward, Vector3.left), Vector3.one);
        var rest = Matrix4x4.TRS(CircleCenter, actorRotation, Vector3.one) * restInActor;
        Vector3 fromPosition = sawAtRelease.GetColumn(3), toPosition = rest.GetColumn(3);
        Quaternion fromRotation = sawAtRelease.rotation, toRotation = rest.rotation;
        const float fallTime = 0.42f, bounceTime = 0.14f;

        var rng = new Random(3);
        var spin = new List<(Vector3 axis, float speed, float delay, Vector3 drift)>();
        foreach (var _ in shards)
            spin.Add((new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f).normalized, 140f + 220f * (float)rng.NextDouble(),
                0.06f * (float)rng.NextDouble(), new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.3f));

        var lagRng = new Random(9);
        var jitter = Vector3.zero;
        int frames = Mathf.RoundToInt(Duration * Fps);
        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;

            // Sawdust: the line grows round behind the saw (alpha falls from 1 to 0 round the ring; Cutoff hides the rest)
            float cutoff = 1f - SawProgress(t);
            Key("Sawdust", typeof(MeshRenderer), "material._Cutoff", t, cutoff);
            Key("Disc/Sawdust", typeof(MeshRenderer), "material._Cutoff", t, cutoff);

            // The saw: in his hand till Release, then falls, swinging flat, and bounces once
            Key("Disc/Saw", typeof(GameObject), "m_IsActive", t, t >= Release ? 1f : 0f, true);
            float s = Mathf.Clamp01((t - Release) / fallTime);
            var position = Vector3.Lerp(fromPosition, toPosition, Mathf.SmoothStep(0f, 1f, s));
            position.y = Mathf.Lerp(fromPosition.y, toPosition.y, s * s);
            var rotation = Quaternion.Slerp(fromRotation, toRotation, s * s * (2f - s));
            float b = (t - Release - fallTime) / bounceTime;
            if (b > 0f && b < 1f)
            {
                position.y += 0.025f * Mathf.Sin(b * Mathf.PI);
                rotation = Quaternion.AngleAxis(6f * Mathf.Sin(b * Mathf.PI), actorRotation * Vector3.forward) * rotation;
            }
            KeyTransform("Disc/Saw", t, position - CircleCenter, rotation);

            // The patch holds until he steps on it, then each shard drops, tumbling
            for (int i = 0; i < shards.Count; i++)
            {
                float d = Mathf.Max(0f, t - Crash - spin[i].delay);
                var p = shards[i].center + spin[i].drift * d + Vector3.down * Mathf.Min(0.5f * Gravity * d * d, 12f);
                KeyTransform("Patch/Shard" + i, t, p, Quaternion.AngleAxis(spin[i].speed * d, spin[i].axis));
            }
            Key("Cracks", typeof(GameObject), "m_IsActive", t, t >= Crash ? 1f : 0f, true);

            // The disc: still, then lag (jumps about on the spot), then down it goes, tilting
            if (t >= LagStart && t < LagDrop && f % 3 == 0)
                jitter = new Vector3((float)lagRng.NextDouble() - 0.5f, 0.6f * (float)lagRng.NextDouble(), (float)lagRng.NextDouble() - 0.5f) * 0.03f;
            if (t < LagStart || t >= LagDrop) jitter = Vector3.zero;
            float fall = Mathf.Max(0f, t - LagDrop);
            KeyTransform("Disc", t, CircleCenter + jitter + Vector3.down * Mathf.Min(0.5f * Gravity * fall * fall, 12f),
                Quaternion.AngleAxis(Mathf.Min(fall * 60f, 35f), actorRotation * Vector3.right));
        }

        var clip = SavedClip("FloorSaw_Floor");
        foreach (var pair in curves)
        {
            var (path, type, property) = pair.Key;
            var binding = EditorCurveBinding.FloatCurve(path, type, property);
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(pair.Value.ToArray()));
        }
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // How far in front of his feet the dropped saw lies: as far forward as the circle lets a saw-length chord go
    static float SawRestDepth(float circleRadius)
    {
        float half = 0.3f, room = circleRadius - 0.04f;
        return room > half ? Mathf.Sqrt(room * room - half * half) : 0.1f;
    }

    // ---------------------------------------------------------------- floor meshes

    // The floor at y = 0 with holes cut in it. Each hole sits in its own rectangle (centre, half extents), filled by a
    // ring from the hole's outline (radius by direction, degrees) out to the rectangle; the rest of the floor is
    // quads between the rectangles. UVs map world XZ onto one OSB sheet.
    static Mesh FloorMesh((Vector3 center, Vector2 half, Func<float, float> outline)[] holes)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = vertices.Count;
            vertices.AddRange(new[] { a, b, c, d });
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        var xs = new List<float> { FloorMinX, CornerX };
        var zs = new List<float> { BackZ, FloorMaxZ };
        foreach (var (center, half, _) in holes)
        {
            xs.Add(center.x - half.x);
            xs.Add(center.x + half.x);
            zs.Add(center.z - half.y);
            zs.Add(center.z + half.y);
        }
        xs.Sort();
        zs.Sort();
        for (int i = 0; i + 1 < xs.Count; i++)
        for (int j = 0; j + 1 < zs.Count; j++)
        {
            float x0 = xs[i], x1 = xs[i + 1], z0 = zs[j], z1 = zs[j + 1];
            if (x1 - x0 < 1e-4f || z1 - z0 < 1e-4f) continue;
            var middle = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
            bool inHole = false;
            foreach (var (center, half, _) in holes)
                inHole |= Mathf.Abs(middle.x - center.x) < half.x && Mathf.Abs(middle.z - center.z) < half.y;
            if (!inHole) Quad(new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0));
        }

        foreach (var (center, half, outline) in holes)
        {
            // Sample round the hole, including the rectangle's corners so its edges stay straight
            var angles = new List<float>();
            for (int i = 0; i < 96; i++) angles.Add(i * 360f / 96f);
            float corner = Mathf.Atan2(half.x, half.y) * Mathf.Rad2Deg;
            angles.AddRange(new[] { corner, 180f - corner, 180f + corner, 360f - corner });
            angles.Sort();
            for (int i = 0; i < angles.Count; i++)
            {
                float a0 = angles[i], a1 = angles[(i + 1) % angles.Count] + (i + 1 == angles.Count ? 360f : 0f);
                if (a1 - a0 < 1e-3f) continue;
                Vector3 d0 = Direction(a0), d1 = Direction(a1);
                Vector3 in0 = center + d0 * outline(a0), in1 = center + d1 * outline(a1);
                Vector3 out0 = center + d0 * ToRectangle(d0, half), out1 = center + d1 * ToRectangle(d1, half);
                Quad(in0, out0, out1, in1);
            }
        }

        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(FacingUp(vertices, triangles), 0);
        mesh.SetUVs(0, vertices.ConvertAll(v => new Vector2(v.x, v.z) / SheetSize));
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Unit vector on the floor for a direction in degrees, measured like a yaw (0 = +Z, 90 = +X)
    static Vector3 Direction(float degrees) => new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad));

    static float ToRectangle(Vector3 d, Vector2 half) =>
        Mathf.Min(Mathf.Abs(d.x) > 1e-5f ? half.x / Mathf.Abs(d.x) : float.MaxValue, Mathf.Abs(d.z) > 1e-5f ? half.y / Mathf.Abs(d.z) : float.MaxValue);

    // Flips any triangle whose winding faces down, so every face of a flat mesh faces up
    static List<int> FacingUp(List<Vector3> vertices, List<int> triangles)
    {
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            if (Vector3.Cross(b - a, c - a).y < 0f) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }
        return triangles;
    }

    // The floor's cut edge round a hole, facing into it
    static Mesh HoleWall(Vector3 center, Func<float, float> outline)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        const int n = 96;
        for (int i = 0; i <= n; i++)
        {
            float a = i * 360f / n;
            var p = center + Direction(a) * outline(a % 360f);
            vertices.Add(p);
            vertices.Add(p + Vector3.down * FloorThickness);
        }
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i;
            triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(Facing(vertices, triangles, center, inward: true), 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Flips side triangles so they face towards (inward) or away from a vertical axis through `center`
    static List<int> Facing(List<Vector3> vertices, List<int> triangles, Vector3 center, bool inward)
    {
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            var normal = Vector3.Cross(b - a, c - a);
            var away = (a + b + c) / 3f - center;
            away.y = 0f;
            if (Vector3.Dot(normal, away) > 0f == inward) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }
        return triangles;
    }

    // The loose disc, centred on its own origin: top (OSB, UVs matching the floor round CircleCenter) as submesh 0,
    // rim and underside (bare edge) as submesh 1
    static Mesh DiscMesh(float radius)
    {
        const int n = 96;
        var vertices = new List<Vector3>();
        var top = new List<int>();
        var rest = new List<int>();
        vertices.Add(Vector3.zero);
        for (int i = 0; i < n; i++) vertices.Add(Direction(i * 360f / n) * radius);
        for (int i = 0; i < n; i++) top.AddRange(new[] { 0, 1 + i, 1 + (i + 1) % n });
        int rim = vertices.Count;
        for (int i = 0; i <= n; i++)
        {
            var p = Direction(i * 360f / n) * radius;
            vertices.Add(p);
            vertices.Add(p + Vector3.down * FloorThickness);
        }
        for (int i = 0; i < n; i++)
        {
            int a = rim + 2 * i;
            rest.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        int bottom = vertices.Count;
        vertices.Add(Vector3.down * FloorThickness);
        for (int i = 0; i < n; i++) vertices.Add(Direction(i * 360f / n) * radius + Vector3.down * FloorThickness);
        for (int i = 0; i < n; i++) rest.AddRange(new[] { bottom, bottom + 1 + (i + 1) % n, bottom + 1 + i });

        FacingUp(vertices, top);
        var rimTriangles = rest.GetRange(0, n * 6);
        Facing(vertices, rimTriangles, Vector3.zero, inward: false);
        var underside = rest.GetRange(n * 6, rest.Count - n * 6);
        for (int i = 0; i < underside.Count; i += 3) // face down
        {
            Vector3 a = vertices[underside[i]], b = vertices[underside[i + 1]], c = vertices[underside[i + 2]];
            if (Vector3.Cross(b - a, c - a).y > 0f) (underside[i + 1], underside[i + 2]) = (underside[i + 2], underside[i + 1]);
        }
        rimTriangles.AddRange(underside);

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(top, 0);
        mesh.SetTriangles(rimTriangles, 1);
        mesh.SetUVs(0, vertices.ConvertAll(v => new Vector2(v.x + CircleCenter.x, v.z + CircleCenter.z) / SheetSize));
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // The sawdust line round the circle, centred on its own origin: u runs round from `startDegrees` the way he turns (Turn),
    // v across from `inner` (vFrom) to `outer` (vTo), so the inner and outer halves share one texture
    static Mesh SawdustRing(float inner, float outer, float startDegrees, float vFrom, float vTo)
    {
        const int n = 160;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i <= n; i++)
        {
            var d = Direction(startDegrees + Mathf.Sign(Turn) * i * 360f / n);
            vertices.Add(d * inner);
            vertices.Add(d * outer);
            uvs.Add(new Vector2(i / (float)n, vFrom));
            uvs.Add(new Vector2(i / (float)n, vTo));
        }
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i;
            triangles.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(FacingUp(vertices, triangles), 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // The crash patch as wedge-shaped shards round a point near the middle, each centred on its own origin; top (OSB,
    // UVs matching the floor) as submesh 0, edges and underside as submesh 1
    static List<(Mesh mesh, Vector3 center)> Shards(Vector3 center, Func<float, float> outline, int count, Random rng)
    {
        var hub = center + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.08f;
        var cuts = new List<float>();
        for (int i = 0; i < count; i++) cuts.Add((i + 0.25f + 0.5f * (float)rng.NextDouble()) * 360f / count);
        var shards = new List<(Mesh, Vector3)>();
        for (int k = 0; k < count; k++)
        {
            float a0 = cuts[k], a1 = cuts[(k + 1) % count] + (k + 1 == count ? 360f : 0f);
            var outlinePoints = new List<Vector3> { hub };
            for (float a = a0; ; a += 4f)
            {
                float at = Mathf.Min(a, a1);
                outlinePoints.Add(center + Direction(at) * (outline(at % 360f) + 0.004f));
                if (at >= a1) break;
            }
            var middle = Vector3.zero;
            foreach (var p in outlinePoints) middle += p;
            middle /= outlinePoints.Count;
            middle.y = 0f;

            var vertices = new List<Vector3>();
            var top = new List<int>();
            var sides = new List<int>();
            int m = outlinePoints.Count;
            foreach (var p in outlinePoints) vertices.Add(p - middle);
            foreach (var p in outlinePoints) vertices.Add(p - middle + Vector3.down * FloorThickness);
            for (int i = 1; i + 1 < m; i++)
            {
                top.AddRange(new[] { 0, i, i + 1 });
                sides.AddRange(new[] { m, m + i + 1, m + i }); // underside
            }
            for (int i = 0; i < m; i++)
            {
                int j = (i + 1) % m;
                sides.AddRange(new[] { i, j, m + i, m + i, j, m + j });
            }
            FacingUp(vertices, top);
            for (int i = 0; i < sides.Count; i += 3) // every side and the underside faces away from the shard's middle or down
            {
                Vector3 a = vertices[sides[i]], b = vertices[sides[i + 1]], c = vertices[sides[i + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                var away = (a + b + c) / 3f - new Vector3(0f, -FloorThickness / 2f, 0f);
                if (Mathf.Abs(normal.y) > 0.9f * normal.magnitude) away = Vector3.down;
                if (Vector3.Dot(normal, away) < 0f) (sides[i + 1], sides[i + 2]) = (sides[i + 2], sides[i + 1]);
            }

            // Split the shared rim so the top keeps its own normals
            var mesh = new Mesh();
            var allVertices = new List<Vector3>(vertices);
            var topSplit = new List<int>();
            foreach (int i in top)
            {
                topSplit.Add(allVertices.Count);
                allVertices.Add(vertices[i]);
            }
            mesh.SetVertices(allVertices);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(topSplit, 0);
            mesh.SetTriangles(sides, 1);
            mesh.SetUVs(0, allVertices.ConvertAll(v => new Vector2(v.x + middle.x, v.z + middle.z) / SheetSize));
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            shards.Add((mesh, middle));
        }
        return shards;
    }

    const float CrackSpan = 1.5f; // metres across the crack decal

    static Mesh FlatQuad(float size)
    {
        float h = size / 2f;
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h) },
            uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------- textures

    // Oriented strand board: big flat chips of light and dark wood at random angles on a tan ground, a darker seam at the
    // sheet's edge
    static Texture2D OsbTexture()
    {
        const int size = 1024;
        var rng = new Random(21);
        var pixels = new Color32[size * size];
        var ground = new Color(0.84f, 0.68f, 0.47f);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = ground;
        for (int chip = 0; chip < 3600; chip++)
        {
            float cx = (float)rng.NextDouble() * size, cy = (float)rng.NextDouble() * size;
            float length = 26f + 52f * (float)rng.NextDouble(), width = 8f + 12f * (float)rng.NextDouble();
            float angle = (float)rng.NextDouble() * Mathf.PI;
            float shade = (float)rng.NextDouble();
            var color = Color.Lerp(new Color(0.7f, 0.52f, 0.31f), new Color(0.93f, 0.8f, 0.6f), shade);
            color = Color.Lerp(color, new Color(0.85f, 0.62f, 0.36f), 0.2f * (float)rng.NextDouble());
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            int reach = Mathf.CeilToInt(length);
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                float u = (dx * ca + dy * sa) / length, v = (-dx * sa + dy * ca) / width;
                if (u * u + v * v > 1f) continue;
                int x = ((int)cx + dx + size) % size, y = ((int)cy + dy + size) % size;
                float grain = 0.93f + 0.07f * Mathf.Sin((dx * ca + dy * sa) * 0.9f + chip);
                pixels[y * size + x] = color * grain;
            }
        }
        for (int i = 0; i < size; i++)
            foreach (int edge in new[] { 0, 1, size - 1 })
            {
                pixels[edge * size + i] = (Color)pixels[edge * size + i] * 0.6f;
                pixels[i * size + edge] = (Color)pixels[i * size + edge] * 0.6f;
            }
        return SaveTexture(pixels, size, size, "FloorSaw_OSB", TextureWrapMode.Repeat);
    }

    // Across v: pale sawdust speckle either side of a dark kerf line in the middle; alpha falls from 1 to 0 along u, so
    // the material's Cutoff reveals the line from u = 0 onwards
    static Texture2D SawdustTexture()
    {
        const int width = 1024, height = 32;
        var rng = new Random(33);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float v = (y + 0.5f) / height, across = Mathf.Abs(v - 0.5f) * 2f;
            float alpha = 0.995f * (1f - (x + 0.5f) / width);
            var c = Color.clear;
            if (across < 0.12f) c = new Color(0.22f, 0.15f, 0.09f);
            else if (rng.NextDouble() < 0.9f - 0.85f * across) c = Color.Lerp(new Color(0.97f, 0.88f, 0.68f), new Color(0.9f, 0.78f, 0.55f), (float)rng.NextDouble());
            else alpha = 0f;
            c.a = alpha;
            pixels[y * width + x] = c;
        }
        return SaveTexture(pixels, width, height, "FloorSaw_Sawdust", TextureWrapMode.Clamp);
    }

    // Black cracks running out from the crash hole's ragged edge, on a decal CrackSpan across centred on the hole
    static Texture2D CrackTexture(Func<float, float> outline)
    {
        const int size = 512;
        float perMetre = size / CrackSpan;
        var rng = new Random(44);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(20, 16, 12, 0);
        void Dot(Vector2 p, float radius)
        {
            for (int dy = -Mathf.CeilToInt(radius); dy <= Mathf.CeilToInt(radius); dy++)
            for (int dx = -Mathf.CeilToInt(radius); dx <= Mathf.CeilToInt(radius); dx++)
            {
                int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                if (x < 0 || y < 0 || x >= size || y >= size || dx * dx + dy * dy > radius * radius) continue;
                pixels[y * size + x].a = 255;
            }
        }
        void Crack(Vector2 from, float heading, float length, float width, int depth)
        {
            var p = from;
            for (float run = 0f; run < length; run += 2f)
            {
                heading += ((float)rng.NextDouble() - 0.5f) * 0.5f;
                p += new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)) * 2f;
                Dot(p, Mathf.Lerp(width, 0.6f, run / length));
                if (depth < 2 && rng.NextDouble() < 0.025f)
                    Crack(p, heading + (rng.NextDouble() < 0.5 ? -0.7f : 0.7f), (length - run) * 0.5f, width * 0.6f, depth + 1);
            }
        }
        var middle = new Vector2(size / 2f, size / 2f);
        for (int k = 0; k < 11; k++)
        {
            float a = (k + (float)rng.NextDouble() * 0.7f) * 360f / 11f;
            var d = Direction(a);
            var start = middle + new Vector2(d.x, d.z) * (outline(a % 360f) - 0.01f) * perMetre;
            Crack(start, a * Mathf.Deg2Rad, (0.12f + 0.22f * (float)rng.NextDouble()) * perMetre, 3.2f, 0);
        }
        // A dark rim right round the hole where the board splintered
        for (int i = 0; i < 360; i++)
        {
            var d = Direction(i);
            Dot(middle + new Vector2(d.x, d.z) * outline(i) * perMetre, 3.5f);
        }
        return SaveTexture(pixels, size, size, "FloorSaw_Cracks", TextureWrapMode.Clamp);
    }

    static Texture2D SaveTexture(Color32[] pixels, int width, int height, string name, TextureWrapMode wrap)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        string path = $"{TextureFolder}/{name}.png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = wrap;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------------------------------------------------------- props

    // A hand saw (see SawTip): yellow and black handle round the grip, a steel blade tapering to the tip
    static GameObject Saw(Transform parent, string name)
    {
        var saw = new GameObject(name);
        saw.transform.SetParent(parent, false);
        Box(saw.transform, "Handle", new Vector3(HandleThickness, 2f * HandleHalf, 0.13f), new Vector3(0f, 0.005f, 0.005f), Lit("Saw Handle", Hex("F2C230"), 0.35f), 0.012f);
        Box(saw.transform, "Handle Grip", new Vector3(HandleThickness + 0.004f, 0.07f, 0.04f), new Vector3(0f, 0.01f, -0.035f), Lit("Saw Grip", Hex("1E1E22"), 0.25f), 0.01f);
        var blade = new GameObject("Blade");
        blade.transform.SetParent(saw.transform, false);
        blade.AddComponent<MeshFilter>().sharedMesh = SaveMesh(BladeMesh(), "FloorSaw_Blade");
        blade.AddComponent<MeshRenderer>().sharedMaterial = Lit("Saw Blade", new Color(0.78f, 0.8f, 0.83f), 0.5f, 0.7f); // satin, not a mirror
        return saw;
    }

    // The blade: a thin plate from just under the handle to the tip, its toothed edge (+Z) straight and its back tapering
    // in towards the tip, with a row of raked teeth along the edge
    static Mesh BladeMesh()
    {
        const float thickness = 0.003f, edge = 0.06f, pitch = 0.012f, depth = 0.007f;
        float top = -HandleHalf + 0.01f, bottom = -SawLength;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        // A flat polygon (y, z) on both faces, each facing out, fanned from its first corner
        void Faces(params Vector2[] outline)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                int i = vertices.Count;
                foreach (var p in outline) vertices.Add(new Vector3(side * thickness / 2f, p.x, p.y));
                for (int k = 1; k + 1 < outline.Length; k++)
                {
                    int a = i, b = i + k, c = i + k + 1;
                    if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).x * side < 0f) (b, c) = (c, b);
                    triangles.AddRange(new[] { a, b, c });
                }
            }
        }
        Vector2[] plate = { new Vector2(top, -0.065f), new Vector2(top, edge), new Vector2(bottom, edge), new Vector2(bottom, 0.015f) };
        Faces(plate);
        int faceTriangles = triangles.Count;
        for (int k = 0; k < plate.Length; k++)
        {
            int i = vertices.Count;
            Vector2 a = plate[k], b = plate[(k + 1) % plate.Length];
            vertices.AddRange(new[] { new Vector3(-thickness / 2f, a.x, a.y), new Vector3(thickness / 2f, a.x, a.y), new Vector3(thickness / 2f, b.x, b.y), new Vector3(-thickness / 2f, b.x, b.y) });
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        // Rim faces: point each away from the blade's middle
        var middle = new Vector3(0f, (top + bottom) / 2f, (edge - 0.03f) / 2f);
        for (int t = faceTriangles; t < triangles.Count; t += 3)
        {
            Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - middle) < 0f) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
        }
        // Teeth, raked towards the tip
        for (float y = top - 0.004f; y - pitch > bottom; y -= pitch)
            Faces(new Vector2(y, edge), new Vector2(y - pitch * 0.75f, edge + depth), new Vector2(y - pitch, edge));

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static readonly Dictionary<string, Mesh> boxes = new Dictionary<string, Mesh>();

    static GameObject Box(Transform parent, string name, Vector3 size, Vector3 position, Material material, float radius = 0f)
    {
        string key = $"FloorSaw_Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_r{radius:0.###}";
        if (!boxes.TryGetValue(key, out var mesh) || mesh == null)
            boxes[key] = mesh = SaveMesh(RoundedBoxMesh.Create(size, radius, radius > 0f ? 3 : 1), key);
        var go = Part(parent, name, mesh, material);
        go.transform.localPosition = position;
        return go;
    }

    static GameObject Cylinder(Transform parent, string name, float radius, float height, Vector3 position, Quaternion rotation, Material material)
    {
        string key = $"FloorSaw_Cylinder_{radius:0.###}x{height:0.###}";
        if (!boxes.TryGetValue(key, out var mesh) || mesh == null)
            boxes[key] = mesh = SaveMesh(CylinderMesh(radius, height), key);
        var go = Part(parent, name, mesh, material);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        return go;
    }

    // Upright cylinder centred on its origin, with flat caps
    static Mesh CylinderMesh(float radius, float height)
    {
        const int n = 32;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= n; i++)
        {
            var d = Direction(i * 360f / n);
            vertices.Add(d * radius + Vector3.up * height / 2f);
            vertices.Add(d * radius + Vector3.down * height / 2f);
            normals.Add(d);
            normals.Add(d);
        }
        for (int i = 0; i < n; i++)
        {
            int a = 2 * i;
            triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        foreach (float y in new[] { 1f, -1f })
        {
            int c = vertices.Count;
            vertices.Add(Vector3.up * y * height / 2f);
            normals.Add(Vector3.up * y);
            for (int i = 0; i < n; i++)
            {
                vertices.Add(Direction(i * 360f / n) * radius + Vector3.up * y * height / 2f);
                normals.Add(Vector3.up * y);
            }
            for (int i = 0; i < n; i++)
                triangles.AddRange(y > 0f ? new[] { c, c + 1 + i, c + 1 + (i + 1) % n } : new[] { c, c + 1 + (i + 1) % n, c + 1 + i });
        }
        // Sides face out
        for (int t = 0; t < n * 6; t += 3)
        {
            Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
            var away = (a + b + c) / 3f;
            away.y = 0f;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), away) < 0f) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
        }
        for (int t = n * 6; t < triangles.Count; t += 3)
        {
            Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normals[triangles[t]]) < 0f) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
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

    static Material Lit(string name, Color color, float smoothness, float metallic = 0f) =>
        SavedMaterial(name, "Universal Render Pipeline/Lit", m =>
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
        });

    static Material Unlit(string name, Color color) =>
        SavedMaterial(name, "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", color));

    static Material SavedMaterial(string name, string shader, Action<Material> setup)
    {
        if (materials.TryGetValue(name, out var material)) return material;
        string path = $"{MaterialFolder}/FloorSaw_{name.Replace(' ', '_')}.mat";
        material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null || material.shader.name != shader)
        {
            if (material != null) AssetDatabase.DeleteAsset(path);
            material = new Material(Shader.Find(shader));
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
    // `probe` reads something off each posed frame (the saw's grip, in the actor's frame); sawAt looks it up by time.
    static AnimationClip BakeClip(GameObject prefab, Scene stage, Func<GameObject, float, Matrix4x4> probe, out Func<float, Matrix4x4> sawAt)
    {
        var go = Object.Instantiate(prefab);
        SceneManager.MoveGameObjectToScene(go, stage);
        // HumanPoseHandler reads the body in world space but writes it relative to the root: pose at the origin
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var bones = Array.ConvertAll(Bones, b => FindOptional(go, b)); // clavicles only where the actor has them
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
        var probes = new Matrix4x4[frames + 1];
        var previous = Quaternion.identity;
        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;
            var pose = Act(t);
            for (int b = 0; b < bones.Length; b++)
                if (bones[b] != null) bones[b].localRotation = Quaternion.Euler(pose.e[b]);
            bones[0].localPosition = hipsRest + pose.hips;
            SawArm(name => bones[Array.IndexOf(Bones, name)], t);
            probes[f] = probe(go, t);
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
        sawAt = t => probes[Mathf.Clamp(Mathf.RoundToInt(t * Fps), 0, frames)];

        var clip = SavedClip("FloorSaw_Builder");
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        for (int i = 0; i < names.Count; i++)
        {
            bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), names[i]));
            curves.Add(SampledCurve(samples, i, frames));
        }
        var emotion = new AnimationCurve();
        foreach (var (time, e) in Faces) emotion.AddKey(StepKey(time, RobloxFace.EmotionKey(e)));
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

    // Middle of the right hand at rest: its own mesh where it has one (Tripo's rigid parts), else the skinned vertices
    // that ride mostly on the hand bone
    static Vector3 FistCenter(GameObject body)
    {
        foreach (var t in body.GetComponentsInChildren<Transform>(true))
            if (t.name == "RightHandMesh") return t.GetComponent<Renderer>().bounds.center;
        var skin = body.GetComponentInChildren<SkinnedMeshRenderer>();
        int hand = Array.FindIndex(skin.bones, b => b.name == "RightHand");
        var vertices = skin.sharedMesh.vertices;
        var weights = skin.sharedMesh.boneWeights;
        var sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < vertices.Length; i++)
            if (weights[i].boneIndex0 == hand && weights[i].weight0 > 0.5f)
            {
                sum += skin.transform.TransformPoint(vertices[i]);
                count++;
            }
        return sum / Mathf.Max(count, 1);
    }

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

    static void AddLight(string name, Color color, float intensity, Vector3 euler, LightShadows shadows)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = shadows;
        light.shadowStrength = 0.85f;
        light.transform.rotation = Quaternion.Euler(euler);
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
