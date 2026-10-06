using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    /// <summary>Typed host verdict of a CYBER verb. Wire values; never renumber. Every value has words.</summary>
    internal enum CyberOutcome : byte
    {
        None = 0, Started = 1, HopStarted = 2, Burned = 3, Dropped = 4,
        NoTarget = 5, OutOfReach = 6, NodeBusy = 7, StillHopping = 8, IntrusionCap = 9, HeldCap = 10, TruckDown = 11, TruckLocked = 12,
        LowCredit = 13, Frozen = 14, RateLimited = 15, Unavailable = 16, BoardFull = 17, NoBoard = 18, AlreadyPosted = 19
    }

    /// <summary>The three operator verbs a host command maps onto (HOLD is a state, not a verb).</summary>
    internal enum CyberVerb : byte { Hop = 0, Burn = 1, Drop = 2 }

    internal static class CyberWords
    {
        public const byte MaxOutcome = (byte)CyberOutcome.AlreadyPosted;

        public static string Of(CyberOutcome outcome, int detail = 0)
        {
            switch (outcome)
            {
                case CyberOutcome.None: return "";
                case CyberOutcome.Started: return "INTRUSION STARTED — HOPPING";
                case CyberOutcome.HopStarted: return "HOPPING DEEPER";
                case CyberOutcome.Burned: return "NODE BURNED — PACKAGE POSTED TO THE BOARD";
                case CyberOutcome.Dropped: return "INTRUSION DROPPED";
                case CyberOutcome.NoTarget: return "NEGATIVE: NO SUCH NODE — PICK ONE FROM THE MAP";
                case CyberOutcome.OutOfReach: return "NEGATIVE: OUT OF REACH — HOP FROM A HELD NODE OR MOVE THE TRUCK IN";
                case CyberOutcome.NodeBusy: return "NEGATIVE: NODE ALREADY HELD — PICK ANOTHER";
                case CyberOutcome.StillHopping: return "NEGATIVE: STILL HOPPING — WAIT FOR THE NODE";
                case CyberOutcome.IntrusionCap: return "NEGATIVE: INTRUSION CAP — WAIT FOR ONE TO END";
                case CyberOutcome.HeldCap: return "NEGATIVE: " + CyberRules.MaxHeldPerFaction + " NODES HELD — BURN OR DROP ONE";
                case CyberOutcome.TruckDown: return "NEGATIVE: EW TRUCK DOWN — NO REACH UNTIL IT IS RESTORED";
                case CyberOutcome.TruckLocked: return "NEGATIVE: EW TRUCK TRACED — LOCKED " + Math.Max(0, detail) + " S";
                case CyberOutcome.LowCredit: return CallWords.Refusal(CallRefusal.LowCredit, need: Math.Max(0, detail));
                case CyberOutcome.Frozen: return "NEGATIVE: CREDIT FROZEN — STAND BY";
                case CyberOutcome.RateLimited: return "NEGATIVE: RATE LIMITED — SLOW DOWN";
                case CyberOutcome.BoardFull: return "NEGATIVE: BOARD FULL — FIRE A POST OR DROP THE NODE";
                case CyberOutcome.NoBoard: return "NEGATIVE: NO TASKED BOARD — THE SPACE DESK IS NOT UP, DROP THE NODE";
                case CyberOutcome.AlreadyPosted: return "NEGATIVE: ALREADY POSTED — THAT PACKAGE IS ON THE BOARD, CLAIM IT OR DROP THE NODE";
                default: return "NEGATIVE: CYBER OFFLINE — NO EW ASSETS";
            }
        }
    }

    internal readonly struct CyberResult
    {
        public readonly CyberOutcome Outcome;
        public readonly int NodeId, Charged, Detail;
        public CyberResult(CyberOutcome outcome, int nodeId = 0, int charged = 0, int detail = 0)
        { Outcome = outcome; NodeId = nodeId; Charged = charged; Detail = detail; }
        public bool Ok => Outcome == CyberOutcome.Started || Outcome == CyberOutcome.HopStarted || Outcome == CyberOutcome.Burned || Outcome == CyberOutcome.Dropped;
        public string Words => CyberWords.Of(Outcome, Detail);
    }

    internal enum CyberEventKind : byte { HopStarted = 1, NodeHeld = 2, Released = 3, Traced = 4 }

    /// <summary>A real intrusion event: the console, the notices and the effect book all hang off this.</summary>
    internal readonly struct CyberEvent
    {
        public readonly CyberEventKind Kind;
        public readonly ulong Operator;
        public readonly int Intrusion, NodeId;
        public readonly NodeKind Node;
        public readonly IntrusionEnd Reason;
        public CyberEvent(CyberEventKind kind, ulong op, int intrusion, int nodeId, NodeKind node, IntrusionEnd reason)
        { Kind = kind; Operator = op; Intrusion = intrusion; NodeId = nodeId; Node = node; Reason = reason; }
    }

    internal readonly struct UpkeepCharge
    {
        public readonly ulong Operator;
        public readonly int Held, Ticks;
        public UpkeepCharge(ulong op, int held, int ticks) { Operator = op; Held = held; Ticks = ticks; }
    }

    /// <summary>One faction's intrusions: who holds what, the limits, the trace clock. Reach and fog are the desk's job.</summary>
    internal sealed class CyberNetwork
    {
        public const int MaxIntrusions = 3;
        private readonly CyberAnchorSet anchors;
        private readonly List<CyberIntrusion> active = new List<CyberIntrusion>(MaxIntrusions);
        private readonly List<CyberEvent> events = new List<CyberEvent>(16);
        private int nextId;

        public CyberNetwork(CyberAnchorSet anchors) { this.anchors = anchors; }

        public IReadOnlyList<CyberIntrusion> Active => active;

        public CyberIntrusion Of(ulong op)
        {
            for (int i = 0; i < active.Count; i++) if (active[i].Operator == op) return active[i];
            return null;
        }

        /// <summary>Nodes held plus hops in flight, across the faction (the 4-node cap counts both).</summary>
        public int Engaged
        {
            get
            {
                int n = 0;
                for (int i = 0; i < active.Count; i++) n += active[i].HeldCount + (active[i].HopTarget != 0 ? 1 : 0);
                return n;
            }
        }

        public bool IsEngaged(int nodeId)
        {
            for (int i = 0; i < active.Count; i++) if (active[i].HopTarget == nodeId || active[i].Holds(nodeId)) return true;
            return false;
        }

        public bool Holds(int nodeId)
        {
            for (int i = 0; i < active.Count; i++) if (active[i].Holds(nodeId)) return true;
            return false;
        }

        public CyberOutcome CanStart(ulong op, int truck, int humans, float now, out int detail)
        {
            detail = 0;
            if (anchors.Count(AnchorKind.EwTruck) == 0) return CyberOutcome.Unavailable;
            if (anchors.Health(AnchorKind.EwTruck, truck) == AnchorHealth.Down) return CyberOutcome.TruckDown;
            if (anchors.TruckLocked(truck, now)) { detail = (int)Math.Ceiling(anchors.LockUntil(truck) - now); return CyberOutcome.TruckLocked; }
            if (Of(op) != null) return CyberOutcome.NodeBusy;
            if (active.Count >= CyberRules.IntrusionCap(humans) || active.Count >= MaxIntrusions) return CyberOutcome.IntrusionCap;
            if (Engaged >= CyberRules.MaxHeldPerFaction) return CyberOutcome.HeldCap;
            return CyberOutcome.None;
        }

        public CyberOutcome Start(ulong op, int truck, in CyberNode node, bool exploit, float now, int humans, out CyberIntrusion made, out int detail)
        {
            made = null;
            CyberOutcome refusal = CanStart(op, truck, humans, now, out detail);
            if (refusal != CyberOutcome.None) return refusal;
            if (IsEngaged(node.Id)) return CyberOutcome.NodeBusy;
            var intrusion = new CyberIntrusion(++nextId, op, truck, now);
            if (!intrusion.TryBeginHop(node.Id, node.Kind, CyberRules.HopSeconds(node.Kind, exploit), now)) return CyberOutcome.Unavailable;
            active.Add(intrusion);
            made = intrusion;
            events.Add(new CyberEvent(CyberEventKind.HopStarted, op, intrusion.Id, node.Id, node.Kind, IntrusionEnd.None));
            return CyberOutcome.Started;
        }

        public CyberOutcome Deeper(ulong op, in CyberNode node, bool exploit, float now)
        {
            CyberIntrusion intrusion = Of(op);
            if (intrusion == null) return CyberOutcome.NoTarget;
            if (intrusion.HopTarget != 0) return CyberOutcome.StillHopping;
            if (IsEngaged(node.Id)) return CyberOutcome.NodeBusy;
            if (intrusion.HeldCount >= CyberRules.MaxHeldPerIntrusion || Engaged >= CyberRules.MaxHeldPerFaction) return CyberOutcome.HeldCap;
            if (!intrusion.TryBeginHop(node.Id, node.Kind, CyberRules.HopSeconds(node.Kind, exploit), now)) return CyberOutcome.Unavailable;
            events.Add(new CyberEvent(CyberEventKind.HopStarted, op, intrusion.Id, node.Id, node.Kind, IntrusionEnd.None));
            return CyberOutcome.HopStarted;
        }

        /// <summary>Releases one held node (BURN or DROP). False when this operator does not hold it.</summary>
        public bool ReleaseNode(ulong op, int nodeId, IntrusionEnd why, float now)
        {
            CyberIntrusion intrusion = Of(op);
            if (intrusion == null || !intrusion.TryHeld(nodeId, out HeldNode node)) return false;
            intrusion.Release(nodeId, why);
            events.Add(new CyberEvent(CyberEventKind.Released, op, intrusion.Id, nodeId, node.Kind, why));
            Sweep();
            return true;
        }

        /// <summary>Ends the operator's whole intrusion; every held node is released.</summary>
        public bool End(ulong op, IntrusionEnd why)
        {
            CyberIntrusion intrusion = Of(op);
            if (intrusion == null) return false;
            EndIntrusion(intrusion, why);
            Sweep();
            return true;
        }

        /// <summary>A real node vanished (its unit died): drop it from whoever held or was hopping to it.</summary>
        public void Lose(int nodeId)
        {
            for (int i = 0; i < active.Count; i++)
            {
                CyberIntrusion x = active[i];
                if (x.HopTarget == nodeId) x.CancelHop();
                if (x.TryHeld(nodeId, out HeldNode node))
                {
                    x.Release(nodeId, IntrusionEnd.NodeLost);
                    events.Add(new CyberEvent(CyberEventKind.Released, x.Operator, x.Id, nodeId, node.Kind, IntrusionEnd.NodeLost));
                }
                // The only hop target was cancelled above and nothing is held: the intrusion has nothing left to do, so it ends (an intrusion with a hop in flight or a held node never reads Idle).
                else if (x.Phase == IntrusionPhase.Idle) x.Finish(IntrusionEnd.NodeLost);
            }
            Sweep();
        }

        public void Advance(float now, float traceFactor, List<UpkeepCharge> upkeep)
        {
            upkeep.Clear();
            for (int i = 0; i < active.Count; i++)
            {
                CyberIntrusion x = active[i];
                if (x.Ended) continue;
                if (anchors.Health(AnchorKind.EwTruck, x.Truck) == AnchorHealth.Down) { EndIntrusion(x, IntrusionEnd.SourceDown); continue; }
                var before = new List<HeldNode>(x.Held);
                IntrusionStep step = x.Advance(now, traceFactor);
                if (step.Traced)
                {
                    foreach (HeldNode h in before) events.Add(new CyberEvent(CyberEventKind.Released, x.Operator, x.Id, h.NodeId, h.Kind, IntrusionEnd.Traced));
                    events.Add(new CyberEvent(CyberEventKind.Traced, x.Operator, x.Id, 0, default, IntrusionEnd.Traced));
                    anchors.Trace(x.Truck, now);
                    continue;
                }
                if (step.Arrived != 0 && x.TryHeld(step.Arrived, out HeldNode arrived))
                    events.Add(new CyberEvent(CyberEventKind.NodeHeld, x.Operator, x.Id, arrived.NodeId, arrived.Kind, IntrusionEnd.None));
                if (step.UpkeepTicks > 0 && x.HeldCount > 0) upkeep.Add(new UpkeepCharge(x.Operator, x.HeldCount, step.UpkeepTicks));
            }
            Sweep();
        }

        public int Drain(List<CyberEvent> into)
        {
            into.Clear();
            into.AddRange(events);
            events.Clear();
            return into.Count;
        }

        public void Clear() { active.Clear(); events.Clear(); }

        private void EndIntrusion(CyberIntrusion x, IntrusionEnd why)
        {
            foreach (HeldNode h in new List<HeldNode>(x.Held))
                events.Add(new CyberEvent(CyberEventKind.Released, x.Operator, x.Id, h.NodeId, h.Kind, why));
            x.Finish(why);
        }

        private void Sweep()
        {
            for (int i = active.Count - 1; i >= 0; i--) if (active[i].Ended) active.RemoveAt(i);
        }
    }

    internal interface ICyberPorts
    {
        float Now { get; }
        int Humans { get; }
        /// <summary>The faction key this desk acts for.</summary>
        int Owner { get; }
        /// <summary>Takes CR from the operator's wallet. None when paid; LowCredit (detail = need) or Frozen otherwise.</summary>
        CyberOutcome TrySpend(ulong op, int cr, out int detail);
        void Refund(ulong op, int cr);
        /// <summary>Posts the BURN package on the TASKED board. None when posted; BoardFull, NoBoard (no SPACE desk) or AlreadyPosted otherwise.</summary>
        CyberOutcome PostPackage(ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort);
    }

    /// <summary>A real unit or airbase the runtime looked at this refresh, with its fog verdict.</summary>
    internal readonly struct NodeObservation
    {
        public readonly NodeSeed Seed;
        public readonly int Victim;
        /// <summary>A fresh real sighting by the faction this refresh (never host truth).</summary>
        public readonly bool Sighted;
        /// <summary>The unit behind it is destroyed or gone.</summary>
        public readonly bool Gone;
        public NodeObservation(NodeSeed seed, int victim, bool sighted, bool gone) { Seed = seed; Victim = victim; Sighted = sighted; Gone = gone; }
    }

    /// <summary>
    /// One faction's CYBER host path: fog, reach, limits, cost, hold effects, BURN packages. Every unknown, hidden or foreign node id answers
    /// the same <see cref="CyberOutcome.NoTarget"/>, so a node id is never an oracle for what the enemy has.
    /// </summary>
    internal sealed class CyberDesk
    {
        private readonly ICyberPorts ports;
        private readonly List<CyberNode> visible = new List<CyberNode>(CyberGraph.MaxNodes);
        private readonly List<EwSource> trucks = new List<EwSource>(AnchorRules.MaxTrucks);
        private readonly List<CyberNode> scratchHeld = new List<CyberNode>(CyberRules.MaxHeldPerFaction);
        private readonly List<CyberNode> candidates = new List<CyberNode>(NodeIdTable.Capacity);
        private readonly List<UpkeepCharge> upkeep = new List<UpkeepCharge>(MaxIntrusionsHint);
        private readonly List<CyberEvent> drained = new List<CyberEvent>(16);
        private readonly HashSet<int> engagedIds = new HashSet<int>();
        private readonly HashSet<int> seenIds = new HashSet<int>();
        private const int MaxIntrusionsHint = CyberNetwork.MaxIntrusions;
        /// <summary>Where each node id stood at its last fresh sighting (fog: a lingering or held node never moves with host truth).</summary>
        private readonly Frozen[] frozen = new Frozen[NodeIdTable.Capacity + 1];
        private struct Frozen { public bool Set; public float X, Z, Front; }

        public CyberDesk(ICyberPorts ports, CyberAnchorSet anchors)
        {
            this.ports = ports;
            Anchors = anchors;
            Network = new CyberNetwork(anchors);
        }

        public CyberAnchorSet Anchors { get; }
        public CyberNetwork Network { get; }
        public CyberEffectBook Effects { get; } = new CyberEffectBook();
        public NodeIdTable Ids { get; } = new NodeIdTable();
        public NodeReveal Reveal { get; } = new NodeReveal();
        public IReadOnlyList<CyberNode> Visible => visible;
        /// <summary>
        /// Cross-faction trace multiplier (an enemy holding this faction's DATA CENTER node, x1.3). Effects live in the HOLDER's desk, so the runtime
        /// folds every other faction's book into this port; the desk only multiplies. Null reads as 1.
        /// </summary>
        public Func<float, float> EnemyTraceFactor { get; set; }
        /// <summary>Raised once per intrusion event after the desk has applied it (console lines, notices, the mirror's event ring).</summary>
        public event Action<CyberEvent> Happened;

        // ---- Fog ---------------------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the visible node list from this refresh's observations: a sighted node is stamped, a destroyed one is lost, a node is
        /// listed while visible (fresh sighting, held, or 30 s after a release), capped at 16 closest to the front (held nodes always stay).
        /// </summary>
        public void Refresh(IReadOnlyList<NodeObservation> observed)
        {
            float now = ports.Now;
            candidates.Clear();
            engagedIds.Clear();
            foreach (CyberIntrusion x in Network.Active)
            {
                foreach (HeldNode h in x.Held) engagedIds.Add(h.NodeId);
                if (x.HopTarget != 0) engagedIds.Add(x.HopTarget);
            }
            seenIds.Clear();
            for (int i = 0; observed != null && i < observed.Count; i++)
            {
                NodeObservation o = observed[i];
                // An id is handed out only when the node first becomes visible, so ids never reveal how many unsighted nodes exist.
                if (!Ids.TryGet(o.Seed.Kind, o.Seed.Key, out int id))
                {
                    if (!o.Sighted || o.Gone) continue;
                    id = Ids.GetOrAdd(o.Seed.Kind, o.Seed.Key);
                    if (id == 0) continue;
                }
                seenIds.Add(id);
                if (o.Gone) { Network.Lose(id); continue; }
                if (o.Sighted)
                {
                    Reveal.Note(id, now);
                    frozen[id] = new Frozen { Set = true, X = o.Seed.X, Z = o.Seed.Z, Front = o.Seed.FrontDistance };
                }
                if (!Reveal.Visible(id, now, engagedIds.Contains(id))) continue;
                // Position is frozen at the last sighted refresh: an unsighted (lingering or held) node never shows where it is now.
                Frozen at = frozen[id];
                float nx = at.Set ? at.X : o.Seed.X, nz = at.Set ? at.Z : o.Seed.Z, nf = at.Set ? at.Front : o.Seed.FrontDistance;
                candidates.Add(new CyberNode(id, o.Seed.Kind, nx, nz, o.Seed.Kind == NodeKind.Relay ? 0u : o.Seed.Key, nf, o.Victim));
            }
            // A node we hold or are hopping to that is no longer among the real candidates (its unit died, its airbase changed hands) is lost.
            foreach (int id in engagedIds) if (!seenIds.Contains(id)) Network.Lose(id);
            CyberGraph.Select(candidates, engagedIds, CyberGraph.MaxNodes, visible);
            Pump(now);
        }

        private bool TryNode(int id, out CyberNode node)
        {
            for (int i = 0; i < visible.Count; i++) if (visible[i].Id == id) { node = visible[i]; return true; }
            node = default; return false;
        }

        // ---- Verbs ---------------------------------------------------------------------------------

        /// <summary>HOP: starts the operator's intrusion from a truck in reach, or goes deeper from a node it holds. Starting costs CR; deeper is free.</summary>
        public CyberResult Hop(ulong op, int nodeId)
        {
            float now = ports.Now;
            if (Anchors.Count(AnchorKind.EwTruck) == 0) return new CyberResult(CyberOutcome.Unavailable);
            if (!TryNode(nodeId, out CyberNode node)) return new CyberResult(CyberOutcome.NoTarget);
            RefreshTrucks();
            bool exploit = CyberRules.Exploit(node.Kind);
            CyberIntrusion mine = Network.Of(op);
            if (mine != null)
            {
                scratchHeld.Clear();
                foreach (HeldNode h in mine.Held) if (TryNode(h.NodeId, out CyberNode hn)) scratchHeld.Add(hn);
                if (mine.HopTarget != 0) return new CyberResult(CyberOutcome.StillHopping, nodeId);
                if (!CyberGraph.Reachable(node, null, scratchHeld, out _, out int via) || via == 0) return new CyberResult(CyberOutcome.OutOfReach, nodeId);
                CyberOutcome deeper = Network.Deeper(op, node, exploit, now);
                if (deeper != CyberOutcome.HopStarted) return new CyberResult(deeper, nodeId);
                Pump(now);
                return new CyberResult(CyberOutcome.HopStarted, nodeId);
            }
            if (trucks.Count == 0) return new CyberResult(CyberOutcome.TruckDown, nodeId);
            if (!CyberGraph.Reachable(node, trucks, null, out int truck, out _) || truck < 0) return new CyberResult(CyberOutcome.OutOfReach, nodeId);
            CyberOutcome can = Network.CanStart(op, truck, ports.Humans, now, out int detail);
            if (can != CyberOutcome.None) return new CyberResult(can, nodeId, 0, detail);
            if (Network.IsEngaged(nodeId)) return new CyberResult(CyberOutcome.NodeBusy, nodeId);
            int cost = CyberRules.StartCostOf(exploit);
            CyberOutcome paid = ports.TrySpend(op, cost, out detail);
            if (paid != CyberOutcome.None) return new CyberResult(paid, nodeId, 0, detail);
            CyberOutcome started = Network.Start(op, truck, node, exploit, now, ports.Humans, out _, out detail);
            if (started != CyberOutcome.Started)
            {
                ports.Refund(op, cost);
                return new CyberResult(started, nodeId, 0, detail);
            }
            Pump(now);
            return new CyberResult(CyberOutcome.Started, nodeId, cost);
        }

        /// <summary>BURN: releases a held node and posts its package on the board. Refused (nothing released) when the board is full.</summary>
        public CyberResult Burn(ulong op, int nodeId)
        {
            float now = ports.Now;
            CyberIntrusion mine = Network.Of(op);
            if (mine == null || !mine.TryHeld(nodeId, out HeldNode held)) return new CyberResult(CyberOutcome.NoTarget);
            if (!CyberPackages.TryOf(held.Kind, out PackageDef def)) return new CyberResult(CyberOutcome.Unavailable);
            // The node may have left the list (a lingering fog entry); a held node stays listed, so a miss means it is gone.
            if (!TryNode(nodeId, out CyberNode node)) return new CyberResult(CyberOutcome.NoTarget);
            bool exploit = CyberRules.Exploit(node.Kind);
            float effort = Math.Max(1, Math.Min(TaskedBoard.MaxMarks, mine.Hops));
            CyberOutcome posted = ports.PostPackage(op, node, def, exploit, effort);
            if (posted != CyberOutcome.None) return new CyberResult(posted, nodeId);
            Network.ReleaseNode(op, nodeId, IntrusionEnd.Burned, now);
            Pump(now);
            return new CyberResult(CyberOutcome.Burned, nodeId);
        }

        /// <summary>DROP: releases one held node (<paramref name="nodeId"/> &gt; 0) or the whole intrusion (0). No package.</summary>
        public CyberResult Drop(ulong op, int nodeId)
        {
            float now = ports.Now;
            CyberIntrusion mine = Network.Of(op);
            if (mine == null) return new CyberResult(CyberOutcome.NoTarget);
            if (nodeId == 0)
            {
                Network.End(op, IntrusionEnd.Dropped);
            }
            else
            {
                if (!mine.Holds(nodeId)) return new CyberResult(CyberOutcome.NoTarget);
                Network.ReleaseNode(op, nodeId, IntrusionEnd.Dropped, now);
            }
            Pump(now);
            return new CyberResult(CyberOutcome.Dropped, nodeId);
        }

        // ---- Time ----------------------------------------------------------------------------------

        /// <summary>Call at about 1 Hz: trace, arrivals, upkeep, effect expiry.</summary>
        public void Tick()
        {
            float now = ports.Now;
            RefreshTrucks();
            Network.Advance(now, TraceFactor(now), upkeep);
            for (int i = 0; i < upkeep.Count; i++)
            {
                UpkeepCharge c = upkeep[i];
                int per = CyberRules.Upkeep(c.Held, Anchors.DataCenterUp);
                for (int t = 0; t < c.Ticks; t++)
                {
                    if (ports.TrySpend(c.Operator, per, out _) != CyberOutcome.None) { Network.End(c.Operator, IntrusionEnd.Unpaid); break; }
                }
            }
            Effects.Expire(now);
            Pump(now);
        }

        /// <summary>Own data center standing -20 %, an enemy holding its data center node +30 %.</summary>
        public float TraceFactor(float now)
        {
            float f = Anchors.DataCenterUp ? CyberRules.DataCenterTraceFactor : 1f;
            float enemy = EnemyTraceFactor != null ? EnemyTraceFactor(now) : 1f;
            return CyberRules.ClampFactor(f * (float.IsNaN(enemy) || enemy < 1f ? 1f : enemy) * Effects.TraceCut(ports.Owner, now));
        }

        /// <summary>The scene ended: every intrusion ends and every effect clears.</summary>
        public void Reset()
        {
            Network.Clear(); Effects.Clear(); visible.Clear(); trucks.Clear(); Ids.Clear(); Reveal.Clear(); Array.Clear(frozen, 0, frozen.Length);
        }

        private void RefreshTrucks() => Anchors.CopyTrucks(trucks);

        private void Pump(float now)
        {
            Network.Drain(drained);
            for (int i = 0; i < drained.Count; i++)
            {
                CyberEvent e = drained[i];
                if (e.Kind == CyberEventKind.NodeHeld && TryNode(e.NodeId, out CyberNode node))
                    Effects.Add(CyberPackages.Hold(node, ports.Owner));
                else if (e.Kind == CyberEventKind.Released)
                {
                    Effects.ReleaseHold(e.NodeId, now);
                    Reveal.Linger(e.NodeId, now);
                }
                Happened?.Invoke(e);
            }
        }
    }
}
