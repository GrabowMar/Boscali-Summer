using NuclearOption.Networking;

namespace BoscaliSummer.Framework.Contracts
{
    internal interface IZoneFortificationService
    {
        /// <summary>
        /// Reinforces occupied civilian shells in a zone the requester's faction controls
        /// with hidden vanilla defense proxies.
        /// </summary>
        bool TryFortify(Airbase airbase, FactionHQ owner, Player requester);
    }
}
