namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Host-only read of occupied front positions near an objective.</summary>
    internal interface IFieldworksReadiness
    {
        void CountNear(FactionHQ observer, float x, float z, float radius,
            out int friendlyDefenders, out int observedHostileDefenders, out int suppressedFriendly);
    }
}
