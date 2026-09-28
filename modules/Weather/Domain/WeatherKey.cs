using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Everything the host decides about the weather, and the only weather data that ever
    /// crosses the network. Every peer derives the same state timeline and the same static cloud
    /// layout from this key and the synced mission clock. Immutable, compared by value.
    /// </summary>
    internal sealed class WeatherKey : IEquatable<WeatherKey>
    {
        /// <summary>No authored starting state: the seed picks one.</summary>
        public const byte AutoState = 255;

        public WeatherKey(uint seed, float epoch, bool dynamic, byte startState,
            float intervalMinutes = 5f, float fadeSeconds = 60f)
        {
            Seed = seed;
            Epoch = epoch;
            Dynamic = dynamic;
            StartState = startState;
            IntervalMinutes = Math.Max(1f, intervalMinutes);
            FadeSeconds = Math.Max(5f, fadeSeconds);
        }

        public uint Seed { get; }
        public float Epoch { get; }

        /// <summary>False holds the start state for the whole mission.</summary>
        public bool Dynamic { get; }
        public byte StartState { get; }
        public float IntervalMinutes { get; }
        public float FadeSeconds { get; }

        /// <summary>The state the timeline opens with.</summary>
        public WeatherRegimeType OpeningState()
        {
            if (StartState != AutoState) return StateTable.Clamp(StartState);
            // Missions open flyable: mostly fair skies, sometimes something brewing.
            float roll = WeatherMath.Hash01(Seed, 7, 1);
            if (roll < 0.20f) return WeatherRegimeType.Clear;
            if (roll < 0.55f) return WeatherRegimeType.Fair;
            if (roll < 0.80f) return WeatherRegimeType.Scattered;
            if (roll < 0.92f) return WeatherRegimeType.Broken;
            return WeatherRegimeType.Overcast;
        }

        public bool Equals(WeatherKey other)
        {
            if (other is null) return false;
            return Seed == other.Seed && Epoch == other.Epoch && Dynamic == other.Dynamic &&
                StartState == other.StartState && IntervalMinutes == other.IntervalMinutes &&
                FadeSeconds == other.FadeSeconds;
        }

        public override bool Equals(object obj) => Equals(obj as WeatherKey);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Seed;
                h = h * 31 + Epoch.GetHashCode();
                h = h * 31 + (Dynamic ? 1 : 0);
                h = h * 31 + StartState;
                h = h * 31 + IntervalMinutes.GetHashCode();
                h = h * 31 + FadeSeconds.GetHashCode();
                return h;
            }
        }
    }
}
