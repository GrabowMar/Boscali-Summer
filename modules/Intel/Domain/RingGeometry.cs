using System;
using BoscaliSummer.Core.Contracts;

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
                float dx = x - rings[i].X, dz = z - rings[i].Z;
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > radius) continue;
                covering++;
                if (!rings[i].Stale) anyFresh = true;
                if (radius - distance > depthMetres) depthMetres = radius - distance;
            }
        }

        /// <summary>
        /// Metres of the leg a→b inside any masked ring, sampled at the midpoints of 16 equal
        /// pieces. A zero-length leg is a point: use <see cref="Coverage"/>.
        /// </summary>
        public static float SegmentExposure(AirDefenceRing[] rings, int count, float ax, float az, float bx, float bz,
            float agl, byte mask, out bool anyFresh)
        {
            anyFresh = false;
            int n = Clamp(rings, count);
            float dx = bx - ax, dz = bz - az;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (n == 0 || !(length > 0f)) return 0f;
            const int samples = ThreatPictureLimits.SegmentSamples;
            int covered = 0;
            for (int s = 0; s < samples; s++)
            {
                float t = (s + 0.5f) / samples;
                float px = ax + dx * t, pz = az + dz * t;
                bool hit = false;
                for (int i = 0; i < n; i++)
                {
                    if (!InMask(rings[i].Kind, mask)) continue;
                    float radius = rings[i].EffectiveRadius(agl);
                    if (!(radius > 0f)) continue;
                    float rx = px - rings[i].X, rz = pz - rings[i].Z;
                    if (rx * rx + rz * rz > radius * radius) continue;
                    hit = true;
                    if (!rings[i].Stale)
                    {
                        anyFresh = true;
                        break;
                    }
                }
                if (hit) covered++;
            }
            return length * covered / samples;
        }

        private static int Clamp(AirDefenceRing[] rings, int count) =>
            rings == null || count <= 0 ? 0 : count < rings.Length ? count : rings.Length;
    }
}
