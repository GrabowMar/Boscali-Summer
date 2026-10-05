using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The seams the SOF service reads and writes on CYBER (spec 0, the ring: SOF beats CYBER, CYBER lifts SOF). Nothing here changes what CYBER does by itself.</summary>
    internal sealed partial class CyberService
    {
        /// <summary>The mod's own EW truck and data center units, so the SOF ground list does not count them twice.</summary>
        internal bool IsAnchorUnit(Unit unit) => spawner.Owns(unit);

        /// <summary>The faction holds a node within <paramref name="metres"/> of the point (the +15 % CYBER ring boost on a SOF mission).</summary>
        internal bool HoldsNodeNear(FactionHQ owner, float x, float z, float metres)
        {
            if (!TryDesk(owner, out CyberDesk desk)) return false;
            foreach (CyberNode n in desk.Visible)
                if (desk.Network.Holds(n.Id) && CyberGraph.InReach(n.X, n.Z, x, z, metres)) return true;
            return false;
        }

        /// <summary>A SOF NETWORK TAP: the owner's own intrusions trace 30 % slower for <paramref name="seconds"/>. False when the faction has no CYBER desk.</summary>
        internal bool AddTraceCut(FactionHQ owner, int sourceId, float seconds)
        {
            if (!TryDesk(owner, out CyberDesk desk)) return false;
            return desk.Effects.Add(CyberPackages.Tap(manager.FactionKeyOf(owner), sourceId, seconds, SupportManager.MissionNow()));
        }

        /// <summary>How many intrusions other factions are running right now (what a tapped network reveals to the tapper).</summary>
        internal int EnemyIntrusionCount(FactionHQ viewer)
        {
            int n = 0;
            foreach (var pair in factions) if (pair.Key != viewer) n += pair.Value.Desk.Network.Active.Count;
            return n;
        }

        /// <summary>Every other faction's EW truck and data center unit, as SABOTAGE targets (they are revealed only by real sensing; the SOF desk applies the fog).</summary>
        internal void CollectEnemyAnchors(FactionHQ viewer, List<KeyValuePair<AnchorSub, Unit>> into)
        {
            foreach (var pair in factions)
            {
                if (pair.Key == viewer) continue;
                foreach (AnchorSlot s in pair.Value.Slots)
                    if (s.Unit != null) into.Add(new KeyValuePair<AnchorSub, Unit>(s.Kind == AnchorKind.EwTruck ? AnchorSub.EwTruck : AnchorSub.DataCenter, s.Unit));
            }
        }
    }
}
