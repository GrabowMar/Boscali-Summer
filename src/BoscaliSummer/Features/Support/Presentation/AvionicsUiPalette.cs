using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>OPS MFD colours, type scale, and spacing.</summary>
    internal static class AvionicsUiPalette
    {
        // Surfaces
        public static readonly Color SurfaceScreen = new Color(0.022f, 0.040f, 0.032f, 0.96f);
        public static readonly Color SurfaceCard = new Color(0.038f, 0.080f, 0.060f, 0.88f);
        public static readonly Color SurfaceCardHover = new Color(0.058f, 0.120f, 0.088f, 0.95f);
        public static readonly Color SurfaceActive = new Color(0.04f, 0.15f, 0.09f, 0.88f);
        public static readonly Color SurfaceInert = new Color(0.025f, 0.05f, 0.04f, 0.60f);
        public static readonly Color SurfaceRibbon = new Color(0.03f, 0.07f, 0.055f, 0.92f);
        public static readonly Color Frame = new Color(0.18f, 0.65f, 0.42f, 0.35f);
        public static readonly Color BorderSubtle = new Color(0.18f, 0.65f, 0.42f, 0.30f);

        // Status rails
        public static readonly Color RailEmerald = new Color(0.000f, 1.000f, 0.616f, 1f);
        public static readonly Color RailAmber = new Color(0.961f, 0.620f, 0.043f, 1f);
        public static readonly Color RailCyan = new Color(0.000f, 0.898f, 1.000f, 1f);
        public static readonly Color RailInert = new Color(0.15f, 0.30f, 0.24f, 0.5f);

        // Type
        public static readonly Color TextPrimary = new Color(0.92f, 1.00f, 0.96f, 1f);
        public static readonly Color TextDim = new Color(0.48f, 0.70f, 0.62f, 1f);
        public static readonly Color TextWarning = new Color(1f, 0.72f, 0.25f, 1f);

        // Spacing, on an 4pt rhythm
        public const float Space2 = 8f;
        public const float Space3 = 12f;
        public const float Space4 = 16f;

        public const float FontTitle = 16f;
        public const float FontLead = 13f;
        public const float FontSmall = 11f;
        public const float FontMicro = 10f;
        public const float FontNano = 9f;

        public static Color WithAlpha(Color color, float alpha) =>
            new Color(color.r, color.g, color.b, alpha);
    }
}
