using System.Collections.Generic;

namespace BoscaliSummer.Modules.HighCommand.Domain
{

    /// <summary>Who has already been paid for which office this mission: one paid kill per office per killer faction.</summary>
    internal sealed class BountyLedger
    {
        /// <summary>Hard ceiling on remembered claims (8 killers x 64 offices); a full ledger pays nothing.</summary>
        public const int MaxClaims = 512;
        private const int Offices = 64;

        private readonly HashSet<long> claims = new HashSet<long>();

        // killer is the FactionHQ instance id (may be negative); the office id takes the low byte.
        private static long Key(int killer, int office) => ((long)killer << 8) | (long)(office & 0xFF);

        /// <summary>True only for the first claim of (killer, office); false for a repeat, an office outside 0..63 or a full ledger.</summary>
        public bool TryClaim(int killer, int office) =>
            office >= 0 && office < Offices && claims.Count < MaxClaims && claims.Add(Key(killer, office));

        /// <summary>Whether (killer, office) was already claimed.</summary>
        public bool Claimed(int killer, int office) =>
            office >= 0 && office < Offices && claims.Contains(Key(killer, office));

        /// <summary>Forget everything (scene reset).</summary>
        public void Clear() => claims.Clear();
    }
}
