using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Third-person orbit camera: drag with the mouse (or the right half of a touch screen)
    /// to orbit, scroll to zoom, Q/E to turn. Pulls in when geometry blocks the view.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float targetHeight = 1.5f;
        public float distance = 5f;
        public float minDistance = 1.2f;
        public float maxDistance = 20f;
        public float yaw;
        public float pitch = 15f;
        public float minPitch = -25f;
        public float maxPitch = 75f;
        public float degreesPerPixel = 0.25f;
        public float keyTurnSpeed = 120f;
        public float collisionRadius = 0.2f;

        float currentDistance;
        Vector3 lastMousePosition;
        bool dragging;

        public void Frame(Transform newTarget, float characterHeight)
        {
            target = newTarget;
            targetHeight = Mathf.Max(0.3f, characterHeight * 0.85f);
            distance = Mathf.Clamp(characterHeight * 2.6f + 1f, minDistance, maxDistance);
            currentDistance = distance;
            if (newTarget != null) yaw = newTarget.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;

            HandleMouse();
            yaw += PlaygroundInput.TouchLookDelta.x * degreesPerPixel;
            pitch -= PlaygroundInput.TouchLookDelta.y * degreesPerPixel;
            if (Input.GetKey(KeyCode.Q)) yaw -= keyTurnSpeed * dt;
            if (Input.GetKey(KeyCode.E)) yaw += keyTurnSpeed * dt;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            Vector3 pivot = target.position + Vector3.up * targetHeight;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.back;

            float allowed = distance;
            if (Physics.SphereCast(pivot, collisionRadius, direction, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                allowed = Mathf.Max(0.3f, hit.distance);
            }
            // Snap in immediately when blocked, ease back out when the view clears.
            currentDistance = allowed < currentDistance
                ? allowed
                : Mathf.Lerp(currentDistance, allowed, 1f - Mathf.Exp(-6f * dt));

            transform.SetPositionAndRotation(pivot + direction * currentDistance, rotation);
        }

        void HandleMouse()
        {
            // Touches are handled by TouchControls; ignore the mouse events browsers synthesise from them.
            if (Input.touchCount > 0)
            {
                dragging = false;
                return;
            }

            Vector3 mouse = Input.mousePosition;
            bool pressedThisFrame = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            if (pressedThisFrame)
            {
                dragging = !PlaygroundInput.IsOverUi(mouse);
            }
            else if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            {
                dragging = false;
            }

            if (dragging)
            {
                Vector3 delta = mouse - lastMousePosition;
                yaw += delta.x * degreesPerPixel;
                pitch -= delta.y * degreesPerPixel;
            }
            lastMousePosition = mouse;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !PlaygroundInput.IsOverUi(mouse))
            {
                distance = Mathf.Clamp(distance * (1f - scroll * 0.1f), minDistance, maxDistance);
            }
        }
    }
}
