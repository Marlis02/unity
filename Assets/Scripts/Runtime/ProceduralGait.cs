using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Swings limb pivots so movement reads clearly without any animation clips. Used by
    /// imported models assembled from separate body parts (see "segments" in character.json).
    /// Pivots are expected to face the character's +Z with limbs hanging down; the optional
    /// joints add knees, elbows, a waist and a neck. Angles stay within what the rounded joints
    /// of such models can show without one part leaving its socket.
    /// </summary>
    public class ProceduralGait : MonoBehaviour
    {
        public PlayableCharacter character;
        public Transform body;
        public Transform leftArm;
        public Transform rightArm;
        public Transform leftLeg;
        public Transform rightLeg;
        public float strideLength = 1.2f;

        [Header("Optional joints")]
        public Transform chest;
        public Transform head;
        public Transform leftForearm;
        public Transform rightForearm;
        public Transform leftShin;
        public Transform rightShin;

        float phase;
        float bodyBaseY;
        float thighLength;
        float shinLength;
        float squat; // 0 standing, 1 fully down

        void Start()
        {
            if (body != null) bodyBaseY = body.localPosition.y;
            // Leg lengths from the rest pose, to lower the body by the right amount in a squat.
            Transform hip = leftLeg != null ? leftLeg : rightLeg;
            Transform knee = leftShin != null ? leftShin : rightShin;
            if (hip != null && knee != null)
            {
                thighLength = Mathf.Max(0f, hip.position.y - knee.position.y);
                shinLength = Mathf.Max(0f, knee.position.y - transform.position.y);
            }
        }

        void LateUpdate()
        {
            if (character == null) return;
            float dt = Time.deltaTime;
            float speed = character.PlanarSpeed;
            float move = Mathf.Clamp01(speed / Mathf.Max(0.1f, character.profile.walkSpeed)); // 0 standing, 1 walking or faster
            float run = Mathf.Clamp01(character.NormalizedGait - 1f);                       // 0 at walking pace, 1 at running pace
            bool grounded = character.Grounded;
            CharacterMotion motion = character.motion;

            phase += speed / Mathf.Max(0.1f, strideLength) * Mathf.PI * dt;
            float sin = Mathf.Sin(phase);
            float cos = Mathf.Cos(phase);

            // Joint angles in degrees. Pitch is negative towards the front, spread is outwards.
            float hipSwing = move * Mathf.Lerp(28f, 42f, run);
            float kneeBend = move * Mathf.Lerp(40f, 75f, run);
            float armSwing = move * Mathf.Lerp(30f, 48f, run);
            float elbowBend = Mathf.Lerp(Mathf.Lerp(6f, 14f, move), 85f, run);
            float breath = Mathf.Sin(Time.time * 2.2f) * 1.5f * (1f - move);

            float leftLegPitch = -hipSwing * sin;
            float rightLegPitch = hipSwing * sin;
            // A knee bends while its leg swings forward and straightens before the foot lands.
            float leftKnee = kneeBend * Mathf.Max(0f, cos) + 3f * move;
            float rightKnee = kneeBend * Mathf.Max(0f, -cos) + 3f * move;
            float leftArmPitch = armSwing * sin;
            float rightArmPitch = -armSwing * sin;
            float armSpread = 0f;
            float lean = move * Mathf.Lerp(2f, 7f, run) + breath;
            float twist = -sin * armSwing * 0.2f; // the shoulders follow the forward arm
            float squatTarget = 0f;

            if (!grounded)
            {
                // Arms up, legs tucked a little while airborne.
                leftArmPitch = rightArmPitch = -135f;
                armSpread = 12f;
                elbowBend = 20f;
                leftLegPitch = -12f;
                rightLegPitch = -20f;
                leftKnee = 35f;
                rightKnee = 50f;
                lean = -4f;
                twist = 0f;
            }
            else if (motion == CharacterMotion.ArmsOut)
            {
                armSpread = 85f;
                elbowBend = 5f;
            }
            else if (motion == CharacterMotion.ArmsForward)
            {
                leftArmPitch = rightArmPitch = -90f;
                armSpread = 5f;
                elbowBend = 10f;
            }
            else if (motion == CharacterMotion.Squat)
            {
                const float hip = 65f, knee = 70f;
                leftLegPitch = rightLegPitch = -hip;
                leftKnee = rightKnee = knee;
                leftArmPitch = rightArmPitch = -60f;
                armSpread = 8f;
                elbowBend = 15f;
                lean = 20f + breath;
                squatTarget = Drop(hip, knee);
            }
            else if (motion == CharacterMotion.Sit)
            {
                // On a chair: thighs level, shins down, hands resting on the thighs.
                const float hip = 90f, knee = 90f;
                leftLegPitch = rightLegPitch = -hip;
                leftKnee = rightKnee = knee;
                leftArmPitch = rightArmPitch = -35f;
                armSpread = 6f;
                elbowBend = 45f;
                lean = 4f + breath;
                squatTarget = Drop(hip, knee);
            }
            squat = Mathf.Lerp(squat, squatTarget, 1f - Mathf.Exp(-14f * dt));

            SetRotation(leftArm, leftArmPitch, 0f, -armSpread, dt);
            SetRotation(rightArm, rightArmPitch, 0f, armSpread, dt);
            SetRotation(leftForearm, -elbowBend, 0f, 0f, dt);
            SetRotation(rightForearm, -elbowBend, 0f, 0f, dt);
            SetRotation(leftLeg, leftLegPitch, 0f, 0f, dt);
            SetRotation(rightLeg, rightLegPitch, 0f, 0f, dt);
            SetRotation(leftShin, leftKnee, 0f, 0f, dt);
            SetRotation(rightShin, rightKnee, 0f, 0f, dt);
            SetRotation(chest, lean, twist, 0f, dt);
            SetRotation(head, -lean * 0.6f, -twist * 0.8f, 0f, dt);

            if (body != null)
            {
                float bob = grounded ? Mathf.Abs(sin) * 0.025f * move : 0f;
                Vector3 p = body.localPosition;
                p.y = bodyBaseY + bob - squat;
                body.localPosition = p;
            }
        }

        /// <summary>How much lower the body sits when the legs bend by these angles, so the feet stay on the floor.</summary>
        float Drop(float hip, float knee)
        {
            return thighLength * (1f - Mathf.Cos(hip * Mathf.Deg2Rad)) + shinLength * (1f - Mathf.Cos((hip - knee) * Mathf.Deg2Rad));
        }

        static void SetRotation(Transform joint, float pitch, float yaw, float roll, float dt)
        {
            if (joint == null) return;
            Quaternion target = Quaternion.Euler(pitch, yaw, roll);
            joint.localRotation = Quaternion.Slerp(joint.localRotation, target, 1f - Mathf.Exp(-14f * dt));
        }
    }
}
