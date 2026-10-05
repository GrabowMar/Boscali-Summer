using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal sealed partial class SupportManager
    {
        /// <summary>One RAISE, ORDER, MISSION or DIVERT for a transport-authenticated player (host only; the desk judges it).</summary>
        internal SofResult RunSofVerb(Player player, in SpaceCommand command)
        {
            if (sof == null) return new SofResult(SofOutcome.Unavailable);
            SofResult result = sof.Verb(player, command);
            if (result.Ok && player != null) overlord?.NoteHuman(player.HQ, Domain.Ops.WatchDomain.Sof);
            return result;
        }
    }
}
