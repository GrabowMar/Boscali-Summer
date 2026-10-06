using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal readonly struct SpaceImpact
    {
        public readonly float X, Z;
        public SpaceImpact(float x, float z) { X = x; Z = z; }
    }

    internal static class SpaceFireControl
    {
        public const float StandardCep = 120f, OpticalRadius = 25f, SarRadius = 30f;
        private const float LargestUnitSample = .99999994f;

        /// <summary>Only the host's immutable confirmed MARK provenance may select the optical/SAR disk.</summary>
        public static bool TrySample(float x, float z, bool confirmedMark, bool sarOnly,
            float angleSample, float radiusSample, out SpaceImpact impact)
        {
            impact = default;
            if (!Coordinate(x) || !Coordinate(z) || !float.IsFinite(angleSample) || !float.IsFinite(radiusSample)) return false;
            double angle = UnitSample(angleSample) * 2d * Math.PI;
            double sample = UnitSample(radiusSample);
            double maximum = sarOnly ? SarRadius : OpticalRadius;
            double radius = confirmedMark ? maximum * Math.Sqrt(sample) :
                StandardCep * Math.Sqrt(-Math.Log(1d - sample) / Math.Log(2d));
            double dx = Math.Cos(angle) * radius, dz = Math.Sin(angle) * radius;
            impact = new SpaceImpact((float)(x + dx), (float)(z + dz));
            if (confirmedMark && DistanceSquared(impact, x, z) > maximum * maximum)
            {
                // Float global coordinates can round an otherwise valid edge outside the disk.
                // Search only that edge case; every retained point satisfies the physical radius cap.
                double lower = 0, upper = 1;
                SpaceImpact inside = new SpaceImpact(x, z);
                for (int i = 0; i < 24; i++)
                {
                    double scale = (lower + upper) * .5d;
                    var candidate = new SpaceImpact((float)(x + dx * scale), (float)(z + dz * scale));
                    if (DistanceSquared(candidate, x, z) <= maximum * maximum) { lower = scale; inside = candidate; }
                    else upper = scale;
                }
                impact = inside;
            }
            return true;
        }

        /// <summary>The impact for a host-created TASKED aim: its fixed ground point and confirmed-MARK provenance.</summary>
        public static bool TrySampleAim(in TaskedAim aim, float angleSample, float radiusSample, out SpaceImpact impact) =>
            TrySample(aim.X, aim.Z, aim.ConfirmedMark, aim.SarOnly, angleSample, radiusSample, out impact);

        private static double DistanceSquared(in SpaceImpact impact, float x, float z)
        {
            double dx = (double)impact.X - x, dz = (double)impact.Z - z;
            return dx * dx + dz * dz;
        }

        private static float UnitSample(float value) => Math.Max(0, Math.Min(LargestUnitSample, value));
        private static bool Coordinate(float value) => float.IsFinite(value) && Math.Abs(value) <= 10000000f;
    }
}
