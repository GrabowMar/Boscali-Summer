using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>
    /// STARFALL (SPACE R4): a line of small kinetic rods walked across the aim. 3 to 5 rods by power, 300 m apart, along the line from the requesting aircraft through the aim (centred on the aim), released
    /// 0.4 s apart, each about 35 % of an ORBITAL ROD. Pure, so the impact points are tested without the game.
    /// </summary>
    internal static class StarfallPlan
    {
        public const float Spacing = 300f, ReleaseGap = 0.4f, BlastScale = 0.35f;
        public const int MinRods = 3, MaxRods = 5;

        /// <summary>3 at power 0.6, 5 at power 1.5: round(3 + 2 x clamp01((power - 0.6) / 0.9)).</summary>
        public static int Count(float power)
        {
            if (float.IsNaN(power) || float.IsInfinity(power)) power = 1f;
            float t = Math.Max(0f, Math.Min(1f, (power - 0.6f) / 0.9f));
            return (int)Math.Round(MinRods + (MaxRods - MinRods) * t, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// The impact points, the first nearest the aircraft and each next one <see cref="Spacing"/> further along the line, the aim in the middle. With the aircraft on top of the aim (or unknown) the line runs north.
        /// </summary>
        public static void Points(float aimX, float aimZ, float fromX, float fromZ, bool hasFrom, int count, SpaceImpact[] into)
        {
            double dx = 0d, dz = 1d;
            if (hasFrom)
            {
                double ax = (double)aimX - fromX, az = (double)aimZ - fromZ, len = Math.Sqrt(ax * ax + az * az);
                if (len > 1d) { dx = ax / len; dz = az / len; }
            }
            count = Math.Max(0, Math.Min(count, into.Length));
            for (int i = 0; i < count; i++)
            {
                double along = Spacing * (i - (count - 1) * 0.5d);
                into[i] = new SpaceImpact((float)(aimX + dx * along), (float)(aimZ + dz * along));
            }
        }

        /// <summary>Seconds after the request the i-th rod is released.</summary>
        public static float ReleaseAt(int index) => ReleaseGap * Math.Max(0, index);
    }
}
