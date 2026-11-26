using System;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    internal static class AsatRules
    {
        public const float Clearance = 0.16f;
        private const float EvadeDistance = 0.20f;
        private const int TrackSectors = 8;

        public static float TrackSector(float position) =>
            (Math.Min(TrackSectors - 1, (int)(Math.Clamp(position, 0f, 1f) * TrackSectors)) + 0.5f) / TrackSectors;

        public static bool Clears(in GeoBird bird, float impactAt, float aimU, float aimV) =>
            !bird.Covers(impactAt, aimU, aimV, Clearance);

        public static bool TryEvade(in GeoBird bird, float now, float impactAt, float aimU, float aimV, out float u, out float v)
        {
            u = v = 0f;
            if (now >= impactAt || bird.Moving(now) || Clears(bird, impactAt, aimU, aimV)) return false;
            float best = float.PositiveInfinity;
            for (int direction = 0; direction < 4; direction++)
            {
                float x = aimU + (direction == 0 ? EvadeDistance : direction == 1 ? -EvadeDistance : 0f);
                float y = aimV + (direction == 2 ? EvadeDistance : direction == 3 ? -EvadeDistance : 0f) / Math.Max(GeoBird.Aspect, 0.01f);
                GeoBird candidate = bird.Relocate(now, x, y);
                if (candidate.Fuel >= bird.Fuel || candidate.Length >= best || !Clears(candidate, impactAt, aimU, aimV)) continue;
                best = candidate.Length;
                u = candidate.ToU;
                v = candidate.ToV;
            }
            return float.IsFinite(best);
        }
    }
}
