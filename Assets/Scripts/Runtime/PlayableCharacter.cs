using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Camera-relative third-person movement on a CharacterController. Drives an optional
    /// Animator through these parameters, each used only when the controller defines it:
    ///   Speed (float)         0 = idle, 1 = walk, 2 = run (normalised by the profile's speeds)
    ///   MoveSpeed (float)     horizontal speed in m/s
    ///   Grounded (bool)
    ///   VerticalSpeed (float) m/s, positive while rising
    ///   Jump (trigger)
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayableCharacter : MonoBehaviour
    {
        const float CoyoteTime = 0.1f;
        const float FallRespawnDepth = -25f;

        public CharacterProfile profile = new CharacterProfile();
        public Animator animator;
        [Tooltip("Movement input is interpreted relative to this transform (usually the camera).")]
        public Transform viewTransform;
        public bool acceptInput = true;
        [Tooltip("Push strength against rigidbodies; 1 pushes a 1 kg body at the character's own speed.")]
        public float pushPower = 1f;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");
        static readonly int JumpHash = Animator.StringToHash("Jump");

        CharacterController controller;
        Vector3 planarVelocity;
        float verticalSpeed;
        float lastGroundedTime = -10f;
        bool jumping;
        float jumpStartY;
        float jumpPeakY;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        bool hasSpeed, hasMoveSpeed, hasGrounded, hasVerticalSpeed, hasJump;

        public CharacterController Controller => controller;
        public float PlanarSpeed { get; private set; }
        public bool Grounded { get; private set; } = true;
        /// <summary>Height gained by the last completed jump, in metres.</summary>
        public float LastJumpHeight { get; private set; }
        public int JumpCount { get; private set; }

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

        public void SetSpawn(Vector3 position, Quaternion rotation)
        {
            spawnPosition = position;
            spawnRotation = rotation;
        }

        public void Respawn()
        {
            Teleport(spawnPosition, spawnRotation);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            planarVelocity = Vector3.zero;
            verticalSpeed = 0f;
            jumping = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 input = acceptInput ? PlaygroundInput.Move : Vector2.zero;
            bool run = acceptInput && PlaygroundInput.Run;
            bool jumpPressed = acceptInput && PlaygroundInput.ConsumeJump();

            Vector3 desired = Vector3.zero;
            if (input.sqrMagnitude > 0.0001f)
            {
                Vector3 forward = viewTransform != null ? viewTransform.forward : Vector3.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
                forward.Normalize();
                var right = new Vector3(forward.z, 0f, -forward.x);
                float speed = run ? profile.runSpeed : profile.walkSpeed;
                desired = (forward * input.y + right * input.x) * speed;
            }

            bool groundedBeforeMove = controller.isGrounded;
            float control = groundedBeforeMove ? 1f : profile.airControl;
            planarVelocity = Vector3.MoveTowards(planarVelocity, desired, profile.acceleration * control * dt);

            if (groundedBeforeMove && verticalSpeed < 0f) verticalSpeed = -2f;

            bool canJump = Time.time - lastGroundedTime <= CoyoteTime && !jumping;
            if (jumpPressed && canJump)
            {
                verticalSpeed = profile.JumpVelocity;
                lastGroundedTime = -10f;
                jumping = true;
                jumpStartY = transform.position.y;
                jumpPeakY = jumpStartY;
                JumpCount++;
                if (hasJump) animator.SetTrigger(JumpHash);
            }

            verticalSpeed -= profile.gravity * dt;
            Vector3 before = transform.position;
            CollisionFlags flags = controller.Move((planarVelocity + Vector3.up * verticalSpeed) * dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;

            Vector3 moved = (transform.position - before) / dt;
            PlanarSpeed = new Vector3(moved.x, 0f, moved.z).magnitude;

            if (controller.isGrounded) lastGroundedTime = Time.time;
            Grounded = Time.time - lastGroundedTime <= CoyoteTime;

            if (jumping)
            {
                jumpPeakY = Mathf.Max(jumpPeakY, transform.position.y);
                if (controller.isGrounded && verticalSpeed <= 0f)
                {
                    jumping = false;
                    LastJumpHeight = jumpPeakY - jumpStartY;
                }
            }

            if (desired.sqrMagnitude > 0.01f)
            {
                var target = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, profile.turnSpeed * dt);
            }

            UpdateAnimator(dt);

            if (transform.position.y < FallRespawnDepth) Respawn();
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

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic || hit.moveDirection.y < -0.3f) return;
            var push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z).normalized;
            float speed = Mathf.Max(planarVelocity.magnitude, 1f) * pushPower / Mathf.Max(1f, body.mass);
            body.linearVelocity = new Vector3(push.x * speed, body.linearVelocity.y, push.z * speed);
        }
    }
}
