using System.Collections.Generic;
using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Screen areas covered by on-screen panels, so a touch or click on a panel does not also
    /// move the camera.
    /// </summary>
    public static class PlaygroundInput
    {
        // Screen-space rects (GUI coordinates, y down) covered by on-screen panels.
        static readonly List<Rect> uiRects = new List<Rect>();
        static int uiRectsFrame = -1;

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
