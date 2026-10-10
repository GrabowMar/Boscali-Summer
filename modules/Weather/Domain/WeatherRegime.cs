using System;

namespace BoscaliSummer.Modules.Weather.Domain
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
    /// The seven weather states and how the timeline moves between them. Uncompiled
    /// configuration: every peer derives the same sky, so these are constants, never settings.
    /// </summary>
    internal static class StateTable
    {
        public const int Count = 7;

        private static readonly SkyParams[] Table =
        {
            // sheet, base, wind, turb, rain, cu, conv, sev, front, haze, qnh, temp,
            // deck depth / smooth, middle cover / sheet, high cover / veil, split,
            // puff scale, depth, base sharp, base wobble, dome, billow, anvil
            Make(0.00f, 2400f, 0.85f, 0.05f, 0f, 0.00f, 0.00f, 0f, 0.00f, 45f, 1022f, 24f,
                 400f, 0.00f, 0.00f, 0.00f, 0.22f, 0.05f, 0.00f,
                 1400f, 500f, 40f, 60f, 0.80f, 0.70f, 0.00f),
            Make(0.02f, 1900f, 0.90f, 0.08f, 0f, 0.42f, 0.00f, 0f, 0.00f, 38f, 1018f, 23f,
                 650f, 0.00f, 0.03f, 0.00f, 0.10f, 0.08f, 0.00f,
                 1100f, 650f, 45f, 35f, 0.95f, 0.65f, 0.00f),
            Make(0.06f, 1600f, 1.00f, 0.14f, 0f, 0.72f, 0.18f, 0f, 0.00f, 28f, 1014f, 22f,
                 1800f, 0.05f, 0.10f, 0.05f, 0.12f, 0.20f, 0.00f,
                 1800f, 2200f, 55f, 60f, 1.00f, 0.80f, 0.00f),
            Make(0.48f, 1300f, 1.05f, 0.20f, 0.15f, 0.55f, 0.22f, 0f, 0.00f, 20f, 1010f, 20f,
                 850f, 0.25f, 0.16f, 0.35f, 0.14f, 0.45f, 0.45f,
                 2400f, 850f, 110f, 70f, 0.50f, 0.45f, 0.00f),
            Make(0.90f, 900f, 1.10f, 0.15f, 0.8f, 0.15f, 0.06f, 0f, 0.78f, 12f, 1006f, 17f,
                 1000f, 0.95f, 0.45f, 0.90f, 0.28f, 0.95f, 0.65f,
                 0f, 1000f, 170f, 35f, 0.15f, 0.12f, 0.00f),
            Make(0.88f, 750f, 1.25f, 0.30f, 3.0f, 0.12f, 0.62f, 0.15f, 1.00f, 9f, 1000f, 17f,
                 2400f, 0.90f, 0.45f, 0.95f, 0.28f, 0.90f, 0.55f,
                 0f, 2400f, 210f, 80f, 0.20f, 0.20f, 0.15f),
            Make(0.55f, 1000f, 1.45f, 0.45f, 2.2f, 0.20f, 1.00f, 0.65f, 1.00f, 8f, 995f, 19f,
                 1200f, 0.15f, 0.20f, 0.25f, 0.42f, 0.25f, 0.30f,
                 4600f, 2000f, 65f, 80f, 1.35f, 0.75f, 0.90f),
        };

        public static SkyParams Get(WeatherRegimeType state) => Table[Index((int)state)];

        /// <summary>Parameters at a fractional ladder position (a fade between neighbours).</summary>
        public static SkyParams At(float level)
        {
            if (level <= 0f) return Table[0];
            if (level >= Count - 1) return Table[Count - 1];
            int i = (int)level;
            // INTERIM(slice 4): holdScales false keeps today's smooth fade of the high layer's streak length between two class skies.
            SkyParams sky = SkyParams.Lerp(Table[i], Table[i + 1], level - i, holdScales: false);
            InterimHorizon(ref sky);
            return sky;
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

        /// <summary>
        /// The old row layout as a designated class sky (INTERIM, slices 2-4): the layer columns are back-derived from the old
        /// middle and high cover / sheet / veil so the existing scenes stay recognisable; the low-cloud lobe size snaps to the
        /// nearest ladder rung; the legacy dry-cumulus level, frontal and split columns ride along for the legacy machinery.
        /// </summary>
        private static SkyParams Make(float overcast, float cloudBase, float wind, float turbulence, float rain,
            float cumulus, float convective, float severity, float frontal, float haze, float qnh, float temperature,
            float layerDepth, float layerSmooth, float midCover, float midSheet, float highCover, float highVeil, float split,
            float puffScale, float puffDepth, float baseSharp, float baseWobble, float dome, float billow, float anvil)
        {
            float lobe = puffScale > 0f ? Math.Max(1000f, puffScale) : 1800f; // the old CloudShape.Resolve floor and default
            var sky = new SkyParams
            {
                CloudBase = cloudBase, SheetCover = overcast, CumulusCover = cumulus * 0.5f, Convective = convective,
                Severity = severity, StratRain = rain, WindFactor = wind, Turbulence = turbulence, Haze = haze, Qnh = qnh,
                Temperature = temperature, OpacityMid = 0.7f, OpacityHigh = 0.15f,
                LobeRung = Ladder.NearestLobeRung(lobe), PuffDepth = puffDepth, BaseSharp = baseSharp, BaseWobble = baseWobble,
                Dome = dome, Billow = billow, Anvil = anvil, DeckDepth = layerDepth, DeckSmooth = layerSmooth,
                Mid = SkyLayer.LegacyMid(midCover, midSheet), High = SkyLayer.LegacyHigh(highCover, highVeil),
                Cumulus = cumulus, Frontal = frontal, Split = split,
            };
            SkyDerived.Apply(ref sky);
            InterimHorizon(ref sky);
            return sky;
        }

        /// <summary>
        /// INTERIM(slice 4): the horizon deck's cover as today's uniform computes it, max(0.9 * Overcast, 0.3 * Cumulus + 0.2 * Convective) on
        /// the row (re-applied after a fade, because the max of a lerp is not the lerp of a max), instead of the baked library formula. Unpinned
        /// play keeps its horizon until slice 4 rewrites the deck; library skies use the baked column.
        /// </summary>
        private static void InterimHorizon(ref SkyParams sky) =>
            sky.HorizonCover = Math.Max(0.9f * sky.SheetCover, 0.3f * sky.Cumulus + 0.2f * sky.Convective);
    }
}
