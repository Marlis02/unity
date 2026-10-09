using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Swings limb pivots so movement reads clearly without any animation clips. Used by
    /// imported models assembled from separate body parts (see "segments" in character.json).
    /// Pivots are expected to face the character's +Z with limbs hanging down; the optional
    /// joints add knees, elbows, a waist and a neck.
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

        void Start()
        {
            if (body != null) bodyBaseY = body.localPosition.y;
        }

        void LateUpdate()
        {
            if (character == null) return;
            float dt = Time.deltaTime;
            float speed = character.PlanarSpeed;
            float gait = Mathf.Clamp01(character.NormalizedGait / 2f);
            float move = Mathf.Clamp01(speed / Mathf.Max(0.1f, character.profile.walkSpeed));
            float run = Mathf.Clamp01(character.NormalizedGait - 1f); // 0 at walk speed, 1 at run speed
            bool grounded = character.Grounded;

            phase += speed / Mathf.Max(0.1f, strideLength) * Mathf.PI * dt;
            float swing = Mathf.Sin(phase) * Mathf.Lerp(0f, 55f, move * (0.6f + gait * 0.6f));

            float armSwing = swing;
            float legSwing = swing;
            float armSpread = 0f;
            if (!grounded)
            {
                // Arms up and legs tucked while airborne.
                armSwing = -150f;
                legSwing = 25f;
                armSpread = 20f;
            }

            SetRotation(leftArm, armSwing, 0f, -armSpread, dt);
            SetRotation(rightArm, grounded ? -armSwing : armSwing, 0f, armSpread, dt);
            SetRotation(leftLeg, grounded ? -legSwing : legSwing, 0f, 0f, dt);
            SetRotation(rightLeg, grounded ? legSwing : -legSwing * 0.5f, 0f, 0f, dt);

            // A knee bends while its leg swings forward, an elbow stays bent more the faster the run.
            float knee = move * (30f + 40f * run);
            float cos = Mathf.Cos(phase);
            SetRotation(leftShin, grounded ? knee * Mathf.Max(0f, cos) + 4f * move : 70f, 0f, 0f, dt);
            SetRotation(rightShin, grounded ? knee * Mathf.Max(0f, -cos) + 4f * move : 35f, 0f, 0f, dt);
            float elbow = grounded ? 8f + move * (12f + 63f * run) : 15f;
            SetRotation(leftForearm, -elbow, 0f, 0f, dt);
            SetRotation(rightForearm, -elbow, 0f, 0f, dt);

            // The shoulders turn with the forward arm, the head keeps looking ahead.
            float breath = Mathf.Sin(Time.time * 2.2f) * 1.5f * (1f - move);
            float twist = grounded ? -swing * 0.2f : 0f;
            float lean = grounded ? move * (3f + 9f * run) + breath : -6f;
            SetRotation(chest, lean, twist, 0f, dt);
            SetRotation(head, -lean * 0.6f, -twist * 0.8f, 0f, dt);

            if (body != null)
            {
                float bob = grounded ? Mathf.Abs(Mathf.Sin(phase)) * 0.04f * Mathf.Clamp01(speed) : 0f;
                Vector3 p = body.localPosition;
                p.y = bodyBaseY + bob;
                body.localPosition = p;
            }
        }

        static void SetRotation(Transform joint, float pitch, float yaw, float roll, float dt)
        {
            if (joint == null) return;
            Quaternion target = Quaternion.Euler(pitch, yaw, roll);
            joint.localRotation = Quaternion.Slerp(joint.localRotation, target, 1f - Mathf.Exp(-14f * dt));
        }
    }
}
