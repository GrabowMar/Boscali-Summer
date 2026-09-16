namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    /// <summary>
    /// The deterministic chain map for follow-on contracts. A completed contract may seed one
    /// immediate successor on the same faction board; the player's action creates the next
    /// task. Exhausted, cancelled and expired contracts never seed anything, and the map is a
    /// pure function so the host decision is reproducible offline.
    /// </summary>
    internal static class OperationChains
    {
        /// <summary>Hard ceiling on a chain: depth 0 is an original offer, depth 2 may not chain again.</summary>
        internal const int MaximumDepth = 2;

        /// <summary>Null means the family is exhausted and leaves no follow-on.</summary>
        internal static OperationKind? FollowOn(OperationKind completed) => completed switch
        {
            OperationKind.Capture => OperationKind.Defend,
            OperationKind.Recon or OperationKind.SortieReport => OperationKind.Interdict,
            OperationKind.DamageAssessment => OperationKind.Interdict,
            OperationKind.SupplyEscort => OperationKind.SupplyInterdict,
            OperationKind.Jam or OperationKind.ElectronicWarfare => OperationKind.Intercept,
            _ => null
        };

        internal static bool CanSeed(OperationKind completed, int chainDepth) =>
            chainDepth < MaximumDepth && FollowOn(completed) != null;
    }
}
