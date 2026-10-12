using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Who stacks with whom on the relief map. The grid is anchored to the world, not the screen:
    /// a pan or an orbit never changes a stack, and the cell changes size only when the zoom
    /// crosses a power of two. Two cells whose contacts sit together across their shared border
    /// merge into the bigger one, so one knot of units reads as one stack instead of two counts
    /// side by side. Sides never mix. Pure: positions in, stack ids out.
    /// </summary>
    internal sealed class ReliefClusters
    {
        internal const int Capacity = 2048;
        /// <summary>Neighbouring cells merge when their centres of mass are closer than this share of a cell.</summary>
        internal const float MergeReach = .7f;

        private readonly Dictionary<(int, int, int), int> cells = new Dictionary<(int, int, int), int>(256);
        private readonly int[] cellOf = new int[Capacity];
        private readonly int[] cellX = new int[Capacity], cellZ = new int[Capacity], side = new int[Capacity];
        private readonly float[] sumX = new float[Capacity], sumZ = new float[Capacity];
        private readonly int[] own = new int[Capacity], total = new int[Capacity], root = new int[Capacity];
        private readonly long[] rank = new long[Capacity];
        private readonly int[] order = new int[Capacity];
        private float cell = 1f;
        private int points, count;

        /// <summary>
        /// The cell edge in metres for a view scale: about <paramref name="pixels"/> on screen, snapped
        /// to a power of two so a slow zoom holds the same grid until it has really changed.
        /// </summary>
        internal static float CellMetres(float metresPerPixel, float pixels)
        {
            float wanted = metresPerPixel * pixels;
            if (!(wanted > 1e-3f) || float.IsInfinity(wanted)) return 1f;
            return (float)Math.Pow(2d, Math.Round(Math.Log(wanted, 2d)));
        }

        internal void Begin(float cellMetres)
        {
            cell = cellMetres > 1e-3f ? cellMetres : 1f;
            cells.Clear();
            points = count = 0;
        }

        /// <summary>Adds a contact; returns its handle for <see cref="StackOf"/>, or -1 when full.</summary>
        internal int Add(float x, float z, int sideKey)
        {
            if (points >= Capacity || float.IsNaN(x) || float.IsNaN(z)) return -1;
            int gx = (int)Math.Floor(x / cell), gz = (int)Math.Floor(z / cell);
            var key = (gx, gz, sideKey);
            if (!cells.TryGetValue(key, out int at))
            {
                at = count++;
                cells.Add(key, at);
                cellX[at] = gx; cellZ[at] = gz; side[at] = sideKey;
                sumX[at] = sumZ[at] = 0f;
                own[at] = 0;
            }
            sumX[at] += x; sumZ[at] += z;
            own[at]++;
            cellOf[points] = at;
            return points++;
        }

        /// <summary>Merges cells across borders. Biggest first, so a knot keeps one stable owner.</summary>
        internal void Solve()
        {
            for (int i = 0; i < count; i++)
            {
                root[i] = i;
                total[i] = own[i];
                order[i] = i;
                rank[i] = ((long)(Capacity - own[i]) << 32) | (uint)i;
            }
            Array.Sort(rank, order, 0, count);
            float reach = MergeReach * cell, reachSquared = reach * reach;
            for (int o = 0; o < count; o++)
            {
                int a = order[o];
                if (root[a] != a) continue;
                float ax = sumX[a] / own[a], az = sumZ[a] / own[a];
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dz == 0) ||
                        !cells.TryGetValue((cellX[a] + dx, cellZ[a] + dz, side[a]), out int b)) continue;
                    // An absorbed cell, or one that has itself absorbed, stays where it is: no chains.
                    if (root[b] != b || total[b] != own[b] || own[b] > own[a]) continue;
                    float bx = sumX[b] / own[b] - ax, bz = sumZ[b] / own[b] - az;
                    if (bx * bx + bz * bz >= reachSquared) continue;
                    root[b] = a;
                    total[a] += own[b];
                }
            }
        }

        /// <summary>The stack a contact ended in. Ids are only comparable within one solve.</summary>
        internal int StackOf(int handle) => handle < 0 || handle >= points ? -1 : root[cellOf[handle]];

        /// <summary>How many contacts a stack holds.</summary>
        internal int Size(int stack) => stack < 0 || stack >= count ? 0 : total[stack];
    }
}
