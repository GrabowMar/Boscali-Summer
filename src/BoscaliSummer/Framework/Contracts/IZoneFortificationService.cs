using NuclearOption.Networking;

namespace BoscaliSummer.Framework.Contracts
{
    internal interface IZoneFortificationService
    {
        bool TryFortify(Airbase airbase, FactionHQ owner, Player requester);
    }
}
