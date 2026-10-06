
namespace BoscaliSummer.Modules.AirSurvival.Domain
{
    internal static class AirStationPolicy
    {
        internal const int MaximumObjectives = 12;
        private const float NearbyObjectiveRange = 25000f;

        // Stable slots spread idle sorties without sending them across the theater.
        internal static int ChooseObjectiveRank(int identity, float nearest, float second, float third)
        {
            int choices = 1;
            if (second - nearest <= NearbyObjectiveRange) choices++;
            if (third - nearest <= NearbyObjectiveRange) choices++;
            return (identity & int.MaxValue) % choices;
        }

        internal static bool TryOperationFix(int identity, float x, float z, float radius,
                                              out float stationX, out float stationZ)
        {
            stationX = stationZ = 0f;
            if ((identity & 1) != 0 || !float.IsFinite(x) || !float.IsFinite(z) || !float.IsFinite(radius) ||
                radius < 0f || radius > 6000f) return false;

            float offset = radius * 0.35f;
            stationX = x + (((identity & 2) == 0) ? offset : -offset);
            stationZ = z + (((identity & 4) == 0) ? offset : -offset);
            return float.IsFinite(stationX) && float.IsFinite(stationZ);
        }

    }

    internal static class AirFlarePolicy
    {
        internal const int MaximumTrackedAircraft = 64;
        internal const int MaximumMissilesPerCheck = 8;
        internal const float CheckSeconds = .2f;

        // A last-second flare supplements the native pilot's warning/reaction cycle.
        internal static bool ShouldPop(float distanceMeters, float closingMetersPerSecond,
            float flareProportion, bool alreadyFiring, float secondsSinceFlare)
        {
            if (alreadyFiring || !float.IsFinite(distanceMeters) || !float.IsFinite(closingMetersPerSecond) ||
                !float.IsFinite(flareProportion) || !float.IsFinite(secondsSinceFlare) ||
                distanceMeters <= 0f || closingMetersPerSecond < 50f ||
                flareProportion <= .05f || secondsSinceFlare < 3f)
                return false;
            float timeToImpact = distanceMeters / closingMetersPerSecond;
            return timeToImpact >= .15f && timeToImpact <= 3.5f;
        }

    }
}
