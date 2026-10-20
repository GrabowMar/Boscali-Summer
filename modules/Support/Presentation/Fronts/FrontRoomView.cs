using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>One programme in the front's queue, already worded for the rail.</summary>
    internal struct FrontQueueView
    {
        public ProgrammeId Id;
        public string Name;
        public float Cost;
        /// <summary>Funding share 0..1 while the bar fills; build progress 0..1 once <see cref="Building"/>.</summary>
        public float Progress;
        public bool Building;
        public int BuildLeft;
        /// <summary>The whole build clock in seconds (the second phase, after the funding bar is full).</summary>
        public int BuildTotal;
    }

    /// <summary>A programme the front can queue (or why it cannot right now; empty Refusal means it can).</summary>
    internal struct FrontOfferView
    {
        public ProgrammeId Id;
        public string Name;
        public float Cost;
        public int BuildSeconds;
        public bool Queued;
        public string Refusal;
    }

    /// <summary>One rung of the front's perk ladder.</summary>
    internal struct FrontPerkView
    {
        public int Rung;
        public string Name, Effect;
        /// <summary>TARGET OUTSIDE &lt;BIRD&gt; FOOTPRINT · RELOCATE while the aim is out of the needed satellite's footprint (empty otherwise).</summary>
        public string Gate;
        public bool Unlocked;
        public int Price;
        /// <summary>Why the pilot cannot use it now (readiness rung, missing satellite, allocation); empty when it is available.</summary>
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

        // NETWORK and SHADOW payloads.
        public readonly NetworkRoomView Network = new NetworkRoomView();
        public readonly ShadowRoomView Shadow = new ShadowRoomView();
    }
}
