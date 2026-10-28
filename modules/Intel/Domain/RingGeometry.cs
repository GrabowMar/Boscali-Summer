using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Intel.Domain
{
    /// <summary>
    /// Distances against a faction's rings at one height above ground. Terrain masking is not
    /// modelled: every SAM needs line of sight, so these answers are conservative by design.
    /// </summary>
    internal static class RingGeometry
    {
        public static bool InMask(AirDefenceKind kind, byte mask) => (mask & ThreatPictureLimits.Bit(kind)) != 0;

        /// <summary>How many masked rings cover the point, the deepest penetration, and whether any is fresh.</summary>
        public static void Coverage(AirDefenceRing[] rings, int count, float x, float z, float agl, byte mask,
            out float depthMetres, out int covering, out bool anyFresh)
        {
            depthMetres = 0f;
            covering = 0;
            anyFresh = false;
            int n = Clamp(rings, count);
            for (int i = 0; i < n; i++)
            {
                if (!InMask(rings[i].Kind, mask)) continue;
                float radius = rings[i].EffectiveRadius(agl);
                if (!(radius > 0f)) continue;
                float distance = Scalar.Distance2D(x, z, rings[i].X, rings[i].Z);
                if (distance > radius) continue;
                covering++;
                if (!rings[i].Stale) anyFresh = true;
                if (radius - distance > depthMetres) depthMetres = radius - distance;
            }
        }

        private static int Clamp(AirDefenceRing[] rings, int count) =>
            rings == null || count <= 0 ? 0 : count < rings.Length ? count : rings.Length;
    }
}
