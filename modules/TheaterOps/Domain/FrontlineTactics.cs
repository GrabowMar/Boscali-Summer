using System;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>Small, deterministic formation rules over Command's existing front trace.</summary>
    internal static class FrontlineTactics
    {
        internal const int GroupSize = 6;
        internal const float LaneSpacing = 180f;

        internal static bool TrySlot(FrontlineTracePoint[] points, int[] lengths, int traceCount,
            float targetX, float targetZ, int lane,
            out float x, out float z, out float tangentX, out float tangentZ)
        {
            x = z = tangentX = tangentZ = 0f;
            if (points == null || lengths == null || traceCount <= 0 ||
                traceCount > lengths.Length || !Finite(targetX) || !Finite(targetZ) || lane < 0)
                return false;

            float best = float.MaxValue;
            int offset = 0;
            for (int trace = 0; trace < traceCount; trace++)
            {
                int length = lengths[trace];
                if (length < 0 || offset + length > points.Length) return false;
                for (int i = offset; i + 1 < offset + length; i++)
                {
                    float dx = points[i + 1].X - points[i].X;
                    float dz = points[i + 1].Z - points[i].Z;
                    float square = dx * dx + dz * dz;
                    if (!Finite(square) || square < 1f) continue;
                    float part = ((targetX - points[i].X) * dx +
                                  (targetZ - points[i].Z) * dz) / square;
                    part = Math.Max(0f, Math.Min(1f, part));
                    float px = points[i].X + part * dx;
                    float pz = points[i].Z + part * dz;
                    float distance = (targetX - px) * (targetX - px) +
                                     (targetZ - pz) * (targetZ - pz);
                    if (!Finite(distance) || distance >= best) continue;
                    float inverse = 1f / (float)Math.Sqrt(square);
                    best = distance;
                    x = px;
                    z = pz;
                    tangentX = dx * inverse;
                    tangentZ = dz * inverse;
                }
                offset += length;
            }
            if (best == float.MaxValue) return false;
            int column = lane == 0 ? 0 : (lane + 1) / 2 * (lane % 2 == 1 ? 1 : -1);
            x += column * LaneSpacing * tangentX;
            z += column * LaneSpacing * tangentZ;
            return Finite(x) && Finite(z);
        }

        internal static bool ShouldAdvance(int ready, int alive, float waitSeconds) =>
            alive > 0 && ready > 0 && (ready * 2 >= alive || waitSeconds >= 90f);

        internal static bool ShouldWithdraw(int alive, int formed) =>
            formed >= 3 && alive > 0 && alive * 2 < formed;

        internal static int PincerAxis(int rank, int viableGroups) =>
            viableGroups < 2 ? 0 : rank == 0 ? -1 : rank == 1 ? 1 : 0;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
