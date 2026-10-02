using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Intel.Domain
{
    /// <summary>
    /// Where a faction has looked: 2 km cells (at most 4096) stamped with the time own units
    /// last saw them. A unit stamps every cell whose centre lies within its radius of its own
    /// cell's centre; one stamp per source cell and radius per pass keeps a 300-unit garrison
    /// from stamping the same ground 300 times.
    /// </summary>
    internal sealed class ObservationGrid
    {
        public const float CellMetres = 2000f;
        public const int DefaultCapacity = 4096;
        public const float ScoutedSeconds = 240f;
        public const float GroundRadius = 6000f;
        public const float AircraftRadius = 10000f;
        public const float RadarRadius = 15000f;

        private readonly Dictionary<int, float> stamps;
        private readonly HashSet<long> passSources = new HashSet<long>();
        private readonly int capacity;

        public ObservationGrid(int capacity = DefaultCapacity)
        {
            this.capacity = capacity < 1 ? 1 : capacity;
            stamps = new Dictionary<int, float>(this.capacity);
        }

        public int Count => stamps.Count;

        public bool Overflowed { get; private set; }

        public static int CellOf(float metres) => (int)MathF.Floor(metres / CellMetres);

        public void BeginPass() => passSources.Clear();

        public void Stamp(float x, float z, float radius, float now)
        {
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsNaN(now) || !(radius > 0f)) return;
            int cx = CellOf(x), cz = CellOf(z);
            long source = ((long)(uint)Key(cx, cz) << 8) | (uint)((int)(radius / 1000f) & 0xFF);
            if (!passSources.Add(source)) return;
            int reach = (int)MathF.Ceiling(radius / CellMetres);
            float limit = radius * radius;
            for (int ix = cx - reach; ix <= cx + reach; ix++)
            {
                for (int iz = cz - reach; iz <= cz + reach; iz++)
                {
                    float dx = (ix - cx) * CellMetres, dz = (iz - cz) * CellMetres;
                    if (dx * dx + dz * dz > limit) continue;
                    int key = Key(ix, iz);
                    if (stamps.TryGetValue(key, out float previous))
                    {
                        if (now > previous) stamps[key] = now;
                        continue;
                    }
                    if (stamps.Count >= capacity)
                    {
                        Overflowed = true;
                        continue;
                    }
                    stamps.Add(key, now);
                }
            }
        }

        /// <summary>
        /// The freshest stamp of the cell holding (x, z) or of any cell whose centre lies within
        /// <paramref name="radius"/>; NaN when none of them was ever looked at.
        /// </summary>
        public float LastStamp(float x, float z, float radius)
        {
            if (float.IsNaN(x) || float.IsNaN(z)) return float.NaN;
            if (!(radius > 0f)) radius = 0f;
            int cx = CellOf(x), cz = CellOf(z);
            int reach = (int)MathF.Ceiling(radius / CellMetres);
            float limit = radius * radius;
            float best = float.NaN;
            for (int ix = cx - reach; ix <= cx + reach; ix++)
            {
                for (int iz = cz - reach; iz <= cz + reach; iz++)
                {
                    if (ix != cx || iz != cz)
                    {
                        float dx = (ix + 0.5f) * CellMetres - x, dz = (iz + 0.5f) * CellMetres - z;
                        if (dx * dx + dz * dz > limit) continue;
                    }
                    if (stamps.TryGetValue(Key(ix, iz), out float stamp) && (float.IsNaN(best) || stamp > best))
                        best = stamp;
                }
            }
            return best;
        }

        /// <summary>Scouted when looked at within 240 s. Never looked at: unscouted, age NaN, never 0.</summary>
        public void Read(float x, float z, float radius, float now, out bool scouted, out float ageSeconds)
        {
            float stamp = LastStamp(x, z, radius);
            if (float.IsNaN(stamp))
            {
                scouted = false;
                ageSeconds = float.NaN;
                return;
            }
            ageSeconds = now - stamp > 0f ? now - stamp : 0f;
            scouted = ageSeconds <= ScoutedSeconds;
        }

        public void Clear()
        {
            stamps.Clear();
            passSources.Clear();
            Overflowed = false;
        }

        private static int Key(int cx, int cz) => ((cx & 0xFFFF) << 16) | (cz & 0xFFFF);
    }
}
