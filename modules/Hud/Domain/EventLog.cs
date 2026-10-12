using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>One line the game added to its feed, kept for DL EVT LOG: the parsed line, the mission-clock stamp it arrived at, and when it came and goes (the game's own clock, <c>Time.timeSinceLevelLoad</c>).</summary>
    internal sealed class EventRow
    {
        public EventLine Line;
        /// <summary>"mm:ss" of the mission clock when the line arrived ("--:--" when there is none).</summary>
        public string Stamp;
        /// <summary>The line's rich text as the game wrote it.</summary>
        public string Raw;
        public float Arrival, RemoveAt;
        /// <summary>Counts up with every line ever added: a row's identity.</summary>
        public int Serial;

        public bool Alive(float now) => now < RemoveAt;

        /// <summary>How far the row has wiped in, 0..1 (180 ms from its arrival).</summary>
        public float Wipe(float now) => Math.Min(1f, Math.Max(0f, (now - Arrival) / EventLog.WipeSeconds));

        /// <summary>BRIGHT for the first 1.5 s.</summary>
        public bool Bright(float now) => now - Arrival < EventLog.BrightSeconds;

        /// <summary>1 until the last second of its life, then down to 0 (a life shorter than two seconds fades over its second half).</summary>
        public float Alpha(float now)
        {
            float left = RemoveAt - now, fade = Math.Min(EventLog.FadeSeconds, (RemoveAt - Arrival) * 0.5f);
            return left <= 0f ? 0f : left >= fade || fade <= 0f ? 1f : left / fade;
        }
    }

    /// <summary>
    /// The newest six lines of the game's message feed in a fixed ring, oldest first (DL EVT LOG), pure. A row lives as long as the game shows the line: the game's own removal time is read when the line is added
    /// (<see cref="Add"/>; <see cref="FallbackSeconds"/> when it cannot be read). The clock is the game's <c>Time.timeSinceLevelLoad</c>, which starts again with every scene: a clock that runs backwards empties the ring.
    /// Everything here is allocation free except <see cref="Add"/>, which runs once per line the game adds.
    /// </summary>
    internal sealed class EventLog
    {
        public const int Capacity = 6;
        public const float WipeSeconds = 0.18f, BrightSeconds = 1.5f, FadeSeconds = 1f, FallbackSeconds = 12f;
        /// <summary>The wipe and the fade are written in this many steps, so a row is drawn again only when a step changed.</summary>
        public const int WipeSteps = 12, FadeSteps = 10;
        /// <summary>The game's feed holds text but this log holds no live row for this long: the capture is not seeing the game's lines (<see cref="Healthy"/>).</summary>
        public const float MissSeconds = 0.6f;

        private readonly EventRow[] ring = new EventRow[Capacity];
        private int count, serial;
        private float missSince = float.NaN;

        public int Count => count;

        /// <summary>The <paramref name="i"/>th oldest row.</summary>
        public EventRow this[int i] => ring[i];

        public void Clear()
        {
            Array.Clear(ring, 0, ring.Length);
            count = 0;
            missSince = float.NaN;
        }

        /// <summary>Keep a line the game just added. <paramref name="removeAt"/> is the game's removal time for it; one that is not after <paramref name="arrival"/>, not a number or absurdly far is replaced by <see cref="FallbackSeconds"/>.</summary>
        public EventRow Add(EventLine line, string raw, string stamp, float arrival, float removeAt)
        {
            Tick(arrival);
            if (float.IsNaN(removeAt) || float.IsInfinity(removeAt) || removeAt <= arrival || removeAt > arrival + 3600f) removeAt = arrival + FallbackSeconds;
            if (count == Capacity)
            {
                Array.Copy(ring, 1, ring, 0, Capacity - 1);
                count--;
            }
            var row = new EventRow { Line = line, Raw = raw, Stamp = stamp ?? "--:--", Arrival = arrival, RemoveAt = removeAt, Serial = ++serial };
            ring[count++] = row;
            return row;
        }

        /// <summary>The scene started again (the clock ran back): nothing of the old one is left.</summary>
        public void Tick(float now)
        {
            if (count > 0 && now + 0.5f < ring[count - 1].Arrival) Clear();
        }

        /// <summary>The rows the game is still showing.</summary>
        public int LiveCount(float now)
        {
            int n = 0;
            for (int i = 0; i < count; i++) if (ring[i].Alive(now)) n++;
            return n;
        }

        /// <summary>MINIMAL keeps only the combat reports that involve the pilot (KILL rows with their own name or aircraft in them) and COMM; every other level every row.</summary>
        public static bool Shows(BoardDeclutter level, EventRow row) =>
            level != BoardDeclutter.Minimal || row.Line.Class == EventClass.Comm || (row.Line.Class == EventClass.Kill && row.Line.Own);

        /// <summary>The live rows <paramref name="level"/> shows, oldest first, into <paramref name="into"/>; how many.</summary>
        public int Select(float now, BoardDeclutter level, EventRow[] into)
        {
            int n = 0;
            for (int i = 0; i < count && n < into.Length; i++)
                if (ring[i].Alive(now) && Shows(level, ring[i])) into[n++] = ring[i];
            return n;
        }

        /// <summary>A number that changes whenever what <see cref="Select"/> returns would be drawn differently: a row comes, goes, steps its wipe or its fade, or stops being BRIGHT.</summary>
        public int Signature(float now, BoardDeclutter level)
        {
            unchecked
            {
                int h = 17 + (int)level;
                for (int i = 0; i < count; i++)
                {
                    EventRow r = ring[i];
                    if (!r.Alive(now) || !Shows(level, r)) continue;
                    h = h * 31 + r.Serial;
                    h = h * 31 + (int)(r.Wipe(now) * WipeSteps);
                    h = h * 31 + (int)(r.Alpha(now) * FadeSteps);
                    h = h * 31 + (r.Bright(now) ? 1 : 0);
                }
                return h;
            }
        }

        /// <summary>
        /// False when the game's own feed has text (<paramref name="nativeHasText"/>) and no row here is live for <see cref="MissSeconds"/>: the capture is not seeing the game's lines, and the game's feed is better left alone
        /// (fail open). A row that is live, or a feed with no text, is healthy at once.
        /// </summary>
        public bool Healthy(float now, bool nativeHasText)
        {
            if (!nativeHasText || LiveCount(now) > 0) { missSince = float.NaN; return true; }
            if (float.IsNaN(missSince)) missSince = now;
            return now - missSince <= MissSeconds;
        }

        /// <summary>The mission clock as "mm:ss" (minutes not capped), "--:--" when it is not a time.</summary>
        public static string Stamp(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return "--:--";
            long s = (long)Math.Floor(seconds);
            long m = s / 60;
            return (m < 10 ? "0" : "") + m + ":" + (s % 60 < 10 ? "0" : "") + s % 60;
        }
    }

    /// <summary>The block as laid out (design units from the screen's top-left, y down); what the offline check reads.</summary>
    internal struct EventLogPlan
    {
        public float Left, Top, Width, Height, StampX, ClassX, LineX, FirstRowY;
        public int Rows;
        /// <summary>The caption shelf, the spine and the stamps (Full only).</summary>
        public bool Furniture;

        public readonly bool Present => Rows > 0;
        public readonly float RowY(int i) => FirstRowY + EventLogLayout.Pitch * i;
        /// <summary>The spine runs from just over the first row to just under the last.</summary>
        public readonly float SpineTop => FirstRowY - EventLogLayout.Pitch * 0.5f;
        public readonly float SpineBottom => RowY(Rows - 1) + EventLogLayout.Pitch * 0.5f;
    }

    /// <summary>
    /// Where DL EVT LOG's pieces go, pure. The block hangs from the game's feed box (its top-left corner, in design units; 4 in and <see cref="TopGap"/> down, which puts the shelf's line at y 18 like the board's shelves on the right):
    /// a caption shelf on top (its line 6 under the block's top, 280..560 wide in steps of 40 so it does not jump with every line), the rows 16 apart with the first one 24 under the top (8 without the shelf), a spine at the
    /// block's left edge with a tick on every row. A row is a stamp
    /// (<see cref="StampChars"/> cells), the class word (<see cref="ClassChars"/>) and the line, in the 12 unit mono face (<see cref="Advance"/> a cell), two cells between the class word and the line.
    /// Lean and Minimal draw no shelf, spine or stamp, so the rows start nearer the edge.
    /// </summary>
    internal static class EventLogLayout
    {
        /// <summary>A character cell of the 12 unit mono face (0.65 em).</summary>
        public const float Advance = 7.8f;
        public const float Pitch = 16f, Margin = 4f, TopGap = 12f, MinWidth = 280f, MaxWidth = 560f, WidthStep = 40f, ShelfToRow = 24f, RowOffset = 8f, Indent = 12f, LeanIndent = 6f, RightPad = 8f, SpineTick = 4f;
        public const int StampChars = 6, ClassChars = 5;

        public static EventLogPlan Plan(float boxLeft, float boxTop, int rows, int widestChars, BoardDeclutter level)
        {
            if (rows <= 0) return default;
            bool furniture = level == BoardDeclutter.Full;
            var p = new EventLogPlan { Left = boxLeft + Margin, Top = boxTop + TopGap, Rows = Math.Min(rows, EventLog.Capacity), Furniture = furniture };
            p.StampX = p.Left + Indent;
            p.ClassX = furniture ? p.StampX + (StampChars + 1) * Advance : p.Left + LeanIndent;
            p.LineX = p.ClassX + (ClassChars + 2) * Advance;
            float need = p.LineX - p.Left + widestChars * Advance + RightPad;
            p.Width = Math.Min(MaxWidth, Math.Max(MinWidth, MathF.Ceiling(need / WidthStep) * WidthStep));
            p.FirstRowY = p.Top + (furniture ? ShelfToRow : RowOffset);
            p.Height = p.FirstRowY - p.Top + Pitch * (p.Rows - 1) + RowOffset;
            return p;
        }
    }
}
