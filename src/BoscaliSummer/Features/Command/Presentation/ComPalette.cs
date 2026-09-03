using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation
{
    internal static class ComPalette
    {
        // Surfaces
        public static readonly Color SurfaceScreen = new Color(0.022f, 0.040f, 0.032f, 0.96f);
        public static readonly Color SurfaceCard = new Color(0.038f, 0.080f, 0.060f, 0.88f);
        public static readonly Color SurfaceCardHover = new Color(0.058f, 0.120f, 0.088f, 0.95f);
        public static readonly Color SurfaceActive = new Color(0.04f, 0.15f, 0.09f, 0.88f);
        public static readonly Color Frame = new Color(0.18f, 0.65f, 0.42f, 0.35f);
        public static readonly Color BorderSubtle = new Color(0.18f, 0.65f, 0.42f, 0.30f);

        // Status rails & Tactical Accents
        public static readonly Color HudEmerald = new Color(0.000f, 1.000f, 0.616f, 1f);
        public static readonly Color ThreatAmber = new Color(0.961f, 0.620f, 0.043f, 1f);
        public static readonly Color AlertRed = new Color(0.937f, 0.267f, 0.267f, 1f);
        public static readonly Color InfoCyan = new Color(0.000f, 0.898f, 1.000f, 1f);

        // Type
        public static readonly Color TextPrimary = new Color(0.92f, 1.00f, 0.96f, 1f);
        public static readonly Color TextDim = new Color(0.48f, 0.70f, 0.62f, 1f);

        // Typography scale
        public const float FontTitle = 16f;
        public const float FontLead = 13f;
        public const float FontSmall = 11f;
        public const float FontMicro = 10f;
        public const float FontNano = 9f;

        public static Color WithAlpha(Color color, float alpha) =>
            new Color(color.r, color.g, color.b, alpha);
    }
}
