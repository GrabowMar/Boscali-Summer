using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace NOAvionics
{
    public enum AvTickRate { Fast, Slow }

    /// <summary>
    /// One scheduler per console (spec §8): 10 Hz live values, 2 Hz slow data, and a restyle pass when the
    /// theme generation changes. Entries bound to a hidden page do not run. Hard ceiling of 256 entries.
    /// </summary>
    public sealed class AvTicker : MonoBehaviour
    {
        public const int MaxEntries = 256;
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("NOA.UI.Tick");
        private static readonly ProfilerMarker RestyleMarker = new ProfilerMarker("NOA.UI.Restyle");

        private struct Entry { public int Page; public AvTickRate Rate; public Action Tick; }
        private readonly List<Entry> entries = new List<Entry>(32);
        private readonly List<AvPart> parts = new List<AvPart>(64);
        private float nextFast, nextSlow;
        private int generation = -1;

        public int ActivePage { get; set; }

        public bool Add(int page, AvTickRate rate, Action tick)
        {
            if (tick == null || entries.Count >= MaxEntries) return false;
            entries.Add(new Entry { Page = page, Rate = rate, Tick = tick });
            return true;
        }

        public void Register(AvPart part) { if (part != null && parts.Count < 4096) parts.Add(part); }

        public void RestyleAll()
        {
            using (RestyleMarker.Auto())
                for (int i = 0; i < parts.Count; i++) parts[i].Restyle();
            generation = AvStyleHost.FuiGeneration;
        }

        private void Update()
        {
            if (generation != AvStyleHost.FuiGeneration) RestyleAll();
            float now = Time.unscaledTime;
            bool fast = now >= nextFast, slow = now >= nextSlow;
            if (!fast && !slow) return;
            if (fast) nextFast = now + 0.1f;
            if (slow) nextSlow = now + 0.5f;
            Run(fast, slow);
        }

        /// <summary>Runs one fast (and optionally slow) tick now: offline harnesses, where time does not advance.</summary>
        public void TickNow(bool slow = true) => Run(true, slow);

        private void Run(bool fast, bool slow)
        {
            using (TickMarker.Auto())
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    if (e.Page >= 0 && e.Page != ActivePage) continue;
                    if ((e.Rate == AvTickRate.Fast && fast) || (e.Rate == AvTickRate.Slow && slow))
                    {
                        try { e.Tick(); }
                        catch (Exception ex) { Debug.LogWarning("[NOAvionics] tick failed and was removed: " + ex.Message); entries.RemoveAt(i); i--; }
                    }
                }
            }
        }
    }
}
