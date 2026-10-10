using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The one notice the ticker shows right now.</summary>
    internal struct TickerLine
    {
        public HudTone Tone;
        public string Channel, Text, Detail;
        /// <summary>How many times the same notice arrived while it was live ("x3").</summary>
        public int Count;
        /// <summary>Seconds since this notice first reached the ticker.</summary>
        public float Age;
    }

    /// <summary>
    /// The notice ticker's rules (2026-10-10), pure. Notices leave the feed cells (which truncated them to a few letters)
    /// for one full-width line:
    /// <list type="bullet">
    /// <item><b>Coalescing</b>: the same channel and text arriving again bumps a count ("x3") and refreshes the dwell
    /// instead of stacking a duplicate.</item>
    /// <item><b>Pre-emption</b>: the line shows the highest tone, newest first among equals; a Warning takes the ticker
    /// from a Caution at once, an Info never takes it from either. A notice that was pre-empted is still there when the
    /// louder one ends, unless its own time ran out meanwhile.</item>
    /// <item><b>Sticky warnings</b>: a Warning stays <see cref="WarningSeconds"/> (an Info only <see cref="InfoSeconds"/>) and
    /// every repeat extends it, so a warning is not gone before the pilot could read it.</item>
    /// <item>A notice waits at most <see cref="QueueSeconds"/> for the ticker; bounded at <see cref="Capacity"/> entries,
    /// the quietest, oldest one makes room.</item>
    /// </list>
    /// A notice's dwell starts when it first reaches the ticker, not when it was pushed. Allocation-free after construction.
    /// </summary>
    internal sealed class NoticeTicker
    {
        public const int Capacity = 6;
        public const float InfoSeconds = 6f, CautionSeconds = 8f, WarningSeconds = 20f;
        public const float QueueSeconds = 20f;
        /// <summary>A notice is "fresh" (the ticker pulses) for this long after it first shows.</summary>
        public const float FreshSeconds = 1.2f;
        /// <summary>A notice with a detail line shows its text, then its detail, each for this long, alternating.</summary>
        public const float PhaseSeconds = 3f;
        public const int MaxCount = 99;

        private struct Entry
        {
            public bool Active, Shown;
            public string Channel, Text, Detail;
            public HudTone Tone;
            public int Count;
            public long Seq;
            public float Expires, ShownAt;
        }

        private readonly Entry[] entries = new Entry[Capacity];
        private long seq;

        public int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Capacity; i++) if (entries[i].Active) n++;
                return n;
            }
        }

        public static float Dwell(HudTone tone) =>
            tone == HudTone.Warning ? WarningSeconds : tone == HudTone.Caution ? CautionSeconds : InfoSeconds;

        public void Push(string channel, HudTone tone, string text, string detail, float now)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < Capacity; i++)
            {
                if (!entries[i].Active) continue;
                if (!Same(entries[i].Channel, channel) || !Same(entries[i].Text, text)) continue;
                entries[i].Count = Math.Min(MaxCount, entries[i].Count + 1);
                entries[i].Detail = detail;
                if (tone > entries[i].Tone) entries[i].Tone = tone;
                entries[i].Seq = ++seq;
                entries[i].Expires = now + (entries[i].Shown ? Dwell(entries[i].Tone) : QueueSeconds);
                return;
            }

            int slot = -1;
            for (int i = 0; i < Capacity && slot < 0; i++) if (!entries[i].Active) slot = i;
            if (slot < 0)
            {
                // Full: the quietest, oldest entry goes; a newcomer quieter than all of them is the one dropped.
                int weakest = 0;
                for (int i = 1; i < Capacity; i++)
                    if (entries[i].Tone < entries[weakest].Tone || entries[i].Tone == entries[weakest].Tone && entries[i].Seq < entries[weakest].Seq) weakest = i;
                if (tone < entries[weakest].Tone) return;
                slot = weakest;
            }
            entries[slot] = new Entry
            {
                Active = true, Channel = channel, Text = text, Detail = detail, Tone = tone, Count = 1, Seq = ++seq,
                Expires = now + QueueSeconds,
            };
        }

        /// <summary>
        /// The notice to show at <paramref name="now"/>: expires the stale ones, picks the highest tone (newest among equals)
        /// and starts its dwell the first time it is picked. False when there is nothing to say.
        /// </summary>
        public bool Peek(float now, out TickerLine line)
        {
            line = default;
            int best = -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (!entries[i].Active) continue;
                if (entries[i].Expires <= now) { entries[i].Active = false; continue; }
                if (best < 0 || entries[i].Tone > entries[best].Tone || entries[i].Tone == entries[best].Tone && entries[i].Seq > entries[best].Seq) best = i;
            }
            if (best < 0) return false;
            if (!entries[best].Shown)
            {
                entries[best].Shown = true;
                entries[best].ShownAt = now;
                entries[best].Expires = now + Dwell(entries[best].Tone);
            }
            line = new TickerLine
            {
                Tone = entries[best].Tone, Channel = entries[best].Channel, Text = entries[best].Text,
                Detail = entries[best].Detail, Count = entries[best].Count, Age = now - entries[best].ShownAt,
            };
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++) entries[i] = default;
        }

        /// <summary>Which of a line's two phases shows now: 0 its text (with "xN"), 1 its detail. Always 0 without a detail.</summary>
        public static int Phase(in TickerLine line) =>
            string.IsNullOrEmpty(line.Detail) ? 0 : (int)(line.Age / PhaseSeconds) % 2;

        /// <summary>The words on the ticker now: the text and its count, then (every other phase) the detail.</summary>
        public static string Words(in TickerLine line) =>
            Phase(line) == 1 ? line.Detail : line.Count > 1 ? line.Text + " x" + line.Count : line.Text;

        public static bool IsFresh(in TickerLine line) => line.Age < FreshSeconds;

        /// <summary>The tag tile for a channel: short, upper case ("ace-hunt" is ACE, "contracts" is OPS).</summary>
        public static string Tag(string channel)
        {
            switch (channel)
            {
                case "ace-hunt": return "ACE";
                case "contracts": return "OPS";
                case null:
                case "": return "INFO";
            }
            string upper = channel.ToUpperInvariant().Replace('-', ' ');
            return upper.Length <= 7 ? upper : upper.Substring(0, 7);
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    }
}
