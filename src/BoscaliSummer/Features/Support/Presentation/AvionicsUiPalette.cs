using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// Design tokens for the OPS cockpit MFD page. Every token here is consumed by
    /// <see cref="SupportPanel"/>, which no longer keeps private copies of the same values.
    /// </summary>
    internal static class AvionicsUiPalette
    {
        // Surfaces
        public static readonly Color SurfaceScreen = new Color(0.02f, 0.04f, 0.05f, 0.96f);
        public static readonly Color SurfaceCard = new Color(0.071f, 0.098f, 0.133f, 0.88f);
        public static readonly Color SurfaceCardHover = new Color(0.094f, 0.133f, 0.188f, 0.95f);
        public static readonly Color SurfaceActive = new Color(0.04f, 0.12f, 0.07f, 0.85f);
        public static readonly Color SurfaceInert = new Color(0.03f, 0.05f, 0.06f, 0.60f);
        public static readonly Color SurfaceRibbon = new Color(0.04f, 0.08f, 0.09f, 0.90f);
        public static readonly Color Frame = new Color(0.18f, 0.28f, 0.30f, 0.85f);

        // Status rails
        public static readonly Color RailEmerald = new Color(0f, 1f, 0.4f, 1f);
        public static readonly Color RailAmber = new Color(0.961f, 0.620f, 0.043f, 1f);
        public static readonly Color RailCyan = new Color(0.024f, 0.714f, 0.831f, 1f);
        public static readonly Color RailInert = new Color(0.2f, 0.28f, 0.35f, 0.5f);

        // Type
        public static readonly Color TextPrimary = new Color(0.973f, 0.980f, 0.988f, 1f);
        public static readonly Color TextDim = new Color(0.55f, 0.62f, 0.65f, 1f);
        public static readonly Color TextWarning = new Color(1f, 0.72f, 0.25f, 1f);

        // Spacing, on an 4pt rhythm
        public const float Space1 = 4f;
        public const float Space2 = 8f;
        public const float Space3 = 12f;
        public const float Space4 = 16f;
        public const float Space5 = 20f;

        public const float FontTitle = 16f;
        public const float FontLead = 13f;
        public const float FontSmall = 11f;
        public const float FontMicro = 10f;
        public const float FontNano = 9f;

        public static Color WithAlpha(Color color, float alpha) =>
            new Color(color.r, color.g, color.b, alpha);
    }
}
