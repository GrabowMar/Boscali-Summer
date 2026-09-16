using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// One complete sky, in the units the vanilla <c>LevelInfo</c> members take. Pure and
    /// allocation-free so the model and its forecast can be tested without Unity.
    /// </summary>
    internal readonly struct WeatherState
    {
        public readonly WeatherRegime Regime;
        public readonly float Conditions;
        public readonly float CloudBase;
        public readonly float WindSpeed;
        public readonly float WindHeading;
        public readonly float Turbulence;

        public WeatherState(
            WeatherRegime regime,
            float conditions,
            float cloudBase,
            float windSpeed,
            float windHeading,
            float turbulence)
        {
            Regime = regime;
            Conditions = conditions;
            CloudBase = cloudBase;
            WindSpeed = windSpeed;
            WindHeading = WrapHeading(windHeading);
            Turbulence = turbulence;
        }

        public bool IsSevere => WeatherRegimes.IsSevere(Regime);

        /// <summary>Shortest-arc blend, so a heading never sweeps the long way round.</summary>
        public static WeatherState Blend(WeatherState from, WeatherState to, float weight)
        {
            float t = WeatherRegimes.Clamp01(weight);
            return new WeatherState(
                t < 0.5f ? from.Regime : to.Regime,
                Lerp(from.Conditions, to.Conditions, t),
                Lerp(from.CloudBase, to.CloudBase, t),
                Lerp(from.WindSpeed, to.WindSpeed, t),
                LerpAngle(from.WindHeading, to.WindHeading, t),
                Lerp(from.Turbulence, to.Turbulence, t));
        }

        /// <summary>
        /// The interactive term: fighting on the ground thickens the sky. Haze is 0..1 and
        /// lifts conditions, drops the cloud base and stirs the air, without ever inventing a
        /// storm out of a clear day.
        /// </summary>
        public static WeatherState WithHaze(WeatherState state, float haze)
        {
            float h = WeatherRegimes.Clamp01(haze);
            if (h <= 0f) return state;
            float conditions = WeatherRegimes.Clamp01(state.Conditions + 0.15f * h);
            return new WeatherState(
                WeatherRegimes.FromConditions(conditions),
                conditions,
                Lerp(state.CloudBase, 600f, h * 0.5f),
                state.WindSpeed + 1.5f * h,
                state.WindHeading,
                WeatherRegimes.Clamp01(state.Turbulence + 0.08f * h));
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public static float LerpAngle(float from, float to, float t)
        {
            float delta = Repeat(to - from + 180f, 360f) - 180f;
            return WrapHeading(from + delta * t);
        }

        public static float WrapHeading(float heading)
        {
            if (float.IsNaN(heading) || float.IsInfinity(heading)) return 0f;
            float wrapped = heading % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        private static float Repeat(float value, float length) => value - (float)Math.Floor(value / length) * length;
    }
}
