namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// The effects a perk can scale. This enum lives in the contract rather than in the
    /// Progression feature because Framework may not import a concrete feature namespace.
    /// </summary>
    internal enum PerkEffect : byte
    {
        FuelUse = 0,
        CombatReward = 1,
        ServiceReward = 2,
        ObjectiveReward = 3,
        SupportCost = 4
    }

    /// <summary>
    /// Server-side perk effects. A perk grants zero or more capability strings; a support
    /// action requires exactly one. That single rule is the whole coupling between the two
    /// features, and <c>PerkCatalogTests</c> asserts both catalogues agree on the set.
    /// </summary>
    internal interface IPlayerPerks
    {
        /// <summary>Multiplier for one effect, 1.0 when the player owns no relevant perk.</summary>
        float Multiplier(ulong playerId, PerkEffect effect);

        bool Grants(ulong playerId, string capability);
    }

    internal static class SupportCapabilities
    {
        public const string Airdrop = "support.airdrop";
        public const string AirDefenceDrop = "support.airdrop-aa";
        public const string Convoy = "support.convoy";
        public const string Recon = "support.recon";
        public const string Fortify = "support.fortify";
        public const string Artillery = "support.artillery";
    }
}
