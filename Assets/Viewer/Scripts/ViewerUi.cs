using System.Collections.Generic;
using UnityEngine;

namespace Viewer
{
    // Screen areas covered by the viewer's panels, so a touch or click on a panel does not also move the camera.
    public static class ViewerUi
    {
        public static bool Debug; // ?debug in the page address: input decisions go to the browser console
        static readonly List<Rect> rects = new List<Rect>(); // screen pixels, y up
        static int frame = -1;

        public static void Register(Rect screenRect)
        {
            if (frame != Time.frameCount)
            {
                rects.Clear();
                frame = Time.frameCount;
            }
            rects.Add(screenRect);
        }

        /// <summary>True when a screen position (pixels, y up, as the Input System reports it) is over a panel.</summary>
        public static bool IsOver(Vector2 position)
        {
            if (Time.frameCount - frame > 1) return false; // last frame's panels still count this frame
            foreach (var rect in rects) if (rect.Contains(position)) return true;
            return false;
        }
    }
}
