using System;
using BoscaliSummer.Core;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The thermodynamics. Closed-form and pure: every value here is a function of the
    /// arguments alone, so the host, a client and a late joiner all derive the same atmosphere
    /// from the mission clock and nothing has to be transmitted.
    ///
    /// Nothing here throws and nothing returns NaN. Every division is guarded and every result
    /// is clamped, because these values feed the vanilla sky and a NaN reaching
    /// <c>LevelInfo.conditions</c> would break the world for every peer.
    /// </summary>
    internal static class Thermo
    {
        /// <summary>Metres of lift per degree of dewpoint spread, the standard rule of thumb.</summary>
        private const float MetresPerDegree = 125f;

        private const float MaxLcl = 4000f;

        /// <summary>
        /// Lifting condensation level: the height a parcel has to rise before it saturates, and
        /// therefore the cloud base. Saturated air has cloud on the deck.
        /// </summary>
        public static float Lcl(float temperatureC, float dewpointC)
        {
            if (!Finite(temperatureC) || !Finite(dewpointC)) return 1200f;
            float spread = temperatureC - dewpointC;
            if (spread <= 0f) return 0f;
            float lcl = spread * MetresPerDegree;
            return lcl > MaxLcl ? MaxLcl : lcl;
        }

        /// <summary>
        /// Convective available potential energy, normalised. Needs all three: an unstable air
        /// mass, moisture in the column, and surface heating to release it. A hot dry air mass
        /// has the heat and none of the moisture, which is why it does not storm. Scaled so a
        /// strongly unstable maritime air mass reaches the top of the range — the severe
        /// threshold is only meaningful if the scale can actually reach it.
        /// </summary>
        public static float Cape(float temperatureC, float dewpointC, float stability, float heating)
        {
            if (!Finite(temperatureC) || !Finite(dewpointC)) return 0f;
            float moisture = Moisture(temperatureC, dewpointC);
            float heat = WeatherRegimes.Clamp01(heating);
            float instability = WeatherRegimes.Clamp01(stability);
            return WeatherRegimes.Clamp01(instability * (0.25f + 0.75f * heat) * (0.25f + 0.75f * moisture));
        }

        /// <summary>
        /// Convective inhibition: what is holding the energy down. A stable air mass caps hard;
        /// so does a cold surface with no heating. Forcing from an active front breaks the cap,
        /// which is why a cold front fires in the afternoon and a ridge does not.
        /// </summary>
        public static float Cin(float stability, float heating, float forcing)
        {
            float instability = WeatherRegimes.Clamp01(stability);
            float heat = WeatherRegimes.Clamp01(heating);
            float lift = WeatherRegimes.Clamp01(forcing);
            float cap = (1f - instability) * 0.85f + (1f - heat) * 0.25f;
            return WeatherRegimes.Clamp01(cap - lift * 0.90f);
        }

        /// <summary>
        /// Vertical wind shear, normalised. Shear is what decides whether convection organises
        /// into a line or stays a single cell, so it matters as much as the energy does.
        /// </summary>
        public static float Shear(float windSpeed, float frontActivity, float cape)
        {
            float speed = windSpeed > 0f && Finite(windSpeed) ? windSpeed : 0f;
            return WeatherRegimes.Clamp01(
                WeatherRegimes.Clamp01(speed / 30f) * 0.55f +
                WeatherRegimes.Clamp01(frontActivity) * 0.30f +
                WeatherRegimes.Clamp01(cape) * 0.25f);
        }

        /// <summary>
        /// Gust peak. There is no gust channel in vanilla weather, so this rides inside the
        /// turbulence channel — which is the right place for it, because a gust is turbulence.
        /// </summary>
        public static float GustSpeed(float windSpeed, float cape)
        {
            float speed = windSpeed > 0f && Finite(windSpeed) ? windSpeed : 0f;
            float energy = WeatherRegimes.Clamp01(cape);
            return speed * (1f + 0.85f * energy) + 2f * energy;
        }

        /// <summary>
        /// Precipitation rate. A capped column does not rain however much energy it has, so the
        /// free convection term is CAPE less CIN and the layered term needs front forcing.
        /// </summary>
        public static float RainRate(float cape, float cin, float frontInfluence, float cellInfluence)
        {
            float free = WeatherRegimes.Clamp01(WeatherRegimes.Clamp01(cape) - WeatherRegimes.Clamp01(cin));
            float convective = WeatherRegimes.Clamp01(cellInfluence) * (0.35f + 0.65f * free);
            float layered = WeatherRegimes.Clamp01(frontInfluence) * (0.15f + 0.65f * free);
            return convective > layered ? convective : layered;
        }

        /// <summary>
        /// Slant visibility in metres. Rain, fog and smoke all extinguish it, but fog is nearly
        /// a step function and the others are not, so it is weighted quadratically. A pilot
        /// cannot land in a hundred metres of visibility, and this is the term that says so.
        /// </summary>
        public static float Visibility(float rainRate, float dewpointSpread, float haze)
        {
            float rain = WeatherRegimes.Clamp01(rainRate);
            float smoke = WeatherRegimes.Clamp01(haze);
            float spread = Finite(dewpointSpread) && dewpointSpread > 0f ? dewpointSpread : 0f;
            float fog = spread >= FogSpread ? 0f : 1f - spread / FogSpread;
            float extinction = 0.35f * rain + 0.15f * rain * rain + 2.2f * fog * fog + 0.8f * smoke;
            float metres = ClearVisibility / (1f + 22f * extinction);
            if (metres < MinVisibility) return MinVisibility;
            return metres > ClearVisibility ? ClearVisibility : metres;
        }

        /// <summary>Relative humidity from the Magnus approximation, 0..1.</summary>
        public static float RelativeHumidity(float temperatureC, float dewpointC)
        {
            if (!Finite(temperatureC) || !Finite(dewpointC)) return 0f;
            float saturation = VapourPressure(temperatureC);
            if (saturation <= 0f) return 0f;
            return WeatherRegimes.Clamp01(VapourPressure(dewpointC) / saturation);
        }

        /// <summary>How much water the column is holding: 1 at saturation, 0 bone dry.</summary>
        private static float Moisture(float temperatureC, float dewpointC)
        {
            float spread = temperatureC - dewpointC;
            if (spread <= 0f) return 1f;
            return WeatherRegimes.Clamp01(1f - spread / 22f);
        }

        private static float VapourPressure(float temperatureC) =>
            6.112f * (float)Math.Exp(17.67 * temperatureC / (temperatureC + 243.5f));

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>Below this spread fog forms; above it there is no fog term at all.</summary>
        private const float FogSpread = 3f;

        private const float ClearVisibility = 16000f;

        private const float MinVisibility = 120f;
    }
}
