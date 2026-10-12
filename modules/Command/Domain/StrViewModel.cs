using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>What the BOARD tab shows. Each value has its own banner words in StrWords.</summary>
    internal enum StrBoardMode : byte
    {
        Operation, Proposals, Assessing, NoOpening, Reduced,
        StaffOff, HostLacksStaff, NoFaction, Waiting, NoAnswer,
    }

    /// <summary>Ink roles shared by the panel and the map; StrInk maps each to a kit role or a data colour.</summary>
    internal enum StrInkRole : byte { Effort, Own, Hostile, Offered, Strike, Wave, Caution, Danger, Muted }

    /// <summary>Three-valued fact: an unknown is never drawn as a no.</summary>
    internal enum StrTri : sbyte { Unknown = -1, No = 0, Yes = 1 }

    /// <summary>One lane: a front, or the air picture in a mission with no ground war.</summary>
    internal sealed class StrFrontRow
    {
        /// <summary>Front id: kind Front, key = platform number.</summary>
        public StrItemId Id;
        /// <summary>"P2" and the place name. Numbers are first-sighting labels (StrOrdinals), not ranks.</summary>
        public string Tag = "", Name = "";
        /// <summary>Global metres of the anchor; NaN when unknown.</summary>
        public float X = float.NaN, Z = float.NaN;
        /// <summary>Hostile share of forces near the front, 0..1; NaN unless observed. Trend: change over the last review, -1..1; NaN unknown.</summary>
        public float Pressure = float.NaN, Trend = float.NaN;
        public bool Observed;
        /// <summary>Words ready to print: "62%" or a dash, a rising or falling arrow, "UNDER PRESSURE", "NOT OBSERVED, 6 MIN OLD".</summary>
        public string PressureText = "—", TrendText = "", StatusText = "";
        public StrInkRole Ink;
        /// <summary>The proposal that targets this front, or None; OfferText is the lane's button or tag word ("O2 DEFEND, MEDIUM", "NO OFFER", "NO REPORT").</summary>
        public StrItemId Offer;
        public string OfferText = "";
        /// <summary>Gate of REDIRECT on this lane (allowed only with a live operation and an offer here).</summary>
        public StrAct Redirect;
    }

    /// <summary>One of the staff's (at most three) proposals.</summary>
    internal sealed class StrProposalRow
    {
        public StrItemId Id;
        /// <summary>Engine ids for the verb call.</summary>
        public int ProposalId, Revision;
        /// <summary>"O1", "ASSAULT", place, risk word, composed aim line, wave chip.</summary>
        public string Tag = "", KindText = "", Name = "", RiskText = "", Aim = "", WaveText = "";
        public TheaterOpKind Kind;
        public TheaterRisk Risk;
        /// <summary>Global metres of the fix, ring radius in metres, hostile share of the front (NaN unknown).</summary>
        public float X = float.NaN, Z = float.NaN, Radius, Pressure = float.NaN;
        public StrItemId Front;
        public bool Recommended;
        public StrTri WaveAffordable = StrTri.Unknown;
        /// <summary>Gate of PICK (also the redirect while an operation is live).</summary>
        public StrAct Pick;
    }

    /// <summary>One ground group of the live operation, nearest the fix first.</summary>
    internal struct StrGroupRow
    {
        /// <summary>"G1": rank by distance in this report, not a game id.</summary>
        public string Tag, DistanceText;
        public float X, Z, DistanceM;
        /// <summary>Engine stage (0..5) and alive count byte as published.</summary>
        public byte Stage, Alive;
    }

    /// <summary>The live operation, the expanded lane of the board.</summary>
    internal sealed class StrEffortView
    {
        public bool Live;
        public StrItemId Id;
        public int OperationId, Revision;
        public TheaterOpKind Kind;
        /// <summary>"01", "MAIN EFFORT", the operation's place, its front's platform tag, the phase word, "SET BY VIPER 1-1" or "SET BY STAFF".</summary>
        public string Tag = "", KindText = "", Name = "", FrontTag = "", PhaseText = "", PickedText = "";
        /// <summary>Route steps for the kind (3 for ASSAULT, 2 otherwise) and the current step (0 before the first); Withdrawing sets Alarm.</summary>
        public int RouteSteps, RouteStep;
        public bool Alarm;
        /// <summary>Seconds of lock left (whole seconds, -1 none) and its "01:32" text; the local pilot is the picker.</summary>
        public int LockSeconds = -1;
        public string LockText = "";
        public bool PickedByLocal, PickedByStaff;
        /// <summary>Callsign of whoever set the effort ("" when the staff did or nobody is known), for the lock refusal.</summary>
        public string PickedBy = "";
        /// <summary>Fix and the operation's origin in global metres, ring radius in metres.</summary>
        public float X = float.NaN, Z = float.NaN, OriginX = float.NaN, OriginZ = float.NaN, Radius;
        /// <summary>Hostile share and trend of the operation's front (NaN unknown) and the lane words.</summary>
        public float Pressure = float.NaN, Trend = float.NaN;
        public string PressureText = "—", TrendText = "";
        /// <summary>True while ADVANCING: the map axis marches only then.</summary>
        public bool Advancing;
        /// <summary>"11 VEH", "3 GRP", "MOVING 2 / ON SLOT 1 / STALLED 0", "ETA": each part a dash when unknown.</summary>
        public string VehiclesText = "—", GroupsText = "—", MovementText = "—", EtaText = "—";
        public StrGroupRow[] Marks = new StrGroupRow[StrViewModel.MaxMarks];
        public int MarkCount;
        /// <summary>Gate of REPLAN (CALLS OFF) and whether the first press is armed.</summary>
        public StrAct Replan;
        public bool ReplanArmed;
    }

    /// <summary>One sortie ticket.</summary>
    internal sealed class StrSortieRow
    {
        public StrItemId Id;
        /// <summary>Card id for the verb calls; the post (office) id for a STRIKE ticket, -1 for any other card.</summary>
        public int ContractId, PostId;
        /// <summary>"S2", family chip ("STRIKE"), title, "14 KM 032" or a dash, "4,200 +120 XP", "09:40" or a dash, "OFFERED", "ACCEPTED", "BY VIPER 1-1".</summary>
        public string Tag = "", ChipText = "", Title = "", DistanceText = "—", RewardText = "", TimeText = "—", StateText = "", ByText = "";
        public bool Offered, Active, SiteKnown;
        /// <summary>Global metres of the site and its ring (0..1500); progress 0..1 (NaN unknown).</summary>
        public float X = float.NaN, Z = float.NaN, Radius, Progress = float.NaN;
        public StrAct Accept;
        /// <summary>SEND WING, COVER, SCOUT or RECALL; the wing line ("WING TASKED"); the gate.</summary>
        public string WingLabel = "SEND WING", WingText = "";
        /// <summary>The host's cached refusal of SEND WING for this card, "" when it would be accepted (the wing gate reads it).</summary>
        public string WingRefusal = "";
        /// <summary>The wing entry for this card exists (host only), and the state it reports.</summary>
        public bool HasWing;
        public WingSortieState WingState;
        public StrAct Wing;
    }

    /// <summary>One confirmed enemy command post. N-08: enemy rows carry no internal state, so no Away and no Destroyed.</summary>
    internal sealed class StrPostRow
    {
        public StrItemId Id;
        public int PostId;
        /// <summary>"COL. IMRE DANKOV", "GROUND COMPONENT CMDR", the display site, "BOUNTY 3,000", "VETERAN +20% BOUNTY", "SEEN 6 MIN AGO", "DOSSIER".</summary>
        public string Name = "", Role = "", Site = "", BountyText = "", TraitText = "", SeenText = "", Short = "";
        public bool Capped;
        public float X, Z;
        /// <summary>The STRIKE ticket for this post, or None.</summary>
        public StrItemId Ticket;
    }

    /// <summary>The reinforcement wave (R4): earned count, price, pool, whether the pool covers it, and why it is held.</summary>
    internal sealed class StrWaveView
    {
        public bool Present;
        public int Credits, PerWave;
        /// <summary>-1 unknown (printed as a dash).</summary>
        public int Cost = -1, Pool = -1;
        public StrTri Affordable = StrTri.Unknown;
        /// <summary>"EARNING 2/3", "READY", "HELD, POOL SHORT"; the group name; "POOL 7,400 to 5,550" or "POOL 7,400, NEEDS 1,850"; the last-wave line.</summary>
        public string StateText = "", GroupText = "", PoolText = "", LastText = "";
        public bool Held;
        /// <summary>Depot position for the wave route line; global metres.</summary>
        public bool DepotKnown;
        public float DepotX, DepotZ;
    }

    /// <summary>One header tile: key, value, caption, bar, state, the one-sentence help of what it verified.</summary>
    internal struct StrTile
    {
        public string Key, Value, Caption, Help;
        /// <summary>0..1, NaN = no bar.</summary>
        public float Bar;
        public NOAvionics.AvState State;
    }

    /// <summary>A tug-of-war balance bar of the SITREP tab: parts sum to 1; unknown parts are NaN.</summary>
    internal struct StrBalance
    {
        public string Key, Text, Caption;
        public float Own, Middle, Hostile;
    }

    /// <summary>One checklist line of the no-opening banner.</summary>
    internal struct StrCheck
    {
        public string Label, Do;
        public StrTri Pass;
    }

    /// <summary>The banner above the board: why there is no board, or what the staff is doing, with a countdown and an optional checklist.</summary>
    internal sealed class StrBanner
    {
        public bool Show;
        /// <summary>Slab word, headline, why, what to do; the last two are "" when the mode needs none.</summary>
        public string Chip = "", Head = "", Why = "", Do = "";
        /// <summary>Countdown in whole seconds (-1 none) and its label ("AUTO-PICK O1 IN", "NEXT REVIEW IN").</summary>
        public int CountSeconds = -1;
        public string CountLabel = "";
        public StrCheck[] Checks = new StrCheck[StrViewModel.MaxChecks];
        public int CheckCount;
    }

    /// <summary>Everything the panel and the overlay display, filled in place by StrViewBuilder.</summary>
    internal sealed class StrViewModel
    {
        public const int MaxProposals = 3, MaxFronts = 8, MaxSorties = 5, MaxPosts = 6, MaxMarks = 8, MaxLog = 8, MaxChecks = 6;

        /// <summary>Bumps only when a displayed value changed.</summary>
        public int Revision;
        public StrBoardMode Mode;
        /// <summary>The last report is old (engine state Stale): the board still shows it, verbs are off, a banner says so.</summary>
        public bool Stale;
        /// <summary>Real age of the war report in seconds, NaN unknown, and its text ("4s").</summary>
        public float AgeSeconds = float.NaN;
        public string AgeText = "—";
        public bool CanCommand, IsHost;
        /// <summary>The staff posture in force (the posture row's selected segment).</summary>
        public TheaterWarPosture Posture;
        /// <summary>Contracts accepted now and the ceiling the host reports (0 = no ceiling), for the BoardFull gate.</summary>
        public int SortieActiveCount, SortieActiveLimit;
        /// <summary>The sortie feed is present and fresh (accept and wing buttons need it) and a sortie action is pending.</summary>
        public bool SortiesLive, SortieActionPending, WarPending;
        /// <summary>The sortie feed exists at all (a stale feed is Available but not Live).</summary>
        public bool SortiesAvailable;
        public ulong LocalId;
        public string FactionCode = "";
        public StrBanner Banner = new StrBanner();
        public StrEffortView Effort = new StrEffortView();
        public StrProposalRow[] Proposals = new StrProposalRow[MaxProposals]; public int ProposalCount;
        public StrFrontRow[] Fronts = new StrFrontRow[MaxFronts]; public int FrontCount;
        public StrSortieRow[] Sorties = new StrSortieRow[MaxSorties]; public int SortieCount;
        /// <summary>Sorties tied to the operation beyond what the board draws ("+n MORE ON THE MAP").</summary>
        public int SortieMore;
        public StrPostRow[] Posts = new StrPostRow[MaxPosts]; public int PostCount;
        public StrWaveView Wave = new StrWaveView();
        public StrTile[] Tiles = new StrTile[4];
        public StrBalance Sectors, Air, Ground, Airbases;
        public string RadarsText = "—", KnownAdText = "—", FrontText = "—", ContestedBasesText = "—";
        /// <summary>DEFCON level 1..5 (0 = not measured), its word and the rule that set it.</summary>
        public int AlertLevel;
        public string AlertWord = "NOT MEASURED", AlertRule = "";
        /// <summary>The AI sortie board: code, count (-1 unknown), share 0..1 (NaN unknown); caption names whom it counts.</summary>
        public string[] SortieCodes = { "CAP", "SEAD", "CAS", "STRIKE", "TRANSIT" };
        public int[] SortieCounts = { -1, -1, -1, -1, -1 };
        public float[] SortieShares = { float.NaN, float.NaN, float.NaN, float.NaN, float.NaN };
        public string SortieBoardCaption = "";
        public string[] Log = new string[MaxLog]; public int LogCount;
        /// <summary>Footer strip: line 1 states status and age, line 2 is the flavour or the latest result.</summary>
        public string FooterStatus = "", FooterNote = "";
        /// <summary>Selected and hovered ids resolved against the rows; SelectionVanished asks the hub to clear its copy.</summary>
        public StrItemId SelectedId, HoverId;
        public bool SelectionVanished;

        // StrViewBuilder's memo of the last build: equal stamp, second, ui and clock-driven flags build nothing.
        internal bool Built;
        internal int BuiltStamp;
        internal long BuiltSecond;
        internal bool BuiltNotice, BuiltArmed;
        internal StrUiState BuiltUi;
        internal int BuiltSince, BuiltAge, BuiltWait, BuiltPostsAge;   // whole seconds of the decaying feed numbers
    }
}
