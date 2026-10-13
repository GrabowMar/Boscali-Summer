using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Chatter
{
    /// <summary>
    /// Which lines a recorded clip serves, from its file name alone (docs/CHATTER_PACKS.md, "Voice packs"). A name is cut into letter-and-digit tokens; plain numbers are
    /// ignored, so <c>003_fox3_fireFox3.ogg</c> works. A token of eight hex digits is a line id: that clip is the recording of exactly that line. Any other token is an
    /// event: the name after the net's dot (<c>fox3</c>, <c>checkin</c>), or a WSO Yappinator event name, so packs made for that mod keep working. A Yappinator
    /// catch-all (<c>fireMissile</c>) is a fallback: it is used only when the pack has nothing recorded for the exact call. Pure.
    /// </summary>
    internal static class ChatterVoiceKeys
    {
        private static readonly Dictionary<string, string[]> Exact = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "firefox2", new[] { "wing.fox2" } }, { "firefox3", new[] { "wing.fox3" } },
            { "firearm", new[] { "wing.magnum" } }, { "fireagm", new[] { "wing.rifle" } },
            { "killgeneric", new[] { "wing.splash" } }, { "killaircraft", new[] { "wing.splash" } },
            { "rwron", new[] { "wing.panic" } }, { "rwroff", new[] { "wing.defensiveclear" } },
            { "fuellow", new[] { "wing.joker", "wing.bingo" } }, { "touchdown", new[] { "wing.down" } },
            { "spawn", new[] { "wing.airborne" } }, { "takedamage", new[] { "wing.damaged" } }, { "eject", new[] { "wing.ejected" } },
            // Names the radio used before the packs.
            { "airbornerejoining", new[] { "wing.airborne" } }, { "unableorder", new[] { "wing.unable" } },
        };

        private static readonly Dictionary<string, string[]> Fallback = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "firemissile", new[] { "wing.fox2", "wing.fox3", "wing.magnum", "wing.rifle" } },
        };

        /// <summary>The key of one exact line's recording.</summary>
        public static string Line(string lineId) => "id:" + lineId;

        /// <summary>
        /// The keys <paramref name="fileName"/> (no extension) serves on <paramref name="net"/> ("wing", "awacs", ...): exact ones into <paramref name="exact"/>, catch-alls
        /// into <paramref name="fallback"/>. <paramref name="known"/> says whether an event id exists in the packs.
        /// </summary>
        public static void Of(string fileName, string net, Func<string, bool> known, List<string> exact, List<string> fallback)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            int start = -1;
            for (int i = 0; i <= fileName.Length; i++)
            {
                bool word = i < fileName.Length && char.IsLetterOrDigit(fileName[i]);
                if (word && start < 0) start = i;
                if (word || start < 0) continue;
                Token(fileName.Substring(start, i - start).ToLowerInvariant(), net, known, exact, fallback);
                start = -1;
            }
        }

        private static void Token(string token, string net, Func<string, bool> known, List<string> exact, List<string> fallback)
        {
            bool digits = true, hex = token.Length == 8;
            foreach (char c in token)
            {
                if (c < '0' || c > '9') digits = false;
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) hex = false;
            }
            if (hex) { Add(exact, Line(token)); return; }
            if (digits) return;
            if (Exact.TryGetValue(token, out string[] events)) { foreach (string e in events) Add(exact, e); return; }
            if (Fallback.TryGetValue(token, out events)) { foreach (string e in events) Add(fallback, e); return; }
            string evt = net + "." + token;
            if (known == null || known(evt)) Add(exact, evt);
        }

        private static void Add(List<string> list, string key)
        {
            if (!list.Contains(key)) list.Add(key);
        }
    }
}
