using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Where the weather seed comes from (spec 9.4): a fresh roll each launch, the mission itself, or a fixed number.</summary>
    internal enum WeatherSeedMode { Random, Mission, Fixed }

    /// <summary>The mission fields a Mission-mode seed is hashed from. <c>default</c> is the NullMission / Free Flight constant.</summary>
    internal readonly struct MissionSeedInputs
    {
        public readonly string MissionName, MapKey;
        public readonly float TimeOfDay, WeatherIntensity, CloudAltitude, WindSpeed, WindHeading;

        public MissionSeedInputs(string missionName, string mapKey, float timeOfDay, float weatherIntensity,
            float cloudAltitude, float windSpeed, float windHeading)
        {
            MissionName = missionName;
            MapKey = mapKey;
            TimeOfDay = timeOfDay;
            WeatherIntensity = weatherIntensity;
            CloudAltitude = cloudAltitude;
            WindSpeed = windSpeed;
            WindHeading = windHeading;
        }
    }

    /// <summary>
    /// Seed resolution. Pure: the host reads the clock once and passes it in as <c>entropy</c>; the result travels in
    /// the key, so no other peer ever rolls anything. A seed is never 0.
    /// </summary>
    internal static class WeatherSeed
    {
        public const int MaxFixed = 16777215;

        /// <summary>Vanilla's default mission (<c>MissionSaveLoad.LoadDefault</c>) is a real mission named "Free Flight"
        /// whose map the player picks. Mission mode treats it like NullMission, or it would hash the map path and give a
        /// different seed per map (spec 9.4: both collapse to one constant).</summary>
        public static bool IsFreeFlightName(string name) =>
            name != null && string.Equals(name.Trim(), "Free Flight", StringComparison.OrdinalIgnoreCase);

        public static uint Resolve(WeatherSeedMode mode, int fixedSeed, ulong entropy, in MissionSeedInputs mission)
        {
            switch (mode)
            {
                case WeatherSeedMode.Mission:
                    return FromMission(mission);
                case WeatherSeedMode.Fixed when fixedSeed >= 1 && fixedSeed <= MaxFixed:
                    return (uint)fixedSeed;
                default:
                    return Mix(entropy);
            }
        }

        /// <summary>SplitMix64 finaliser folded to 32 bits: neighbouring clock values give unrelated seeds.</summary>
        public static uint Mix(ulong entropy)
        {
            unchecked
            {
                ulong z = entropy + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                uint seed = (uint)(z ^ (z >> 32));
                return seed == 0u ? 1u : seed;
            }
        }

        /// <summary>Stable across runs and machines: no clock, no string.GetHashCode.</summary>
        public static uint FromMission(in MissionSeedInputs mission)
        {
            int name = (int)Deterministic.HashString(mission.MissionName ?? string.Empty);
            int map = (int)Deterministic.HashString(mission.MapKey ?? string.Empty);
            uint first = Deterministic.Hash(name, map, Quantise(mission.TimeOfDay, 100f), Quantise(mission.WeatherIntensity, 1000f));
            uint second = Deterministic.Hash((int)first, Quantise(mission.CloudAltitude, 1f),
                Quantise(mission.WindSpeed, 100f), Quantise(mission.WindHeading, 10f));
            return second == 0u ? 1u : second;
        }

        private static int Quantise(float value, float scale) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0 : (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
    }
}
