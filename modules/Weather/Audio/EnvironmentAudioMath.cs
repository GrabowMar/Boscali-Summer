using System;

namespace BoscaliSummer.Modules.Weather.Audio
{
    /// <summary>Bounded listener presentation, not weather or propagation authority.</summary>
    internal static class EnvironmentAudioMath
    {
        internal static (float gain, float cutoff) Rain(float rain, bool cockpit, float speedMps,
            float heightAboveGroundM, float exposure, float time)
        {
            rain = Unit(rain); exposure = Unit(exposure);
            float variation = 1f + 0.055f * (float)Math.Sin(Finite(time) * 0.37f)
                + 0.025f * (float)Math.Sin(Finite(time) * 1.13f + 2.1f);
            if (cockpit)
            {
                // Impact energy changes with slipstream; falling water never changes pitch.
                float speed = Unit(Finite(speedMps) / 250f);
                return ((float)Math.Pow(rain, 0.7f) * (0.15f + speed * 0.12f) * exposure * variation,
                    4200f + speed * 1100f);
            }
            // Most audible rainfall strikes nearby surfaces. The free-air bed is quieter;
            // a roof removes local impacts but leaves filtered rain audible around it.
            float height = float.IsNaN(heightAboveGroundM) || float.IsInfinity(heightAboveGroundM)
                ? 1000f : Math.Max(0f, heightAboveGroundM);
            float proximity = 1f / (1f + height / 45f);
            proximity *= proximity;
            float shelter = 0.22f + exposure * 0.78f;
            float gain = (float)Math.Pow(rain, 0.7f) * (0.035f + proximity * 0.245f) * shelter * variation;
            float openCutoff = 2400f + proximity * 5100f;
            return (gain, 900f + (openCutoff - 900f) * exposure);
        }

        internal static float ThunderCutoff(float distanceM, bool cockpit)
        {
            float distance = Math.Max(0f, Finite(distanceM));
            float cutoff = 1000f + 8500f / (1f + distance / 850f);
            return cockpit ? Math.Min(2600f, cutoff * 0.62f) : cutoff;
        }

        private static float Unit(float value) => Math.Max(0f, Math.Min(1f, Finite(value)));
        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }
}
