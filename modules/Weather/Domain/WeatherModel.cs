using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The deterministic weather schedule. Every peer derives the same sky from the mission
    /// identity and the mission clock alone, so the forecast needs no wire format of its own:
    /// the vanilla <c>LevelInfo</c> sync vars carry the authoritative values and this model
    /// supplies the shape around them.
    ///
    /// A mission is a run of <see cref="FrontsPerPhase"/>-long weather phases; each phase picks
    /// its own character (a fair day, a storm day) and each front inside it picks a regime and
    /// a position inside that regime's band. Consecutive fronts cross-fade over the last
    /// <see cref="FrontBlendSeconds"/>, so the sky builds and breaks instead of stepping.
    /// </summary>
    internal static class WeatherModel
    {
        /// <summary>
        /// One weather front. Five minutes: long enough to plan a sortie inside one sky, short
        /// enough that a player who flies for half an hour crosses several and sees the weather
        /// change. The first draft of this used twenty-five minutes and nothing visible happened
        /// inside a session — do not lengthen it again without that in mind.
        /// </summary>
        public const float FrontSeconds = 300f;

        /// <summary>How much of the tail of a front is spent cross-fading into the next one.</summary>
        public const float FrontBlendSeconds = 90f;

        /// <summary>Fronts that share one day character. Three fronts is fifteen minutes.</summary>
        public const int FrontsPerPhase = 3;

        /// <summary>
        /// The severity roll is raised to this power before it picks a regime, which fattens the
        /// heavy tail: roughly a third of fronts are SQUALL or STORM, and CLEAR stays possible
        /// but rare. A straight roll spends most of its life in the two middle regimes and the
        /// sky never does anything worth reacting to.
        /// </summary>
        public const float SeveritySkew = 0.70f;

        /// <summary>How far the heading may back or veer inside a single front.</summary>
        public const float VeerDegrees = 55f;

        /// <summary>Lowest cloud base the model will ask vanilla for.</summary>
        public const float MinCloudBase = 450f;

        public const float MaxCloudBase = 3400f;

        public const int MaxFrontIndex = 1_000_000;

        public static int Seed(string missionIdentity)
        {
            if (string.IsNullOrEmpty(missionIdentity)) return 0x5EED;
            uint hash = 2166136261u;
            for (int i = 0; i < missionIdentity.Length; i++)
                hash = (hash ^ missionIdentity[i]) * 16777619u;
            return (int)hash;
        }

        public static int FrontIndex(float missionTime)
        {
            if (float.IsNaN(missionTime) || missionTime <= 0f) return 0;
            int index = (int)(missionTime / FrontSeconds);
            return index > MaxFrontIndex ? MaxFrontIndex : index;
        }

        public static float FrontStart(int front) => front * FrontSeconds;

        public static float PhaseIndex(int front) => front / FrontsPerPhase;

        /// <summary>How far into the cross-fade from <paramref name="front"/> to the next front we are.</summary>
        public static float BlendWeight(float missionTime)
        {
            float local = missionTime - FrontIndex(missionTime) * FrontSeconds;
            float t = WeatherRegimes.Clamp01((local - (FrontSeconds - FrontBlendSeconds)) / FrontBlendSeconds);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The sky the model wants at a mission time, before any haze term.</summary>
        public static WeatherState Sample(int seed, float missionTime)
        {
            int front = FrontIndex(missionTime);
            WeatherState current = Front(seed, front);
            float weight = BlendWeight(missionTime);
            if (weight <= 0f) return current;
            return WeatherState.Blend(current, Front(seed, front + 1), weight);
        }

        /// <summary>The settled sky of one front, ignoring the cross-fade into its successor.</summary>
        public static WeatherState Front(int seed, int front)
        {
            if (front < 0) front = 0;
            if (front > MaxFrontIndex) front = MaxFrontIndex;

            uint roll = Mix(seed, front);
            float phaseRoll = Unit(Mix(Mix(seed, (int)PhaseIndex(front)), 0x2F1Fu));
            float severityRoll = (float)Math.Pow(Unit(Mix(roll, 0x11u)), SeveritySkew);

            // 62/38 split: the day character sets the ceiling, the front roll sets the weather
            // inside it, so a storm day still has lulls and a fair day still has haze.
            int band = (int)((severityRoll * 0.62f + phaseRoll * 0.38f) * WeatherRegimes.Count);
            int index = band < 0 ? 0 : band >= WeatherRegimes.Count ? WeatherRegimes.Count - 1 : band;

            float position = Unit(Mix(roll, 0x23u));
            float conditions = WeatherState.Lerp(
                WeatherRegimes.ConditionsLo(index), WeatherRegimes.ConditionsHi(index), position);
            float cloudBase = WeatherState.Lerp(
                WeatherRegimes.CloudBaseLo(index), WeatherRegimes.CloudBaseHi(index), Unit(Mix(roll, 0x31u)));
            float windSpeed = WeatherState.Lerp(
                WeatherRegimes.WindLo(index), WeatherRegimes.WindHi(index), position);
            float turbulence = WeatherState.Lerp(
                WeatherRegimes.TurbulenceLo(index), WeatherRegimes.TurbulenceHi(index), Unit(Mix(roll, 0x41u)));

            float heading = Unit(Mix(roll, 0x53u)) * 360f;
            float veerScale = 0.4f + 0.6f * (index / (float)(WeatherRegimes.Count - 1));
            float veer = (Unit(Mix(roll, 0x61u)) - 0.5f) * 2f * VeerDegrees * veerScale;

            return new WeatherState(
                WeatherRegimes.FromIndex(index),
                conditions,
                ClampCloudBase(cloudBase),
                windSpeed,
                heading + veer,
                turbulence);
        }

        /// <summary>Mission time at which the next front's cross-fade begins.</summary>
        public static float NextChangeAt(float missionTime)
        {
            int front = FrontIndex(missionTime);
            float changeAt = FrontStart(front + 1) - FrontBlendSeconds;
            return changeAt > missionTime ? changeAt : FrontStart(front + 2) - FrontBlendSeconds;
        }

        public static WeatherRegime NextRegime(int seed, float missionTime)
        {
            int front = FrontIndex(missionTime);
            if (FrontStart(front + 1) - FrontBlendSeconds <= missionTime) front += 1;
            return Front(seed, front + 1).Regime;
        }

        public static float ClampCloudBase(float value)
        {
            if (float.IsNaN(value)) return 1800f;
            if (value < MinCloudBase) return MinCloudBase;
            return value > MaxCloudBase ? MaxCloudBase : value;
        }

        private static uint Mix(int seed, int value)
        {
            uint hash = (uint)seed ^ ((uint)value * 0x9E3779B9u);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            return hash ^ (hash >> 16);
        }

        private static uint Mix(uint a, uint b)
        {
            uint hash = a ^ (b * 0x9E3779B9u);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            return hash ^ (hash >> 16);
        }

        private static float Unit(uint hash) => (hash >> 8) * (1f / 16777216f);
    }
}
