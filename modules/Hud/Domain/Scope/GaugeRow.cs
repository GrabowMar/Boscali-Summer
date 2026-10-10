using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Which gauges the four slots of the gauge row show, pure.</summary>
    internal static class GaugeRow
    {
        public const int Slots = 4;

        /// <summary>Where the chevron of a Mode gauge's progress rail sits for <paramref name="progress"/> (0..1, <see cref="GaugeSpec.Value"/>): the rail runs from the cell's left pad to the diamond.</summary>
        public static float RailX(float cellX, float cellW, float progress) => cellX + 6f + (cellW - 28f) * (progress < 0f ? 0f : progress > 1f ? 1f : progress);

        /// <summary>
        /// The page's own gauges first (in its order), then the registered ones by rank (registration order among equals), skipping any
        /// whose id the page already shows, at most <see cref="Slots"/>. Returns how many slots are filled.
        /// </summary>
        public static int Merge(GaugeSpec[] page, int pageCount, GaugeSpec[] registered, int registeredCount, GaugeSpec[] into)
        {
            int n = 0;
            for (int i = 0; i < pageCount && n < Slots && n < into.Length; i++) into[n++] = page[i];
            int pageEnd = n;
            ulong taken = 0;
            while (n < Slots && n < into.Length)
            {
                int best = -1;
                for (int i = 0; i < registeredCount && i < 64; i++)
                {
                    if ((taken & (1UL << i)) != 0) continue;
                    if (best < 0 || registered[i].Rank > registered[best].Rank) best = i;
                }
                if (best < 0) break;
                taken |= 1UL << best;
                bool dup = false;
                for (int k = 0; k < pageEnd; k++) if (into[k].Id == registered[best].Id) dup = true;
                if (!dup) into[n++] = registered[best];
            }
            return n;
        }
    }
}
