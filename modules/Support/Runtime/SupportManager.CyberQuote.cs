using BoscaliSummer.Core.Game;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal sealed partial class SupportManager
    {
        /// <summary>
        /// EXPLOIT on FLARE BARRAGE and EMP (spec §1.4): the faction holds a node. A CALL quote has no aim yet, so the spec's "within 20 km of the aim"
        /// reads here as "holds any node" (host: the desk; client: the faction mirror), and the chip names exactly what the host charges.
        /// </summary>
        internal bool CyberExploit(FactionHQ owner)
        {
            if (owner == null) return false;
            if (GameAccess.IsServer()) return cyber != null && cyber.HoldsAnyNode(owner);
            return cyberMirror.HoldsAnyNode && GameManager.GetLocalPlayer<Player>(out Player local) && local != null && ReferenceEquals(local.HQ, owner);
        }
    }
}
