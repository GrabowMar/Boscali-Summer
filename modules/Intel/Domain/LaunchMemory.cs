using System;

namespace BoscaliSummer.Modules.Intel.Domain
{
    internal struct LaunchEntry
    {
        public uint OwnerId;
        public int CellX;
        public int CellZ;
        public float X;
        public float Z;
        public float MaxRange;
        public float MinAltitude;
        public float MaxAltitude;
        public bool OwnerStatic;
        public float LastLaunch;
    }

    /// <summary>
    /// Where a faction has been shot at from: surface launch origins of anti-air missiles
    /// that entered its tracking, on a 500 m grid, with the missile's own envelope. This is
    /// how an IR belt that never emits becomes known. Origins age out 300 s after their last
    /// launch (mobile launcher) or 900 s (fixed); at most 64, the oldest makes room.
    /// </summary>
    internal sealed class LaunchMemory
    {
        public const int Capacity = 64;
        public const float CellMetres = 500f;
        public const float MobileSeconds = 300f;
        public const float StaticSeconds = 900f;

        private readonly LaunchEntry[] entries;
        private int count;

        public LaunchMemory(int capacity = Capacity)
        {
            entries = new LaunchEntry[capacity < 1 ? 1 : capacity];
        }

        public int Count => count;

        public LaunchEntry this[int index] => entries[index];

        public void Record(uint ownerId, float x, float z, bool ownerStatic, float maxRange, float minAltitude,
            float maxAltitude, float now)
        {
            if (float.IsNaN(x) || float.IsNaN(z) || !(maxRange > 0f)) return;
            if (minAltitude < 0f) minAltitude = 0f;
            int cx = (int)MathF.Floor(x / CellMetres), cz = (int)MathF.Floor(z / CellMetres);
            for (int i = 0; i < count; i++)
            {
                ref LaunchEntry entry = ref entries[i];
                if (entry.CellX != cx || entry.CellZ != cz || Math.Abs(entry.MaxRange - maxRange) > 1f ||
                    Math.Abs(entry.MinAltitude - minAltitude) > 1f || Math.Abs(entry.MaxAltitude - maxAltitude) > 1f)
                    continue;
                entry.OwnerId = ownerId;
                entry.OwnerStatic = ownerStatic;
                entry.LastLaunch = now;
                return;
            }
            int slot = count < entries.Length ? count++ : Oldest();
            entries[slot] = new LaunchEntry
            {
                OwnerId = ownerId,
                CellX = cx,
                CellZ = cz,
                X = (cx + 0.5f) * CellMetres,
                Z = (cz + 0.5f) * CellMetres,
                MaxRange = maxRange,
                MinAltitude = minAltitude,
                MaxAltitude = maxAltitude,
                OwnerStatic = ownerStatic,
                LastLaunch = now
            };
        }

        public void Prune(float now)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                float limit = entries[i].OwnerStatic ? StaticSeconds : MobileSeconds;
                if (!(now - entries[i].LastLaunch > limit)) continue;
                entries[i] = entries[--count];
                entries[count] = default;
            }
        }

        public void Clear()
        {
            Array.Clear(entries, 0, count);
            count = 0;
        }

        private int Oldest()
        {
            int oldest = 0;
            for (int i = 1; i < count; i++)
                if (entries[i].LastLaunch < entries[oldest].LastLaunch) oldest = i;
            return oldest;
        }
    }
}
