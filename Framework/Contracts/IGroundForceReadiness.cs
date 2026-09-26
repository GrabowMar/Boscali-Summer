using NuclearOption.Networking;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Read-only ground-force readiness earned by the SPEC OPS detachment: one plus the best
    /// formed team's rank. Urban Combat consults it when it establishes a fast-rope encampment;
    /// absent or unknown, it reads as the untrained value of one.
    /// The owner is the faction the action executes for, so a host answers for the requester
    /// rather than the local player.
    /// </summary>
    internal interface IGroundForceReadiness
    {
        /// <summary>Encampments one fast-rope insertion establishes, 1..4.</summary>
        int InsertionCamps(FactionHQ owner);
    }
}
