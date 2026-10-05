using BoscaliSummer.Modules.Support.Domain.Cyber;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal sealed partial class SupportManager
    {
        /// <summary>One HOP, BURN or DROP for a transport-authenticated player (host only; the desk judges it).</summary>
        internal CyberResult RunCyberVerb(Player player, CyberVerb verb, int target) =>
            cyber != null ? cyber.Verb(player, verb, target) : new CyberResult(CyberOutcome.Unavailable);
    }
}
