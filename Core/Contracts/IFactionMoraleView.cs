namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Host-owned faction mood used when generating a contract offer.</summary>
    internal interface IFactionMoraleView
    {
        bool TryGetContractMultiplier(int factionId, out float multiplier);

        /// <summary>Host only: the faction's morale 0..100 (the nation layer shows it as STABILITY). False on a client or for an unknown faction.</summary>
        bool TryGetMorale(int factionId, out float morale);

        /// <summary>Host only: adds to the faction's morale, clamped to 0..100.</summary>
        void AwardMorale(int factionId, float delta);
    }
}
