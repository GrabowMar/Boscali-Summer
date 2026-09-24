namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Host-owned faction mood used when generating a contract offer.</summary>
    internal interface IFactionMoraleView
    {
        bool TryGetContractMultiplier(int factionId, out float multiplier);
    }
}
