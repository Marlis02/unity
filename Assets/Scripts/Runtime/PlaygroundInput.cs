using System.Collections.Generic;
using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Merges keyboard/mouse input with the on-screen touch controls so characters and the
    /// camera read one source regardless of the device the build runs on.
    /// </summary>
    public static class PlaygroundInput
    {
        // Written by TouchControls every frame.
        public static Vector2 TouchMove;
        public static Vector2 TouchLookDelta;
        public static bool TouchRun;
        public static bool TouchJumpQueued;

        // Screen-space rects (GUI coordinates, y down) covered by on-screen panels.
        static readonly List<Rect> uiRects = new List<Rect>();
        static int uiRectsFrame = -1;

        public static Vector2 Move
        {
            get
            {
                float x = 0f, y = 0f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
                return Vector2.ClampMagnitude(new Vector2(x, y) + TouchMove, 1f);
            }
        }

        public static bool Run =>
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || TouchRun;

        /// <summary>Returns true once per jump press.</summary>
        public static bool ConsumeJump()
        {
            bool jump = Input.GetKeyDown(KeyCode.Space) || TouchJumpQueued;
            TouchJumpQueued = false;
            return jump;
        }

        /// <summary>Registers a GUI rect (y down) that pointer input should not pass through.</summary>
        public static void RegisterUiRect(Rect guiRect)
        {
            if (uiRectsFrame != Time.frameCount)
            {
                uiRects.Clear();
                uiRectsFrame = Time.frameCount;
            }
            uiRects.Add(guiRect);
        }

        /// <summary>True when a screen position (y up, as in Input.mousePosition) is over a panel.</summary>
        public static bool IsOverUi(Vector2 screenPosition)
        {
            // Rects registered last frame are still valid for this frame's Update.
            if (Time.frameCount - uiRectsFrame > 1) return false;
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            foreach (var rect in uiRects)
            {
                if (rect.Contains(guiPoint)) return true;
            }
            return false;
        }
    }
}
