using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Viewer
{
    // A free camera around a point, for looking at the character and recording him from a phone. Reads the Input System
    // (the project has the old Input Manager turned off).
    // Touch: one finger orbits; two fingers move the view, pinch to zoom and twist to turn; a tap, a double tap and a
    // long press are reported (a tap only once no second tap follows).
    // Mouse: left drag orbits, right or middle drag (or Shift + left) moves, the wheel zooms, a double click or F asks to
    // reframe. Movement is smoothed a little so recordings look steady.
    public class ViewerCamera : MonoBehaviour
    {
        const float Smoothing = 12f;
        const float OrbitDegreesPerScreen = 240f; // a drag across the screen's short side
        const float TapSlopFraction = 0.03f;
        const float TapMaxSeconds = 0.45f;    // a slow phone delivers the lift a frame late
        const float DoubleTapSeconds = 0.4f;
        const float LongPressSeconds = 0.8f;
        const float MouseAfterTouchSeconds = 0.6f; // browsers also report touches as mouse events
        const float FloorClearance = 0.05f;

        public float minDistance = 0.3f;
        public float maxDistance = 40f;
        public float minPitch = -60f;
        public float maxPitch = 89f;

        public event Action Tapped;
        public event Action DoubleTapped;
        public event Action LongPressed;

        Camera viewCamera;
        Vector3 pivot, targetPivot;
        float yaw = 180f, targetYaw = 180f;
        float pitch = 10f, targetPitch = 10f;
        float distance = 4f, targetDistance = 4f;

        // Orbit, pan and pinch: read frame by frame from the fingers on the screen
        int fingerCount;
        int id0 = -1, id1 = -1;
        Vector2 last0, last1;
        bool gestureOnUi;

        // Taps and long presses: read finger by finger, as the Input System reports each finger going down and up in
        // order even when a slow frame holds the end of one tap and the start of the next (a quick double tap)
        class Press
        {
            public Vector2 origin;
            public double start;    // the touch's own time stamps, not the frame's, so a slow phone reads them right
            public bool candidate;  // still a tap: a single finger that has neither moved nor started on the menu
            public bool longFired;
        }
        readonly Dictionary<int, Press> presses = new Dictionary<int, Press>(); // by finger
        double lastTapTime = -10.0;
        int lastTapFrame;
        Vector2 lastTapPosition;
        bool tapPending;
        float lastTouchTime = -10f;

        enum MouseDrag { None, Orbit, Pan }
        MouseDrag mouseDrag;
        Vector2 lastMouse;
        float lastClickTime = -10f;
        Vector2 lastClickPosition;

        float ShortSide => Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
        float TapSlop => ShortSide * TapSlopFraction;

        /// <summary>
        /// Points the camera at a spot, fitting about this height into the band of the screen between top and bottom
        /// (fractions of its height from the top) and centring the spot in that band.
        /// </summary>
        public void Focus(Vector3 point, float size, float top, float bottom, bool resetAngles, bool instant)
        {
            float fov = viewCamera != null ? viewCamera.fieldOfView : 50f;
            float band = Mathf.Clamp(bottom - top, 0.2f, 1f);
            // A portrait phone sees less across than up: fit the size into the narrower of the two
            float halfHeight = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * Mathf.Max(0.1f, (float)Screen.width / Mathf.Max(1, Screen.height));
            targetDistance = Mathf.Clamp(size * 0.62f / Mathf.Min(halfHeight * band, halfWidth * 1.6f) + 0.4f, minDistance, maxDistance);
            // The band's middle lies (centre - 0.5) of the screen's height below its centre: aim that much above the spot
            float below = ((top + bottom) * 0.5f - 0.5f) * 2f * targetDistance * halfHeight;
            targetPivot = point + Vector3.up * below;
            if (resetAngles)
            {
                // From the front, turning the short way round
                targetYaw = 180f + Mathf.Round((yaw - 180f) / 360f) * 360f;
                targetPitch = 8f;
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
            EnhancedTouchSupport.Enable();
        }

        void OnEnable()
        {
            Touch.onFingerDown += FingerDown;
            Touch.onFingerMove += FingerMove;
            Touch.onFingerUp += FingerUp;
        }

        void OnDisable()
        {
            Touch.onFingerDown -= FingerDown;
            Touch.onFingerMove -= FingerMove;
            Touch.onFingerUp -= FingerUp;
        }

        void LateUpdate()
        {
            if (Touch.activeTouches.Count > 0 || fingerCount > 0) HandleTouches();
            else HandleMouse();
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) DoubleTapped?.Invoke();
            if (presses.Count == 1)
            {
                foreach (var press in presses.Values)
                {
                    if (!press.candidate || press.longFired || Now - press.start < LongPressSeconds) continue;
                    press.longFired = true;
                    press.candidate = false;
                    tapPending = false;
                    LongPressed?.Invoke();
                }
            }
            // A single tap once no second tap can still come: its time is up, and a frame has gone by since it was seen,
            // so a second tap made in time has reached the Input System (on a slow frame that comes late)
            if (tapPending && Now - lastTapTime > DoubleTapSeconds && Time.frameCount > lastTapFrame)
            {
                tapPending = false;
                if (ViewerUi.Debug) UnityEngine.Debug.Log($"[ViewerInput] frame {Time.frameCount} single tap");
                Tapped?.Invoke();
            }

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

        // The lowest pitch that keeps the camera above the floor
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
            // The view follows the fingers: what was under them stays under them
            float fov = viewCamera != null ? viewCamera.fieldOfView : 50f;
            float metresPerPixel = 2f * distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, Screen.height);
            targetPivot -= (transform.right * pixels.x + transform.up * pixels.y) * metresPerPixel;
        }

        void HandleTouches()
        {
            lastTouchTime = Time.unscaledTime;

            // Fingers still on the screen; the first two drive the gesture
            int active = 0;
            int a = -1, b = -1;
            Vector2 p0 = default, p1 = default;
            var touches = Touch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                Touch touch = touches[i];
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) continue;
                if (active == 0) { a = touch.touchId; p0 = touch.screenPosition; }
                else if (active == 1) { b = touch.touchId; p1 = touch.screenPosition; }
                active++;
            }
            if (active > 0 && fingerCount == 0) gestureOnUi = ViewerUi.IsOver(p0);

            // Start over whenever fingers are added, lifted or swapped, so nothing jumps
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
            fingerCount = active;
        }

        void FingerDown(Finger finger)
        {
            lastTouchTime = Time.unscaledTime;
            var touch = finger.currentTouch.valid ? finger.currentTouch : finger.lastTouch;
            bool alone = presses.Count == 0;
            if (!alone)
            {
                // A second finger: a pinch or a pan, not a tap
                foreach (var other in presses.Values) other.candidate = false;
                tapPending = false;
            }
            presses[finger.index] = new Press
            {
                origin = touch.screenPosition,
                start = touch.startTime,
                candidate = alone && !ViewerUi.IsOver(touch.screenPosition),
            };
        }

        void FingerMove(Finger finger)
        {
            if (!presses.TryGetValue(finger.index, out var press) || !press.candidate) return;
            if ((finger.screenPosition - press.origin).magnitude <= TapSlop) return;
            press.candidate = false; // a drag
            tapPending = false;      // and a drag right after a tap makes that no tap either
        }

        void FingerUp(Finger finger)
        {
            lastTouchTime = Time.unscaledTime;
            if (!presses.TryGetValue(finger.index, out var press)) return;
            presses.Remove(finger.index);
            var touch = finger.lastTouch;
            Vector2 position = touch.valid ? touch.screenPosition : finger.screenPosition;
            double time = touch.valid ? touch.time : Now;
            bool tap = press.candidate && (position - press.origin).magnitude <= TapSlop && time - press.start <= TapMaxSeconds;
            if (ViewerUi.Debug)
                UnityEngine.Debug.Log($"[ViewerInput] frame {Time.frameCount} finger {finger.index} up: tap {tap} ({time - press.start:0.000} s, candidate {press.candidate})");
            if (tap) Tap(position, time);
        }

        // Seconds on the clock the Input System stamps its events with
        static double Now => Time.realtimeSinceStartupAsDouble;

        void Tap(Vector2 position, double time)
        {
            if (ViewerUi.Debug)
                UnityEngine.Debug.Log($"[ViewerInput] frame {Time.frameCount} tap at {position}, pending {tapPending}, {time - lastTapTime:0.000} s after the last, {(position - lastTapPosition).magnitude:0} px away");
            if (tapPending && time - lastTapTime <= DoubleTapSeconds && (position - lastTapPosition).magnitude <= TapSlop * 2f)
            {
                tapPending = false;
                if (ViewerUi.Debug) UnityEngine.Debug.Log($"[ViewerInput] frame {Time.frameCount} double tap");
                DoubleTapped?.Invoke();
            }
            else
            {
                tapPending = true;
                lastTapTime = time;
                lastTapFrame = Time.frameCount;
                lastTapPosition = position;
            }
        }

        void HandleMouse()
        {
            var mouseDevice = Mouse.current;
            if (mouseDevice == null) return;
            Vector2 mouse = mouseDevice.position.ReadValue();
            if (Time.unscaledTime - lastTouchTime < MouseAfterTouchSeconds)
            {
                mouseDrag = MouseDrag.None;
                lastMouse = mouse;
                return;
            }

            bool left = mouseDevice.leftButton.wasPressedThisFrame;
            bool other = mouseDevice.rightButton.wasPressedThisFrame || mouseDevice.middleButton.wasPressedThisFrame;
            if (left || other)
            {
                bool overUi = ViewerUi.IsOver(mouse);
                var keyboard = Keyboard.current;
                bool shift = keyboard != null && keyboard.shiftKey.isPressed;
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
            else if (!mouseDevice.leftButton.isPressed && !mouseDevice.rightButton.isPressed && !mouseDevice.middleButton.isPressed)
            {
                mouseDrag = MouseDrag.None;
            }

            Vector2 delta = mouse - lastMouse;
            if (mouseDrag == MouseDrag.Orbit) Orbit(delta);
            else if (mouseDrag == MouseDrag.Pan) Pan(delta);
            lastMouse = mouse;

            // Wheels report notches as about 120 on some platforms and as 1 on others
            float scroll = mouseDevice.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 10f) scroll /= 120f;
            if (Mathf.Abs(scroll) > 0.01f && !ViewerUi.IsOver(mouse))
                targetDistance *= Mathf.Pow(0.9f, Mathf.Clamp(scroll, -5f, 5f));
        }
    }
}
