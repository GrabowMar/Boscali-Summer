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

        /// <summary>WCAG relative luminance, treating channels as sRGB.</summary>
        public float RelativeLuminance =>
            0.2126f * Linear(R) + 0.7152f * Linear(G) + 0.0722f * Linear(B);

        private static float Linear(float c) =>
            c <= 0.03928f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);

        /// <summary>
        /// WCAG contrast between two opaque colours. Composite translucent colours over
        /// their background first.
        /// </summary>
        public static float Contrast(Rgba a, Rgba b)
        {
            float la = a.RelativeLuminance;
            float lb = b.RelativeLuminance;
            if (la < lb) { float t = la; la = lb; lb = t; }
            return (la + 0.05f) / (lb + 0.05f);
        }
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

        // ----------------------------------------------------------------------- text
        public static readonly Rgba TextPrimary = new Rgba(0.929f, 0.973f, 0.980f, 1f);
        public static readonly Rgba TextDim = new Rgba(0.737f, 0.847f, 0.871f, 1f);
        // Secondary/disabled copy still has to be readable on a moving tactical map. Its
        // quieter role comes from weight, framing and control state rather than low opacity.
        public static readonly Rgba TextMuted = new Rgba(0.467f, 0.588f, 0.631f, 1f);
        public static readonly Rgba TextInk = new Rgba(0.004f, 0.047f, 0.024f, 1f); // #010C06 optical dark ink for solid active button plates
        public static readonly Rgba RailInfo = new Rgba(0.500f, 0.760f, 0.850f, 1f);
        public static readonly Rgba RailInert = new Rgba(0.239f, 0.337f, 0.376f, 0.650f);
        public const float Space2 = 8f;
        public const float RowHeight = 30f;

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
        /// The left bay runs from the canvas's bottom margin up to under the news ticker:
        /// 1026px at 1080p (<see cref="BayReserve"/>). The map's old left log panel took the
        /// top of it; the log is on EVN LOG now, so a panel fills the bay.
        /// <see cref="PanelHeight"/> stays the floor and the fallback, so a screen with no
        /// measurable canvas is exactly what it always was.
        /// </summary>
        public const float PanelHeightMax = 1026f;

        /// <summary>What the bay keeps off the canvas height: the news ticker (8 margin + 30), one 8 px gutter under it, and the 8 px
        /// bottom margin.</summary>
        public const float BayReserve = 54f;
    }
}
