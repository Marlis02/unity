using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// On-screen controls for phones and tablets: a floating joystick on the left half of the
    /// screen, camera drag on the right half, and Jump / Run buttons. Stays hidden until the
    /// first touch so desktop players never see it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TouchControls : MonoBehaviour
    {
        /// <summary>True once the player has touched the screen.</summary>
        public static bool InUse { get; private set; }

        int moveFinger = -1;
        int lookFinger = -1;
        Vector2 moveOrigin;
        Vector2 moveCurrent;
        Vector2 lastLook;
        bool active;
        Texture2D circle;
        GUIStyle buttonLabel;

        float Radius => Mathf.Min(Screen.width, Screen.height) * 0.11f;
        // Screen space (y up).
        Vector2 JumpCenter => new Vector2(Screen.width - Radius * 1.5f, Radius * 1.6f);
        Vector2 RunCenter => new Vector2(Screen.width - Radius * 3.9f, Radius * 1.1f);

        void Awake()
        {
            circle = CreateCircleTexture(128);
        }

        void OnDestroy()
        {
            if (circle != null) Destroy(circle);
        }

        void Update()
        {
            PlaygroundInput.TouchMove = Vector2.zero;
            PlaygroundInput.TouchLookDelta = Vector2.zero;

            if (Input.touchCount > 0) active = InUse = true;
            if (!active) return;

            float r = Radius;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                Vector2 pos = touch.position;
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        if (Vector2.Distance(pos, JumpCenter) <= r)
                        {
                            PlaygroundInput.TouchJumpQueued = true;
                        }
                        else if (Vector2.Distance(pos, RunCenter) <= r * 0.8f)
                        {
                            PlaygroundInput.TouchRun = !PlaygroundInput.TouchRun;
                        }
                        else if (PlaygroundInput.IsOverUi(pos))
                        {
                            // Belongs to an on-screen panel.
                        }
                        else if (pos.x < Screen.width * 0.5f && moveFinger < 0)
                        {
                            moveFinger = touch.fingerId;
                            moveOrigin = pos;
                            moveCurrent = pos;
                        }
                        else if (lookFinger < 0)
                        {
                            lookFinger = touch.fingerId;
                            lastLook = pos;
                        }
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (touch.fingerId == moveFinger) moveCurrent = pos;
                        if (touch.fingerId == lookFinger)
                        {
                            PlaygroundInput.TouchLookDelta += pos - lastLook;
                            lastLook = pos;
                        }
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (touch.fingerId == moveFinger) moveFinger = -1;
                        if (touch.fingerId == lookFinger) lookFinger = -1;
                        break;
                }
            }

            if (moveFinger >= 0)
            {
                PlaygroundInput.TouchMove = Vector2.ClampMagnitude((moveCurrent - moveOrigin) / r, 1f);
            }
        }

        void OnGUI()
        {
            if (!active || Event.current.type != EventType.Repaint) return;
            PlaygroundFont.ApplyToGui();
            if (buttonLabel == null)
            {
                buttonLabel = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                };
            }

            float r = Radius;
            if (moveFinger >= 0)
            {
                DrawCircle(moveOrigin, r, new Color(1f, 1f, 1f, 0.18f));
                Vector2 knob = moveOrigin + PlaygroundInput.TouchMove * r;
                DrawCircle(knob, r * 0.45f, new Color(1f, 1f, 1f, 0.45f));
            }
            else
            {
                DrawLabel(new Vector2(Screen.width * 0.18f, r * 1.2f), r, "Тяни здесь,\nчтобы идти", new Color(1f, 1f, 1f, 0.35f));
            }

            DrawCircle(JumpCenter, r, new Color(1f, 1f, 1f, 0.3f));
            DrawLabel(JumpCenter, r, "Прыжок", Color.white);

            Color runColor = PlaygroundInput.TouchRun ? new Color(1f, 0.75f, 0.2f, 0.6f) : new Color(1f, 1f, 1f, 0.22f);
            DrawCircle(RunCenter, r * 0.8f, runColor);
            DrawLabel(RunCenter, r * 0.8f, "Бег", Color.white);
        }

        void DrawCircle(Vector2 screenCenter, float radius, Color color)
        {
            var rect = new Rect(screenCenter.x - radius, Screen.height - screenCenter.y - radius, radius * 2f, radius * 2f);
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, circle);
            GUI.color = previous;
        }

        void DrawLabel(Vector2 screenCenter, float radius, string text, Color color)
        {
            var rect = new Rect(screenCenter.x - radius * 1.5f, Screen.height - screenCenter.y - radius, radius * 3f, radius * 2f);
            buttonLabel.fontSize = Mathf.RoundToInt(radius * 0.32f);
            buttonLabel.normal.textColor = color;
            GUI.Label(rect, text, buttonLabel);
        }

        static Texture2D CreateCircleTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                    byte alpha = (byte)(Mathf.Clamp01(half - d) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
