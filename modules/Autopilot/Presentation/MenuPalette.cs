using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// Colour tokens of the interaction menu, using the same semantic roles as the avionics consoles.
    /// Ring plates are opaque, the dock tables and bars are translucent (<see cref="Panel"/>), text colours are solid, and dimming is by colour token, never by alpha over the native HUD (alpha-blended text only
    /// holds in Gamma space; the game runs Linear).
    /// </summary>
    internal sealed class MenuPalette
    {
        private static MenuPalette cached, cachedDim;
        private static Color cachedInk, cachedWarn, cachedAlert;
        private static int frame = -1, generation = -1;

        public readonly Rgba Ink, InkHi, Ink2, InkDim, Hair, Txt, OnInk, OnInk2, Line, Plate, PlateHi, Panel, Scrim, Amber, Red, Casing, Hub, Glow, Flash;

        private MenuPalette(Color ink, Color warn, Color alert, float dim)
        {
            Color inkHi = Scale(ink, 0.99f / Mathf.Max(0.001f, Mathf.Max(ink.r, Mathf.Max(ink.g, ink.b))));
            Ink = Rgb(ink, dim); InkHi = Rgb(inkHi, 1f); Ink2 = Rgb(AvInk.Dim, dim);
            InkDim = Rgb(AvInk.Muted, 1f);
            Hair = Rgb(AvInk.Hairline, 1f).WithAlpha(0.45f);
            Txt = Rgb(AvInk.Ink, dim);
            // text on the focus fill: the dark ink, and a lighter solid secondary for state lines (the prototype blended 76% ink over the fill, i.e. 24% toward
            // it; the 13 px regular face is thinner than the browser's, so 15% keeps it readable on the neon)
            OnInk = Rgb(AvInk.Ground, 1f);
            OnInk2 = new Rgba(OnInk.R + (inkHi.r - OnInk.R) * 0.15f, OnInk.G + (inkHi.g - OnInk.G) * 0.15f, OnInk.B + (inkHi.b - OnInk.B) * 0.15f);
            Line = Rgb(AvInk.Frame, dim).WithAlpha(0.70f);
            Plate = Rgb(AvInk.Surface, 1f); PlateHi = Rgb(AvInk.Raised, 1f);
            // the dock tables, the title and the help bar let the cockpit through; the grey scrim under everything keeps their text readable
            Panel = Plate.WithAlpha(0.84f);
            Scrim = Rgb(AvInk.Ground, 1f).WithAlpha(0.30f);
            Amber = Rgb(warn, 1f); Red = Rgb(alert, 1f);
            Casing = Rgb(AvInk.Ground, 1f).WithAlpha(0.90f);
            Hub = Rgb(AvInk.Ground, 1f);
            Glow = Rgb(inkHi, 1f).WithAlpha(0.35f);
            Flash = Rgb(AvInk.Ink, 1f);
        }

        /// <summary>The live palette, read from the theme once per frame.</summary>
        public static MenuPalette Get(bool dimmed = false)
        {
            if (frame != Time.frameCount || cached == null || generation != AvStyleHost.FuiGeneration)
            {
                frame = Time.frameCount;
                Color ink = AvInk.Select, warn = AvInk.State(AvState.Caution), alert = AvInk.State(AvState.Danger);
                if (cached == null || generation != AvStyleHost.FuiGeneration || ink != cachedInk || warn != cachedWarn || alert != cachedAlert)
                {
                    generation = AvStyleHost.FuiGeneration;
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
