using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Intel.Domain
{
    /// <summary>
    /// The release-point search behind IThreatPicture.TryFindAttackProfile: 9 release points on
    /// a ±80° arc of the release range around the target, facing the attacker, scored by
    /// 10 × radar-cone metres + point-defence metres of the straight transit leg at transit
    /// height. Ties go to a release point outside every ring at release height, then to the
    /// earlier candidate (the direct approach first). Allocation-free: at most 9 × 2 × 16
    /// samples against at most 96 rings.
    /// </summary>
    internal static class AttackProfileSearch
    {
        public const int Candidates = 9;
        public const float ArcStepDegrees = 20f;
        public const float RadarConeWeight = 10f;

        private static readonly int[] Steps = { 0, -1, 1, -2, 2, -3, 3, -4, 4 };

        public static bool TryFind(AirDefenceRing[] rings, int count, float fromX, float fromZ, float targetX,
            float targetZ, float releaseRange, float transitAgl, float releaseAgl, out AttackProfile profile)
        {
            profile = default;
            if (!float.IsFinite(fromX) || !float.IsFinite(fromZ) || !float.IsFinite(targetX) || !float.IsFinite(targetZ) || !float.IsFinite(transitAgl) ||
                !float.IsFinite(releaseAgl) || !float.IsFinite(releaseRange) || !(releaseRange > 0f))
                return false;

            float bearing = MathF.Atan2(fromZ - targetZ, fromX - targetX);
            float bestScore = float.PositiveInfinity;
            bool bestCovered = true;
            for (int k = 0; k < Candidates; k++)
            {
                float angle = bearing + Steps[k] * ArcStepDegrees * (MathF.PI / 180f);
                float rx = targetX + MathF.Cos(angle) * releaseRange;
                float rz = targetZ + MathF.Sin(angle) * releaseRange;
                float radar = RingGeometry.SegmentExposure(rings, count, fromX, fromZ, rx, rz, transitAgl,
                    ThreatPictureLimits.RadarConeMask, out bool radarFresh);
                float point = RingGeometry.SegmentExposure(rings, count, fromX, fromZ, rx, rz, transitAgl,
                    ThreatPictureLimits.PointDefenceMask, out bool pointFresh);
                RingGeometry.Coverage(rings, count, rx, rz, releaseAgl, ThreatPictureLimits.AllRingsMask,
                    out _, out int covering, out bool coverFresh);
                float score = RadarConeWeight * radar + point;
                bool covered = covering > 0;
                bool better = score < bestScore || (score == bestScore && bestCovered && !covered);
                if (!better) continue;
                bestScore = score;
                bestCovered = covered;
                bool exposed = radar > 0f || point > 0f || covered;
                bool staleOnly = exposed && !radarFresh && !pointFresh && !coverFresh;
                profile = new AttackProfile(rx, rz, radar, point, covered, staleOnly);
                if (bestScore <= 0f && !bestCovered) break;
            }
            return true;
        }

    }
}
