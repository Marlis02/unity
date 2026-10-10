using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

// Animation test for the main character, bacon: one take through the moves the shorts will need, a second or two
// each, on the lineup's plain studio. He looks around, walks off to his right, turns, runs across, walks back to the
// middle, jumps, cheers with both arms up, waves, squats, shrugs, bows, kicks and points. Poses are set per bone and
// keyed straight onto the bones (a generic clip, see BakeClip).
// Tools/Characters/Build Animation Test -> Assets/Scenes/Test_MinifigAnimations.unity + Timeline.
public static class MinifigAnimationTestBuilder
{
    const float Fps = 30f;
    const string ScenePath = "Assets/Scenes/Test_MinifigAnimations.unity";
    const string TimelinePath = "Assets/Timelines/Test_MinifigAnimations.playable";
    const string ClipPath = "Assets/Animations/MinifigTest_Hero.anim";
    const string ActorPrefab = MinifigCharacterBuilder.PrefabFolder + "/bacon.prefab";

    // Beats, seconds
    const float LookFrom = 0.5f, JumpUp = 8.9f, JumpDown = 9.45f, JumpHeight = 0.5f;
    const float CheerFrom = 10f, WaveFrom = 11.8f, SquatFrom = 13.8f, ShrugFrom = 15.4f, BowFrom = 16.8f;
    const float KickFrom = 18.4f, PointFrom = 20f, PointTo = 21.4f;
    // The moves take 22.5 s; the face test as long as its faces
    static float Duration => FaceTest ? LookFrom + Faces.Length * ExpressionTime + 1f : 22.5f;
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

    static readonly string[] Bones =
    {
        "Hips", "Spine", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
        "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
    };
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

    // Face test (the user, 2026-10-09: "take the body's movement out, I can't see the face"): he stands still, the
    // camera close on his head, and only the face plays its expressions. False: the moves as well.
    const bool FaceTest = true;
    const float FaceHeight = 1.64f; // metres, the middle of his face

    static Pose Act(float t)
    {
        if (FaceTest) return Idle(0f);
        var p = Idle(t);
        p.move.x = X(t);
        p.Rot("Hips", 0f, Yaw(t), 0f);
        p.Add(LookAround(t));
        foreach (var leg in Legs) p.Add(Gait(t, leg.from, leg.to, Mathf.Abs(leg.x1 - leg.x0) / (leg.to - leg.from), leg.run));
        p.Add(Jump(t, JumpUp, JumpDown, JumpHeight));
        p.Add(Cheer(t)).Add(WaveHand(t)).Add(Squat(t)).Add(Shrug(t)).Add(Bow(t)).Add(Kick(t)).Add(Point(t));
        // An arm lifted out past ShoulderLiftFrom brings its clavicle up with it, taking that much off the
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
        // Arms up with the elbows out to the sides, forearms up (as in Cheer)
        foreach (var s in Sides) p.Rot(s + "UpperArm", RaisedForward * arms, 0f, Out(s) * 110f * arms).Rot(s + "LowerArm", 0f, 0f, Out(s) * 45f * arms);
        if (t < up) return Crouch(p, 32f * Smooth(start, up - 0.05f, t) * (1f - Smooth(up - 0.05f, up, t)));
        if (t < down)
        {
            p.hips.y += 4f * height * air * (1f - air);
            foreach (var s in Sides) p.Rot(s + "UpperLeg", -30f * Mathf.Sin(air * Mathf.PI), 0f, 0f).Rot(s + "LowerLeg", 50f * Mathf.Sin(air * Mathf.PI), 0f, 0f);
            return p;
        }
        return Crouch(p, 26f * Mathf.Sin(Mathf.Clamp01((t - down) / (settled - down)) * Mathf.PI));
    }

    // The foot is a block under the shin, so the ankle bends no further than AnkleMax; deeper crouches lift the heels
    // instead, the foot pivoting on its ball.
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
    // Y), so for an arm pointing up a forward lean is +X; -X, forward for a hanging arm, takes raised arms back behind
    // the head.
    const float RaisedForward = 18f;

    // Both arms up, elbows out to the sides and forearms up (the user: elbows point out), pumping, a little bounce
    static Pose Cheer(float t)
    {
        float w = Env(t, CheerFrom, WaveFrom, 0.25f), pump = Wave(t, 2.5f);
        var p = new Pose().Rot("Head", -10f * w, 0f, 0f);
        foreach (var s in Sides)
            p.Rot(s + "UpperArm", RaisedForward * w, 0f, Out(s) * (105f + 8f * pump) * w).Rot(s + "LowerArm", 0f, 0f, Out(s) * (45f + 10f * pump) * w);
        p.hips.y += 0.03f * Mathf.Abs(pump) * w;
        return p;
    }

    // The right arm out to the side, the elbow pointing out and the forearm up, swinging (the user drew it: elbows out,
    // not in)
    static Pose WaveHand(float t)
    {
        float w = Env(t, WaveFrom, SquatFrom, 0.25f);
        return new Pose().Rot("RightUpperArm", RaisedForward * w, 0f, Out("Right") * 100f * w)
            .Rot("RightLowerArm", 0f, 0f, Out("Right") * (62f + 20f * Wave(t, 2.4f)) * w)
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

    // ---------------------------------------------------------------- the face

    // LiveFace's fields, as keyed
    static readonly string[] FaceFields =
    {
        "eyeOpen", "eyeHappy", "brow", "browAngle", "browRaise", "smile", "mouthOpen", "mouthWidth", "teeth", "lookX", "lookY",
        "eyeWhite", "lids", "browThick", "squareEyes", "smirk", "blush", "gritted", "tears", "cheeks", "wobble", "pupilSmall",
        "sweat", "lidTilt", "squeeze", "creases", "mouthSquare", "bold", "gums", "tongueOut", "browAsym", "eyeAsym",
        "eyeRound", "eyeCross", "browArc", "mouthD", "lookSide",
    };
    // An expression: plain black eyes and nothing extra (Neutral), with these fields set
    static float[] F(params (string field, float value)[] set)
    {
        var face = new float[FaceFields.Length];
        face[0] = 1f; face[5] = 0.6f; face[7] = 1f; // eyes open, a smile, a mouth of the usual width
        foreach (var (field, value) in set) face[Array.IndexOf(FaceFields, field)] = value;
        return face;
    }
    // His resting face, Chill Face (the user's pick): sleepy lidded white eyes, an easy half smile
    static readonly float[] ChillFace = F(("brow", 0.6f), ("browRaise", -0.2f), ("smile", 0.7f), ("eyeWhite", 1f), ("lids", 0.55f), ("smirk", 0.35f));
    // The expressions, one after another (each ExpressionTime long from LookFrom), Chill Face between them
    static readonly (string name, float[] face)[] Faces =
    {
        ("curious", F(("brow", 0.8f), ("browAngle", -0.2f), ("browRaise", 0.5f), ("smile", 0.2f), ("mouthOpen", 0.05f), ("mouthWidth", 0.8f))),
        ("epic", F(("brow", 0.8f), ("browAngle", -0.3f), ("browRaise", 0.5f), ("smile", 1f), ("mouthOpen", 0.55f), ("mouthWidth", 1.55f), ("teeth", 1f),
            ("eyeWhite", 1f), ("browThick", 0.3f))),
        ("angry teeth", F(("eyeOpen", 0.85f), ("brow", 1f), ("browAngle", 1f), ("browRaise", -0.3f), ("smile", -0.2f), ("mouthOpen", 0.45f),
            ("mouthWidth", 1.25f), ("browThick", 0.5f), ("gritted", 1f))),
        ("surprised", F(("brow", 1f), ("browAngle", -0.4f), ("browRaise", 1f), ("smile", 0f), ("mouthOpen", 0.75f), ("mouthWidth", 0.55f))),
        ("full laugh", F(("eyeHappy", 1f), ("brow", 0.7f), ("browAngle", -0.4f), ("browRaise", 0.5f), ("smile", 1f), ("mouthOpen", 1f),
            ("mouthWidth", 1.45f), ("teeth", 1f), ("blush", 0.3f), ("tears", 1f))),
        ("friendly", F(("eyeHappy", 0.5f), ("brow", 0.5f), ("browAngle", -0.2f), ("browRaise", 0.3f), ("smile", 0.9f), ("mouthOpen", 0.15f),
            ("mouthWidth", 1.1f), ("teeth", 0.6f))),
        ("mouth full", F(("eyeOpen", 0.9f), ("eyeHappy", 0.3f), ("brow", 0.5f), ("browAngle", -0.1f), ("browRaise", 0.2f), ("smile", 0.1f),
            ("mouthWidth", 0.55f), ("blush", 0.4f), ("cheeks", 1f), ("wobble", 0.4f))),
        ("scared", F(("brow", 1f), ("browAngle", -0.9f), ("browRaise", 0.9f), ("smile", -0.6f), ("mouthOpen", 0.35f), ("mouthWidth", 0.9f),
            ("teeth", 0.6f), ("eyeWhite", 1f), ("wobble", 1f), ("pupilSmall", 1f), ("sweat", 1f))),
        ("man face", F(("brow", 1f), ("browAngle", -0.1f), ("smile", 0.15f), ("mouthWidth", 1.25f), ("eyeWhite", 1f), ("lids", 0.45f),
            ("browThick", 1f), ("smirk", 0.9f))),
        ("angry shout", F(("eyeOpen", 0.8f), ("brow", 1f), ("browAngle", 1f), ("browRaise", -0.3f), ("smile", -0.6f), ("mouthOpen", 0.8f),
            ("mouthWidth", 1.2f), ("teeth", 1f))),
        ("lol", F(("smile", 0.3f), ("mouthOpen", 1f), ("mouthWidth", 1.3f), ("eyeWhite", 1f), ("squareEyes", 1f), ("blush", 0.6f))),
        // The user's cartoon references (drawn our own way): heavy brush lines, creases, lids slanted with the mood
        ("scream", F(("eyeWhite", 1f), ("lids", 0.25f), ("lidTilt", 1f), ("pupilSmall", 0.35f), ("brow", 1f), ("browThick", 1f), ("browAngle", 1f),
            ("browRaise", -0.45f), ("creases", 1f), ("smile", -0.3f), ("mouthOpen", 1f), ("mouthWidth", 1.25f), ("mouthSquare", 1f),
            ("teeth", 0.7f), ("bold", 1f))),
        ("wail", F(("squeeze", 1f), ("brow", 1f), ("browThick", 0.6f), ("browAngle", -0.7f), ("browRaise", 0.3f), ("creases", 0.8f),
            ("smile", -0.7f), ("mouthOpen", 0.9f), ("mouthWidth", 1.15f), ("mouthSquare", 0.6f), ("teeth", 0.5f), ("tears", 1f), ("bold", 1f))),
        ("cringe", F(("eyeWhite", 1f), ("lids", 0.35f), ("lidTilt", -0.5f), ("brow", 1f), ("browThick", 0.6f), ("browAngle", -0.8f),
            ("browRaise", 0.5f), ("smile", 0.7f), ("mouthOpen", 0.45f), ("mouthWidth", 1.45f), ("gritted", 1f), ("gums", 1f), ("creases", 0.6f),
            ("bold", 1f))),
        ("disgust", F(("eyeWhite", 1f), ("lids", 0.45f), ("lidTilt", 0.5f), ("brow", 1f), ("browThick", 0.6f), ("browAngle", 0.6f),
            ("browRaise", -0.2f), ("smirk", -0.8f), ("smile", -0.4f), ("wobble", 0.7f), ("mouthOpen", 0.3f), ("mouthWidth", 1.05f),
            ("tongueOut", 1f), ("creases", 0.7f), ("bold", 1f))),
        ("fury", F(("eyeWhite", 1f), ("lids", 0.2f), ("lidTilt", 1f), ("pupilSmall", 0.55f), ("brow", 1f), ("browThick", 1f), ("browAngle", 1f),
            ("browRaise", -0.55f), ("smile", -0.4f), ("mouthOpen", 0.55f), ("mouthWidth", 1.35f), ("gritted", 1f), ("creases", 1f),
            ("bold", 1f), ("mouthSquare", 0.6f))),
        // The funny-cartoon set (the user's references): asymmetric brows and eyes, big teeth
        ("sly", F(("eyeWhite", 1f), ("lids", 0.4f), ("lidTilt", 0.4f), ("brow", 1f), ("browThick", 0.8f), ("browAsym", 0.7f), ("browAngle", 0.3f),
            ("smirk", 0.8f), ("smile", 0.8f), ("mouthOpen", 0.25f), ("mouthWidth", 1.2f), ("gritted", 1f), ("gums", 0.5f), ("bold", 0.8f))),
        ("tease", F(("eyeWhite", 1f), ("eyeAsym", 0.8f), ("lids", 0.2f), ("brow", 1f), ("browThick", 0.6f), ("browAsym", -0.6f), ("browRaise", 0.3f),
            ("smile", 0.7f), ("mouthOpen", 0.35f), ("mouthWidth", 1.1f), ("teeth", 0.6f), ("tongueOut", 1f), ("bold", 0.8f))),
        ("meh", F(("eyeWhite", 1f), ("lids", 0.75f), ("brow", 1f), ("browThick", 0.5f), ("browRaise", -0.3f), ("browAsym", 0.3f),
            ("smile", -0.15f), ("mouthWidth", 0.7f), ("smirk", -0.3f), ("bold", 0.6f))),
        // Cartoon faces like the user's latest references: big round eyes with shiny pupils, thin arched brows, D mouths
        ("awkward", F(("eyeWhite", 1f), ("eyeRound", 1f), ("lookSide", 0.7f), ("brow", 1f), ("browArc", 1f), ("browAngle", -0.7f), ("browRaise", 0.4f),
            ("smile", 0.9f), ("mouthOpen", 0.75f), ("mouthWidth", 1.4f), ("teeth", 1f), ("gums", 0.3f), ("smirk", -0.3f))),
        ("silly", F(("eyeWhite", 1f), ("eyeRound", 1f), ("eyeCross", 0.8f), ("brow", 1f), ("browArc", 1f), ("browRaise", 0.5f), ("browAsym", 0.4f),
            ("smile", 0.7f), ("mouthOpen", 0.15f), ("tongueOut", 1f), ("smirk", 0.4f))),
        ("big laugh", F(("eyeHappy", 1f), ("brow", 0.8f), ("browArc", 1f), ("browRaise", 0.4f), ("mouthD", 1f), ("mouthOpen", 1f), ("mouthWidth", 1.3f),
            ("teeth", 1f))),
        ("shocked", F(("eyeWhite", 1f), ("eyeRound", 1f), ("pupilSmall", 0.6f), ("brow", 1f), ("browArc", 1f), ("browRaise", 1f), ("mouthD", 0.6f),
            ("mouthOpen", 0.9f), ("mouthWidth", 0.75f), ("smile", -0.2f))),
        ("happy", F(("eyeWhite", 1f), ("eyeRound", 1f), ("brow", 1f), ("browArc", 1f), ("browRaise", 0.4f), ("mouthD", 1f),
            ("mouthOpen", 0.8f), ("mouthWidth", 1.2f), ("smile", 0.5f))),
        ("unimpressed", F(("eyeWhite", 1f), ("eyeRound", 1f), ("lids", 0.5f), ("lookSide", -0.6f), ("brow", 1f), ("browArc", 1f), ("browAngle", 0.2f),
            ("smile", -0.4f), ("mouthWidth", 0.8f), ("wobble", 0.4f))),
    };
    const float ExpressionTime = 1.1f;
    static float FaceFrom(string name) => LookFrom + ExpressionTime * Array.FindIndex(Faces, f => f.name == name);

    // The face at time t: the expressions blended in and out over a fifth of a second, blinks every few seconds, eyes
    // that follow the head and dart about, brows that twitch, a mouth that flaps while he talks and shakes as he laughs
    static float[] Face(float t)
    {
        var face = (float[])ChillFace.Clone();
        float total = 0f;
        var mix = new float[face.Length];
        for (int i = 0; i < Faces.Length; i++)
        {
            float from = LookFrom + i * ExpressionTime;
            float w = Env(t, from, from + ExpressionTime, 0.2f);
            if (w <= 0f) continue;
            total += w;
            for (int k = 0; k < mix.Length; k++) mix[k] += w * Faces[i].face[k];
        }
        if (total > 0f)
        {
            float keep = Mathf.Max(0f, 1f - total);
            for (int k = 0; k < face.Length; k++)
                if (k != 9 && k != 10) face[k] = ChillFace[k] * keep + mix[k] / Mathf.Max(total, 1f);
        }

        // Blinks: a quick close and a slower open, every 2-4 s (not while the eyes are shut or squeezed already)
        float blink = 0f;
        var random = new System.Random(7);
        for (float at = 1.3f; at < Duration; at += 2f + 2f * (float)random.NextDouble())
            blink = Mathf.Max(blink, BlinkShape(t - at));
        face[0] *= 1f - blink * Mathf.Clamp01(face[0] * 1.5f - 0.2f);

        // Eyes: ahead of the head when he looks around, small darts otherwise
        float look = 45f * Smooth(LookFrom - 0.1f, LookFrom + 0.2f, t) - 90f * Smooth(LookFrom + 0.55f, LookFrom + 0.95f, t)
            + 45f * Smooth(LookFrom + 1.3f, LookFrom + 1.65f, t);
        float dart = Mathf.Round(Mathf.PerlinNoise(t * 0.9f, 3.1f) * 4f) / 4f - 0.5f;
        face[9] = -0.85f * look / 45f + 0.5f * dart + face[Array.IndexOf(FaceFields, "lookSide")];
        face[10] = 0.3f * (Mathf.PerlinNoise(t * 0.7f, 8.3f) - 0.5f);
        // Brows twitch a little
        face[4] += 0.25f * (Mathf.PerlinNoise(t * 1.3f, 5.7f) - 0.5f);

        // Talking (friendly): syllables of different sizes, four or five a second
        float talk = Env(t, FaceFrom("friendly") + 0.15f, FaceFrom("friendly") + ExpressionTime - 0.15f, 0.1f);
        if (talk > 0f)
        {
            float syllable = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 4.6f)) * (0.4f + 0.6f * Mathf.PerlinNoise(t * 3f, 1.7f));
            face[6] = Mathf.Max(face[6], talk * 0.55f * syllable);
            face[7] += talk * 0.15f * syllable;
        }
        // Laughing, wailing, screaming: the open mouth shakes
        float shake = 0f;
        foreach (var name in new[] { "full laugh", "wail", "scream" })
            shake = Mathf.Max(shake, Env(t, FaceFrom(name), FaceFrom(name) + ExpressionTime, 0.2f));
        face[6] = Mathf.Clamp01(face[6] + 0.1f * shake * Mathf.Sin(t * Mathf.PI * 2f * 6f));
        return face;
    }

    // A blink dt seconds after it starts: shut in 0.06 s, open again over 0.1 s
    static float BlinkShape(float dt) => dt < 0f || dt > 0.16f ? 0f : dt < 0.06f ? dt / 0.06f : 1f - (dt - 0.06f) / 0.1f;

    // ---------------------------------------------------------------- build

    [MenuItem("Tools/Characters/Build Animation Test")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[MinifigTest] Exit Play Mode first: scene changes made in Play Mode are lost."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        RigUtility.EnsureFolder("Assets/Animations");
        RigUtility.EnsureFolder("Assets/Timelines");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefab);
        if (prefab == null) { Debug.LogError($"[MinifigTest] {ActorPrefab} missing: build it first (Tools/Characters)"); return; }
        // The arms hang along the body in Idle: the forearm's angle out from straight down at rest
        var hang = Find(prefab, "RightHand").position - Find(prefab, "RightLowerArm").position;
        armRestOut = Mathf.Atan2(Mathf.Abs(hang.x), -hang.y) * Mathf.Rad2Deg;
        thigh = Vector3.Distance(Find(prefab, "LeftUpperLeg").position, Find(prefab, "LeftLowerLeg").position);
        shin = Vector3.Distance(Find(prefab, "LeftLowerLeg").position, Find(prefab, "LeftFoot").position);

        AnimationClip clip;
        var stage = EditorSceneManager.NewPreviewScene();
        try { clip = BakeClip(prefab, stage); }
        finally { EditorSceneManager.ClosePreviewScene(stage); }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = CharacterLineupBuilder.BuildCamera(FaceTest ? 0.75f : 4.4f, FaceTest ? 0.9f : 2.6f);
        if (FaceTest)
        {
            // Level with his face, the same distance away (the lens is set for it)
            camera.transform.position = new Vector3(0f, FaceHeight, camera.transform.position.z);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, FaceHeight, 0f) - camera.transform.position);
        }
        CharacterLineupBuilder.BuildStudio();
        var hero = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        hero.name = "Bacon";
        hero.transform.SetPositionAndRotation(Vector3.zero, Facing);

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

        var director = new GameObject("Timeline_MinifigTest").AddComponent<PlayableDirector>();
        var heroTrack = timeline.CreateTrack<AnimationTrack>(null, "Bacon");
        heroTrack.trackOffset = TrackOffset.ApplySceneOffsets; // the clip keys the hips, not the root
        var heroClip = heroTrack.CreateClip(clip);
        heroClip.start = 0;
        heroClip.duration = Duration;
        director.SetGenericBinding(heroTrack, hero.GetComponent<Animator>());
        director.playableAsset = timeline;
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Hold;

        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[MinifigTest] Built {ScenePath}, {Duration} s");
    }

    // The act posed on a copy of the prefab frame by frame and keyed straight onto the bones (a generic clip: each
    // bone's local rotation, the hips' local position). Not as humanoid muscles: Unity's muscle space moved a hand turned
    // about the forearm into the forearm, which twisted at the elbow, and swung raised upper arms about themselves.
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
        var face = go.GetComponentInChildren<LiveFace>(true);
        string facePath = face != null ? AnimationUtility.CalculateTransformPath(face.transform, go.transform) : null;

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
        void Key(string path, string property, Func<int, float> value, Type type = null)
        {
            var keys = new Keyframe[frames + 1];
            for (int f = 0; f <= frames; f++)
            {
                int a = Mathf.Max(f - 1, 0), b = Mathf.Min(f + 1, frames);
                float slope = (value(b) - value(a)) * Fps / (b - a);
                keys[f] = new Keyframe(f / Fps, value(f), slope, slope);
            }
            bindings.Add(EditorCurveBinding.FloatCurve(path, type ?? typeof(Transform), property));
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
        if (facePath != null)
        {
            var faces = new float[frames + 1][];
            for (int f = 0; f <= frames; f++) faces[f] = Face(f / Fps);
            for (int k = 0; k < FaceFields.Length; k++)
            {
                int field = k;
                if (FaceFields[k] == "lookSide") continue; // only steers lookX
                Key(facePath, FaceFields[k], f => faces[f][field], typeof(LiveFace));
            }
        }
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
