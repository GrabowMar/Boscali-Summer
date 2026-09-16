using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    internal readonly struct WeatherForecastEntry
    {
        /// <summary>Mission time the entry applies at.</summary>
        public readonly float AtSeconds;

        public readonly WeatherState State;

        public WeatherForecastEntry(float atSeconds, WeatherState state)
        {
            AtSeconds = atSeconds;
            State = state;
        }
    }

    /// <summary>
    /// A bounded forward sample of <see cref="WeatherModel"/>: what the schedule holds for the
    /// next stretch of the mission. Rebuilt from the mission clock alone, so it is identical on
    /// the host and on every client and stays correct across a late join.
    /// </summary>
    internal sealed class WeatherForecast
    {
        public const int MaxEntries = 12;
        public const float MinStepSeconds = 30f;
        public const float DefaultStepSeconds = 180f;
        public const int DefaultSteps = 8;

        private readonly WeatherForecastEntry[] entries = new WeatherForecastEntry[MaxEntries];

        public int Count { get; private set; }

        /// <summary>Seconds until the next front starts crossing over. Never negative.</summary>
        public float NextChangeSeconds { get; private set; }

        public WeatherRegime NextRegime { get; private set; }

        public WeatherForecastEntry this[int index] => entries[index < 0 ? 0 : index >= Count ? Count - 1 : index];

        public static WeatherForecast Build(
            int seed,
            float missionTime,
            int steps = DefaultSteps,
            float stepSeconds = DefaultStepSeconds)
        {
            if (steps < 0) steps = 0;
            if (steps > MaxEntries) steps = MaxEntries;
            if (float.IsNaN(stepSeconds) || stepSeconds < MinStepSeconds) stepSeconds = MinStepSeconds;

            var forecast = new WeatherForecast();
            forecast.Count = steps;
            for (int i = 0; i < steps; i++)
            {
                float at = missionTime + stepSeconds * (i + 1);
                forecast.entries[i] = new WeatherForecastEntry(at, WeatherModel.Sample(seed, at));
            }

            float changeAt = WeatherModel.NextChangeAt(missionTime);
            float untilChange = changeAt - missionTime;
            forecast.NextChangeSeconds = float.IsNaN(untilChange) || untilChange < 0f ? 0f : untilChange;
            forecast.NextRegime = WeatherModel.NextRegime(seed, missionTime);
            return forecast;
        }

        /// <summary>Mission time at which the forecast moves into <paramref name="target"/>.</summary>
        public static float SecondsToRegime(
            WeatherRegime target,
            int seed,
            float missionTime,
            float horizonSeconds)
        {
            if (float.IsNaN(horizonSeconds) || horizonSeconds < 0f) return -1f;
            int front = WeatherModel.FrontIndex(missionTime);
            int lastFront = WeatherModel.FrontIndex(missionTime + horizonSeconds);
            if (lastFront > front + MaxEntries) lastFront = front + MaxEntries;            for (int f = front + 1; f <= lastFront; f++)
            {
                if (WeatherModel.Front(seed, f).Regime != target) continue;
                float at = WeatherModel.FrontStart(f) - WeatherModel.FrontBlendSeconds;
                if (at < missionTime) at = missionTime;
                return at - missionTime;
            }
            return -1f;
        }
    }
}
