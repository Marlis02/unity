using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Viewer
{
    // The phone viewer: bacon on the studio floor, a free camera (ViewerCamera) and a small menu to pick his move, his
    // face, the speed and a pause. Made for filming him off the screen, so the menu hides completely.
    // The menu is drawn with IMGUI but its buttons are hit-tested here from the Input System, as the project has the
    // old Input Manager off and touches must work in a phone browser.
    // Gestures: a tap pauses, a double tap brings him back into the frame, a long press brings the menu back.
    // Keys: H menu, Space pause, F reframe, Left/Right face.
    public class BaconViewer : MonoBehaviour
    {
        public ViewerCamera viewCamera;
        public BaconMotion actor;

        static readonly BaconMotion.Move[] Moves =
        {
            BaconMotion.Move.Idle, BaconMotion.Move.Walk, BaconMotion.Move.Run, BaconMotion.Move.Jump,
            BaconMotion.Move.Cheer, BaconMotion.Move.Wave, BaconMotion.Move.Squat, BaconMotion.Move.Shrug,
            BaconMotion.Move.Bow, BaconMotion.Move.Kick, BaconMotion.Move.Point, BaconMotion.Move.LookAround,
        };
        static readonly string[] MoveTitles =
        {
            "Покой", "Шаг", "Бег", "Прыжок",
            "Ура", "Привет", "Присед", "Плечами",
            "Поклон", "Пинок", "Указать", "Оглянуться",
        };
        static readonly float[] Speeds = { 0.25f, 0.5f, 1f };
        static readonly string[] SpeedTitles = { "0.25×", "0.5×", "1×" };

        const float Margin = 10f, Padding = 10f, Gap = 4f, RowHeight = 34f, PanelWidth = 340f;
        const float FrameHeight = 0.95f, FrameSize = 1.9f; // metres: the middle of his body and about his height

        bool menuVisible = true;
        bool paused;
        float playbackSpeed = 0.5f;

        struct Button
        {
            public Rect rect;    // GUI units, y down
            public string label;
            public bool active;
            public Action action;
        }

        readonly List<Button> buttons = new List<Button>();
        Rect panelRect, helpRect, faceLabelRect;
        string helpText = "";
        float scale = 1f;
        int pressedButton = -1;
        readonly Dictionary<int, int> fingerButtons = new Dictionary<int, int>(); // finger -> the button it went down on
        float lastTouchTime = -10f;
        float fps = 60f;

        bool debugInput; // ?debug in the page address: input is logged to the browser console
        Font font;
        GUIStyle panelStyle, buttonStyle, activeButtonStyle, labelStyle, smallStyle;
        Texture2D panelTexture, buttonTexture, pressedTexture;

        void Awake()
        {
            EnhancedTouchSupport.Enable();
            font = Resources.Load<Font>("ViewerFont");
            debugInput = Application.absoluteURL.Contains("debug");
            ViewerUi.Debug = debugInput;
        }

        void Start()
        {
            actor.speed = playbackSpeed;
            Layout();
            Frame(true);
            viewCamera.Tapped += () => SetPaused(!paused);
            viewCamera.DoubleTapped += () => Frame(false);
            viewCamera.LongPressed += () => menuVisible = true;
        }

        // He goes into the part of the screen the menu leaves free: below it on a portrait phone, where the menu runs
        // across the screen, and the whole screen otherwise
        void Frame(bool instant)
        {
            float top = 0f, bottom = 1f; // fractions of the screen's height from its top
            if (menuVisible && panelRect.xMax * scale > Screen.width * 0.45f)
            {
                top = panelRect.yMax * scale / Mathf.Max(1, Screen.height);
                bottom = helpRect.yMin * scale / Mathf.Max(1, Screen.height);
            }
            if (debugInput) Debug.Log($"[ViewerInput] frame {Time.frameCount} frame him: band {top:0.00}-{bottom:0.00}, instant {instant}");
            viewCamera.Focus(actor.transform.position + Vector3.up * FrameHeight, FrameSize, top, bottom, true, instant);
        }

        void SetPaused(bool value)
        {
            paused = value;
            actor.paused = value;
        }

        void SetSpeed(float value)
        {
            playbackSpeed = value;
            actor.speed = value;
            SetPaused(false);
        }

        void ChangeFace(int step)
        {
            int count = BaconMotion.Faces.Length;
            actor.face = ((actor.face + step) % count + count) % count;
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.001f, Time.unscaledDeltaTime), 0.05f);
            Layout();
            HandlePointer();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.hKey.wasPressedThisFrame) menuVisible = !menuVisible;
            if (keyboard.spaceKey.wasPressedThisFrame) SetPaused(!paused);
            if (keyboard.rightArrowKey.wasPressedThisFrame) ChangeFace(1);
            if (keyboard.leftArrowKey.wasPressedThisFrame) ChangeFace(-1);
        }

        // A button acts when the finger or the mouse button is let go over the same button it went down on
        void HandlePointer()
        {
            var touches = Touch.activeTouches;
            if (touches.Count > 0) lastTouchTime = Time.unscaledTime;
            if (debugInput)
            {
                var m = Mouse.current;
                if (m != null && (m.leftButton.wasPressedThisFrame || m.leftButton.wasReleasedThisFrame))
                    Debug.Log($"[ViewerInput] frame {Time.frameCount} mouse {(m.leftButton.wasPressedThisFrame ? "down" : "")}{(m.leftButton.wasReleasedThisFrame ? "up" : "")} at {m.position.ReadValue()} screen {Screen.width}x{Screen.height}");
            }
            if (touches.Count > 0 || Time.unscaledTime - lastTouchTime < 0.6f) return; // a browser's copies of touches

            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 position = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame) pressedButton = HitButton(position);
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (Release(position, pressedButton) is Action action) action();
                pressedButton = -1;
            }
        }

        // Fingers on the menu, one by one in the order they went down and up (a slow frame can hold several)
        void OnEnable()
        {
            Touch.onFingerDown += FingerDown;
            Touch.onFingerUp += FingerUp;
        }

        void OnDisable()
        {
            Touch.onFingerDown -= FingerDown;
            Touch.onFingerUp -= FingerUp;
        }

        void FingerDown(Finger finger)
        {
            lastTouchTime = Time.unscaledTime;
            var touch = finger.currentTouch.valid ? finger.currentTouch : finger.lastTouch;
            int hit = HitButton(touch.screenPosition);
            if (debugInput) Debug.Log($"[ViewerInput] frame {Time.frameCount} finger {finger.index} down at {touch.screenPosition}, button {hit}, screen {Screen.width}x{Screen.height}");
            if (hit < 0) return;
            fingerButtons[finger.index] = hit;
            pressedButton = hit;
        }

        void FingerUp(Finger finger)
        {
            lastTouchTime = Time.unscaledTime;
            if (!fingerButtons.TryGetValue(finger.index, out int button)) return;
            fingerButtons.Remove(finger.index);
            var touch = finger.lastTouch;
            Vector2 position = touch.valid ? touch.screenPosition : finger.screenPosition;
            if (pressedButton == button) pressedButton = -1;
            Release(position, button)?.Invoke();
        }

        // The button's action when the pointer is let go over the button it went down on
        Action Release(Vector2 screenPosition, int pressed)
        {
            int hit = HitButton(screenPosition);
            return hit >= 0 && hit == pressed && hit < buttons.Count ? buttons[hit].action : null;
        }

        int HitButton(Vector2 screenPosition)
        {
            if (!menuVisible) return -1;
            var gui = new Vector2(screenPosition.x, Screen.height - screenPosition.y) / scale;
            for (int i = 0; i < buttons.Count; i++) if (buttons[i].rect.Contains(gui)) return i;
            return -1;
        }

        // ---------------------------------------------------------------- layout

        // Phones get a menu about as wide as the screen's short side, big enough for fingers
        void Layout()
        {
            float shortSide = Mathf.Min(Screen.width, Screen.height);
            scale = Application.isMobilePlatform ? Mathf.Max(0.5f, shortSide / 360f) : Mathf.Clamp(shortSide / 620f, 1f, 3f);
            buttons.Clear();
            if (!menuVisible) return;

            float screenWidth = Screen.width / scale, screenHeight = Screen.height / scale;
            float width = Mathf.Min(PanelWidth, screenWidth - 2f * Margin);
            float inner = width - 2f * Padding;
            float x = Margin + Padding, y = Margin + Padding;

            // Face: < name >
            float arrow = 44f;
            Add(new Rect(x, y, arrow, RowHeight), "<", false, () => ChangeFace(-1));
            faceLabelRect = new Rect(x + arrow + Gap, y, inner - 2f * (arrow + Gap), RowHeight);
            Add(new Rect(x + inner - arrow, y, arrow, RowHeight), ">", false, () => ChangeFace(1));
            y += RowHeight + 2f * Gap;

            // Moves, four to a row
            float cell = (inner - 3f * Gap) / 4f;
            for (int i = 0; i < Moves.Length; i++)
            {
                var move = Moves[i];
                Add(new Rect(x + (i % 4) * (cell + Gap), y + (i / 4) * (RowHeight + Gap), cell, RowHeight), MoveTitles[i],
                    actor.move == move, () => actor.Play(move));
            }
            y += (Moves.Length + 3) / 4 * (RowHeight + Gap) + Gap;

            // Speed and pause
            for (int i = 0; i < Speeds.Length; i++)
            {
                float speed = Speeds[i];
                Add(new Rect(x + i * (cell + Gap), y, cell, RowHeight), SpeedTitles[i], !paused && Mathf.Approximately(playbackSpeed, speed), () => SetSpeed(speed));
            }
            Add(new Rect(x + 3f * (cell + Gap), y, cell, RowHeight), paused ? "Дальше" : "Пауза", paused, () => SetPaused(!paused));
            y += RowHeight + Gap;

            // Framing and hiding the menu
            float half = (inner - Gap) / 2f;
            Add(new Rect(x, y, half, RowHeight), "К персонажу", false, () => Frame(false));
            Add(new Rect(x + half + Gap, y, half, RowHeight), "Скрыть меню", false, () => menuVisible = false);
            y += RowHeight;

            panelRect = new Rect(Margin, Margin, width, y + Padding - Margin);
            RegisterScreen(panelRect);

            helpText = Application.isMobilePlatform
                ? "1 палец — вращать · 2 пальца — двигать, щипок — зум\nТап — пауза · 2 тапа — к персонажу · долгое — меню"
                : "Левая кнопка — вращать · правая или Shift — двигать · колесо — зум\nПробел — пауза · двойной клик или F — к персонажу · H — меню · ← → лицо";
            float helpWidth = Mathf.Min(560f, screenWidth - 2f * Margin);
            float helpHeight = 2f * 16f + 12f;
            helpRect = new Rect(Margin, screenHeight - helpHeight - Margin, helpWidth, helpHeight);
            RegisterScreen(helpRect);
        }

        void Add(Rect rect, string label, bool active, Action action)
        {
            buttons.Add(new Button { rect = rect, label = label, active = active, action = action });
        }

        void RegisterScreen(Rect gui)
        {
            ViewerUi.Register(new Rect(gui.x * scale, Screen.height - (gui.y + gui.height) * scale, gui.width * scale, gui.height * scale));
        }

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            var e = Event.current;
            if (debugInput && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.TouchDown || e.type == EventType.TouchUp))
                Debug.Log($"[ViewerInput] frame {Time.frameCount} imgui {e.type} at {e.mousePosition} screen {Screen.width}x{Screen.height}");
            if (!menuVisible || e.type != EventType.Repaint) return;
            EnsureStyles();
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            GUI.Box(panelRect, GUIContent.none, panelStyle);
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                GUI.Box(b.rect, b.label, i == pressedButton ? activeButtonStyle : b.active ? activeButtonStyle : buttonStyle);
            }
            GUI.Label(faceLabelRect, "Лицо: " + BaconMotion.Faces[actor.face].title, labelStyle);

            GUI.Box(helpRect, GUIContent.none, panelStyle);
            GUI.Label(new Rect(helpRect.x + 10f, helpRect.y + 6f, helpRect.width - 20f, helpRect.height - 12f), helpText, smallStyle);
            float width = Screen.width / scale;
            GUI.Label(new Rect(width - 90f, 8f, 82f, 22f), $"{fps:0} FPS", smallStyle);

            GUI.matrix = previous;
        }

        void EnsureStyles()
        {
            if (panelStyle != null) return;
            panelTexture = Solid(new Color(0.07f, 0.08f, 0.11f, 0.82f));
            buttonTexture = Solid(new Color(0.22f, 0.24f, 0.29f, 0.95f));
            pressedTexture = Solid(new Color(0.32f, 0.34f, 0.4f, 0.98f));

            panelStyle = new GUIStyle { padding = new RectOffset(12, 12, 10, 10) };
            panelStyle.normal.background = panelTexture;

            labelStyle = new GUIStyle { font = font, fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            labelStyle.normal.textColor = new Color(0.93f, 0.95f, 0.98f);

            smallStyle = new GUIStyle { font = font, fontSize = 12, wordWrap = true };
            smallStyle.normal.textColor = new Color(0.75f, 0.8f, 0.88f);

            buttonStyle = new GUIStyle { font = font, fontSize = 13, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            buttonStyle.normal.background = buttonTexture;
            buttonStyle.normal.textColor = new Color(0.9f, 0.92f, 0.96f);

            activeButtonStyle = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Bold };
            activeButtonStyle.normal.background = pressedTexture;
            activeButtonStyle.normal.textColor = new Color(0.98f, 0.76f, 0.3f);
        }

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
