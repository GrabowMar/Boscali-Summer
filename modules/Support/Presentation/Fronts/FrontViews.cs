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
        /// <summary>Funds per minute the faction's share tick moves into its three fronts (0 when unknown); each front takes its priority share of it.</summary>
        public float Trickle;
        /// <summary>The local pilot's skill grade per front system (index = <see cref="Front"/>, 1..6): what unlocks its perks. Null reads as grade 1.</summary>
        public int[] Grades;
        public float Now, CostScale;
        public string Faction, Callsign;
        public DateTime Utc;
        public MapFrame Frame;
        /// <summary>Bit i set while the faction's satellite i is not up (never launched or lost), and bit i set once it was launched this scene (from the OPS mirror).</summary>
        public byte BirdsDown, BirdsLaunched;
        /// <summary>The strongest enemy's satellites that are not up (never launched or lost: the mirror cannot tell which); EnemyKnown is false before the OPS mirror arrives.</summary>
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
        /// <summary>What the shown front has built as the mirrors tell it (<see cref="FrontViews.World"/>); WorldKnown false leaves the offer refusals to the host.</summary>
        public FrontWorld World;
        public bool WorldKnown;
        /// <summary>When the faction's FOB ends (its OPS row; 0 when none is up or the OPS mirror is unknown): the FOB card's time left.</summary>
        public float FobUntil;
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

        /// <summary>Per front: its ladder (infrastructure, then the asset it carries), then its operation; each is queued with automatic placement.</summary>
        private static readonly ProgrammeId[][] OfferSet =
        {
            new[] { ProgrammeId.UplinkSite, ProgrammeId.LaunchSatellite, ProgrammeId.Asat },
            new[] { ProgrammeId.DataCenter, ProgrammeId.EwTruck, ProgrammeId.ZeroDay },
            new[] { ProgrammeId.Camp, ProgrammeId.TrainTeam, ProgrammeId.Fob },
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
            // This front's share of the minute's trickle, as far as it still fits under the budget cap.
            v.FundsRate = IsWaiting(row) ? 0 : (int)Math.Round(Math.Min(c.Trickle * row.PriorityPct / 100f, Math.Max(0f, FrontRules.BudgetCap - row.Budget)));
            v.FocusGrid =row.HasFocus ? OpsKit.Grid(row.FocusX, row.FocusZ) : "";
            v.PowerLine = IsWaiting(row) ? "" : FrontSuperiority.PowerLine(row.Superiority);
            v.Grade = GradeOf(c, f);
            float stamp = known && d != null ? d.Now : c.Now;
            v.FeedAge = known && d != null ? Math.Max(0f, c.Now - d.Now) : -1f;
            for (int i = 0; i < v.Shares.Length; i++) v.Shares[i] = known && d != null ? d.Fronts[i].PriorityPct : 0;

            for (int i = 0; i < row.Queue.Count && v.Queue.Count < FrontRules.MaxQueue; i++)
            {
                FrontQueueRow q = row.Queue[i];
                float cost = FrontRules.Cost(q.Id, c.CostScale);
                float build = Math.Max(1f, FrontRules.BuildSeconds(q.Id));
                bool building = i == 0 && q.Percent >= 100; // only the head ever builds: a waiting programme a donor filled is FUNDED, not BUILDING
                int left = (int)Math.Max(0f, Math.Ceiling(q.BuildLeft - (building ? c.Now - stamp : 0f)));
                v.Queue.Add(new FrontQueueView
                {
                    Id = q.Id, Row = q.Row, Name = FrontWords.Programme(q.Id, q.Kind), Cost = cost, Building = building, BuildLeft = left, BuildTotal = (int)build,
                    Progress = building ? Mathf.Clamp01(1f - left / build) : q.Percent / 100f
                });
            }

            QueuedCounts queued = FrontLadder.Queued(row.Queue);
            foreach (ProgrammeId id in OfferSet[(int)f])
            {
                // A SATELLITE offer launches the lowest kind neither up nor queued (only the queue counts while the OPS mirror is unknown).
                byte kind = id == ProgrammeId.LaunchSatellite ? FrontLadder.FreeKind((c.WorldKnown ? c.World.BirdsUp : 0) | queued.Birds) : (byte)0;
                v.Offers.Add(Offer(f, id, kind, row, queued, c));
            }

            foreach (CallRow r in CallSheet.Rows)
            {
                if (r.Front != f) continue;
                int price = c.Price != null ? c.Price(r.Id) : CallSheet.BasePrice(r.Tier);
                v.Perks.Add(new FrontPerkView
                {
                    Id = r.Id, Rung = r.Grade, Name = r.Label, Effect = PerkEffect(r.Id), Price = price, Unlocked = CallGate.Open(r, v.Grade),
                    Gate = FootprintGate(r.Id, c), Reason = PerkReason(r, v.Grade, price, c)
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

        /// <summary>Why a perk is not usable right now ("" when it is): the pilot's skill grade (<see cref="CallGate"/>), the satellite it needs, then the allocation price.</summary>
        private static string PerkReason(in CallRow r, int grade, int price, in FrontInputs c)
        {
            if (!CallGate.Open(r, grade)) return CallGate.LockWord(r);
            int k = PerkBird(r.Id);
            if (k >= 0 && c.HasBird != null)
            {
                if ((c.BirdsDown & c.BirdsLaunched & (1 << k)) != 0) return "SATELLITE LOST · RELAUNCH IT";
                if (!c.HasBird[k]) return "NO " + GeoSpace.Names[k] + " SATELLITE · LAUNCH ONE";
            }
            return price > c.Allocation ? "NEEDS " + price + " AP · YOU HAVE " + c.Allocation : "";
        }

        /// <summary>The local pilot's grade in a front's system (1 when the window passed none).</summary>
        internal static int GradeOf(in FrontInputs c, Front f) => c.Grades != null && (int)f < c.Grades.Length ? Math.Max(1, c.Grades[(int)f]) : 1;

        /// <summary>A build as a press offers it: name, cost, build time, and the refusal (WAITING FOR THE HOST before the first front snapshot).</summary>
        private static FrontOfferView Offer(Front f, ProgrammeId id, byte kind, FrontRow row, in QueuedCounts queued, in FrontInputs c) => new FrontOfferView
        {
            Id = id, Kind = kind, Name = FrontWords.Programme(id, kind), Cost = FrontRules.Cost(id, c.CostScale), BuildSeconds = (int)FrontRules.BuildSeconds(id),
            Refusal = IsWaiting(row) ? "WAITING FOR THE HOST" : Refusal(f, id, kind, row, queued, c)
        };

        /// <summary>The client's best reading of why a build cannot be queued (the ladder against the mirrored world and queue); "" when the world is unknown. The host judges again on the press.</summary>
        public static string Refusal(Front f, ProgrammeId id, byte kind, FrontRow row, in QueuedCounts queued, in FrontInputs c)
        {
            if (row.Queue.Count >= FrontRules.MaxQueue) return FrontWords.Refusal(FrontOutcome.QueueFull, f);
            return c.WorldKnown ? FrontWords.Refusal(FrontLadder.Check(f, id, kind, c.World, queued), f) : "";
        }

        /// <summary>
        /// What a front has built, read from the mirrors the client has (null = not received): pads and birds from OPS, data centers and trucks from CYBER (a packing truck still holds
        /// its data center, as on the host), camps, teams and held buildings from SOF, the FOB from its OPS row. False when the front's own mirror is missing.
        /// </summary>
        public static bool World(Front f, OpsStateData ops, CyberStateData cyber, SofStateData sof, float now, out FrontWorld w)
        {
            w = default;
            if (ops != null)
            {
                foreach (OpsSiteRow s in ops.Sites) if (s.State == SiteState.Up) w.PadsLive++;
                w.BirdsUp = (byte)(~ops.BirdsDown & ((1 << SpaceRules.BirdCount) - 1));
                foreach (OpsRow r in ops.Rows) w.FobUp |= r.Domain == OpDomain.Sof && r.Kind == OpKind.Fob && r.State == OpState.Done && r.EndsAt > now;
            }
            if (cyber != null)
                foreach (CyberAnchorRow a in cyber.Anchors)
                    if (a.Health != AnchorHealth.Down) { if (a.Kind == AnchorKind.DataCenter) w.DataCentersLive++; else w.TrucksLive++; }
            if (sof != null)
            {
                foreach (SofCampRow cr in sof.Camps) if (cr.Health != AnchorHealth.Down) w.CampsLive++;
                w.Teams = sof.Teams.Count;
                w.HasHeldBuilding = sof.Held.Count > 0;
            }
            return f == Front.Space ? ops != null : f == Front.Cyber ? cyber != null : sof != null;
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
                // A bird never launched is not in orbit (its card says NOT LAUNCHED); a launched one that is down stays on the plate as lost.
                bool has = c.HasBird != null && c.HasBird[k], lost = (c.BirdsDown & c.BirdsLaunched & (1 << k)) != 0;
                if (!has && !lost) continue;
                OrbitTrackView b = Station(k, true, GeoOf(c.Geo, k, true), c, v);
                string word = C2Orbit.BirdState(Kinds[k], c.SpaceLinked, has, c.Family, c.RadarSeconds, c.RadarUnavailable, out C2Tone tone);
                AvState state = C2Kit.StateOf(tone);
                if (lost) { word = "LOST · RELAUNCH IT"; state = AvState.Danger; }
                b.Alive = has && !lost; b.Overhead &= b.Alive; b.Callsign = Names[k]; b.State = word; b.Tone = state;
                b.Orbit = "SLOT " + b.Slot + " · " + b.Catalog;
                v.Birds.Add(b);
            }
            // The enemy constellation: only birds that are up (the mirror carries no launched mask for the enemy, so a down bit may be one never launched), parked the same way.
            for (int k = 0; c.EnemyKnown && k < Kinds.Length; k++)
            {
                if ((c.EnemyBirdsDown & (1 << k)) != 0) continue;
                OrbitTrackView b = Station(k, false, GeoOf(c.EnemyGeo, k, false), c, v);
                b.Alive = true; b.Callsign = "HOSTILE-" + (k + 1); b.State = "TRACKED"; b.Tone = AvState.Danger;
                b.Orbit = "SLOT " + b.Slot + " · " + b.Catalog;
                v.Birds.Add(b);
            }
            Satellites(v, c, SupportManager.Active?.OpsMirror.Sites);
        }

        // ---- SATELLITES tab -------------------------------------------------------------------------------------------

        /// <summary>
        /// The SATELLITES tab from what <see cref="Orbit"/> just built (own and hostile birds, airbases), the OPS mirror's sites and the SPACE queue: pads, the three
        /// fleet cards with their LAUNCH offer per kind, hostile birds and ASAT, map ghosts of queued pads, the coverage gap and the header counts.
        /// </summary>
        internal static void Satellites(FrontRoomView v, in FrontInputs c, IReadOnlyList<OpsSiteRow> sites)
        {
            v.Pads.Clear(); v.Fleet.Clear(); v.Hostile.Clear(); v.Ghosts.Clear();
            int alive = 0;
            foreach (OrbitTrackView b in v.Birds) if (b.Own && b.Alive) alive |= 1 << (int)b.Kind;
            Pads(v, c, sites, alive);
            QueuedCounts queued = FrontLadder.Queued(v.Row.Queue);
            v.BirdsUp = 0;
            for (int k = 0; k < Kinds.Length; k++)
            {
                bool found = TryOwn(v, k, out OrbitTrackView b), up = found && b.Alive;
                string state = FleetState(v, k, up, found && !b.Alive, b, out AvState tone);
                if (up) v.BirdsUp++;
                v.Fleet.Add(new FleetCardView
                {
                    Kind = k, Callsign = Names[k], State = state, Tone = tone, Up = up, Fuel = up ? b.Fuel : 0f, Covers = up ? Covers(v, b) : "", Perk = PerkLine(v, k),
                    CanRelocate = up && !b.Relocating && b.Fuel >= GeoBird.Cost(0.005f), // OrbitRoom.CanRelocate's rule
                    Launch = Offer(Front.Space, ProgrammeId.LaunchSatellite, (byte)k, v.Row, queued, c),
                });
            }
            v.BirdCap = Math.Min(FrontRules.BirdCap, FrontRules.BirdsPerPad * v.PadsUp);
            v.Asat = Offer(Front.Space, ProgrammeId.Asat, 0, v.Row, queued, c);
            foreach (OrbitTrackView b in v.Birds)
            {
                if (b.Own) continue;
                string near = NearestOwn(v, b);
                v.Hostile.Add(new HostileBirdView { Kind = (int)b.Kind, Word = GeoSpace.Names[(int)b.Kind], OverOwn = near.Length > 0, Nearest = near });
            }
            v.CoverageGap = "";
            // A gap needs a bird up and a known world: before the mirrors arrive, or with no satellite at all, every base would read as a false gap.
            for (int i = 0; v.BirdsUp > 0 && (c.SpaceLinked || c.WorldKnown) && i < v.Airbases.Count; i++)
            {
                MapAirbaseView a = v.Airbases[i];
                if (a.Side != AirbaseSide.Own || UnderOwn(v, a)) continue;
                v.CoverageGap = "GAP · " + a.Name + " NOT COVERED";
                break;
            }
        }

        /// <summary>
        /// Pads by slot: standing and lost ones from the OPS mirror, and each queued LAUNCH PAD (queue order) in the slot the host will build it into
        /// (<see cref="FrontLadder.PadSlot"/>; it takes over a lost card in that slot). A queued pad with a point is also a map ghost. Up pads link the birds on
        /// station (<paramref name="alive"/>, a kind mask) two each, in slot and kind order: the wire carries no per-pad link, so this words the capacity rule.
        /// </summary>
        private static void Pads(FrontRoomView v, in FrontInputs c, IReadOnlyList<OpsSiteRow> sites, int alive)
        {
            int present = 0, down = 0, taken = 0;
            for (int i = 0; sites != null && i < sites.Count; i++)
            {
                if (sites[i].Slot >= FrontRules.PadCap) continue;
                present |= 1 << sites[i].Slot;
                if (sites[i].State != SiteState.Up) down |= 1 << sites[i].Slot;
            }
            for (int i = 0; i < v.Queue.Count; i++)
            {
                FrontQueueRow q = v.Row.Queue[i];
                int slot = q.Id == ProgrammeId.UplinkSite ? FrontLadder.PadSlot(present, down, ref taken) : -1;
                if (slot < 0) continue;
                FrontQueueView qv = v.Queue[i];
                var p = new PadView
                {
                    Slot = slot, Name = SiteName(slot), Building = true, Row = qv.Row, Progress = qv.Progress, BuildLeft = qv.BuildLeft, Links = "",
                    U = q.HasPoint ? c.Frame.U(q.X) : -1f, V = q.HasPoint ? c.Frame.V(q.Z) : -1f, Place = q.HasPoint ? PlaceOf(q.X, q.Z, c) : "AUTO PLACE",
                };
                v.Pads.Add(p);
                if (q.HasPoint) v.Ghosts.Add(new MapGhostView { U = p.U, V = p.V, Label = p.Name + " · " + p.Place + " · " + (qv.Building ? FrontRules.Clock(qv.BuildLeft) : Pct(qv.Progress)) });
            }
            for (int i = 0; sites != null && i < sites.Count; i++)
            {
                OpsSiteRow s = sites[i];
                if (s.Slot >= FrontRules.PadCap || (taken & (1 << s.Slot)) != 0) continue;
                bool up = s.State == SiteState.Up;
                v.Pads.Add(new PadView { Slot = s.Slot, Name = SiteName(s.Slot), Up = up, Lost = !up, U = c.Frame.U(s.X), V = c.Frame.V(s.Z), Place = PlaceOf(s.X, s.Z, c), Links = "" });
            }
            v.Pads.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            v.PadsUp = 0;
            int kind = 0;
            for (int i = 0; i < v.Pads.Count; i++)
            {
                PadView p = v.Pads[i];
                if (!p.Up) continue;
                v.PadsUp++;
                string links = "";
                for (int n = 0; n < FrontRules.BirdsPerPad; n++)
                {
                    while (kind < Kinds.Length && (alive & (1 << kind)) == 0) kind++;
                    if (kind >= Kinds.Length) break;
                    links += (links.Length > 0 ? ", " : "") + Names[kind++];
                }
                p.Links = "LINKS " + (links.Length > 0 ? links : "NONE");
                v.Pads[i] = p;
            }
        }

        private static string Pct(float share) => (int)(Mathf.Clamp01(share) * 100f) + " %";

        private static bool TryOwn(FrontRoomView v, int k, out OrbitTrackView b)
        {
            foreach (OrbitTrackView t in v.Birds) if (t.Own && (int)t.Kind == k) { b = t; return true; }
            b = default;
            return false;
        }

        /// <summary>A fleet card's state: on station or burning; else its launch row (building, funding, or waiting for a pad); else lost or never launched.</summary>
        private static string FleetState(FrontRoomView v, int k, bool up, bool lost, in OrbitTrackView b, out AvState tone)
        {
            if (up)
            {
                tone = b.Relocating ? AvState.Caution : AvState.Ready;
                return b.Relocating ? "BURNING " + FrontRules.Clock(b.EtaSeconds) : "ON STATION";
            }
            for (int i = 0; i < v.Queue.Count; i++)
            {
                FrontQueueRow q = v.Row.Queue[i];
                if (q.Id != ProgrammeId.LaunchSatellite || q.Kind != k) continue;
                FrontQueueView qv = v.Queue[i];
                tone = !qv.Building && q.Waiting ? AvState.Caution : AvState.Info;
                return qv.Building ? "LAUNCHING " + FrontRules.Clock(qv.BuildLeft) : q.Waiting ? "WAITS FOR A PAD" : "QUEUED · " + Pct(qv.Progress);
            }
            tone = lost ? AvState.Danger : AvState.Inert;
            return lost ? "LOST" : "NOT LAUNCHED";
        }

        /// <summary>"COVERS FELDSPAR, OPAL" (+n past three): the airbases under a bird's footprint by the map's rule (<see cref="GeoBird.Distance"/> within its reach), or "COVERS NOTHING".</summary>
        private static string Covers(FrontRoomView v, in OrbitTrackView b)
        {
            string names = "";
            int n = 0;
            foreach (MapAirbaseView a in v.Airbases)
            {
                if (GeoBird.Distance(b.U, b.V, a.U, a.V) > b.Radius) continue;
                if (n++ < 3) names += (names.Length > 0 ? ", " : "") + a.Name;
            }
            return n == 0 ? "COVERS NOTHING" : "COVERS " + names + (n > 3 ? " +" + (n - 3) : "");
        }

        /// <summary>The nearest of our airbases under a (hostile) bird's footprint, "" when none.</summary>
        private static string NearestOwn(FrontRoomView v, in OrbitTrackView b)
        {
            string best = "";
            float bestD = b.Radius;
            foreach (MapAirbaseView a in v.Airbases)
            {
                if (a.Side != AirbaseSide.Own) continue;
                float d = GeoBird.Distance(b.U, b.V, a.U, a.V);
                if (d <= bestD) { bestD = d; best = a.Name; }
            }
            return best;
        }

        private static bool UnderOwn(FrontRoomView v, in MapAirbaseView a)
        {
            foreach (OrbitTrackView b in v.Birds) if (b.Own && b.Alive && GeoBird.Distance(b.U, b.V, a.U, a.V) <= b.Radius) return true;
            return false;
        }

        /// <summary>The perk a bird kind enables, worded READY or with its lock reason: the first ready one of that kind's perks, else its lowest.</summary>
        private static string PerkLine(FrontRoomView v, int k)
        {
            int pick = -1;
            for (int i = 0; i < v.Perks.Count; i++)
            {
                if (PerkBird(v.Perks[i].Id) != k) continue;
                if (v.Perks[i].Reason.Length == 0) { pick = i; break; }
                if (pick < 0) pick = i;
            }
            if (pick < 0) return "";
            FrontPerkView p = v.Perks[pick];
            return p.Name + (p.Reason.Length == 0 ? " READY" : p.Reason.StartsWith("NEEDS", StringComparison.Ordinal) ? " " + p.Reason : " · " + p.Reason);
        }

        /// <summary>Where a pad stands, worded: the nearest airbase's short name within twice the pick radius, else its grid.</summary>
        private static string PlaceOf(float x, float z, in FrontInputs c)
        {
            string best = null;
            float bestD = 2f * FrontRules.PadPickMetres;
            for (int i = 0; c.Airbases != null && i < c.Airbases.Count; i++)
            {
                float d = Dist(c.Airbases[i].X, c.Airbases[i].Z, x, z);
                if (d <= bestD) { bestD = d; best = c.Airbases[i].Name; }
            }
            return best != null ? ShortName(best) : "GRID " + OpsKit.Grid(x, z);
        }

        internal static string SiteName(int slot) => "PAD-" + (Math.Min(Math.Max(slot, 0), FrontRules.PadCap - 1) + 1);

        /// <summary>The uplink markers: the faction's own sites (OPS mirror) with their standing and what losing each costs, then the enemy sites the CYBER fog reveals. No site, no marker: nothing is invented.</summary>
        private static void Sites(FrontRoomView v, in FrontInputs c)
        {
            SupportManager m = SupportManager.Active;
            IReadOnlyList<BoscaliSummer.Modules.Support.Domain.Ops.OpsSiteRow> own = m?.OpsMirror.Sites;
            IReadOnlyList<CyberNodeRow> foe = m != null && m.CyberMirror.Known ? m.CyberMirror.State.Nodes : null;
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
                        : rebuilding ? "SPACE " + space + " UNTIL REBUILT" : "SPACE " + space + " · QUEUE A LAUNCH PAD",
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
            n.Nodes.Clear(); n.Anchors.Clear(); n.Roster.Clear();
            Infrastructure(v, c, known ? s : null);
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
                n.HopLine = CyberNetWords.IntrusionLine(mine, s, now);
                n.HopFraction = CyberNetWords.HopProgress(mine, now);
                n.HopLeft = n.Hopping ? (int)Math.Ceiling(Math.Max(0f, mine.HopEndsAt - now)) : 0;
                NodeKind hopKind = n.Hopping && TryNode(s, mine.HopTarget, out CyberNodeRow hopRow) ? hopRow.Kind : NodeKind.Radar;
                float rate = ((n.Hopping ? CyberRules.HopTraceRate : 0f) + CyberRules.HeldTraceRate * myHeld) * (s.DataCenterUp ? CyberRules.DataCenterTraceFactor : 1f);
                n.EstTraceSeconds = rate > 0f ? (int)Math.Ceiling((CyberRules.TraceMax - mine.Trace) / rate) : -1;
                n.Stale = (n.Hopping || myHeld > 0) && n.AgeSeconds > StaleAfterSeconds;
                n.Session = "SESSION " + (n.Operator.Length == 0 ? "YOURS" : n.Operator) + " · " + myHeld + " HELD";
                n.Prompt = (n.Operator.Length == 0 ? "you" : n.Operator.ToLowerInvariant()) + "@" + TruckTag(mine.Truck).ToLowerInvariant() + ":~$";
            }
            else n.HopLine = "IDLE · NO HOP RUNNING"; // the dial already says NO INTRUSION
            n.Infocon = (byte)(locked ? 2 : anyDown ? 3 : 5);
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
                string clock = qv.Building ? FrontRules.Clock(qv.BuildLeft) : Pct(qv.Progress);
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

        /// <summary>CAMP-1 is host camp slot 0.</summary>
        internal static string CampName(int slot) => "CAMP-" + (slot + 1);

        private static readonly int[] homes = new int[SofRules.MaxTeams];

        /// <summary>
        /// The SHADOW room (spec 7) from the SOF mirror (<paramref name="s"/>, null or unknown before the host speaks) and the SOF front row in <paramref name="v"/>:
        /// the TEAMS &amp; BASES tab (camps by host slot with the queued ones, the FOB, a card per team with its live verbs, each queued TRAIN row as a card), the map
        /// (camps, ghosts, held buildings, targets, teams, enemies) and the TARGETS tab (teams in the field, the target list, the aim's SEND card, hostiles near us).
        /// Picks follow ruling D-R5: a team or target that left, or a target id the host reused for another thing, is dropped with the reason, never swapped; a new
        /// aim (target or RECON point) pre-picks the nearest team that can go (spec 7.3); a mirror that stops running drops every pick.
        /// </summary>
        public static void Shadow(FrontRoomView v, in FrontInputs c, SofStateData s, bool known, CyberStateData cyber, int selectedSlot, int selectedTarget)
        {
            ShadowRoomView r = v.Shadow;
            MapFrame frame = c.Frame;
            float now = c.Now;
            r.Teams.Clear(); r.Targets.Clear(); r.Held.Clear(); r.Enemies.Clear(); r.EnemyMark.Clear(); r.Missions.Clear(); r.Crew.Clear(); r.Field.Clear();
            r.CanSweep = false; r.SweepLabel = "SWEEP"; r.QrfLine = ""; r.QrfTone = AvState.Inert; r.Near = 0;
            bool active = known && s != null && s.Active;
            r.Active = active;
            r.SelectedSlot = -1; r.SelectedTargetId = 0;
            r.TapLine = "NO TAP RUNNING"; r.TeamCap = active ? s.TeamCap : 0; r.TeamsUp = 0;
            r.FieldNote = ""; r.SendWhy = ""; r.GoIn = ""; r.Helo = "";
            r.FileTab = r.FileA = r.FileB = r.FileC = r.FileD = "";
            if (!known) { r.History.Clear(); r.LastSeq = 0; }
            if (active) CampRules.Homes(s.Camps, s.Teams, homes);
            else for (int i = 0; i < homes.Length; i++) homes[i] = -1;
            Bases(v, c, active ? s : null);
            if (!active)
            {
                // Bug 9: no pick and no RECON point outlives a mirror that stopped running (a blink before the host speaks keeps them: nothing is written back then).
                r.Meta = known ? NoCamp : "WAITING FOR THE HOST";
                r.Reason = ""; r.HasPoint = false; r.AimSeen = 0; r.PickedId = 0;
                r.FieldNote = known ? "NO TEAM YET · BUILD A CAMP, THEN TRAIN ONE ON TEAMS & BASES" : "WAITING FOR THE HOST";
                return;
            }
            foreach (SofHeldRow h in s.Held)
                r.Held.Add(new SofHeldView { Id = h.Id, X = frame.U(h.X), Y = frame.V(h.Z), SecondsLeft = Math.Max(0f, h.Until - now), Name = "BUILDING #" + h.Id });
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
            r.Near = near;
            r.CanSweep = near > 0 && !rolling && !refit;
            r.SweepLabel = rolling ? "QRF " + (int)Math.Ceiling(s.SweepEndsAt - now) + " S" : refit ? "SWEEP " + FrontKit.Clock(s.SweepReadyAt - now) : "SWEEP · " + SofRules.SweepCost + " AP";
            r.QrfLine = rolling ? "QRF ROLLING · IT REACHES THEM IN " + (int)Math.Ceiling(s.SweepEndsAt - now) + " S"
                : hunting > 0 ? "QRF HUNTING " + hunting + " TEAM" + (hunting == 1 ? "" : "S") + " · THEIR EXPOSURE CLIMBS"
                : near > 0 ? "DETECTED WITHIN " + (int)(SofRules.DetectMetres / 1000f) + " KM OF OUR SITES" + (refit ? " · QRF REFITS " + FrontKit.Clock(s.SweepReadyAt - now) : " · SWEEP SENDS THE QRF")
                : refit ? "QUIET · QRF REFITS " + FrontKit.Clock(s.SweepReadyAt - now) : "QUIET · SWEEP HUNTS ENEMY TEAMS NEAR YOUR CAMPS";
            r.QrfTone = near > 0 || hunting > 0 ? AvState.Danger : AvState.Inert;

            // Picks (ruling D-R5). A picked team or target that left the snapshot is cleared with the reason, never swapped for another; a target id the host reused
            // (another kind, or far from where the pick last saw it: ids are recycled once a target is gone) is dropped too. Only "nothing picked yet" (-1) takes the first team.
            string gone = "";
            if (selectedSlot >= 0 && !s.Teams.Exists(t => t.Slot == selectedSlot)) { gone = SofRules.Callsign(selectedSlot) + " LEFT THE ROSTER"; selectedSlot = Gone; }
            else if (selectedSlot == -1 && s.Teams.Count > 0) selectedSlot = s.Teams[0].Slot;
            SofTargetRow tgt = default;
            bool haveTarget = false;
            if (selectedTarget != 0)
            {
                foreach (SofTargetRow t in s.Targets) if (t.Id == selectedTarget) { tgt = t; haveTarget = true; break; }
                if (!haveTarget) gone += (gone.Length > 0 ? " · " : "") + "T-" + selectedTarget + " IS NO LONGER REVEALED";
                else if (r.PickedId == tgt.Id && (tgt.Kind != r.PickedKind || tgt.Sub != r.PickedSub
                    || SofRules.Distance(tgt.X, tgt.Z, r.PickedX, r.PickedZ) > (tgt.Kind == TargetKind.Ground ? ReusedGroundMetres : ReusedSiteMetres)))
                {
                    gone += (gone.Length > 0 ? " · " : "") + "T-" + tgt.Id + " IS ANOTHER TARGET NOW";
                    haveTarget = false;
                }
                if (!haveTarget) selectedTarget = 0;
            }
            if (haveTarget) { r.PickedId = tgt.Id; r.PickedKind = tgt.Kind; r.PickedSub = tgt.Sub; r.PickedX = tgt.X; r.PickedZ = tgt.Z; r.HasPoint = false; }
            else r.PickedId = 0;
            if (gone.Length > 0) r.Reason = gone;
            bool point = !haveTarget && r.HasPoint;
            int aim = haveTarget ? tgt.Id : point ? r.PointId : 0;
            float ax = haveTarget ? tgt.X : frame.X(r.PointU), az = haveTarget ? tgt.Z : frame.Z(r.PointV);
            if (aim != r.AimSeen)
            {
                r.AimSeen = aim;
                int pre = aim != 0 ? SofAim.Nearest(s.Teams, ax, az) : -1;
                if (pre >= 0) selectedSlot = pre;
            }
            r.SelectedSlot = selectedSlot; r.SelectedTargetId = selectedTarget;

            // Teams by slot: every team on the roster, whatever the cap now (bug 7), and a queued TRAIN row in each free slot after them in queue order. The ones out
            // of camp also go in the TARGETS tab's field strip; every one not lost is a team the SEND card can pick.
            SofTeamRow cur = default;
            bool haveTeam = false, curLocked = false;
            string readyList = "";
            int qi = 0;
            for (int slot = 0; slot < SofRules.MaxTeams; slot++)
            {
                SofTeamRow t = default;
                bool present = false;
                foreach (SofTeamRow x in s.Teams) if (x.Slot == slot) { t = x; present = true; break; }
                if (!present)
                {
                    while (qi < v.Queue.Count && v.Row.Queue[qi].Id != ProgrammeId.TrainTeam) qi++;
                    if (qi >= v.Queue.Count) continue;
                    FrontQueueView q = v.Queue[qi++];
                    r.Teams.Add(new SofTeamView
                    {
                        Slot = slot, Training = true, Building = q.Building, Row = q.Row, Progress = q.Progress, BuildLeft = q.BuildLeft, Callsign = SofRules.Callsign(slot), Codename = TeamCodename(slot),
                        State = q.Building ? "TRAINING " + FrontRules.Clock(q.BuildLeft) : "FUNDING " + Pct(q.Progress), Mission = "", Flags = "", Eta = "", Control = "", Stamp = "", Home = "", VerbNote = ""
                    });
                    continue;
                }
                bool locked = !t.Mine && !string.IsNullOrEmpty(t.Controller);
                AvState tone = t.State == TeamState.Pinned || t.State == TeamState.Lost ? AvState.Danger : t.Exposure >= 70 || (t.State != TeamState.Raising && t.Ammo < SofDesk.AmmoPerMission) || t.Hunted ? AvState.Caution
                    : t.State == TeamState.OnSite ? AvState.Info : AvState.Ready;
                bool afield = t.State == TeamState.Moving || t.State == TeamState.OnSite;
                string mission = t.Mission == MissionKind.None ? (t.State == TeamState.Ready ? "NO MISSION · SEND IT FROM TARGETS" : "NO MISSION")
                    : SofWords.Kind(t.Mission) + (t.TargetId > 0 ? " T-" + t.TargetId : "") + " · " + OpName(slot, t.Mission, t.TargetId) + (afield ? " · ODDS " + t.Odds + " %" : "");
                bool helo = t.Carried || t.Insert == Insertion.Helicopter;
                if (t.State != TeamState.Lost) r.TeamsUp++;
                r.Teams.Add(new SofTeamView
                {
                    Slot = slot, Present = true, Selected = slot == selectedSlot, Callsign = SofRules.Callsign(slot), State = SofWords.State(t.State), Tone = tone,
                    Codename = TeamCodename(slot), Control = SofPageWords.Control(t), Stamp = StampWord(t), Locked = locked, Home = HomeWord(s, t, c),
                    Mission = mission, Flags = (helo ? "HELO" : "GROUND") + (t.Push ? " · PUSH" : "") + (t.Hold ? " · HOLD" : "") + (t.Exploit ? " · EXPLOIT" : "") + (t.Wounded ? " · WIA" : "") + (t.Hunted ? " · QRF ON US" : ""),
                    Eta = TeamEta(t, now), X = frame.U(t.X), Y = frame.V(t.Z), DestX = frame.U(t.DestX), DestY = frame.V(t.DestZ), TargetX = frame.U(t.TargetX), TargetY = frame.V(t.TargetZ),
                    HasDest = t.HasDest, Lasing = t.Lasing, Wounded = t.Wounded, Helicopter = helo, Exposure = t.Exposure, Ammo = t.Ammo, Odds = t.Odds,
                    VerbOn = locked ? (byte)0 : Verbs(s, t), VerbOnLatched = (byte)((t.Push ? 1 : 0) | (t.Hold ? 2 : 0) | (t.LiftWaiting ? 8 : 0)),
                    VerbNote = locked ? "VIEW ONLY · " + SofPageWords.Control(t) : VerbNote(t, now),
                });
                if (slot == selectedSlot) { cur = t; haveTeam = true; curLocked = locked; }
                string call = SofRules.Callsign(slot);
                if (t.State == TeamState.Moving || t.State == TeamState.OnSite || t.State == TeamState.Returning || t.State == TeamState.Pinned)
                    r.Field.Add(new SofFieldView
                    {
                        Slot = slot, Tone = tone,
                        Left = call + " " + TeamCodename(slot) + " › " + (t.Mission == MissionKind.None ? "HOME" : (t.TargetId > 0 ? "T-" + t.TargetId + " " : "") + SofWords.Kind(t.Mission)),
                        Right = (t.State == TeamState.Returning ? "EXFIL · " : t.State == TeamState.Pinned ? "PINNED · " : "") + TeamEta(t, now) + " · EXP " + t.Exposure + " %",
                    });
                bool go = SofAim.CanGo(t);
                if (go) readyList += (readyList.Length > 0 ? ", " : "") + call;
                if (t.State == TeamState.Lost || aim == 0) continue;
                float d = SofRules.Distance(t.X, t.Z, ax, az);
                string why = locked ? "CTRL " + t.Controller : t.State != TeamState.Ready ? SofWords.State(t.State) : "LOW AMMO";
                r.Crew.Add(new SofCrewView
                {
                    Slot = slot, CanGo = go, Word = call + " · " + (go ? KmWord(d) : why),
                    Help = go ? call + " can go now: " + KmWord(d) + " from the aim, about " + FrontKit.Clock(d / SofRules.SpeedMetresPerSecond) + " on foot." + (t.Carried ? " It rides a helicopter: +20 odds." : "")
                        : call + " cannot go now: " + why.ToLowerInvariant() + ". Picking it shows why on SEND.",
                });
            }
            Remember(r, s);
            r.FieldNote = r.Field.Count > 0 ? "" : s.Teams.Count == 0 ? (r.CampsUp > 0 ? "NO TEAM YET · TRAIN ONE ON TEAMS & BASES" : "NO TEAM YET · BUILD A CAMP, THEN TRAIN ONE ON TEAMS & BASES")
                : "NO TEAM OUT · " + (readyList.Length > 0 ? readyList + " READY TO GO" : "NONE READY NOW");

            foreach (SofTargetRow t in s.Targets)
            {
                if (r.Targets.Count >= SofWire.MaxTargets) break;
                string on = OnIt(s, t.Id);
                r.Targets.Add(new SofTargetView
                {
                    Id = t.Id, Kind = t.Kind, Sub = t.Sub, Filter = SofAim.Filter(t.Kind, t.Sub), X = frame.U(t.X), Y = frame.V(t.Z), Exploit = t.Exploit, Resisted = t.Resisted,
                    Selected = t.Id == selectedTarget, Name = TargetName(t), Grid = OpsKit.Grid(t.X, t.Z),
                    Tag = on.Length > 0 ? on : t.Resisted ? "RESISTED" : (t.Exploit ? "EXPLOIT · " : "") + SofWords.Kind(BestMission(t)),
                    TagTone = on.Length > 0 ? AvState.Ready : t.Resisted ? AvState.Inert : t.Exploit ? AvState.Caution : AvState.Info,
                });
            }

            if (aim != 0)
            {
                // The aim's SEND card (spec 7.3): every mission, lit when the aim takes it, priced, and with its odds for the picked team (an estimate: zero armour, the
                // ring judged at the aim); why that team cannot go now ("" when it can: the room adds the price against the pilot's allocation); how it goes in (D-R2).
                bool ring = cyber != null && cyber.Nodes.Exists(nd => nd.Held && SofRules.Distance(nd.X, nd.Z, ax, az) <= SofRules.RingBoostMetres);
                foreach (MissionKind kind in Missions)
                {
                    bool recon = kind == MissionKind.Recon, valid = recon || (haveTarget && SofRules.Valid(kind, tgt.Kind, tgt.Sub)), exploit = !recon && haveTarget && tgt.Exploit;
                    r.Missions.Add(new SofMissionView
                    {
                        Kind = kind, Name = kind == MissionKind.Tap ? "TAP" : SofWords.Kind(kind), Ap = SofRules.CostOf(kind, exploit), Valid = valid, Exploit = exploit,
                        Best = haveTarget ? kind == BestMission(tgt) : recon,
                        Odds = !valid ? "" : kind == MissionKind.Lase ? "NO ROLL" : "EST ~" + SofRules.Odds(haveTeam ? cur.Exposure : 0f, 0, haveTeam && cur.Carried, exploit, ring) + " %",
                        Note = valid ? Does(kind) : point ? "A MAP POINT TAKES ONLY RECON" : "NEEDS " + NeedWord(kind),
                    });
                }
                string call = haveTeam ? SofRules.Callsign(cur.Slot) : "";
                int alt = SofAim.Nearest(s.Teams, ax, az);
                string other = alt >= 0 && alt != selectedSlot ? " · " + SofRules.Callsign(alt) + " CAN GO: PICK IT ABOVE" : "";
                bool ready = haveTeam && cur.State == TeamState.Ready;
                r.SendWhy = !haveTeam ? (s.Teams.Count == 0 ? (r.CampsUp > 0 ? "NO TEAM YET · TRAIN ONE ON TEAMS & BASES" : "NO TEAM YET · BUILD A CAMP, THEN TRAIN ONE")
                        : alt >= 0 ? "PICK A TEAM ABOVE" : "NO TEAM CAN GO NOW")
                    : curLocked ? "VIEW ONLY · " + call + " IS COMMANDED BY " + cur.Controller + other
                    : !ready ? call + " IS " + SofWords.State(cur.State) + (other.Length > 0 ? other : " · NO TEAM IS READY")
                    : cur.Ammo + 0.01f < SofDesk.AmmoPerMission ? call + " NEEDS " + (int)SofDesk.AmmoPerMission + " % AMMO · IT REARMS AT CAMP" + other
                    : "";
                if (haveTeam)
                {
                    float d = SofRules.Distance(cur.X, cur.Z, ax, az), sp = SofRules.SpeedMetresPerSecond;
                    r.GoIn = cur.Carried ? "HELO · " + call + " RIDES A PILOT'S HELICOPTER · +20 ODDS"
                        : "GROUND · " + KmWord(d) + " · ETA " + FrontKit.Clock(d / sp) + " · " + FrontKit.Clock(d / (sp * SofRules.PushSpeedFactor)) + " WITH PUSH";
                    r.Helo = cur.Carried ? "THE PILOT FLIES IT IN" : cur.LiftWaiting ? "HELO ASKED · A PILOT LANDING BESIDE " + call + " CARRIES IT: +20 ODDS"
                        : "HELO · LIFT ON TEAMS & BASES: A PILOT CARRIES IT, +20 ODDS";
                }
            }

            // The picked target's file card, or the RECON point's.
            if (haveTarget)
            {
                r.FileTab = "FILE T-" + tgt.Id;
                r.FileA = SofWords.Target(tgt.Kind, tgt.Sub, tgt.Id).Replace(" #" + tgt.Id, "") + (tgt.Exploit ? " · EXPLOIT" : tgt.Resisted ? " · RESISTED" : "");
                r.FileB = "GRID " + OpsKit.Grid(tgt.X, tgt.Z) + " · BEST " + SofWords.Kind(BestMission(tgt));
                r.FileC = TaskedOn(s, tgt.Id);
                r.FileD = tgt.Exploit ? "EXPLOIT · CUTS A TARGET MISSION'S COST" : tgt.Resisted ? "RESISTED · SPACE BEATS SOF" : "";
            }
            else if (point)
            {
                r.FileTab = "RECON POINT";
                r.FileA = "OPEN GROUND";
                r.FileB = "GRID " + OpsKit.Grid(ax, az);
                r.FileC = "ONLY RECON TAKES A POINT";
                r.FileD = Does(MissionKind.Recon);
            }

            r.TapLine = s.TapUntil > now ? "TAP · " + s.TapIntrusions + " INTRUSIONS SHARED · " + FrontKit.Clock(s.TapUntil - now) : "NO TAP RUNNING";
            r.Meta = r.TeamsUp + " TEAMS OUT · " + s.Enemies.Count + " ENEMY TEAMS SEEN";
        }

        /// <summary>A target id kept by a pick moved this far since the last build: the host reused the id for another target (a contact drives, a site never moves).</summary>
        private const float ReusedGroundMetres = 5000f, ReusedSiteMetres = 300f;

        private static string KmWord(float metres) =>
            (metres < 10000f ? (metres / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : Mathf.RoundToInt(metres / 1000f).ToString(System.Globalization.CultureInfo.InvariantCulture)) + " KM";

        /// <summary>What a mission does, in a few words (the SEND card's line under the missions).</summary>
        private static string Does(MissionKind k) =>
            k == MissionKind.Recon ? "REVEALS " + (SofRules.ReconRadius / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KM ROUND IT FOR " + FrontKit.Clock(SofRules.ReconRevealSeconds)
            : k == MissionKind.Lase ? "MARKS IT FOR YOUR STRIKES UNTIL STOPPED"
            : k == MissionKind.Sabotage ? "KNOCKS THE SITE OUT"
            : k == MissionKind.Seize ? "HOLDS IT " + FrontKit.Clock(SofRules.HeldSeconds) + " · A FOB SPOT"
            : "FEEDS CYBER THEIR NETWORK FOR " + FrontKit.Clock(SofRules.TapSeconds);

        /// <summary>The list row's tag for a target a team of ours works: "A-1 ON IT", "A-1 +1 ON IT"; "" when none.</summary>
        private static string OnIt(SofStateData s, int targetId)
        {
            string first = "";
            int n = 0;
            foreach (SofTeamRow t in s.Teams)
                if (t.TargetId == targetId && t.Mission != MissionKind.None && t.State != TeamState.Lost) { if (n++ == 0) first = SofRules.Callsign(t.Slot); }
            return n == 0 ? "" : first + (n > 1 ? " +" + (n - 1) : "") + " ON IT";
        }

        /// <summary>
        /// Camps by host slot (the mirror lists them in slot order: standing, damaged or lost; each queued CAMP row in the slot the host will build it into, taking over a
        /// lost card there), the FOB card (ruling D-R3), and a map ghost for each queued camp or FOB with a point. <paramref name="s"/> null: only the queue is known.
        /// </summary>
        private static void Bases(FrontRoomView v, in FrontInputs c, SofStateData s)
        {
            ShadowRoomView r = v.Shadow;
            MapFrame f = c.Frame;
            r.Camps.Clear(); r.Ghosts.Clear(); r.CampsUp = 0;
            int n = s != null ? Math.Min(s.Camps.Count, CampRules.MaxCamps) : 0, present = 0, down = 0, taken = 0;
            for (int i = 0; i < n; i++) { present |= 1 << i; if (s.Camps[i].Health == AnchorHealth.Down) down |= 1 << i; }
            var fob = new SofFobView();
            for (int i = 0; i < v.Queue.Count; i++)
            {
                FrontQueueRow q = v.Row.Queue[i];
                FrontQueueView qv = v.Queue[i];
                string clock = qv.Building ? FrontRules.Clock(qv.BuildLeft) : Pct(qv.Progress);
                if (q.Id == ProgrammeId.Fob)
                {
                    fob = new SofFobView { Queued = true, Row = qv.Row, Progress = qv.Progress, State = qv.Building ? "BUILDING " + clock : "FUNDING " + clock, Tone = qv.Building ? AvState.Caution : AvState.Info,
                        Note = qv.Building ? "UP IN " + clock + " · REARM + REFUEL BY THE BUILDING" : "FRONT FUNDS PAY IT · CHIP IN SPEEDS IT UP" };
                    if (q.HasPoint) r.Ghosts.Add(new MapGhostView { U = f.U(q.X), V = f.V(q.Z), Kind = 1, Label = "FOB · " + clock });
                    continue;
                }
                if (q.Id != ProgrammeId.Camp) continue;
                int slot = FrontLadder.AnchorSlot(present, down, ref taken, CampRules.MaxCamps);
                if (slot < 0) continue;
                var cv = new SofCampView
                {
                    Slot = slot, Name = CampName(slot), State = InfraState.Building, X = q.HasPoint ? f.U(q.X) : -1f, Y = q.HasPoint ? f.V(q.Z) : -1f,
                    Place = q.HasPoint ? PlaceOf(q.X, q.Z, c) : "AUTO PLACE", Holds = "", Row = qv.Row, Progress = qv.Progress, BuildLeft = qv.BuildLeft
                };
                r.Camps.Add(cv);
                if (q.HasPoint) r.Ghosts.Add(new MapGhostView { U = cv.X, V = cv.Y, Label = cv.Name + " · " + cv.Place + " · " + clock });
            }
            for (int i = 0; i < n; i++)
            {
                if ((taken & (1 << i)) != 0) continue;
                SofCampRow cr = s.Camps[i];
                bool lost = cr.Health == AnchorHealth.Down;
                if (!lost) r.CampsUp++;
                r.Camps.Add(new SofCampView
                {
                    Slot = i, Name = CampName(i), Health = cr.Health, State = lost ? InfraState.Lost : cr.Health == AnchorHealth.Damaged ? InfraState.Damaged : InfraState.Up,
                    X = f.U(cr.X), Y = f.V(cr.Z), Place = PlaceOf(cr.X, cr.Z, c), Holds = lost ? "" : Holds(i)
                });
            }
            r.Camps.Sort((a, b) => a.Slot.CompareTo(b.Slot));

            if (!fob.Queued)
            {
                bool up = c.WorldKnown && c.World.FobUp, held = s != null && s.Held.Count > 0;
                fob = up ? new SofFobView { Up = true, Tone = AvState.Ready, State = c.FobUntil > c.Now ? "UP · " + FrontRules.Clock(c.FobUntil - c.Now) + " LEFT" : "UP",
                        Note = "REARM + REFUEL BY THE HELD BUILDING · ROOM FOR " + FrontRules.TeamsPerBase + " MORE TEAMS" }
                    : held ? new SofFobView { Tone = AvState.Inert, State = "NOT BUILT", Note = "PLACE IT ON A BUILDING YOUR TEAMS HOLD" }
                    : new SofFobView { Tone = AvState.Caution, State = "NEEDS A SEIZED BUILDING", Note = "SEIZE A BUILDING FROM TARGETS, THEN PLACE IT THERE" };
            }
            r.Fob = fob;
        }

        /// <summary>"HOLDS A-1, A-2 · FULL", "HOLDS A-1 · ROOM FOR 1", "ROOM FOR 2 TEAMS": the teams that call camp slot <paramref name="camp"/> home (<see cref="homes"/>).</summary>
        private static string Holds(int camp)
        {
            string who = "";
            int k = 0;
            for (int slot = 0; slot < homes.Length; slot++)
                if (homes[slot] == camp) { who += (who.Length > 0 ? ", " : "") + SofRules.Callsign(slot); k++; }
            int room = FrontRules.TeamsPerBase - k;
            return k == 0 ? "ROOM FOR " + room + " TEAMS" : "HOLDS " + who + (room <= 0 ? " · FULL" : " · ROOM FOR " + room);
        }

        /// <summary>Ruling D-R4: "AT CAMP-1" for a ready team standing by its home camp, "HOME CAMP-1" out in the field, the FOB, or NO CAMP.</summary>
        private static string HomeWord(SofStateData s, in SofTeamRow t, in FrontInputs c)
        {
            if (t.State == TeamState.Lost) return "NO CONTACT";
            int h = t.Slot < homes.Length ? homes[t.Slot] : -1;
            if (h < 0 || h >= s.Camps.Count) return c.WorldKnown && c.World.FobUp ? "HOME FOB" : "NO CAMP";
            bool at = t.State == TeamState.Ready && SofRules.Distance(t.X, t.Z, s.Camps[h].X, s.Camps[h].Z) <= SofDesk.BaseRadius;
            return (at ? "AT " : "HOME ") + CampName(h);
        }

        /// <summary>
        /// The verbs the host would take from this team now (bits: 1 PUSH, 2 HOLD, 4 EXFIL, 8 LIFT; SofDesk.Run's rules): PUSH while it marches, HOLD on the march or on
        /// site, EXFIL on the march (home again, by the nearest base), on site or when it stands away from every camp and held building, LIFT while ready on the ground.
        /// Pinned, training, wounded and lost: none.
        /// </summary>
        private static byte Verbs(SofStateData s, in SofTeamRow t)
        {
            bool moving = t.State == TeamState.Moving || t.State == TeamState.Returning;
            int on = (moving ? 1 : 0) | (moving || t.State == TeamState.OnSite ? 2 : 0) | (t.State == TeamState.Ready && !t.Carried ? 8 : 0);
            if (!t.Carried && (moving || t.State == TeamState.OnSite || (t.State == TeamState.Ready && Away(s, t)))) on |= 4;
            return (byte)on;
        }

        /// <summary>Farther than 50 m from every standing camp and held building: an EXFIL has somewhere to go (the host refuses one at home).</summary>
        private static bool Away(SofStateData s, in SofTeamRow t)
        {
            foreach (SofCampRow cr in s.Camps) if (cr.Health != AnchorHealth.Down && SofRules.Distance(t.X, t.Z, cr.X, cr.Z) <= 50f) return false;
            foreach (SofHeldRow h in s.Held) if (SofRules.Distance(t.X, t.Z, h.X, h.Z) <= 50f) return false;
            return true;
        }

        /// <summary>What the team's verbs mean now, or why it has none (ruling D-R2: LIFT is the helicopter insertion, a pilot picks the team up and it gains +20 odds).</summary>
        private static string VerbNote(in SofTeamRow t, float now)
        {
            if (t.Carried) return "IN A HELO · +20 ODDS";
            switch (t.State)
            {
                case TeamState.Raising: return "TRAINING · READY IN " + SpaceRules.Clock(t.EndsAt - now);
                case TeamState.Recovering: return "WIA · RECOVERS IN " + SpaceRules.Clock(t.EndsAt - now);
                case TeamState.Pinned: return "PINNED · TAKES NO ORDERS";
                case TeamState.Lost: return "LOST · NO CONTACT";
                case TeamState.OnSite: return "HOLD HIDES · EXFIL ABANDONS";
                case TeamState.Moving: return "PUSH 1.5x FASTER · HOLD HIDES";
                case TeamState.Returning: return "HEADING HOME · PUSH SPEEDS";
                default: return t.LiftWaiting ? "HELO ASKED · LAND BESIDE IT" : "LIFT = HELO PICKUP · +20 ODDS";
            }
        }
    }
}
