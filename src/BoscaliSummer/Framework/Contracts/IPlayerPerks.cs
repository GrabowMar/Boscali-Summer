namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Perk-scaled effect kinds. Lives here so Framework need not import Progression.</summary>
    internal enum PerkEffect : byte
    {
        FuelUse = 0,
        CombatReward = 1,
        ServiceReward = 2,
        ObjectiveReward = 3,
        SupportCost = 4
    }

    /// <summary>
    /// Server perk effects. A perk grants capability strings; a support action requires one.
    /// </summary>
    internal interface IPlayerPerks
    {
        /// <summary>Multiplier for one effect, 1.0 when the player owns no relevant perk.</summary>
        float Multiplier(ulong playerId, PerkEffect effect);

        bool Grants(ulong playerId, string capability);
    }

    internal static class SupportCapabilities
    {
        public const string Recon = "support.recon";
        public const string Fortify = "support.fortify";
        public const string Artillery = "support.artillery";
        public const string Emp = "support.emp";
    }
}
