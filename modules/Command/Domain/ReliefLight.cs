using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The relief map's baked grey shade: one hillshade value per heightfield sample,
    /// multiplied with the intel texture by an unlit material. Pure so the look stays
    /// testable. Steep ground sits deeper in its own shadow than its facing alone
    /// gives, and high ground lifts slightly toward the snowfields.
    /// </summary>
    internal static class ReliefLight
    {
        /// <summary>Samples of shore glow falloff; beyond reads as open ground or sea.</summary>
        internal const int CoastReach = 6;
        private const float Base = 150f;
        private const float Gain = 90f;
        private const float Minimum = 80f;
        private const float Maximum = 248f;
        private const float PeakLift = 12f;

        /// <summary>
        /// Distance of every cell to the nearest cell of the opposite medium: 0 touches
        /// the shoreline, <see cref="CoastReach"/> is open ground or sea. Empty on bad input.
        /// </summary>
        internal static byte[] CoastDistance(bool[] land, int side)
        {
            if (land == null || side < 3 || land.Length != side * side)
                return new byte[0];
            var distance = new byte[land.Length];
            for (int i = 0; i < distance.Length; i++) distance[i] = CoastReach;
            var queue = new int[land.Length];
            int head = 0, tail = 0;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int at = z * side + x;
                bool medium = land[at];
                if ((x > 0 && land[at - 1] != medium) ||
                    (x < side - 1 && land[at + 1] != medium) ||
                    (z > 0 && land[at - side] != medium) ||
                    (z < side - 1 && land[at + side] != medium))
                {
                    distance[at] = 0;
                    queue[tail++] = at;
                }
            }
            while (head < tail)
            {
                int at = queue[head++];
                int next = distance[at] + 1;
                if (next >= CoastReach) continue;
                int x = at % side, z = at / side;
                Relax(x > 0 ? at - 1 : -1);
                Relax(x < side - 1 ? at + 1 : -1);
                Relax(z > 0 ? at - side : -1);
                Relax(z < side - 1 ? at + side : -1);

                void Relax(int cell)
                {
                    if (cell < 0 || distance[cell] != CoastReach) return;
                    distance[cell] = (byte)next;
                    queue[tail++] = cell;
                }
            }
            return distance;
        }

        /// <summary>
        /// Shades one ground vertex, 0..255 grey. Facing is dot(normal, light);
        /// slopeDrop is metres of elevation change per heightfield sample; heights
        /// are metres above the sea. The beach rim catches light on the land side.
        /// </summary>
        internal static byte Shade(float facing, float slopeDrop, float heightAboveSea,
            float peakHeight, int coast, bool land)
        {
            if (float.IsNaN(facing)) facing = 0f;
            if (float.IsNaN(slopeDrop)) slopeDrop = 0f;
            if (float.IsNaN(heightAboveSea)) heightAboveSea = 0f;
            if (float.IsNaN(peakHeight)) peakHeight = 1f;
            float light = Base + Gain * Clamp(facing, -1f, 1f);
            light *= 1f - Math.Min(Math.Max(slopeDrop, 0f) / 40f, 1f) * .2f;
            if (land)
            {
                light += PeakLift * Math.Min(Math.Max(heightAboveSea, 0f) /
                    Math.Max(peakHeight, 1f), 1f);
                if (coast <= 0) light += 12f;
            }
            if (light < Minimum) return (byte)Minimum;
            if (light > Maximum) return (byte)Maximum;
            return (byte)Math.Round(light);
        }

        private static float Clamp(float value, float minimum, float maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
