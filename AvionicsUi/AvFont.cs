using System.Collections.Generic;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Font resolution and caching across avionics panels.
    /// Resolves the vanilla MFD TMP_FontAsset off screen templates or the active scene,
    /// preventing fallback to default generic TMP fonts.
    /// </summary>
    public static class AvFont
    {
        private static TMP_FontAsset font;
        private static float nextScan;
        private static readonly Dictionary<TMP_FontAsset, Material> OutlinedMaterials = new Dictionary<TMP_FontAsset, Material>(2);

        public static TMP_FontAsset Font
        {
            get
            {
                if (font != null) return font;

                // Re-scanning the scene every access is wasted work while nothing has a
                // font yet (e.g. before the first MFD or HUD spawns). Throttle to 1 Hz.
                if (Time.unscaledTime < nextScan) return null;
                nextScan = Time.unscaledTime + 1f;

                // 1. Prioritize resolving from an MFDScreen template/label
                MFDScreen mfdScreen = Object.FindObjectOfType<MFDScreen>();
                if (mfdScreen != null && mfdScreen.label != null && mfdScreen.label.font != null)
                {
                    font = mfdScreen.label.font;
                    return font;
                }

                // 2. Prioritize tactical screen font from active theme system
                try
                {
                    var theme = ThemeManager.Active?.TacScreenTheme;
                    if (theme != null && theme.TextStyles != null)
                    {
                        for (int i = 0; i < theme.TextStyles.Count; i++)
                        {
                            var item = theme.TextStyles[i];
                            if (item?.Style?.Font != null)
                            {
                                font = item.Style.Font;
                                return font;
                            }
                        }
                    }
                }
                catch { }

                // 3. Fall back to any TextMeshProUGUI in the scene
                TMP_Text any = Object.FindObjectOfType<TextMeshProUGUI>();
                if (any != null) font = any.font;
                return font;
            }
            set
            {
                if (value != null) { font = value; nextScan = 0f; }
            }
        }

        public static void Reset()
        {
            font = null;
            nextScan = 0f;
        }

        /// <summary>
        /// One outlined TMP material per font, cached for the process lifetime (fonts are
        /// scene-durable assets, never destroyed under a mod). Every caller of a given font
        /// shares the same material instance; none of them owns or destroys it.
        /// </summary>
        public static Material Outlined(TMP_FontAsset f)
        {
            if (f == null) return null;
            if (OutlinedMaterials.TryGetValue(f, out Material existing) && existing != null) return existing;

            Material source = f.material;
            if (source == null) return null;

            // Only opt into the outline pass when the shader actually exposes it. Forcing the
            // OUTLINE_ON keyword on a TMP shader variant that has no _OutlineWidth/_OutlineColor
            // (e.g. the game's own MFD font material, which differs from the fixture's default TMP
            // asset) leaves the keyword enabled with undefined outline params - on some shader
            // variants that reads back as a washed-out/near-invisible face instead of a no-op.
            Material material = source;
            if (source.HasProperty("_OutlineWidth") && source.HasProperty("_OutlineColor"))
            {
                material = new Material(source);
                material.EnableKeyword("OUTLINE_ON");
                material.SetFloat("_OutlineWidth", 0.18f);
                material.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 0.6f));
            }

            OutlinedMaterials[f] = material;
            return material;
        }
    }
}
