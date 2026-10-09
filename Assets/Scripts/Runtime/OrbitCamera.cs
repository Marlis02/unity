using System;
using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Free camera around a point, made for looking at characters and recording them.
    /// Touch: one finger orbits; two fingers move the view, pinch to zoom and twist to turn;
    /// double tap asks to reframe; a long press is reported for bringing hidden menus back.
    /// Mouse: left drag orbits, right or middle drag (or Shift + left) moves, the wheel zooms,
    /// double click or F asks to reframe. Movement is smoothed slightly so recordings look steady.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        const float Smoothing = 12f;
        const float OrbitDegreesPerScreen = 240f; // a drag across the screen's short side
        const float TapSlopFraction = 0.03f;
        const float TapMaxSeconds = 0.3f;
        const float DoubleTapSeconds = 0.35f;
        const float LongPressSeconds = 0.7f;
        const float MouseAfterTouchSeconds = 0.6f;
        const float FloorClearance = 0.05f;

        public float minDistance = 0.3f;
        public float maxDistance = 80f;
        public float minPitch = -60f;
        public float maxPitch = 89f;

        public event Action DoubleTapped;
        public event Action LongPressed;

        Camera viewCamera;
        Vector3 pivot, targetPivot;
        float yaw = 180f, targetYaw = 180f;
        float pitch = 10f, targetPitch = 10f;
        float distance = 5f, targetDistance = 5f;

        int fingerCount;
        int maxFingers;
        int id0 = -1, id1 = -1;
        Vector2 last0, last1;
        bool gestureOnUi;
        float pressStart;
        Vector2 pressOrigin;
        bool pressMoved;
        bool longPressFired;
        float lastTapTime = -10f;
        Vector2 lastTapPosition;
        float lastTouchTime = -10f;

        enum MouseDrag { None, Orbit, Pan }
        MouseDrag mouseDrag;
        Vector3 lastMouse;
        float lastClickTime = -10f;
        Vector3 lastClickPosition;

        float ShortSide => Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
        float TapSlop => ShortSide * TapSlopFraction;

        /// <summary>Points the camera at a spot; size is roughly the height of what should fill the view.</summary>
        public void Focus(Vector3 point, float size, bool resetAngles, bool instant)
        {
            targetPivot = point;
            targetDistance = Mathf.Clamp(size * 1.6f + 0.6f, minDistance, maxDistance);
            if (resetAngles)
            {
                // Face the characters from the front, turning the short way round.
                targetYaw = 180f + Mathf.Round((yaw - 180f) / 360f) * 360f;
                targetPitch = 10f;
            }
            if (instant)
            {
                pivot = targetPivot;
                distance = targetDistance;
                yaw = targetYaw;
                pitch = targetPitch;
            }
        }

        void Awake()
        {
            viewCamera = GetComponent<Camera>();
        }

        void LateUpdate()
        {
            if (Input.touchCount > 0 || fingerCount > 0) HandleTouches();
            else HandleMouse();
            if (Input.GetKeyDown(KeyCode.F)) DoubleTapped?.Invoke();

            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
            targetPitch = Mathf.Clamp(targetPitch, Mathf.Max(minPitch, LowestPitch(targetPivot, targetDistance)), maxPitch);

            float t = 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime);
            pivot = Vector3.Lerp(pivot, targetPivot, t);
            yaw = Mathf.Lerp(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            distance = Mathf.Lerp(distance, targetDistance, t);

            Quaternion rotation = Quaternion.Euler(Mathf.Max(pitch, LowestPitch(pivot, distance)), yaw, 0f);
            transform.SetPositionAndRotation(pivot + rotation * Vector3.back * distance, rotation);
        }

        /// <summary>The lowest pitch that keeps the camera above the floor.</summary>
        static float LowestPitch(Vector3 around, float atDistance)
        {
            float sine = (FloorClearance - around.y) / Mathf.Max(0.01f, atDistance);
            return Mathf.Asin(Mathf.Clamp(sine, -1f, 1f)) * Mathf.Rad2Deg;
        }

        void Orbit(Vector2 pixels)
        {
            float degreesPerPixel = OrbitDegreesPerScreen / ShortSide;
            targetYaw += pixels.x * degreesPerPixel;
            targetPitch -= pixels.y * degreesPerPixel;
        }

        void Pan(Vector2 pixels)
        {
            // The view follows the fingers: what was under them stays under them.
            float fov = viewCamera != null ? viewCamera.fieldOfView : 60f;
            float metresPerPixel = 2f * distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, Screen.height);
            targetPivot -= (transform.right * pixels.x + transform.up * pixels.y) * metresPerPixel;
        }

        void HandleTouches()
        {
            lastTouchTime = Time.unscaledTime;

            // Fingers still on the screen; the first two drive the gesture.
            int active = 0;
            int a = -1, b = -1;
            Vector2 p0 = default, p1 = default;
            Vector2 releasePosition = pressOrigin;
            bool quickTap = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    releasePosition = touch.position;
                    // On a slow frame a short tap can start and end before the gesture saw it.
                    quickTap |= fingerCount == 0 && touch.phase == TouchPhase.Ended && Input.touchCount == 1;
                    continue;
                }
                if (active == 0) { a = touch.fingerId; p0 = touch.position; }
                else if (active == 1) { b = touch.fingerId; p1 = touch.position; }
                active++;
            }

            if (active > 0 && fingerCount == 0)
            {
                gestureOnUi = PlaygroundInput.IsOverUi(p0);
                pressStart = Time.unscaledTime;
                pressOrigin = p0;
                pressMoved = false;
                longPressFired = false;
                maxFingers = 0;
            }
            maxFingers = Mathf.Max(maxFingers, active);

            // Start over whenever fingers are added, lifted or swapped, so nothing jumps.
            bool sameFingers = active == fingerCount && a == id0 && (active < 2 || b == id1);
            if (sameFingers && !gestureOnUi)
            {
                if (active == 1)
                {
                    Orbit(p0 - last0);
                }
                else if (active >= 2)
                {
                    Vector2 before = last1 - last0;
                    Vector2 now = p1 - p0;
                    if (before.magnitude > 1f && now.magnitude > 1f)
                    {
                        targetDistance *= before.magnitude / now.magnitude;
                        targetYaw += Vector2.SignedAngle(before, now);
                    }
                    Pan((p0 + p1) * 0.5f - (last0 + last1) * 0.5f);
                }
            }
            last0 = p0;
            last1 = p1;
            id0 = a;
            id1 = b;

            if (active >= 2 || (active == 1 && (p0 - pressOrigin).magnitude > TapSlop)) pressMoved = true;
            if (active == 1 && !pressMoved && !longPressFired && Time.unscaledTime - pressStart >= LongPressSeconds)
            {
                longPressFired = true;
                LongPressed?.Invoke();
            }

            if (active == 0 && fingerCount > 0)
            {
                bool tap = maxFingers == 1 && !pressMoved && !longPressFired && !gestureOnUi
                           && Time.unscaledTime - pressStart <= TapMaxSeconds;
                if (tap) Tap(releasePosition);
            }
            else if (quickTap && !PlaygroundInput.IsOverUi(releasePosition))
            {
                Tap(releasePosition);
            }
            fingerCount = active;
        }

        void Tap(Vector2 position)
        {
            if (Time.unscaledTime - lastTapTime <= DoubleTapSeconds && (position - lastTapPosition).magnitude <= TapSlop * 2f)
            {
                lastTapTime = -10f;
                DoubleTapped?.Invoke();
            }
            else
            {
                lastTapTime = Time.unscaledTime;
                lastTapPosition = position;
            }
        }

        void HandleMouse()
        {
            // Browsers also report touches as mouse events; those were handled above.
            if (Time.unscaledTime - lastTouchTime < MouseAfterTouchSeconds)
            {
                mouseDrag = MouseDrag.None;
                lastMouse = Input.mousePosition;
                return;
            }

            Vector3 mouse = Input.mousePosition;
            bool left = Input.GetMouseButtonDown(0);
            bool other = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            if (left || other)
            {
                bool overUi = PlaygroundInput.IsOverUi(mouse);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                mouseDrag = overUi ? MouseDrag.None : (other || shift) ? MouseDrag.Pan : MouseDrag.Orbit;
                if (left && !overUi)
                {
                    if (Time.unscaledTime - lastClickTime <= DoubleTapSeconds && (mouse - lastClickPosition).magnitude <= TapSlop)
                    {
                        lastClickTime = -10f;
                        DoubleTapped?.Invoke();
                    }
                    else
                    {
                        lastClickTime = Time.unscaledTime;
                        lastClickPosition = mouse;
                    }
                }
            }
            else if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                mouseDrag = MouseDrag.None;
            }

            Vector2 delta = mouse - lastMouse;
            if (mouseDrag == MouseDrag.Orbit) Orbit(delta);
            else if (mouseDrag == MouseDrag.Pan) Pan(delta);
            lastMouse = mouse;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !PlaygroundInput.IsOverUi(mouse))
            {
                targetDistance *= Mathf.Pow(0.9f, Mathf.Clamp(scroll, -5f, 5f));
            }
        }
    }
}
