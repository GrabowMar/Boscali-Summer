using System;

namespace BoscaliSummer.Features.Support.Domain.Layout
{
    /// <summary>WCAG relative luminance and contrast, on straight sRGB channels.</summary>
    internal static class Contrast
    {
        public static float Ratio(float r1, float g1, float b1, float r2, float g2, float b2)
        {
            float lighter = Luminance(r1, g1, b1);
            float darker = Luminance(r2, g2, b2);
            if (darker > lighter)
            {
                float swap = lighter;
                lighter = darker;
                darker = swap;
            }
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        public static float Luminance(float r, float g, float b) =>
            0.2126f * Channel(r) + 0.7152f * Channel(g) + 0.0722f * Channel(b);

        private static float Channel(float value)
        {
            if (value <= 0.03928f) return value / 12.92f;
            return (float)Math.Pow((value + 0.055f) / 1.055f, 2.4);
        }
    }
}
