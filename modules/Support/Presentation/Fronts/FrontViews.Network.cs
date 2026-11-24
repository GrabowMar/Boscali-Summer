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
    // The NETWORK room mapping (cyber front). Inputs, the rail and the ORBIT room are in FrontViews.cs.
    internal static partial class FrontViews
    {
        // ---- NETWORK --------------------------------------------------------------------------------------------------

        private static readonly IFormatProvider NetFmt = System.Globalization.CultureInfo.InvariantCulture;
        private const int StaleAfterSeconds = 20; // a running session's trace moves in 2 % steps, so the host sends at least every few seconds while it climbs

        // Fixed flavour words for the hostname-like handles; the word is picked by the node id, so it never changes and never claims anything.
        private static readonly string[] HostWords = { "vesper", "cobalt", "nova", "tundra", "ember", "quartz", "sable", "harbor", "falcon", "onyx", "lumen", "basalt", "cinder", "mistral", "zenith", "argon" };

        private static string NodeTag(NodeKind kind, int id) => CyberNetWords.Code(kind) + "-" + id.ToString("00", NetFmt);

        private static string NodeLabel(in CyberNodeRow n) => NodeTag(n.Kind, n.Id);

        /// <summary>The hostname-like handle of a node: "rdr-04.vesper".</summary>
        private static string NodeHandle(NodeKind kind, int id) => NodeTag(kind, id).ToLowerInvariant() + "." + HostWords[(id & int.MaxValue) % HostWords.Length];

        private static string TruckTag(int ordinal) => "EW-" + (ordinal + 1);

        private static string Km(float metres) => (metres / 1000f).ToString("0.#", NetFmt);

        /// <summary>The build that gives the room its reach: a data center first, then an EW truck within 20 km of it.</summary>
        private static string NoTruck(bool dataCenter) => dataCenter ? "DEPLOY AN EW TRUCK ON INFRASTRUCTURE" : "BUILD A DATA CENTER, THEN DEPLOY AN EW TRUCK";

        /// <summary>The SHADOW room's meta and warning with no camp standing (the room keys its hint on it).</summary>
        public const string NoCamp = "BUILD A CAMP";

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string Who(string owner) => owner == "ALLY" ? "AN ALLY" : owner + " (ALLY)";

        /// <summary>What holding a node does, read from the table the host applies (<see cref="CyberPackages.Hold"/>).</summary>
        private static string HoldWords(NodeKind kind)
        {
            CyberEffect e = CyberPackages.Hold(new CyberNode(0, kind, 0f, 0f, 0u, 0f, 0), 0);
            switch (e.Kind)
            {
                case EffectKind.RadarJam: return "JAMS THAT RADAR · ITS RANGE -" + Mathf.RoundToInt((1f - e.Factor) * 100f) + " % WHILE HELD";
                case EffectKind.SamBlock: return "SAMS WITHIN " + Km(e.Radius) + " KM CANNOT LAUNCH WHILE HELD";
                case EffectKind.RelaySilence: return "SIGHTINGS WITHIN " + Km(e.Radius) + " KM ARE NOT SHARED WHILE HELD";
                case EffectKind.BirdJam: return "ENEMY SPACE COOLDOWN +" + Mathf.RoundToInt((e.Factor - 1f) * 100f) + " %, OPTICAL -" + Mathf.RoundToInt((1f - CyberEffectBook.BirdOpticalFactor) * 100f) + " % WHILE HELD";
                default: return "ENEMY INTRUSIONS TRACE +" + Mathf.RoundToInt((e.Factor - 1f) * 100f) + " % FASTER WHILE HELD";
            }
        }

        /// <summary>BURN in words: the package the host posts (label and payoff from <see cref="CyberPackages"/>) and what happens to the session.</summary>
        private static string BurnWords(NodeKind kind, bool exploit, bool lastNode)
        {
            if (!CyberPackages.TryOf(kind, out PackageDef def)) return "NO STRIKE PACKAGE FOR THIS NODE";
            return "POSTS " + def.Label + " · " + def.Payoff(exploit) + " · " + (lastNode ? "ENDS YOUR SESSION" : "RELEASES THE NODE");
        }

        /// <summary>The HOP quote the host charges: starting an intrusion costs <see cref="CyberRules.StartCostOf"/>, going deeper is free; the hop time is <see cref="CyberRules.HopSeconds"/>.</summary>
        private static string HopWords(NodeKind kind, bool starts, bool exploit, int heldNow, bool dataCenterUp, out int cost)
        {
            cost = starts ? CyberRules.StartCostOf(exploit) : 0;
            float rate = (CyberRules.HopTraceRate + CyberRules.HeldTraceRate * heldNow) * (dataCenterUp ? CyberRules.DataCenterTraceFactor : 1f);
            return (starts ? "START " + cost + " AP" : "DEEPER · FREE") + " · " + CyberRules.HopSeconds(kind, exploit).ToString("0.#", NetFmt) + " S HOP · TRACE ~+" + rate.ToString("0.#", NetFmt) + " %/S" + (exploit ? " · EXPLOIT" : "");
        }

        /// <summary>The nearest source the operator can hop from (own truck, or own held node once a session runs), its distance and its range, in metres.</summary>
        private static bool Nearest(List<EwSource> trucks, List<CyberNode> mineNodes, bool have, float x, float z, int skipId, out string src, out float dist, out float range)
        {
            src = ""; dist = float.MaxValue; range = 0f;
            if (have)
            {
                for (int i = 0; i < mineNodes.Count; i++)
                {
                    if (mineNodes[i].Id == skipId) continue;
                    float d = Dist(mineNodes[i].X, mineNodes[i].Z, x, z);
                    if (d < dist) { dist = d; src = NodeTag(mineNodes[i].Kind, mineNodes[i].Id); range = CyberGraph.ChainReach; }
                }
            }
            else
            {
                for (int i = 0; i < trucks.Count; i++)
                {
                    float d = Dist(trucks[i].X, trucks[i].Z, x, z);
                    if (d < dist) { dist = d; src = TruckTag(trucks[i].Index); range = trucks[i].Reach; }
                }
            }
            return src.Length > 0;
        }

        /// <summary>The intrusion that holds or is hopping a node, with the node it came in from: the previous node of its chain, or its truck.</summary>
        private static bool Chain(CyberStateData s, int nodeId, out CyberIntrusionRow row, out int fromNode, out int truck)
        {
            row = default; fromNode = -1; truck = -1;
            foreach (CyberIntrusionRow x in s.Intrusions)
            {
                int at = x.Held == null ? -1 : Array.IndexOf(x.Held, nodeId);
                if (at < 0 && !(x.Phase == IntrusionPhase.Hopping && x.HopTarget == nodeId)) continue;
                int count = x.Held == null ? 0 : x.Held.Length, prev = at >= 0 ? at - 1 : count - 1;
                if (prev >= 0) fromNode = x.Held[prev]; else truck = x.Truck;
                row = x;
                return true;
            }
            return false;
        }

        /// <summary>The route of a chain up to a node: "EW-1 &gt; RDR-01 &gt; SAM-02".</summary>
        private static string PathOf(CyberStateData s, in CyberIntrusionRow x, int nodeId)
        {
            string p = TruckTag(x.Truck);
            if (x.Held != null)
                for (int i = 0; i < x.Held.Length; i++)
                {
                    p += " > " + (TryNode(s, x.Held[i], out CyberNodeRow hn) ? NodeTag(hn.Kind, hn.Id) : "#" + x.Held[i]);
                    if (x.Held[i] == nodeId) return p;
                }
            return p + " > " + (TryNode(s, nodeId, out CyberNodeRow tn) ? NodeTag(tn.Kind, tn.Id) : "#" + nodeId);
        }

        private static byte HopPercent(CyberStateData s, int nodeId, float now)
        {
            foreach (CyberIntrusionRow x in s.Intrusions)
                if (x.Phase == IntrusionPhase.Hopping && x.HopTarget == nodeId) return (byte)Mathf.RoundToInt(CyberNetWords.HopProgress(x, now) * 100f);
            return 0;
        }

        /// <summary>The operator whose intrusion is hopping the node, upper-case ("YOU" for the local one, "" when nobody).</summary>
        private static string HopperOf(CyberStateData s, int nodeId)
        {
            foreach (CyberIntrusionRow x in s.Intrusions)
                if (x.Phase == IntrusionPhase.Hopping && x.HopTarget == nodeId) return x.Own ? "YOU" : (x.Operator ?? "").ToUpperInvariant();
            return "";
        }

        /// <summary>The NETWORK room from the CYBER mirror (<paramref name="s"/>, null or unknown before the host speaks) and the CYBER front row in <paramref name="v"/>.</summary>
        public static void Network(FrontRoomView v, in FrontInputs c, CyberStateData s, bool known, int selected)
        {
            NetworkRoomView n = v.Network;
            MapFrame frame = c.Frame;
            float now = c.Now;
            n.Nodes.Clear(); n.Anchors.Clear();
            Infrastructure(v, c, known ? s : null);
            n.MapKmW = frame.W / 1000f; n.MapKmH = frame.H / 1000f;
            bool active = known && s != null && s.Active;
            n.Active = active;
            n.Dossier = default;
            n.HasIntrusion = false; n.Trace = 0; n.CanHop = n.CanBurn = n.CanDrop = false;
            n.HeldTotal = 0; n.IntrusionCap = 0; n.Sessions = 0; n.YourHeld = 0; n.Hopping = false; n.HopLeft = 0;
            n.EstTraceSeconds = -1; n.Stale = false; n.AgeSeconds = 0; n.Operator = "";
            n.ThreatWord = known ? "CALM" : "NO LINK"; n.ThreatNote = "";
            n.Breaches = 0; n.CanPurge = false; n.PurgeLabel = "PURGE";
            n.Next = ""; n.NextTone = AvState.Caution;
            n.SelectedId = -1;
            if (!active)
            {
                n.Meta = known ? "NO EW ASSET STANDING" : "WAITING FOR THE HOST";
                n.Next = known ? NoTruck(false) : "WAITING FOR THE HOST CYBER STATE · NOTHING TO SHOW YET";
                n.NextTone = known ? AvState.Caution : AvState.Inert;
                n.Dossier.Next = n.Next; n.Dossier.NextTone = n.NextTone;
                return;
            }
            n.AgeSeconds = Math.Max(0, (int)(now - s.Now));
            var trucks = new List<EwSource>(4);
            var lockUntil = new List<float>(4); // by truck ordinal
            int truckOrdinal = 0, centers = 0;
            bool locked = false, anyDown = false, anyTruck = false, packing = false;
            foreach (CyberAnchorRow a in s.Anchors)
            {
                bool truck = a.Kind == AnchorKind.EwTruck;
                int ordinal = truck ? truckOrdinal++ : centers++; // the host sends its slots in (kind, index) order: the ordinal of a kind is its slot
                float reach = truck ? AnchorRules.Reach(a.Health) : 0f;
                if (truck && a.Health != AnchorHealth.Down && !a.Packing) trucks.Add(new EwSource(ordinal, a.X, a.Z, reach)); // a packing truck is offline (the host's rule)
                if (truck) { lockUntil.Add(a.LockUntil); anyTruck = true; packing |= a.Packing; }
                bool lockedNow = a.LockUntil > now;
                locked |= lockedNow; anyDown |= a.Health == AnchorHealth.Down;
                n.Anchors.Add(new NetAnchorView
                {
                    Kind = a.Kind, Health = a.Health, Packing = a.Packing, X = frame.U(a.X), Y = frame.V(a.Z), Reach = a.Packing ? 0f : reach / frame.W,
                    LockLeft = truck && lockedNow ? (int)Math.Ceiling(a.LockUntil - now) : 0,
                    Label = (truck ? "EW-" : "DC-") + (ordinal + 1), Slot = ordinal, Breached = !truck && a.Health != AnchorHealth.Down && Breached(s, a.X, a.Z)
                });
            }

            CyberIntrusionRow mine = default;
            bool have = false;
            foreach (CyberIntrusionRow x in s.Intrusions) if (x.Own) { mine = x; have = true; break; }
            n.HasIntrusion = have;
            n.HeldTotal = s.HeldTotal; n.IntrusionCap = s.IntrusionCap; n.Sessions = s.Intrusions.Count;
            int others = s.Intrusions.Count - (have ? 1 : 0);
            int myHeld = have && mine.Held != null ? mine.Held.Length : 0;
            n.YourHeld = myHeld;
            n.Hopping = have && mine.Phase == IntrusionPhase.Hopping;
            if (have) n.Operator = string.IsNullOrEmpty(mine.Operator) ? "" : mine.Operator.ToUpperInvariant();
            var mineNodes = new List<CyberNode>(4); // the operator's own chain: the only thing a deeper hop starts from
            for (int i = 0; i < myHeld; i++)
                if (TryNode(s, mine.Held[i], out CyberNodeRow chainRow)) mineNodes.Add(new CyberNode(chainRow.Id, chainRow.Kind, chainRow.X, chainRow.Z, 0u, 0f, 0));

            // How many hops each node is away, by the host's reach rule (a running session hops on from its own held nodes only).
            int nodeCount = s.Nodes.Count;
            var graph = new List<CyberNode>(nodeCount);
            var busy = new bool[nodeCount];
            var hops = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++) { CyberNodeRow r = s.Nodes[i]; graph.Add(new CyberNode(r.Id, r.Kind, r.X, r.Z, 0u, 0f, 0)); busy[i] = r.Held || r.Hopping; }
            CyberGraph.Hops(graph, have ? null : trucks, have ? mineNodes : null, busy, Math.Max(1, CyberRules.MaxHeldPerIntrusion - myHeld), hops);

            int inReachCount = 0;
            for (int ni = 0; ni < nodeCount; ni++)
            {
                CyberNodeRow r = s.Nodes[ni];
                CyberNode cn = graph[ni];
                int viaTruck = -1, viaNode = 0;
                // Same rule as the host: a running session hops on from its own held nodes only; with none, from a truck in reach.
                bool reach = have ? CyberGraph.Reachable(cn, null, mineNodes, out viaTruck, out viaNode) : CyberGraph.Reachable(cn, trucks, null, out viaTruck, out viaNode);
                NetNodeState st = r.Held ? NetNodeState.Held : r.Hopping ? NetNodeState.Hopping : reach ? NetNodeState.InReach : NetNodeState.OutOfReach;
                if (st == NetNodeState.InReach) inReachCount++;
                int fromNode = -1, fromAnchor = -1;
                if ((r.Held || r.Hopping) && Chain(s, r.Id, out CyberIntrusionRow _, out int chainFrom, out int chainTruck)) { fromNode = chainFrom; fromAnchor = chainTruck >= 0 ? TruckAnchor(n.Anchors, chainTruck) : -1; }
                else if (reach) { fromNode = viaNode != 0 ? viaNode : -1; fromAnchor = viaTruck >= 0 ? TruckAnchor(n.Anchors, viaTruck) : -1; }
                string owner = r.Held ? (have && mine.Held != null && Array.IndexOf(mine.Held, r.Id) >= 0 ? "YOU" : HolderOf(s, r.Id)) : r.Hopping ? HopperOf(s, r.Id) : "";
                if (owner.Length == 0 && (r.Held || r.Hopping)) owner = "ALLY";
                bool src = Nearest(trucks, mineNodes, have, r.X, r.Z, r.Id, out string srcTag, out float srcDist, out float _);
                n.Nodes.Add(new NetNodeView
                {
                    Id = r.Id, Kind = r.Kind, X = frame.U(r.X), Y = frame.V(r.Z), State = st, HopPct = r.Hopping ? HopPercent(s, r.Id, now) : (byte)0,
                    FromNode = fromNode, FromAnchor = fromAnchor, Label = NodeLabel(r), Handle = NodeHandle(r.Kind, r.Id), Owner = owner,
                    Reach = src ? srcTag + " · " + Km(srcDist) + " KM" : "", SourceKm = src ? srcDist / 1000f : -1f, Place = PlaceOf(r.X, r.Z, c), Hops = hops[ni]
                });
            }
            // Our own anchors as nodes after the enemy's: negative ids the room picks locally. The node vocabulary (a wire enum) has no truck: an EW truck reads as
            // the relay it is to the network, a data center as a data center; Anchors[AnchorAt].Kind says which anchor it is.
            for (int i = 0; i < n.Anchors.Count; i++)
            {
                NetAnchorView a = n.Anchors[i];
                n.Nodes.Add(new NetNodeView
                {
                    Id = NetworkRoomView.OwnId(a.Kind, a.Slot), Kind = a.Kind == AnchorKind.EwTruck ? NodeKind.Relay : NodeKind.DataCenter, X = a.X, Y = a.Y, State = NetNodeState.OutOfReach, FromNode = -1, FromAnchor = -1,
                    Label = a.Label, Handle = a.Label.ToLowerInvariant() + ".local", Owner = "YOU", Reach = a.Reach > 0f ? "REACH " + Mathf.RoundToInt(a.Reach * n.MapKmW) + " KM" : "",
                    SourceKm = -1f, Place = a.Kind == AnchorKind.EwTruck ? "GRID " + OpsKit.Grid(frame.X(a.X), frame.Z(a.Y)) : PlaceOf(frame.X(a.X), frame.Z(a.Y), c),
                    Own = true, AnchorAt = i, Breached = a.Breached
                });
            }

            // The pick: kept while the node stays in the net; a vanished one is cleared (never swapped for another). The first pick is the room's (it knows its filter).
            if (selected > 0 && !s.Nodes.Exists(q => q.Id == selected)) selected = Gone;
            n.SelectedId = selected;

            // YOUR trace, the hop in flight and the estimate (rules only: no counter-pressure is mirrored).
            float maxRange = 0f;
            int freeTrucks = 0, lockedTrucks = 0, lockedLeft = 0;
            string lockedTag = "";
            for (int i = 0; i < trucks.Count; i++)
            {
                maxRange = Math.Max(maxRange, trucks[i].Reach);
                float lu = trucks[i].Index < lockUntil.Count ? lockUntil[trucks[i].Index] : 0f;
                if (lu > now) { lockedTrucks++; if (lockedTag.Length == 0) { lockedTag = TruckTag(trucks[i].Index); lockedLeft = (int)Math.Ceiling(lu - now); } }
                else freeTrucks++;
            }
            if (have)
            {
                n.Trace = mine.Trace;
                n.HopLeft = n.Hopping ? (int)Math.Ceiling(Math.Max(0f, mine.HopEndsAt - now)) : 0;
                NodeKind hopKind = n.Hopping && TryNode(s, mine.HopTarget, out CyberNodeRow hopRow) ? hopRow.Kind : NodeKind.Radar;
                float rate = ((n.Hopping ? CyberRules.HopTraceRate : 0f) + CyberRules.HeldTraceRate * myHeld) * (s.DataCenterUp ? CyberRules.DataCenterTraceFactor : 1f);
                n.EstTraceSeconds = rate > 0f ? (int)Math.Ceiling((CyberRules.TraceMax - mine.Trace) / rate) : -1;
                n.Stale = (n.Hopping || myHeld > 0) && n.AgeSeconds > StaleAfterSeconds;
            }
            n.ThreatWord = locked ? "TRACED" : anyDown ? "DEGRADED" : "CALM";
            n.ThreatNote = locked ? "AN EW TRUCK WAS TRACED AND IS LOCKED OUT" : anyDown ? "AN EW ASSET IS DOWN · BUILD ANOTHER ON INFRASTRUCTURE" : "NO LOSS ON OUR EW NET";
            // The other side: enemy holds on our own network and our PURGE (protocol 42).
            int breached = s.Breaches.Count;
            bool purging = s.PurgeEndsAt > now, cooling = !purging && s.PurgeReadyAt > now;
            int purgeLeft = purging ? (int)Math.Ceiling(s.PurgeEndsAt - now) : 0;
            n.Breaches = breached;
            n.CanPurge = breached > 0 && !purging && !cooling;
            n.PurgeLabel = purging ? "CUT IN " + purgeLeft + " S" : cooling ? "PURGE " + SpaceRules.Clock(s.PurgeReadyAt - now) : "PURGE";
            if (breached > 0 || purging)
            {
                n.ThreatWord = purging ? "PURGING" : "BREACHED";
                // The PURGE button beside it carries the countdown, so the note only counts the holds.
                n.ThreatNote = breached == 0 ? "PURGE UNDER WAY" : breached + " ENEMY HOLD" + (breached == 1 ? "" : "S") + " ON OUR NET";
            }
            n.Meta = s.Nodes.Count + " NODES MAPPED · " + s.HeldTotal + " HELD";

            // The one next step for the session (or why nothing can start).
            if (n.Stale) { n.Next = "STALE · NO HOST UPDATE FOR " + n.AgeSeconds + " S · HOP IS BLOCKED UNTIL ONE ARRIVES"; n.NextTone = AvState.Danger; }
            else if (have && mine.Trace >= 85) { n.Next = "TRACE " + mine.Trace + " % · BURN OR DROP NOW"; n.NextTone = AvState.Danger; }
            else if (n.Hopping) { n.Next = "HOP UNDER WAY · LANDS IN " + n.HopLeft + " S, THEN HOP DEEPER OR BURN"; n.NextTone = AvState.Info; }
            else if (have) { n.Next = "HOLDING " + myHeld + " · BURN FOR A STRIKE PACKAGE OR DROP BEFORE THE TRACE FILLS"; n.NextTone = mine.Trace >= 50 ? AvState.Caution : AvState.Ready; }
            else if (trucks.Count == 0)
            {
                n.Next = packing ? "EW TRUCK PACKING UP · NO REACH UNTIL IT REDEPLOYS" : anyTruck ? "ALL EW TRUCKS DOWN · NO REACH · " + NoTruck(s.DataCenterUp) : NoTruck(s.DataCenterUp);
                n.NextTone = anyTruck && !packing ? AvState.Danger : AvState.Caution;
            }
            else if (freeTrucks == 0 && lockedTrucks > 0) { n.Next = lockedTag + " TRACED · LOCKED " + SpaceRules.Clock(lockedLeft) + " · NO HOP UNTIL IT IS FREE"; n.NextTone = AvState.Danger; }
            else if (s.Intrusions.Count >= s.IntrusionCap) { n.Next = s.Intrusions.Count + " SESSIONS RUNNING (CAP " + s.IntrusionCap + ") · WAIT FOR ONE TO END"; n.NextTone = AvState.Caution; }
            else if (s.HeldTotal >= CyberRules.MaxHeldPerFaction) { n.Next = "FACTION HOLDS " + s.HeldTotal + " / " + CyberRules.MaxHeldPerFaction + " NODES · A BURN OR DROP FREES ONE"; n.NextTone = AvState.Caution; }
            else if (inReachCount == 0) { n.Next = "NO NODE WITHIN " + Km(maxRange) + " KM OF A TRUCK · MORE NODES SHOW AS THEY ARE FOUND"; n.NextTone = AvState.Caution; }
            else { n.Next = (others > 0 ? others + " ALLY SESSION · " : "") + "SELECT AN IN-REACH NODE AND HOP TO OPEN YOUR SESSION"; n.NextTone = AvState.Ready; }

            // Inspector and actions for the selected node.
            if (selected > 0 && TryNode(s, selected, out CyberNodeRow sel))
            {
                NetNodeView view = n.Nodes.Find(q => q.Id == selected);
                bool isHeld = sel.Held, exploit = CyberRules.Exploit(sel.Kind);
                // BURN and DROP act on the operator's own session only (the host refuses any other node): a node an ally holds shows who does.
                bool mineSel = have && mine.Held != null && Array.IndexOf(mine.Held, selected) >= 0;
                bool last = mineSel && myHeld == 1 && !n.Hopping;
                string quote = HopWords(sel.Kind, !have, exploit, myHeld, s.DataCenterUp, out int cost);
                string who = view.Owner;
                string ownerLine = isHeld ? (mineSel ? "HELD BY YOU" + (n.Operator.Length > 0 ? " · " + n.Operator : "") : "HELD BY " + Who(who))
                    : sel.Hopping ? (who == "YOU" ? "HOP BY YOU · LANDS IN " + n.HopLeft + " S" : "HOP BY " + Who(who)) : "UNHELD · ENEMY NETWORK NODE";
                bool engagedSel = isHeld || sel.Hopping;
                string reachLine = engagedSel && Chain(s, selected, out CyberIntrusionRow chainOf, out int _, out int _) ? "PATH " + PathOf(s, chainOf, selected)
                    : Nearest(trucks, mineNodes, have, sel.X, sel.Z, selected, out string nearTag, out float nearDist, out float nearRange)
                        ? (view.State == NetNodeState.InReach ? "IN REACH · " : "OUT OF REACH · NEAREST ") + nearTag + " · " + Km(nearDist) + " / " + Km(nearRange) + " KM"
                        : "NO REACH SOURCE STANDING";

                // The host's own order of refusals, so the line and the button agree with what a press would do.
                int viaT = -1;
                if (!have && view.State == NetNodeState.InReach) CyberGraph.Reachable(new CyberNode(sel.Id, sel.Kind, sel.X, sel.Z, 0u, 0f, 0), trucks, null, out viaT, out int _);
                float lockLeft = !have && viaT >= 0 && viaT < lockUntil.Count && lockUntil[viaT] > now ? lockUntil[viaT] - now : 0f;
                bool canHop = false;
                string next; AvState nextTone;
                if (mineSel && sel.Purging) { next = "PURGE INCOMING · THE OWNER IS CUTTING THIS NODE · BURN IT NOW OR LOSE IT"; nextTone = AvState.Danger; }
                else if (mineSel)
                {
                    bool hot = mine.Trace >= 85;
                    next = hot ? "WARNING · TRACE " + mine.Trace + " % · BURN OR DROP NOW" : "NEXT · BURN FOR ITS PACKAGE, DROP TO RELEASE, OR HOLD AND WATCH THE TRACE";
                    nextTone = hot ? AvState.Danger : AvState.Ready;
                }
                else if (isHeld) { next = "HELD BY " + Who(who) + " · ONLY THEIR SESSION CAN BURN OR DROP IT · PICK ANOTHER NODE"; nextTone = AvState.Caution; }
                else if (sel.Hopping)
                {
                    next = who == "YOU" ? "HOP LANDS IN " + n.HopLeft + " S · THEN HOP DEEPER OR BURN IT" : Who(who) + " IS HOPPING IT · PICK ANOTHER NODE";
                    nextTone = who == "YOU" ? AvState.Info : AvState.Caution;
                }
                else if (n.Stale) { next = "BLOCKED · STALE HOST DATA (" + n.AgeSeconds + " S) · WAIT FOR AN UPDATE"; nextTone = AvState.Danger; }
                else if (n.Hopping) { next = "BLOCKED · YOUR HOP LANDS IN " + n.HopLeft + " S · ONE HOP AT A TIME"; nextTone = AvState.Caution; }
                else if (view.State != NetNodeState.InReach)
                {
                    next = have ? "BLOCKED · OUT OF REACH OF YOUR HELD NODES (" + Km(CyberGraph.ChainReach) + " KM CHAIN) · HOP A NODE CLOSER FIRST"
                        : trucks.Count == 0 ? "BLOCKED · NO LIVE EW TRUCK · " + NoTruck(s.DataCenterUp) : "BLOCKED · BEYOND EVERY LIVE EW TRUCK'S " + Km(maxRange) + " KM · PICK A NODE IN REACH";
                    nextTone = AvState.Caution;
                }
                else if (have && myHeld >= CyberRules.MaxHeldPerIntrusion) { next = "BLOCKED · YOUR SESSION HOLDS " + myHeld + " / " + CyberRules.MaxHeldPerIntrusion + " · BURN OR DROP ONE FIRST"; nextTone = AvState.Caution; }
                else if (s.HeldTotal >= CyberRules.MaxHeldPerFaction) { next = "BLOCKED · FACTION HOLDS " + s.HeldTotal + " / " + CyberRules.MaxHeldPerFaction + " NODES · A BURN OR DROP FREES ONE"; nextTone = AvState.Caution; }
                else if (lockLeft > 0f) { next = "BLOCKED · " + TruckTag(viaT) + " TRACED · LOCKED " + SpaceRules.Clock(lockLeft); nextTone = AvState.Danger; }
                else if (!have && s.Intrusions.Count >= s.IntrusionCap) { next = "BLOCKED · " + s.Intrusions.Count + " SESSIONS RUNNING (CAP " + s.IntrusionCap + ") · WAIT FOR ONE TO END"; nextTone = AvState.Caution; }
                else
                {
                    canHop = true;
                    next = "NEXT · HOP IT · " + (have ? "FREE, DEEPER" : cost + " AP TO START") + ", LANDS IN " + CyberRules.HopSeconds(sel.Kind, exploit).ToString("0.#", NetFmt) + " S";
                    nextTone = AvState.Ready;
                }

                n.Dossier = new NetDossierView
                {
                    Has = true, Title = CyberNetWords.Node(sel.Kind, sel.Id), Handle = view.Handle + " · GRID " + OpsKit.Grid(sel.X, sel.Z),
                    Stamp = mineSel ? "HELD · YOU" : isHeld ? "HELD · ALLY" : sel.Hopping ? (who == "YOU" ? "HOPPING " + view.HopPct + " %" : "ALLY HOP") : view.State == NetNodeState.InReach ? "IN REACH" : "OUT OF REACH",
                    StampTone = mineSel ? AvState.Ready : isHeld ? AvState.Caution : sel.Hopping ? AvState.Info : view.State == NetNodeState.InReach ? AvState.Caution : AvState.Inert,
                    Owner = ownerLine, Reach = reachLine, Hold = HoldWords(sel.Kind),
                    Hop = engagedSel ? "NOT AVAILABLE · NODE IS " + (isHeld ? "HELD" : "BEING HOPPED") : quote,
                    Burn = mineSel ? BurnWords(sel.Kind, exploit, last) : "ONLY ON A NODE YOUR SESSION HOLDS",
                    Drop = mineSel ? "RELEASES THE NODE, NO PACKAGE · " + (last ? "ENDS YOUR SESSION, TRACE CLEARS" : "YOUR OTHER NODES STAY") : "ONLY ON A NODE YOUR SESSION HOLDS",
                    Next = next, NextTone = nextTone, HopCost = engagedSel ? 0 : cost
                };
                n.CanHop = canHop;
                n.CanBurn = mineSel;
                n.CanDrop = mineSel;
            }
            else
            {
                n.Dossier.Next = "NO NODE PICKED · PICK ONE ON THE MAP OR IN THE LIST TO SEE ITS QUOTE";
                n.Dossier.NextTone = AvState.Inert;
            }
        }
    }
}
