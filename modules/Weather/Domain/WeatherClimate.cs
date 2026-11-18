using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// What the climate of one of the mod's maps does to the weather (map builder v2, section 6). The tables are keyed by
    /// <see cref="ClimateProfile.id"/> and exist nowhere else; 0 and every unknown id mean "no climate", which is every vanilla map.
    /// The host applies <see cref="RemapClass"/> and <see cref="Sets"/> once at capture and sends the id in the weather key; the tape
    /// reads <see cref="StoryBias"/> and <see cref="Storms"/> from that byte alone. Temperature, haze and snow are client-local and read
    /// <see cref="Local"/>, the map's own profile, which is the same on every peer because the map build is checked at the handshake.
    /// </summary>
    internal static class WeatherClimate
    {
        public const byte None = 0, Tropical = 1, Alpine = 2, Arctic = 3;
        public const int Count = 4;

        /// <summary>The sea-level temperature the class rows of <see cref="StateTable"/> were written for.</summary>
        public const float ReferenceSeaLevelC = 21f;
        /// <summary>The diurnal swing of a map with no climate.</summary>
        public const float DefaultDiurnalC = 5f;

        /// <summary>The climate of the loaded map as the main thread last read it from <see cref="MapClimate.Current"/> (WeatherManager, every frame); null on a vanilla map.
        /// Fields are also built on worker threads, and the lookup behind the contract touches scene objects, so they read this copy.</summary>
        public static volatile ClimateProfile Local;

        // Clear, Fair, Scattered, Broken, Overcast, RainSquall, Storm
        private static readonly float[][] ClassBias =
        {
            null,
            new[] { 0.6f, 1.2f, 1.6f, 1.0f, 0.5f, 1.4f, 1.2f },
            new[] { 1.0f, 1.2f, 1.2f, 1.2f, 0.9f, 0.7f, 0.6f },
            new[] { 1.3f, 0.8f, 0.6f, 1.3f, 1.8f, 0.5f, 0f },
        };

        // Frontal, ColdFront, Convective, Occluded, HighPressure
        private static readonly float[][] StoryBiasRows =
        {
            null,
            new[] { 0.3f, 0.3f, 3.0f, 0.2f, 1.0f },
            new[] { 1.0f, 1.2f, 1.5f, 0.8f, 1.0f },
            new[] { 1.5f, 0.6f, 0f, 2.0f, 1.5f },
        };

        private static readonly bool[] LenticularRows = { false, false, true, false }, FogBankRows = { false, false, true, true },
            StormRows = { true, true, true, false };

        /// <summary>The weight of a class <c>n</c> steps from the mission's own: the mission setting stays the centre of the remap.</summary>
        private static readonly float[] Kernel = { 1f, 0.6f, 0.25f, 0.08f };

        /// <summary>An unknown id is no climate.</summary>
        public static byte Clean(byte id) => id < Count ? id : None;

        /// <summary>The table id of a profile; 0 for none (a vanilla map) and for an id this build does not know.</summary>
        public static byte Id(ClimateProfile profile) => profile != null ? Clean(profile.id) : None;

        public static bool Storms(byte id) => StormRows[Clean(id)];

        public static float StoryBias(byte id, int story)
        {
            float[] row = StoryBiasRows[Clean(id)];
            return row != null && (uint)story < (uint)row.Length ? row[story] : 1f;
        }

        /// <summary>The share of each class the remap gives a mission of class <paramref name="cls"/>: bias times kernel, normalised. Tests and the remap share it.</summary>
        public static void RemapWeights(byte id, int cls, float[] into)
        {
            float[] bias = ClassBias[Clean(id)];
            float total = 0f;
            for (int k = 0; k < WeatherKey.ClassCount; k++)
            {
                int away = System.Math.Abs(k - cls);
                into[k] = bias != null && away < Kernel.Length ? bias[k] * Kernel[away] : 0f;
                total += into[k];
            }
            for (int k = 0; k < WeatherKey.ClassCount; k++) into[k] = total > 0f ? into[k] / total : k == cls ? 1f : 0f;
        }

        /// <summary>The class a mission of class <paramref name="cls"/> opens in under this climate: a hashed pick among its neighbours, weighted by the bias. No climate, or no class, returns <paramref name="cls"/>.</summary>
        public static byte RemapClass(byte id, byte cls, uint seed)
        {
            if (Clean(id) == None || cls >= WeatherKey.ClassCount) return cls;
            var weights = new float[WeatherKey.ClassCount];
            RemapWeights(id, cls, weights);
            float roll = WeatherMath.Hash01(seed, 0xC11A);
            byte last = cls;
            for (int k = 0; k < weights.Length; k++)
            {
                if (weights[k] <= 0f) continue;
                last = (byte)k;
                roll -= weights[k];
                if (roll < 0f) return last;
            }
            return last;
        }

        /// <summary>The set pieces (<see cref="Superstructures"/> bits) the climate adds to a mission: lenticulars over the alpine range six times in ten, a fog bank a third of the time.</summary>
        public static byte Sets(byte id, uint seed)
        {
            id = Clean(id);
            byte sets = 0;
            if (LenticularRows[id] && WeatherMath.Hash01(seed, 0xC11B) < 0.6f) sets |= Superstructures.LenticularSet;
            if (FogBankRows[id] && WeatherMath.Hash01(seed, 0xC11C) < 0.35f) sets |= Superstructures.FogBankSet;
            return sets;
        }

        /// <summary>How much of the precipitation falls as snow at this air temperature: none at 1.5 C and above, all of it at -0.5 C and below.</summary>
        public static float Snow(float temperatureC) => Scalar.Clamp01((1.5f - temperatureC) / 2f);

        /// <summary>The snow share at the viewer: always 0 on a map with no climate, so vanilla maps keep their rain at every altitude.</summary>
        public static float SnowNow(float temperatureC) => Local != null ? Snow(temperatureC) : 0f;

        /// <summary>A METAR temperature group: two digits, M before a negative one ("M09", "14").</summary>
        public static string MetarDegrees(float celsius)
        {
            int whole = (int)System.Math.Round(celsius, System.MidpointRounding.AwayFromZero);
            return (whole < 0 ? "M" : "") + System.Math.Min(99, System.Math.Abs(whole)).ToString("00");
        }
    }
}
