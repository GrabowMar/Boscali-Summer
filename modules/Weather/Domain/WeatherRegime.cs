using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Discrete, recognizable weather regimes that translate into native Nuclear Option
    /// environment properties (conditions, cloud height, wind, turbulence).
    /// </summary>
    internal enum WeatherRegimeType
    {
        Clear = 0,
        Fair = 1,
        Scattered = 2,
        Broken = 3,
        Overcast = 4,
        RainSquall = 5,
        Storm = 6
    }

    /// <summary>
    /// Metadata, target thresholds, and pilot tactical advice for each weather regime.
    /// </summary>
    internal readonly struct RegimeSnapshot
    {
        public WeatherRegimeType Type { get; }
        public string Name { get; }
        public string Code { get; }
        public float TargetConditions { get; }
        public float TargetCloudHeight { get; }
        public float TargetTurbulence { get; }
        public float WindSpeedMultiplier { get; }
        public string TacticalBriefing { get; }

        public RegimeSnapshot(
            WeatherRegimeType type,
            string name,
            string code,
            float targetConditions,
            float targetCloudHeight,
            float targetTurbulence,
            float windSpeedMultiplier,
            string tacticalBriefing)
        {
            Type = type;
            Name = name;
            Code = code;
            TargetConditions = targetConditions;
            TargetCloudHeight = targetCloudHeight;
            TargetTurbulence = targetTurbulence;
            WindSpeedMultiplier = windSpeedMultiplier;
            TacticalBriefing = tacticalBriefing;
        }

        public static RegimeSnapshot FromType(WeatherRegimeType type)
        {
            switch (type)
            {
                case WeatherRegimeType.Clear:
                    return new RegimeSnapshot(
                        WeatherRegimeType.Clear,
                        "Clear Sky",
                        "CLR",
                        targetConditions: 0.05f,
                        targetCloudHeight: 3800f,
                        targetTurbulence: 0.05f,
                        windSpeedMultiplier: 0.6f,
                        tacticalBriefing: "OPTIMAL VISIBILITY // FULL IR SENSOR RANGE // NO CEILING CONSTRAINTS");

                case WeatherRegimeType.Fair:
                    return new RegimeSnapshot(
                        WeatherRegimeType.Fair,
                        "Fair",
                        "FEW",
                        targetConditions: 0.18f,
                        targetCloudHeight: 3200f,
                        targetTurbulence: 0.10f,
                        windSpeedMultiplier: 0.8f,
                        tacticalBriefing: "LIGHT CLOUD SHREDS // STABLE AIRFLOW // MINIMAL SENSOR OCCLUSION");

                case WeatherRegimeType.Scattered:
                    return new RegimeSnapshot(
                        WeatherRegimeType.Scattered,
                        "Scattered Clouds",
                        "SCT",
                        targetConditions: 0.35f,
                        targetCloudHeight: 2700f,
                        targetTurbulence: 0.20f,
                        windSpeedMultiplier: 1.0f,
                        tacticalBriefing: "ISOLATED CLOUD MASKING // USABLE FOR RADAR/OPTICAL TERRAIN BREAKS");

                case WeatherRegimeType.Broken:
                    return new RegimeSnapshot(
                        WeatherRegimeType.Broken,
                        "Broken Deck",
                        "BKN",
                        targetConditions: 0.52f,
                        targetCloudHeight: 2200f,
                        targetTurbulence: 0.35f,
                        windSpeedMultiplier: 1.2f,
                        tacticalBriefing: "VARIABLE CEILING // RESTRICTED HIGH-ALTITUDE BOMBING // POP-UP THREATS");

                case WeatherRegimeType.Overcast:
                    return new RegimeSnapshot(
                        WeatherRegimeType.Overcast,
                        "Overcast",
                        "OVC",
                        targetConditions: 0.68f,
                        targetCloudHeight: 1800f,
                        targetTurbulence: 0.50f,
                        windSpeedMultiplier: 1.4f,
                        tacticalBriefing: "LOW CEILING // REDUCED AMBIENT LIGHT // CAS FORCED INTO SHORAD ENVELOPE");

                case WeatherRegimeType.RainSquall:
                    return new RegimeSnapshot(
                        WeatherRegimeType.RainSquall,
                        "Rain Squall",
                        "RA+",
                        targetConditions: 0.82f,
                        targetCloudHeight: 1500f,
                        targetTurbulence: 0.70f,
                        windSpeedMultiplier: 1.7f,
                        tacticalBriefing: "ACTIVE PRECIPITATION // GUSTS BUFFETING AIRCRAFT // DEGRADED IR TRACKING");

                case WeatherRegimeType.Storm:
                    return new RegimeSnapshot(
                        WeatherRegimeType.Storm,
                        "Thunderstorm",
                        "TS",
                        targetConditions: 0.95f,
                        targetCloudHeight: 1200f,
                        targetTurbulence: 0.90f,
                        windSpeedMultiplier: 2.1f,
                        tacticalBriefing: "SEVERE TURBULENCE // FREQUENT LIGHTNING // EXTREME LOW DECK");

                default:
                    return FromType(WeatherRegimeType.Fair);
            }
        }

        public static RegimeSnapshot FromConditions(float conditions)
        {
            float c = Math.Max(0f, Math.Min(1f, conditions));
            if (c < 0.12f) return FromType(WeatherRegimeType.Clear);
            if (c < 0.28f) return FromType(WeatherRegimeType.Fair);
            if (c < 0.45f) return FromType(WeatherRegimeType.Scattered);
            if (c < 0.60f) return FromType(WeatherRegimeType.Broken);
            if (c < 0.75f) return FromType(WeatherRegimeType.Overcast);
            if (c < 0.88f) return FromType(WeatherRegimeType.RainSquall);
            return FromType(WeatherRegimeType.Storm);
        }
    }

    /// <summary>
    /// The numbers one weather state means. Everything downstream reads these; a fade between
    /// states lerps them, so clouds grow and shrink in place instead of rearranging.
    /// </summary>
    internal struct StateParams
    {
        /// <summary>Stratiform sheet coverage 0..1 (the continuous deck under OVERCAST).</summary>
        public float Overcast;
        public float CloudBase;
        /// <summary>Multiplier on the mission wind speed.</summary>
        public float WindFactor;
        public float Turbulence;
        /// <summary>Widespread light rain under the sheet, mm/h.</summary>
        public float AreaRain;
        /// <summary>Fair-weather cumulus groups, 0..1: how many are out and how big.</summary>
        public float Cumulus;
        /// <summary>Convective cells, 0..1: how many are active and how deep.</summary>
        public float Convective;
        /// <summary>Share of cells that are severe (hail, strong lightning), 0..1.</summary>
        public float Severity;
        /// <summary>Frontal bands, 0..1: first band from ~0.1, second from ~0.7.</summary>
        public float Frontal;
        public float HazeKm;
        public float Qnh;
        public float Temperature;

        public static StateParams Lerp(StateParams a, StateParams b, float t)
        {
            return new StateParams
            {
                Overcast = WeatherMath.Lerp(a.Overcast, b.Overcast, t),
                CloudBase = WeatherMath.Lerp(a.CloudBase, b.CloudBase, t),
                WindFactor = WeatherMath.Lerp(a.WindFactor, b.WindFactor, t),
                Turbulence = WeatherMath.Lerp(a.Turbulence, b.Turbulence, t),
                AreaRain = WeatherMath.Lerp(a.AreaRain, b.AreaRain, t),
                Cumulus = WeatherMath.Lerp(a.Cumulus, b.Cumulus, t),
                Convective = WeatherMath.Lerp(a.Convective, b.Convective, t),
                Severity = WeatherMath.Lerp(a.Severity, b.Severity, t),
                Frontal = WeatherMath.Lerp(a.Frontal, b.Frontal, t),
                HazeKm = WeatherMath.Lerp(a.HazeKm, b.HazeKm, t),
                Qnh = WeatherMath.Lerp(a.Qnh, b.Qnh, t),
                Temperature = WeatherMath.Lerp(a.Temperature, b.Temperature, t),
            };
        }
    }

    /// <summary>
    /// The seven weather states and how the timeline moves between them. Uncompiled
    /// configuration: every peer derives the same sky, so these are constants, never settings.
    /// </summary>
    internal static class StateTable
    {
        public const int Count = 7;

        private static readonly StateParams[] Table =
        {
            //   sheet  base   wind  turb  rain  cu    conv  sev   front haze qnh    temp
            Make(0.00f, 2400f, 0.85f, 0.05f, 0f,  0.00f, 0.00f, 0f,  0.00f, 45f, 1022f, 24f), // CLEAR
            Make(0.03f, 1800f, 0.90f, 0.08f, 0f,  0.45f, 0.00f, 0f,  0.00f, 38f, 1018f, 23f), // FAIR
            Make(0.10f, 1500f, 1.00f, 0.14f, 0f,  0.85f, 0.18f, 0f,  0.00f, 28f, 1014f, 22f), // SCATTERED
            Make(0.40f, 1200f, 1.05f, 0.20f, 0.2f, 1.00f, 0.30f, 0f,  0.35f, 20f, 1010f, 20f), // BROKEN
            Make(0.85f,  900f, 1.10f, 0.15f, 1.0f, 0.55f, 0.12f, 0f,  0.80f, 12f, 1006f, 17f), // OVERCAST
            Make(0.80f,  800f, 1.25f, 0.30f, 3.0f, 0.40f, 0.60f, 0.1f, 1.00f,  9f, 1000f, 17f), // RAIN SQUALL
            Make(0.70f,  900f, 1.45f, 0.45f, 2.0f, 0.30f, 1.00f, 0.6f, 1.00f,  8f,  995f, 19f), // STORM
        };

        public static StateParams Get(WeatherRegimeType state) => Table[Index((int)state)];

        /// <summary>Parameters at a fractional ladder position (a fade between neighbours).</summary>
        public static StateParams At(float level)
        {
            if (level <= 0f) return Table[0];
            if (level >= Count - 1) return Table[Count - 1];
            int i = (int)level;
            return StateParams.Lerp(Table[i], Table[i + 1], level - i);
        }

        public static WeatherRegimeType Clamp(int value) => (WeatherRegimeType)Index(value);

        /// <summary>The state a vanilla <c>conditions</c> value reads as.</summary>
        public static WeatherRegimeType FromConditions(float conditions) => RegimeSnapshot.FromConditions(conditions).Type;

        /// <summary>
        /// One step of the walk: hold, or move to a neighbour. Weather builds and clears one
        /// state at a time, and storms do not linger.
        /// </summary>
        public static WeatherRegimeType Next(WeatherRegimeType current, float roll)
        {
            int i = (int)current;
            float up, down;
            switch (current)
            {
                case WeatherRegimeType.Clear: up = 0.45f; down = 0f; break;
                case WeatherRegimeType.RainSquall: up = 0.25f; down = 0.40f; break;
                case WeatherRegimeType.Storm: up = 0f; down = 0.60f; break;
                default: up = 0.30f; down = 0.30f; break;
            }
            if (roll < up) return Clamp(i + 1);
            if (roll < up + down) return Clamp(i - 1);
            return current;
        }

        private static int Index(int i) => i < 0 ? 0 : i >= Count ? Count - 1 : i;

        private static StateParams Make(float overcast, float cloudBase, float wind, float turbulence, float rain,
            float cumulus, float convective, float severity, float frontal, float haze, float qnh, float temperature)
        {
            return new StateParams
            {
                Overcast = overcast, CloudBase = cloudBase, WindFactor = wind, Turbulence = turbulence,
                AreaRain = rain, Cumulus = cumulus, Convective = convective, Severity = severity,
                Frontal = frontal, HazeKm = haze, Qnh = qnh, Temperature = temperature,
            };
        }
    }
}
