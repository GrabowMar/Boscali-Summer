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

        /// <summary>
        /// The fog colours at night (RainAtmosphere): vanilla's night fog is a saturated teal (blue up to six times the red) about seven times brighter
        /// than its own night sky at the horizon; distant cloud and the far sea dissolve into it, and from above a storm deck the far deck glowed as a
        /// cyan band, from under it the far sea (2026-10-19). After sunset
        /// (<paramref name="sunHeight"/> is the sine of the sun's elevation; full from about 7 degrees down) it turns grey and takes the overcast darkening,
        /// less what the fog already took under a deck (<paramref name="shade"/>), so the two never stack. Identity with the sun up; no clamp (the fog is HDR at altitude).
        /// </summary>
        public static void NightHaze(float sunHeight, float shade, ref float r, ref float g, ref float b)
        {
            if (!Scalar.IsFinite(sunHeight) || !Scalar.IsFinite(shade)) return;
            float night = 1f - WeatherMath.Smoothstep(-0.12f, 0f, sunHeight);
            if (night <= 0f) return;
            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float dark = 1f - TintDark * night * (1f - Scalar.Clamp01(shade));
            r = (r + (lum - r) * night) * dark;
            g = (g + (lum - g) * night) * dark;
            b = (b + (lum - b) * night) * dark;
        }

        /// <summary>Night light keeps this share under a new moon and <see cref="NightLightFull"/> under a full one (vanilla moon intensity 0.5).</summary>
        public const float NightLightNew = 0.35f, NightLightFull = 0.4f;
        /// <summary>How far the night ambient is pulled to a moonlit blue-grey (scotopic vision: colour fades, blues hold).</summary>
        public const float NightBlue = 0.6f;

        /// <summary>0 by day, 1 from about 7 degrees below the horizon; the same ramp as <see cref="NightHaze"/>.</summary>
        public static float Night(float sunHeight) =>
            Scalar.IsFinite(sunHeight) ? 1f - WeatherMath.Smoothstep(-0.12f, 0f, sunHeight) : 0f;

        /// <summary>
        /// Dark nights (2026-10-26): the factor on the night ambient, fog colour and sky dome. Vanilla lifts the night ambient by its gradients and adds the moon on top,
        /// so a moonless night reads as grey dusk. 1 by day; <see cref="NightLightNew"/> to <see cref="NightLightFull"/> at night by the moon's <c>Light.intensity</c>.
        /// </summary>
        public static float NightLight(float sunHeight, float moon)
        {
            float night = Night(sunHeight);
            if (night <= 0f) return 1f;
            float full = Scalar.IsFinite(moon) ? Scalar.Clamp01(moon / 0.5f) : 0f;
            float floor = NightLightNew + (NightLightFull - NightLightNew) * full;
            return 1f + (floor - 1f) * night;
        }

        /// <summary>Night ambient colour: scaled by <see cref="NightLight"/> and pulled toward a moonlit blue-grey of the same luminance. Identity by day; no clamp.</summary>
        public static void NightAmbient(float sunHeight, float moon, ref float r, ref float g, ref float b)
        {
            float night = Night(sunHeight);
            if (night <= 0f) return;
            float k = NightLight(sunHeight, moon), pull = NightBlue * night;
            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            // Blue-grey of luminance 1: 0.2126*0.78 + 0.7152*1.02 + 0.0722*1.5 = 1.0.
            r = (r + (lum * 0.78f - r) * pull) * k;
            g = (g + (lum * 1.02f - g) * pull) * k;
            b = (b + (lum * 1.5f - b) * pull) * k;
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
