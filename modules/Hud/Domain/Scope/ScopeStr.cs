using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// Allocation-free words for the scope, pure. <see cref="Int"/> and <see cref="Pct"/> are cached strings; <see cref="Cat(string,int,string)"/>
    /// and its siblings compose into ONE shared scratch buffer and return a span of it, which the painter copies at once: use the result
    /// in the very call that receives it, never keep it, never nest two in one expression.
    /// </summary>
    internal static class ScopeStr
    {
        /// <summary>Whole numbers below this are cached (a thousand rounds, a 1200 kJ capacitor, an altitude in feet are all four digits).</summary>
        public const int IntCache = 10000;

        private static readonly string[] Ints = new string[IntCache];
        private static readonly string[] Pcts = new string[101];
        private static readonly char[] Scratch = new char[128];

        private static readonly string[] SpeedUnits = { "KM/H", "KT" }, SpeedUnitsSp = { " KM/H", " KT" };

        /// <summary>The speed unit in capitals ("KM/H", "KT") and with a leading space; cached, so a per-frame caller builds nothing.</summary>
        public static string SpeedUnit(NOAvionics.AvUnits units) => SpeedUnits[units == NOAvionics.AvUnits.Imperial ? 1 : 0];

        public static string SpeedUnitSp(NOAvionics.AvUnits units) => SpeedUnitsSp[units == NOAvionics.AvUnits.Imperial ? 1 : 0];

        /// <summary>"7", cached (made on first use) for 0 to <see cref="IntCache"/> - 1: a value of a thousand or more does not allocate in a steady frame either.</summary>
        public static string Int(int n)
        {
            if ((uint)n >= Ints.Length) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Ints[n] ?? (Ints[n] = n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>"42%", cached for 0..100 (clamped).</summary>
        public static string Pct(int n)
        {
            n = n < 0 ? 0 : n > 100 ? 100 : n;
            return Pcts[n] ?? (Pcts[n] = n + "%");
        }

        public static ReadOnlySpan<char> Cat(string a, string b) => Cat(a, b, null);

        public static ReadOnlySpan<char> Cat(string a, string b, string c)
        {
            int n = Put(0, a);
            n = Put(n, b);
            n = Put(n, c);
            return new ReadOnlySpan<char>(Scratch, 0, n);
        }

        /// <summary>"a" + "b" + value + "c" (a seeker word, a space and a figure, without joining strings first).</summary>
        public static ReadOnlySpan<char> Cat(string a, string b, int value, string c)
        {
            int n = Put(0, a);
            n = Put(n, b);
            n = PutInt(n, value);
            n = Put(n, c);
            return new ReadOnlySpan<char>(Scratch, 0, n);
        }

        public static ReadOnlySpan<char> Cat(string a, int value) => Cat(a, value, null);

        public static ReadOnlySpan<char> Cat(string a, int value, string b)
        {
            int n = Put(0, a);
            n = PutInt(n, value);
            n = Put(n, b);
            return new ReadOnlySpan<char>(Scratch, 0, n);
        }

        /// <summary>"a" + value with <paramref name="decimals"/> places + "b"; a sign is written for a negative value, or always with <paramref name="plus"/>.</summary>
        public static ReadOnlySpan<char> Cat(string a, double value, int decimals, string b, bool plus = false)
        {
            int n = Put(0, a);
            n = NOAvionics.AvNumFormat.Write(Scratch, n, value, decimals, plus);
            n = Put(n, b);
            return new ReadOnlySpan<char>(Scratch, 0, n);
        }

        /// <summary>"a" + v1 + sep + v2 + "b" (two integers: a clock, a ratio).</summary>
        public static ReadOnlySpan<char> Cat(string a, int v1, string sep, int v2, string b)
        {
            int n = Put(0, a);
            n = PutInt(n, v1);
            n = Put(n, sep);
            n = PutInt(n, v2);
            n = Put(n, b);
            return new ReadOnlySpan<char>(Scratch, 0, n);
        }

        /// <summary>"K92 22KM": the name (upper case, cut with an ellipsis so the whole fits <paramref name="maxChars"/>, left out when three characters of it do not fit), a space and the distance in the player's units.</summary>
        public static ReadOnlySpan<char> Distance(string name, float metres, NOAvionics.AvUnits units, int maxChars)
        {
            int dist = NOAvionics.AvUnitTable.DistanceReading(Scratch, 100, metres, units) - 100;   // measured in the tail of the scratch, then moved up
            int room = maxChars - dist - 1, n = 0;
            string upper = string.IsNullOrEmpty(name) || room < 3 ? "" : name.Trim().ToUpperInvariant();     // no room for a name: the distance alone
            if (upper.Length > room) { n = Put(0, upper.Substring(0, room - 1)); Scratch[n++] = '…'; }
            else n = Put(0, upper);
            if (n > 0) Scratch[n++] = ' ';
            Array.Copy(Scratch, 100, Scratch, n, dist);
            for (int i = n; i < n + dist; i++) Scratch[i] = char.ToUpperInvariant(Scratch[i]);
            return new ReadOnlySpan<char>(Scratch, 0, n + dist);
        }

        /// <summary>The base's name on a row of its own: upper case, trimmed, cut with an ellipsis so it fits <paramref name="maxChars"/>; empty with no name or no room.</summary>
        public static ReadOnlySpan<char> Name(string name, int maxChars)
        {
            if (string.IsNullOrEmpty(name) || maxChars < 1) return default;
            int a = 0, b = name.Length;
            while (a < b && char.IsWhiteSpace(name[a])) a++;
            while (b > a && char.IsWhiteSpace(name[b - 1])) b--;
            int len = Math.Min(Math.Min(b - a, maxChars), Scratch.Length), keep = b - a > len ? len - 1 : len;
            for (int i = 0; i < keep; i++) Scratch[i] = char.ToUpperInvariant(name[a + i]);
            if (keep < len) Scratch[keep] = '…';
            return new ReadOnlySpan<char>(Scratch, 0, len);
        }

        /// <summary>Blank characters kept between the distance and the margin figure on the SYSTEMS strip's base line.</summary>
        public const int MarginGap = 1;

        /// <summary>
        /// How the base line (the name, a space and the distance at the left, a margin figure at the right edge) spends <paramref name="totalChars"/> so the NAME stays whole: the margin gives way first
        /// and never takes room from the name. 0: the margin of <paramref name="fullChars"/> characters fits with <see cref="MarginGap"/> blank after the distance; 1: its short form of
        /// <paramref name="shortChars"/> does (0: it has none); 2: the margin is left out. <paramref name="room"/> is what the name, space and distance may take (pass it to <see cref="Distance"/>, which cuts a name
        /// that cannot fit even then).
        /// </summary>
        public static int FitMargin(string name, float metres, NOAvionics.AvUnits units, int totalChars, int fullChars, int shortChars, out int room)
        {
            int dist = NOAvionics.AvUnitTable.DistanceReading(Scratch, 100, metres, units) - 100;
            int whole = (string.IsNullOrEmpty(name) ? 0 : name.Trim().Length + 1) + dist;
            int form = whole + MarginGap + fullChars <= totalChars ? 0 : shortChars > 0 && whole + MarginGap + shortChars <= totalChars ? 1 : 2;
            room = form == 2 ? totalChars : totalChars - MarginGap - (form == 0 ? fullChars : shortChars);
            return form;
        }

        private static int Put(int at, string s)
        {
            if (string.IsNullOrEmpty(s)) return at;
            int len = Math.Min(s.Length, Scratch.Length - at);
            s.CopyTo(0, Scratch, at, len);
            return at + len;
        }

        private static int PutInt(int at, int value)
        {
            if (at >= Scratch.Length - 11) return at;
            if (value < 0) { Scratch[at++] = '-'; value = -value; }
            int start = at;
            do { Scratch[at++] = (char)('0' + value % 10); value /= 10; } while (value > 0);
            Array.Reverse(Scratch, start, at - start);
            return at;
        }
    }
}
