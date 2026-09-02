namespace BoscaliSummer.Framework.Contracts
{
    internal interface IPlayerEntitlements
    {
        float FuelUseMultiplier(ulong playerId);
        float RewardAllocationMultiplier(ulong playerId, byte rewardType);
        bool HasEntitlement(ulong playerId, string entitlement);
    }

    internal static class SupportEntitlements
    {
        public const string VehicleAirdrop = "support.vehicle-airdrop";
        public const string Artillery = "support.artillery";
        public const string Fortification = "support.fortification";
    }
}
