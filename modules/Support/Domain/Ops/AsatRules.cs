using System;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    /// <summary>A fictional intercept contests one forecast theatre pass of the selected satellite.</summary>
    internal static class AsatRules
    {
        public const float PlumeSeconds = 30f;
        private const float ConjunctionTolerance = 4f;
        private const float ForecastBandSeconds = 15f;
        private const int TrackSectors = 8;

        public static float TrackSector(float position) =>
            (Math.Min(TrackSectors - 1, (int)(Math.Clamp(position, 0f, 1f) * TrackSectors)) + 0.5f) / TrackSectors;

        public static float ForecastBand(float conjunctionAt) => MathF.Ceiling(conjunctionAt / ForecastBandSeconds) * ForecastBandSeconds;

        public static bool SameSatellite(SpaceState committed, SpaceState current, BirdKind kind, int instance) =>
            instance > 0 && current != null && ReferenceEquals(committed, current) && current.BirdInstance(kind) == instance;

        public static bool TryForecast(in GeoBird bird, float launchAt, out float conjunctionAt)
        {
            conjunctionAt = 0f;
            float earliest = launchAt + OpsRules.AsatFlightSeconds;
            if (!bird.NextTheatrePass(earliest, out OrbitPass pass)) return false;
            if (pass.PeakAt < earliest && !bird.NextTheatrePass(pass.EndAt + 0.1f, out pass)) return false;
            if (!float.IsFinite(pass.PeakAt) || pass.PeakAt < earliest) return false;
            conjunctionAt = pass.PeakAt;
            return true;
        }

        public static bool OpportunityRemains(in GeoBird bird, float conjunctionAt)
        {
            float earliest = Math.Max(0f, conjunctionAt - ConjunctionTolerance);
            if (!bird.NextTheatrePass(earliest, out OrbitPass pass)) return false;
            if (pass.PeakAt < earliest && !bird.NextTheatrePass(pass.EndAt + 0.1f, out pass)) return false;
            return Math.Abs(pass.PeakAt - conjunctionAt) <= ConjunctionTolerance;
        }

        public static float RetimeShift(bool advance) => advance ? -GeoOrbit.RephaseStepSeconds : GeoOrbit.RephaseStepSeconds;

        public static bool TryDefenseRetime(in GeoBird bird, float now, float conjunctionAt, out bool advance)
        {
            advance = false;
            if (now >= conjunctionAt || !OpportunityRemains(bird, conjunctionAt)) return false;
            if (BreaksForecast(bird, now, conjunctionAt, false)) return true;
            advance = BreaksForecast(bird, now, conjunctionAt, true);
            return advance;
        }

        private static bool BreaksForecast(in GeoBird bird, float now, float conjunctionAt, bool advance) =>
            GeoSpace.PlanRephase(bird, now, RetimeShift(advance), out GeoBird planned) == GeoSpace.Refusal.None &&
                planned.ArrivalAt < conjunctionAt && !OpportunityRemains(planned, conjunctionAt);
    }
}
