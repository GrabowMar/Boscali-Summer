using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The overcast look that the capped native conditions no longer carry (spec 8.1). Vanilla greys the fog and kills the sun only above <c>conditions 0.6</c>, which the native
    /// value never reaches, so <c>RainAtmosphere</c> supplies the look: a shade from 0 to 1 by the local opacity-weighted sky cover and a smooth below-deck factor, the grey and
    /// the dim of the air that follow it, and a sun intensity that scales with it. Every constant is a start value tuned on the bench against the old 0.6-0.8 look.
    /// </summary>
    internal static class OvercastLook
    {
        /// <summary>Cover under this casts no shade; the shade reaches 1 at cover 1.</summary>
        public const float CoverFloor = 0.1f;
        /// <summary>Half the width, in metres, of the transition at the top of the deck: full shade below the top less this, none above it plus this.</summary>
        public const float DeckBlend = 450f;
        /// <summary>The sun keeps this share of its intensity under a full shade.</summary>
        public const float SunFloor = 0.15f;
        /// <summary>The fog colour is pulled this far toward its luminance and darkened this much under a full shade.</summary>
        public const float TintGrey = 0.8f, TintDark = 0.35f;

        /// <summary>0-1: how much cloud hangs overhead. Smooth in the cover and in the altitude, so crossing the top of a deck never steps.</summary>
        public static float Shade(float skyCover, float cameraY, float deckTop)
        {
            if (!Scalar.IsFinite(skyCover) || !Scalar.IsFinite(cameraY) || !Scalar.IsFinite(deckTop)) return 0f;
            float cover = Scalar.Clamp01((skyCover - CoverFloor) / (1f - CoverFloor));
            float below = 1f - WeatherMath.Smoothstep(deckTop - DeckBlend, deckTop + DeckBlend, cameraY);
            return cover * below;
        }

        /// <summary>The factor on <c>sun.intensity</c>: 1 in the open, <see cref="SunFloor"/> under a full shade.</summary>
        public static float SunScale(float shade) => 1f - (1f - SunFloor) * Scalar.Clamp01(shade);

        /// <summary>Pulls a fog colour toward its luminance and darkens it by the shade. Identity at 0.</summary>
        public static void Tint(float shade, ref float r, ref float g, ref float b)
        {
            float k = Scalar.Clamp01(shade);
            if (k <= 0f) return;
            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float grey = TintGrey * k, dark = 1f - TintDark * k;
            r = Scalar.Clamp01((r + (lum - r) * grey) * dark);
            g = Scalar.Clamp01((g + (lum - g) * grey) * dark);
            b = Scalar.Clamp01((b + (lum - b) * grey) * dark);
        }
    }
}
