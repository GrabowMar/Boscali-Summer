using System;

namespace BoscaliSummer.Modules.Weather.Audio
{
    /// <summary>Bounded listener presentation, not weather or propagation authority.</summary>
    internal static class EnvironmentAudioMath
    {
        internal static (float gain, float cutoff) Rain(float rain, bool cockpit, float speedMps,
            float heightAboveGroundM, float exposure, float time, float cloudMoisture = 0f)
        {
            rain = Unit(rain); exposure = Unit(exposure); cloudMoisture = Unit(cloudMoisture);
            float variation = 1f + 0.055f * (float)Math.Sin((float.IsFinite(time) ? time : 0f) * 0.37f)
                + 0.025f * (float)Math.Sin((float.IsFinite(time) ? time : 0f) * 1.13f + 2.1f);
            if (cockpit)
            {
                // Impact energy changes with slipstream; falling water never changes pitch.
                // Suspended cloud droplets strike only with forward speed: hovering in cloud
                // is silent, while fast flight through it patters like light rain.
                // The base sits above engine/rotor masking: hover in heavy rain peaks near
                // -12 dBFS through the 0.78-capped patter clip instead of vanishing at -19.
                float speed = Unit((float.IsFinite(speedMps) ? speedMps : 0f) / 250f);
                float rainGain = (float)Math.Pow(rain, 0.7f) * (0.40f + speed * 0.14f);
                float cloudGain = (float)Math.Pow(cloudMoisture, 0.7f) * speed * 0.38f;
                float water = Math.Max(rainGain, cloudGain);
                return (water * exposure * variation,
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
            float distance = Math.Max(0f, (float.IsFinite(distanceM) ? distanceM : 0f));
            float cutoff = 1000f + 8500f / (1f + distance / 850f);
            return cockpit ? Math.Min(2600f, cutoff * 0.62f) : cutoff;
        }

        private static float Unit(float value) => Math.Max(0f, Math.Min(1f, (float.IsFinite(value) ? value : 0f)));
    }
}
