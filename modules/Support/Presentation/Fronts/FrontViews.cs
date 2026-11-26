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
        public int[] EnemyTrackIds;
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
    internal static partial class FrontViews
    {
        /// <summary>The row painted before the host has said anything: shared, never edited, recognised by reference. The panels show a dash and WAITING FOR HOST for it, never a made-up figure.</summary>
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
            v.Queue.Clear(); v.Offers.Clear(); v.Perks.Clear();
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
                bool building = i == 0 && q.Percent >= 100 && !q.Waiting; // only the head ever builds: a waiting programme a donor filled is FUNDED, not BUILDING
                int left = (int)Math.Max(0f, Math.Ceiling(q.BuildLeft - (building ? c.Now - stamp : 0f)));
                v.Queue.Add(new FrontQueueView
                {
                    Id = q.Id, Row = q.Row, Name = FrontWords.Programme(q.Id, q.Kind), Cost = cost, Building = building, Waiting = q.Waiting, BuildLeft = left, BuildTotal = (int)build,
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

        /// <summary>One superiority term's lean from the host (<see cref="FrontSuperiority.Terms"/>): 0 even, 1 ahead, 2 behind.</summary>
        public static int Lean(FrontRow r, int term) => (r.Terms >> (2 * term)) & 3;

        /// <summary>
        /// The FRONT tab's three superiority terms (<see cref="FrontSuperiority.Compute"/>'s summands, in its order) with our own count each, read from the room
        /// payload <see cref="Orbit"/>, <see cref="Network"/> or <see cref="Shadow"/> just built for the shown front.
        /// </summary>
        public static void Terms(FrontRoomView v)
        {
            string[] t = v.Terms;
            switch (v.Front)
            {
                case Front.Space:
                    t[0] = "SATELLITES UP " + v.BirdsUp + " / " + FrontRules.BirdCap;
                    t[1] = "PADS LIVE " + v.PadsUp + " / " + FrontRules.PadCap;
                    // ponytail: no own jam flag is mirrored, so the lean words it (EVEN reads NO even when both sides jam); mirror the flag if that matters.
                    int jam = Lean(v.Row, 2);
                    t[2] = "JAMMING THEIR SATELLITES · " + (jam == 1 ? "YES" : jam == 2 ? "NO · THEY JAM YOURS" : "NO");
                    break;
                case Front.Cyber:
                    NetworkRoomView n = v.Network;
                    int live = 0;
                    float trace = 0f;
                    foreach (InfraCenterView c in n.Centers) if (c.State == InfraState.Up || c.State == InfraState.Damaged) live++;
                    foreach (InfraTruckView k in n.Trucks)
                    {
                        if (k.State == InfraState.Up || k.State == InfraState.Damaged) live++; // a packing truck is offline (FrontService.Sample)
                        trace = Math.Max(trace, k.Trace); // the highest session trace, as the host weighs it
                    }
                    t[0] = "ENEMY NODES HELD " + n.HeldTotal;
                    t[1] = "DATA CENTERS + TRUCKS " + live;
                    t[2] = n.Sessions == 0 ? "YOUR TRACE · NO SESSION" : "YOUR TRACE " + Mathf.RoundToInt(trace * 100f) + " %";
                    break;
                default:
                    ShadowRoomView s = v.Shadow;
                    t[0] = "TEAMS IN THE FIELD " + s.Afield;
                    t[1] = "BUILDINGS HELD " + s.Held.Count;
                    t[2] = "CAMPS " + s.CampsUp + " / " + FrontRules.CampCap;
                    break;
            }
        }

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

        /// <summary>The satellite a perk needs over its aim point: RECON PASS and OVERWATCH = RADAR, SAT CAMERA = OPTICAL, ORBITAL ROD and STARFALL = KINETIC. -1 for a perk that needs none.</summary>
        public static int PerkBird(SupportActionId id) =>
            id == SupportActionId.SatCamera ? 0 : id == SupportActionId.Recon || id == SupportActionId.Overwatch ? 1 : id == SupportActionId.Artillery || id == SupportActionId.Starfall ? 2 : -1;

        /// <summary>The state word when the current aim lies outside the footprint of the bird the perk needs ("" otherwise, or with no aim).</summary>
        public static string FootprintGate(SupportActionId id, in FrontInputs c)
        {
            int k = PerkBird(id);
            if (k < 0 || !c.AimKnown || c.HasBird == null || !c.HasBird[k] || (c.BirdsDown & (1 << k)) != 0) return "";
            GeoBird bird = GeoOf(c.Geo, k, true);
            return bird.Covers(c.Now, c.AimU, c.AimV, GeoSpace.Footprint(Kinds[k], bird.Bus)) ? "" : SupportWords.OutsideFootprint(GeoSpace.Names[k]);
        }

        /// <summary>One line per perk for the FRONT tab's perk list.</summary>
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
            src != null && k < src.Length ? src[k] : new GeoBird(own ? OwnU[k] : FoeU[k], own ? OwnV[k] : FoeV[k], 100f);

        internal static string BirdCallsign(int k) => Names[k];

        /// <summary>Slot longitude of a parked point: a fixed per-faction base plus the point's offset across the theatre at geostationary range (1 degree = 735 km). Flavour; it moves only when the bird really does.</summary>
        internal static string SlotLon(string faction, bool own, float u, float theatreM)
        {
            float lon = 12f + FactionIdentity.Of(faction) % 600u / 10f + (own ? 0f : 9.6f) + (u - 0.5f) * theatreM / 735000f;
            return lon.ToString("0.00", NetFmt) + "E";
        }

        private static OrbitTrackView Station(int k, bool own, GeoBird g, in FrontInputs c, FrontRoomView v)
        {
            float u = g.U(c.Now), radius = own ? GeoSpace.Footprint(Kinds[k], g.Bus) : 0f;
            return new OrbitTrackView
            {
                Kind = Kinds[k], Bus = g.Bus, Own = own, U = u, V = g.V(c.Now), FromU = g.FromU, FromV = g.FromV, TargetU = g.ToU, TargetV = g.ToV,
                Relocating = g.Moving(c.Now), EtaSeconds = g.Eta(c.Now), BurnCost = g.BurnCost, Fuel = g.Fuel, Radius = radius,
                Overhead = own && v.HasFocus && g.Covers(c.Now, v.FocusU, v.FocusV, radius),
                FootprintKm = radius * v.TheatreKm, Slot = SlotLon(c.Faction, own, u, c.Frame.W),
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
            for (int k = 0; c.EnemyTrackIds != null && k < Kinds.Length && k < c.EnemyTrackIds.Length; k++)
            {
                if (c.EnemyTrackIds[k] <= 0) continue;
                OrbitTrackView b = Station(k, false, GeoOf(c.EnemyGeo, k, false), c, v);
                b.TrackId = c.EnemyTrackIds[k];
                b.Alive = true; b.Callsign = "T" + b.TrackId + " · " + GeoSpace.Names[k]; b.State = "COARSE TRACK"; b.Tone = AvState.Danger;
                v.Birds.Add(b);
            }
            Satellites(v, c, SupportManager.Active?.OpsMirror.Sites);
        }

    }
}
