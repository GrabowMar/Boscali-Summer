using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The seams OPERATIONS reads and writes on CYBER: the data center as the vulnerable anchor, the launcher site, SAM C2 targets, the SAM NET FAIL effect and the event feed (hop work, counter-trace).</summary>
    internal sealed partial class CyberService
    {
        private int opsEffectSerial;

        /// <summary>Raised after every CYBER event of a faction: (owner, event, the victim faction key of the node a hop took, 0 when it is not a data center node).</summary>
        internal event Action<FactionHQ, CyberEvent, int> Observed;

        /// <summary>True while any of the faction's data centers stands (Live or Damaged): the vulnerable anchor of every CYBER operation.</summary>
        internal bool DataCenterUp(FactionHQ owner) => TryDesk(owner, out CyberDesk desk) && desk.Anchors.DataCenterUp;

        /// <summary>Where the faction's first standing data center is, and the airbase it belongs to (the ASAT launcher is placed beside it).</summary>
        internal bool TryDataCenterSpot(FactionHQ owner, out GlobalPosition spot, out Airbase parent)
        {
            spot = default; parent = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionCyber f)) return false;
            foreach (AnchorSlot slot in f.Slots)
            {
                if (slot.Kind != AnchorKind.DataCenter || slot.Unit == null || f.Anchors.Health(slot.Kind, slot.Index) == AnchorHealth.Down) continue;
                spot = slot.Unit.transform.position.ToGlobalPosition();
                parent = slot.Parent;
                return true;
            }
            return false;
        }

        /// <summary>A SAM C2 node the faction can see right now, as an operation target (id, frozen position, victim). Unknown, hidden and other-kind ids all answer false.</summary>
        internal bool TryNodeTarget(FactionHQ owner, int nodeId, out OpTarget target)
        {
            target = default;
            if (!TryDesk(owner, out CyberDesk desk)) return false;
            foreach (CyberNode n in desk.Visible)
                if (n.Id == nodeId && n.Kind == NodeKind.SamC2) { target = new OpTarget(n.Id, n.X, n.Z, n.Victim); return true; }
            return false;
        }

        /// <summary>An EW truck of the victim faction (its key) stands within <paramref name="metres"/> of the point (it halves SAM NET FAIL). Other factions' trucks never count.</summary>
        internal bool EnemyTruckWithin(int victimKey, float x, float z, float metres)
        {
            if (victimKey == 0) return false;
            var trucks = new List<EwSource>(AnchorRules.MaxTrucks);
            foreach (var pair in factions)
            {
                if (manager.FactionKeyOf(pair.Key) != victimKey) continue;
                pair.Value.Anchors.CopyTrucks(trucks);
                foreach (EwSource t in trucks) if (CyberGraph.InReach(t.X, t.Z, x, z, metres)) return true;
            }
            return false;
        }

        /// <summary>ZERO-DAY: every SAM launcher within 3 km of the node point stops launching for <paramref name="seconds"/>, through the same launch block a held SAM C2 node uses.</summary>
        internal bool AddSamNetFail(FactionHQ owner, in OpTarget target, float seconds)
        {
            if (!TryDesk(owner, out CyberDesk desk) || !OpsRules.Finite(seconds) || seconds <= 0f) return false;
            float now = SupportManager.MissionNow();
            int id = 1000000 + (++opsEffectSerial);
            return desk.Effects.Add(new CyberEffect(EffectKind.SamBlock, EffectSource.Package, id, manager.FactionKeyOf(owner), target.Victim, target.X, target.Z, 3000f, 0u, 1f, now + seconds));
        }

        private int VictimOf(FactionCyber f, CyberEvent e)
        {
            if (e.Kind != CyberEventKind.NodeHeld || e.Node != NodeKind.DataCenter) return 0;
            foreach (CyberNode n in f.Desk.Visible) if (n.Id == e.NodeId) return n.Victim;
            return 0;
        }
    }
}
