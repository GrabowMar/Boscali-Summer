using System;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>DL EVT LOG (top-left): a line of the game's feed read into spans and a class word, the six-row ring with its ageing, the declutter filter and the block's layout.</summary>
    internal static class EventLogTests
    {
        public static void Run()
        {
            Colours();
            Screenshot();
            Verbs();
            Net();
            Chat();
            Unknown();
            Tags();
            Fitting();
            Ownership();
            Ageing();
            Ring();
            Filtering();
            Signature();
            Health();
            Clock();
            Layout();
        }

        private static string Spans(EventLine l) => string.Join("|", l.Spans.Select(s => s.Role.ToString()[0] + (s.Rgb >= 0 ? "#" + s.Rgb.ToString("X6") : "") + ":" + s.Text));

        private static void Colours()
        {
            TestAssert.Eq(EventParser.Color("#ff4d4d"), 0xFF4D4D, "Six digits");
            TestAssert.Eq(EventParser.Color("#58A8FFFF"), 0x58A8FF, "Eight digits (the game's AddColor writes RRGGBBAA): the alpha is dropped");
            TestAssert.Eq(EventParser.Color("#f80"), 0xFF8800, "Three digits double");
            TestAssert.Eq(EventParser.Color("#f80c"), 0xFF8800, "Four digits double and drop the alpha");
            TestAssert.Eq(EventParser.Color("Red"), 0xFF0000, "A TMP colour name, any case");
            TestAssert.Eq(EventParser.Color("#12345"), -1, "Five digits is nothing");
            TestAssert.Eq(EventParser.Color("#gg0000"), -1, "Not hex is nothing");
            TestAssert.Eq(EventParser.Color("teal"), -1, "A name TMP does not have is nothing");
            TestAssert.Eq(EventParser.Color(null), -1, "No value is nothing");
        }

        /// <summary>The four lines the owner's screenshot shows: blue and red names (the game's AddColor, eight digits), a white one, the verb between them uncoloured.</summary>
        private static void Screenshot()
        {
            EventLine a = EventParser.Parse("<color=#58A8FFFF>FGA-57 Anvil</color> intercepted <color=#FF4D4DFF>MLRS rocket</color>");
            TestAssert.Eq(a.Class, EventClass.Intcp, "intercepted is INTCP");
            TestAssert.Eq(Spans(a), "N#58A8FF:FGA-57 ANVIL|V: INTERCEPTED |N#FF4D4D:MLRS ROCKET", "Two names with their native colours and the verb between them");
            TestAssert.Eq(a.Plain(), "FGA-57 ANVIL INTERCEPTED MLRS ROCKET", "All the words, in order, in capitals");
            TestAssert.Eq(a.Chars, 1 + 12 + 13 + 1 + 11, "A square is one cell");
            TestAssert.That(!a.Truncated && !a.Own, "Fits and is nobody's own");
            EventLine b = EventParser.Parse("<color=#58A8FFFF>Cursor Class LFD</color> intercepted <color=#FF4D4DFF>MLRS rocket</color>");
            TestAssert.Eq(b.Spans[0].Text, "CURSOR CLASS LFD", "Another interceptor");
            EventLine c = EventParser.Parse("<color=#F4F6F8FF>FGA-57 Anvil</color> intercepted <color=#FF4D4DFF>MLRS rocket</color>");
            TestAssert.Eq(c.Spans[0].Rgb, 0xF4F6F8, "The white name keeps its white square");
            TestAssert.That(c.Spans[0].Swatch && c.Spans[2].Swatch && !c.Spans[1].Swatch, "Names wear a square, the verb does not");
            TestAssert.Eq(EventWords.Of(EventClass.Intcp), "INTCP", "The class word");
        }

        private static void Verbs()
        {
            string kill = "<color=#FF4D4DFF>Alpha 1</color> {0} <color=#58A8FFFF>Bravo 2</color>";
            void Is(string verb, EventClass want, string word)
            {
                EventLine l = EventParser.Parse(string.Format(kill, verb));
                TestAssert.Eq(l.Class, want, verb + " is " + word);
                TestAssert.Eq(EventWords.Of(l.Class), word, verb + " reads " + word);
            }
            Is("shot down", EventClass.Kill, "KILL");
            Is("destroyed", EventClass.Kill, "KILL");
            Is("killed", EventClass.Kill, "KILL");
            Is("sank", EventClass.Kill, "KILL");
            Is("demolished", EventClass.Demo, "DEMO");
            Is("intercepted", EventClass.Intcp, "INTCP");
            Is("repaired", EventClass.Info, "INFO");
            // The verbs of a kill without a killer, and the other messages the game writes.
            TestAssert.Eq(EventParser.Parse("<color=#FF4D4DFF>Alpha 1</color> crashed").Class, EventClass.Crash, "crashed is CRASH");
            TestAssert.Eq(EventParser.Parse("<color=#FF4D4DFF>Truck</color> was destroyed").Class, EventClass.Kill, "was destroyed is KILL");
            TestAssert.Eq(EventParser.Parse("<color=#FF4D4DFF>Barracks</color> collapsed").Class, EventClass.Demo, "collapsed is DEMO");
            TestAssert.Eq(EventParser.Parse("<color=#58A8FFFF>Pilot</color> was captured by <color=#FF4D4DFF>Truck 4</color>").Class, EventClass.Capt, "was captured by is CAPT");
            TestAssert.Eq(EventParser.Parse("Maris Airport has been captured by Boscali Defence Force").Class, EventClass.Capt, "The base capture message is CAPT");
            TestAssert.Eq(EventParser.Parse("<color=#58A8FFFF>Pilot</color> was repaired").Class, EventClass.Info, "was repaired is INFO");
            // A verb is searched in the words the game left uncoloured: a unit called "Left Hook" is not a NET line and "Crashed Hawk" is not a crash.
            TestAssert.Eq(EventParser.Parse("<color=#58A8FFFF>Left Hook</color> shot down <color=#FF4D4DFF>Bravo</color>").Class, EventClass.Kill, "A coloured name does not set the class");
            TestAssert.Eq(EventParser.Parse("<color=#58A8FFFF>Left Hook</color> deployed at <color=#58A8FFFF>Annex Cliff</color> / <color=#58A8FFFF>C4</color>").Class, EventClass.Info, "...and 'deployed at' is INFO");
            // Words, not letters inside words.
            TestAssert.Eq(EventParser.Parse("Leftover fuel at the depot").Class, EventClass.Info, "Leftover is not LEFT");
            TestAssert.Eq(EventParser.Parse("Shot down range cleared").Class, EventClass.Kill, "Shot down in the words is a KILL when nothing else says");
            TestAssert.Eq(EventParser.Parse("Shotdown").Class, EventClass.Info, "Shotdown is nothing");
        }

        private static void Net()
        {
            EventLine j = EventParser.Parse("<color=#FFD84AFF>ANumeric joined the game</color>");
            TestAssert.Eq(j.Class, EventClass.Net, "joined is NET (the game colours the whole line, so the whole line is searched)");
            TestAssert.Eq(Spans(j), "N#FFD84A:ANUMERIC JOINED THE GAME", "The join line is one coloured span");
            TestAssert.Eq(EventParser.Parse("Bob Disconnected").Class, EventClass.Net, "Disconnected is NET");
            TestAssert.Eq(EventParser.Parse("<color=#58A8FFFF>Bob</color> joined <color=#58A8FFFF>Boscali Defence Force</color>").Class, EventClass.Net, "joined a faction is NET");
            TestAssert.Eq(EventParser.Parse("Bob left").Class, EventClass.Net, "left is NET");
        }

        private static void Chat()
        {
            EventLine c = EventParser.Parse("<color=#AABBCCFF>(ally)Pilot One:</color> Rolling in from the north");
            TestAssert.Eq(c.Class, EventClass.Comm, "A speaker and a colon is COMM");
            TestAssert.Eq(Spans(c), "N#AABBCC:(ALLY)PILOT ONE:|T: ROLLING IN FROM THE NORTH", "The speaker is a name in its colour, the words after it plain text");
            TestAssert.Eq(EventParser.Parse("<color=#AABBCCFF>Pilot One:</color> They shot down my friend at the airfield").Class, EventClass.Comm, "A verb in the text does not make chat a KILL");
            EventLine own = EventParser.Parse("<color=#AABBCCFF>(ally)Pilot One:</color> hello", "Pilot One");
            TestAssert.That(own.Own && own.Spans[0].Own && !own.Spans[1].Own, "The pilot's own chat name is theirs, ally prefix and colon aside");
            // The mod's radio subtitles ("Callsign: words", no tags): the speaker is split off, uncoloured, so no square.
            EventLine r = EventParser.Parse("Hawk 2-1: Contact bearing 090, angels 12");
            TestAssert.Eq(r.Class, EventClass.Comm, "A radio line is COMM");
            TestAssert.Eq(Spans(r), "N:HAWK 2-1:|T: CONTACT BEARING 090, ANGELS 12", "Speaker and words");
            TestAssert.That(!r.Spans[0].Swatch, "An uncoloured speaker has no square");
            TestAssert.Eq(EventParser.Parse("Time 10:30 hold").Class, EventClass.Info, "A colon inside a number is not a speaker");
            TestAssert.Eq(EventParser.Parse("one two three four five: text").Class, EventClass.Info, "Five words before a colon is a sentence, not a name");
            // The game writes a typed '<' as "<<i></i>": a literal '<' and two empty tags.
            TestAssert.Eq(EventParser.Parse("<color=#AABBCCFF>Pilot:</color> 1 <<i></i>3").Plain(), "PILOT: 1 <3", "A typed < is a < and the empty tags are gone");
        }

        private static void Unknown()
        {
            EventLine s = EventParser.Parse("Nuclear weapon launched, exclusion zone active!");
            TestAssert.Eq(s.Class, EventClass.Info, "A line no rule knows is INFO");
            TestAssert.Eq(s.Plain(), "NUCLEAR WEAPON LAUNCHED, EXCLUSION ZONE ACTIVE!", "...with all of its words, in order");
            TestAssert.Eq(s.Spans.Length, 1, "One span of plain text");
            TestAssert.That(s.Spans[0].Role == EventRole.Text && s.Spans[0].Rgb < 0, "Plain text, no colour");
            TestAssert.Eq(EventParser.Parse("  too   many \t spaces  ").Plain(), "TOO MANY SPACES", "Whitespace is one space and the ends are trimmed");
            TestAssert.Eq(EventParser.Parse("").Class, EventClass.Info, "An empty line is INFO");
            TestAssert.Eq(EventParser.Parse(null).Spans.Length, 0, "Null is nothing");
            TestAssert.Eq(EventParser.Parse("Slow Motion Enabled").Plain(), "SLOW MOTION ENABLED", "Another one");
        }

        private static void Tags()
        {
            // Nested: the inner colour wins inside it and the outer comes back.
            EventLine n = EventParser.Parse("<color=#FF0000>A <color=#00FF00>B</color> C</color> D");
            TestAssert.Eq(Spans(n), "N#FF0000:A |N#00FF00:B|N#FF0000: C|V: D", "Nested colours: red, green inside, red again, then none");
            // Unclosed: it runs to the end of the line.
            EventLine u = EventParser.Parse("<color=#ff0000>Alpha shot down Bravo");
            TestAssert.Eq(Spans(u), "N#FF0000:ALPHA SHOT DOWN BRAVO", "An open colour tag colours the rest of the line");
            TestAssert.Eq(u.Class, EventClass.Kill, "...and the verb is still found (the whole line is searched when all of it is coloured)");
            // A close with nothing open, and the tags that are not colours.
            TestAssert.Eq(EventParser.Parse("</color>Hello").Plain(), "HELLO", "A stray close tag is ignored");
            TestAssert.Eq(EventParser.Parse("<b>Hello</b> <size=80%>world</size> <i>now</i>").Plain(), "HELLO WORLD NOW", "Other tags are dropped, never the words");
            TestAssert.Eq(EventParser.Parse("<color=#ff0000>Hello</color").Plain(), "HELLO</COLOR", "A tag that never closes is text");
            TestAssert.Eq(EventParser.Parse("a < b and c > d").Plain(), "A < B AND C > D", "Comparison signs are text");
            TestAssert.Eq(EventParser.Parse("<#00ff00>Green</color> plain").Spans[0].Rgb, 0x00FF00, "The <#rrggbb> shorthand is a colour tag");
            TestAssert.Eq(EventParser.Parse("<color=\"red\">Red</color>").Spans[0].Rgb, 0xFF0000, "A quoted colour name");
            TestAssert.Eq(EventParser.Parse("<color=nonsense>Text</color>").Spans[0].Rgb, -1, "A colour that is not one leaves the text as it was");
            // Same colour twice in a row is one span.
            TestAssert.Eq(EventParser.Parse("<color=#ff0000>A</color><color=#ff0000>B</color>").Spans.Length, 1, "Adjacent runs of one colour join");
            // Nothing but a tag.
            TestAssert.Eq(EventParser.Parse("<color=#ff0000></color>").Spans.Length, 0, "A tag with nothing in it is nothing");
        }

        private static void Fitting()
        {
            string words = "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo sierra tango";
            foreach (string text in new[] { words, words.Substring(0, 90) })
            {
                EventLine l = EventParser.Parse(text);
                TestAssert.That(text.Length >= 90, "The test line is 90 characters or more (" + text.Length + ")");
                TestAssert.That(l.Truncated, "A line past the room is cut: " + text.Length);
                TestAssert.That(l.Chars + 2 <= EventParser.MaxChars, "...to the room and the plus: " + l.Chars);
                string kept = l.Plain();
                TestAssert.That(kept.EndsWith(" +", StringComparison.Ordinal) && !kept.Contains("."), "It ends in a plus and has no dots: " + kept);
                string body = kept.Substring(0, kept.Length - 2), upper = text.ToUpperInvariant();
                TestAssert.That(upper.StartsWith(body, StringComparison.Ordinal), "What is kept is the line's own beginning");
                TestAssert.That(upper.Substring(body.Length).StartsWith(" ", StringComparison.Ordinal), "...cut at a space: whole words only");
                TestAssert.That(upper.Substring(body.Length + 1).Split(' ')[0].Length + 1 + body.Length + 2 > EventParser.MaxChars, "...and the next word would not have fitted");
            }
            // A line that fits exactly is not cut.
            string exact = new string('x', EventParser.MaxChars);
            TestAssert.That(!EventParser.Parse(exact).Truncated && EventParser.Parse(exact).Chars == EventParser.MaxChars, "A line of exactly the room is whole");
            TestAssert.That(EventParser.Parse(exact + "x").Truncated, "One more cell is a cut");
            // One word longer than the row: its first characters, then the plus; no dots.
            EventLine one = EventParser.Parse(new string('a', 90));
            TestAssert.That(one.Truncated && one.Chars + 2 <= EventParser.MaxChars && one.Plain().EndsWith(" +", StringComparison.Ordinal), "A 90 character word is cut to the room: " + one.Plain());
            TestAssert.That(one.Plain().Substring(0, one.Plain().Length - 2).All(ch => ch == 'A'), "...and what stays is its first letters");
            // A kill with long names loses words from the end; the squares count.
            EventLine k = EventParser.Parse("<color=#58A8FFFF>Cursor Class Light Fighter Demonstrator</color> shot down <color=#FF4D4DFF>Anvil Class Mobile Rocket Launcher System</color>");
            TestAssert.That(k.Truncated && k.Chars + 2 <= EventParser.MaxChars, "A kill with long names is cut: " + k.Plain());
            TestAssert.Eq(k.Class, EventClass.Kill, "...and is still a KILL");
            TestAssert.That(k.Spans[k.Spans.Length - 1].Text == k.Spans[k.Spans.Length - 1].Text.TrimEnd(), "...with no space at the end");
            // A different room.
            EventLine narrow = EventParser.Parse("one two three four five six", null, null, 14);
            TestAssert.Eq(narrow.Plain(), "ONE TWO +", "A room of 14 cells keeps 'ONE TWO' and the plus (a third word would be 13 cells and the plus 15)");
        }

        private static void Ownership()
        {
            EventLine k = EventParser.Parse("<color=#FF4D4DFF>Pit Viper</color> shot down <color=#58A8FFFF>FS-20 Vortex</color>", "Pit Viper", "FS-20 Vortex");
            TestAssert.That(k.Own && k.Spans[0].Own && k.Spans[2].Own && !k.Spans[1].Own, "Both names are the pilot's own (player and aircraft)");
            EventLine o = EventParser.Parse("<color=#FF4D4DFF>Pit Viper</color> shot down <color=#58A8FFFF>FS-20 Vortex</color>", "someone else", "FS-20 vortex");
            TestAssert.That(o.Own && !o.Spans[0].Own && o.Spans[2].Own, "The aircraft matches in any case");
            TestAssert.That(!EventParser.Parse("<color=#FF4D4DFF>Pit Viper</color> shot down <color=#58A8FFFF>FS-20 Vortex</color>", "", null).Own, "No own names, no own lines");
            TestAssert.That(!EventParser.Parse("<color=#FF4D4DFF>Pit Viper Two</color> shot down <color=#58A8FFFF>X</color>", "Pit Viper").Own, "A name that only starts the same is not the pilot's");
            TestAssert.That(!EventParser.Parse("Pit Viper deployed", "Pit Viper").Own, "Plain text is not a name");
        }

        private static EventRow Row(EventLog log, string rich, float arrival, float removeAt, string own = null) =>
            log.Add(EventParser.Parse(rich, own), rich, EventLog.Stamp(arrival), arrival, removeAt);

        private static void Ageing()
        {
            var log = new EventLog();
            EventRow r = Row(log, "Hello there", 100f, 110f);
            TestAssert.That(r.Alive(109.99f) && !r.Alive(110f), "A row is alive until the game's removal time");
            TestAssert.Near(r.Wipe(100f), 0f, "Wipe is 0 on arrival");
            TestAssert.Near(r.Wipe(100.09f), 0.5f, "...half at 90 ms");
            TestAssert.Near(r.Wipe(100.18f), 1f, "...whole at 180 ms");
            TestAssert.Near(r.Wipe(105f), 1f, "...and stays whole");
            TestAssert.Near(r.Wipe(99f), 0f, "...never negative");
            TestAssert.That(r.Bright(100f) && r.Bright(101.49f) && !r.Bright(101.5f), "BRIGHT for 1.5 s");
            TestAssert.Near(r.Alpha(100f), 1f, "Full alpha at the start");
            TestAssert.Near(r.Alpha(109f), 1f, "...until the last second");
            TestAssert.Near(r.Alpha(109.5f), 0.5f, "...half way through it");
            TestAssert.Near(r.Alpha(109.99f), 0.01f, "...nearly gone");
            TestAssert.Near(r.Alpha(110f), 0f, "...gone at the removal time");
            EventRow s = Row(log, "Short", 200f, 201f);
            TestAssert.Near(s.Alpha(200f), 1f, "A life of one second fades over its second half: full at first");
            TestAssert.Near(s.Alpha(200.75f), 0.5f, "...half at 0.75 s (0.25 left of a 0.5 fade)");
            // The game's time cannot be read: 12 seconds.
            TestAssert.Near(Row(log, "x", 300f, float.NaN).RemoveAt, 312f, "NaN removal time: 12 s");
            TestAssert.Near(Row(log, "x", 300f, 299f).RemoveAt, 312f, "A removal time in the past: 12 s");
            TestAssert.Near(Row(log, "x", 300f, 300f).RemoveAt, 312f, "A removal time that is now: 12 s");
            TestAssert.Near(Row(log, "x", 300f, float.PositiveInfinity).RemoveAt, 312f, "Infinity: 12 s");
            TestAssert.Near(Row(log, "x", 300f, 9999f).RemoveAt, 312f, "Hours away: 12 s");
            TestAssert.Near(Row(log, "x", 300f, 320f).RemoveAt, 320f, "A sane time is kept");
        }

        private static void Ring()
        {
            var log = new EventLog();
            for (int i = 0; i < 8; i++) Row(log, "Line " + i, 10f + i, 100f);
            TestAssert.Eq(log.Count, 6, "The ring holds six");
            TestAssert.Eq(log[0].Line.Plain(), "LINE 2", "The two oldest were pushed out; the oldest left is first");
            TestAssert.Eq(log[5].Line.Plain(), "LINE 7", "...and the newest is last");
            TestAssert.That(log[0].Serial < log[1].Serial && log[4].Serial < log[5].Serial, "Serials count up with every line");
            TestAssert.Eq(log[5].Serial, 8, "The eighth line is serial 8");
            TestAssert.Eq(log.LiveCount(50f), 6, "All six are live before their time");
            TestAssert.Eq(log.LiveCount(100f), 0, "...and none at it");
            var into = new EventRow[EventLog.Capacity];
            TestAssert.Eq(log.Select(50f, BoardDeclutter.Full, into), 6, "Select returns every live row");
            TestAssert.That(into[0] == log[0] && into[5] == log[5], "...oldest first");
            // Rows that live different lengths drop out in the order they expire, not the order they came.
            var log2 = new EventLog();
            Row(log2, "Long", 10f, 40f);
            Row(log2, "Short", 11f, 20f);
            Row(log2, "Medium", 12f, 30f);
            TestAssert.Eq(log2.Select(25f, BoardDeclutter.Full, into), 2, "The short one is gone at 25");
            TestAssert.That(into[0].Line.Plain() == "LONG" && into[1].Line.Plain() == "MEDIUM", "...the others keep their order");
            TestAssert.Eq(log2.Select(35f, BoardDeclutter.Full, into), 1, "...and the medium one at 35");
            log2.Clear();
            TestAssert.Eq(log2.Count, 0, "Clear empties it");
            TestAssert.Eq(log2.Add(EventParser.Parse("again"), "again", "--:--", 5f, 9f).Serial, 4, "Serials go on counting after a clear");
        }

        private static void Filtering()
        {
            var log = new EventLog();
            Row(log, "<color=#FF4D4DFF>Pit Viper</color> shot down <color=#58A8FFFF>Bravo</color>", 10f, 100f, "Pit Viper");
            Row(log, "<color=#FF4D4DFF>Alpha</color> shot down <color=#58A8FFFF>Bravo</color>", 11f, 100f, "Pit Viper");
            Row(log, "<color=#FF4D4DFF>Pit Viper</color> crashed", 12f, 100f, "Pit Viper");
            Row(log, "<color=#AABBCCFF>Pilot One:</color> hello", 13f, 100f, "Pit Viper");
            Row(log, "Hawk 2-1: Contact", 14f, 100f, "Pit Viper");
            Row(log, "Slow Motion Enabled", 15f, 100f, "Pit Viper");
            var into = new EventRow[EventLog.Capacity];
            TestAssert.Eq(log.Select(20f, BoardDeclutter.Full, into), 6, "Full shows all");
            TestAssert.Eq(log.Select(20f, BoardDeclutter.Lean, into), 6, "Lean shows all (it drops the furniture, not rows)");
            int n = log.Select(20f, BoardDeclutter.Minimal, into);
            TestAssert.Eq(n, 3, "Minimal: the pilot's own KILL row and the two COMM rows");
            TestAssert.That(into[0].Line.Class == EventClass.Kill && into[0].Line.Own, "...the KILL involves the pilot");
            TestAssert.That(into[1].Line.Class == EventClass.Comm && into[2].Line.Class == EventClass.Comm, "...then the chat and the radio, in order");
            TestAssert.Eq(log.Select(200f, BoardDeclutter.Minimal, into), 0, "Nothing live, nothing shown");
        }

        private static void Signature()
        {
            var log = new EventLog();
            int none = log.Signature(10f, BoardDeclutter.Full);
            Row(log, "Hello", 10f, 30f);
            int a = log.Signature(10.00f, BoardDeclutter.Full);
            TestAssert.That(a != none, "A new row changes it");
            TestAssert.Eq(log.Signature(10.00f, BoardDeclutter.Full), a, "...and the same instant is the same");
            TestAssert.That(log.Signature(10.05f, BoardDeclutter.Full) != a, "A wipe step changes it");
            int done = log.Signature(10.2f, BoardDeclutter.Full);
            TestAssert.Eq(log.Signature(10.5f, BoardDeclutter.Full), done, "A settled row does not change it");
            TestAssert.That(log.Signature(11.6f, BoardDeclutter.Full) != done, "Ceasing to be BRIGHT changes it");
            int settled = log.Signature(12f, BoardDeclutter.Full);
            TestAssert.Eq(log.Signature(25f, BoardDeclutter.Full), settled, "...then nothing until the fade");
            TestAssert.That(log.Signature(29.5f, BoardDeclutter.Full) != settled, "A fade step changes it");
            TestAssert.Eq(log.Signature(30f, BoardDeclutter.Full), none, "A row that has gone is as if it had never come");
            TestAssert.That(log.Signature(12f, BoardDeclutter.Lean) != settled, "The level is part of it");
        }

        private static void Health()
        {
            var log = new EventLog();
            TestAssert.That(log.Healthy(100f, false), "No native text, healthy");
            TestAssert.That(log.Healthy(100f, true), "Native text and no row: still healthy for the first moment");
            TestAssert.That(log.Healthy(100.5f, true), "...and at half a second");
            TestAssert.That(!log.Healthy(100.7f, true), "...but not past 0.6 s: the capture is not seeing the game's lines");
            TestAssert.That(!log.Healthy(105f, true), "...and it stays unhealthy while it lasts");
            Row(log, "Late line", 105f, 115f);
            TestAssert.That(log.Healthy(105.1f, true), "A live row makes it healthy at once");
            TestAssert.That(log.Healthy(116f, true), "The row has gone and the grace starts again");
            TestAssert.That(log.Healthy(116.5f, true) && !log.Healthy(116.7f, true), "...for 0.6 s");
            TestAssert.That(log.Healthy(120f, false), "No native text resets it");
            TestAssert.That(log.Healthy(120.1f, true), "...and the grace is whole again");
        }

        private static void Clock()
        {
            TestAssert.Eq(EventLog.Stamp(0f), "00:00", "Stamp 0");
            TestAssert.Eq(EventLog.Stamp(65.9f), "01:05", "Stamp 65.9");
            TestAssert.Eq(EventLog.Stamp(599f), "09:59", "Stamp 599");
            TestAssert.Eq(EventLog.Stamp(3725f), "62:05", "Minutes run past 59");
            TestAssert.Eq(EventLog.Stamp(float.NaN), "--:--", "NaN has no stamp");
            TestAssert.Eq(EventLog.Stamp(-1f), "--:--", "...nor a negative time");
            TestAssert.Eq(EventLog.Stamp(float.PositiveInfinity), "--:--", "...nor infinity");
            // The scene starts again: the clock runs back and the old rows go.
            var log = new EventLog();
            Row(log, "Old", 500f, 520f);
            log.Tick(499.8f);
            TestAssert.Eq(log.Count, 1, "A little jitter is not a new scene");
            log.Tick(3f);
            TestAssert.Eq(log.Count, 0, "A clock that ran back empties the ring");
            Row(log, "Old", 500f, 520f);
            Row(log, "New", 4f, 20f);
            TestAssert.Eq(log.Count, 1, "A line that arrives from before the newest one starts the ring again");
            TestAssert.Eq(log[0].Line.Plain(), "NEW", "...with itself");
        }

        private static void Layout()
        {
            EventLogPlan none = EventLogLayout.Plan(0f, 0f, 0, 0, BoardDeclutter.Full);
            TestAssert.That(!none.Present, "No rows, no block");
            EventLogPlan p = EventLogLayout.Plan(0f, 0f, 4, 36, BoardDeclutter.Full);
            TestAssert.Near(p.Left, 16f, "The block shares the left stack's 16 unit inset");
            TestAssert.Near(p.Top, 12f, "...and 12 under its top (the shelf's line is then at y 18, the board's own)");
            TestAssert.Near(p.FirstRowY, 36f, "The first row is 24 under the block's top, under the shelf");
            TestAssert.Near(p.RowY(3), 36f + 48f, "Rows are 16 apart");
            TestAssert.Near(p.Height, 24f + 48f + 8f, "The block ends 8 under its last row");
            TestAssert.Near(p.StampX, 28f, "The stamp is 12 in from the block");
            TestAssert.Near(p.ClassX, 28f + 7f * 7.8f, "The class word follows the six cell stamp and a cell");
            TestAssert.Near(p.LineX, p.ClassX + 7f * 7.8f, "The line follows the five cell class word and two cells");
            TestAssert.That(p.Furniture, "Full draws the furniture");
            TestAssert.Near(p.SpineTop, p.FirstRowY - 8f, "The spine starts half a row over the first");
            TestAssert.Near(p.SpineBottom, p.RowY(3) + 8f, "...and ends half a row under the last");
            TestAssert.Near(p.Width % 40f, 0f, "The width is a multiple of 40");
            TestAssert.That(p.Width >= p.LineX - p.Left + 36f * 7.8f + 8f && p.Width - 40f < p.LineX - p.Left + 36f * 7.8f + 8f, "...the least one that holds the widest line and its margin");
            TestAssert.Near(EventLogLayout.Plan(0f, 0f, 1, 5, BoardDeclutter.Full).Width, 280f, "A short line gets the minimum of 280");
            EventLogPlan wide = EventLogLayout.Plan(0f, 0f, 6, EventParser.MaxChars, BoardDeclutter.Full);
            TestAssert.Near(wide.Width, 560f, "A line of the longest allowed fills 560");
            TestAssert.That(wide.LineX - wide.Left + EventParser.MaxChars * 7.8f + 8f <= 560f, "...and fits it");
            TestAssert.Near(EventLogLayout.Plan(0f, 0f, 6, 200, BoardDeclutter.Full).Width, 560f, "Never wider than 560");
            TestAssert.Eq(EventLogLayout.Plan(0f, 0f, 9, 10, BoardDeclutter.Full).Rows, 6, "Never more than six rows");
            EventLogPlan lean = EventLogLayout.Plan(0f, 0f, 4, 36, BoardDeclutter.Lean);
            TestAssert.That(!lean.Furniture, "Lean has no shelf, spine or stamps");
            TestAssert.Near(lean.FirstRowY, lean.Top + 8f, "...the first row is 8 under the top");
            TestAssert.Near(lean.ClassX, lean.Left + 6f, "...the class word starts nearer the edge");
            TestAssert.That(lean.LineX < p.LineX, "...and the line with it");
            EventLogPlan min = EventLogLayout.Plan(0f, 0f, 2, 36, BoardDeclutter.Minimal);
            TestAssert.That(!min.Furniture && min.Rows == 2, "Minimal is the same");
            EventLogPlan moved = EventLogLayout.Plan(40f, 200f, 1, 10, BoardDeclutter.Full);
            TestAssert.Near(moved.Left, 56f, "The block follows the box's corner");
            TestAssert.Near(moved.Top, 212f, "...in both directions");
        }
    }
}
