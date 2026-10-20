using System;
using System.Text;

namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>The four hidden puzzles on the PILOT console (any order); solving all four unlocks the menu.</summary>
    internal enum SpookPuzzle : byte { Signal, Cipher, Breaker, Echo }

    /// <summary>
    /// Answers and rules of the SET puzzles. Pure (pinned by tests/BoscaliSummer.Tests/Features/Spooktober):
    /// the widgets only ask these functions whether the player got it. Text answers forgive trim and case, nothing else.
    /// </summary>
    internal static class SpookPuzzles
    {
        // SIGNAL: a SCAN FREQ stepper in tenths of a MHz; the transmission reads 131.0 backwards.
        public const int SignalTenths = 1310;
        public const int SignalMin = 1180, SignalMax = 1369;
        public const float SignalHoldSeconds = 2f;

        public static bool SignalSolved(int tenths, float heldSeconds) => tenths == SignalTenths && heldSeconds >= SignalHoldSeconds;

        // CIPHER: a corrupted label that is the word shifted by a plain ROT-n shown on the same page.
        public const string CipherWord = "HOLLOW";
        public const int CipherShift = 7;

        public static string CipherLabel => Rot(CipherWord, CipherShift);

        public static bool CipherSolved(string answer) => Matches(answer, CipherWord);

        /// <summary>Caesar shift of A-Z; every other character (lower case, digits, spaces) is kept as it is.</summary>
        public static string Rot(string text, int shift)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append(c >= 'A' && c <= 'Z' ? (char)('A' + ((c - 'A' + shift) % 26 + 26) % 26) : c);
            return sb.ToString();
        }

        // BREAKER: six SENSOR BANK toggles, lights-out rules; bit i is bank i, set = on.
        public const int BreakerCount = 6;
        public const int BreakerStart = 0b000010;

        /// <summary>Press bank <paramref name="index"/>: it and its neighbours flip (the ends have one neighbour). Off the panel does nothing.</summary>
        public static int BreakerPress(int state, int index)
        {
            if (index < 0 || index >= BreakerCount) return state;
            int flip = 0;
            for (int i = Math.Max(0, index - 1); i <= Math.Min(BreakerCount - 1, index + 1); i++) flip |= 1 << i;
            return state ^ flip;
        }

        public static bool BreakerSolved(int state)
        {
            int all = (1 << BreakerCount) - 1;
            return (state & all) == all;
        }

        // ECHO: the footer ticker blinks the word in Morse; the player types the word.
        public const string EchoWord = "GHOST";

        private static readonly string[] MorseTable =
        {
            ".-", "-...", "-.-.", "-..", ".", "..-.", "--.", "....", "..", ".---", "-.-", ".-..", "--",
            "-.", "---", ".--.", "--.-", ".-.", "...", "-", "..-", "...-", ".--", "-..-", "-.--", "--.."
        };

        /// <summary>Standard ITU letters, one space between letters. Anything that is not a letter is dropped.</summary>
        public static string Morse(string word)
        {
            if (string.IsNullOrEmpty(word)) return "";
            var sb = new StringBuilder();
            foreach (char c in word.ToUpperInvariant())
            {
                if (c < 'A' || c > 'Z') continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(MorseTable[c - 'A']);
            }
            return sb.ToString();
        }

        public static bool EchoSolved(string answer) => Matches(answer, EchoWord);

        private static bool Matches(string answer, string expected) =>
            answer != null && string.Equals(answer.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
