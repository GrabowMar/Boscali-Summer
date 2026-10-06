using BoscaliSummer.Modules.Support.Domain.Cyber;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The seam the AI doctrine reads on CYBER: the faction's own SAM C2 node nearest the front, as an operation target. Fog holds: it is a node the faction's own desk lists (a real sighting).</summary>
    internal sealed partial class CyberService
    {
        internal bool TryBestSamNode(FactionHQ owner, out int nodeId)
        {
            nodeId = 0;
            if (!TryDesk(owner, out CyberDesk desk)) return false;
            float bestFront = float.PositiveInfinity;
            foreach (CyberNode n in desk.Visible)
                if (n.Kind == NodeKind.SamC2 && (nodeId == 0 || n.FrontDistance < bestFront || (n.FrontDistance == bestFront && n.Id < nodeId))) { nodeId = n.Id; bestFront = n.FrontDistance; }
            return nodeId != 0;
        }
    }
}
