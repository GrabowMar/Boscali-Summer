using System;

namespace NOAvionics
{
    /// <summary>
    /// An engine-free sRGB colour representation used for UI calculations, contrast validation,
    /// and palette rendering across all Nuclear Option avionics surfaces.
    /// </summary>
    public readonly struct Rgba
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;
        public readonly float A;

        public Rgba(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public Rgba WithAlpha(float a) => new Rgba(R, G, B, a);

        /// <summary>Composite this colour, at its own alpha, over an opaque background.</summary>
        public Rgba Over(Rgba background) =>
            new Rgba(R * A + background.R * (1f - A),
                     G * A + background.G * (1f - A),
                     B * A + background.B * (1f - A),
                     1f);

        /// <summary>WCAG relative luminance, treating channels as sRGB.</summary>
        public float RelativeLuminance =>
            0.2126f * Linear(R) + 0.7152f * Linear(G) + 0.0722f * Linear(B);

        private static float Linear(float c) =>
            c <= 0.03928f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);

        /// <summary>
        /// WCAG contrast between two opaque colours. Composite translucent colours with
        /// <see cref="Over"/> first.
        /// </summary>
        public static float Contrast(Rgba a, Rgba b)
        {
            float la = a.RelativeLuminance;
            float lb = b.RelativeLuminance;
            if (la < lb) { float t = la; la = lb; lb = t; }
            return (la + 0.05f) / (lb + 0.05f);
        }

        public static Rgba White => new Rgba(1f, 1f, 1f);
    }

    /// <summary>How a control is weighted against the others around it.</summary>
    public enum AvButtonStyle
    {
        /// <summary>An ordinary action. Neutral until pointed at or selected.</summary>
        Default,

        /// <summary>The primary action of a page. Carries a fill at rest.</summary>
        Primary,

        /// <summary>Plumbing: page arrows, steppers, toggles. Recedes until pointed at.</summary>
        Quiet,

        /// <summary>Destructive action: removes or aborts. Turns alert-coloured under pointer.</summary>
        Danger,

        /// <summary>Page selector: lit and underlined with a bottom rail while active.</summary>
        Tab,

        /// <summary>Multi-state selection toggle (e.g. formation shapes, radio repeat/shuffle).</summary>
        Toggle,
    }

    /// <summary>
    /// Shared pure design tokens for 5th-generation fighter avionics MFDs in Nuclear Option.
    /// Follows the 60-30-10 rule and strict WCAG AA (≥4.5:1) readability floors.
    /// </summary>
    public static class AvTokens
    {
        // ------------------------------------------------------------------- surfaces
        // Neutral instrument surfaces. The game's theme supplies the operational colours.
        public static readonly Rgba Ground = new Rgba(0.020f, 0.039f, 0.059f, 0.985f);
        public static readonly Rgba Surface = new Rgba(0.039f, 0.078f, 0.110f, 0.980f);
        public static readonly Rgba SurfaceRaised = new Rgba(0.071f, 0.133f, 0.173f, 0.980f);
        public static readonly Rgba SurfaceInert = new Rgba(0.031f, 0.071f, 0.094f, 0.960f);
        public static readonly Rgba Hairline = new Rgba(0.212f, 0.376f, 0.427f, 0.750f);
        public static readonly Rgba Frame = new Rgba(0.353f, 0.620f, 0.686f, 0.900f);
        public static readonly Rgba PanelEdge = new Rgba(0.260f, 0.490f, 0.550f, 0.900f);
        public static readonly Rgba PanelShadow = new Rgba(0.005f, 0.008f, 0.014f, 1f);
        public static readonly Rgba HudPanel = new Rgba(0.015f, 0.025f, 0.038f, 0.850f);

        // ----------------------------------------------------------------------- text
        public static readonly Rgba TextPrimary = new Rgba(0.929f, 0.973f, 0.980f, 1f);
        public static readonly Rgba TextDim = new Rgba(0.737f, 0.847f, 0.871f, 1f);
        // Secondary/disabled copy still has to be readable on a moving tactical map. Its
        // quieter role comes from weight, framing and control state rather than low opacity.
        public static readonly Rgba TextMuted = new Rgba(0.467f, 0.588f, 0.631f, 1f);
        public static readonly Rgba TextInk = new Rgba(0.004f, 0.047f, 0.024f, 1f); // #010C06 optical dark ink for solid active button plates

        // ---------------------------------------------------------------- status rails
        public static readonly Rgba RailReady = new Rgba(0.420f, 0.830f, 0.620f, 1f);
        public static readonly Rgba RailCaution = new Rgba(1.000f, 0.760f, 0.320f, 1f);
        public static readonly Rgba RailDanger = new Rgba(1.000f, 0.380f, 0.400f, 1f);
        public static readonly Rgba RailInfo = new Rgba(0.500f, 0.760f, 0.850f, 1f);
        public static readonly Rgba RailInert = new Rgba(0.239f, 0.337f, 0.376f, 0.650f);
        public static Rgba Dim => TextDim;
        public static Rgba Disabled => TextMuted;
        public const float Space2 = 8f;

        public const float Pad = 14f;
        public const float Gap = 8f;
        public const float RowHeight = 30f;
        public const float RowPitch = 32f;

        // ----------------------------------------------------------------- typography
        public const float FontTitle = 16f;
        public const float FontLead = 13f;
        public const float FontBody = 12f;
        public const float FontSmall = 11f;
        public const float FontMicro = 10f;

        // --------------------------------------------------------------------- layout
        public const float PanelWidth = 480f;
        public const float PanelHeight = 596f;

        /// <summary>
        /// The tallest a panel may grow when its column has the room.
        ///
        /// The left bay is about 918px at 1080p once the mission clock and the spawn strip
        /// are reserved, and the panels used to take 596 of it. This sits a module below
        /// that bay so a panel cannot overhang the strip it was reserved away from.
        /// <see cref="PanelHeight"/> stays the floor and the fallback, so a screen with no
        /// measurable parent is exactly what it always was.
        /// </summary>
        public const float PanelHeightMax = 896f;
    }
}
