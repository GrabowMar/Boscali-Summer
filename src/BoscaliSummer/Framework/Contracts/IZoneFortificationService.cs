using NuclearOption.Networking;

namespace BoscaliSummer.Framework.Contracts
{
    internal interface IZoneFortificationService
    {
        /// <summary>
        /// Reinforces the garrison of a zone the requester's faction controls.
        /// Returns <c>true</c> only when every precondition for placing defenders has been
        /// verified, so a caller may charge for the request. Implementations must not tear
        /// down the existing garrison before that point: a fortification that cannot complete
        /// has to leave the zone exactly as it found it.
        /// </summary>
        bool TryFortify(Airbase airbase, FactionHQ owner, Player requester);
    }
}
