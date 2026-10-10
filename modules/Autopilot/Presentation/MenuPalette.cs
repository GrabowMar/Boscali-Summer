using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// Colour tokens of the v7 menu. Ink, caution and alert follow the player's HUD theme (<see cref="AvTheme"/>: AllClear / Warning / Alert,
    /// with the kit's fixed fallbacks when no theme is up). Everything else is derived from the ink so a re-themed HUD re-themes the menu.
    /// Plates are opaque, text colours are solid, and dimming is by colour token, never by alpha over the native HUD (alpha-blended text only
    /// holds in Gamma space; the game runs Linear).
    /// </summary>
    internal sealed class MenuPalette
    {
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
            // text on the focus fill: the dark ink, and a lighter solid secondary for state lines (the prototype blended 76% ink over the fill, i.e. 24% toward
            // it; the 13 px regular face is thinner than the browser's, so 15% keeps it readable on the neon)
            OnInk = new Rgba(0.008f, 0.102f, 0.047f);
            OnInk2 = new Rgba(OnInk.R + (inkHi.r - OnInk.R) * 0.15f, OnInk.G + (inkHi.g - OnInk.G) * 0.15f, OnInk.B + (inkHi.b - OnInk.B) * 0.15f);
            Line = Rgb(ink, dim).WithAlpha(0.70f);
            Plate = new Rgba(0.008f, 0.071f, 0.039f, 1f); PlateHi = new Rgba(0.004f, 0.094f, 0.051f, 1f);
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
                Color ink = AvTheme.Accent, warn = AvTheme.Warning, alert = AvTheme.Alert;
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
