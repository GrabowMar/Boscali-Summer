using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    // Pure payloads of the NETWORK and SHADOW rooms. The controller maps CyberStateData / SofStateData onto these (positions already
    // normalised to the theatre map, 0..1, y down); the rooms derive every line, ring and edge from the fields.

    internal enum NetNodeState : byte { OutOfReach, InReach, Hopping, Held, Traced }

    internal struct NetNodeView
    {
        public int Id;
        public NodeKind Kind;
        public float X, Y;
        public NetNodeState State;
        /// <summary>Hop progress 0..100 while <see cref="NetNodeState.Hopping"/>.</summary>
        public byte HopPct;
        /// <summary>Where the intrusion reached this node from: another node id, or -1.</summary>
        public int FromNode;
        /// <summary>... or the own anchor (index into <see cref="NetworkRoomView.Anchors"/>), or -1.</summary>
        public int FromAnchor;
        public string Label;
        /// <summary>Hostname-like handle, fixed per node from its kind and id ("rdr-04.vesper"); it carries no reading.</summary>
        public string Handle;
        /// <summary>Who holds or is hopping it: "YOU", an operator's name, "ALLY" when the mirror names none, "" when nobody.</summary>
        public string Owner;
        /// <summary>The nearest reach source of the operator (own truck, or own held node once a session runs) and its distance ("EW-1 · 11.3 KM"); "" when none stands.</summary>
        public string Reach;
        /// <summary>Kilometres to that source, -1 with none standing.</summary>
        public float SourceKm;
        /// <summary>Where it stands: the nearest airbase's short name, else its grid.</summary>
        public string Place;
        /// <summary>Hops a session needs to reach it (<see cref="CyberGraph.Hops"/>): 1 in reach now, n through n-1 nodes on the way, 0 no route.</summary>
        public int Hops;
        /// <summary>One of our own anchors shown as a node (<see cref="NetworkRoomView.OwnId"/>: a negative id, picked in the room only, never sent to the host); <see cref="AnchorAt"/> indexes <see cref="NetworkRoomView.Anchors"/>.</summary>
        public bool Own;
        public int AnchorAt;
        /// <summary>An own node an enemy holds right now (a breach on our network matched by kind and position).</summary>
        public bool Breached;
    }

    internal struct NetAnchorView
    {
        public AnchorKind Kind;
        public AnchorHealth Health;
        /// <summary>A truck packing up to redeploy: offline, no reach.</summary>
        public bool Packing;
        public float X, Y;
        /// <summary>Reach as a share of the map width (EW trucks only).</summary>
        public float Reach;
        public string Label;
        /// <summary>Seconds left on a trace lock (EW trucks only), 0 when free.</summary>
        public int LockLeft;
        /// <summary>The host's slot index of its kind (EW-1 is truck slot 0): what <see cref="IFrontActions.MoveTruck"/> names.</summary>
        public int Slot;
        public bool Breached;
    }

    /// <summary>Where one data center or EW truck stands on the INFRASTRUCTURE tab. Lost reads DOWN on a truck card.</summary>
    internal enum InfraState : byte { Up, Damaged, Packing, Building, Lost }

    /// <summary>
    /// One data center card: standing, damaged or lost (the CYBER mirror's anchors), or building (a queued DATA CENTER row in the slot the host builds into:
    /// Row, Progress and BuildLeft come from it; <see cref="FrontRoomView.Queue"/> carries the same Row with the funding / building split).
    /// </summary>
    internal struct InfraCenterView
    {
        /// <summary>The host's data center slot (DC-1 is slot 0).</summary>
        public int Slot;
        public string Name;
        public InfraState State;
        /// <summary>Map point (0..1, y down); -1 for a queued center with automatic placement.</summary>
        public float U, V;
        /// <summary>The airbase short name it stands by, a grid, or AUTO PLACE.</summary>
        public string Place;
        /// <summary>"RUNS EW-1" or "IDLE" while it stands ("" otherwise): the wire carries no pairing, each standing truck takes the nearest free center.</summary>
        public string Runs;
        /// <summary>What a standing center does for us ("CUTS YOUR TRACE ×0.8"); "" otherwise.</summary>
        public string Effect;
        public bool Breached;
        public ushort Row;
        public float Progress;
        public int BuildLeft;
    }

    /// <summary>One EW truck card: standing, packing up (RELOCATE), down, or building (a queued EW TRUCK row, like <see cref="InfraCenterView"/>).</summary>
    internal struct InfraTruckView
    {
        /// <summary>The host's truck slot (EW-1 is 0): <see cref="IFrontActions.MoveTruck"/> takes it.</summary>
        public int Ordinal;
        public string Name;
        public InfraState State;
        public float U, V;
        /// <summary>"GRID HI162", or AUTO PLACE for a queued truck with no point.</summary>
        public string Grid;
        /// <summary>Enemy nodes within its reach now ("4 ENEMY NODES IN REACH"); 0 while it is offline.</summary>
        public int InReach;
        public string InReachWord;
        /// <summary>The highest trace of the sessions it sources (0..1) and its words ("TRACE 46 % · YOUR SESSION" / "NO SESSION ON IT").</summary>
        public float Trace;
        public string TraceWord;
        public int LockLeft;
        public ushort Row;
        public float Progress;
        public int BuildLeft;
    }

    /// <summary>The selected node's inspector: already-worded lines (the quote is the host's own: same rules, same numbers).</summary>
    internal struct NetDossierView
    {
        public bool Has;
        public string Title, Handle, Stamp, Owner, Reach, Hold, Hop, Burn, Drop, Next;
        public AvState StampTone, NextTone;
        /// <summary>Allocation the HOP quote charges (0 when going deeper from a held node); the room weighs it against the pilot's balance.</summary>
        public int HopCost;
    }

    internal sealed class NetworkRoomView
    {
        public bool Active;
        /// <summary>Theatre size in kilometres, so the room can link nodes within 12 km.</summary>
        public float MapKmW = 90f, MapKmH = 60f;
        /// <summary>The enemy nodes the mirror carries, then our own anchors as nodes (<see cref="NetNodeView.Own"/>).</summary>
        public readonly List<NetNodeView> Nodes = new List<NetNodeView>(CyberWire.MaxNodes + CyberWire.MaxAnchors);
        public readonly List<NetAnchorView> Anchors = new List<NetAnchorView>(4);
        /// <summary>Own anchors take node ids below zero, by kind and slot (EW-1 -10, DC-1 -20): the room picks them, the host never hears of them.</summary>
        public const int OwnTruckId = -10, OwnCenterId = -20;
        public static int OwnId(AnchorKind kind, int slot) => (kind == AnchorKind.EwTruck ? OwnTruckId : OwnCenterId) - slot;
        // INFRASTRUCTURE tab (FrontViews.Network).
        public readonly List<InfraCenterView> Centers = new List<InfraCenterView>(FrontRules.DataCenterCap);
        public readonly List<InfraTruckView> Trucks = new List<InfraTruckView>(FrontRules.TruckCap);
        public FrontOfferView BuildCenter, DeployTruck, ZeroDay;
        /// <summary>Queued data centers and trucks with a map point ("DC-2 · OPAL · 0:48", "EW-2 · 1:12"; Kind is the AnchorKind).</summary>
        public readonly List<MapGhostView> Ghosts = new List<MapGhostView>(FrontRules.MaxQueue);
        /// <summary>Standing data centers and trucks (a truck packing up still stands), for the section heads.</summary>
        public int CentersUp, TrucksUp;
        // YOUR session.
        public bool HasIntrusion;
        public byte Trace;
        public string Operator = "";
        public int YourHeld;
        public bool Hopping;
        public float HopFraction;
        public int HopLeft;
        /// <summary>Estimated seconds until the trace fills at the current rate (rules only, no counter-pressure); -1 when nothing climbs it.</summary>
        public int EstTraceSeconds = -1;
        public string HopLine = "";
        /// <summary>No host update for this long while the session should be changing it; blocks HOP.</summary>
        public bool Stale;
        public int AgeSeconds;
        /// <summary>The one next step (or the blocking reason) for the whole session.</summary>
        public string Next = "";
        public AvState NextTone = AvState.Caution;
        // The FACTION's network.
        public byte Infocon = 5;
        public string ThreatWord = "", ThreatNote = "";
        /// <summary>Enemy holds on our own network, and whether PURGE can run now (its label carries the countdown or the recharge).</summary>
        public int Breaches;
        public bool CanPurge;
        public string PurgeLabel = "PURGE";
        /// <summary>Nodes the whole faction holds or hops (faction total, never divided by a personal cap) and the running sessions against their cap.</summary>
        public int HeldTotal, IntrusionCap, Sessions;
        public int SelectedId = -1;
        public NetDossierView Dossier;
        public bool CanHop, CanBurn, CanDrop;
        // Session log: who is online, then the faction's real events (kept across refreshes, the mirror only carries the newest three).
        public const int MaxHistory = 8;
        public readonly List<FrontLogLine> Roster = new List<FrontLogLine>(3);
        public readonly List<FrontLogLine> History = new List<FrontLogLine>(MaxHistory);
        public int LastSeq;
        public string Prompt = "";
        public string Session = "";
        public string Meta = "";
    }

    /// <summary>
    /// One camp card, by host slot (CAMP-1 is slot 0): standing, damaged or lost (the SOF mirror, which lists camps in slot order), or building (a queued CAMP row in
    /// the slot the host builds into, which takes over a lost card there: Row, Progress and BuildLeft come from it).
    /// </summary>
    internal struct SofCampView
    {
        public int Slot;
        /// <summary>Map point (0..1, y down); -1 for a queued camp with automatic placement.</summary>
        public float X, Y;
        public AnchorHealth Health;
        public InfraState State;
        public string Name;
        /// <summary>The airbase short name it stands by, a grid, or AUTO PLACE.</summary>
        public string Place;
        /// <summary>"HOLDS A-1, A-2 · FULL" / "ROOM FOR 2 TEAMS" (derived: <see cref="CampRules.Homes"/>); "" for a building or lost camp.</summary>
        public string Holds;
        public ushort Row;
        public float Progress;
        public int BuildLeft;
    }

    /// <summary>The FOB card (ruling D-R3: its place is not mirrored): UP with the time left, building or funding (a queued FOB row), NOT BUILT, or NEEDS A SEIZED BUILDING.</summary>
    internal struct SofFobView
    {
        public string State, Note;
        public AvState Tone;
        public bool Up, Queued;
        public ushort Row;
        public float Progress;
    }

    internal struct SofTeamView
    {
        public int Slot;
        public string Callsign, State, Mission, Flags, Eta;
        /// <summary>Fixed word per slot ("SABRE") that rides the callsign on the dossier tab; it carries no reading.</summary>
        public string Codename;
        /// <summary>Who commands the team (<see cref="SofPageWords.Control"/>: YOU, CTRL name, AUTO) and the stamp word of its state (STANDBY, TASKED, PINNED ...).</summary>
        public string Control, Stamp;
        /// <summary>The camp it calls home (ruling D-R4: "AT CAMP-1" while ready there, "HOME CAMP-2" out in the field, "NO CAMP").</summary>
        public string Home;
        public AvState Tone;
        public float X, Y, DestX, DestY, TargetX, TargetY;
        public bool HasDest, Lasing, Wounded, Selected, Present;
        /// <summary>Another operator commands this team: the room shows it and offers no order for it.</summary>
        public bool Locked;
        public byte Exposure, Ammo, Odds;
        public bool Helicopter;
        /// <summary>PUSH, HOLD, EXFIL, LIFT as bits 0..3 (<see cref="IFrontActions.TeamOrder"/>'s verbs): which the host would take now, and which toggles are on.</summary>
        public byte VerbOn, VerbOnLatched;
        /// <summary>What the verbs mean for this team now, or why there are none (LIFT: the team waits for a pilot's helicopter, +20 odds once carried).</summary>
        public string VerbNote;
        /// <summary>A queued TRAIN TEAM row (Present false) in the slot the host raises it into: funding or building, with its row for CHIP IN.</summary>
        public bool Training, Building;
        public ushort Row;
        public float Progress;
        public int BuildLeft;
    }

    internal struct SofTargetView
    {
        public int Id;
        public TargetKind Kind;
        public AnchorSub Sub;
        /// <summary>Its filter chip (ruling D-R1, <see cref="SofAim.Filter"/>): 1 SITES, 2 BUILDINGS, 3 NETWORK, 4 GROUND.</summary>
        public int Filter;
        public float X, Y;
        public bool Exploit, Resisted, Selected;
        /// <summary>"T-3 UPLINK", the grid square, and the row's tag: who is on it ("A-1 ON IT"), else EXPLOIT / RESISTED, else the mission it allows.</summary>
        public string Name, Grid, Tag;
        public AvState TagTone;
    }

    /// <summary>A team out in the field, for the TARGETS tab's strip: "A-1 SABRE › T-2 SABOTAGE" and "ON SITE 0:32 · EXP 38 %".</summary>
    internal struct SofFieldView
    {
        public int Slot;
        public string Left, Right;
        public AvState Tone;
    }

    /// <summary>One team the SEND card can send: "A-1 · 3.2 KM" when it can go now, else why not ("B-1 · MOVING").</summary>
    internal struct SofCrewView
    {
        public int Slot;
        public bool CanGo;
        public string Word, Help;
    }

    internal struct SofHeldView
    {
        /// <summary>The host's held-building id (the FOB pick snaps to it).</summary>
        public int Id;
        public float X, Y, SecondsLeft;
        public string Name;
    }

    /// <summary>One mission on the SEND card, for the picked aim and the picked team: lit when the aim allows it (whoever goes), its price, odds and what it does (or what it needs).</summary>
    internal struct SofMissionView
    {
        public MissionKind Kind;
        public string Name, Odds, Note;
        public int Ap;
        /// <summary>Valid: the aim takes this mission. Best: the one the card opens on.</summary>
        public bool Valid, Exploit, Best;
    }

    internal sealed class ShadowRoomView
    {
        public bool Active;
        /// <summary>Camp cards by host slot (standing, lost and queued); the map pins the ones with a point that are not building (those are <see cref="Ghosts"/>).</summary>
        public readonly List<SofCampView> Camps = new List<SofCampView>(CampRules.MaxCamps);
        /// <summary>Queued camps and a queued FOB with a map point ("CAMP-2 · OPAL · 0:51"; Kind 0 camp, 1 FOB).</summary>
        public readonly List<MapGhostView> Ghosts = new List<MapGhostView>(FrontRules.MaxQueue);
        public SofFobView Fob;
        /// <summary>Standing camps and fielded teams (a lost one excluded), for the section heads.</summary>
        public int CampsUp, TeamsUp;
        /// <summary>Every team on the roster (bug 7: one beyond the cap stays), then each queued TRAIN TEAM row as a card in the slot it will take.</summary>
        public readonly List<SofTeamView> Teams = new List<SofTeamView>(4);
        public readonly List<SofTargetView> Targets = new List<SofTargetView>(SofWire.MaxTargets);
        public readonly List<SofHeldView> Held = new List<SofHeldView>(4);
        public readonly List<NetPoint> Enemies = new List<NetPoint>(4);
        /// <summary>One glyph per enemy: ? seen by our bird, ! detected near our sites, Q hunted by our QRF.</summary>
        public readonly List<string> EnemyMark = new List<string>(4);
        /// <summary>HOSTILES NEAR US: how many, one line on the QRF, its tone, and whether SWEEP can roll now (its label carries the countdown or the refit).</summary>
        public int Near;
        public string QrfLine = "", SweepLabel = "SWEEP";
        public AvState QrfTone = AvState.Inert;
        public bool CanSweep;
        public int TeamCap;
        public string TapLine = "";
        /// <summary>The picked team: the one SEND sends (a new aim pre-picks the nearest that can go, spec 7.3) and the jacket TEAMS &amp; BASES clips.</summary>
        public int SelectedSlot = -1;
        public int SelectedTargetId;
        public string Meta = "";
        /// <summary>
        /// The operator's area RECON point: a click on open map ground (map u,v in 0..1, y down) and its id for <see cref="IFrontActions.TeamMission"/>. It is the aim
        /// while no target is picked (picking one drops it). Local state: the room writes it, CLEAR or a sent RECON clears it.
        /// </summary>
        public bool HasPoint;
        public float PointU, PointV;
        public int PointId;
        /// <summary>Why a pick was dropped (a team left the roster, a target left the list or was reused); the room clears it on the operator's next pick.</summary>
        public string Reason = "";

        // The TARGETS tab (spec 7.3).
        /// <summary>Teams out in the field, and what the strip says when none is.</summary>
        public readonly List<SofFieldView> Field = new List<SofFieldView>(4);
        public string FieldNote = "";
        /// <summary>The SEND card of the aim (a picked target or the RECON point): its missions, the teams it can send, why the picked team cannot go now ("" when it can), and how it goes in.</summary>
        public readonly List<SofMissionView> Missions = new List<SofMissionView>(5);
        public readonly List<SofCrewView> Crew = new List<SofCrewView>(4);
        public string SendWhy = "", GoIn = "", Helo = "";
        /// <summary>The picked target's file: the tab word and four worded lines (kind, grid and best mission, who is tasked on it, what its tag means).</summary>
        public string FileTab = "", FileA = "", FileB = "", FileC = "", FileD = "";
        /// <summary>The aim the last build saw (a target id or a point id), and the picked target's kind and place then: a new aim pre-picks a team, a reused id is dropped.</summary>
        public int AimSeen, PickedId;
        public TargetKind PickedKind;
        public AnchorSub PickedSub;
        public float PickedX, PickedZ;

        public const int MaxHistory = 8;
        public readonly List<FrontLogLine> History = new List<FrontLogLine>(MaxHistory);
        public int LastSeq;
    }

    internal struct NetPoint { public float X, Y; }
}
