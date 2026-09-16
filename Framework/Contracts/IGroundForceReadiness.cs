using NuclearOption.Networking;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Read-only ground-force readiness bought at the base of operations (OPS › SPEC OPS).
    /// Urban Combat consults it when it reinforces a zone or establishes a fast-rope
    /// encampment; absent or unknown, everything reads as the untrained value of one.
    /// The owner is the faction the action executes for, so a host answers for the requester
    /// rather than the local player.
    /// </summary>
    internal interface IGroundForceReadiness
    {
        /// <summary>Defensive positions one accepted zone fortification occupies, 1..4.</summary>
        int FortificationShells(FactionHQ owner);

        /// <summary>Encampments one fast-rope insertion establishes, 1..4.</summary>
        int InsertionCamps(FactionHQ owner);
    }
}
