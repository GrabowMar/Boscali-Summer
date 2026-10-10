using NOAvionics;
using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// Colour tokens of the v7 menu. Ink, caution and alert follow the player's HUD theme
    /// (<c>ThemeManager.Active.ColorTheme</c> AllClear / Warning / Alert); without a theme (menus, the offline check, the editor stub
    /// that throws) they are the base game's default green, amber and red. Everything else is derived from the ink so a re-themed HUD
    /// re-themes the menu. Plates are opaque and dimming is by colour token, never by alpha over the native HUD.
    /// </summary>
    internal sealed class MenuPalette
    {
        private static readonly Color FallbackInk = new Color(0.263f, 0.804f, 0.325f);
        private static readonly Color FallbackWarn = new Color(1f, 0.702f, 0f);
        private static readonly Color FallbackAlert = new Color(1f, 0.302f, 0.239f);
        private static MenuPalette cached, cachedDim;
        private static Color cachedInk, cachedWarn, cachedAlert;
        private static int frame = -1;

        public readonly Rgba Ink, InkHi, Ink2, InkDim, Hair, Txt, OnInk, OnInk2, Line, Plate, PlateHi, Amber, Red, Casing, Hub, Glow, Flash;

        private MenuPalette(Color ink, Color warn, Color alert, float dim)
        {
            Color inkHi = Scale(ink, 0.99f / Mathf.Max(ink.r, Mathf.Max(ink.g, ink.b)));
            Color txt = Color.Lerp(ink, Color.white, 0.65f);
            Ink = Rgb(ink, dim); InkHi = Rgb(inkHi, 1f); Ink2 = Rgb(Color.Lerp(ink, Color.white, 0.1f), dim);
            InkDim = Rgb(Color.Lerp(ink, new Color(0.3f, 0.4f, 0.34f), 0.45f), 1f);
            Hair = Rgb(ink, 1f).WithAlpha(0.30f);
            Txt = Rgb(txt, dim);
            OnInk = new Rgba(0.008f, 0.102f, 0.047f); OnInk2 = OnInk.WithAlpha(0.76f);
            Line = Rgb(ink, dim).WithAlpha(0.70f);
            Plate = new Rgba(0.008f, 0.071f, 0.039f, 0.93f); PlateHi = new Rgba(0.004f, 0.094f, 0.051f, 0.96f);
            Amber = Rgb(warn, 1f); Red = Rgb(alert, 1f);
            Casing = new Rgba(0.004f, 0.063f, 0.039f, 0.85f);
            Hub = new Rgba(0.008f, 0.059f, 0.031f, 1f);
            Glow = Rgb(inkHi, 1f).WithAlpha(0.35f);
            Flash = new Rgba(0.918f, 1f, 0.941f, 1f);
        }

        /// <summary>The live palette, read from the theme once per frame.</summary>
        public static MenuPalette Get(bool dimmed = false)
        {
            if (frame != Time.frameCount || cached == null)
            {
                frame = Time.frameCount;
                Color ink = FallbackInk, warn = FallbackWarn, alert = FallbackAlert;
                try
                {
                    var theme = ThemeManager.Active.ColorTheme;
                    ink = theme.AllClear; warn = theme.Warning; alert = theme.Alert;
                }
                catch (System.Exception) { /* no theme up: the fallback ink */ }
                if (cached == null || ink != cachedInk || warn != cachedWarn || alert != cachedAlert)
                {
                    cachedInk = ink; cachedWarn = warn; cachedAlert = alert;
                    cached = new MenuPalette(ink, warn, alert, 1f);
                    cachedDim = new MenuPalette(ink, warn, alert, 0.8f);
                }
            }
            return dimmed ? cachedDim : cached;
        }

        public static Color ToColor(Rgba c) => new Color(c.R, c.G, c.B, c.A);

        private static Rgba Rgb(Color c, float scale) => new Rgba(c.r * scale, c.g * scale, c.b * scale, 1f);

        private static Color Scale(Color c, float k) => new Color(Mathf.Min(1f, c.r * k), Mathf.Min(1f, c.g * k), Mathf.Min(1f, c.b * k), 1f);

        /// <summary>A colour pulled toward the plate: how the ring key-caps fade while a dock is up (opacity without alpha).</summary>
        public Rgba Fade(Rgba c, float opacity) =>
            new Rgba(Plate.R + (c.R - Plate.R) * opacity, Plate.G + (c.G - Plate.G) * opacity, Plate.B + (c.B - Plate.B) * opacity, 1f);
    }
}
