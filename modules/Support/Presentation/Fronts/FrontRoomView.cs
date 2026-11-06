using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>One programme in the front's queue, already worded for the rail.</summary>
    internal struct FrontQueueView
    {
        public ProgrammeId Id;
        /// <summary>The host's row id: CHIP IN donates to this row.</summary>
        public ushort Row;
        public string Name;
        public float Cost;
        /// <summary>Funding share 0..1 while the bar fills; build progress 0..1 once <see cref="Building"/>.</summary>
        public float Progress;
        public bool Building;
        public int BuildLeft;
        /// <summary>The whole build clock in seconds (the second phase, after the funding bar is full).</summary>
        public int BuildTotal;
    }

    /// <summary>A build the front can queue with automatic placement (or why it cannot right now; empty Refusal means it can). Kind is the satellite kind a SATELLITE offer launches.</summary>
    internal struct FrontOfferView
    {
        public ProgrammeId Id;
        public byte Kind;
        public string Name;
        public float Cost;
        public int BuildSeconds;
        public string Refusal;
    }

    /// <summary>One rung of the front's perk ladder.</summary>
    internal struct FrontPerkView
    {
        public SupportActionId Id;
        public int Rung;
        public string Name, Effect;
        /// <summary>TARGET OUTSIDE &lt;BIRD&gt; FOOTPRINT · RELOCATE while the aim is out of the needed satellite's footprint (empty otherwise).</summary>
        public string Gate;
        public bool Unlocked;
        public int Price;
        /// <summary>Why the pilot cannot use it now (skill grade, missing satellite, allocation); empty when it is available.</summary>
        public string Reason;
    }

    internal struct FrontLogLine
    {
        public string Stamp, Text;
        public AvState Tone;
        /// <summary>Seconds since the host wrote the line (the radio log only; zero for a terminal line).</summary>
        public float Ago;
    }

    /// <summary>One satellite as the ORBIT situation view draws it. Pure data: the view derives every point from these.</summary>
    internal struct OrbitTrackView
    {
        public BirdKind Kind;
        public bool Own, Alive;
        /// <summary>Where the bird is now, map u,v (0..1, v down): its parked point, or a point on the transfer while it relocates.</summary>
        public float U, V;
        /// <summary>The transfer under way: from where it left to the new parked point (equal to U,V while parked).</summary>
        public float FromU, FromV, TargetU, TargetV;
        public bool Relocating;
        /// <summary>Seconds left on the transfer, and the fuel percent it cost.</summary>
        public float EtaSeconds, BurnCost;
        /// <summary>Fuel left, 0..100.</summary>
        public float Fuel;
        /// <summary>Footprint radius as a share of the theatre width.</summary>
        public float Radius;
        /// <summary>The footprint covers the area of operations (the map centre) now.</summary>
        public bool Overhead;
        public string Callsign, State;
        public AvState Tone;
        public string Orbit;
        /// <summary>Slot longitude ("41.03E") and catalogue number ("CAT 41732") of the parked point: flavour derived from the faction and the point, never a reading of its own. Empty in a hand-built fixture.</summary>
        public string Slot, Catalog;
        /// <summary>Footprint radius in kilometres.</summary>
        public float FootprintKm;
    }

    /// <summary>A ground marker on the situation view (an uplink site).</summary>
    internal struct OrbitSiteView
    {
        public float X, Y;
        public bool Own, Live, Rebuilding;
        public string Name, State;
        /// <summary>Where it stands ("GRID C4") and what losing it costs, already worded; empty in a hand-built fixture.</summary>
        public string Grid, Cost;
    }

    /// <summary>
    /// One launch pad on the SATELLITES tab: standing or lost (the OPS mirror's sites), or building (a queued LAUNCH PAD row: Row, Progress and BuildLeft come from it,
    /// and <see cref="FrontRoomView.Queue"/> carries the same Row with the funding / building split).
    /// </summary>
    internal struct PadView
    {
        /// <summary>0-based slot, the one the host builds into (an empty slot first, then the first destroyed one).</summary>
        public int Slot;
        /// <summary>PAD-1, PAD-2.</summary>
        public string Name;
        public bool Up, Building, Lost;
        /// <summary>Map point (0..1, y down); -1 for a queued pad with automatic placement (no point yet).</summary>
        public float U, V;
        /// <summary>Where it stands: the airbase short name, a grid, or AUTO PLACE.</summary>
        public string Place;
        /// <summary>The birds an up pad links ("LINKS DARKSTAR, WATCHTOWER" / "LINKS NONE"); "" for a lost or building pad.</summary>
        public string Links;
        public ushort Row;
        public float Progress;
        public int BuildLeft;
    }

    /// <summary>One fleet card per bird kind (always three, by kind): its state, footprint coverage, the perk it enables, and the LAUNCH offer for that kind.</summary>
    internal struct FleetCardView
    {
        public int Kind;
        public string Callsign;
        /// <summary>NOT LAUNCHED / ON STATION / BURNING m:ss / LOST / LAUNCHING m:ss (QUEUED · n % or WAITS FOR A PAD before the build starts).</summary>
        public string State;
        public AvState Tone;
        /// <summary>On station (alive): RELOCATE and the feed apply; LAUNCH does not.</summary>
        public bool Up;
        /// <summary>Fuel 0..100 (0 when not up).</summary>
        public float Fuel;
        /// <summary>"COVERS FELDSPAR, OPAL" / "COVERS NOTHING" (airbases under the footprint); "" when not up.</summary>
        public string Covers;
        /// <summary>The perk this kind enables, READY or its lock reason ("SAT CAMERA READY", "OVERWATCH NEEDS SPACE SKILL 3").</summary>
        public string Perk;
        public bool CanRelocate;
        public FrontOfferView Launch;
    }

    /// <summary>An enemy bird the OPS mirror carries (up and known only).</summary>
    internal struct HostileBirdView
    {
        public int Kind;
        /// <summary>OPTICAL / RADAR / KINETIC.</summary>
        public string Word;
        /// <summary>Its footprint covers at least one of our airbases.</summary>
        public bool OverOwn;
        /// <summary>The nearest of our airbases under its footprint ("" when none).</summary>
        public string Nearest;
    }

    /// <summary>A queued build with a map point: a ghost on the plate with its label ("PAD-2 · OPAL · 1:12"); Kind says what it builds where a room has more than one (CYBER: the AnchorKind).</summary>
    internal struct MapGhostView
    {
        public float U, V;
        public string Label;
        public byte Kind;
    }

    internal enum AirbaseSide : byte { Neutral, Own, Enemy }

    /// <summary>An airbase in world metres as the controller reads it from the game (the input of <see cref="FrontViews.Airbases"/>).</summary>
    internal struct AirbaseFix
    {
        public float X, Z;
        public string Name;
        public AirbaseSide Side;
    }

    /// <summary>An airbase on the map plate: 0..1 position (y down), short name and who holds it.</summary>
    internal struct MapAirbaseView
    {
        public float U, V;
        public string Name;
        public AirbaseSide Side;
    }

    /// <summary>
    /// Everything a front window paints, built by the (later) controller from the faction mirror. Plain data, reused each
    /// refresh; the rail reads the common part, each room reads its own payload.
    /// </summary>
    internal sealed class FrontRoomView
    {
        public Front Front;
        public string Faction = "", Callsign = "", Dtg = "", Classification = "";
        /// <summary>The local pilot's allocation (what a donation spends).</summary>
        public int Allocation;
        /// <summary>Funds per minute the share tick should add to this front's budget (0 at the cap or before the host speaks).</summary>
        public int FundsRate;
        /// <summary>Mission clock (seconds) the lock line counts against.</summary>
        public float Now;
        public FrontRow Row = new FrontRow();
        /// <summary>The faction-wide funding split (SPACE, CYBER, SOF percent) as the host last said it.</summary>
        public readonly int[] Shares = new int[FrontRules.FrontCount];
        /// <summary>Seconds since the host's last front snapshot reached us; -1 before the first one. Snapshots are change-only, so a quiet front ages: it is not a staleness verdict.</summary>
        public float FeedAge = -1f;
        public string FocusGrid = "";
        public readonly List<FrontQueueView> Queue = new List<FrontQueueView>(FrontRules.MaxQueue);
        public readonly List<FrontOfferView> Offers = new List<FrontOfferView>(4);
        public readonly List<FrontPerkView> Perks = new List<FrontPerkView>(8);
        /// <summary>This front's perk power as the rail words it: <c>POWER ×1.25 · AHEAD</c> (empty before the host has spoken).</summary>
        public string PowerLine = "";
        /// <summary>The local pilot's skill grade in this front's system (1..6), shown where front readiness used to be.</summary>
        public int Grade = 1;
        public readonly List<FrontLogLine> Log = new List<FrontLogLine>(8);

        // ORBIT payload.
        public SpaceFeedView Feed = new SpaceFeedView();
        public readonly List<OrbitTrackView> Birds = new List<OrbitTrackView>(6);
        public readonly List<OrbitSiteView> Sites = new List<OrbitSiteView>(6);
        public string Theatre = "";
        /// <summary>The SPACE front's focus pin on the map (0..1, y down) when the rail has one set; the birds' OVER FOCUS reading is against it.</summary>
        public bool HasFocus;
        public float FocusU, FocusV, TheatreKm;
        /// <summary>Airbases on the theatre plate (all three rooms draw them).</summary>
        public readonly List<MapAirbaseView> Airbases = new List<MapAirbaseView>(MaxAirbases);
        public const int MaxAirbases = 24;
        // SATELLITES tab (FrontViews.Orbit).
        public readonly List<PadView> Pads = new List<PadView>(FrontRules.PadCap);
        public readonly List<FleetCardView> Fleet = new List<FleetCardView>(FrontRules.BirdCap);
        public readonly List<HostileBirdView> Hostile = new List<HostileBirdView>(FrontRules.BirdCap);
        public FrontOfferView Asat;
        public readonly List<MapGhostView> Ghosts = new List<MapGhostView>(FrontRules.MaxQueue);
        /// <summary>"GAP · CLIFFLINE NOT COVERED": the first of our airbases under no own footprint, or "".</summary>
        public string CoverageGap = "";
        /// <summary>Live pads, birds on station, and how many birds the live pads hold now (2 per pad, at most 3).</summary>
        public int PadsUp, BirdsUp, BirdCap;

        // NETWORK and SHADOW payloads.
        public readonly NetworkRoomView Network = new NetworkRoomView();
        public readonly ShadowRoomView Shadow = new ShadowRoomView();
    }
}
