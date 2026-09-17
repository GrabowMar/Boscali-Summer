using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The daily rhythm: surface heating during the day builds convection and mixes the
    /// boundary layer, and at night the surface cools, the layer collapses and dewpoint
    /// converges on temperature. That is where night fog and dawn stratus come from, and it is
    /// the reason a storm day has an afternoon peak instead of being uniformly violent.
    ///
    /// Pure and deterministic.
    /// </summary>
    internal static class Diurnal
    {
        /// <summary>
        /// Surface heating, 0..1. A negative <paramref name="daylight"/> means "unknown" and
        /// falls back to a mission-clock day cycle, which is what the pure model uses; the live
        /// manager passes the level's synced time of day. Zero is a real value — night — and is
        /// deliberately not treated as missing, or the whole model would lurch whenever the
        /// reader's view crossed into cloud.
        /// </summary>
        public static float Heating(float missionTime, float daylight)
        {
            if (float.IsNaN(daylight) || float.IsInfinity(daylight) || daylight < 0f) return ClockCycle(missionTime);
            float clamped = daylight > 1f ? 1f : daylight;
            // Heating lags illumination: the ground is still warm after the sun has gone.
            return WeatherRegimes.Clamp01(0.25f + 0.75f * clamped);
        }

        /// <summary>Daylight could not be read; the model falls back to the mission clock.</summary>
        public const float UnknownDaylight = -1f;

        /// <summary>
        /// Depth of the mixed layer in metres. Deep and turbulent on a hot unstable afternoon,
        /// shallow and calm at night, which is what caps the cloud and traps the haze.
        /// </summary>
        public static float BoundaryLayer(float heating, float stability)
        {
            float heat = WeatherRegimes.Clamp01(heating);
            float instability = WeatherRegimes.Clamp01(stability);
            return 250f + 2750f * heat * (0.35f + 0.65f * instability);
        }

        /// <summary>The night floor: enough heating to keep the model alive but no convection.</summary>
        public const float NightHeating = 0.05f;

        /// <summary>Peaks mid-afternoon and bottoms out before dawn.</summary>
        private static float ClockCycle(float missionTime)
        {
            if (float.IsNaN(missionTime) || float.IsInfinity(missionTime)) return NightHeating;
            float days = missionTime / 86400f;
            float phase = days - (float)Math.Floor(days);
            float wave = 0.5f + 0.5f * (float)Math.Cos((phase - AfternoonPeak) * 2.0 * Math.PI);
            return WeatherRegimes.Clamp01(NightHeating + (1f - NightHeating) * wave);
        }

        /// <summary>Fraction of a day at which surface heating peaks, about 14:00.</summary>
        private const float AfternoonPeak = 0.58f;
    }
}
