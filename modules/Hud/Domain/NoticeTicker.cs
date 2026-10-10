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
    /// <item><b>Paging</b>: the line is one row wide, so a longer text or detail is cut at spaces into pages of at most
    /// <see cref="PageChars"/> characters that follow each other every <see cref="PhaseSeconds"/>; the dwell grows to give every
    /// page its turn, so a long notice is read in full instead of elided.</item>
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
        /// <summary>The longest page of a long text or detail: the board's ticker row holds about 43 characters of the mono face at 12 px beside its channel chip, and a warning's prefix takes ten.</summary>
        public const int PageChars = 34;
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

        /// <summary>How long a notice stays once shown: its tone's dwell, or every page of its text and detail for <see cref="PhaseSeconds"/>, whichever is longer.</summary>
        private static float Hold(HudTone tone, string text, string detail) =>
            Math.Max(Dwell(tone), (PageCount(text) + (string.IsNullOrEmpty(detail) ? 0 : PageCount(detail))) * PhaseSeconds);

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
                entries[i].Expires = now + (entries[i].Shown ? Hold(entries[i].Tone, entries[i].Text, entries[i].Detail) : QueueSeconds);
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
                entries[best].Expires = now + Hold(entries[best].Tone, entries[best].Text, entries[best].Detail);
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

        private static string TextWords(in TickerLine line) => line.Count > 1 ? line.Text + " x" + line.Count : line.Text;

        /// <summary>
        /// Which page shows now: the pages of the text (with "xN") first, then those of the detail, each for
        /// <see cref="PhaseSeconds"/>, then round again. Always 0 for a short text without a detail.
        /// </summary>
        public static int Phase(in TickerLine line)
        {
            int pages = PageCount(TextWords(line)) + (string.IsNullOrEmpty(line.Detail) ? 0 : PageCount(line.Detail));
            return pages <= 1 ? 0 : (int)(line.Age / PhaseSeconds) % pages;
        }

        /// <summary>The words on the ticker now: a page of the text and its count, then a page of the detail.</summary>
        public static string Words(in TickerLine line)
        {
            string text = TextWords(line);
            int phase = Phase(line), textPages = PageCount(text);
            return phase < textPages ? Page(text, phase) : Page(line.Detail, phase - textPages);
        }

        /// <summary>How many pages <paramref name="s"/> needs (at least 1).</summary>
        public static int PageCount(string s)
        {
            int n = 0;
            for (int at = 0; s != null && at < s.Length; at = SkipSpaces(s, Cut(s, at))) n++;
            return Math.Max(1, n);
        }

        /// <summary>Page <paramref name="index"/> of <paramref name="s"/>: cut at the last space within <see cref="PageChars"/> (a longer word is cut where it falls).</summary>
        public static string Page(string s, int index)
        {
            int n = 0;
            for (int at = 0; s != null && at < s.Length; at = SkipSpaces(s, Cut(s, at)), n++)
                if (n == index) return s.Substring(at, Cut(s, at) - at);
            return "";
        }

        private static int Cut(string s, int at)
        {
            if (s.Length - at <= PageChars) return s.Length;
            int cut = at + PageChars, space = s.LastIndexOf(' ', cut);
            return space > at ? space : cut;
        }

        private static int SkipSpaces(string s, int at)
        {
            while (at < s.Length && s[at] == ' ') at++;
            return at;
        }

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
