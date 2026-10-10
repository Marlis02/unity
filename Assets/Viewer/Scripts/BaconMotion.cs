using System;
using UnityEngine;

namespace Viewer
{
    // Bacon's moves and faces played live on his bones, for the phone viewer: the poses and expressions of the animation
    // test (Assets/Editor/MinifigAnimationTestBuilder.cs, the same numbers), each move looping on the spot, so the viewer
    // can switch, slow down and pause them. If a pose changes there, change it here as well.
    // Bones in the rest pose are unrotated: a pose is Euler angles added per bone (arm out = Z with Out(side), arm
    // forward = -X, knee back = +X, spine/head forward = +X, turn right = +Y) and an offset of the hips.
    // Runs before LiveFace (LateUpdate), which hands the face's numbers to the shader.
    [DefaultExecutionOrder(-50)]
    public class BaconMotion : MonoBehaviour
    {
        public enum Move { Idle, LookAround, Walk, Run, Jump, Cheer, Wave, Squat, Shrug, Bow, Kick, Point }

        public Move move = Move.Idle;
        public int face;              // an index into Faces
        public float speed = 1f;      // 1 = as in the shorts
        public bool paused;

        // ---------------------------------------------------------------- bones and calibration

        static readonly string[] Bones =
        {
            "Hips", "Spine", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
        };
        static readonly string[] Sides = { "Left", "Right" };
        static float Out(string side) => side == "Left" ? -1f : 1f; // sign of Z that swings that side's arm out

        Transform[] bones;
        Vector3 hipsRest;
        float armRestOut, thigh, shin;
        LiveFace liveFace;

        void Awake()
        {
            bones = Array.ConvertAll(Bones, Find);
            hipsRest = bones[0].localPosition;
            // The arms hang along the body in Idle: the forearm's angle out from straight down at rest
            var hang = transform.InverseTransformVector(Find("RightHand").position - Find("RightLowerArm").position);
            armRestOut = Mathf.Atan2(Mathf.Abs(hang.x), -hang.y) * Mathf.Rad2Deg;
            thigh = transform.InverseTransformVector(Find("LeftUpperLeg").position - Find("LeftLowerLeg").position).magnitude;
            shin = transform.InverseTransformVector(Find("LeftLowerLeg").position - Find("LeftFoot").position).magnitude;
            liveFace = GetComponentInChildren<LiveFace>(true);
            faceNow = (float[])Faces[0].values.Clone();
            faceFrom = (float[])faceNow.Clone();
        }

        Transform Find(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            throw new ArgumentException($"No {name} under {gameObject.name}");
        }

        // ---------------------------------------------------------------- clocks

        const float CrossFade = 0.3f; // seconds from one move into the next
        Move previous;
        float moveTime, previousTime, fade = 1f, clock;

        /// <summary>Switches to a move, blending out of the current one; picking the playing move starts it over.</summary>
        public void Play(Move next)
        {
            previous = move;
            previousTime = moveTime;
            move = next;
            moveTime = 0f;
            fade = 0f;
        }

        void LateUpdate()
        {
            float dt = paused ? 0f : Time.deltaTime * speed;
            clock += dt;
            moveTime += dt;
            previousTime += dt;
            fade = Mathf.Min(1f, fade + dt / CrossFade);

            var pose = Act(move, moveTime);
            if (fade < 1f)
            {
                float w = Mathf.SmoothStep(0f, 1f, fade);
                pose = Act(previous, previousTime).Scale(1f - w).Add(pose, w);
            }
            ShoulderLift(pose);
            for (int b = 0; b < bones.Length; b++) bones[b].localRotation = Quaternion.Euler(pose.e[b]);
            bones[0].localPosition = hipsRest + pose.hips;

            UpdateFace(dt);
        }

        // ---------------------------------------------------------------- the moves

        class BaconPose
        {
            public readonly Vector3[] e = new Vector3[Bones.Length];
            public Vector3 hips;

            public BaconPose Rot(string bone, float x, float y, float z)
            {
                e[Array.IndexOf(Bones, bone)] += new Vector3(x, y, z);
                return this;
            }

            public BaconPose Add(BaconPose other, float weight = 1f)
            {
                for (int i = 0; i < e.Length; i++) e[i] += other.e[i] * weight;
                hips += other.hips * weight;
                return this;
            }

            public BaconPose Scale(float weight)
            {
                for (int i = 0; i < e.Length; i++) e[i] *= weight;
                hips *= weight;
                return this;
            }
        }

        // How long each move takes before it starts over, seconds
        static float Loop(Move m)
        {
            switch (m)
            {
                case Move.LookAround: return 3f;
                case Move.Jump: return 1.9f;
                case Move.Squat: return 2.8f;
                case Move.Shrug: return 2.2f;
                case Move.Bow: return 2.6f;
                case Move.Kick: return 2.1f;
                case Move.Point: return 2.4f;
                default: return 0f; // goes on and on
            }
        }

        BaconPose Act(Move m, float time)
        {
            float loop = Loop(m);
            float t = loop > 0f ? Mathf.Repeat(time, loop) : time;
            var p = Idle(clock); // breathing goes on through every move and every switch
            switch (m)
            {
                case Move.LookAround: p.Add(LookAround(t)); break;
                case Move.Walk: p.Add(Gait(time, false)); break;
                case Move.Run: p.Add(Gait(time, true)); break;
                case Move.Jump: p.Add(Jump(t, 0.45f, 1f, 0.5f)); break;
                case Move.Cheer: p.Add(Cheer(time)); break;
                case Move.Wave: p.Add(WaveHand(time)); break;
                case Move.Squat: p.Add(Squat(t)); break;
                case Move.Shrug: p.Add(Shrug(t)); break;
                case Move.Bow: p.Add(Bow(t)); break;
                case Move.Kick: p.Add(Kick(t)); break;
                case Move.Point: p.Add(Point(t)); break;
            }
            return p;
        }

        // An arm lifted out past ShoulderLiftFrom brings its clavicle up with it, taking that much off the arm itself so it
        // still points the same way, and a raised arm comes up out of the shoulder
        const float ShoulderLiftFrom = 70f, ShoulderLiftRate = 0.4f, ShoulderLiftMax = 28f; // degrees

        static void ShoulderLift(BaconPose p)
        {
            foreach (var s in Sides)
            {
                int arm = Array.IndexOf(Bones, s + "UpperArm");
                float lift = Mathf.Clamp((Out(s) * p.e[arm].z - ShoulderLiftFrom) * ShoulderLiftRate, 0f, ShoulderLiftMax);
                p.Rot(s + "Shoulder", 0f, 0f, Out(s) * lift);
                p.e[arm].z -= Out(s) * lift;
            }
        }

        // Standing easy, arms hanging along the body
        BaconPose Idle(float t)
        {
            float breathe = Wave(t, 0.4f);
            var p = new BaconPose().Rot("Spine", 1.5f * breathe, 0f, 0f).Rot("Head", -1.5f * breathe, 0f, 0f);
            foreach (var s in Sides)
                p.Rot(s + "UpperArm", 0f, 0f, Out(s) * (3f - armRestOut + 1f * breathe)).Rot(s + "LowerArm", -2f, 0f, 0f);
            return p;
        }

        // A look to his right, then his left, then back at us
        static float Look(float t) =>
            45f * Smooth(0.3f, 0.65f, t) - 90f * Smooth(0.95f, 1.4f, t) + 45f * Smooth(1.7f, 2.05f, t);

        static BaconPose LookAround(float t)
        {
            float look = Look(t);
            return new BaconPose().Rot("Head", 0f, look, 0f).Rot("Spine", 0f, 0.3f * look, 0f);
        }

        // Walking or running on the spot at the shorts' paces (walk 0.81 m/s, run 2 m/s): the thighs swing (forward = -X),
        // the knee bends as the leg swings through, the opposite arm swings with it, the hips drop as the legs part
        BaconPose Gait(float t, bool run)
        {
            var p = new BaconPose();
            float speed = run ? 2f : 0.81f;
            float amp = run ? 38f : 24f, knee = run ? 65f : 35f, arm = run ? 45f : 18f, elbow = run ? 75f : 3f;
            float cycle = 4f * (thigh + shin) * Mathf.Sin(amp * Mathf.Deg2Rad) * (run ? 1.35f : 1f); // ground per cycle
            float phase = t * speed / cycle;
            float drop = 0f;
            for (int k = 0; k < 2; k++)
            {
                string s = Sides[k];
                float c = (phase + 0.5f * k) * 2f * Mathf.PI;
                float swing = -amp * Mathf.Sin(c);
                float bend = knee * Mathf.Max(0f, Mathf.Cos(c));
                float ankle = Mathf.Clamp(-0.4f * (swing + bend), -AnkleMax, AnkleMax);
                p.Rot(s + "UpperLeg", swing, 0f, 0f).Rot(s + "LowerLeg", bend, 0f, 0f).Rot(s + "Foot", ankle, 0f, 0f);
                p.Rot(s + "UpperArm", -swing * arm / amp, 0f, 0f).Rot(s + "LowerArm", -elbow, 0f, 0f);
                drop = (thigh + shin) * (1f - Mathf.Cos(swing * Mathf.Deg2Rad));
            }
            p.hips.y -= (run ? 0.6f : 1f) * drop;
            return p.Rot("Spine", run ? 12f : 3f, 0f, 0f);
        }

        // Crouch, spring up with the arms thrown up in a V, land and soak it up
        BaconPose Jump(float t, float up, float down, float height)
        {
            var p = new BaconPose();
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
        BaconPose Crouch(BaconPose p, float a)
        {
            float flat = Mathf.Min(a, AnkleMax), tiptoe = a - flat;
            foreach (var s in Sides) p.Rot(s + "UpperLeg", -a, 0f, 0f).Rot(s + "LowerLeg", 2f * a, 0f, 0f).Rot(s + "Foot", -flat, 0f, 0f);
            float r = a * Mathf.Deg2Rad;
            p.hips += new Vector3(0f, -(thigh + shin) * (1f - Mathf.Cos(r)) + AnkleToBall * Mathf.Sin(tiptoe * Mathf.Deg2Rad), -(thigh - shin) * Mathf.Sin(r));
            return p.Rot("Spine", 0.4f * a, 0f, 0f);
        }

        // A raised arm's lean forward (Unity's Euler order is Z, X, Y, so for an arm raised to the side +X leans it forward)
        const float RaisedForward = 18f;

        // Both arms up, elbows out to the sides and forearms up, pumping, a little bounce
        static BaconPose Cheer(float t)
        {
            float pump = Wave(t, 2.5f);
            var p = new BaconPose().Rot("Head", -10f, 0f, 0f);
            foreach (var s in Sides)
                p.Rot(s + "UpperArm", RaisedForward, 0f, Out(s) * (105f + 8f * pump)).Rot(s + "LowerArm", 0f, 0f, Out(s) * (45f + 10f * pump));
            p.hips.y += 0.03f * Mathf.Abs(pump);
            return p;
        }

        // The right arm out to the side, the elbow pointing out and the forearm up, swinging
        static BaconPose WaveHand(float t)
        {
            return new BaconPose().Rot("RightUpperArm", RaisedForward, 0f, Out("Right") * 100f)
                .Rot("RightLowerArm", 0f, 0f, Out("Right") * (62f + 20f * Wave(t, 2.4f)))
                .Rot("RightHand", 0f, PalmForward("Right"), 0f).Rot("Head", 0f, 0f, 6f);
        }

        // Down into a deep squat, arms forward for balance, a moment down there, and up
        BaconPose Squat(float t)
        {
            float w = Env(t, 0f, 2.2f, 0.45f);
            var p = Crouch(new BaconPose(), 60f * w);
            foreach (var s in Sides) p.Rot(s + "UpperArm", -65f * w, 0f, 0f);
            return p;
        }

        // At rest the palms face the thighs; this turn about the forearm turns them to face the forearm's front
        static float PalmForward(string side) => Out(side) * 90f;

        // Shoulders up, forearms out, palms up, head tilted
        static BaconPose Shrug(float t)
        {
            float w = Env(t, 0f, 1.6f, 0.25f);
            var p = new BaconPose().Rot("Head", 0f, 0f, 10f * w);
            foreach (var s in Sides)
                p.Rot(s + "UpperArm", -10f * w, 0f, Out(s) * 28f * w).Rot(s + "LowerArm", -70f * w, 0f, 0f).Rot(s + "Hand", 0f, PalmForward(s) * w, 0f);
            return p;
        }

        // A bow from the hips and waist, legs kept upright, arms left hanging
        static BaconPose Bow(float t)
        {
            float w = Env(t, 0f, 2f, 0.35f);
            var p = new BaconPose().Rot("Hips", 20f * w, 0f, 0f).Rot("Spine", 25f * w, 0f, 0f).Rot("Head", 10f * w, 0f, 0f);
            foreach (var s in Sides) p.Rot(s + "UpperLeg", -20f * w, 0f, 0f).Rot(s + "UpperArm", -45f * w, 0f, 0f);
            return p;
        }

        // Right knee up, the leg snaps out straight and comes down; arms out for balance, leaning back
        static BaconPose Kick(float t)
        {
            float lift = Smooth(0.1f, 0.45f, t) * (1f - Smooth(1.05f, 1.45f, t));
            float snap = Smooth(0.5f, 0.65f, t) * (1f - Smooth(0.9f, 1.2f, t));
            var p = new BaconPose().Rot("RightUpperLeg", -(55f + 30f * snap) * lift, 0f, 0f).Rot("RightLowerLeg", 85f * (1f - snap) * lift, 0f, 0f)
                .Rot("RightFoot", -20f * lift, 0f, 0f).Rot("Spine", -12f * lift, 0f, 0f);
            foreach (var s in Sides) p.Rot(s + "UpperArm", 0f, 0f, Out(s) * 35f * lift);
            return p;
        }

        // The right arm straight out at the camera
        static BaconPose Point(float t)
        {
            float w = Env(t, 0f, 1.8f, 0.25f);
            return new BaconPose().Rot("RightUpperArm", -85f * w, 0f, Out("Right") * 8f * w).Rot("RightLowerArm", 8f * w, 0f, 0f)
                .Rot("Spine", 0f, -10f * w, 0f).Rot("Head", 4f * w, 0f, 0f);
        }

        static float Wave(float t, float hz, float phase = 0f) => Mathf.Sin((t * hz + phase) * 2f * Mathf.PI);
        static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
        static float Env(float t, float start, float end, float fade) => Smooth(start, start + fade, t) * (1f - Smooth(end - fade, end, t));

        // ---------------------------------------------------------------- the face

        // LiveFace's fields in this order; lookSide only steers lookX
        static readonly string[] FaceFields =
        {
            "eyeOpen", "eyeHappy", "brow", "browAngle", "browRaise", "smile", "mouthOpen", "mouthWidth", "teeth", "lookX", "lookY",
            "eyeWhite", "lids", "browThick", "squareEyes", "smirk", "blush", "gritted", "tears", "cheeks", "wobble", "pupilSmall",
            "sweat", "lidTilt", "squeeze", "creases", "mouthSquare", "bold", "gums", "tongueOut", "browAsym", "eyeAsym",
            "eyeRound", "eyeCross", "browArc", "mouthD", "lookSide",
        };
        const int EyeOpen = 0, BrowRaise = 4, MouthOpen = 6, MouthWidth = 7, LookX = 9, LookY = 10, LookSide = 36;

        // An expression: plain black eyes and nothing extra (Neutral), with these fields set
        static float[] F(params (string field, float value)[] set)
        {
            var face = new float[FaceFields.Length];
            face[0] = 1f; face[5] = 0.6f; face[7] = 1f; // eyes open, a smile, a mouth of the usual width
            foreach (var (field, value) in set) face[Array.IndexOf(FaceFields, field)] = value;
            return face;
        }

        public struct Expression
        {
            public string name, title; // as in the animation test; for the menu
            public float[] values;
        }

        static Expression E(string name, string title, float[] values) => new Expression { name = name, title = title, values = values };

        // His resting face first (Chill Face, the user's pick), then the animation test's faces in its order
        public static readonly Expression[] Faces =
        {
            E("chill", "Чилл", F(("brow", 0.6f), ("browRaise", -0.2f), ("smile", 0.7f), ("eyeWhite", 1f), ("lids", 0.55f), ("smirk", 0.35f))),
            E("curious", "Любопытство", F(("brow", 0.8f), ("browAngle", -0.2f), ("browRaise", 0.5f), ("smile", 0.2f), ("mouthOpen", 0.05f), ("mouthWidth", 0.8f))),
            E("epic", "Эпик", F(("brow", 0.8f), ("browAngle", -0.3f), ("browRaise", 0.5f), ("smile", 1f), ("mouthOpen", 0.55f), ("mouthWidth", 1.55f), ("teeth", 1f),
                ("eyeWhite", 1f), ("browThick", 0.3f))),
            E("angry teeth", "Злость", F(("eyeOpen", 0.85f), ("brow", 1f), ("browAngle", 1f), ("browRaise", -0.3f), ("smile", -0.2f), ("mouthOpen", 0.45f),
                ("mouthWidth", 1.25f), ("browThick", 0.5f), ("gritted", 1f))),
            E("surprised", "Удивление", F(("brow", 1f), ("browAngle", -0.4f), ("browRaise", 1f), ("smile", 0f), ("mouthOpen", 0.75f), ("mouthWidth", 0.55f))),
            E("full laugh", "Смех до слёз", F(("eyeHappy", 1f), ("brow", 0.7f), ("browAngle", -0.4f), ("browRaise", 0.5f), ("smile", 1f), ("mouthOpen", 1f),
                ("mouthWidth", 1.45f), ("teeth", 1f), ("blush", 0.3f), ("tears", 1f))),
            E("friendly", "Говорит", F(("eyeHappy", 0.5f), ("brow", 0.5f), ("browAngle", -0.2f), ("browRaise", 0.3f), ("smile", 0.9f), ("mouthOpen", 0.15f),
                ("mouthWidth", 1.1f), ("teeth", 0.6f))),
            E("mouth full", "Рот полный", F(("eyeOpen", 0.9f), ("eyeHappy", 0.3f), ("brow", 0.5f), ("browAngle", -0.1f), ("browRaise", 0.2f), ("smile", 0.1f),
                ("mouthWidth", 0.55f), ("blush", 0.4f), ("cheeks", 1f), ("wobble", 0.4f))),
            E("scared", "Страх", F(("brow", 1f), ("browAngle", -0.9f), ("browRaise", 0.9f), ("smile", -0.6f), ("mouthOpen", 0.35f), ("mouthWidth", 0.9f),
                ("teeth", 0.6f), ("eyeWhite", 1f), ("wobble", 1f), ("pupilSmall", 1f), ("sweat", 1f))),
            E("man face", "Man Face", F(("brow", 1f), ("browAngle", -0.1f), ("smile", 0.15f), ("mouthWidth", 1.25f), ("eyeWhite", 1f), ("lids", 0.45f),
                ("browThick", 1f), ("smirk", 0.9f))),
            E("angry shout", "Кричит злой", F(("eyeOpen", 0.8f), ("brow", 1f), ("browAngle", 1f), ("browRaise", -0.3f), ("smile", -0.6f), ("mouthOpen", 0.8f),
                ("mouthWidth", 1.2f), ("teeth", 1f))),
            E("lol", "LOL", F(("smile", 0.3f), ("mouthOpen", 1f), ("mouthWidth", 1.3f), ("eyeWhite", 1f), ("squareEyes", 1f), ("blush", 0.6f))),
            E("scream", "Вопль", F(("eyeWhite", 1f), ("lids", 0.25f), ("lidTilt", 1f), ("pupilSmall", 0.35f), ("brow", 1f), ("browThick", 1f), ("browAngle", 1f),
                ("browRaise", -0.45f), ("creases", 1f), ("smile", -0.3f), ("mouthOpen", 1f), ("mouthWidth", 1.25f), ("mouthSquare", 1f),
                ("teeth", 0.7f), ("bold", 1f))),
            E("wail", "Рыдает", F(("squeeze", 1f), ("brow", 1f), ("browThick", 0.6f), ("browAngle", -0.7f), ("browRaise", 0.3f), ("creases", 0.8f),
                ("smile", -0.7f), ("mouthOpen", 0.9f), ("mouthWidth", 1.15f), ("mouthSquare", 0.6f), ("teeth", 0.5f), ("tears", 1f), ("bold", 1f))),
            E("cringe", "Кринж", F(("eyeWhite", 1f), ("lids", 0.35f), ("lidTilt", -0.5f), ("brow", 1f), ("browThick", 0.6f), ("browAngle", -0.8f),
                ("browRaise", 0.5f), ("smile", 0.7f), ("mouthOpen", 0.45f), ("mouthWidth", 1.45f), ("gritted", 1f), ("gums", 1f), ("creases", 0.6f),
                ("bold", 1f))),
            E("disgust", "Фу", F(("eyeWhite", 1f), ("lids", 0.45f), ("lidTilt", 0.5f), ("brow", 1f), ("browThick", 0.6f), ("browAngle", 0.6f),
                ("browRaise", -0.2f), ("smirk", -0.8f), ("smile", -0.4f), ("wobble", 0.7f), ("mouthOpen", 0.3f), ("mouthWidth", 1.05f),
                ("tongueOut", 1f), ("creases", 0.7f), ("bold", 1f))),
            E("fury", "Ярость", F(("eyeWhite", 1f), ("lids", 0.2f), ("lidTilt", 1f), ("pupilSmall", 0.55f), ("brow", 1f), ("browThick", 1f), ("browAngle", 1f),
                ("browRaise", -0.55f), ("smile", -0.4f), ("mouthOpen", 0.55f), ("mouthWidth", 1.35f), ("gritted", 1f), ("creases", 1f),
                ("bold", 1f), ("mouthSquare", 0.6f))),
            E("sly", "Хитрый", F(("eyeWhite", 1f), ("lids", 0.4f), ("lidTilt", 0.4f), ("brow", 1f), ("browThick", 0.8f), ("browAsym", 0.7f), ("browAngle", 0.3f),
                ("smirk", 0.8f), ("smile", 0.8f), ("mouthOpen", 0.25f), ("mouthWidth", 1.2f), ("gritted", 1f), ("gums", 0.5f), ("bold", 0.8f))),
            E("tease", "Дразнит", F(("eyeWhite", 1f), ("eyeAsym", 0.8f), ("lids", 0.2f), ("brow", 1f), ("browThick", 0.6f), ("browAsym", -0.6f), ("browRaise", 0.3f),
                ("smile", 0.7f), ("mouthOpen", 0.35f), ("mouthWidth", 1.1f), ("teeth", 0.6f), ("tongueOut", 1f), ("bold", 0.8f))),
            E("meh", "Мэх", F(("eyeWhite", 1f), ("lids", 0.75f), ("brow", 1f), ("browThick", 0.5f), ("browRaise", -0.3f), ("browAsym", 0.3f),
                ("smile", -0.15f), ("mouthWidth", 0.7f), ("smirk", -0.3f), ("bold", 0.6f))),
            E("awkward", "Неловко", F(("eyeWhite", 1f), ("eyeRound", 1f), ("lookSide", 0.7f), ("brow", 1f), ("browArc", 1f), ("browAngle", -0.7f), ("browRaise", 0.4f),
                ("smile", 0.9f), ("mouthOpen", 0.75f), ("mouthWidth", 1.4f), ("teeth", 1f), ("gums", 0.3f), ("smirk", -0.3f))),
            E("silly", "Дурачится", F(("eyeWhite", 1f), ("eyeRound", 1f), ("eyeCross", 0.8f), ("brow", 1f), ("browArc", 1f), ("browRaise", 0.5f), ("browAsym", 0.4f),
                ("smile", 0.7f), ("mouthOpen", 0.15f), ("tongueOut", 1f), ("smirk", 0.4f))),
            E("big laugh", "Хохот", F(("eyeHappy", 1f), ("brow", 0.8f), ("browArc", 1f), ("browRaise", 0.4f), ("mouthD", 1f), ("mouthOpen", 1f), ("mouthWidth", 1.3f),
                ("teeth", 1f))),
            E("shocked", "Шок", F(("eyeWhite", 1f), ("eyeRound", 1f), ("pupilSmall", 0.6f), ("brow", 1f), ("browArc", 1f), ("browRaise", 1f), ("mouthD", 0.6f),
                ("mouthOpen", 0.9f), ("mouthWidth", 0.75f), ("smile", -0.2f))),
            E("happy", "Радость", F(("eyeWhite", 1f), ("eyeRound", 1f), ("brow", 1f), ("browArc", 1f), ("browRaise", 0.4f), ("mouthD", 1f),
                ("mouthOpen", 0.8f), ("mouthWidth", 1.2f), ("smile", 0.5f))),
            E("unimpressed", "Не впечатлён", F(("eyeWhite", 1f), ("eyeRound", 1f), ("lids", 0.5f), ("lookSide", -0.6f), ("brow", 1f), ("browArc", 1f), ("browAngle", 0.2f),
                ("smile", -0.4f), ("mouthWidth", 0.8f), ("wobble", 0.4f))),
        };

        const float FaceBlend = 0.2f; // seconds from one expression into the next
        float[] faceNow, faceFrom;
        int faceShown;
        float faceTime = 1f, nextBlink = 1.3f, blinkAt = -10f;
        readonly System.Random random = new System.Random(7);

        // The expression blended in over a fifth of a second, blinks every few seconds, eyes that follow the head and dart
        // about, brows that twitch, a mouth that flaps while he talks and shakes as he laughs (as Face(t) in the test)
        void UpdateFace(float dt)
        {
            if (liveFace == null) return;
            face = (face % Faces.Length + Faces.Length) % Faces.Length;
            if (face != faceShown)
            {
                faceFrom = (float[])faceNow.Clone();
                faceShown = face;
                faceTime = 0f;
            }
            faceTime += dt;
            float w = Mathf.SmoothStep(0f, 1f, faceTime / FaceBlend);
            var target = Faces[face].values;
            for (int k = 0; k < faceNow.Length; k++) faceNow[k] = Mathf.Lerp(faceFrom[k], target[k], w);
            var v = (float[])faceNow.Clone();

            // Blinks: a quick close and a slower open, every 2-4 s (not while the eyes are shut or squeezed already)
            if (clock >= nextBlink)
            {
                blinkAt = nextBlink;
                nextBlink += 2f + 2f * (float)random.NextDouble();
            }
            float blink = BlinkShape(clock - blinkAt);
            v[EyeOpen] *= 1f - blink * Mathf.Clamp01(v[EyeOpen] * 1.5f - 0.2f);

            // Eyes: ahead of the head when he looks around, small darts otherwise
            float look = move == Move.LookAround ? Look(Mathf.Repeat(moveTime + 0.1f, Loop(Move.LookAround))) * fade : 0f;
            float dart = Mathf.Round(Mathf.PerlinNoise(clock * 0.9f, 3.1f) * 4f) / 4f - 0.5f;
            v[LookX] = -0.85f * look / 45f + 0.5f * dart + v[LookSide];
            v[LookY] = 0.3f * (Mathf.PerlinNoise(clock * 0.7f, 8.3f) - 0.5f);
            v[BrowRaise] += 0.25f * (Mathf.PerlinNoise(clock * 1.3f, 5.7f) - 0.5f);

            string name = Faces[face].name;
            if (name == "friendly")
            {
                // Talking: syllables of different sizes, four or five a second
                float syllable = Mathf.Abs(Mathf.Sin(clock * Mathf.PI * 4.6f)) * (0.4f + 0.6f * Mathf.PerlinNoise(clock * 3f, 1.7f));
                v[MouthOpen] = Mathf.Max(v[MouthOpen], w * 0.55f * syllable);
                v[MouthWidth] += w * 0.15f * syllable;
            }
            if (name == "full laugh" || name == "wail" || name == "scream")
                v[MouthOpen] = Mathf.Clamp01(v[MouthOpen] + 0.1f * w * Mathf.Sin(clock * Mathf.PI * 2f * 6f)); // the open mouth shakes

            Set(liveFace, v);
            liveFace.Apply();
        }

        // A blink dt seconds after it starts: shut in 0.06 s, open again over 0.1 s
        static float BlinkShape(float dt) => dt < 0f || dt > 0.16f ? 0f : dt < 0.06f ? dt / 0.06f : 1f - (dt - 0.06f) / 0.1f;

        static void Set(LiveFace f, float[] v)
        {
            f.eyeOpen = v[0]; f.eyeHappy = v[1]; f.brow = v[2]; f.browAngle = v[3]; f.browRaise = v[4]; f.smile = v[5];
            f.mouthOpen = v[6]; f.mouthWidth = v[7]; f.teeth = v[8]; f.lookX = v[9]; f.lookY = v[10]; f.eyeWhite = v[11];
            f.lids = v[12]; f.browThick = v[13]; f.squareEyes = v[14]; f.smirk = v[15]; f.blush = v[16]; f.gritted = v[17];
            f.tears = v[18]; f.cheeks = v[19]; f.wobble = v[20]; f.pupilSmall = v[21]; f.sweat = v[22]; f.lidTilt = v[23];
            f.squeeze = v[24]; f.creases = v[25]; f.mouthSquare = v[26]; f.bold = v[27]; f.gums = v[28]; f.tongueOut = v[29];
            f.browAsym = v[30]; f.eyeAsym = v[31]; f.eyeRound = v[32]; f.eyeCross = v[33]; f.browArc = v[34]; f.mouthD = v[35];
        }
    }
}
