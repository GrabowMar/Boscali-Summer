using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Scales one ground radar's detection range. Verified vanilla seam: <c>Radar.RadarParameters</c> is a public <c>RadarParams</c> struct field and
    /// its <c>maxRange</c> drives both <c>Radar.GetRadarRange()</c> and the in-range test and signal in <c>Radar.RadarCheck()</c>. The first touch of a
    /// radar remembers its own baseline, so a restore always writes back the original number and a scene reset never leaves a jammed radar behind.
    /// </summary>
    internal sealed class CyberRadarScaler
    {
        private sealed class Entry { public Radar Radar; public float Baseline, Factor; public bool Touched; }

        private readonly Dictionary<int, Entry> applied = new Dictionary<int, Entry>(32);
        private readonly List<int> drop = new List<int>(8);

        public bool Active => applied.Count > 0;

        public void Begin()
        {
            foreach (var pair in applied) pair.Value.Touched = false;
        }

        public void Apply(Radar radar, float factor)
        {
            if (radar == null) return;
            int id = radar.GetInstanceID();
            if (factor >= 0.999f)
            {
                if (applied.TryGetValue(id, out Entry back)) { Restore(back); applied.Remove(id); }
                return;
            }
            if (!applied.TryGetValue(id, out Entry entry))
            {
                if (applied.Count >= 256) return;
                entry = new Entry { Radar = radar, Baseline = radar.RadarParameters.maxRange, Factor = 1f };
                applied.Add(id, entry);
            }
            entry.Touched = true;
            if (Mathf.Abs(entry.Factor - factor) < 0.001f) return;
            RadarParams p = radar.RadarParameters;
            p.maxRange = entry.Baseline * factor;
            radar.RadarParameters = p;
            entry.Factor = factor;
        }

        /// <summary>Radars no effect touched this pass are restored (an effect ended) or forgotten (the radar was destroyed).</summary>
        public void End()
        {
            drop.Clear();
            foreach (var pair in applied)
                if (!pair.Value.Touched || pair.Value.Radar == null) drop.Add(pair.Key);
            for (int i = 0; i < drop.Count; i++)
            {
                Entry e = applied[drop[i]];
                Restore(e);
                applied.Remove(drop[i]);
            }
            drop.Clear();
        }

        public void RestoreAll()
        {
            foreach (var pair in applied) Restore(pair.Value);
            applied.Clear();
        }

        private static void Restore(Entry e)
        {
            if (e.Radar == null) return;
            RadarParams p = e.Radar.RadarParameters;
            p.maxRange = e.Baseline;
            e.Radar.RadarParameters = p;
        }
    }
}
