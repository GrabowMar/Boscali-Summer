using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>One flight-level evaluation for precipitation and condensation effects.</summary>
    internal readonly struct FlightWeatherAirMass
    {
        internal readonly float Rain;
        internal readonly float CloudMoisture;
        internal readonly float VisualRain;
        internal readonly float Atmosphere;

        private FlightWeatherAirMass(float rain, float moisture)
        {
            Rain = rain;
            CloudMoisture = moisture;
            // Condensation wets exposed glass; it is not falling precipitation or global haze.
            VisualRain = rain;
            Atmosphere = rain;
        }

        internal static FlightWeatherAirMass Evaluate(WeatherPoint point, float altitude,
            bool insideCloud, float? forcedRain)
        {
            return Evaluate(point, altitude, insideCloud ? 0.5f : 0f, forcedRain);
        }

        internal static FlightWeatherAirMass Evaluate(WeatherPoint point, float altitude,
            float cloudDensity, float? forcedRain)
        {
            float precipitation = forcedRain.HasValue
                ? Clamp01(forcedRain.Value)
                : Clamp01(point.RainRate / 20f) * VerticalRain(point, altitude);
            float moisture = WeatherMath.Smoothstep(0.025f, 0.65f, Clamp01(cloudDensity));
            return new FlightWeatherAirMass(precipitation, moisture);
        }

        internal static float VerticalRain(WeatherPoint point, float altitude)
        {
            if (altitude <= point.CloudBase) return 1f;
            if (altitude >= point.CloudTop) return 0f;
            float t = (altitude - point.CloudBase) /
                Math.Max(1f, point.CloudTop - point.CloudBase);
            return 1f - t * t * (3f - 2f * t);
        }

        private static float Clamp01(float value) =>
            float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
    }
}
