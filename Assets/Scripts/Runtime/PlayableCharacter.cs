using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>What a character shows on the spot.</summary>
    public enum CharacterMotion { Idle, Walk, Run, Jump }

    /// <summary>
    /// A character standing on the stage and showing a motion in place: walking and running
    /// animate at the profile's speeds without moving it, jumping hops straight up under gravity.
    /// Drives an optional Animator through these parameters, each used only when the controller
    /// defines it:
    ///   Speed (float)         0 = idle, 1 = walk, 2 = run
    ///   MoveSpeed (float)     the shown speed in m/s
    ///   Grounded (bool)
    ///   VerticalSpeed (float) m/s, positive while rising
    ///   Jump (trigger)
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayableCharacter : MonoBehaviour
    {
        const float PauseBetweenJumps = 0.35f;
        const float FallRespawnDepth = -25f;

        public CharacterProfile profile = new CharacterProfile();
        public Animator animator;
        public CharacterMotion motion = CharacterMotion.Idle;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
        static readonly int JumpHash = Animator.StringToHash("Jump");

        CharacterController controller;
        float verticalSpeed;
        float groundedTime;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        bool hasSpeed, hasMoveSpeed, hasGrounded, hasVerticalSpeed, hasJump;

        /// <summary>Speed the character is shown moving at, in m/s; it stays on the spot.</summary>
        public float PlanarSpeed { get; private set; }
        public bool Grounded { get; private set; } = true;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            CacheAnimatorParameters();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        void CacheAnimatorParameters()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == SpeedHash && parameter.type == AnimatorControllerParameterType.Float) hasSpeed = true;
                else if (parameter.nameHash == MoveSpeedHash && parameter.type == AnimatorControllerParameterType.Float) hasMoveSpeed = true;
                else if (parameter.nameHash == GroundedHash && parameter.type == AnimatorControllerParameterType.Bool) hasGrounded = true;
                else if (parameter.nameHash == VerticalSpeedHash && parameter.type == AnimatorControllerParameterType.Float) hasVerticalSpeed = true;
                else if (parameter.nameHash == JumpHash && parameter.type == AnimatorControllerParameterType.Trigger) hasJump = true;
            }
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            spawnPosition = position;
            spawnRotation = rotation;
            verticalSpeed = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bool grounded = controller.isGrounded;
            groundedTime = grounded ? groundedTime + dt : 0f;
            if (grounded && verticalSpeed < 0f) verticalSpeed = -2f;

            if (motion == CharacterMotion.Jump && grounded && groundedTime >= PauseBetweenJumps)
            {
                verticalSpeed = profile.JumpVelocity;
                groundedTime = 0f;
                if (hasJump) animator.SetTrigger(JumpHash);
            }

            verticalSpeed -= profile.gravity * dt;
            CollisionFlags flags = controller.Move(Vector3.up * (verticalSpeed * dt));
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
            Grounded = controller.isGrounded;

            PlanarSpeed = motion == CharacterMotion.Walk ? profile.walkSpeed
                : motion == CharacterMotion.Run ? profile.runSpeed
                : 0f;

            UpdateAnimator(dt);

            if (transform.position.y < FallRespawnDepth) Teleport(spawnPosition, spawnRotation);
        }

        /// <summary>0 = idle, 1 = walk speed, 2 = run speed; values in between blend.</summary>
        public float NormalizedGait
        {
            get
            {
                float walk = Mathf.Max(0.01f, profile.walkSpeed);
                if (PlanarSpeed <= walk) return PlanarSpeed / walk;
                float run = Mathf.Max(walk + 0.01f, profile.runSpeed);
                return 1f + Mathf.Clamp01((PlanarSpeed - walk) / (run - walk));
            }
        }

        void UpdateAnimator(float dt)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (hasSpeed) animator.SetFloat(SpeedHash, NormalizedGait, 0.1f, dt);
            if (hasMoveSpeed) animator.SetFloat(MoveSpeedHash, PlanarSpeed, 0.1f, dt);
            if (hasGrounded) animator.SetBool(GroundedHash, Grounded);
            if (hasVerticalSpeed) animator.SetFloat(VerticalSpeedHash, verticalSpeed);
        }
    }
}
