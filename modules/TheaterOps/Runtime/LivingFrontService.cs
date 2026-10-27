using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Modules.TheaterOps.Networking;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>Who sent a staff command: the id the lock compares and the callsign it prints. A staff pick has the default actor.</summary>
    internal readonly struct IntentActor
    {
        internal readonly ulong Id;
        internal readonly string Callsign;
        internal IntentActor(ulong id, string callsign) { Id = id; Callsign = callsign ?? ""; }
    }

    /// <summary>The host's typed answer to a command (annex 01 E7).</summary>
    internal readonly struct IntentReply
    {
        internal readonly TheaterCommandCode Code;
        internal readonly string Detail;
        internal readonly float LockSeconds;
        internal IntentReply(TheaterCommandCode code, string detail = "", float lockSeconds = 0f) { Code = code; Detail = detail ?? ""; LockSeconds = lockSeconds; }
    }

    /// <summary>
    /// One host-run staff per faction. It reads real units and front geometry, keeps a live deck of up to three operations, runs the pilot's pick
    /// and reports it all in one immutable snapshot (annex 01). It is also the ground service's window on the war (IGroundWarSource) and reads the ground report back
    /// for the phase, the forces and the NoForces rule.
    /// </summary>
    internal sealed partial class LivingFrontService : MonoBehaviour, ISceneService, ITheaterWarView, ITheaterAirStationView,
        ITheaterWarHost, IOperationFocusSource, IGroundWarSource
    {
        internal static LivingFrontService Active { get; private set; }
        internal bool Authoritative => settings != null && settings.Enabled.Value && GameAccess.IsServer();

        public event Action<int, float> MoraleAwarded;

        private const int MaximumFactions = 8;
        private const int MaximumUnitScan = 4096;
        private const int MaximumObjectiveScan = 128;
        private const int MaximumObjectiveFronts = 4;
        private const int MaximumRemembered = 16;

        private readonly FrontlineTracePoint[] tracePoints = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] traceLengths = new int[FrontlineTraceLimits.MaximumTraces];
        private readonly float[] tracePressure = new float[FrontlineTraceLimits.MaximumTraces];
        private readonly float[] pointDistance = new float[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] pointOrder = new int[FrontlineTraceLimits.MaximumPoints];
        private readonly List<ContactFix> fixes = new List<ContactFix>(512);
        private readonly List<Vector2> ownGround = new List<Vector2>(256);
        private readonly List<FrontCandidate> seeds = new List<FrontCandidate>(LivingWarRules.MaximumFronts);
        private readonly List<WarOffer> ranked = new List<WarOffer>(LivingWarRules.MaximumFronts);
        private readonly List<DeckOffer> offers = new List<DeckOffer>(LivingWarRules.MaximumFronts);
        private readonly List<string> forgotten = new List<string>(MaximumRemembered);
        private readonly Dictionary<string, string> placeNames = new Dictionary<string, string>(64);
        private readonly Dictionary<string, FactionWar> wars = new Dictionary<string, FactionWar>(MaximumFactions);
        private readonly List<FactionHQ> hqs = new List<FactionHQ>(MaximumFactions);
        private readonly FaultLedger holdFaults = new FaultLedger();
        private ITerritoryIngress territory;
        private IThreatPicture picture;
        private IAircraftTaskExclusion excluded;
        private ITheaterCensusSource censusSource;
        private TheaterOpsSettings settings;
        private TheaterPriorityService priority;
        private LivingFrontNet network;
        private NavalFrontService naval;
        private PhasedGate rollGate;
        private int cursor;
        private TheaterCommandResult hostCommand;
        private float hostCommandAt;
        private FactionHQ viewHq;
        // The guard bodies are cached delegates over these fields, so a review and a hold read allocate nothing.
        private FactionWar reviewWar, holdWar, vetoWar;
        private float reviewNow, vetoNow;
        private Action reviewBody, holdBody;
        private Func<WarFrontRead, bool> isVetoed;

        internal sealed class FrontCandidate
        {
            internal string Key, Name, PlaceId;
            internal float X, Z, TracePressure;
            internal FrontSource Source;
            internal bool Held, Live;
        }

        internal sealed class Operation
        {
            internal int Id, Revision, InitialGround;
            internal string Key, Name, PlaceId;
            internal TheaterOpKind Kind;
            internal FrontSource Source;
            internal TheaterCarrier Carrier;
            internal bool CarrierBased, HasHold;
            /// <summary>A pilot drew it: the fix stays on the drawn objective (it does not follow the front) and the hold is read there.</summary>
            internal bool Drawn;
            internal float X, Z, OriginX, OriginZ, Started, InitialHold, HoldNow, PhaseSince, Progress = -1f;
            internal TheaterPhase Phase;
            internal TheaterPhaseNote PhaseNote;
            internal Sustained Gain, Loss, Clear, NotLive, NoForces;
            /// <summary>The ground report as of the last review, never mutated after it is built (the snapshot shares it).</summary>
            internal TheaterForcesView Forces = new TheaterForcesView();
            // Who picked it and until when other pilots are locked out. A staff pick locks nobody.
            internal ulong PickedById;
            internal string PickedBy = "";
            internal bool PickedByStaff;
            internal float LockUntil;
        }

        /// <summary>What the staff remembers of a front between reviews: its pressure (for the trend) and its status (for the log line when it flips).</summary>
        internal struct FrontMemo { internal float Pressure; internal TheaterFrontStatus Status; }

        internal sealed class FactionWar
        {
            internal FactionHQ HQ;
            internal readonly List<FrontCandidate> Candidates = new List<FrontCandidate>(LivingWarRules.MaximumFronts);
            internal readonly List<WarFrontRead> Reads = new List<WarFrontRead>(LivingWarRules.MaximumFronts);
            internal readonly List<Place> Places = new List<Place>(32);
            internal readonly List<DeckCard> Deck = new List<DeckCard>(DeckRules.Slots);
            internal readonly List<TheaterFrontView> Fronts = new List<TheaterFrontView>(LivingWarRules.MaximumFronts);
            internal readonly VetoBook Vetoes = new VetoBook();
            internal readonly StaffLog Log = new StaffLog();
            internal readonly FaultLedger Faults = new FaultLedger();
            internal readonly Dictionary<string, FrontMemo> Baseline = new Dictionary<string, FrontMemo>(MaximumRemembered);
            internal readonly Dictionary<string, float> ObservedAt = new Dictionary<string, float>(MaximumRemembered);
            internal readonly Dictionary<string, float> PaidUntil = new Dictionary<string, float>(MaximumRemembered);
            internal TheaterWarPosture Posture = TheaterWarPosture.Steady;
            internal Operation Active;
            internal int Revision, NextId = 1, ObjectiveCursor;
            internal float NextReview, NextHold, FirstDealAt = -1f, OfferDeadline, SensedAt = -1f, FrozenUntil, LastVetoUntil;
            internal bool DeadlineArmed, InReview, GroundSeen, HasObjectives;
            internal RejectTally Tally;
            internal GroundReason GroundShown = GroundReason.Unknown, GroundPending = GroundReason.Unknown;
            internal TheaterCensus Census = TheaterCensus.Unmeasured;
            internal TheaterResultView LastResult;
            internal float LastResultAt;
            /// <summary>The picture the pilot sees and the wire carries: built by the host after every review and accepted command, received on a client.</summary>
            internal TheaterWarSnapshot Published;
            /// <summary>Host: the mission clock when published. Client: the unscaled clock at receipt.</summary>
            internal float PublishedAt;
            // Proof counters (WarProof reads them; nothing else does).
            internal int HostileFixes, PostsHit;
            internal int StaffPicks, PilotPicks, StaffOverrides, Redirects, TrendWrites, RewardPaidTotal, ResultFunds;
            internal float FirstDeckAt = -1f, StaffPickedAt = -1f, AgeAtRefreshMax = -1f;
        }

        internal void Configure(TheaterOpsSettings config, TheaterPriorityService priorityService, LivingFrontNet transport,
            NavalFrontService navalService)
        {
            settings = config;
            desk.Configure(config);
            priority = priorityService;
            network = transport;
            naval = navalService;
            reviewBody = RunFaction;
            holdBody = () => ReadHold(holdWar);
            isVetoed = front => vetoWar.Vetoes.IsVetoed(front.Key, front.X, front.Z, vetoNow, LivingWarRules.FollowRadius);
        }

        private void Awake() => Active = this;
        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            ResetForScene();
        }

        public void ResetForScene()
        {
            wars.Clear(); hqs.Clear(); placeNames.Clear(); ranked.Clear(); offers.Clear(); fixes.Clear(); seeds.Clear();
            territory = null; picture = null; excluded = null; censusSource = null;
            UnbindSorties();
            reviewWar = holdWar = vetoWar = null;
            scanWar = null; scanned = false;
            rollGate.Reset(); cursor = 0;
            viewHq = null; hostCommand = null;
            network?.ResetScene();
        }

        private static bool MissionOver => GameManager.gameResolution != GameResolution.Ongoing;

        private void Update()
        {
            // After the end screen nothing runs: no review, no reward, no message, no directive (annex 01 E10).
            if (!Authoritative || MissionOver) return;
            float now = Time.timeSinceLevelLoad;
            if (rollGate.Due(now, 1f, 0.625f)) RollCall();   // 1 Hz slot .625 (table in HighCommandManager)
            // The control field advances only when somebody reads it, so a running operation reads its origin once a second.
            foreach (FactionWar running in wars.Values)
                if (running.Active != null && now >= running.NextHold)
                {
                    running.NextHold = now + LivingWarRules.HoldTickSeconds;
                    holdWar = running;
                    Guard.Run(holdFaults, now, holdBody, LogFault, "hold read");
                }
            // A review waits for its unit scan, which takes a few frames on a big mission (the lists are shared: one review at a time).
            if (scanWar != null) { FinishScanStep(now); return; }
            if (hqs.Count == 0) return;
            // One faction per frame keeps the unit and front scans bounded.
            if (cursor >= hqs.Count) cursor = 0;
            FactionHQ hq = hqs[cursor++];
            if (hq == null || hq.faction == null) return;
            FactionWar war = StateOf(hq.faction.factionName);
            if (war == null) return;
            war.HQ = hq;
            if (now >= war.NextReview && now >= war.FrozenUntil) { BeginScan(war, now); FinishScanStep(now); }
        }

        /// <summary>One slice of the scan in flight; the review follows in the frame the scan ends (the first slice shares the frame it began in).</summary>
        private void FinishScanStep(float now)
        {
            if (!StepScan(ScanUnitsPerFrame)) return;
            FactionWar war = scanWar;
            scanWar = null;
            scanned = true;
            try { Review(war, now); }
            finally { scanned = false; }
        }

        private void RollCall()
        {
            hqs.Clear();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hqs.Count >= MaximumFactions) break;
                if (hq == null || hq.faction == null || string.IsNullOrEmpty(hq.faction.factionName)) continue;
                hqs.Add(hq);
            }
        }

        private FactionWar StateOf(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return null;
            if (wars.TryGetValue(faction, out FactionWar existing)) return existing;
            if (wars.Count >= MaximumFactions) return null;
            var state = new FactionWar { FirstDealAt = Time.timeSinceLevelLoad + LivingWarRules.FirstDealSeconds };
            wars.Add(faction, state);
            return state;
        }

        private FactionWar LocalState()
        {
            if (!GameAccess.TryGetLocalFaction(out FactionHQ hq) || hq?.faction == null) return null;
            return wars.TryGetValue(hq.faction.factionName, out FactionWar war) ? war : null;
        }

        internal void ClearRemoteState()
        {
            if (GameAccess.IsServer()) return;
            wars.Clear(); hostCommand = null;
        }

        private static void LogFault(string text) => Plugin.Logger?.LogWarning(text);

        private void ResolveServices()
        {
            if (territory == null) ModuleServices.TryGet(out territory);
            if (picture == null) ModuleServices.TryGet(out picture);
            if (excluded == null) ModuleServices.TryGet(out excluded);
            if (censusSource == null) ModuleServices.TryGet(out censusSource);
            BindSorties();
        }

        /// <summary>One guarded review. A fault backs the next one off, ends an operation past its ceiling without sensing, and publishes the unhealthy picture.</summary>
        private void Review(FactionWar war, float now)
        {
            reviewWar = war; reviewNow = now;
            if (Guard.Run(war.Faults, now, reviewBody, LogFault, war.HQ.faction.factionName))
            {
                war.NextReview = NextReviewAfter(war, now);
                return;
            }
            war.NextReview = now + war.Faults.NextDelay(LivingWarRules.ReviewSeconds);
            EnforceCeiling(war, now);
            if (!war.Faults.Healthy && war.SensedAt >= 0f)
                try { Publish(war, now); }
                catch (Exception) { /* the last good picture stays up and its age keeps growing, which is the honest state of a faulting staff */ }
        }

        /// <summary>Every 10 s, except that the first deck is due at 15 s and the staff's pick at its deadline, both finer than the cadence (so this runs after the review armed it).</summary>
        private static float NextReviewAfter(FactionWar war, float now)
        {
            float next = now + LivingWarRules.ReviewSeconds;
            if (now < war.FirstDealAt) return Math.Min(next, war.FirstDealAt);
            return war.DeadlineArmed && war.Active == null ? Math.Min(next, Math.Max(now + 1f, war.OfferDeadline)) : next;
        }

        /// <summary>
        /// Order matters: Collect before Publish before AdvanceOperation before Maintain before the staff pick, so a card the operation just consumed is
        /// excluded in the same pass and a freshly ended operation's key is already vetoed when the deck is refilled.
        /// </summary>
        private void RunFaction()
        {
            FactionWar war = reviewWar;
            float now = reviewNow;
            float age = SnapshotAge.Host(now, war.SensedAt);
            if (age > war.AgeAtRefreshMax) war.AgeAtRefreshMax = age;
            war.InReview = true;
            try
            {
                Collect(war, now);
                PublishFronts(war, now);
                Debounce(war);
                war.Census = censusSource != null && censusSource.TryMeasure(war.HQ, out TheaterCensus census) ? census : TheaterCensus.Unmeasured;
                AdvanceOperation(war, now);
                ReviewDesk(war, now);
                Maintain(war, now);
                StaffPick(war, now);
                DirectNaval(war);
                Publish(war, now);
            }
            finally { war.InReview = false; }
        }

        /// <summary>The 600 s ceiling holds even when sensing faults: an operation past it ends STALLED without a Collect.</summary>
        private void EnforceCeiling(FactionWar war, float now)
        {
            if (war.Active == null || now - war.Active.Started < LivingWarRules.OperationLimitSeconds) return;
            try { EndOperation(war, TheaterOutcome.Stalled, TheaterWhy.TimeLimit, now, ""); }
            catch (Exception) { war.Active = null; }
        }

        private void ReadHold(FactionWar war)
        {
            Operation op = war?.Active;
            if (op == null || territory == null) return;
            op.HasHold = territory.TryGetHoldStrength(war.HQ.GetInstanceID(), op.Drawn ? op.X : op.OriginX, op.Drawn ? op.Z : op.OriginZ, out float hold);
            if (op.HasHold) op.HoldNow = hold;
        }

        private void DirectNaval(FactionWar war)
        {
            Operation op = war.Active;
            if (op == null || naval == null) return;
            NavalRole role = op.Kind == TheaterOpKind.Defend ? NavalRole.Screen
                : op.Kind == TheaterOpKind.Recon ? NavalRole.Patrol : NavalRole.CoastalSupport;
            naval.SetObjective(war.HQ, op.Key, new GlobalPosition(op.X, 0f, op.Z), role);
        }

        private void ClearOperationDirective(FactionWar war)
        {
            if (priority != null && war.Active != null &&
                priority.TryGetDirective(war.HQ.faction.factionName, out PriorityDirective current) &&
                current.Key == war.Active.Key) priority.ClearDirective(war.HQ);
        }

        private static bool HasHuman(FactionHQ hq) => hq != null && hq.factionPlayers != null && hq.factionPlayers.Count > 0;

        /// <summary>Read-only view of every faction's staff for the sim proof hooks; the view properties advance nothing, this does not either.</summary>
        internal IReadOnlyDictionary<string, FactionWar> Wars => wars;
    }
}
