using System.Globalization;

namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>The three bosses of the menu. All are open from the start; each defeat is kept on its own.</summary>
    internal enum SpookBoss : byte { Kaiju, FallenAce, HellsArmy }

    /// <summary>
    /// Which bosses this player has beaten, and the one-line string it is stored as (a hidden config entry). Pure.
    /// </summary>
    internal sealed class SpookProgress
    {
        private const int AllBosses = 0b111;

        private int beaten;

        /// <summary>How many of the three bosses this player has beaten (0 to 3), in any order.</summary>
        public int ChainLevel => Count(beaten);

        public bool Beaten(SpookBoss b) => (beaten & (1 << (int)b)) != 0;

        /// <summary>Keeps a defeat of <paramref name="b"/>; true only the first time.</summary>
        public bool RecordDefeat(SpookBoss b)
        {
            if (Beaten(b)) return false;
            beaten |= 1 << (int)b;
            return true;
        }

        public string Save() => "v3;b=" + beaten;

        /// <summary>
        /// Anything unparseable or out of range loads fresh. v3 is the beaten mask alone; v2 (<c>v2;p=;b=;h=</c>, from the puzzle
        /// era) keeps its mask; v1 (the old in-order chain, <c>d=</c> 0 to 3) loads as its first d bosses beaten.
        /// </summary>
        public static SpookProgress Load(string text)
        {
            var fresh = new SpookProgress();
            if (string.IsNullOrEmpty(text)) return fresh;
            string[] part = text.Split(';');
            if (part.Length == 2 && part[0] == "v3")
                return Field(part[1], "b=", AllBosses, out int mask) ? new SpookProgress { beaten = mask } : fresh;
            if (part.Length != 4 || part[0] != "v1" && part[0] != "v2") return fresh;
            bool v1 = part[0] == "v1";
            if (!Field(part[2], v1 ? "d=" : "b=", v1 ? 3 : AllBosses, out int d)) return fresh;
            return new SpookProgress { beaten = v1 ? (1 << d) - 1 : d };
        }

        private static int Count(int mask)
        {
            int n = 0;
            for (int m = mask; m != 0; m &= m - 1) n++;
            return n;
        }

        private static bool Field(string part, string key, int max, out int value)
        {
            value = 0;
            return part.StartsWith(key, System.StringComparison.Ordinal)
                && int.TryParse(part.Substring(key.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value)
                && value <= max;
        }
    }

    /// <summary>What the SPOOKTOBER page shows and allows (pure; the widgets just ask).</summary>
    internal static class SpookMenu
    {
        public const string HellTitle = "HELL'S ARMY";

        /// <summary>The first card's title; the kaiju module has to be installed and on for the summon to work.</summary>
        public static string KaijuCard(bool summonAvailable) => summonAvailable ? "KAIJU" : "KAIJU MODULE OFF";

        /// <summary>Only the host may spawn, as with the kaiju summon; everyone else sees HOST ONLY.</summary>
        public static bool SpawnBlocked(bool isHost) => !isHost;
    }
}
