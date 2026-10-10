using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The notice ticker: coalescing, pre-emption, sticky warnings, bounded and allocation-free queueing.</summary>
    internal static class NoticeTickerTests
    {
        public static void Run()
        {
            Basics();
            Coalescing();
            Preemption();
            Sticky();
            Waiting();
            Bounded();
            Words();
        }

        private static void Basics()
        {
            var t = new NoticeTicker();
            TestAssert.That(!t.Peek(0f, out _), "An empty ticker has nothing to say");
            t.Push("comms", HudTone.Info, "", null, 0f);
            TestAssert.That(!t.Peek(0f, out _), "An empty notice is not pushed");
            t.Push("comms", HudTone.Info, "RADIO CHECK", "FROM ALPHA 1", 0f);
            TestAssert.That(t.Peek(0f, out TickerLine line) && line.Text == "RADIO CHECK" && line.Detail == "FROM ALPHA 1" &&
                line.Channel == "comms" && line.Count == 1 && line.Age == 0f, "A pushed notice shows at once");
            TestAssert.That(NoticeTicker.IsFresh(line), "...and reads as fresh");
            t.Peek(5f, out line);
            TestAssert.That(line.Age == 5f && !NoticeTicker.IsFresh(line), "Its age counts from when it first showed");
            TestAssert.That(t.Peek(5.9f, out _) && !t.Peek(6.1f, out _), "An Info lasts six seconds");
            t.Push("comms", HudTone.Caution, "LOW", null, 10f);
            t.Peek(10f, out _);
            TestAssert.That(t.Peek(17.9f, out _) && !t.Peek(18.1f, out _), "A Caution lasts eight");
            t.Clear();
            TestAssert.That(!t.Peek(18f, out _) && t.Count == 0, "Clear empties it");
        }

        private static void Coalescing()
        {
            var t = new NoticeTicker();
            t.Push("events", HudTone.Caution, "SUPEREVENT", null, 0f);
            t.Peek(0f, out _);
            t.Push("events", HudTone.Caution, "SUPEREVENT", null, 3f);
            t.Push("events", HudTone.Caution, "SUPEREVENT", "AREA 4", 4f);
            t.Peek(4f, out TickerLine line);
            TestAssert.That(t.Count == 1 && line.Count == 3 && line.Detail == "AREA 4", "The same notice three times is one line with a count of three");
            TestAssert.That(NoticeTicker.Words(line) == "AREA 4", "Four seconds in it has moved on to its detail");
            line.Age = 0f;
            TestAssert.That(NoticeTicker.Words(line) == "SUPEREVENT x3", "...and on its text it reads x3: " + NoticeTicker.Words(line));
            TestAssert.That(t.Peek(11.9f, out _) && !t.Peek(12.1f, out _), "Every repeat refreshes the dwell");
            t.Push("events", HudTone.Info, "SUPEREVENT", null, 20f);
            t.Push("weather", HudTone.Info, "SUPEREVENT", null, 20f);
            TestAssert.That(t.Count == 2, "Another channel's identical words are a different notice");
            for (int i = 0; i < 200; i++) t.Push("spam", HudTone.Info, "LINE", null, 21f);
            t.Peek(21f, out line);
            TestAssert.That(line.Count <= NoticeTicker.MaxCount, "The repeat count is capped");
        }

        private static void Preemption()
        {
            var t = new NoticeTicker();
            t.Push("comms", HudTone.Info, "INFO", null, 0f);
            t.Peek(0f, out TickerLine line);
            TestAssert.That(line.Text == "INFO", "An Info shows when it is alone");
            t.Push("events", HudTone.Warning, "WARN", null, 1f);
            t.Peek(1f, out line);
            TestAssert.That(line.Text == "WARN", "A Warning takes the ticker from an Info at once");
            t.Push("comms", HudTone.Info, "LATER INFO", null, 2f);
            t.Peek(2f, out line);
            TestAssert.That(line.Text == "WARN", "A later Info never takes it back from a Warning");
            t.Push("weather", HudTone.Caution, "CAUTION", null, 3f);
            t.Peek(3f, out line);
            TestAssert.That(line.Text == "WARN", "Nor does a Caution");
            // The Warning ends at 21 s; the Caution (pushed at 3 s, still inside its 20 s wait) takes the ticker then.
            TestAssert.That(t.Peek(21.5f, out line) && line.Text == "CAUTION" && line.Age == 0f, "A pre-empted notice returns when the louder one ends, its dwell starting then");

            var warnings = new NoticeTicker();
            warnings.Push("events", HudTone.Warning, "FIRST WARN", null, 0f);
            warnings.Peek(0f, out line);
            warnings.Push("session", HudTone.Warning, "NEWER WARN", null, 1f);
            warnings.Peek(1f, out line);
            TestAssert.That(line.Text == "NEWER WARN", "A newer Warning takes it from an older one");
        }

        private static void Sticky()
        {
            var t = new NoticeTicker();
            t.Push("events", HudTone.Warning, "SAM LAUNCH", null, 0f);
            t.Peek(0f, out _);
            TestAssert.That(t.Peek(19.9f, out _) && !t.Peek(20.1f, out _), "A Warning stays twenty seconds, not eight");
            t.Clear();
            t.Push("events", HudTone.Warning, "SAM LAUNCH", null, 0f);
            t.Peek(0f, out _);
            t.Push("events", HudTone.Warning, "SAM LAUNCH", null, 10f);
            TestAssert.That(t.Peek(29.9f, out _) && !t.Peek(30.1f, out _), "A repeat extends a sticky warning");
        }

        private static void Waiting()
        {
            var t = new NoticeTicker();
            t.Push("events", HudTone.Warning, "WARN", null, 0f);
            t.Peek(0f, out _);
            t.Push("comms", HudTone.Info, "WAITING", null, 1f);
            TestAssert.That(t.Peek(19.9f, out TickerLine line) && line.Text == "WARN", "An Info waits behind a Warning");
            TestAssert.That(t.Peek(20.5f, out line) && line.Text == "WAITING" && line.Age == 0f, "...and shows for its own full dwell once the warning ends");

            var stale = new NoticeTicker();
            stale.Push("events", HudTone.Warning, "WARN", null, 0f);
            stale.Peek(0f, out _);
            stale.Push("comms", HudTone.Info, "TOO LATE", null, 1f);
            stale.Peek(5f, out _);
            TestAssert.That(!stale.Peek(21.5f, out _) && stale.Count == 0, "A notice that waited longer than the queue time is not shown stale");
        }

        private static void Bounded()
        {
            var t = new NoticeTicker();
            for (int i = 0; i < NoticeTicker.Capacity + 3; i++) t.Push("c", HudTone.Info, "NOTICE " + i, null, i);
            TestAssert.That(t.Count == NoticeTicker.Capacity, "The queue is bounded");
            t.Push("c", HudTone.Warning, "IMPORTANT", null, 20f);
            t.Peek(20f, out TickerLine line);
            TestAssert.That(line.Text == "IMPORTANT" && t.Count <= NoticeTicker.Capacity, "A full queue still admits a Warning");

            var full = new NoticeTicker();
            for (int i = 0; i < NoticeTicker.Capacity; i++) full.Push("c", HudTone.Caution, "CAUTION " + i, null, i);
            full.Push("c", HudTone.Info, "QUIET", null, 10f);
            bool quietShown = false;
            for (float now = 10f; now < 120f; now += 1f)
                if (full.Peek(now, out line) && line.Text == "QUIET") quietShown = true;
            TestAssert.That(!quietShown, "A newcomer quieter than everything queued is the one dropped");
        }

        private static void Words()
        {
            var line = new TickerLine { Tone = HudTone.Info, Text = "ENTERING AREA", Detail = "SURVEY THE AFTERMATH", Count = 1, Age = 0f };
            TestAssert.That(NoticeTicker.Words(line) == "ENTERING AREA" && NoticeTicker.Phase(line) == 0, "A notice opens on its text");
            line.Age = NoticeTicker.PhaseSeconds + 0.1f;
            TestAssert.That(NoticeTicker.Words(line) == "SURVEY THE AFTERMATH" && NoticeTicker.Phase(line) == 1, "...then shows its detail");
            line.Age = 2f * NoticeTicker.PhaseSeconds + 0.1f;
            TestAssert.That(NoticeTicker.Words(line) == "ENTERING AREA", "...and alternates");
            TestAssert.That(NoticeTicker.Tag("comms") == "COMMS" && NoticeTicker.Tag("ace-hunt") == "ACE" && NoticeTicker.Tag("contracts") == "OPS" &&
                NoticeTicker.Tag(null) == "INFO" && NoticeTicker.Tag("a-very-long-channel") == "A VERY ", "Channel tags are short and upper case");
            line.Detail = null; line.Age = 4f;
            TestAssert.That(NoticeTicker.Words(line) == "ENTERING AREA" && NoticeTicker.Phase(line) == 0, "Without a detail it never alternates");
            Paging();
        }

        /// <summary>A 90-character notice is read in full: pages cut at spaces, each in turn, and the dwell covers them all.</summary>
        private static void Paging()
        {
            string longText = "SESSION HOST: THE MISSION WILL RESTART IN FIVE MINUTES FOR A SCHEDULED MAINTENANCE WINDOW";
            TestAssert.That(longText.Length > 80 && NoticeTicker.PageCount(longText) == 3 && NoticeTicker.PageCount("SHORT") == 1 && NoticeTicker.PageCount(null) == 1 && NoticeTicker.PageCount("") == 1,
                "Only a long line takes more than one page: " + NoticeTicker.PageCount(longText));
            string rejoined = "";
            for (int i = 0; i < NoticeTicker.PageCount(longText); i++)
            {
                string page = NoticeTicker.Page(longText, i);
                TestAssert.That(page.Length > 0 && page.Length <= NoticeTicker.PageChars && !page.StartsWith(" ") && !page.EndsWith(" "), "Page " + i + " fits and is trimmed: [" + page + "]");
                rejoined += (i > 0 ? " " : "") + page;
            }
            TestAssert.That(rejoined == longText, "...and the pages put back together are the whole notice: " + rejoined);
            TestAssert.That(NoticeTicker.Page("ABCDEFGHIJ".PadRight(60, 'X'), 1).Length == 60 - NoticeTicker.PageChars && NoticeTicker.PageCount("ABCDEFGHIJ".PadRight(60, 'X')) == 2, "A word longer than a page is cut where it falls");

            var line = new TickerLine { Tone = HudTone.Info, Text = longText, Detail = "AREA 4 EAST", Count = 1, Age = 0f };
            TestAssert.That(NoticeTicker.Words(line) == NoticeTicker.Page(longText, 0), "It opens on the first page");
            line.Age = NoticeTicker.PhaseSeconds * 2.1f;
            TestAssert.That(NoticeTicker.Words(line) == NoticeTicker.Page(longText, 2), "...turns the pages...");
            line.Age = NoticeTicker.PhaseSeconds * 3.1f;
            TestAssert.That(NoticeTicker.Words(line) == "AREA 4 EAST", "...then the detail...");
            line.Age = NoticeTicker.PhaseSeconds * 4.1f;
            TestAssert.That(NoticeTicker.Words(line) == NoticeTicker.Page(longText, 0), "...and round again");

            var t = new NoticeTicker();
            t.Push("session", HudTone.Info, longText, "AREA 4 EAST", 0f);
            t.Peek(0f, out _);
            TestAssert.That(t.Peek(11.9f, out _) && !t.Peek(12.1f, out _), "An Info with four pages stays 12 s, not 6: every page gets its turn");
        }
    }
}
