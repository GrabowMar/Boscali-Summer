using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The scope's colour tokens (HUD v8). Ink, caution and warning follow the player's HUD theme; the rest are derived from it.
    /// Amber and red are ONLY for caution and warning.</summary>
    internal enum ScopeTone : byte { Ink, Hi, Dim, Faint, Amber, Red, Plate, Bg, White }

    /// <summary>How a gauge reads: ring = time (cooldown, countdown), arc = amount, bar = amount with threshold ticks, needle = bearing,
    /// mode = state word with a progress rail, bars = signal strength.</summary>
    internal enum GaugeKind : byte { Arc, Ring, Bars, Bar, Needle, Mode }

    /// <summary>
    /// One mini-gauge of the board's gauge row (HUD v8): a label, a short figure and a shape. Strings are built by the provider only when
    /// they change (the painter copies, it never allocates). <c>Sub</c> is at most 7 characters (the shell does not wrap), amber and red
    /// only when the figure is a caution or warning, <c>Flash</c> only for warnings.
    /// </summary>
    internal struct GaugeSpec
    {
        /// <summary>Stable id ("lance", "perk", "radio", "ap", "ace"...): a page gauge with the same id displaces the registered one.</summary>
        public string Id;
        public string Label, Text, Sub;
        /// <summary>Ring only: the word inside the ring (the countdown).</summary>
        public string Inner;
        public GaugeKind Kind;
        /// <summary>0..1: arc / ring / bar fill, bars signal level (quarters), mode progress rail.</summary>
        public float Value;
        /// <summary>Needle only: compass degrees relative to the nose (0 = up, clockwise).</summary>
        public float Angle;
        public ScopeTone Tone;
        public bool Flash, Ready;
        /// <summary>Registered gauges only: higher ranks take a slot first (a live ACE HUNT needle outranks an idle RADIO).</summary>
        public int Rank;
        /// <summary>Bar only: tick positions 0..1 (provider-owned array, drawn in <see cref="TickTone"/>); null = none.</summary>
        public float[] Ticks;
        public int TickCount;
        public ScopeTone TickTone;
    }

    /// <summary>Fills <paramref name="spec"/>; false = this gauge has nothing live and takes no slot.</summary>
    internal delegate bool GaugeProvider(out GaugeSpec spec);

    /// <summary>
    /// The gauge row's registry: any module offers a gauge by id (LANCE from Vanguard, PERK from Support, RADIO from Radio, AUTOPILOT from
    /// Autopilot...), the shell shows up to four per tick: the active page's own first, then the registered ones by rank. A gauge
    /// whose provider says false is not drawn; a row with no gauge takes no room.
    /// <code>ScopeGauges.Register("lance", ReadLance);   // bool ReadLance(out GaugeSpec g)
    /// ScopeGauges.Unregister("lance", ReadLance);</code>
    /// </summary>
    internal static class ScopeGauges
    {
        private static readonly List<string> Ids = new List<string>(8);
        private static readonly List<GaugeProvider> Providers = new List<GaugeProvider>(8);

        public static int Count => Ids.Count;

        public static void Register(string id, GaugeProvider provider)
        {
            if (string.IsNullOrEmpty(id) || provider == null) return;
            int at = Ids.IndexOf(id);
            if (at >= 0) { Providers[at] = provider; return; }
            Ids.Add(id);
            Providers.Add(provider);
        }

        public static void Unregister(string id, GaugeProvider provider)
        {
            int at = Ids.IndexOf(id);
            if (at < 0 || Providers[at] != provider) return;
            Ids.RemoveAt(at);
            Providers.RemoveAt(at);
        }

        /// <summary>Gauge <paramref name="index"/> of <see cref="Count"/> (registration order); false when it has nothing live.</summary>
        public static bool TryRead(int index, out GaugeSpec spec)
        {
            spec = default;
            if (index < 0 || index >= Providers.Count) return false;
            try
            {
                if (!Providers[index](out spec)) return false;
            }
            catch (Exception e)
            {
                ScopeFeeds.OnError?.Invoke("ScopeGauge." + Ids[index], e);
                return false;
            }
            if (string.IsNullOrEmpty(spec.Id)) spec.Id = Ids[index];
            return true;
        }
    }
}
