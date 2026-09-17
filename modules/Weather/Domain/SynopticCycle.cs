using System;
using BoscaliSummer.Core;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The slow scale: a pressure system taking hours to pass, which is what makes a mission's
    /// weather evolve instead of merely cycling. It also decides which two air masses are in
    /// contact, so the front that results is a consequence of the masses rather than a label
    /// rolled out of a table.
    ///
    /// Pure and deterministic from <c>seed</c> and the mission clock.
    /// </summary>
    internal static class SynopticCycle
    {
        /// <summary>
        /// How long one pressure system takes to pass, in seconds. An hour is a pacing decision
        /// rather than a meteorological one — real synoptic systems take days — but a sortie is
        /// half an hour and the sky has to do something inside it. This is the same bargain
        /// <c>WeatherModel.FrontSeconds</c> records: one full passage per hour means a player
        /// sees a sky build, break and clear without ever sitting in nothing.
        /// </summary>
        public const float PeriodSeconds = 3600f;

        /// <summary>Which cycle of the synoptic rhythm a mission time falls in.</summary>
        public static int CycleIndex(float missionTime)
        {
            if (float.IsNaN(missionTime) || missionTime <= 0f) return 0;
            float cycles = (float)Math.Floor(missionTime / PeriodSeconds);
            if (cycles > 1_000_000f) return 1_000_000;
            return (int)cycles;
        }

        /// <summary>Position through the current cycle, 0..1.</summary>
        public static float Phase01(float missionTime)
        {
            if (float.IsNaN(missionTime) || missionTime <= 0f) return 0f;
            float phases = missionTime / PeriodSeconds;
            float phase = phases - (float)Math.Floor(phases);
            return phase < 0f ? 0f : phase > 1f ? 1f : phase;
        }

        /// <summary>
        /// Synoptic pressure, 0 = a deep low and 1 = a strong ridge. A fundamental plus a
        /// seeded second harmonic, so consecutive cycles are similar in shape but not identical
        /// and a long mission never sees the same passage twice.
        /// </summary>
        public static float Pressure(int seed, float missionTime)
        {
            if (float.IsNaN(missionTime)) return 0.5f;
            int cycle = CycleIndex(missionTime);
            float phase = Phase01(missionTime) * 2f * (float)Math.PI;
            float tilt = Deterministic.UnitFloat(Deterministic.Hash(seed, cycle, 0x5B)) * 2f - 1f;
            float wave = (float)Math.Sin(phase) * 0.62f + (float)Math.Sin(phase * 2f + tilt) * 0.22f;
            // Inverted: the trough of the wave is the low.
            return WeatherRegimes.Clamp01(0.5f - wave * 0.5f);
        }

        /// <summary>
        /// The two air masses in contact this cycle. The pair is drawn once per cycle so a
        /// passage is a coherent story rather than a shuffle; which of the two owns the map
        /// right now is the caller's business, decided by where the boundary has got to.
        /// </summary>
        public static AirMassKind Sample(int seed, float missionTime, out AirMassKind behind, out AirMassKind ahead)
        {
            int cycle = CycleIndex(missionTime);
            int pair = (int)(Deterministic.UnitFloat(Deterministic.Hash(seed, cycle, 0x71)) * 4f);

            switch (pair)
            {
                case 0:
                    // The classic warm-sector passage: polar air pushing into tropical maritime.
                    behind = AirMassKind.MaritimePolar;
                    ahead = AirMassKind.MaritimeTropical;
                    break;
                case 1:
                    // A continental cold outbreak, drier and colder than the marine case.
                    behind = AirMassKind.ContinentalPolar;
                    ahead = AirMassKind.MaritimeTropical;
                    break;
                case 2:
                    // Deep cold: arctic air undercutting polar air. Sharp and dry.
                    behind = AirMassKind.ContinentalArctic;
                    ahead = AirMassKind.ContinentalPolar;
                    break;
                default:
                    // A dry line: heat and moisture with no thermal boundary to speak of.
                    behind = AirMassKind.MaritimeTropical;
                    ahead = AirMassKind.ContinentalTropical;
                    break;
            }

            // A ridge is a single air mass with nothing to fight, so the pair collapses to one.
            if (Pressure(seed, missionTime) >= HighPressurePhase) ahead = behind;

            // Before the boundary reaches the map centre you are in the air that is about to be
            // displaced; after it you are in the air that displaced it.
            return Phase01(missionTime) < 0.5f ? ahead : behind;
        }

        /// <summary>At and above this pressure the cycle is a ridge: no boundary is in play.</summary>
        public const float HighPressurePhase = 0.72f;
    }
}
