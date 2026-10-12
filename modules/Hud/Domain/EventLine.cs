using System;
using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What a line of the game's feed is about: the class word DL EVT LOG puts in front of it (<see cref="EventWords.Of"/>).</summary>
    internal enum EventClass : byte { Info, Intcp, Kill, Demo, Crash, Capt, Net, Comm }

    /// <summary>How a span of a line is drawn: a name (INK, BRIGHT when it is the pilot's own, with a square in its native colour when the game coloured it), the verb between names (DIM), plain text (INK).</summary>
    internal enum EventRole : byte { Name, Verb, Text }

    internal static class EventWords
    {
        /// <summary>The class word, three to five capitals.</summary>
        public static string Of(EventClass c)
        {
            switch (c)
            {
                case EventClass.Intcp: return "INTCP";
                case EventClass.Kill: return "KILL";
                case EventClass.Demo: return "DEMO";
                case EventClass.Crash: return "CRASH";
                case EventClass.Capt: return "CAPT";
                case EventClass.Net: return "NET";
                case EventClass.Comm: return "COMM";
                default: return "INFO";
            }
        }
    }

    /// <summary>One run of a line: its words in capitals (edge spaces kept: they are the gaps between spans), the game's colour for it and what it is.</summary>
    internal struct EventSpan
    {
        public string Text;
        /// <summary>The native colour tag's 0xRRGGBB, -1 for a span the game left uncoloured.</summary>
        public int Rgb;
        public EventRole Role;
        /// <summary>The pilot's own name or aircraft.</summary>
        public bool Own;

        /// <summary>A name the game coloured wears a small square in that colour (one character cell wide).</summary>
        public readonly bool Swatch => Role == EventRole.Name && Rgb >= 0;
    }

    /// <summary>A line of the feed read into spans, class and all, and fitted to the row (<see cref="EventParser.Parse"/>).</summary>
    internal sealed class EventLine
    {
        public EventClass Class;
        public EventSpan[] Spans;
        /// <summary>Whole words were cut from the end to fit: the row ends in a DIM "+".</summary>
        public bool Truncated;
        /// <summary>The pilot's own name or aircraft is in it.</summary>
        public bool Own;
        /// <summary>Character cells the kept spans take (a swatch is one cell), without the "+" and its gap.</summary>
        public int Chars;

        /// <summary>The kept words as one string (what a row reads as, without the squares), with the " +" when cut.</summary>
        public string Plain()
        {
            var sb = new StringBuilder();
            foreach (EventSpan s in Spans) sb.Append(s.Text);
            if (Truncated) sb.Append(" +");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Reads one line of the game's message feed (MessageUI: kill lines, game messages, chat, the mod's radio subtitles) into spans, pure. The game marks names with TMP colour tags
    /// (<c>&lt;color=#RRGGBBAA&gt;name&lt;/color&gt;</c>, nested or left open too); every other tag is dropped, the words never are: a line it does not recognise is INFO with the whole text kept, and
    /// reordering never happens. The class comes from the verb the game writes between the names (INTERCEPTED, SHOT DOWN, DESTROYED, KILLED, SANK, DEMOLISHED, COLLAPSED, CRASHED, CAPTURED, JOINED, LEFT),
    /// searched in the words the game left uncoloured (a unit called "Left Hook" is not a NET line), or in all of it when the game coloured the whole line (the join message); a speaker and a colon
    /// ("Name: text": chat, radio) is COMM and wins over any verb in the text. A line longer than the row loses whole words from its end (<see cref="EventLine.Truncated"/>); never dots.
    /// </summary>
    internal static class EventParser
    {
        /// <summary>The room a row's line has, in character cells (the 560 unit block less its columns; the 12 unit mono face advances 7.8 a cell).</summary>
        public const int MaxChars = 55;

        /// <summary>The longest "Name:" the game's chat can write: "(ally)" and a 32 character name, and the colon.</summary>
        private const int SpeakerMax = 40;

        public static EventLine Parse(string rich, string ownPlayer = null, string ownAircraft = null, int maxChars = MaxChars)
        {
            List<EventSpan> spans = Runs(rich ?? "");
            Normalise(spans);
            var line = new EventLine { Class = EventClass.Info };
            int speaker = SpeakerLength(spans);
            if (speaker > 0)
            {
                line.Class = EventClass.Comm;
                SplitAt(spans, speaker);
                int taken = 0;
                for (int i = 0; i < spans.Count; i++)
                {
                    EventSpan s = spans[i];
                    s.Role = taken < speaker ? EventRole.Name : EventRole.Text;
                    taken += s.Text.Length;
                    spans[i] = s;
                }
            }
            else
            {
                line.Class = Verb(spans);
                bool named = false;
                foreach (EventSpan s in spans) if (s.Rgb >= 0) named = true;
                for (int i = 0; i < spans.Count; i++)
                {
                    EventSpan s = spans[i];
                    s.Role = s.Rgb >= 0 ? EventRole.Name : named ? EventRole.Verb : EventRole.Text;
                    spans[i] = s;
                }
            }
            string a = Name(ownPlayer), b = Name(ownAircraft);
            for (int i = 0; i < spans.Count; i++)
            {
                EventSpan s = spans[i];
                if (s.Role != EventRole.Name) continue;
                string n = Name(s.Text);
                s.Own = n.Length > 0 && (n == a || n == b);
                line.Own |= s.Own;
                spans[i] = s;
            }
            line.Truncated = Fit(spans, maxChars);
            line.Spans = spans.ToArray();
            line.Chars = Cells(spans);
            return line;
        }

        // ---------------------------------------------------------------------------------------------------- the tags

        /// <summary>The line cut into runs of one colour each (adjacent runs of the same colour joined), tags dropped; a colour tag nests, a close tag with nothing open is ignored, one left open runs to the end.</summary>
        private static List<EventSpan> Runs(string s)
        {
            var runs = new List<EventSpan>();
            var text = new StringBuilder();
            var open = new List<int>();
            int rgb = -1, i = 0;
            void Flush()
            {
                if (text.Length == 0) return;
                if (runs.Count > 0 && runs[runs.Count - 1].Rgb == rgb)
                {
                    EventSpan last = runs[runs.Count - 1];
                    last.Text += text.ToString();
                    runs[runs.Count - 1] = last;
                }
                else runs.Add(new EventSpan { Text = text.ToString(), Rgb = rgb });
                text.Clear();
            }
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '<')
                {
                    if (i + 1 < s.Length && s[i + 1] == '<') { text.Append('<'); i++; continue; }      // the game writes a typed '<' as "<<i></i>": a literal one, then two empty tags
                    int close = s.IndexOf('>', i + 1);
                    if (close > i + 1 && IsTag(s, i + 1, close))
                    {
                        int at = i + 1, nameEnd = at;
                        if (s[nameEnd] == '/') nameEnd++;
                        while (nameEnd < close && s[nameEnd] != '=' && s[nameEnd] != ' ') nameEnd++;
                        string name = s.Substring(at, nameEnd - at).ToLowerInvariant();
                        string value = nameEnd < close && s[nameEnd] == '=' ? s.Substring(nameEnd + 1, close - nameEnd - 1).Trim().Trim('"', '\'') : null;
                        if (name == "color" || name[0] == '#')
                        {
                            Flush();
                            int parsed = Color(name[0] == '#' ? name : value);
                            open.Add(parsed >= 0 ? parsed : rgb);
                            rgb = open[open.Count - 1];
                        }
                        else if (name == "/color")
                        {
                            Flush();
                            if (open.Count > 0) open.RemoveAt(open.Count - 1);
                            rgb = open.Count > 0 ? open[open.Count - 1] : -1;
                        }
                        i = close + 1;
                        continue;
                    }
                }
                text.Append(c < ' ' ? ' ' : c);
                i++;
            }
            Flush();
            return runs;
        }

        /// <summary>The inside of a tag: a name (or '/' and a name, or a '#' colour) and no further '&lt;'.</summary>
        private static bool IsTag(string s, int from, int close)
        {
            if (close - from > 80) return false;
            char first = s[from];
            if (!(first == '/' || first == '#' || char.IsLetter(first))) return false;
            for (int i = from; i < close; i++) if (s[i] == '<') return false;
            return true;
        }

        /// <summary>#RGB, #RGBA, #RRGGBB, #RRGGBBAA (the alpha dropped; the game's AddColor writes eight digits) or one of TMP's names; -1 when it is none of them.</summary>
        public static int Color(string v)
        {
            if (string.IsNullOrEmpty(v)) return -1;
            if (v[0] != '#')
            {
                switch (v.ToLowerInvariant())
                {
                    case "black": return 0x000000;
                    case "blue": return 0x0000FF;
                    case "green": return 0x008000;
                    case "orange": return 0xFFA500;
                    case "purple": return 0x800080;
                    case "red": return 0xFF0000;
                    case "white": return 0xFFFFFF;
                    case "yellow": return 0xFFFF00;
                    case "grey": case "gray": return 0x808080;
                    default: return -1;
                }
            }
            string h = v.Substring(1);
            if (h.Length == 3 || h.Length == 4) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            else if (h.Length == 8) h = h.Substring(0, 6);
            if (h.Length != 6) return -1;
            return int.TryParse(h, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out int rgb) ? rgb : -1;
        }

        // ---------------------------------------------------------------------------------------------------- the words

        /// <summary>Capitals, whitespace as single spaces (also across spans), the line's ends trimmed, empty spans gone.</summary>
        private static void Normalise(List<EventSpan> spans)
        {
            bool space = true;      // the line starts as after a space: no leading gap
            for (int i = 0; i < spans.Count; i++)
            {
                EventSpan s = spans[i];
                var sb = new StringBuilder(s.Text.Length);
                foreach (char c in s.Text.ToUpperInvariant())
                {
                    bool white = char.IsWhiteSpace(c);
                    if (white && space) continue;
                    sb.Append(white ? ' ' : c);
                    space = white;
                }
                s.Text = sb.ToString();
                spans[i] = s;
            }
            for (int i = spans.Count - 1; i >= 0; i--)      // no trailing gap, and no span left with nothing in it
            {
                EventSpan s = spans[i];
                s.Text = s.Text.TrimEnd();
                if (s.Text.Length == 0) { spans.RemoveAt(i); continue; }
                spans[i] = s;
                break;
            }
            spans.RemoveAll(s => s.Text.Length == 0);
        }

        /// <summary>The characters of "Name:" at the start of a chat or radio line (the speaker and its colon), 0 when the line has none.</summary>
        private static int SpeakerLength(List<EventSpan> spans)
        {
            var sb = new StringBuilder();
            foreach (EventSpan s in spans) sb.Append(s.Text);
            string p = sb.ToString();
            int colon = p.IndexOf(':');
            if (colon < 1 || colon + 1 > SpeakerMax) return 0;
            if (colon + 1 < p.Length && p[colon + 1] != ' ') return 0;
            int words = 1;
            for (int i = 0; i < colon; i++) if (p[i] == ' ') words++;
            return words <= 4 ? colon + 1 : 0;
        }

        /// <summary>Make a span boundary at <paramref name="at"/> characters from the line's start (splitting the span it falls in).</summary>
        private static void SplitAt(List<EventSpan> spans, int at)
        {
            int taken = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                int len = spans[i].Text.Length;
                if (at > taken && at < taken + len)
                {
                    EventSpan head = spans[i], tail = spans[i];
                    head.Text = spans[i].Text.Substring(0, at - taken);
                    tail.Text = spans[i].Text.Substring(at - taken);
                    spans[i] = head;
                    spans.Insert(i + 1, tail);
                    return;
                }
                taken += len;
            }
        }

        /// <summary>The class from the verb: in the uncoloured words if there are any, else in the whole line.</summary>
        private static EventClass Verb(List<EventSpan> spans)
        {
            var plain = new StringBuilder();
            var all = new StringBuilder();
            foreach (EventSpan s in spans)
            {
                all.Append(s.Text);
                if (s.Rgb < 0) plain.Append(s.Text);
            }
            bool letters = false;
            for (int i = 0; i < plain.Length; i++) if (char.IsLetter(plain[i])) { letters = true; break; }
            return Verb((letters ? plain : all).ToString());
        }

        /// <summary>The first known verb, word by word (a word is a run of letters: "FGA-57" is two).</summary>
        private static EventClass Verb(string upper)
        {
            int i = 0, n = upper.Length;
            while (i < n)
            {
                while (i < n && !char.IsLetter(upper[i])) i++;
                int j = i;
                while (j < n && char.IsLetter(upper[j])) j++;
                if (j > i)
                {
                    switch (upper.Substring(i, j - i))
                    {
                        case "INTERCEPTED": return EventClass.Intcp;
                        case "DESTROYED": case "KILLED": case "SANK": return EventClass.Kill;
                        case "SHOT":
                            if (string.CompareOrdinal(upper, j, " DOWN", 0, 5) == 0 && (j + 5 >= n || !char.IsLetter(upper[j + 5]))) return EventClass.Kill;
                            break;
                        case "DEMOLISHED": case "COLLAPSED": return EventClass.Demo;
                        case "CRASHED": return EventClass.Crash;
                        case "CAPTURED": return EventClass.Capt;
                        case "JOINED": case "LEFT": case "DISCONNECTED": return EventClass.Net;
                    }
                }
                i = j;
            }
            return EventClass.Info;
        }

        /// <summary>A name as the pilot is known by it: capitals, no "(ALLY)" in front, no colon behind.</summary>
        private static string Name(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string n = s.Trim().ToUpperInvariant();
            if (n.StartsWith("(ALLY)", StringComparison.Ordinal)) n = n.Substring(6);
            return n.TrimEnd(':', ' ');
        }

        // ---------------------------------------------------------------------------------------------------- the fit

        private static int Cells(List<EventSpan> spans)
        {
            int n = 0;
            foreach (EventSpan s in spans) n += (s.Swatch ? 1 : 0) + s.Text.Length;
            return n;
        }

        /// <summary>Whole words off the end until the line and its " +" fit <paramref name="max"/> cells; a first word that alone is too long is cut to the room (no dots). True when anything was cut.</summary>
        private static bool Fit(List<EventSpan> spans, int max)
        {
            if (Cells(spans) <= max) return false;
            int room = Math.Max(1, max - 2);
            while (spans.Count > 0 && Cells(spans) > room)
            {
                int last = spans.Count - 1;
                EventSpan s = spans[last];
                string t = s.Text.TrimEnd();
                int cut = t.LastIndexOf(' ');
                if (cut > 0) { s.Text = t.Substring(0, cut); spans[last] = s; }
                else if (spans.Count > 1) spans.RemoveAt(last);
                else
                {
                    s.Text = t.Substring(0, Math.Min(t.Length, Math.Max(1, room - (s.Swatch ? 1 : 0))));
                    spans[last] = s;
                    break;
                }
            }
            if (spans.Count > 0)
            {
                EventSpan s = spans[spans.Count - 1];
                s.Text = s.Text.TrimEnd();
                spans[spans.Count - 1] = s;
            }
            return true;
        }
    }
}
