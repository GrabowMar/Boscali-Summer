using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    // The NETWORK room's INFRASTRUCTURE tab mapping. Inputs, the rail and the ORBIT room are in FrontViews.cs.
    internal static partial class FrontViews
    {
        // ---- INFRASTRUCTURE tab -----------------------------------------------------------------------------------------

        /// <summary>A breach on our network lies this close to a data center of ours: it is that center (the wire carries the kind and our unit's spot, no owner id).</summary>
        private const float BreachMatchMetres = 1500f;

        private static bool Breached(CyberStateData s, float x, float z)
        {
            foreach (CyberBreachRow b in s.Breaches) if (b.Kind == NodeKind.DataCenter && Dist(b.X, b.Z, x, z) <= BreachMatchMetres) return true;
            return false;
        }

        /// <summary>
        /// The INFRASTRUCTURE tab: the three CYBER offers; data centers and EW trucks by host slot (standing, packing or lost from the CYBER mirror, building from the
        /// CYBER queue in the slot the host will build into, which takes over a lost card there); a map ghost for every queued center or truck with a point. A truck
        /// card counts the enemy nodes its reach covers and the trace of the sessions it sources; a center card names the truck it runs (each standing truck takes
        /// the nearest free standing center: the wire carries no pairing).
        /// </summary>
        private static void Infrastructure(FrontRoomView v, in FrontInputs c, CyberStateData s)
        {
            NetworkRoomView n = v.Network;
            n.Centers.Clear(); n.Trucks.Clear(); n.Ghosts.Clear();
            QueuedCounts queued = FrontLadder.Queued(v.Row.Queue);
            n.BuildCenter = Offer(Front.Cyber, ProgrammeId.DataCenter, 0, v.Row, queued, c);
            n.DeployTruck = Offer(Front.Cyber, ProgrammeId.EwTruck, 0, v.Row, queued, c);
            n.ZeroDay = Offer(Front.Cyber, ProgrammeId.ZeroDay, 0, v.Row, queued, c);
            MapFrame f = c.Frame;
            int dcPresent = 0, dcDown = 0, dcTaken = 0, trPresent = 0, trDown = 0, trTaken = 0, dcN = 0, trN = 0;
            for (int i = 0; s != null && i < s.Anchors.Count; i++)
            {
                CyberAnchorRow a = s.Anchors[i];
                bool down = a.Health == AnchorHealth.Down;
                if (a.Kind == AnchorKind.EwTruck) { trPresent |= 1 << trN; if (down) trDown |= 1 << trN; trN++; }
                else { dcPresent |= 1 << dcN; if (down) dcDown |= 1 << dcN; dcN++; }
            }
            for (int i = 0; i < v.Queue.Count; i++)
            {
                FrontQueueRow q = v.Row.Queue[i];
                if (q.Id != ProgrammeId.DataCenter && q.Id != ProgrammeId.EwTruck) continue;
                bool truck = q.Id == ProgrammeId.EwTruck;
                int slot = truck ? FrontLadder.AnchorSlot(trPresent, trDown, ref trTaken, FrontRules.TruckCap) : FrontLadder.AnchorSlot(dcPresent, dcDown, ref dcTaken, FrontRules.DataCenterCap);
                if (slot < 0) continue;
                FrontQueueView qv = v.Queue[i];
                float u = q.HasPoint ? f.U(q.X) : -1f, vv = q.HasPoint ? f.V(q.Z) : -1f;
                string clock = qv.Building ? FrontPanels.Clocked("", qv.BuildLeft) : Pct(qv.Progress);
                if (truck)
                {
                    var t = new InfraTruckView { Ordinal = slot, Name = TruckTag(slot), State = InfraState.Building, U = u, V = vv, Grid = q.HasPoint ? "GRID " + OpsKit.Grid(q.X, q.Z) : "AUTO PLACE",
                        InReachWord = "", TraceWord = "", Row = qv.Row, Progress = qv.Progress, BuildLeft = qv.BuildLeft };
                    n.Trucks.Add(t);
                    if (q.HasPoint) n.Ghosts.Add(new MapGhostView { U = u, V = vv, Kind = (byte)AnchorKind.EwTruck, Label = t.Name + " · " + clock });
                }
                else
                {
                    var d = new InfraCenterView { Slot = slot, Name = "DC-" + (slot + 1), State = InfraState.Building, U = u, V = vv, Place = q.HasPoint ? PlaceOf(q.X, q.Z, c) : "AUTO PLACE",
                        Runs = "", Effect = "", Row = qv.Row, Progress = qv.Progress, BuildLeft = qv.BuildLeft };
                    n.Centers.Add(d);
                    if (q.HasPoint) n.Ghosts.Add(new MapGhostView { U = u, V = vv, Kind = (byte)AnchorKind.DataCenter, Label = d.Name + " · " + d.Place + " · " + clock });
                }
            }
            dcN = trN = 0;
            string effect = "CUTS YOUR TRACE ×" + CyberRules.DataCenterTraceFactor.ToString("0.0#", NetFmt);
            for (int i = 0; s != null && i < s.Anchors.Count; i++)
            {
                CyberAnchorRow a = s.Anchors[i];
                bool down = a.Health == AnchorHealth.Down;
                InfraState st = down ? InfraState.Lost : a.Packing ? InfraState.Packing : a.Health == AnchorHealth.Damaged ? InfraState.Damaged : InfraState.Up;
                if (a.Kind == AnchorKind.DataCenter)
                {
                    int slot = dcN++;
                    if ((dcTaken & (1 << slot)) != 0) continue;
                    n.Centers.Add(new InfraCenterView { Slot = slot, Name = "DC-" + (slot + 1), State = st, U = f.U(a.X), V = f.V(a.Z), Place = PlaceOf(a.X, a.Z, c), Runs = down ? "" : "IDLE",
                        Effect = down ? "" : effect, Breached = !down && Breached(s, a.X, a.Z) });
                    continue;
                }
                int ord = trN++;
                if ((trTaken & (1 << ord)) != 0) continue;
                float reach = down || a.Packing ? 0f : AnchorRules.Reach(a.Health);
                int inReach = 0;
                foreach (CyberNodeRow nd in s.Nodes) if (reach > 0f && CyberGraph.InReach(a.X, a.Z, nd.X, nd.Z, reach)) inReach++;
                int trace = -1;
                bool mine = false;
                foreach (CyberIntrusionRow x in s.Intrusions) if (x.Truck == ord && x.Trace > trace) { trace = x.Trace; mine = x.Own; }
                n.Trucks.Add(new InfraTruckView
                {
                    Ordinal = ord, Name = TruckTag(ord), State = st, U = f.U(a.X), V = f.V(a.Z), Grid = "GRID " + OpsKit.Grid(a.X, a.Z), InReach = inReach,
                    InReachWord = reach <= 0f ? "OFFLINE · NO REACH" : inReach + " ENEMY NODE" + (inReach == 1 ? "" : "S") + " IN REACH · " + Km(reach) + " KM",
                    Trace = Math.Max(0, trace) / 100f, TraceWord = trace < 0 ? "NO SESSION ON IT" : "TRACE " + trace + " % · " + (mine ? "YOUR SESSION" : "ALLY SESSION"),
                    LockLeft = a.LockUntil > c.Now ? (int)Math.Ceiling(a.LockUntil - c.Now) : 0
                });
            }
            n.Centers.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            n.Trucks.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
            // Each standing truck runs off the nearest standing center not yet taken; the counts for the section heads.
            n.CentersUp = n.TrucksUp = 0;
            int pairedMask = 0;
            for (int t = 0; t < n.Trucks.Count; t++)
            {
                InfraTruckView tv = n.Trucks[t];
                if (tv.State == InfraState.Building || tv.State == InfraState.Lost) continue;
                n.TrucksUp++;
                int best = -1;
                float bestD = float.MaxValue;
                for (int d = 0; d < n.Centers.Count; d++)
                {
                    InfraCenterView cv = n.Centers[d];
                    if ((pairedMask & (1 << d)) != 0 || (cv.State != InfraState.Up && cv.State != InfraState.Damaged)) continue;
                    float du = (cv.U - tv.U) * f.W, dz = (cv.V - tv.V) * f.H, dd = du * du + dz * dz;
                    if (dd < bestD) { bestD = dd; best = d; }
                }
                if (best < 0) continue;
                pairedMask |= 1 << best;
                InfraCenterView pc = n.Centers[best];
                pc.Runs = "RUNS " + tv.Name;
                n.Centers[best] = pc;
            }
            foreach (InfraCenterView cv in n.Centers) if (cv.State == InfraState.Up || cv.State == InfraState.Damaged) n.CentersUp++;
        }

        private static int TruckAnchor(List<NetAnchorView> anchors, int truckOrdinal)
        {
            int seen = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Kind != AnchorKind.EwTruck) continue;
                if (seen++ == truckOrdinal) return i;
            }
            return -1;
        }

        /// <summary>The operator whose intrusion holds the node, upper-case ("" when the mirror does not say).</summary>
        private static string HolderOf(CyberStateData s, int nodeId)
        {
            foreach (CyberIntrusionRow x in s.Intrusions)
                if (x.Held != null && Array.IndexOf(x.Held, nodeId) >= 0) return (x.Operator ?? "").ToUpperInvariant();
            return "";
        }

        private static bool TryNode(CyberStateData s, int id, out CyberNodeRow row)
        {
            foreach (CyberNodeRow n in s.Nodes) if (n.Id == id) { row = n; return true; }
            row = default; return false;
        }
    }
}
