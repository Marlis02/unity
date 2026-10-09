using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Swings limb pivots of the built-in primitive characters so movement reads clearly
    /// without any animation clips.
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

            phase += speed / Mathf.Max(0.1f, strideLength) * Mathf.PI * dt;
            float swing = Mathf.Sin(phase) * Mathf.Lerp(0f, 55f, Mathf.Clamp01(speed / Mathf.Max(0.1f, character.profile.walkSpeed)) * (0.6f + gait * 0.6f));

            float armSwing = swing;
            float legSwing = swing;
            float armSpread = 0f;
            if (!character.Grounded)
            {
                // Arms up and legs tucked while airborne.
                armSwing = -150f;
                legSwing = 25f;
                armSpread = 20f;
            }

            SetSwing(leftArm, armSwing, -armSpread, dt);
            SetSwing(rightArm, character.Grounded ? -armSwing : armSwing, armSpread, dt);
            SetSwing(leftLeg, character.Grounded ? -legSwing : legSwing, 0f, dt);
            SetSwing(rightLeg, character.Grounded ? legSwing : -legSwing * 0.5f, 0f, dt);

            if (body != null)
            {
                float bob = character.Grounded ? Mathf.Abs(Mathf.Sin(phase)) * 0.04f * Mathf.Clamp01(speed) : 0f;
                Vector3 p = body.localPosition;
                p.y = bodyBaseY + bob;
                body.localPosition = p;
            }
        }

        static void SetSwing(Transform limb, float pitch, float roll, float dt)
        {
            if (limb == null) return;
            Quaternion target = Quaternion.Euler(pitch, 0f, roll);
            limb.localRotation = Quaternion.Slerp(limb.localRotation, target, 1f - Mathf.Exp(-14f * dt));
        }
    }
}
