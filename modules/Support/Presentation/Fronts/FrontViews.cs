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
        /// <summary>The inverse of <see cref="U"/> and <see cref="V"/>: world metres east and north of the map origin.</summary>
        public float X(float u) => (u - 0.5f) * W;
        public float Z(float v) => (0.5f - v) * H;
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
        /// <summary>The faction's own uplink sites (OPS mirror) and the enemy ones the CYBER fog reveals (nodes of kind Uplink). Null reads the live manager's mirrors.</summary>
        public IReadOnlyList<BoscaliSummer.Modules.Support.Domain.Ops.OpsSiteRow> Sites;
        public IReadOnlyList<CyberNodeRow> Uplinks;
    }

    /// <summary>
    /// The pure mapping from the faction mirrors onto the plain-data views the front window paints. The controller gathers inputs and
    /// calls these; the offline harness calls the same functions with fixture mirrors, so the words and geometry are exercised there.
    /// </summary>
    internal static class FrontViews
    {
        /// <summary>The row painted before the host has said anything: shared, never edited, recognised by reference. The rail shows a dash and WAITING FOR HOST for it, never a made-up R1.</summary>
        public static readonly FrontRow Waiting = new FrontRow();
        public static bool IsWaiting(FrontRow r) => ReferenceEquals(r, Waiting);

        /// <summary>A picked node or team whose object left the snapshot: nothing is picked until the pilot picks again (0 / -1 stay "nothing picked yet" and may pre-select the first).</summary>
        public const int Gone = -2;

        /// <summary>A RECON ground point rides <see cref="IFrontActions.TeamMission"/>'s target slot as -(packed point) - 1: a real target id is positive, so the two never meet.</summary>
        public static int PointId(float x, float z) => -SofCodes.PackPoint(x, z) - 1;

        public static bool TryPoint(int id, out float x, out float z)
        {
            x = z = 0f;
            return id < 0 && SofCodes.TryUnpackPoint(-id - 1, out x, out z);
        }

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
            FrontRow row = known && d != null ? d.Fronts[(int)f] : Waiting;
            v.Row = row;
            v.FocusGrid = row.HasFocus ? OpsKit.Grid(row.FocusX, row.FocusZ) : "";
            v.PowerLine = IsWaiting(row) ? "" : FrontSuperiority.PowerLine(row.Superiority);
            float stamp = known && d != null ? d.Now : c.Now;
            v.FeedAge = known && d != null ? Math.Max(0f, c.Now - d.Now) : -1f;
            for (int i = 0; i < v.Shares.Length; i++) v.Shares[i] = known && d != null ? d.Fronts[i].PriorityPct : 0;

            for (int i = 0; i < row.Queue.Count && v.Queue.Count < FrontRules.MaxQueue; i++)
            {
                FrontQueueRow q = row.Queue[i];
                float cost = FrontRules.Cost(q.Id, row.Readiness, c.CostScale);
                float build = Math.Max(1f, FrontRules.BuildSeconds(q.Id, row.Readiness));
                bool building = i == 0 && q.Percent >= 100; // only the head ever builds: a waiting programme a donor filled is FUNDED, not BUILDING
                int left = (int)Math.Max(0f, Math.Ceiling(q.BuildLeft - (building ? c.Now - stamp : 0f)));
                v.Queue.Add(new FrontQueueView
                {
                    Id = q.Id, Name = ProgrammeName(q.Id, row.Readiness), Cost = cost, Building = building, BuildLeft = left, BuildTotal = (int)build,
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
                    Refusal = queued ? "" : IsWaiting(row) ? "WAITING FOR THE HOST" : Refusal(f, id, known ? d : null, row, c)
                });
            }

            foreach (CallRow r in CallSheet.Rows)
            {
                if (r.Front != f) continue;
                int price = c.Price != null ? c.Price(r.Id) : CallSheet.BasePrice(r.Rung);
                v.Perks.Add(new FrontPerkView
                {
                    Rung = r.Rung, Name = r.Label, Effect = PerkEffect(r.Id), Price = price, Unlocked = !IsWaiting(row) && r.Rung <= row.Readiness,
                    Gate = FootprintGate(r.Id, c), Reason = IsWaiting(row) ? "" : PerkReason(r.Id, r.Rung, row, price, c)
                });
            }

            for (int i = 0; i < row.Log.Count; i++)
            {
                FrontLogRow l = row.Log[i];
                string text = FrontWords.Line(f, l, row.DirectiveBy);
                if (text.Length == 0) continue;
                v.Log.Add(new FrontLogLine { Stamp = FrontKit.Clock(l.Time), Text = text, Tone = LogTone((FrontLogCode)l.Code, l.A), Ago = Math.Max(0f, c.Now - l.Time) });
            }
        }

        /// <summary>Why a perk is not usable right now, read from what the rail already holds ("" when it is): the readiness rung, the satellite it needs, then the allocation price.</summary>
        private static string PerkReason(SupportActionId id, int rung, FrontRow row, int price, in FrontInputs c)
        {
            if (rung > row.Readiness) return "NEEDS READINESS " + rung;
            int k = PerkBird(id);
            if (k >= 0 && c.HasBird != null)
            {
                if ((c.BirdsDown & (1 << k)) != 0) return "SATELLITE LOST · REBUILD " + (c.BirdPercent != null && k < c.BirdPercent.Length ? c.BirdPercent[k] : 0) + " %";
                if (!c.HasBird[k]) return "NO " + GeoSpace.Names[k] + " SATELLITE · LAUNCH ONE";
            }
            return price > c.Allocation ? "NEEDS " + price + " AP · YOU HAVE " + c.Allocation : "";
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

        /// <summary>The satellite a perk needs over its aim point: RECON PASS and OVERWATCH = RADAR, SAT CAMERA = OPTICAL, ORBITAL ROD and STARFALL = KINETIC. -1 for a perk that needs none.</summary>
        public static int PerkBird(SupportActionId id) =>
            id == SupportActionId.SatCamera ? 0 : id == SupportActionId.Recon || id == SupportActionId.Overwatch ? 1 : id == SupportActionId.Artillery || id == SupportActionId.Starfall ? 2 : -1;

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
                case SupportActionId.Recon: return "RADAR bird scans the aim, reveals.";
                case SupportActionId.SatCamera: return "OPTICAL bird images the aim.";
                case SupportActionId.Prsm: return "Ballistic missile strike on the aim.";
                case SupportActionId.Cruise: return "Salvo of guided cruise missiles.";
                case SupportActionId.Artillery: return "Kinetic rod from the KINETIC bird.";
                case SupportActionId.Overwatch: return "Radar follows movers near the aim.";
                case SupportActionId.Starfall: return "3 to 5 small rods in a line.";
                case SupportActionId.Siphon: return "Takes funds from enemy budgets.";
                case SupportActionId.DirectAction: return "Team raids a SAM, radar or anchor.";
                case SupportActionId.ElintSweep: return "Finds emitting enemy ground radars.";
                case SupportActionId.FlareMissile: return "Flare barrage against inbound fire.";
                case SupportActionId.RadarBlind: return "Blinds the enemy radar at the aim.";
                case SupportActionId.SamNetDown: return "Takes a revealed SAM net offline.";
                case SupportActionId.Emp: return "Electromagnetic pulse over the aim.";
                case SupportActionId.JtacMark: return "Team laser mark for any call.";
                case SupportActionId.ReconTeam: return "Team reveals a circle at the aim.";
                case SupportActionId.Fortify: return "Defenders reinforce a held zone.";
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

        internal static string BirdCallsign(int k) => Names[k];

        /// <summary>Slot longitude of a parked point: a fixed per-faction base plus the point's offset across the theatre at geostationary range (1 degree = 735 km). Flavour; it moves only when the bird really does.</summary>
        internal static string SlotLon(string faction, bool own, float u, float theatreM)
        {
            float lon = 12f + FactionIdentity.Of(faction) % 600u / 10f + (own ? 0f : 9.6f) + (u - 0.5f) * theatreM / 735000f;
            return lon.ToString("0.00", NetFmt) + "E";
        }

        private static OrbitTrackView Station(int k, bool own, GeoBird g, in FrontInputs c, FrontRoomView v)
        {
            float u = g.U(c.Now);
            return new OrbitTrackView
            {
                Kind = Kinds[k], Own = own, U = u, V = g.V(c.Now), FromU = g.FromU, FromV = g.FromV, TargetU = g.ToU, TargetV = g.ToV,
                Relocating = g.Moving(c.Now), EtaSeconds = g.Eta(c.Now), BurnCost = g.BurnCost, Fuel = g.Fuel, Radius = Reach[k],
                Overhead = v.HasFocus && g.Covers(c.Now, v.FocusU, v.FocusV, Reach[k]),
                FootprintKm = Reach[k] * v.TheatreKm, Slot = SlotLon(c.Faction, own, u, c.Frame.W),
                Catalog = "CAT " + (40000u + (FactionIdentity.Of(c.Faction) + 977u * (uint)(k + 1) + (own ? 0u : 3331u)) % 9000u),
            };
        }

        /// <summary>Each satellite parked over a point of the theatre (geostationary), its footprint, fuel and any transfer under way; the uplink sites; the focus pin the footprints are read against.</summary>
        public static void Orbit(FrontRoomView v, in FrontInputs c)
        {
            v.Birds.Clear(); v.Sites.Clear();
            v.TheatreKm = c.Frame.W / 1000f;
            v.HasFocus = v.Row != null && v.Row.HasFocus && !IsWaiting(v.Row);
            v.FocusU = v.HasFocus ? c.Frame.U(v.Row.FocusX) : 0.5f; v.FocusV = v.HasFocus ? c.Frame.V(v.Row.FocusZ) : 0.5f;
            Sites(v, c);
            for (int k = 0; k < Kinds.Length; k++)
            {
                bool has = c.HasBird != null && c.HasBird[k], down = (c.BirdsDown & (1 << k)) != 0;
                if (!has && !down) continue;
                OrbitTrackView b = Station(k, true, GeoOf(c.Geo, k, true), c, v);
                string word = C2Orbit.BirdState(Kinds[k], c.SpaceLinked, has, c.Family, c.RadarSeconds, c.RadarUnavailable, out C2Tone tone);
                AvState state = C2Kit.StateOf(tone);
                if (down) { word = "LOST · REBUILD " + (c.BirdPercent != null ? c.BirdPercent[k] : 0) + " %"; state = AvState.Danger; }
                b.Alive = has && !down; b.Overhead &= b.Alive; b.Callsign = Names[k]; b.State = word; b.Tone = state;
                b.Orbit = "SLOT " + b.Slot + " · " + b.Catalog;
                v.Birds.Add(b);
            }
            if (!c.EnemyKnown) return;
            // The enemy constellation, mirrored alive or dead from the host's real bird state, parked the same way.
            for (int k = 0; k < Kinds.Length; k++)
            {
                bool down = (c.EnemyBirdsDown & (1 << k)) != 0;
                OrbitTrackView b = Station(k, false, GeoOf(c.EnemyGeo, k, false), c, v);
                b.Alive = !down; b.Overhead &= b.Alive; b.Callsign = "HOSTILE-" + (k + 1); b.State = down ? "DESTROYED" : "TRACKED"; b.Tone = down ? AvState.Inert : AvState.Danger;
                b.Orbit = "SLOT " + b.Slot + " · " + b.Catalog;
                v.Birds.Add(b);
            }
        }

        private static readonly string[] SiteNames = { "ALPHA", "BRAVO" };

        internal static string SiteName(int slot) => "UPLINK " + SiteNames[Math.Min(Math.Max(slot, 0), SiteNames.Length - 1)];

        /// <summary>The uplink markers: the faction's own sites (OPS mirror) with their standing and what losing each costs, then the enemy sites the CYBER fog reveals. No site, no marker: nothing is invented.</summary>
        private static void Sites(FrontRoomView v, in FrontInputs c)
        {
            SupportManager m = c.Sites == null || c.Uplinks == null ? SupportManager.Active : null;
            IReadOnlyList<BoscaliSummer.Modules.Support.Domain.Ops.OpsSiteRow> own = c.Sites ?? m?.OpsMirror.Sites;
            IReadOnlyList<CyberNodeRow> foe = c.Uplinks ?? (m != null && m.CyberMirror.Known ? m.CyberMirror.State.Nodes : null);
            int up = 0, n = own != null ? own.Count : 0;
            for (int i = 0; i < n; i++) if (own[i].State == BoscaliSummer.Modules.Support.Domain.Ops.SiteState.Up) up++;
            string space = c.Family == SpaceFamilyState.Dark ? "DARK" : "DEGRADED";
            for (int i = 0; i < n; i++)
            {
                BoscaliSummer.Modules.Support.Domain.Ops.OpsSiteRow s = own[i];
                bool live = s.State == BoscaliSummer.Modules.Support.Domain.Ops.SiteState.Up, rebuilding = s.State == BoscaliSummer.Modules.Support.Domain.Ops.SiteState.Rebuilding;
                v.Sites.Add(new OrbitSiteView
                {
                    X = c.Frame.U(s.X), Y = c.Frame.V(s.Z), Own = true, Live = live, Rebuilding = rebuilding, Name = SiteName(s.Slot),
                    State = live ? "LIVE" : rebuilding ? "REBUILDING" : "DESTROYED", Grid = "GRID " + OpsKit.Grid(s.X, s.Z),
                    Cost = live ? (up <= 1 ? "LAST SITE · LOSS DARKENS SPACE" : "LOSS DEGRADES SPACE")
                        : rebuilding ? "SPACE " + space + " UNTIL REBUILT" : "SPACE " + space + " · QUEUE AN UPLINK SITE",
                });
            }
            for (int i = 0; foe != null && i < foe.Count && v.Sites.Count < 6; i++)
            {
                if (foe[i].Kind != NodeKind.Uplink) continue;
                v.Sites.Add(new OrbitSiteView
                {
                    X = c.Frame.U(foe[i].X), Y = c.Frame.V(foe[i].Z), Own = false, Live = true, Name = "HOSTILE " + NodeTag(NodeKind.Uplink, foe[i].Id), State = "REVEALED",
                    Grid = "GRID " + OpsKit.Grid(foe[i].X, foe[i].Z), Cost = "STRIKE IT TO DEGRADE THEIR SPACE",
                });
            }
        }

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
            float rate = (CyberRules.HopTraceRate(kind) + CyberRules.HeldTraceRate * heldNow) * (dataCenterUp ? CyberRules.DataCenterTraceFactor : 1f);
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

        public static void Network(NetworkRoomView n, CyberStateData s, bool known, in MapFrame frame, int selected, float now)
        {
            n.Nodes.Clear(); n.Anchors.Clear(); n.Roster.Clear();
            n.MapKmW = frame.W / 1000f; n.MapKmH = frame.H / 1000f;
            bool active = known && s != null && s.Active;
            n.Active = active;
            n.Dossier = default;
            n.HasIntrusion = false; n.Trace = 0; n.CanHop = n.CanBurn = n.CanDrop = false;
            n.HeldTotal = 0; n.IntrusionCap = 0; n.Sessions = 0; n.YourHeld = 0; n.Hopping = false; n.HopFraction = 0f; n.HopLeft = 0;
            n.EstTraceSeconds = -1; n.Stale = false; n.AgeSeconds = 0; n.Operator = ""; n.Prompt = "guest@net:~$";
            n.Infocon = 5; n.ThreatWord = known ? "CALM" : "NO LINK"; n.ThreatNote = "";
            n.Breaches = 0; n.CanPurge = false; n.PurgeLabel = "PURGE";
            n.HopLine = ""; n.Next = ""; n.NextTone = AvState.Caution;
            n.SelectedId = -1;
            n.Session = "NO SESSION · IDLE";
            if (!known) { n.History.Clear(); n.LastSeq = 0; }
            if (!active)
            {
                n.Meta = known ? "NO EW ASSET STANDING" : "WAITING FOR THE HOST";
                n.HopLine = "NO EW LINK · NOTHING TO HOP FROM";
                n.Next = known ? "NO EW TRUCK STANDING · QUEUE OR REBUILD ONE UNDER PROGRAMMES TO REACH THE ENEMY NET" : "WAITING FOR THE HOST CYBER STATE · NOTHING TO SHOW YET";
                n.NextTone = known ? AvState.Caution : AvState.Inert;
                n.Dossier.Next = n.Next; n.Dossier.NextTone = n.NextTone;
                return;
            }
            n.AgeSeconds = Math.Max(0, (int)(now - s.Now));
            var trucks = new List<EwSource>(4);
            var lockUntil = new List<float>(4); // by truck ordinal
            int truckOrdinal = 0, centers = 0;
            bool locked = false, anyDown = false, anyTruck = false;
            foreach (CyberAnchorRow a in s.Anchors)
            {
                bool truck = a.Kind == AnchorKind.EwTruck;
                int ordinal = truck ? truckOrdinal++ : centers++;
                float reach = truck ? AnchorRules.Reach(a.Health) : 0f;
                if (truck && a.Health != AnchorHealth.Down) trucks.Add(new EwSource(ordinal, a.X, a.Z, reach));
                if (truck) { lockUntil.Add(a.LockUntil); anyTruck = true; }
                bool lockedNow = a.LockUntil > now;
                locked |= lockedNow; anyDown |= a.Health == AnchorHealth.Down;
                n.Anchors.Add(new NetAnchorView
                {
                    Kind = a.Kind, Health = a.Health, X = frame.U(a.X), Y = frame.V(a.Z), Reach = reach / frame.W, Rebuild = a.Rebuild,
                    LockLeft = truck && lockedNow ? (int)Math.Ceiling(a.LockUntil - now) : 0,
                    Label = (truck ? "EW-" : "DC-") + (ordinal + 1)
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

            int inReachCount = 0;
            foreach (CyberNodeRow r in s.Nodes)
            {
                var cn = new CyberNode(r.Id, r.Kind, r.X, r.Z, 0u, 0f, 0);
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
                string reachText = Nearest(trucks, mineNodes, have, r.X, r.Z, r.Id, out string srcTag, out float srcDist, out float _) ? srcTag + " · " + Km(srcDist) + " KM" : "";
                n.Nodes.Add(new NetNodeView
                {
                    Id = r.Id, Kind = r.Kind, X = frame.U(r.X), Y = frame.V(r.Z), State = st, HopPct = r.Hopping ? HopPercent(s, r.Id, now) : (byte)0,
                    FromNode = fromNode, FromAnchor = fromAnchor, Label = NodeLabel(r), Handle = NodeHandle(r.Kind, r.Id), Owner = owner, Reach = reachText
                });
            }

            // The pick: kept while the node stays in the net; a vanished one is cleared (never swapped for another); the first time, the best node to start from.
            if (selected > 0 && !s.Nodes.Exists(q => q.Id == selected)) selected = Gone;
            else if (selected == 0 && s.Nodes.Count > 0)
            {
                selected = s.Nodes[0].Id;
                for (int i = 0; i < n.Nodes.Count; i++) if (n.Nodes[i].State == NetNodeState.InReach) { selected = n.Nodes[i].Id; break; }
            }
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
                n.HopLine = CyberNetWords.IntrusionLine(mine, s, now);
                n.HopFraction = CyberNetWords.HopProgress(mine, now);
                n.HopLeft = n.Hopping ? (int)Math.Ceiling(Math.Max(0f, mine.HopEndsAt - now)) : 0;
                NodeKind hopKind = n.Hopping && TryNode(s, mine.HopTarget, out CyberNodeRow hopRow) ? hopRow.Kind : NodeKind.Radar;
                float rate = ((n.Hopping ? CyberRules.HopTraceRate(hopKind) : 0f) + CyberRules.HeldTraceRate * myHeld) * (s.DataCenterUp ? CyberRules.DataCenterTraceFactor : 1f);
                n.EstTraceSeconds = rate > 0f ? (int)Math.Ceiling((CyberRules.TraceMax - mine.Trace) / rate) : -1;
                n.Stale = (n.Hopping || myHeld > 0) && n.AgeSeconds > StaleAfterSeconds;
                n.Session = "SESSION " + (n.Operator.Length == 0 ? "YOURS" : n.Operator) + " · " + myHeld + " HELD";
                n.Prompt = (n.Operator.Length == 0 ? "you" : n.Operator.ToLowerInvariant()) + "@" + TruckTag(mine.Truck).ToLowerInvariant() + ":~$";
            }
            else n.HopLine = "IDLE · NO HOP RUNNING"; // the dial already says NO INTRUSION
            n.Infocon = (byte)(locked ? 2 : anyDown ? 3 : 5);
            n.ThreatWord = locked ? "TRACED" : anyDown ? "DEGRADED" : "CALM";
            n.ThreatNote = locked ? "AN EW TRUCK WAS TRACED AND IS LOCKED OUT" : anyDown ? "AN EW ASSET IS DOWN · REBUILD IT FROM THE RAIL" : "NO LOSS ON OUR EW NET";
            // The other side: enemy holds on our own network and our PURGE (protocol 42).
            int breached = s.Breaches.Count;
            bool purging = s.PurgeEndsAt > now, cooling = !purging && s.PurgeReadyAt > now;
            int purgeLeft = purging ? (int)Math.Ceiling(s.PurgeEndsAt - now) : 0;
            n.Breaches = breached;
            n.CanPurge = breached > 0 && !purging && !cooling;
            n.PurgeLabel = purging ? "CUT IN " + purgeLeft + " S" : cooling ? "PURGE " + SpaceRules.Clock(s.PurgeReadyAt - now) : "PURGE";
            if (breached > 0 || purging)
            {
                n.Infocon = (byte)(breached >= 2 ? 1 : 2);
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
            else if (trucks.Count == 0 && anyTruck) { n.Next = "ALL EW TRUCKS DOWN · NO REACH · REBUILD ONE FROM THE RAIL"; n.NextTone = AvState.Danger; }
            else if (freeTrucks == 0 && lockedTrucks > 0) { n.Next = lockedTag + " TRACED · LOCKED " + SpaceRules.Clock(lockedLeft) + " · NO HOP UNTIL IT IS FREE"; n.NextTone = AvState.Danger; }
            else if (s.Intrusions.Count >= s.IntrusionCap) { n.Next = s.Intrusions.Count + " SESSIONS RUNNING (CAP " + s.IntrusionCap + ") · WAIT FOR ONE TO END"; n.NextTone = AvState.Caution; }
            else if (s.HeldTotal >= CyberRules.MaxHeldPerFaction) { n.Next = "FACTION HOLDS " + s.HeldTotal + " / " + CyberRules.MaxHeldPerFaction + " NODES · A BURN OR DROP FREES ONE"; n.NextTone = AvState.Caution; }
            else if (inReachCount == 0) { n.Next = "NO NODE WITHIN " + Km(maxRange) + " KM OF A TRUCK · MORE NODES SHOW AS THEY ARE FOUND"; n.NextTone = AvState.Caution; }
            else { n.Next = (others > 0 ? others + " ALLY SESSION · " : "") + "SELECT AN IN-REACH NODE AND HOP TO OPEN YOUR SESSION"; n.NextTone = AvState.Ready; }

            // Log: who is online (the mirror lists every intrusion of the faction), then the real events.
            for (int pass = 0; pass < 2; pass++)
                foreach (CyberIntrusionRow x in s.Intrusions)
                {
                    if (x.Own != (pass == 0)) continue;
                    string op = string.IsNullOrEmpty(x.Operator) ? (x.Own ? "you" : "ally") : x.Operator.ToLowerInvariant();
                    n.Roster.Add(new FrontLogLine
                    {
                        Stamp = x.Own ? "YOU" : "ALLY", Text = op + " · " + TruckTag(x.Truck) + " · " + (x.Held != null ? x.Held.Length : 0) + " HELD · TRACE " + x.Trace + " %",
                        Tone = x.Own ? (CyberNetWords.TraceTone(x.Trace) == 2 ? AvState.Danger : CyberNetWords.TraceTone(x.Trace) == 1 ? AvState.Caution : AvState.Ready) : AvState.Info
                    });
                }
            Remember(n, s);

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
                        : trucks.Count == 0 ? "BLOCKED · NO LIVE EW TRUCK · REBUILD ONE FROM THE RAIL" : "BLOCKED · BEYOND EVERY LIVE EW TRUCK'S " + Km(maxRange) + " KM · PICK A NODE IN REACH";
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

        /// <summary>Keeps the faction's real events across refreshes (the mirror carries only the newest three), oldest first, stamped with their own sequence number.</summary>
        private static void Remember(NetworkRoomView n, CyberStateData s)
        {
            int newest = 0;
            foreach (CyberEventRow e in s.Events) newest = Math.Max(newest, e.Seq);
            if (newest < n.LastSeq) { n.History.Clear(); n.LastSeq = 0; } // a new mission numbers its events again
            for (; ; )
            {
                int best = -1;
                for (int i = 0; i < s.Events.Count; i++)
                    if (s.Events[i].Seq > n.LastSeq && (best < 0 || s.Events[i].Seq < s.Events[best].Seq)) best = i;
                if (best < 0) break;
                n.History.Add(HistoryLine(s.Events[best]));
                n.LastSeq = s.Events[best].Seq;
                if (n.History.Count > NetworkRoomView.MaxHistory) n.History.RemoveAt(0);
            }
        }

        private static FrontLogLine HistoryLine(in CyberEventRow e)
        {
            string stamp = "#" + e.Seq.ToString("00", NetFmt), by = " [" + (e.Own ? "YOU" : "ALLY") + "]", h = NodeHandle(e.Node, e.NodeId);
            switch (e.Kind)
            {
                case CyberEventKind.HopStarted: return new FrontLogLine { Stamp = stamp, Text = "hop " + h + by, Tone = AvState.Info };
                case CyberEventKind.NodeHeld: return new FrontLogLine { Stamp = stamp, Text = "held " + h + by, Tone = AvState.Ready };
                case CyberEventKind.Released:
                    return new FrontLogLine
                    {
                        Stamp = stamp, Text = "rel " + h + " " + CyberNetWords.ReasonWord(e.Reason).ToLowerInvariant() + by,
                        Tone = e.Reason == IntrusionEnd.Burned || e.Reason == IntrusionEnd.Dropped ? AvState.Info : AvState.Danger
                    };
                default: return new FrontLogLine { Stamp = stamp, Text = "TRACED · EW TRUCK REVEALED 2:00" + by, Tone = AvState.Danger };
            }
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

        private static readonly string[] TeamWords = { "SABRE", "VIPER", "HALBERD", "KESTREL" };
        private static readonly string[] OpAdjectives = { "IRON", "PALE", "HOLLOW", "SILENT", "COLD", "AMBER", "BLIND", "CINDER" };
        private static readonly string[] OpNouns = { "HERON", "LANTERN", "ANVIL", "MARTEN", "SPINDLE", "WARDEN", "FALCONER", "THRESHOLD" };

        /// <summary>The dossier word of a team slot: fixed per slot, it carries no reading (the host's own words keep the A-1 designator).</summary>
        internal static string TeamCodename(int slot) => TeamWords[Math.Max(0, slot) % TeamWords.Length];

        /// <summary>The operation name of a mission: the same team, mission and target always give the same two words, so the quote and the card agree.</summary>
        internal static string OpName(int slot, MissionKind kind, int targetId)
        {
            int h = ((slot + 1) * 31 + (int)kind * 17 + Math.Max(0, targetId) * 7) & int.MaxValue;
            return OpAdjectives[h % OpAdjectives.Length] + " " + OpNouns[h / OpAdjectives.Length % OpNouns.Length];
        }

        /// <summary>One fixed rubber-stamp word per team state: STANDBY for a team at rest, TASKED once it marches on a mission.</summary>
        private static string StampWord(in SofTeamRow t) =>
            t.State == TeamState.Ready ? (t.LiftWaiting ? "LIFT ASKED" : "STANDBY") : t.State == TeamState.Moving && t.Mission != MissionKind.None ? "TASKED" : SofWords.State(t.State);

        /// <summary>A team's standing warning (rank 3 lost, 2 pinned, 1 too little ammo for another mission), "" and rank 0 when it is fine.</summary>
        private static string AlertWord(in SofTeamRow t, float now, out int rank)
        {
            rank = t.State == TeamState.Lost ? 3 : t.State == TeamState.Pinned ? 2 : t.State != TeamState.Raising && t.Ammo < SofDesk.AmmoPerMission ? 1 : 0;
            return rank == 3 ? "LOST" : rank == 2 ? "PINNED · LOST IN " + SpaceRules.Clock(t.EndsAt - now) : rank == 1 ? "LOW AMMO " + t.Ammo + " %" : "";
        }

        /// <summary>What the host asks of a target for this mission, in the order words use.</summary>
        private static string NeedWord(MissionKind k) =>
            k == MissionKind.Lase ? "A GROUND CONTACT" : k == MissionKind.Sabotage ? "AN ENEMY ANCHOR" : k == MissionKind.Seize ? "A BUILDING" : k == MissionKind.Tap ? "A RELAY OR DATA CENTER" : "A TARGET";

        private static string TaskedOn(SofStateData s, int targetId)
        {
            string who = "";
            foreach (SofTeamRow t in s.Teams)
                if (t.TargetId == targetId && t.Mission != MissionKind.None && t.State != TeamState.Lost) who += (who.Length > 0 ? ", " : "") + SofRules.Callsign(t.Slot) + " " + SofWords.Kind(t.Mission);
            return who.Length > 0 ? "TASKED · " + who : "NO TEAM TASKED";
        }

        private static AvState EventTone(SofEventKind k) =>
            k == SofEventKind.Lost || k == SofEventKind.Failed || k == SofEventKind.Retaken ? AvState.Danger : k == SofEventKind.Pinned ? AvState.Caution
            : k == SofEventKind.Success || k == SofEventKind.Seized || k == SofEventKind.Home || k == SofEventKind.Raised ? AvState.Ready : AvState.Info;

        /// <summary>Keeps the faction's real events across refreshes (the mirror carries only the newest three), oldest first, stamped with their own sequence number.</summary>
        private static void Remember(ShadowRoomView r, SofStateData s)
        {
            int newest = 0;
            foreach (SofEventRow e in s.Events) newest = Math.Max(newest, e.Seq);
            if (newest < r.LastSeq) { r.History.Clear(); r.LastSeq = 0; } // a new mission numbers its events again
            for (; ; )
            {
                int best = -1;
                for (int i = 0; i < s.Events.Count; i++)
                    if (s.Events[i].Seq > r.LastSeq && (best < 0 || s.Events[i].Seq < s.Events[best].Seq)) best = i;
                if (best < 0) break;
                SofEventRow ev = s.Events[best];
                r.History.Add(new FrontLogLine { Stamp = "#" + ev.Seq.ToString("00", System.Globalization.CultureInfo.InvariantCulture), Text = SofPageWords.EventLine(ev), Tone = EventTone(ev.Kind) });
                r.LastSeq = ev.Seq;
                if (r.History.Count > ShadowRoomView.MaxHistory) r.History.RemoveAt(0);
            }
        }

        public static void Shadow(ShadowRoomView r, SofStateData s, bool known, CyberStateData cyber, in MapFrame frame, int selectedSlot, int selectedTarget, float now)
        {
            r.Camps.Clear(); r.Teams.Clear(); r.Targets.Clear(); r.Held.Clear(); r.Enemies.Clear(); r.EnemyMark.Clear(); r.Missions.Clear();
            r.CanSweep = false; r.SweepLabel = "SWEEP"; r.QrfLine = ""; r.QrfTone = AvState.Inert;
            for (int i = 0; i < 4; i++) { r.OrderOn[i] = false; r.OrderLatched[i] = false; }
            bool active = known && s != null && s.Active;
            r.Active = active;
            r.HasFob = false; r.FobName = ""; r.FobNote = "";
            r.SelectedSlot = -1; r.SelectedTargetId = 0;
            r.TapLine = "NO TAP RUNNING"; r.TeamCap = active ? s.TeamCap : 0;
            r.TeamLine = ""; r.AimLine = ""; r.AimNote = ""; r.QuoteLine = ""; r.QuoteNote = ""; r.IssueLabel = "ISSUE ORDER"; r.IssueNote = ""; r.ExfilLabel = "EXFIL"; r.ExfilNote = "";
            r.TeamTone = r.AimTone = r.QuoteTone = r.IssueTone = AvState.Inert;
            r.CanIssue = false;
            r.FileTab = r.FileA = r.FileB = r.FileC = r.FileD = "";
            r.WarnSlot = -1; r.WarnText = ""; r.WarnTone = AvState.Inert;
            if (!known) { r.History.Clear(); r.LastSeq = 0; }
            if (!active)
            {
                r.Meta = known ? "NO CAMP STANDING" : "WAITING FOR THE HOST";
                r.DraftMission = MissionKind.None; r.Reason = "";
                r.WarnText = r.Meta;
                r.TeamLine = known ? "BUILD A CAMP, THEN QUEUE TRAIN TEAM" : "";
                r.FileTab = "NO FILE"; r.FileA = "NO TARGET";
                return;
            }
            foreach (SofCampRow c in s.Camps)
                r.Camps.Add(new SofCampView { X = frame.U(c.X), Y = frame.V(c.Z), Health = c.Health, Rebuild = c.Rebuild, Name = "CAMP-" + (r.Camps.Count + 1) });
            foreach (SofHeldRow h in s.Held)
                r.Held.Add(new SofHeldView { X = frame.U(h.X), Y = frame.V(h.Z), SecondsLeft = Math.Max(0f, h.Until - now), Name = "BUILDING #" + h.Id });
            int near = 0, hunting = 0;
            foreach (SofEnemyRow e in s.Enemies)
            {
                r.Enemies.Add(new NetPoint { X = frame.U(e.X), Y = frame.V(e.Z) });
                r.EnemyMark.Add(e.Hunted ? "Q" : e.Near ? "!" : "?");
                if (e.Near) near++;
                if (e.Hunted) hunting++;
            }
            // The other side: hostile teams near our sites and our QRF (protocol 42).
            bool rolling = s.SweepEndsAt > now, refit = !rolling && s.SweepReadyAt > now;
            r.CanSweep = near > 0 && !rolling && !refit;
            r.SweepLabel = rolling ? "QRF " + (int)Math.Ceiling(s.SweepEndsAt - now) + " S" : refit ? "SWEEP " + FrontKit.Clock(s.SweepReadyAt - now) : "SWEEP";
            // One short line (the card is 170 px wide): what the QRF is doing, else how many hostiles are near our sites.
            r.QrfLine = rolling ? "QRF ROLLING · " + (int)Math.Ceiling(s.SweepEndsAt - now) + " S"
                : hunting > 0 ? "QRF HUNTING " + hunting + " TEAM" + (hunting == 1 ? "" : "S")
                : near > 0 ? near + " HOSTILE" + (near == 1 ? "" : "S") + " NEAR US" + (refit ? " · QRF " + FrontKit.Clock(s.SweepReadyAt - now) : "")
                : refit ? "QRF REFIT · " + FrontKit.Clock(s.SweepReadyAt - now) : "NO HOSTILES NEAR US";
            r.QrfTone = near > 0 || hunting > 0 ? AvState.Danger : AvState.Inert;

            // A picked team or target that left the snapshot is cleared with the reason, never swapped for another (and the draft with it); only "nothing picked yet" (-1) takes the first team.
            string gone = "";
            if (selectedSlot >= 0 && !s.Teams.Exists(t => t.Slot == selectedSlot)) { gone = SofRules.Callsign(selectedSlot) + " LEFT THE ROSTER"; selectedSlot = Gone; }
            else if (selectedSlot == -1 && s.Teams.Count > 0) selectedSlot = s.Teams[0].Slot;
            if (selectedTarget != 0 && !s.Targets.Exists(t => t.Id == selectedTarget)) { gone += (gone.Length > 0 ? " · " : "") + "T-" + selectedTarget + " IS NO LONGER REVEALED"; selectedTarget = 0; }
            if (gone.Length > 0)
            {
                if (r.DraftMission != MissionKind.None) gone += " · DRAFT CLEARED";
                r.DraftMission = MissionKind.None;
                r.Reason = gone;
            }
            r.SelectedSlot = selectedSlot; r.SelectedTargetId = selectedTarget;

            SofTeamRow cur = default;
            bool haveTeam = false, curLocked = false;
            AvState curTone = AvState.Inert;
            int alerts = 0, worstRank = 0;
            string worstText = "";
            for (int slot = 0; slot < Math.Max((int)s.TeamCap, 1) && slot < SofRules.MaxTeams; slot++)
            {
                SofTeamRow t = default;
                bool present = false;
                foreach (SofTeamRow x in s.Teams) if (x.Slot == slot) { t = x; present = true; break; }
                if (!present) { r.Teams.Add(new SofTeamView { Slot = slot, Present = false }); continue; }
                string alert = AlertWord(t, now, out int rank);
                bool locked = !t.Mine && !string.IsNullOrEmpty(t.Controller);
                AvState tone = t.State == TeamState.Pinned || t.State == TeamState.Lost ? AvState.Danger : t.Exposure >= 70 || rank == 1 || t.Hunted ? AvState.Caution : t.State == TeamState.OnSite ? AvState.Info : AvState.Ready;
                string rest = TeamEta(t, now);
                string mission = t.Mission == MissionKind.None ? "NO MISSION"
                    : SofWords.Kind(t.Mission) + (t.TargetId > 0 ? " · T-" + t.TargetId : "") + " · " + OpName(slot, t.Mission, t.TargetId) + (t.State == TeamState.Moving || t.State == TeamState.OnSite ? " · " + t.Odds + " %" : "");
                bool helo = t.Carried || t.Insert == Insertion.Helicopter;
                r.Teams.Add(new SofTeamView
                {
                    Slot = slot, Present = true, Selected = slot == selectedSlot, Callsign = SofRules.Callsign(slot), State = SofWords.State(t.State), Tone = tone,
                    Codename = TeamCodename(slot), Control = SofPageWords.Control(t), Stamp = StampWord(t), Locked = locked,
                    Mission = mission, Flags = (helo ? "HELO" : "GROUND") + (t.Push ? " · PUSH" : "") + (t.Hold ? " · HOLD" : "") + (t.Exploit ? " · EXPLOIT" : "") + (t.Wounded ? " · WIA" : "") + (t.Hunted ? " · QRF ON US" : ""),
                    Eta = rest, X = frame.U(t.X), Y = frame.V(t.Z), DestX = frame.U(t.DestX), DestY = frame.V(t.DestZ), TargetX = frame.U(t.TargetX), TargetY = frame.V(t.TargetZ),
                    HasDest = t.HasDest, Lasing = t.Lasing, Wounded = t.Wounded, Helicopter = helo, Exposure = t.Exposure, Ammo = t.Ammo, Odds = t.Odds
                });
                if (rank > 0)
                {
                    alerts++;
                    if (rank > worstRank) { worstRank = rank; r.WarnSlot = slot; worstText = SofRules.Callsign(slot) + " " + TeamCodename(slot) + " · " + alert; }
                }
                if (slot == selectedSlot) { cur = t; haveTeam = true; curLocked = locked; curTone = tone; }
            }
            if (alerts > 0) { r.WarnText = worstText + (alerts > 1 ? " · +" + (alerts - 1) + " MORE" : ""); r.WarnTone = worstRank >= 2 ? AvState.Danger : AvState.Caution; }
            else if (s.Teams.Count == 0) r.WarnText = "NO TEAM RAISED";
            else { r.WarnText = "ALL TEAMS CLEAR · NONE PINNED, LOST OR LOW ON AMMO"; r.WarnTone = AvState.Ready; }
            Remember(r, s);

            SofTargetRow tgt = default;
            bool haveTarget = false;
            foreach (SofTargetRow t in s.Targets)
            {
                if (r.Targets.Count >= SofWire.MaxTargets) break;
                if (t.Id == selectedTarget) { tgt = t; haveTarget = true; }
                r.Targets.Add(new SofTargetView
                {
                    Id = t.Id, Kind = t.Kind, X = frame.U(t.X), Y = frame.V(t.Z), Exploit = t.Exploit, Resisted = t.Resisted, Selected = t.Id == selectedTarget, Name = TargetName(t),
                    Detail = OpsKit.Grid(t.X, t.Z) + " · " + (t.Resisted ? "RESISTED" : t.Exploit ? "EXPLOIT" : SofWords.Kind(BestMission(t)))
                });
            }

            // Missions: any picked team the operator may command can be drafted for; only a team at rest with ammo, aimed at a valid target, can be sent.
            bool ready = haveTeam && cur.State == TeamState.Ready;
            bool lowAmmo = haveTeam && cur.Ammo < SofDesk.AmmoPerMission, canTake = ready && !curLocked && !lowAmmo;
            foreach (MissionKind kind in Missions)
            {
                // RECON aims at a target or a ground point; the host gives it no EXPLOIT discount or boost. Odds are an estimate: zero armour, the team where it stands now.
                bool recon = kind == MissionKind.Recon, atPoint = recon && r.HasDraft, exploit = !recon && haveTarget && tgt.Exploit, aimed = atPoint || haveTarget;
                bool valid = recon ? aimed : haveTarget && SofRules.Valid(kind, tgt.Kind, tgt.Sub);
                string odds = "--";
                if (aimed && kind != MissionKind.Lase)
                {
                    float ox = haveTeam ? cur.X : atPoint ? frame.X(r.DraftU) : tgt.X, oz = haveTeam ? cur.Z : atPoint ? frame.Z(r.DraftV) : tgt.Z;
                    bool ring = cyber != null && cyber.Nodes.Exists(nd => nd.Held && SofRules.Distance(nd.X, nd.Z, ox, oz) <= SofRules.RingBoostMetres);
                    odds = "EST ~" + SofRules.Odds(haveTeam ? cur.Exposure : 0f, 0, haveTeam && (cur.Carried || cur.Insert == Insertion.Helicopter), exploit, ring) + " %";
                }
                else if (aimed) odds = "NO ROLL";
                bool best = haveTarget && kind == BestMission(tgt);
                r.Missions.Add(new SofMissionView
                {
                    Kind = kind, Name = SofWords.Kind(kind), Cost = SofRules.CostOf(kind, exploit) + " AP", Odds = odds,
                    Note = !haveTeam ? "NEEDS A TEAM" : curLocked ? "VIEW ONLY · " + SofPageWords.Control(cur) : !ready ? SofRules.Callsign(cur.Slot) + " IS " + SofWords.State(cur.State)
                        : lowAmmo ? "NEEDS " + (int)SofDesk.AmmoPerMission + " % AMMO" : !aimed ? (recon ? "PICK TARGET OR POINT" : "PICK A TARGET")
                        : valid ? (atPoint ? "AT THE MAP POINT" : best ? "BEST FIT" : "FITS THE TARGET") : "WRONG TARGET KIND",
                    Allowed = canTake && valid, Pick = haveTeam && !curLocked, Drafted = kind == r.DraftMission, Exploit = exploit, Current = best
                });
            }

            // Orders: PUSH, HOLD, EXFIL, LIFT. Another operator's team gets none.
            string call = haveTeam ? SofRules.Callsign(cur.Slot) : "";
            if (haveTeam && !curLocked)
            {
                bool moving = cur.State == TeamState.Moving || cur.State == TeamState.Returning;
                r.OrderOn[0] = moving; r.OrderLatched[0] = cur.Push;
                r.OrderOn[1] = moving || cur.State == TeamState.OnSite; r.OrderLatched[1] = cur.Hold;
                r.OrderOn[2] = cur.State == TeamState.Moving || cur.State == TeamState.OnSite || cur.State == TeamState.Returning || (cur.State == TeamState.Ready && !cur.Carried);
                r.OrderOn[3] = cur.State == TeamState.Ready && !cur.Carried; r.OrderLatched[3] = cur.LiftWaiting;
            }
            if (haveTeam)
            {
                r.ExfilLabel = "EXFIL " + call;
                r.ExfilNote = cur.Mission == MissionKind.None ? call + " HAS NO MISSION · IT GOES HOME" : "ABANDONS " + SofWords.Kind(cur.Mission) + (cur.TargetId > 0 ? " T-" + cur.TargetId : "") + " · " + call + " GOES HOME";
            }
            else r.ExfilNote = "NO TEAM PICKED";

            // The composer, one line per step: TEAM, MISSION (the rows), AIM, QUOTE, ISSUE.
            MissionKind draft = r.DraftMission;
            SofMissionView dm = default;
            foreach (SofMissionView m in r.Missions) if (m.Kind == draft) dm = m;
            bool draftAimed = haveTarget || (draft == MissionKind.Recon && r.HasDraft);
            if (haveTeam)
            {
                r.TeamLine = call + " " + TeamCodename(cur.Slot) + " · " + SofWords.State(cur.State) + " · " + SofPageWords.Control(cur) + (curLocked ? " · VIEW ONLY" : "");
                r.TeamTone = curLocked ? AvState.Caution : curTone;
            }
            else r.TeamLine = s.Teams.Count == 0 ? "NO TEAM RAISED · QUEUE TRAIN TEAM" : "NONE PICKED · CLICK A TEAM IN THE ROSTER";
            if (haveTarget)
            {
                r.AimLine = TargetName(tgt) + (tgt.Exploit ? " · EXPLOIT" : tgt.Resisted ? " · RESISTED" : "");
                r.AimNote = "GRID " + OpsKit.Grid(tgt.X, tgt.Z) + " · BEST " + SofWords.Kind(BestMission(tgt));
                r.AimTone = tgt.Resisted ? AvState.Inert : tgt.Exploit ? AvState.Caution : AvState.Info;
            }
            else if (r.HasDraft)
            {
                r.AimLine = "MAP POINT · RECON ONLY";
                r.AimNote = "GRID " + OpsKit.Grid(frame.X(r.DraftU), frame.Z(r.DraftV)) + " · CLICK THE MAP TO MOVE IT";
                r.AimTone = AvState.Info;
            }
            else
            {
                r.AimLine = "NO AIM YET";
                r.AimNote = s.Targets.Count > 0 ? "CLICK A TARGET, OR MAP GROUND FOR RECON" : "CLICK MAP GROUND FOR A RECON POINT";
            }
            if (draft == MissionKind.None) { r.QuoteLine = "NOTHING DRAFTED"; r.QuoteNote = "PICK A MISSION ABOVE"; }
            else
            {
                r.QuoteLine = dm.Name + " · " + dm.Cost + " · " + dm.Odds;
                r.QuoteNote = "OP " + OpName(haveTeam ? cur.Slot : 0, draft, haveTarget ? tgt.Id : 0) + " · " + (draft == MissionKind.Lase ? "HOLDS UP TO " : "ON SITE ") + SpaceRules.Clock(SofRules.OnSiteSeconds(draft));
                r.QuoteTone = dm.Allowed ? AvState.Ready : AvState.Caution;
            }
            r.CanIssue = draft != MissionKind.None && dm.Allowed;
            if (r.CanIssue) r.IssueLabel = "ISSUE " + call + " · " + SofWords.Kind(draft);
            if (r.Reason.Length > 0) { r.IssueNote = r.Reason; r.IssueTone = AvState.Caution; }
            else if (!haveTeam) { r.IssueNote = "NEXT · PICK A TEAM"; r.IssueTone = AvState.Info; }
            else if (curLocked) { r.IssueNote = "BLOCKED · " + call + " IS COMMANDED BY " + cur.Controller; r.IssueTone = AvState.Caution; }
            else if (!ready) { r.IssueNote = "BLOCKED · " + call + " IS " + SofWords.State(cur.State); r.IssueTone = AvState.Caution; }
            else if (lowAmmo) { r.IssueNote = "BLOCKED · " + call + " NEEDS " + (int)SofDesk.AmmoPerMission + " % AMMO"; r.IssueTone = AvState.Caution; }
            else if (draft == MissionKind.None) { r.IssueNote = "NEXT · CLICK A MISSION TO SEND IT"; r.IssueTone = AvState.Info; }
            else if (!draftAimed) { r.IssueNote = "NEXT · PICK " + (draft == MissionKind.Recon ? "A TARGET OR MAP POINT" : NeedWord(draft)); r.IssueTone = AvState.Info; }
            else if (!dm.Allowed) { r.IssueNote = "BLOCKED · " + SofWords.Kind(draft) + " NEEDS " + NeedWord(draft); r.IssueTone = AvState.Caution; }
            else { r.IssueNote = "READY · ISSUE SENDS ONCE"; r.IssueTone = AvState.Ready; }

            // The picked target's file card.
            if (haveTarget)
            {
                r.FileTab = "FILE T-" + tgt.Id;
                r.FileA = SofWords.Target(tgt.Kind, tgt.Sub, tgt.Id).Replace(" #" + tgt.Id, "") + (tgt.Exploit ? " · EXPLOIT" : tgt.Resisted ? " · RESISTED" : "");
                r.FileB = "GRID " + OpsKit.Grid(tgt.X, tgt.Z) + " · BEST " + SofWords.Kind(BestMission(tgt));
                r.FileC = TaskedOn(s, tgt.Id);
                r.FileD = tgt.Exploit ? "EXPLOIT · CUTS A TARGET MISSION'S COST" : tgt.Resisted ? "RESISTED · SPACE BEATS SOF" : "";
            }
            else
            {
                r.FileTab = r.HasDraft ? "RECON POINT" : "NO FILE";
                r.FileA = r.HasDraft ? "GROUND POINT" : "NOTHING PICKED";
                r.FileB = r.HasDraft ? "GRID " + OpsKit.Grid(frame.X(r.DraftU), frame.Z(r.DraftV)) : s.Targets.Count + " TARGETS REVEALED";
                r.FileC = r.HasDraft ? "ONLY RECON TAKES A POINT" : s.Targets.Count > 0 ? "PICK A ROW OR A MARKER ON THE MAP" : "RECON GROUND TO REVEAL ONE";
            }

            r.TapLine = s.TapUntil > now ? "TAP · " + s.TapIntrusions + " INTRUSIONS SHARED · " + FrontKit.Clock(s.TapUntil - now) : "NO TAP RUNNING";
            int teamsOut = 0;
            foreach (SofTeamRow t in s.Teams) if (t.State != TeamState.Lost) teamsOut++;
            r.Meta = teamsOut + " TEAMS OUT · " + s.Enemies.Count + " ENEMY TEAMS SEEN";
        }
    }
}
