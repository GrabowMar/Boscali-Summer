using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The faction-only view of <see cref="CyberService"/>: everything a client may learn is derived here from the host's own desk, never read from a client.</summary>
    internal sealed partial class CyberService
    {
        private sealed partial class FactionCyber
        {
            /// <summary>The last three intrusion events, newest last, each with a faction-wide sequence number (the console and the HUD notices).</summary>
            public readonly List<KeyValuePair<int, CyberEvent>> Ring = new List<KeyValuePair<int, CyberEvent>>(3);
            public int EventSeq;
        }

        partial void RecordEvent(FactionCyber f, CyberEvent e)
        {
            f.Ring.Add(new KeyValuePair<int, CyberEvent>(++f.EventSeq, e));
            while (f.Ring.Count > CyberWire.MaxEvents) f.Ring.RemoveAt(0);
        }

        /// <summary>Fills the viewer's faction view (always the full state). False when the faction has no CYBER (<c>Active</c> stays false).</summary>
        internal bool FillState(Player viewer, CyberStateData into)
        {
            into.Active = false; into.DataCenterUp = false; into.IntrusionCap = 0; into.HeldTotal = 0;
            into.Anchors.Clear(); into.Nodes.Clear(); into.Intrusions.Clear(); into.Events.Clear();
            if (viewer == null || viewer.HQ == null || manager?.Settings == null || !manager.Settings.CyberEnabled.Value ||
                !factions.TryGetValue(viewer.HQ, out FactionCyber f)) return false;
            ulong viewerId = PlayerIdentity.Of(viewer);
            float now = SupportManager.MissionNow();
            CyberDesk desk = f.Desk;
            into.Active = true;
            into.DataCenterUp = f.Anchors.DataCenterUp;
            into.IntrusionCap = (byte)Math.Min(CyberNetwork.MaxIntrusions, CyberRules.IntrusionCap(manager.HumanCount(viewer.HQ)));
            into.HeldTotal = (byte)Math.Min(CyberRules.MaxHeldPerFaction, desk.Network.Engaged);
            foreach (AnchorSlot slot in f.Slots)
            {
                if (into.Anchors.Count >= CyberWire.MaxAnchors || !f.Anchors.TryPosition(slot.Kind, slot.Index, out float x, out float z)) continue;
                AnchorHealth health = f.Anchors.Health(slot.Kind, slot.Index);
                into.Anchors.Add(new CyberAnchorRow { Kind = slot.Kind, Health = health, X = x, Z = z,
                    Rebuild = health == AnchorHealth.Down ? (byte)Mathf.Clamp(Mathf.RoundToInt(slot.Bar.Fraction * 100f), 0, 100) : (byte)0,
                    LockUntil = slot.Kind == AnchorKind.EwTruck && f.Anchors.TruckLocked(slot.Index, now) ? f.Anchors.LockUntil(slot.Index) : 0f });
            }
            foreach (CyberNode n in desk.Visible)
            {
                if (into.Nodes.Count >= CyberWire.MaxNodes) break;
                bool held = desk.Network.Holds(n.Id);
                into.Nodes.Add(new CyberNodeRow { Id = n.Id, Kind = n.Kind, X = n.X, Z = n.Z, Held = held, Hopping = !held && desk.Network.IsEngaged(n.Id), Exploit = CyberRules.Exploit(n.Kind) });
            }
            foreach (CyberIntrusion x in desk.Network.Active)
            {
                if (into.Intrusions.Count >= CyberWire.MaxIntrusions) break;
                bool own = x.Operator == viewerId;
                var held = new int[Math.Min(x.HeldCount, CyberWire.MaxHeld)];
                for (int i = 0; i < held.Length; i++) held[i] = x.Held[i].NodeId;
                // Trace is sent in 2 % steps so a rising bar does not make a new message every poll.
                into.Intrusions.Add(new CyberIntrusionRow { Id = x.Id, Truck = x.Truck, HopTarget = x.HopTarget, Own = own, Phase = x.Phase,
                    Trace = (byte)Mathf.Clamp(Mathf.RoundToInt(x.Trace * 0.5f) * 2, 0, 100), HopSeconds = (byte)Mathf.Clamp(Mathf.RoundToInt(x.HopEndsAt - x.HopStartedAt), 0, 255),
                    HopEndsAt = x.HopTarget != 0 ? x.HopEndsAt : 0f, Held = held, Operator = own ? "" : manager.PlayerLabel(viewer.HQ, x.Operator) });
            }
            for (int i = 0; i < f.Ring.Count && into.Events.Count < CyberWire.MaxEvents; i++)
            {
                CyberEvent e = f.Ring[i].Value;
                into.Events.Add(new CyberEventRow { Seq = f.Ring[i].Key, Kind = e.Kind, Node = e.Node, Reason = e.Reason, NodeId = e.NodeId, Own = e.Operator == viewerId });
            }
            return true;
        }
    }
}
