using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Font with Cyrillic glyphs for the HUD and world labels. WebGL has no system fonts to
    /// fall back on, so Unity's built-in font renders Russian text as blanks there.
    /// </summary>
    public static class PlaygroundFont
    {
        const string ResourcePath = "Fonts/LiberationSans-Regular";

        static Font font;

        public static Font Get()
        {
            if (font == null) font = Resources.Load<Font>(ResourcePath);
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }

        /// <summary>Makes every style of the current GUI skin without its own font use this one.</summary>
        public static void ApplyToGui()
        {
            Font current = Get();
            if (GUI.skin.font != current) GUI.skin.font = current;
        }
    }
}
