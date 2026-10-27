using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    internal enum TheaterWarPosture : byte { Cautious = 0, Steady = 1, Bold = 2 }
    internal enum TheaterOpKind : byte { Assault = 0, Defend = 1, Recon = 2 }
    internal enum TheaterPhase : byte { Awaiting = 0, Forming = 1, Advancing = 2, InContact = 3, Holding = 4, Scouting = 5, Withdrawing = 6 }
    internal enum TheaterPhaseNote : byte { None = 0, NoSteering = 1, NoForces = 2 }
    internal enum TheaterFrontStatus : byte { Unconfirmed = 0, Holding = 1, UnderPressure = 2, InContact = 3 }
    internal enum TheaterCarrier : byte { None = 0, Ground = 1, AirNaval = 2 }
    internal enum TheaterRisk : byte { Unknown = 0, Moderate = 1, High = 2 }
    internal enum TheaterOutcome : byte { None = 0, Secured = 1, Repulsed = 2, Stalled = 3, CalledOff = 4, Redirected = 5 }
    internal enum TheaterWhy : byte
    {
        None = 0, ObjectiveCaptured = 1, FrontAdvanced = 2, HeldClear = 3, Observed = 4, ForcesLost = 5,
        FrontPushedBack = 6, ObjectiveLost = 7, NoForces = 8, TimeLimit = 9, BaseLost = 10, FrontVanished = 11,
    }
    internal enum TheaterThreatWhy : byte
    { None = 0, Nominal = 1, AirContact = 2, AirOutnumbered = 3, GroundContact = 4, BaseContested = 5, BasesContested = 6 }
    internal enum TheaterEmptyReason : byte
    {
        None = 0, StaffOff = 1, HostLacksModule = 2, NoFaction = 3, WaitingForHost = 4, FirstReview = 5, NoFronts = 6,
        NoGroundWar = 7, NoFriendlyGroundNearFront = 8, FrontsQuiet = 9, PostureTooCautious = 10, CalledOff = 11,
        StaffFault = 12, OperationLive = 13, TacticsOff = 14, RtsOwnsOrders = 15, NoTerritoryField = 16,
    }
    internal enum TheaterViewState : byte { Live = 0, WaitingForHost = 1, HostLacksModule = 2, StaffOff = 3, NoFaction = 4, Stale = 5 }
    /// <summary>Bounds of a pilot-drawn offensive, shared by the map tool and the host's check: arrow length, and how far from a front the press may land.</summary>
    internal static class TheaterDrawLimits
    {
        public const float MinimumMetres = 1500f, MaximumMetres = 30000f, SnapMetres = 8000f;
    }

    internal enum TheaterCommandCode : byte
    {
        None = 0, Accepted = 1, StaleChoice = 2, Locked = 3, RateLimited = 4, Invalid = 5, NoOperation = 6,
        SessionChanged = 7, Unconfirmed = 8, NotAllowed = 9,
    }
    [Flags]
    internal enum TheaterCaps : ushort
    {
        None = 0,
        StaffRunning = 1,      // the host staff is authoritative, enabled and healthy
        GroundSteering = 2,    // ground steering is available (adapter on, no RTS, host authority)
        GroundCarriers = 4,    // at least one front read has a carrier within reach
        GroundWar = 8,         // the mission fields ground vehicles for this faction or a tracked hostile ground force
        ConvoyGroups = 16,     // the wave desk reports a selectable combat convoy group
        HumanPilot = 32,       // the faction has at least one human player
        TerritoryField = 64,   // Command's control field is present (fronts can be sensed)
        Objectives = 128,      // the mission has active CaptureAirbase or DestroyUnits objectives for this faction
        Intel = 256,           // the threat picture is present (area scouting)
    }

    /// <summary>Ground: stage of a ground group. The byte is the wire value.</summary>
    internal enum GroundStage : byte { None = 0, Rally = 1, Line = 2, Assault = 3, Contact = 4, Withdraw = 5 }

    /// <summary>Ground: why a faction's ground steering is, or is not, running. The byte is the wire value; 255 = not host-measured.</summary>
    internal enum GroundReason : byte
    {
        Ready = 0, TacticsOff = 1, RtsOwnsOrders = 2, Unsupported = 3, NotHost = 4, NoVehicles = 5, NoFront = 6, Pending = 7,
        Unknown = 255,
    }

    /// <summary>Sorties: reinforcement wave state.</summary>
    internal enum WaveState : byte { Off = 0, Earning = 1, Blocked = 2 }

    /// <summary>Sorties: first failing gate in this evaluation order; None = a wave could be bought now.</summary>
    internal enum WaveBlock : byte
    { None = 0, SwitchedOff = 1, NoSorties = 2, MissionForbids = 3, NoOperation = 4, NoDepot = 5, NoGroup = 6, Cooling = 7, PoolShort = 8 }

    /// <summary>The one fresh window for a hostile contact, seconds. HostileContact, ground ContactRule and the engine's rings all read it.</summary>
    internal static class ContactWindow { public const float FreshSeconds = 30f; }

    /// <summary>Friendly ground per tracked hostile an ASSAULT needs, by posture: the three ratios that are literals at LivingWarRules.cs:68-70 today. LivingWarRules.Evaluate and TheaterWords.Posture both read it, so the rule and its caption cannot drift apart.</summary>
    internal static class PostureRule
    {
        public const float CautiousRatio = 1.25f, SteadyRatio = 1.0f, BoldRatio = 0.75f;
        public static float Ratio(TheaterWarPosture posture) =>
            posture == TheaterWarPosture.Cautious ? CautiousRatio : posture == TheaterWarPosture.Bold ? BoldRatio : SteadyRatio;
    }

    internal sealed class TheaterFrontView
    {
        public string Key = "", Name = "";                          // Name <= 28 characters, a place
        public float X, Z, Pressure, Trend;                         // Pressure 0..1, Trend -1..1; both 0 while !Observed (print a dash)
        public float ObservedAgeSeconds = -1f;                      // -1 = never observed
        public TheaterFrontStatus Status;
        public bool Observed, Objective, Held;
        public byte NearFriendly = 255, NearHostile = 255, Carriers = 255;   // 255 = unknown (NearHostile is 255 when !Observed)
    }

    internal sealed class TheaterProposalView
    {
        public int Id, Revision;
        public TheaterOpKind Kind;
        public string Name = "", TargetKey = "";
        public float X, Z, Radius, Pressure;                        // Radius = LivingWarRules.RadiusOf(kind), metres
        public TheaterRisk Risk;
        public TheaterCarrier Carrier;
        public byte FriendlyGround = 255, FriendlyOther = 255, HostileTracked = 255;
        public bool Recommended;                                    // the staff's choice
    }

    internal sealed class TheaterForceMark { public float X, Z; public byte Stage, Alive; }   // Stage = GroundStage 0..5; Alive 0..6

    internal sealed class TheaterForcesView
    {
        public byte Groups = 255, Vehicles = 255, Formed = 255, Rally = 255, Line = 255, Assault = 255, Contact = 255, Withdraw = 255,
            Moving = 255, Steered = 255, OnSlot = 255, Stalled = 255;   // 255 = unknown
        public ushort EtaSeconds = 65535;                           // 65535 = unknown
        public float NearestToFixMeters = -1f;                      // -1 = unknown
        public TheaterForceMark[] Marks = Array.Empty<TheaterForceMark>();   // at most 8, nearest the fix first
    }

    internal sealed class TheaterOperationView
    {
        public int Id, Revision;                                    // Id = the picked card's id, never reused
        public TheaterOpKind Kind;
        public string TargetKey = "", Name = "";
        public float X, Z, OriginX, OriginZ, Radius;
        public TheaterPhase Phase;
        public TheaterPhaseNote PhaseNote;
        public float ElapsedSeconds, LimitSeconds;
        public float Progress = -1f;                                // -1 = unknown
        public TheaterForcesView Forces = new TheaterForcesView();
        public string PickedBy = "";
        public ulong PickedById;
        public bool PickedByStaff;
        public float LockSeconds;                                   // 0 = none; decay by ITheaterWarView.SecondsSincePublished
    }

    internal sealed class TheaterResultView
    {
        public int OperationId;
        public TheaterOpKind Kind;
        public string Name = "";
        public TheaterOutcome Outcome;
        public TheaterWhy Why;
        public float AgeSeconds;
        public int Funds, Morale;
    }

    internal sealed class TheaterCommandResult { public TheaterCommandCode Code; public string Detail = ""; public float LockSeconds, SecondsAgo; }

    /// <summary>Counts measured by the host per faction. -1 = unknown. default(TheaterCensus) is "not measured": check Known first.</summary>
    internal struct TheaterCensus
    {
        public bool Known;
        public short AirFriendly, AirParked, AirHostileTracked, GroundFriendly, GroundHostileTracked, ShipsFriendly, ShipsHostileTracked;
        public short BasesFriendly, BasesHostile, BasesNeutral, BasesContested, Radars;
        public short SectorsFriendly, SectorsHostile, SectorsContested, SectorsNeutral;
        public short SortieCap, SortieSead, SortieCas, SortieStrike, SortieTransit;
        public float FrontMetres;                                   // -1 unknown
        public byte Threat;                                         // 0 unknown, 1 maximum .. 5 nominal
        public TheaterThreatWhy ThreatWhy;

        /// <summary>Every number -1, Known false, Threat 0.</summary>
        public static TheaterCensus Unmeasured => new TheaterCensus
        {
            Known = false,
            AirFriendly = -1, AirParked = -1, AirHostileTracked = -1, GroundFriendly = -1, GroundHostileTracked = -1,
            ShipsFriendly = -1, ShipsHostileTracked = -1,
            BasesFriendly = -1, BasesHostile = -1, BasesNeutral = -1, BasesContested = -1, Radars = -1,
            SectorsFriendly = -1, SectorsHostile = -1, SectorsContested = -1, SectorsNeutral = -1,
            SortieCap = -1, SortieSead = -1, SortieCas = -1, SortieStrike = -1, SortieTransit = -1,
            FrontMetres = -1f, Threat = 0, ThreatWhy = TheaterThreatWhy.None,
        };
    }

    /// <summary>The reinforcement wave as the host measured it. Numbers are seconds or funds at build time; -1 is unknown and prints as a dash.</summary>
    internal sealed class TheaterWaveView
    {
        public WaveState State { get; }
        public WaveBlock Block { get; }          // the gate as of now, also while earning, so the price shows a check or a cross up front
        public int Credits { get; }              // banked finished tied sorties, 0..6
        public int CreditsPerWave { get; }       // 3, a host constant, shown not assumed
        public string Group { get; }             // next group name, "" when none is selectable (at most 32)
        public int Cost { get; }                 // native GetCost rounded; -1 unknown
        public int Pool { get; }                 // floor of factionFunds, clamped to 1,000,000; -1 unknown
        public int CooldownSeconds { get; }      // native cooldown left; -1 unknown
        public int Bought { get; }               // waves bought this mission, monotone (clients diff it for a callout)
        public float LastBoughtAgo { get; }      // seconds at build time; -1 never
        public bool DepotKnown { get; }          // DepotX, DepotZ valid
        public float DepotX { get; }             // global metres: the standing depot nearest the operation fix
        public float DepotZ { get; }

        public TheaterWaveView(WaveState state, WaveBlock block, int credits, int creditsPerWave, string group, int cost,
            int pool, int cooldownSeconds, int bought, float lastBoughtAgo, bool depotKnown, float depotX, float depotZ)
        {
            State = state; Block = block; Credits = credits; CreditsPerWave = creditsPerWave; Group = group ?? string.Empty;
            Cost = cost; Pool = pool; CooldownSeconds = cooldownSeconds; Bought = bought; LastBoughtAgo = lastBoughtAgo;
            DepotKnown = depotKnown; DepotX = depotX; DepotZ = depotZ;
        }
    }

    /// <summary>The newest finished sortie. Clients diff Seq (and Wave.Bought) to raise one cockpit callout each.</summary>
    internal sealed class TheaterOutcomeView
    {
        public int Seq { get; }                  // strictly increasing per faction war, at least 1
        public string Title { get; }             // at most 40
        public string Pilot { get; }             // at most 32
        public float X { get; }                  // global metres, finite
        public float Z { get; }
        public bool Counted { get; }             // credited to an operation (live or inside the 120 s window)
        public bool ByWing { get; }
        public int Credits { get; }              // bank after this outcome, 0..6

        public TheaterOutcomeView(int seq, string title, string pilot, float x, float z, bool counted, bool byWing, int credits)
        {
            Seq = seq; Title = title ?? string.Empty; Pilot = pilot ?? string.Empty; X = x; Z = z;
            Counted = counted; ByWing = byWing; Credits = credits;
        }
    }

    internal sealed class TheaterWarSnapshot
    {
        public string Faction = "";
        public TheaterWarPosture Posture;
        public TheaterCaps Caps;
        public TheaterEmptyReason Reason;
        public float ReasonSeconds;                                 // e.g. seconds to the first deal
        public float SensedAgeSeconds;                              // age of fronts and counts when published (mission clock)
        public float StaffPickSeconds = -1f;                        // -1 none; seconds to the staff's pick while it is armed
        public GroundReason GroundReason = GroundReason.Unknown;    // Unknown when not host-measured
        public TheaterFrontView[] Fronts = Array.Empty<TheaterFrontView>();         // live fronts, pressure descending, at most 8
        public TheaterProposalView[] Proposals = Array.Empty<TheaterProposalView>(); // the deck, at most 3
        public TheaterOperationView Operation;                      // null when none
        public TheaterWaveView Wave;                                // null when the desk is unavailable
        public TheaterResultView LastResult;                        // newest ended operation, null if none
        public TheaterOutcomeView LastOutcome;                      // newest finished sortie, null if none
        public TheaterCensus Census = TheaterCensus.Unmeasured;
        public string[] Log = Array.Empty<string>();                // newest first, at most 8, each <= 64
    }

    internal interface ITheaterWarView
    {
        bool Available { get; }                       // the local module is installed and switched on
        TheaterViewState State { get; }
        TheaterWarSnapshot Snapshot { get; }          // null until the first host report; never mutated after publication
        bool HasSnapshot { get; }
        float SnapshotAgeSeconds { get; }             // -1 none; host on the mission clock, client send-age plus unscaled receipt time
        float SecondsSincePublished { get; }          // -1 none; decay base for the seconds-fields of Snapshot
        bool IsFresh { get; }                         // age in 0..15
        bool CommandPending { get; }
        TheaterCommandResult LastCommand { get; }     // null once older than 6 s (unscaled)
        bool CanCommand { get; }
        TheaterWarPosture Posture { get; }            // thin getters over Snapshot
        IReadOnlyList<TheaterFrontView> Fronts { get; }
        IReadOnlyList<TheaterProposalView> Proposals { get; }
        TheaterOperationView ActiveOperation { get; }
        void Refresh();
        bool RequestPick(int proposalId, int revision);      // set or redirect the main effort
        bool RequestCancel(int operationId, int revision);   // CALL OFF; vetoes the front for 180 s
        bool RequestDraw(float startX, float startZ, float fixX, float fixZ);   // a pilot-drawn offensive: press on a front, release on the objective (global metres)
        bool RequestPosture(TheaterWarPosture posture);
    }
}
