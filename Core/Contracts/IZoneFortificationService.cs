using NuclearOption.Networking;

namespace BoscaliSummer.Core.Contracts
{
    internal interface IZoneFortificationService
    {

        /// <summary>
        /// Reinforces occupied civilian shells in a zone the requester's faction controls
        /// with hidden vanilla defense proxies. At most <paramref name="shells"/> additional
        /// buildings are occupied, bounded by the zone and theater ceilings; true when at
        /// least one was placed.
        /// </summary>
        bool TryFortify(Airbase airbase, FactionHQ owner, Player requester, int shells);

        /// <summary>The same for a faction's own policy (no requesting pilot; AI factions too): host only, same ceilings.</summary>
        bool TryFortifyFor(Airbase airbase, FactionHQ owner, int shells);
    }
}
