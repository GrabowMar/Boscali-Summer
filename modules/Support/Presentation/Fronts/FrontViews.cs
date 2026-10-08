using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>The theatre in metres, centred on the map origin: world X east and Z north map onto 0..1 with y down (north up).</summary>
    internal readonly struct MapFrame
    {
        public readonly float W, H;
        public MapFrame(float w, float h) { W = Math.Max(1000f, w); H = Math.Max(1000f, h); }
        public float U(float x) => GeoSpace.U(x, W);
        public float V(float z) => GeoSpace.V(z, H);
    }

    /// <summary>The non-mirror inputs of one refresh, gathered by the controller (so the mapping below needs no game).</summary>
    internal struct FrontInputs
    {
        public int Allocation;
        public float Now, CostScale;
        public string Faction, Callsign;
        public DateTime Utc;
        public MapFrame Frame;
        /// <summary>Bit i set while the faction's satellite i is dead, and its rebuild percent (from the OPS mirror).</summary>
        public byte BirdsDown;
        public byte[] BirdPercent;
        /// <summary>The strongest enemy's dead satellites (public: its launches and losses are visible); EnemyKnown is false before the OPS mirror arrives.</summary>
        public byte EnemyBirdsDown;
        public bool EnemyKnown;
        /// <summary>Whether the faction owns satellite i.</summary>
        public bool[] HasBird;
        /// <summary>Each satellite's geostationary state (own / strongest enemy); null falls back to the default parking slots until the host owns it.</summary>
        public GeoBird[] Geo, EnemyGeo;
        /// <summary>The map point the pilot is aiming a perk at (calls aim / targeting pod), when there is one.</summary>
        public bool AimKnown;
        public float AimU, AimV;
        public bool SpaceLinked;
        public SpaceFamilyState Family;
        public int RadarSeconds;
        public bool RadarUnavailable;
        public int Teams;
        public bool HeldBuilding;
        /// <summary>Airbases read from the game (null in tests that do not care).</summary>
        public IList<AirbaseFix> Airbases;
        /// <summary>The allocation price of a perk (the controller passes the manager's quote).</summary>
        public Func<SupportActionId, int> Price;
    }

    /// <summary>
    /// The pure mapping from the faction mirrors onto the plain-data views the front window paints. The controller gathers inputs and
    /// calls these; the offline harness calls the same functions with fixture mirrors, so the words and geometry are exercised there.
    /// </summary>
    internal static class FrontViews
    {
        private static readonly ProgrammeId[][] OfferSet =
        {
            new[] { ProgrammeId.Readiness, ProgrammeId.LaunchSatellite, ProgrammeId.UplinkSite, ProgrammeId.Asat },
            new[] { ProgrammeId.Readiness, ProgrammeId.DataCenter, ProgrammeId.EwTruck, ProgrammeId.ZeroDay },
            new[] { ProgrammeId.Readiness, ProgrammeId.TrainTeam, ProgrammeId.Camp, ProgrammeId.Fob },
        };

        /// <summary>Airbase map marks: world metres onto the plate, a short upper-case name, capped so the label pool never overflows.</summary>
        public static void Airbases(List<MapAirbaseView> into, IList<AirbaseFix> src, in MapFrame frame)
        {
            into.Clear();
            if (src == null) return;
            for (int i = 0; i < src.Count && into.Count < FrontRoomView.MaxAirbases; i++)
                into.Add(new MapAirbaseView { U = frame.U(src[i].X), V = frame.V(src[i].Z), Name = ShortName(src[i].Name), Side = src[i].Side });
        }

        /// <summary>"Airbase Kestrel Ridge" becomes "KESTREL" ; long names are cut to 10 characters.</summary>
        public static string ShortName(string raw)
        {
            string s = (raw ?? "").Trim().ToUpperInvariant();
            if (s.StartsWith("AIRBASE ", StringComparison.Ordinal)) s = s.Substring(8).TrimStart();
            if (s.Length > 10) { int sp = s.IndexOf(' '); s = sp >= 3 && sp <= 10 ? s.Substring(0, sp) : s.Substring(0, 10); }
            return s;
        }

        // ---- Common (rail) --------------------------------------------------------------------------------------------

        public static void Common(FrontRoomView v, Front f, FrontStateData d, bool known, in FrontInputs c)
        {
            v.Front = f;
            v.Faction = c.Faction ?? "";
            v.Callsign = c.Callsign ?? "";
            v.Dtg = c.Utc.ToString("ddHHmm", System.Globalization.CultureInfo.InvariantCulture) + "Z" + c.Utc.ToString("MMMyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
            v.Allocation = c.Allocation;
            v.Now = c.Now;
            v.Theatre = "THEATRE " + (c.Frame.W / 1000f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " x " + (c.Frame.H / 1000f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " KM";
            Airbases(v.Airbases, c.Airbases, c.Frame);
            v.Queue.Clear(); v.Offers.Clear(); v.Perks.Clear(); v.Log.Clear();
            FrontRow row = known && d != null ? d.Fronts[(int)f] : new FrontRow { Directive = FrontRules.DefaultDirective(f), PriorityPct = 33 };
            v.Row = row;
            v.FocusGrid = row.HasFocus ? OpsKit.Grid(row.FocusX, row.FocusZ) : "";
            float stamp = known && d != null ? d.Now : c.Now;

            for (int i = 0; i < row.Queue.Count && v.Queue.Count < FrontRules.MaxQueue; i++)
            {
                FrontQueueRow q = row.Queue[i];
                float cost = FrontRules.Cost(q.Id, row.Readiness, c.CostScale);
                float build = Math.Max(1f, FrontRules.BuildSeconds(q.Id, row.Readiness));
                bool building = q.Percent >= 100;
                int left = (int)Math.Max(0f, Math.Ceiling(q.BuildLeft - (building ? c.Now - stamp : 0f)));
                v.Queue.Add(new FrontQueueView
                {
                    Id = q.Id, Name = ProgrammeName(q.Id, row.Readiness), Cost = cost, Building = building, BuildLeft = left,
                    Progress = building ? Mathf.Clamp01(1f - left / build) : q.Percent / 100f
                });
            }

            foreach (ProgrammeId id in OfferSet[(int)f])
            {
                bool queued = row.Queue.Exists(q => q.Id == id);
                v.Offers.Add(new FrontOfferView
                {
                    Id = id, Name = ProgrammeName(id, row.Readiness), Cost = FrontRules.Cost(id, row.Readiness, c.CostScale),
                    BuildSeconds = (int)FrontRules.BuildSeconds(id, row.Readiness), Queued = queued,
                    Refusal = queued ? "" : Refusal(f, id, known ? d : null, row, c)
                });
            }

            foreach (CallRow r in CallSheet.Rows)
            {
                if (r.Front != f) continue;
                v.Perks.Add(new FrontPerkView { Rung = r.Rung, Name = r.Label, Effect = PerkEffect(r.Id), Price = c.Price != null ? c.Price(r.Id) : CallSheet.BasePrice(r.Rung), Unlocked = r.Rung <= row.Readiness, Gate = FootprintGate(r.Id, c) });
            }

            for (int i = 0; i < row.Log.Count; i++)
            {
                FrontLogRow l = row.Log[i];
                string text = FrontWords.Line(f, l, row.DirectiveBy);
                if (text.Length == 0) continue;
                v.Log.Add(new FrontLogLine { Stamp = FrontKit.Clock(l.Time), Text = text, Tone = LogTone((FrontLogCode)l.Code, l.A) });
            }
        }

        private static string ProgrammeName(ProgrammeId id, int readiness) =>
            id == ProgrammeId.Readiness ? "READINESS " + readiness + " → " + Math.Min(FrontRules.MaxReadiness, readiness + 1) : FrontRules.Info(id).Name;

        /// <summary>The client's best reading of why a programme cannot be queued; the host judges again on the press.</summary>
        public static string Refusal(Front f, ProgrammeId id, FrontStateData d, FrontRow row, in FrontInputs c)
        {
            if (row.Queue.Count >= FrontRules.MaxQueue) return FrontWords.Refusal(FrontOutcome.QueueFull, f);
            switch (id)
            {
                case ProgrammeId.Readiness: return row.Readiness >= FrontRules.MaxReadiness ? FrontWords.Refusal(FrontOutcome.ReadinessMax, f) : "";
                case ProgrammeId.LaunchSatellite: return c.BirdsDown == 0 ? FrontWords.Refusal(FrontOutcome.BirdsUp, f) : "";
                case ProgrammeId.Asat: return d != null && d.Fronts[(int)Front.Cyber].Readiness < FrontRules.AsatCyberReadiness ? FrontWords.Refusal(FrontOutcome.NeedsCyber3, f) : "";
                case ProgrammeId.TrainTeam: return c.Teams >= FrontRules.TeamCap ? FrontWords.Refusal(FrontOutcome.TeamCap, f) : "";
                case ProgrammeId.Fob: return c.HeldBuilding ? "" : FrontWords.Refusal(FrontOutcome.NoHeldBuilding, f);
                default: return "";
            }
        }

        private static AvState LogTone(FrontLogCode code, int a)
        {
            switch (code)
            {
                case FrontLogCode.Done: case FrontLogCode.Effect: return AvState.Ready;
                case FrontLogCode.Rebuild: return AvState.Danger;
                case FrontLogCode.Counter: return a != 0 ? AvState.Caution : AvState.Info;
                case FrontLogCode.Started: return AvState.Caution;
                default: return AvState.Info;
            }
        }

        /// <summary>The satellite a perk needs over its aim point: RECON PASS = RADAR, SAT CAMERA = OPTICAL, ORBITAL ROD = KINETIC. -1 for a perk that needs none.</summary>
        public static int PerkBird(SupportActionId id) => id == SupportActionId.SatCamera ? 0 : id == SupportActionId.Recon ? 1 : id == SupportActionId.Artillery ? 2 : -1;

        /// <summary>The state word when the current aim lies outside the footprint of the bird the perk needs ("" otherwise, or with no aim).</summary>
        public static string FootprintGate(SupportActionId id, in FrontInputs c)
        {
            int k = PerkBird(id);
            if (k < 0 || !c.AimKnown || c.HasBird == null || !c.HasBird[k] || (c.BirdsDown & (1 << k)) != 0) return "";
            return GeoOf(c.Geo, k, true).Covers(c.Now, c.AimU, c.AimV, Reach[k]) ? "" : SupportWords.OutsideFootprint(GeoSpace.Names[k]);
        }

        /// <summary>One line per perk for the rail's tier ladder.</summary>
        public static string PerkEffect(SupportActionId id)
        {
            switch (id)
            {
                case SupportActionId.Recon: return "RADAR bird scans your aim for ground contacts.";
                case SupportActionId.SatCamera: return "OPTICAL bird looks at the aim, then reveals.";
                case SupportActionId.Prsm: return "Offboard ballistic strike on the aim.";
                case SupportActionId.Cruise: return "Salvo of guided cruise missiles.";
                case SupportActionId.Artillery: return "Kinetic rod from orbit, needs the rod bird.";
                case SupportActionId.ElintSweep: return "Locates emitting enemy ground radars.";
                case SupportActionId.FlareMissile: return "Decoy barrage against inbound missiles.";
                case SupportActionId.RadarBlind: return "Blinds the enemy radar at the aim.";
                case SupportActionId.SamNetDown: return "Takes a revealed SAM net offline.";
                case SupportActionId.Emp: return "Electromagnetic pulse over the aim.";
                case SupportActionId.JtacMark: return "Team laser mark for any call.";
                case SupportActionId.ReconTeam: return "Team reveals a circle around the aim.";
                case SupportActionId.Fortify: return "Reinforces a held zone with defenders.";
                case SupportActionId.SabotageStrike: return "Team sabotage of an enemy anchor.";
                default: return "";
            }
        }

        // ---- ORBIT ----------------------------------------------------------------------------------------------------

        private static readonly BirdKind[] Kinds = { BirdKind.Optical, BirdKind.Radar, BirdKind.Kinetic };
        private static readonly string[] Names = { "DARKSTAR", "WATCHTOWER", "SPEARPOINT" };
        /// <summary>Footprint radius per bird as a share of the theatre width (optical / radar swath, kinetic strike reach).</summary>
        internal static float[] Reach => GeoSpace.Reach;
        private static readonly float[] OwnU = { 0.38f, 0.55f, 0.46f }, OwnV = { 0.42f, 0.58f, 0.50f }, FoeU = { 0.74f, 0.66f, 0.84f }, FoeV = { 0.26f, 0.40f, 0.22f };

        /// <summary>A bird's geostationary state: the host's when the inputs carry it, else it sits parked at its default slot with a full tank.</summary>
        internal static GeoBird GeoOf(GeoBird[] src, int k, bool own) =>
            src != null && k < src.Length && (src[k].Fuel > 0f || src[k].Length > 0f) ? src[k] : new GeoBird(own ? OwnU[k] : FoeU[k], own ? OwnV[k] : FoeV[k], 100f);

        private static OrbitTrackView Station(int k, bool own, GeoBird g, float now) => new OrbitTrackView
        {
            Kind = Kinds[k], Own = own, U = g.U(now), V = g.V(now), FromU = g.FromU, FromV = g.FromV, TargetU = g.ToU, TargetV = g.ToV,
            Relocating = g.Moving(now), EtaSeconds = g.Eta(now), BurnCost = g.BurnCost, Fuel = g.Fuel, Radius = Reach[k],
            Overhead = g.Covers(now, 0.5f, 0.5f, Reach[k]),
        };

        /// <summary>Each satellite parked over a point of the theatre (geostationary), its footprint, fuel and any transfer under way.</summary>
        public static void Orbit(FrontRoomView v, in FrontInputs c)
        {
            v.Birds.Clear(); v.Sites.Clear();
            for (int k = 0; k < Kinds.Length; k++)
            {
                bool has = c.HasBird != null && c.HasBird[k], down = (c.BirdsDown & (1 << k)) != 0;
                if (!has && !down) continue;
                OrbitTrackView b = Station(k, true, GeoOf(c.Geo, k, true), c.Now);
                string word = C2Orbit.BirdState(Kinds[k], c.SpaceLinked, has, c.Family, c.RadarSeconds, c.RadarUnavailable, out C2Tone tone);
                AvState state = C2Kit.StateOf(tone);
                if (down) { word = "LOST · REBUILD " + (c.BirdPercent != null ? c.BirdPercent[k] : 0) + " %"; state = AvState.Danger; }
                b.Alive = has && !down; b.Overhead &= b.Alive; b.Callsign = Names[k]; b.State = word; b.Tone = state;
                b.Orbit = "GEO 35 786 KM · FUEL " + Mathf.RoundToInt(b.Fuel) + " %";
                v.Birds.Add(b);
            }
            if (!c.EnemyKnown) return;
            // The enemy constellation, mirrored alive or dead from the host's real bird state, parked the same way.
            for (int k = 0; k < Kinds.Length; k++)
            {
                bool down = (c.EnemyBirdsDown & (1 << k)) != 0;
                OrbitTrackView b = Station(k, false, GeoOf(c.EnemyGeo, k, false), c.Now);
                b.Alive = !down; b.Overhead &= b.Alive; b.Callsign = "HOSTILE-" + (k + 1); b.State = down ? "DESTROYED" : "TRACKED"; b.Tone = down ? AvState.Inert : AvState.Danger;
                b.Orbit = "GEO 35 786 KM";
                v.Birds.Add(b);
            }
        }

        // ---- NETWORK --------------------------------------------------------------------------------------------------

        private static string NodeLabel(in CyberNodeRow n) => CyberNetWords.Code(n.Kind) + "-" + n.Id.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

        private static string NodeEffect(NodeKind k)
        {
            switch (k)
            {
                case NodeKind.Radar: return "Shows what the radar sees to your pilots; its picture feeds the SAM nodes.";
                case NodeKind.SamC2: return "Holding it blinds the SAM site's radar for friendly pilots.";
                case NodeKind.Relay: return "A hop point: holding it extends your reach deeper into the net.";
                case NodeKind.Uplink: return "Holding it jams that enemy uplink site.";
                default: return "Holding it denies the enemy network its data centre shield.";
            }
        }

        public static void Network(NetworkRoomView n, CyberStateData s, bool known, in MapFrame frame, int selected, float now)
        {
            n.Nodes.Clear(); n.Anchors.Clear(); n.Terminal.Clear();
            n.MapKmW = frame.W / 1000f; n.MapKmH = frame.H / 1000f;
            bool active = known && s != null && s.Active;
            n.Active = active;
            n.Dossier = default;
            n.HasIntrusion = false; n.Trace = 0; n.CanHop = n.CanBurn = n.CanDrop = false;
            n.HeldTotal = 0; n.IntrusionCap = 0; n.Infocon = 5; n.ThreatWord = known ? "CALM" : "NO LINK"; n.ThreatNote = ""; n.ActionNote = "";
            n.SelectedId = -1;
            n.Session = "NO SESSION · IDLE";
            if (!active)
            {
                n.Meta = known ? "NO EW ASSET STANDING" : "WAITING FOR THE HOST";
                n.TraceNote = "BUILD AN EW TRUCK TO REACH THE ENEMY NET";
                n.Terminal.Add(new FrontLogLine { Stamp = "", Text = known ? ">> NO EW TRUCK STANDING" : ">> WAITING FOR THE HOST CYBER STATE", Tone = AvState.Caution });
                return;
            }
            var trucks = new List<EwSource>(4);
            int truckOrdinal = 0, centers = 0;
            bool locked = false, anyDown = false;
            foreach (CyberAnchorRow a in s.Anchors)
            {
                bool truck = a.Kind == AnchorKind.EwTruck;
                int ordinal = truck ? truckOrdinal++ : centers++;
                float reach = truck ? AnchorRules.Reach(a.Health) : 0f;
                if (truck && a.Health != AnchorHealth.Down) trucks.Add(new EwSource(ordinal, a.X, a.Z, reach));
                locked |= a.LockUntil > now; anyDown |= a.Health == AnchorHealth.Down;
                n.Anchors.Add(new NetAnchorView
                {
                    Kind = a.Kind, Health = a.Health, X = frame.U(a.X), Y = frame.V(a.Z), Reach = reach / frame.W, Rebuild = a.Rebuild,
                    Label = (truck ? "EW-" : "DC-") + (ordinal + 1)
                });
            }
            var held = new List<CyberNode>(8);
            foreach (CyberNodeRow r in s.Nodes) if (r.Held) held.Add(new CyberNode(r.Id, r.Kind, r.X, r.Z, 0u, 0f, 0));

            CyberIntrusionRow mine = default;
            bool have = false;
            foreach (CyberIntrusionRow x in s.Intrusions) if (x.Own) { mine = x; have = true; break; }
            n.HasIntrusion = have;
            n.HeldTotal = s.HeldTotal; n.IntrusionCap = s.IntrusionCap;
            int others = s.Intrusions.Count - (have ? 1 : 0);

            if (selected > 0 && !s.Nodes.Exists(r => r.Id == selected)) selected = 0;
            if (selected == 0 && s.Nodes.Count > 0) selected = s.Nodes[0].Id;
            n.SelectedId = selected;

            foreach (CyberNodeRow r in s.Nodes)
            {
                var cn = new CyberNode(r.Id, r.Kind, r.X, r.Z, 0u, 0f, 0);
                bool reach = CyberGraph.Reachable(cn, trucks, held, out int viaTruck, out int viaNode);
                NetNodeState st = r.Held ? NetNodeState.Held : r.Hopping ? NetNodeState.Hopping : reach ? NetNodeState.InReach : NetNodeState.OutOfReach;
                int fromNode = -1, fromAnchor = -1;
                if (r.Held && have && mine.Held != null)
                {
                    int at = Array.IndexOf(mine.Held, r.Id);
                    if (at > 0) fromNode = mine.Held[at - 1];
                    else if (at == 0) fromAnchor = TruckAnchor(n.Anchors, mine.Truck);
                }
                else if (reach) { fromNode = viaTruck >= 0 ? -1 : viaNode; fromAnchor = viaTruck >= 0 ? TruckAnchor(n.Anchors, viaTruck) : -1; }
                byte hop = st == NetNodeState.Hopping && have && mine.HopTarget == r.Id ? (byte)Mathf.RoundToInt(CyberNetWords.HopProgress(mine, now) * 100f) : (byte)0;
                n.Nodes.Add(new NetNodeView { Id = r.Id, Kind = r.Kind, X = frame.U(r.X), Y = frame.V(r.Z), State = st, HopPct = hop, FromNode = fromNode, FromAnchor = fromAnchor, Label = NodeLabel(r) });
            }

            // Trace, threat and the intrusion line.
            if (have)
            {
                n.Trace = mine.Trace;
                int tone = CyberNetWords.TraceTone(mine.Trace);
                n.TraceNote = CyberNetWords.IntrusionLine(mine, s, now) + (tone == 2 ? " · BURN OR DROP NOW" : "");
                n.Session = "SESSION " + (string.IsNullOrEmpty(mine.Operator) ? "YOURS" : mine.Operator.ToUpperInvariant()) + " · " + (mine.Held != null ? mine.Held.Length : 0) + " HELD";
            }
            else n.TraceNote = others > 0 ? others + " FRIENDLY INTRUSION RUNNING · HOP A NODE IN REACH TO OPEN YOURS" : "HOP A NODE IN REACH TO OPEN ONE";
            n.Infocon = (byte)(locked ? 2 : anyDown ? 3 : 5);
            n.ThreatWord = locked ? "TRACED" : anyDown ? "DEGRADED" : "CALM";
            n.ThreatNote = locked ? "AN EW TRUCK WAS TRACED AND IS LOCKED OUT" : anyDown ? "AN EW ASSET IS DOWN · REBUILD IT FROM THE RAIL" : "NO LOSS ON OUR EW NET";
            n.Meta = s.Nodes.Count + " NODES MAPPED · " + s.HeldTotal + " HELD";

            // Dossier and actions for the selected node.
            if (selected > 0 && TryNode(s, selected, out CyberNodeRow sel))
            {
                NetNodeView view = n.Nodes.Find(x => x.Id == selected);
                bool isHeld = sel.Held;
                bool hopping = have && mine.Phase == IntrusionPhase.Hopping;
                string status = isHeld ? "HELD BY YOUR SIDE" : sel.Hopping ? "HOP IN PROGRESS" : view.State == NetNodeState.InReach ? "IN REACH · HOP IT" : "OUT OF REACH OF EVERY TRUCK AND HELD NODE";
                n.Dossier = new NetDossierView
                {
                    Has = true, Title = CyberNetWords.Node(sel.Kind, sel.Id), Kind = CyberNetWords.Name(sel.Kind),
                    Grid = "GRID " + OpsKit.Grid(sel.X, sel.Z), Defenses = "—", Effect = NodeEffect(sel.Kind),
                    Exploit = sel.Exploit ? "ZERO-DAY ARMED · CHEAPER, FASTER HOP" : "NONE IN STOCK",
                    Status = status, StatusTone = isHeld ? AvState.Ready : sel.Hopping ? AvState.Info : view.State == NetNodeState.InReach ? AvState.Caution : AvState.Inert
                };
                n.CanHop = !isHeld && view.State == NetNodeState.InReach && !hopping;
                n.CanBurn = have && isHeld;
                n.CanDrop = have && isHeld;
                n.ActionNote = n.CanHop ? "HOP COSTS ALLOCATION TO START, FREE DEEPER. THE TRACE CLIMBS WHILE YOU HOLD." : isHeld ? "BURN POSTS THE NODE'S STRIKE PACKAGE; DROP RELEASES IT." : "";
            }

            // Terminal: the faction's newest events, oldest first.
            n.Terminal.Add(new FrontLogLine { Stamp = "", Text = ">> " + CyberNetWords.Sub(s), Tone = AvState.Ready });
            foreach (CyberEventRow e in s.Events)
                n.Terminal.Add(new FrontLogLine
                {
                    Stamp = "#" + e.Seq.ToString("00", System.Globalization.CultureInfo.InvariantCulture), Text = ">> " + CyberNetWords.EventLine(e, now),
                    Tone = e.Kind == CyberEventKind.Traced ? AvState.Danger : e.Kind == CyberEventKind.NodeHeld ? AvState.Ready : AvState.Info
                });
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

        private static bool TryNode(CyberStateData s, int id, out CyberNodeRow row)
        {
            foreach (CyberNodeRow n in s.Nodes) if (n.Id == id) { row = n; return true; }
            row = default; return false;
        }

        // ---- SHADOW ---------------------------------------------------------------------------------------------------

        private static readonly MissionKind[] Missions = { MissionKind.Recon, MissionKind.Lase, MissionKind.Sabotage, MissionKind.Seize, MissionKind.Tap };

        public static MissionKind BestMission(in SofTargetRow t) =>
            t.Kind == TargetKind.Ground ? MissionKind.Lase : t.Kind == TargetKind.Building ? MissionKind.Seize : t.Kind == TargetKind.Relay ? MissionKind.Tap : MissionKind.Sabotage;

        private static string TargetName(in SofTargetRow t) => "T-" + t.Id + " " + SofWords.Target(t.Kind, t.Sub, t.Id).Replace(" #" + t.Id, "");

        /// <summary>The short time word of a team card (the card is narrow): a clock for the phase that ends, ETA for a march.</summary>
        private static string TeamEta(in SofTeamRow t, float now)
        {
            switch (t.State)
            {
                case TeamState.Raising: case TeamState.Recovering: return SpaceRules.Clock(t.EndsAt - now);
                case TeamState.OnSite: return t.Mission == MissionKind.Lase ? "HOLDING" : "ON SITE " + SpaceRules.Clock(t.EndsAt - now);
                case TeamState.Pinned: return "LOST IN " + SpaceRules.Clock(t.EndsAt - now);
                case TeamState.Moving: case TeamState.Returning:
                    if (t.Carried) return "IN HELO";
                    float speed = SofRules.SpeedMetresPerSecond * (t.Push ? SofRules.PushSpeedFactor : 1f);
                    return "ETA " + SpaceRules.Clock(SofRules.Distance(t.X, t.Z, t.DestX, t.DestZ) / speed);
                case TeamState.Ready: return t.LiftWaiting ? "LIFT ASKED" : "AT CAMP";
                default: return "";
            }
        }

        public static void Shadow(ShadowRoomView r, SofStateData s, bool known, CyberStateData cyber, in MapFrame frame, int selectedSlot, int selectedTarget, float now)
        {
            r.Camps.Clear(); r.Teams.Clear(); r.Targets.Clear(); r.Held.Clear(); r.Enemies.Clear(); r.Missions.Clear();
            for (int i = 0; i < 4; i++) { r.OrderOn[i] = false; r.OrderLatched[i] = false; }
            bool active = known && s != null && s.Active;
            r.Active = active;
            r.HasFob = false; r.FobName = ""; r.FobNote = "";
            r.SelectedSlot = -1; r.SelectedTargetId = 0;
            r.TapLine = "NO TAP RUNNING"; r.TargetLine = ""; r.OrderNote = ""; r.TeamCap = active ? s.TeamCap : 0;
            if (!active)
            {
                r.Meta = known ? "NO CAMP STANDING" : "WAITING FOR THE HOST";
                r.OrderNote = known ? "BUILD A CAMP, THEN QUEUE TRAIN TEAM." : "";
                r.TargetLine = "NO TARGET";
                return;
            }
            foreach (SofCampRow c in s.Camps)
                r.Camps.Add(new SofCampView { X = frame.U(c.X), Y = frame.V(c.Z), Health = c.Health, Rebuild = c.Rebuild, Name = "CAMP-" + (r.Camps.Count + 1) });
            foreach (SofHeldRow h in s.Held)
                r.Held.Add(new SofHeldView { X = frame.U(h.X), Y = frame.V(h.Z), SecondsLeft = Math.Max(0f, h.Until - now), Name = "BUILDING #" + h.Id });
            foreach (SofEnemyRow e in s.Enemies) r.Enemies.Add(new NetPoint { X = frame.U(e.X), Y = frame.V(e.Z) });

            if (selectedSlot < 0 || !s.Teams.Exists(t => t.Slot == selectedSlot)) selectedSlot = s.Teams.Count > 0 ? s.Teams[0].Slot : -1;
            if (selectedTarget != 0 && !s.Targets.Exists(t => t.Id == selectedTarget)) selectedTarget = 0;
            r.SelectedSlot = selectedSlot; r.SelectedTargetId = selectedTarget;

            SofTeamRow cur = default;
            bool haveTeam = false;
            for (int slot = 0; slot < Math.Max((int)s.TeamCap, 1) && slot < SofRules.MaxTeams; slot++)
            {
                SofTeamRow t = default;
                bool present = false;
                foreach (SofTeamRow x in s.Teams) if (x.Slot == slot) { t = x; present = true; break; }
                if (!present) { r.Teams.Add(new SofTeamView { Slot = slot, Present = false }); continue; }
                AvState tone = t.State == TeamState.Pinned || t.State == TeamState.Lost ? AvState.Danger : t.Exposure >= 70 ? AvState.Caution : t.State == TeamState.OnSite ? AvState.Info : AvState.Ready;
                string rest = TeamEta(t, now);
                string mission = t.Mission == MissionKind.None ? "NO MISSION" : SofWords.Kind(t.Mission) + (t.TargetId > 0 ? " · T-" + t.TargetId : "");
                bool helo = t.Carried || t.Insert == Insertion.Helicopter;
                r.Teams.Add(new SofTeamView
                {
                    Slot = slot, Present = true, Selected = slot == selectedSlot, Callsign = SofRules.Callsign(slot), State = SofWords.State(t.State), Tone = tone,
                    Mission = mission, Flags = (helo ? "HELO" : "GROUND") + (t.Push ? " · PUSH" : "") + (t.Hold ? " · HOLD" : "") + (t.Exploit ? " · EXPLOIT" : "") + (t.Wounded ? " · WIA" : ""),
                    Eta = rest, X = frame.U(t.X), Y = frame.V(t.Z), DestX = frame.U(t.DestX), DestY = frame.V(t.DestZ), TargetX = frame.U(t.TargetX), TargetY = frame.V(t.TargetZ),
                    HasDest = t.HasDest, Lasing = t.Lasing, Wounded = t.Wounded, Helicopter = helo, Exposure = t.Exposure, Ammo = t.Ammo, Odds = t.Odds
                });
                if (slot == selectedSlot) { cur = t; haveTeam = true; }
            }

            SofTargetRow tgt = default;
            bool haveTarget = false;
            foreach (SofTargetRow t in s.Targets)
            {
                if (r.Targets.Count >= SofWire.MaxTargets) break;
                if (t.Id == selectedTarget) { tgt = t; haveTarget = true; }
                r.Targets.Add(new SofTargetView
                {
                    Id = t.Id, Kind = t.Kind, X = frame.U(t.X), Y = frame.V(t.Z), Exploit = t.Exploit, Resisted = t.Resisted, Selected = t.Id == selectedTarget, Name = TargetName(t)
                });
            }
            r.TargetLine = haveTarget
                ? "TARGET " + TargetName(tgt) + " · GRID " + OpsKit.Grid(tgt.X, tgt.Z) + " · BEST " + SofWords.Kind(BestMission(tgt))
                : s.Targets.Count > 0 ? "NO TARGET SELECTED · CLICK A REVEALED TARGET" : "NO TARGET · REVEALS APPEAR HERE ONCE A TEAM RECONS";

            // Missions.
            bool ready = haveTeam && cur.State == TeamState.Ready;
            foreach (MissionKind kind in Missions)
            {
                bool valid = kind == MissionKind.Recon ? haveTarget : haveTarget && SofRules.Valid(kind, tgt.Kind, tgt.Sub);
                string odds = "--";
                if (haveTarget && kind != MissionKind.Lase)
                {
                    float ox = haveTeam ? cur.X : tgt.X, oz = haveTeam ? cur.Z : tgt.Z;
                    bool ring = cyber != null && cyber.Nodes.Exists(nd => nd.Held && SofRules.Distance(nd.X, nd.Z, ox, oz) <= SofRules.RingBoostMetres);
                    odds = SofRules.Odds(haveTeam ? cur.Exposure : 0f, 0, haveTeam && (cur.Carried || cur.Insert == Insertion.Helicopter), tgt.Exploit, ring) + " %";
                }
                else if (haveTarget) odds = "NO ROLL";
                r.Missions.Add(new SofMissionView
                {
                    Kind = kind, Name = SofWords.Kind(kind), Cost = SofRules.CostOf(kind, haveTarget && tgt.Exploit) + " AP", Odds = odds,
                    Note = !haveTeam ? "NEEDS A TEAM" : !ready ? SofRules.Callsign(cur.Slot) + " IS " + SofWords.State(cur.State) : !haveTarget ? "PICK A TARGET" : valid ? "BEST FIT" : "WRONG TARGET KIND",
                    Allowed = ready && valid, Exploit = haveTarget && tgt.Exploit, Current = haveTarget && kind == BestMission(tgt)
                });
            }

            // Orders: PUSH, HOLD, EXFIL, LIFT.
            if (haveTeam)
            {
                bool moving = cur.State == TeamState.Moving || cur.State == TeamState.Returning;
                r.OrderOn[0] = moving; r.OrderLatched[0] = cur.Push;
                r.OrderOn[1] = moving || cur.State == TeamState.OnSite; r.OrderLatched[1] = cur.Hold;
                r.OrderOn[2] = cur.State == TeamState.Moving || cur.State == TeamState.OnSite || cur.State == TeamState.Returning || (cur.State == TeamState.Ready && !cur.Carried);
                r.OrderOn[3] = cur.State == TeamState.Ready && !cur.Carried; r.OrderLatched[3] = cur.LiftWaiting;
                r.OrderNote = "ORDERS FOR " + SofRules.Callsign(cur.Slot) + " · LIFT ONLY FOR A TEAM AT REST.";
            }
            else r.OrderNote = s.Teams.Count == 0 ? "RAISE A TEAM FIRST: QUEUE TRAIN TEAM." : "SELECT A TEAM FIRST.";

            r.TapLine = s.TapUntil > now ? "TAP · " + s.TapIntrusions + " INTRUSIONS SHARED · " + FrontKit.Clock(s.TapUntil - now) : "NO TAP RUNNING";
            int teamsOut = 0;
            foreach (SofTeamRow t in s.Teams) if (t.State != TeamState.Lost) teamsOut++;
            r.Meta = teamsOut + " TEAMS OUT · " + s.Enemies.Count + " ENEMY TEAMS SEEN";
        }
    }
}
