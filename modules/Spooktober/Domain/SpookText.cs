using System.Text;

namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>The words of the mystery transmission and the glitch that corrupts text on the COMMS feed and cards.</summary>
    internal static class SpookText
    {
        /// <summary>
        /// Airs once, 3 to 8 minutes after the first spawn, and is kept as LAST INTERCEPT on PILOT. It reads the SIGNAL answer
        /// (131.0) backwards: zero, one, three, one.
        /// </summary>
        public const string Transmission =
            "...any pilot on this net... never, ever solve the puzzles in SET. Whatever you hear on 31.10... don't answer. " +
            "Carrier tone reads: zero... one... three... one... zero... one... three... one... Do not tune it.";

        private const string Blocks = "\u2588\u2593\u2592\u2591";

        /// <summary>
        /// Swap about <paramref name="amount"/> (0 to 1) of the letters for block glyphs. Deterministic for a given text and seed;
        /// digits, spaces and punctuation are never touched.
        /// </summary>
        public static string Glitch(string text, int seed, float amount)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                uint h = Hash(seed, i);
                bool swap = char.IsLetter(c) && (h % 1000u) / 1000f < amount;
                sb.Append(swap ? Blocks[(int)((h / 1000u) % (uint)Blocks.Length)] : c);
            }
            return sb.ToString();
        }

        // Integer mix instead of System.Random so the same seed gives the same glitch on every runtime.
        private static uint Hash(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 747796405u + (uint)index * 2891336453u + 12345u;
                h ^= h >> 15; h *= 2246822519u;
                h ^= h >> 13; h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
