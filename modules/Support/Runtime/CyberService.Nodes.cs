using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The real-world node refresh (spec §1.2) and the host verbs (spec §1.3) of <see cref="CyberService"/>.</summary>
    internal sealed partial class CyberService
    {
        private const float NodeRefreshSeconds = 10f;
        private readonly List<Unit> enemyUplinks = new List<Unit>(8), enemyCenters = new List<Unit>(4);

        private sealed partial class FactionCyber
        {
            public readonly List<NodeObservation> Observed = new List<NodeObservation>(64);
            public readonly CyberObservations Observations = new CyberObservations();
            public float NextNodeRefresh;
        }

        /// <summary>Every 10 s: rebuild the faction's node list from the real enemy world and hand it to the desk (fog, caps, lost nodes).</summary>
        partial void TickNodes(FactionCyber f, float now)
        {
            if (now < f.NextNodeRefresh) return;
            f.NextNodeRefresh = now + NodeRefreshSeconds;
            CollectEnemyAnchors(f.Owner);
            f.Observations.Build(f.Owner, manager.FactionKeyOf, IsAnchor, enemyUplinks, enemyCenters, f.Observed);
            f.Desk.Refresh(f.Observed);
        }

        /// <summary>The mod's own spawned anchors are not generic RADAR / SAM nodes: they become UPLINK and DATA CENTER nodes (or nothing). The SOF camp and FOB vehicles are the mod's too, never a node.</summary>
        private bool IsAnchor(Unit unit) => spawner.Owns(unit) || (space != null && space.Spawner.Owns(unit)) || (manager != null && manager.Sof != null && manager.Sof.IsCampUnit(unit));

        private void CollectEnemyAnchors(FactionHQ viewer)
        {
            enemyUplinks.Clear(); enemyCenters.Clear();
            foreach (var pair in factions)
            {
                if (pair.Key == viewer) continue;
                foreach (AnchorSlot s in pair.Value.Slots) if (s.Kind == AnchorKind.DataCenter && s.Unit != null) enemyCenters.Add(s.Unit);
            }
            if (space == null) return;
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null || hq == viewer) continue;
                foreach (Unit unit in space.UplinksFor(hq)) if (unit != null) enemyUplinks.Add(unit);
            }
        }

        /// <summary>The faction has at least one intrusion holding a node (the EXPLOIT chip on FLARE BARRAGE and EMP reads it).</summary>
        internal bool HoldsAnyNode(FactionHQ owner)
        {
            if (!TryDesk(owner, out CyberDesk desk)) return false;
            foreach (CyberIntrusion x in desk.Network.Active) if (x.HeldCount > 0) return true;
            return false;
        }

        /// <summary>
        /// HOP, BURN or DROP for one player. Faction and identity come from the transport-authenticated player, never from the message; the desk
        /// answers the same NO TARGET for an unknown, hidden or foreign node id.
        /// </summary>
        internal CyberResult Verb(Player player, CyberVerb verb, int target)
        {
            if (!GameAccess.IsServer() || player == null || player.HQ == null || manager?.Settings == null || !manager.Settings.Enabled.Value ||
                !manager.Settings.CyberEnabled.Value || !factions.TryGetValue(player.HQ, out FactionCyber f)) return new CyberResult(CyberOutcome.Unavailable);
            ulong op = PlayerIdentity.Of(player);
            if (op == PlayerIdentity.None) return new CyberResult(CyberOutcome.Unavailable);
            return verb == CyberVerb.Hop ? f.Desk.Hop(op, target) : verb == CyberVerb.Burn ? f.Desk.Burn(op, target) : f.Desk.Drop(op, target);
        }
    }
}
