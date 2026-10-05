using BoscaliSummer.Modules.Support.Domain.Cyber;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal sealed partial class SupportManager
    {
        /// <summary>One HOP, BURN or DROP for a transport-authenticated player (host only; the desk judges it).</summary>
        internal CyberResult RunCyberVerb(Player player, CyberVerb verb, int target)
        {
            if (cyber == null) return new CyberResult(CyberOutcome.Unavailable);
            CyberResult result = cyber.Verb(player, verb, target);
            // Only a verb the desk accepted is a human working CYBER: refusals, replays and garbage never keep OVERLORD out.
            if (result.Ok && player != null) overlord?.NoteHuman(player.HQ, Domain.Ops.WatchDomain.Cyber);
            return result;
        }
    }
}
