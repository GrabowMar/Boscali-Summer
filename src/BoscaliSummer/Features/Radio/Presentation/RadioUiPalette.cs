using System;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>A colour value that keeps the radio palette independent of Unity.</summary>
    internal readonly struct RadioRgba
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;
        public readonly float A;

        public RadioRgba(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public RadioRgba WithAlpha(float alpha) => new RadioRgba(R, G, B, alpha);

        public RadioRgba Scaled(float factor) =>
            new RadioRgba(R * factor, G * factor, B * factor, A);

        public RadioRgba Over(RadioRgba background) =>
            new RadioRgba(
                R * A + background.R * (1f - A),
                G * A + background.G * (1f - A),
                B * A + background.B * (1f - A));

        public float RelativeLuminance =>
            0.2126f * Linear(R) + 0.7152f * Linear(G) + 0.0722f * Linear(B);

        public static float Contrast(RadioRgba first, RadioRgba second)
        {
            float high = first.RelativeLuminance;
            float low = second.RelativeLuminance;
            if (high < low)
            {
                float swap = high;
                high = low;
                low = swap;
            }

            return (high + 0.05f) / (low + 0.05f);
        }

        public static RadioRgba Lerp(RadioRgba from, RadioRgba to, float amount) =>
            new RadioRgba(
                from.R + (to.R - from.R) * amount,
                from.G + (to.G - from.G) * amount,
                from.B + (to.B - from.B) * amount,
                from.A + (to.A - from.A) * amount);

        public static RadioRgba Shade(float alpha) => new RadioRgba(0f, 0f, 0f, alpha);

        public static RadioRgba White => new RadioRgba(1f, 1f, 1f);

        private static float Linear(float channel) =>
            channel <= 0.03928f
                ? channel / 12.92f
                : (float)Math.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    internal enum RadioButtonStyle
    {
        Default,
        Primary,
        Quiet,
        Toggle
    }

    internal readonly struct RadioUiPaint
    {
        public readonly RadioRgba Fill;
        public readonly RadioRgba Text;

        public RadioUiPaint(RadioRgba fill, RadioRgba text)
        {
            Fill = fill;
            Text = text;
        }
    }

    /// <summary>Radio MFD colours and button fill/text for each interaction state.</summary>
    internal static class RadioUiPalette
    {
        public static readonly RadioRgba PanelGround =
            new RadioRgba(0.022f, 0.040f, 0.032f, 0.965f);
        public static readonly RadioRgba PanelEdge =
            new RadioRgba(0.18f, 0.55f, 0.38f);
        public static readonly RadioRgba Frame =
            new RadioRgba(0.18f, 0.65f, 0.42f, 0.35f);
        public static readonly RadioRgba Dim =
            new RadioRgba(0.62f, 0.72f, 0.67f);
        public static readonly RadioRgba Disabled =
            new RadioRgba(0.34f, 0.44f, 0.40f, 0.75f);

        // 3-layer design tokens (Surfaces, Borders, Rails, Typography)
        public static readonly RadioRgba SurfaceCard = new RadioRgba(0.038f, 0.080f, 0.060f, 0.88f);
        public static readonly RadioRgba BorderSubtle = new RadioRgba(0.18f, 0.65f, 0.42f, 0.30f);
        public static readonly RadioRgba RailEmerald = new RadioRgba(0.000f, 1.000f, 0.616f);
        public static readonly RadioRgba RailCyan = new RadioRgba(0.000f, 0.898f, 1.000f);
        public static readonly RadioRgba TextPrimary = new RadioRgba(0.92f, 1.00f, 0.96f);

        public static RadioUiPaint Paint(
            RadioButtonStyle style, RadioRgba accent,
            bool enabled, bool latched, bool hover, bool pressed)
        {
            if (!enabled)
                return new RadioUiPaint(RadioRgba.Shade(0.18f), Disabled);

            if (pressed)
                return new RadioUiPaint(Wash(accent, 0.52f, 0.90f), RadioRgba.White);

            if (latched)
            {
                float scale = hover ? 0.42f : 0.34f;
                float alpha = hover ? 0.86f : 0.82f;
                return new RadioUiPaint(Wash(accent, scale, alpha), RadioRgba.White);
            }

            switch (style)
            {
                case RadioButtonStyle.Primary:
                    return new RadioUiPaint(
                        hover ? Wash(accent, 0.25f, 0.76f) : Wash(accent, 0.16f, 0.64f),
                        hover ? RadioRgba.White : RadioRgba.Lerp(accent, RadioRgba.White, 0.35f));

                case RadioButtonStyle.Quiet:
                    return new RadioUiPaint(
                        hover ? Wash(accent, 0.18f, 0.58f) : RadioRgba.Shade(0.20f),
                        hover ? accent : Dim);

                default:
                    return new RadioUiPaint(
                        hover ? Wash(accent, 0.23f, 0.68f) : RadioRgba.Shade(0.28f),
                        hover ? RadioRgba.White : accent);
            }
        }

        public static RadioRgba Wash(RadioRgba accent, float scale, float alpha) =>
            accent.Scaled(scale).WithAlpha(alpha);
    }
}
