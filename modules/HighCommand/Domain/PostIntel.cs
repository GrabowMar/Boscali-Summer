namespace BoscaliSummer.Modules.HighCommand.Domain
{
    /// <summary>A confirmed post as one observer last saw it.</summary>
    internal readonly struct PostSighting
    {
        /// <summary>Seconds since the last sighting, clamped to 0..PostIntel.MaximumAge.</summary>
        public readonly float Age;
        /// <summary>GLOBAL metres of the building when it was last sighted.</summary>
        public readonly float X, Y, Z;

        public PostSighting(float age, float x, float y, float z) { Age = age; X = x; Y = y; Z = z; }
    }

    /// <summary>What each faction has confirmed about enemy command posts: last sighting time and global position per (observer, key).</summary>
    internal sealed class PostIntel
    {
        public const int Observers = 8, Keys = 64;
        /// <summary>The wire bound for an intel age; older reads as this.</summary>
        public const float MaximumAge = 600f;
        // A stamp of 0 means never seen, so a sighting at mission time 0 (or on an unreadable clock) is stored just above it.
        private const float MinimumStamp = 0.001f;

        private struct Record { public float Seen, X, Y, Z; }

        private readonly Record[] records = new Record[Observers * Keys];

        private static bool InRange(int observer, int key) => observer >= 0 && observer < Observers && key >= 0 && key < Keys;

        /// <summary>Record a sighting of the post building. True the first time since the key was last forgotten. Out-of-range observer or key, or a position that is not finite: ignored, false.</summary>
        public bool Confirm(int observer, int key, float now, float gx, float gy, float gz)
        {
            if (!InRange(observer, key) || !float.IsFinite(gx) || !float.IsFinite(gy) || !float.IsFinite(gz)) return false;
            ref Record record = ref records[observer * Keys + key];
            bool first = record.Seen <= 0f;
            record.Seen = float.IsFinite(now) && now > MinimumStamp ? now : MinimumStamp;
            record.X = gx; record.Y = gy; record.Z = gz;
            return first;
        }

        /// <summary>The record, or false when this observer never confirmed that post (or it fell since).</summary>
        public bool TryGet(int observer, int key, float now, out PostSighting sighting)
        {
            sighting = default;
            if (!InRange(observer, key)) return false;
            Record record = records[observer * Keys + key];
            if (record.Seen <= 0f) return false;
            float age = now - record.Seen;
            // A clock that went backwards reads fresh; one that cannot be read reads as the oldest, never as fresh.
            age = float.IsNaN(age) ? MaximumAge : age < 0f ? 0f : age > MaximumAge ? MaximumAge : age;
            sighting = new PostSighting(age, record.X, record.Y, record.Z);
            return true;
        }

        /// <summary>Seconds-clock of the last sighting, 0 when never.</summary>
        public float LastSight(int observer, int key) => InRange(observer, key) ? records[observer * Keys + key].Seen : 0f;

        /// <summary>Drop every observer's record of this key: the post fell or was rebuilt, so nobody knows where it stands.</summary>
        public void Forget(int key)
        {
            if (key < 0 || key >= Keys) return;
            for (int observer = 0; observer < Observers; observer++) records[observer * Keys + key] = default;
        }

        /// <summary>Forget everything (scene reset).</summary>
        public void Clear() => System.Array.Clear(records, 0, records.Length);
    }
}
