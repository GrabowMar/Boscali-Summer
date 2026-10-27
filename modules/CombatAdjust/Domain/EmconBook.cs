using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.CombatAdjust.Domain
{
    /// <summary>
    /// Which radars this module has switched off and when each comes back: dark from the reaction delay until 5 s after the last missile
    /// is gone, never longer than 45 s. Only radars it switched off are ever switched back on.
    /// </summary>
    internal sealed class EmconBook
    {
        public const float AfterSeconds = 5f, CapSeconds = 45f;

        private struct Entry
        {
            public float Start, Delay, Gone;
            public bool Switched;
        }

        private readonly Dictionary<uint, Entry> entries = new Dictionary<uint, Entry>();
        private readonly List<uint> scratch = new List<uint>();

        /// <summary>A missile is coming for this radar. A second one while it hides keeps it dark, inside the first one's cap.</summary>
        public void Darken(uint radarUnitId, float now, float delay)
        {
            if (entries.TryGetValue(radarUnitId, out Entry e))
            {
                if (now < End(e))
                {
                    e.Gone = float.PositiveInfinity;
                    entries[radarUnitId] = e;
                    return;
                }
                entries[radarUnitId] = new Entry { Start = now, Delay = delay, Gone = float.PositiveInfinity, Switched = e.Switched };
                return;
            }
            entries[radarUnitId] = new Entry { Start = now, Delay = delay, Gone = float.PositiveInfinity };
        }

        public void MissileGone(uint radarUnitId, float now)
        {
            if (entries.TryGetValue(radarUnitId, out Entry e) && float.IsPositiveInfinity(e.Gone))
            {
                e.Gone = now;
                entries[radarUnitId] = e;
            }
        }

        public bool ShouldBeDark(uint radarUnitId, float now) => entries.TryGetValue(radarUnitId, out Entry e) && Dark(e, now);

        /// <summary>Fills the radars to switch off and back on now (each change once); returns how many changed.</summary>
        public int Due(float now, List<uint> darkOut, List<uint> lightOut)
        {
            int changed = 0;
            scratch.Clear();
            foreach (KeyValuePair<uint, Entry> kv in entries) scratch.Add(kv.Key);
            foreach (uint id in scratch)
            {
                Entry e = entries[id];
                bool want = Dark(e, now);
                if (want && !e.Switched) { e.Switched = true; darkOut.Add(id); changed++; }
                else if (!want && e.Switched) { e.Switched = false; lightOut.Add(id); changed++; }
                if (!e.Switched && now >= End(e)) entries.Remove(id);
                else entries[id] = e;
            }
            return changed;
        }

        /// <summary>Everything this module switched off, to switch back on at once (rule off, scene end), and forget the rest.</summary>
        public void ReleaseAll(List<uint> lightOut)
        {
            foreach (KeyValuePair<uint, Entry> kv in entries)
                if (kv.Value.Switched) lightOut.Add(kv.Key);
            entries.Clear();
        }

        private static bool Dark(Entry e, float now) => now >= e.Start + e.Delay && now < End(e);

        private static float End(Entry e) => Math.Min(e.Gone + AfterSeconds, e.Start + e.Delay + CapSeconds);
    }
}
