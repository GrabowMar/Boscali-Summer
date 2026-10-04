using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>Warning timeline of one rod: warn at <see cref="WarnAt"/>, release at <see cref="LaunchAt"/>, land at <see cref="ImpactAt"/>.</summary>
    internal readonly struct RodSchedule
    {
        public readonly float WarnAt, LaunchAt, ImpactAt;
        public RodSchedule(float warnAt, float launchAt, float impactAt) { WarnAt = warnAt; LaunchAt = launchAt; ImpactAt = impactAt; }
        public float LeadSeconds => ImpactAt - WarnAt;
        public bool Valid => SpaceRules.MissionTime(WarnAt) && SpaceRules.Finite(LaunchAt) && SpaceRules.Finite(ImpactAt);
    }

    /// <summary>
    /// Rod flight and warning arithmetic in mission time. The spec asks for a 10-14 s physical flight and a warning at
    /// least 15 s before impact, so a short host dwell before release makes up the difference: max(0, 15 - flight).
    /// </summary>
    internal static class RodTiming
    {
        public const float WarningSeconds = 15f;
        /// <summary>The geometry the native rod is released with (shared with the action that spawns it).</summary>
        public const float ReleaseAltitude = 20000f, ReleaseSpeed = 2500f;
        /// <summary>
        /// Lower bound of the flight until real rods have been measured: the 20 km fall at the 2.5 km/s release speed is 8 s
        /// without drag, and gravity can add well under 4 % of that speed, so no rod can land in under about 7.4 s. Using a
        /// bound that is too short only lengthens the dwell; the lead can never come out under 15 s because of it.
        /// </summary>
        public const float MinimumFlightSeconds = 7f;

        public static float Dwell(float flightSeconds) =>
            !SpaceRules.Finite(flightSeconds) || flightSeconds <= 0f ? WarningSeconds : Math.Max(0f, WarningSeconds - flightSeconds);

        public static RodSchedule Plan(float now, float flightSeconds)
        {
            float flight = !SpaceRules.Finite(flightSeconds) || flightSeconds <= 0f ? 0f : flightSeconds;
            float launch = now + Dwell(flightSeconds);
            return new RodSchedule(now, launch, launch + flight);
        }
    }

    /// <summary>
    /// The flight time the warning is planned with. Nothing is known before the first rod, so it starts at the geometric lower
    /// bound; each rod that really lands then adds its measured flight, and the estimate is the shortest of the recent ones so
    /// the planned lead is never overstated.
    /// </summary>
    internal sealed class RodFlightEstimate
    {
        public const int Window = 4;
        public const float ShortestPlausible = 3f, LongestPlausible = 60f;
        private readonly float[] samples = new float[Window];
        private int next;

        public int Count { get; private set; }

        public float Seconds
        {
            get
            {
                if (Count == 0) return RodTiming.MinimumFlightSeconds;
                float shortest = float.MaxValue;
                for (int i = 0; i < Count; i++) shortest = Math.Min(shortest, samples[i]);
                return shortest;
            }
        }

        public bool Record(float seconds)
        {
            if (!SpaceRules.Finite(seconds) || seconds < ShortestPlausible || seconds > LongestPlausible) return false;
            samples[next] = seconds;
            next = (next + 1) % Window;
            if (Count < Window) Count++;
            return true;
        }
    }
}
