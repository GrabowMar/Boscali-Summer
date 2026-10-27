using System.Text;

namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>The glitch that corrupts text on the COMMS feed and cards.</summary>
    internal static class SpookText
    {
        // ASCII noise: the console and HUD fonts have no block glyphs (they printed as empty boxes in game).
        private const string Blocks = "##%:";

        /// <summary>
        /// Swap about <paramref name="amount"/> (0 to 1) of the letters for noise glyphs. Deterministic for a given text and seed;
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
