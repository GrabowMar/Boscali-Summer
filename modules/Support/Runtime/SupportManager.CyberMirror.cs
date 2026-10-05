using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The client side of CYBER: the mirror of the local player's own faction and the three verbs the NET page sends. CYBER request ids share the SPACE
    /// request counter, so they can never collide with CALLS ids; the verdicts arrive through <see cref="SpaceReplied"/> like every SPACE reply.
    /// </summary>
    internal sealed partial class SupportManager
    {
        private readonly CyberMirror cyberMirror = new CyberMirror();
        private bool cyberWanted;
        private float nextCyberSync;

        /// <summary>The local player's faction CYBER view as the host last told it (own faction only).</summary>
        internal CyberMirror CyberMirror => cyberMirror;

        internal void ReceiveCyberState(CyberStateData data)
        {
            if (data == null || data.Protocol != SupportNet.ProtocolVersion) return;
            cyberMirror.Apply(data, SupportNet.ProtocolVersion, MissionNow());
        }

        /// <summary>The NET page is on screen: keep asking the host for a state until one arrives (and again after a lost link).</summary>
        internal void SetCyberWanted(bool wanted)
        {
            if (wanted && !cyberWanted) nextCyberSync = 0f;
            cyberWanted = wanted;
        }

        private void UpdateCyberMirror()
        {
            if (network == null || !cyberWanted || cyberMirror.Known) return;
            float t = Time.unscaledTime;
            if (t < nextCyberSync) return;
            nextCyberSync = t + 2f;
            network.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion, SpaceCommandKind.CyberSync, 0));
        }

        /// <summary>HOP to a visible node (starts an intrusion, or goes deeper from a node you hold). Returns the request id (0 when not sent).</summary>
        internal int CyberHop(int nodeId) => SendCyber(SpaceCommandKind.CyberHop, nodeId);

        /// <summary>BURN a held node: release it and post its package on the TASKED board.</summary>
        internal int CyberBurn(int nodeId) => SendCyber(SpaceCommandKind.CyberBurn, nodeId);

        /// <summary>DROP one held node, or the whole intrusion when <paramref name="nodeId"/> is 0.</summary>
        internal int CyberDrop(int nodeId) => SendCyber(SpaceCommandKind.CyberDrop, nodeId);

        private int SendCyber(SpaceCommandKind kind, int nodeId) => nodeId < 0 ? 0 : SendSpace(kind, nodeId, null);
    }
}
