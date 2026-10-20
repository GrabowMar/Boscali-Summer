using System.Globalization;

namespace BoscaliSummer.Modules.Spooktober.Domain
{
    /// <summary>The boss chain, in order: each one is opened by beating the one before it. The last is a locked placeholder.</summary>
    internal enum SpookBoss : byte { Kaiju, FallenAce, Redacted }

    /// <summary>
    /// What this player has solved and beaten, and the one-line string it is stored as (a hidden config entry).
    /// Pure (pinned by tests/BoscaliSummer.Tests/Features/Spooktober).
    /// </summary>
    internal sealed class SpookProgress
    {
        private const int AllPuzzles = 0b1111;
        private const int MaxChain = 2;

        private int solved;

        public bool HeardIntercept { get; set; }

        /// <summary>0 nothing beaten, 1 kaiju beaten, 2 fallen ace beaten.</summary>
        public int ChainLevel { get; private set; }

        public bool Has(SpookPuzzle p) => (solved & (1 << (int)p)) != 0;

        /// <summary>Mark a puzzle solved; true only the first time.</summary>
        public bool Solve(SpookPuzzle p)
        {
            if (Has(p)) return false;
            solved |= 1 << (int)p;
            return true;
        }

        public int SolvedCount
        {
            get
            {
                int n = 0;
                for (int m = solved; m != 0; m &= m - 1) n++;
                return n;
            }
        }

        public bool Unlocked => solved == AllPuzzles;

        /// <summary>Kaiju is open once unlocked, the ace once the kaiju fell; the placeholder stays shut.</summary>
        public bool BossOpen(SpookBoss b)
        {
            switch (b)
            {
                case SpookBoss.Kaiju: return Unlocked;
                case SpookBoss.FallenAce: return Unlocked && ChainLevel >= 1;
                default: return false;
            }
        }

        /// <summary>A defeat only advances the chain from the level directly below it; true when it did.</summary>
        public bool RecordDefeat(SpookBoss b)
        {
            if ((int)b != ChainLevel || ChainLevel >= MaxChain) return false;
            ChainLevel++;
            return true;
        }

        public string Save() => "v1;p=" + solved + ";d=" + ChainLevel + ";h=" + (HeardIntercept ? 1 : 0);

        /// <summary>Anything unparseable or out of range (null, wrong version, p over 15, d over 2, h not 0/1) loads fresh.</summary>
        public static SpookProgress Load(string text)
        {
            var fresh = new SpookProgress();
            if (string.IsNullOrEmpty(text)) return fresh;
            string[] part = text.Split(';');
            if (part.Length != 4 || part[0] != "v1") return fresh;
            if (!Field(part[1], "p=", AllPuzzles, out int p) || !Field(part[2], "d=", MaxChain, out int d) || !Field(part[3], "h=", 1, out int h)) return fresh;
            return new SpookProgress { solved = p, ChainLevel = d, HeardIntercept = h == 1 };
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
        public const string RedactedTitle = "[REDACTED] \u00b7 31.10";

        /// <summary>The first card's title; the kaiju module has to be installed and on for the summon to work.</summary>
        public static string KaijuCard(bool summonAvailable) => summonAvailable ? "KAIJU" : "KAIJU MODULE OFF";

        /// <summary>Only the host may spawn, as with the kaiju summon; everyone else sees HOST ONLY.</summary>
        public static bool SpawnBlocked(bool isHost) => !isHost;
    }
}
