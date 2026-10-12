using System.Collections.Generic;

namespace BoscaliSummer.Modules.HighCommand.Domain
{
    /// <summary>Why a command-post kill did or did not pay (BountyLedger.Settle).</summary>
    internal enum BountyVerdict : byte
    {
        Pay,        // the first paid kill of this office by this killer faction: its ledger entry is now spent
        Off,        // the economy switch is off
        NotHuman,   // the killer is an AI faction, the post's own side, or unknown: nothing is paid and no entry is consumed
        Afield,     // the commander was away on a convoy: no kill, no bounty, no entry consumed
        Flipped,    // the building changed hands: it pays nobody
        Claimed,    // this killer faction already took this office (or the ledger is full and fails closed)
    }

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

        /// <summary>
        /// The one decision for a command-post kill. The first failing rule wins and only a kill that passes every other
        /// rule touches the ledger, so an AI killer or an absent commander never uses up the office.
        /// </summary>
        public BountyVerdict Settle(int killer, int office, bool economyOn, bool humanKiller, bool postFlipped, bool commanderAway) =>
            !economyOn ? BountyVerdict.Off :
            !humanKiller ? BountyVerdict.NotHuman :
            commanderAway ? BountyVerdict.Afield :
            postFlipped ? BountyVerdict.Flipped :
            TryClaim(killer, office) ? BountyVerdict.Pay : BountyVerdict.Claimed;

        /// <summary>Forget everything (scene reset).</summary>
        public void Clear() => claims.Clear();
    }
}
