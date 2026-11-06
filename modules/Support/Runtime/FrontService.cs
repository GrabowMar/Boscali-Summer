using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The verdict of one front command as the host replies it (the client words it with <see cref="FrontWords"/>).</summary>
    internal readonly struct FrontResult
    {
        public readonly FrontOutcome Outcome;
        public readonly int Detail, Charged;
        public readonly string By;
        public FrontResult(FrontOutcome outcome, int detail = 0, int charged = 0, string by = "") { Outcome = outcome; Detail = detail; Charged = charged; By = by ?? ""; }
        public bool Ok => FrontWords.Good(Outcome);
    }

    /// <summary>
    /// OPS FRONTS S1b: the host owner of every faction's three fronts (spec 3, 5). One <see cref="FrontBook"/> per faction (seeded with <c>Support/FrontSeed</c>), AI-only factions included.
    /// Every 60 s a share of the faction's vanilla funds moves into the fronts; every 5 s the real services are read into a <see cref="FrontWorld"/>, an idle front's quartermaster queues its
    /// next ladder step (organic fronts 4.6: a human front after 180 s with nothing queued by a human, an AI front whenever its queue is empty, then its doctrine), the books tick and each
    /// finished programme is placed through the services (pads, data centers, trucks, camps) or run as an effect. A queued build is checked twice: the ladder and the service's placement
    /// rule at queue time, the service again at completion. Superiority is recomputed against the strongest enemy and, at +40, presses the enemy front it beats (SPACE on SOF exposure,
    /// CYBER on SPACE cooldowns, SOF on CYBER trace). The same service answers the faction's commands, builds its mirror and gates perks (<see cref="IFrontPower"/>): the host reads the
    /// books, a client reads the mirror.
    /// </summary>
    internal sealed class FrontService : MonoBehaviour, ISceneService, IFrontPower
    {
        private const int MaximumFactions = 8;
        private const float WorldSeconds = 5f, SnapshotSeconds = 1f;

        private sealed class FactionFront
        {
            public FactionHQ Owner;
            public FrontBook Book;
            public FrontWorld World;
            public FrontSides Own;
            public readonly bool[] CounterOn = new bool[FrontRules.FrontCount];
            public CounterPressure Pressure = CounterPressure.None;
            public FrontStateData Cache;
            public float CacheAt = float.NegativeInfinity;
            /// <summary>An AI faction queues its next ASAT no sooner than this (one every <see cref="AiAsatSpacingSeconds"/>).</summary>
            public float NextAiAsat;
            /// <summary>[front, programme]: the quartermaster skips this automatic step until this mission second (it found no room; ruling R20).</summary>
            public readonly float[,] BlockedUntil = new float[FrontRules.FrontCount, FrontRules.Programmes.Length];
        }

        private const float AiAsatSpacingSeconds = 1200f;
        /// <summary>An automatic pool build that found no room (NO SPOT, NO SITE, NOT HELD) is not tried again by the quartermaster for this long.</summary>
        private const float BlockedSeconds = 600f;

        private readonly Dictionary<FactionHQ, FactionFront> factions = new Dictionary<FactionHQ, FactionFront>();
        private readonly List<FactionFront> order = new List<FactionFront>(MaximumFactions);
        /// <summary>Scratch: a front's queued data-center points for the EW TRUCK queue check.</summary>
        private readonly List<(float X, float Z)> queuedCenters = new List<(float X, float Z)>(FrontRules.MaxQueue);
        private SupportManager manager;
        private SpaceService space;
        private CyberService cyber;
        private SofService sof;
        private OpsService ops;
        private PhasedGate worldGate;
        private float nextShare = -1f, nextWarning;

        internal static FrontService Active { get; private set; }

        /// <summary>True while the host ticks the fronts (the anchor rebuilds then wait for their programme).</summary>
        internal bool Running { get; private set; }

        public void Configure(SupportManager support, SpaceService spaceService, CyberService cyberService, SofService sofService, OpsService opsService)
        {
            manager = support; space = spaceService; cyber = cyberService; sof = sofService; ops = opsService;
            Active = this;
            manager.ConfigureFronts(this);
            manager.AttachFronts(this);
        }

        public void ResetForScene()
        {
            ClearPressure();
            factions.Clear();
            order.Clear();
            Running = false;
            nextShare = -1f;
            worldGate.Reset();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            ResetForScene();
        }

        // ---- Reads ---------------------------------------------------------------------------------

        /// <summary>The faction has a programme of this kind queued or building (the anchor rebuild waits for it).</summary>
        internal bool HasProgramme(FactionHQ owner, ProgrammeId id)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionFront f)) return false;
            for (int i = 0; i < FrontRules.FrontCount; i++) if (f.Book.Side((Front)i).Has(id)) return true;
            return false;
        }

        /// <summary>The enemy CYBER front's pressure on this faction's SPACE task cooldowns (1 = none, 1.5 = CYBER leads by 40 or more).</summary>
        internal float SpaceCooldownFactor(FactionHQ owner) => owner != null && factions.TryGetValue(owner, out FactionFront f) ? f.Pressure.SpaceCooldown : 1f;

        public float Power(FactionHQ hq, Front front)
        {
            if (hq == null) return 1f;
            if (GameAccess.IsServer()) return factions.TryGetValue(hq, out FactionFront f) ? FrontSuperiority.Power(f.Book.Side(front).Superiority) : 1f;
            return IsLocalFaction(hq) ? manager.FrontMirror.Power(front) : 1f;
        }

        /// <summary>Host: the faction's own counts from the last 5 s world read. Empty before the first read or for a client.</summary>
        public FrontSides Facts(FactionHQ hq) => GameAccess.IsServer() && hq != null && factions.TryGetValue(hq, out FactionFront f) ? f.Own : default;

        private static bool IsLocalFaction(FactionHQ hq) => GameManager.GetLocalPlayer<Player>(out Player local) && local != null && ReferenceEquals(local.HQ, hq);

        /// <summary>The director bias a faction's watch brain follows on one front: its directive and focus pin. Neutral until the faction has a book.</summary>
        internal DirectorBias BiasFor(FactionHQ owner, Front front)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionFront f)) return DirectorBias.Neutral(front);
            FrontSide s = f.Book.Side(front);
            return new DirectorBias(s.Directive, s.HasFocus, s.FocusX, s.FocusZ);
        }

        /// <summary>Fills the viewer's faction snapshot (cached for a second). Protocol 0 (nothing to send) when the faction has no book.</summary>
        internal bool FillState(Player viewer, FrontStateData into)
        {
            if (viewer == null || viewer.HQ == null || !Running || !factions.TryGetValue(viewer.HQ, out FactionFront f)) { into.Protocol = 0; return false; }
            float now = SupportManager.MissionNow();
            if (f.Cache == null || now - f.CacheAt >= SnapshotSeconds || now < f.CacheAt)
            {
                f.Cache = f.Book.Snapshot(into.Protocol, 0, now);
                f.CacheAt = now;
            }
            for (int i = 0; i < into.Fronts.Length; i++) into.Fronts[i] = f.Cache.Fronts[i];
            into.PriorityLockUntil = f.Cache.PriorityLockUntil;
            into.PriorityBy = f.Cache.PriorityBy;
            return true;
        }

        /// <summary>Dev readout for the solo acceptance sim: the first other faction's queued programmes and superiority sum, and the viewer's host budget.</summary>
        internal bool EnemyReadout(FactionHQ viewer, out int queued, out int superioritySum, out float hostBudget)
        {
            queued = superioritySum = 0; hostBudget = 0f;
            if (viewer != null && factions.TryGetValue(viewer, out FactionFront mine))
                for (int i = 0; i < FrontRules.FrontCount; i++) hostBudget += mine.Book.Side((Front)i).Budget;
            foreach (FactionFront f in order)
            {
                if (f.Owner == viewer) continue;
                for (int i = 0; i < FrontRules.FrontCount; i++)
                {
                    FrontSide side = f.Book.Side((Front)i);
                    queued += side.Queue.Count; superioritySum += side.Superiority;
                }
                return true;
            }
            return false;
        }

        // ---- Host tick -----------------------------------------------------------------------------

        private bool Enabled => manager?.Settings != null && manager.Settings.Enabled.Value;

        private void Update()
        {
            if (!Enabled)
            {
                if (factions.Count > 0 || Running) ResetForScene();
                return;
            }
            if (!GameAccess.IsServer() || (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)) { Running = false; return; }
            float now = SupportManager.MissionNow();
            if (!SpaceRules.MissionTime(now) || !worldGate.Due(now, WorldSeconds, 0.1875f, 1f)) return;   // every 5 s, at .1875 of the second (the first within a second): clear of the 1 Hz slots (table in HighCommandManager)
            try
            {
                Tick(now);
                Running = true;
            }
            catch (Exception e)
            {
                if (Time.unscaledTime >= nextWarning) { nextWarning = Time.unscaledTime + 10f; Plugin.Logger?.LogWarning("[Support.Fronts] Tick failed: " + e.Message); }
            }
        }

        private void Tick(float now)
        {
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null || factions.ContainsKey(hq) || factions.Count >= MaximumFactions) continue;
                var made = new FactionFront { Owner = hq, Book = new FrontBook(manager.Settings.FrontSeed.Value) };
                factions.Add(hq, made);
                order.Add(made);
            }
            if (nextShare < 0f) nextShare = now + FrontRules.ShareIntervalSeconds;
            float scale = manager.Settings.ProgrammeCostScale.Value;
            foreach (FactionFront f in order) f.Book.CostScale = scale;

            if (now >= nextShare)
            {
                nextShare = now + FrontRules.ShareIntervalSeconds;
                float share = manager.Settings.FrontShare.Value;
                foreach (FactionFront f in order)
                {
                    if (f.Owner == null) continue;
                    float taken = f.Book.ShareTick(f.Owner.factionFunds, share, now, manager.Settings.FrontShareCap.Value);
                    if (taken > 0f) f.Owner.AddFunds(-taken);
                }
            }

            foreach (FactionFront f in order) Sample(f);
            JamFlags();
            foreach (FactionFront f in order)
            {
                if (f.Owner == null) continue;
                Quartermaster(f, now);
                foreach (FrontEvent e in f.Book.Tick(now, f.World)) Apply(f, e, now);
            }
            Superiority(now);
        }

        // ---- The world, read from the real services --------------------------------------------------

        private void Sample(FactionFront f)
        {
            FactionHQ hq = f.Owner;
            var w = new FrontWorld();
            var s = new FrontSides();
            s.UplinksTotal = FrontRules.PadCap; // the pads term reads 0..1 by pads built out of the two slots
            if (space != null && space.TryGetStateCoarse(hq, out SpaceState state))
            {
                w.BirdsUp = (byte)(~state.DownMask & ((1 << SpaceRules.BirdCount) - 1));
                for (int i = 0; i < SpaceRules.BirdCount; i++) if ((w.BirdsUp & (1 << i)) != 0) s.BirdsUp++;
                w.PadsLive = s.UplinksLive = state.LiveUplinkCount;
            }
            if (cyber != null && cyber.TryDesk(hq, out CyberDesk cd))
            {
                int trucks = cd.Anchors.LiveCount(AnchorKind.EwTruck);
                w.DataCentersLive = cd.Anchors.LiveCount(AnchorKind.DataCenter);
                w.TrucksLive = trucks + cyber.PackingTrucks(hq); // a truck packing up still holds its data center (ruling R10) ...
                s.AnchorsLive = w.DataCentersLive + trucks;        // ... but is not live for superiority
                int held = 0; float trace = 0f;
                foreach (CyberIntrusion x in cd.Network.Active) { held += x.HeldCount; trace = Mathf.Max(trace, x.Trace); }
                s.HeldEnemyNodes = held; s.Trace = Mathf.Clamp01(trace / CyberRules.TraceMax);
            }
            if (sof != null && sof.TryDesk(hq, out SofDesk sd))
            {
                w.CampsLive = s.CampsLive = sd.Camps.LiveCount();
                w.Teams = sd.ActiveCount; w.HasHeldBuilding = sd.Held.Count > 0; w.FobUp = sd.FobActive;
                s.HeldBuildings = sd.Held.Count;
                int afield = 0;
                foreach (SofTeam t in sd.Teams)
                    if (t.Active && (t.State == TeamState.Moving || t.State == TeamState.OnSite || t.State == TeamState.Returning || t.State == TeamState.Pinned)) afield++;
                s.TeamsAfield = afield;
            }
            f.World = w;
            f.Own = s;
        }

        /// <summary>This side's CYBER jams an opposing satellite while any enemy faction's bird tasks run slow.</summary>
        private void JamFlags()
        {
            if (cyber == null) return;
            foreach (FactionFront f in order)
            {
                bool jams = false;
                foreach (FactionFront other in order)
                    if (other != f && other.Owner != null && cyber.BirdCooldownFactor(other.Owner) > 1.001f) { jams = true; break; }
                f.Own.JamsOpponent = jams;
            }
        }

        // ---- AI factions -----------------------------------------------------------------------------

        private bool EnemyBirdUp(FactionHQ owner)
        {
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null || space == null) return false;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null || hq == owner || !space.TryGetStateCoarse(hq, out SpaceState s)) continue;
                for (int b = 0; b < SpaceRules.BirdCount; b++) if (s.HasBird((BirdKind)b)) return true;
            }
            return false;
        }

        /// <summary>
        /// The quartermaster (organic fronts 4.6): a human front idle for 180 s with an empty queue climbs its ladder by one step at the automatic spot; a faction with no humans does so
        /// whenever a front's queue is empty. Owner 0 (the pool), never a human queue. A step the service cannot place now (no held airbase) or that is backing off after finding no room
        /// (<see cref="BlockedSeconds"/>) is not queued: a human front waits (it never jumps to a later step that needs this one), an AI front follows its doctrine instead
        /// (<see cref="FrontAi.Doctrine"/>: ASAT, ZERO-DAY, FOB), as it does with the ladder complete; a doctrine build backing off is skipped the same way.
        /// </summary>
        private void Quartermaster(FactionFront f, float now)
        {
            bool humans = manager.HumanCount(f.Owner) > 0;
            for (int i = 0; i < FrontRules.FrontCount; i++)
            {
                var front = (Front)i;
                if (!f.Book.QuartermasterDue(front, now, humans)) continue;
                QueuedCounts queued = f.Book.Queued(front);
                if (FrontLadder.Next(front, f.World, queued, out ProgrammeId id, out byte kind) && now >= f.BlockedUntil[i, (int)id] &&
                    Queue(f, front, id, kind, false, 0f, 0f, false, now) == FrontOutcome.Queued) continue;
                if (humans) continue;
                bool sam = front == Front.Cyber && cyber != null && cyber.TryBestSamNode(f.Owner, out _);
                bool asat = front == Front.Space && now >= f.NextAiAsat && EnemyBirdUp(f.Owner);
                if (FrontAi.Doctrine(front, f.World, queued, sam, asat, out id) && now >= f.BlockedUntil[i, (int)id] &&
                    Queue(f, front, id, 0, false, 0f, 0f, false, now) == FrontOutcome.Queued && id == ProgrammeId.Asat)
                    f.NextAiAsat = now + AiAsatSpacingSeconds;
            }
        }

        // ---- Programme effects ----------------------------------------------------------------------

        /// <summary>
        /// A finished programme meets its owning service, which places it again at completion (the world may have moved since the queue check). DONE is logged only when the service
        /// accepted; when it refused (no legal ground, the prerequisite is gone, nothing to hit), a FAILED line names the service's cause and the cost the programme captured goes back to
        /// the front's budget, once (<see cref="FrontBook.Settle"/>).
        /// </summary>
        private void Apply(FactionFront f, FrontEvent e, float now)
        {
            FactionHQ owner = f.Owner;
            bool ok = false;
            string why = null;
            FrontOutcome failure = FrontOutcome.Unavailable; // what the log names if the service refuses
            var bird = (BirdKind)e.Kind;
            try
            {
                switch (e.Id)
                {
                    case ProgrammeId.LaunchSatellite:
                        ok = space != null && space.TryLaunchBird(owner, bird);
                        if (!ok && space != null) failure = space.CheckLaunch(owner, bird); // the real cause: NO PAD, PAD FULL, KIND UP
                        break;
                    case ProgrammeId.UplinkSite: ok = space != null && space.TryBuildPad(owner, e.HasPoint, e.X, e.Z, out failure); break;
                    case ProgrammeId.DataCenter: ok = cyber != null && cyber.TryBuildDataCenter(owner, e.HasPoint, e.X, e.Z, out failure); break;
                    case ProgrammeId.EwTruck: ok = cyber != null && cyber.TryDeployTruck(owner, e.HasPoint, e.X, e.Z, out failure); break;
                    case ProgrammeId.Camp: ok = sof != null && sof.TryBuildCamp(owner, e.HasPoint, e.X, e.Z, out failure); break;
                    case ProgrammeId.TrainTeam:
                        SofResult r = sof != null ? sof.TrainTeam(owner) : new SofResult(SofOutcome.Unavailable);
                        ok = r.Ok; why = r.Words;
                        failure = r.Outcome == SofOutcome.TeamCap ? FrontOutcome.TeamCap : r.Outcome == SofOutcome.CampFull ? FrontOutcome.CampFull :
                            r.Outcome == SofOutcome.NoCamp || r.Outcome == SofOutcome.CampDown ? FrontOutcome.NoCamp : FrontOutcome.Unavailable;
                        break;
                    case ProgrammeId.ZeroDay: ok = ops != null && ops.RunProgramme(owner, e.Id); failure = FrontOutcome.NoSamNet; break;
                    case ProgrammeId.Fob:
                        // The picked building while it is still held, else the first held one (ruling R15: the strict pick was judged at queue time).
                        ok = ops != null && ops.RunProgramme(owner, ProgrammeId.Fob, e.HasPoint && sof != null ? sof.HeldNear(owner, e.X, e.Z, FrontRules.FobPickMetres) : 0);
                        failure = FrontOutcome.NoHeldBuilding;
                        break;
                    case ProgrammeId.Asat: ok = ops != null && ops.RunProgramme(owner, e.Id); break;
                }
            }
            catch (Exception ex)
            {
                // A service that throws must not swallow the programme: it is refused like any other failure and the rest of the faction's completions still run.
                ok = false; why = ex.Message; failure = FrontOutcome.Unavailable;
                Plugin.Logger?.LogWarning("[Support.Fronts] " + owner.name + " " + FrontWords.Programme(e.Id) + " effect threw: " + ex.Message);
            }
            if (!ok && failure == FrontOutcome.None) failure = FrontOutcome.Unavailable; // a refusal is never settled as DONE
            f.Book.Settle(e, ok ? FrontOutcome.None : failure, now);
            // An automatic pool build that found no room would loop (fund, build, refund) every cycle: the quartermaster backs off that step for 10 minutes.
            if (!ok && !e.HasPoint && e.Owner == 0 && (failure == FrontOutcome.NoSpot || failure == FrontOutcome.NoSite || failure == FrontOutcome.NotHeld))
                f.BlockedUntil[(int)e.Front, (int)e.Id] = now + BlockedSeconds;
            if (ok)
            {
                if (e.Id == ProgrammeId.LaunchSatellite) f.Book.NoteLog(Front.Space, FrontLogCode.Effect, (int)e.Id, (int)bird, now);
                Plugin.Logger?.LogInfo("[Support.Fronts] " + owner.name + " " + FrontRules.Name(e.Front) + " " + FrontWords.Programme(e.Id) + " complete.");
                return;
            }
            if (string.IsNullOrEmpty(why)) why = FrontWords.Refusal(failure, e.Front);
            Plugin.Logger?.LogInfo("[Support.Fronts] " + owner.name + " " + FrontRules.Name(e.Front) + " " + FrontWords.Programme(e.Id) + " had no effect (" +
                (why.Length > 0 ? why : "nothing to do") + "); " + (int)e.Cost + " returned to the front budget.");
        }

        // ---- Superiority and the counter triangle ----------------------------------------------------

        private void Superiority(float now)
        {
            foreach (FactionFront a in order)
            {
                for (int i = 0; i < FrontRules.FrontCount; i++)
                {
                    var front = (Front)i;
                    FrontSides enemy = default;
                    float best = float.NegativeInfinity;
                    foreach (FactionFront b in order)
                    {
                        if (b == a) continue;
                        float strength = FrontSuperiority.Strength(front, b.Own);
                        if (strength > best) { best = strength; enemy = b.Own; }
                    }
                    int s = FrontSuperiority.Compute(front, a.Own, enemy);
                    a.Book.SetSuperiority(front, s);
                    a.Book.SetTerms(front, FrontSuperiority.Terms(front, a.Own, enemy));
                    bool on = FrontSuperiority.CounterActive(s);
                    if (on != a.CounterOn[i]) { a.CounterOn[i] = on; a.Book.NoteLog(front, FrontLogCode.Counter, on ? 1 : 0, 0, now); }
                }
            }
            foreach (FactionFront x in order)
            {
                bool space = false, cyberLead = false, sofLead = false;
                foreach (FactionFront y in order)
                {
                    if (y == x) continue;
                    space |= y.CounterOn[(int)Front.Space]; cyberLead |= y.CounterOn[(int)Front.Cyber]; sofLead |= y.CounterOn[(int)Front.Sof];
                }
                x.Pressure = FrontCounters.Against(space, cyberLead, sofLead);
                if (sof != null && sof.TryDesk(x.Owner, out SofDesk sd)) sd.ExposureScale = x.Pressure.SofExposure;
                if (cyber != null && cyber.TryDesk(x.Owner, out CyberDesk cd)) cd.CounterTrace = x.Pressure.CyberTrace;
            }
        }

        private void ClearPressure()
        {
            foreach (FactionFront x in order)
            {
                if (sof != null && sof.TryDesk(x.Owner, out SofDesk sd)) sd.ExposureScale = 1f;
                if (cyber != null && cyber.TryDesk(x.Owner, out CyberDesk cd)) cd.CounterTrace = 1f;
            }
        }

        // ---- SIPHON ---------------------------------------------------------------------------------------

        /// <summary>
        /// SIPHON (CYBER R3, host only): funds move from the strongest enemy on the CYBER front into the caster's CYBER budget. The ask is (40 + 15 per enemy node the caster holds) x power, capped by what that enemy has in all
        /// its front budgets and taken from its richest first (<see cref="Domain.Fronts.Siphon"/>). Both factions get a log line. NothingToSiphon below 10 funds.
        /// </summary>
        internal SupportResult Siphon(FactionHQ owner, float power, out int taken)
        {
            taken = 0;
            if (!GameAccess.IsServer() || owner == null || !Running || !factions.TryGetValue(owner, out FactionFront caster)) return SupportResult.NothingToSiphon;
            FactionFront foe = null;
            float best = float.NegativeInfinity;
            foreach (FactionFront b in order)
            {
                if (b == caster || b.Owner == null) continue;
                float strength = FrontSuperiority.Strength(Front.Cyber, b.Own);
                if (strength > best) { best = strength; foe = b; }
            }
            if (foe == null) return SupportResult.NothingToSiphon;
            var budgets = new float[FrontRules.FrontCount];
            var takes = new float[FrontRules.FrontCount];
            for (int i = 0; i < budgets.Length; i++) budgets[i] = Domain.Fronts.Siphon.Available(foe.Book.Side((Front)i)); // unspent budget + unstarted funding bars
            float total = Domain.Fronts.Siphon.Plan(Domain.Fronts.Siphon.Wanted(caster.Own.HeldEnemyNodes, power), budgets, takes);
            if (total <= 0f) return SupportResult.NothingToSiphon;
            float now = SupportManager.MissionNow();
            for (int i = 0; i < takes.Length; i++)
            {
                if (takes[i] <= 0f) continue;
                Domain.Fronts.Siphon.Drain(foe.Book.Side((Front)i), takes[i]);
                foe.Book.NoteLog((Front)i, FrontLogCode.Siphoned, (int)takes[i], 0, now);
            }
            caster.Book.Side(Front.Cyber).Budget += total;
            caster.Book.NoteLog(Front.Cyber, FrontLogCode.Siphon, (int)total, 0, now);
            caster.CacheAt = foe.CacheAt = float.NegativeInfinity; // both factions' next snapshot carries the new budgets and the log lines
            taken = (int)total;
            Plugin.Logger?.LogInfo("[Support.Fronts] " + owner.name + " SIPHON took " + taken + " funds from " + foe.Owner.name + ".");
            return SupportResult.Accepted;
        }

        // ---- Commands -----------------------------------------------------------------------------------

        private static bool Unpack(int packed, out Front front, out int value)
        {
            front = (Front)(packed & 3); value = packed >> 2;
            return packed >= 0 && (packed & 3) < FrontRules.FrontCount;
        }

        /// <summary>One front command for a transport-authenticated player (host only). Faction and identity come from the player, never from the message.</summary>
        internal FrontResult Verb(Player player, in SpaceCommand c)
        {
            if (!GameAccess.IsServer() || player == null || player.HQ == null || !Enabled || !Running) return new FrontResult(FrontOutcome.Unavailable);
            ulong key = PlayerIdentity.Of(player);
            if (key == PlayerIdentity.None) return new FrontResult(FrontOutcome.Unavailable);
            if (!factions.TryGetValue(player.HQ, out FactionFront f)) return new FrontResult(FrontOutcome.Offline);
            float now = SupportManager.MissionNow();
            string name = manager.PlayerLabel(player.HQ, key);
            FrontResult result;
            switch (c.Kind)
            {
                case SpaceCommandKind.FrontDirective: result = SetDirective(f, c.Target, key, name, now); break;
                case SpaceCommandKind.FrontPriority: result = SetPriority(f, c.Ids, key, name, now); break;
                case SpaceCommandKind.FrontFocus: result = SetFocus(f, c.Ids, now); break;
                case SpaceCommandKind.FrontQueue: result = Queue(f, c.Ids, true, now); break;
                case SpaceCommandKind.FrontDonate: result = Donate(f, player, c.Ids); break;
                case SpaceCommandKind.RelocateBird: result = Relocate(f, c.Ids, now); break;
                case SpaceCommandKind.TruckMove: result = MoveTruck(f, c.Ids); break;
                default: result = new FrontResult(FrontOutcome.Unavailable); break;
            }
            if (result.Ok) f.CacheAt = float.NegativeInfinity;
            return result;
        }

        private FrontResult SetDirective(FactionFront f, int target, ulong key, string name, float now)
        {
            if (!Unpack(target, out Front front, out int value)) return new FrontResult(FrontOutcome.BadFront);
            var directive = (FrontDirective)Mathf.Clamp(value, 0, 255);
            if (value > (int)FrontDirective.Hold || !FrontRules.IsValid(front, directive)) return new FrontResult(FrontOutcome.BadPosture);
            if (f.Book.SetDirective(front, directive, key, name, now, out _)) return new FrontResult(FrontOutcome.DirectiveSet);
            FrontSide s = f.Book.Side(front);
            return new FrontResult(FrontOutcome.Locked, Mathf.CeilToInt(Mathf.Max(0f, s.DirectiveLockUntil - now)), 0, s.DirectiveByName);
        }

        private FrontResult SetPriority(FactionFront f, int[] ids, ulong key, string name, float now)
        {
            if (ids == null || ids.Length != FrontRules.FrontCount) return new FrontResult(FrontOutcome.BadWeights);
            var weights = new float[FrontRules.FrontCount];
            for (int i = 0; i < weights.Length; i++) weights[i] = Mathf.Clamp(ids[i], 0, 1000);
            if (f.Book.SetPriority(weights, key, name, now, out _)) return new FrontResult(FrontOutcome.PrioritySet);
            if (weights[0] + weights[1] + weights[2] <= 0f) return new FrontResult(FrontOutcome.BadWeights);
            return new FrontResult(FrontOutcome.Locked, Mathf.CeilToInt(Mathf.Max(0f, f.Book.PriorityLockUntil - now)), 0, f.Book.PriorityByName);
        }

        private FrontResult SetFocus(FactionFront f, int[] ids, float now)
        {
            if (ids == null || ids.Length != 2 || !Unpack(ids[0], out Front front, out int clear)) return new FrontResult(FrontOutcome.BadFront);
            if (clear != 0) { f.Book.ClearFocus(front); return new FrontResult(FrontOutcome.FocusCleared); }
            if (!SofCodes.TryUnpackPoint(ids[1], out float x, out float z)) return new FrontResult(FrontOutcome.BadAmount);
            f.Book.SetFocus(front, x, z, now);
            return new FrontResult(FrontOutcome.FocusSet);
        }

        /// <summary>RELOCATE: a faction member burns one of its birds to a map point. The host judges the bird, the burn state and the fuel.</summary>
        private FrontResult Relocate(FactionFront f, int[] ids, float now)
        {
            if (ids == null || ids.Length != 2 || ids[0] < 0 || ids[0] >= SpaceRules.BirdCount || !GeoSpace.TryUnpack(ids[1], out float u, out float v)) return new FrontResult(FrontOutcome.BadAmount);
            if (space == null) return new FrontResult(FrontOutcome.Unavailable);
            var before = space.TryGetStateCoarse(f.Owner, out SpaceState state) ? state.Geo((BirdKind)ids[0]) : default;
            switch (space.TryRelocate(f.Owner, ids[0], u, v, now, out GeoBird after))
            {
                case GeoSpace.Refusal.None: return new FrontResult(FrontOutcome.Relocated, Mathf.CeilToInt(after.Duration), Mathf.RoundToInt(before.Fuel - after.Fuel));
                case GeoSpace.Refusal.Moving: return new FrontResult(FrontOutcome.Burning);
                case GeoSpace.Refusal.NoMove: return new FrontResult(FrontOutcome.NoMove);
                case GeoSpace.Refusal.NoFuel: return new FrontResult(FrontOutcome.NoFuel, Mathf.CeilToInt(GeoBird.Cost(GeoBird.Distance(before.U(now), before.V(now), u, v))));
                default: return new FrontResult(FrontOutcome.BirdLost);
            }
        }

        /// <summary>QUEUE: Ids = { front | programme &lt;&lt; 2 | kind &lt;&lt; 6, packed point (0 = automatic placement) }.</summary>
        private FrontResult Queue(FactionFront f, int[] ids, bool human, float now)
        {
            if (ids == null || ids.Length != 2 || !Unpack(ids[0], out Front front, out int packed)) return new FrontResult(FrontOutcome.BadFront);
            int programme = packed & 15; byte kind = (byte)((packed >> 4) & 3);
            if (programme >= FrontRules.Programmes.Length) return new FrontResult(FrontOutcome.NotAProgramme);
            float px = 0f, pz = 0f;
            bool hasPoint = ids[1] != 0;
            if (hasPoint && !SofCodes.TryUnpackPoint(ids[1], out px, out pz)) return new FrontResult(FrontOutcome.NoSpot);
            return new FrontResult(Queue(f, front, (ProgrammeId)programme, kind, hasPoint, px, pz, human, now));
        }

        /// <summary>
        /// One build onto a front's queue (Queued, or the refusal; nothing is paid on a refusal): the book's dry check first (queue room, the ladder counting queued rows), then the
        /// service's placement rule, then the book takes it. Owner 0: the faction pool. <paramref name="human"/> restarts the front's quartermaster clock.
        /// </summary>
        private FrontOutcome Queue(FactionFront f, Front front, ProgrammeId id, byte kind, bool hasPoint, float x, float z, bool human, float now)
        {
            FrontOutcome r = f.Book.RefusalCode(front, id, kind, f.World);
            if (r == FrontOutcome.None) r = Placement(f.Owner, f.Book.Side(front), id, hasPoint, x, z);
            if (r == FrontOutcome.None) r = f.Book.Enqueue(front, id, kind, hasPoint, x, z, 0, human, f.World, now);
            return r == FrontOutcome.None ? FrontOutcome.Queued : r;
        }

        /// <summary>The service's placement rule at queue time (None when the build may stand there). A satellite, a team and an ASAT need no place: their row waits on the ladder.</summary>
        private FrontOutcome Placement(FactionHQ owner, FrontSide side, ProgrammeId id, bool hasPoint, float x, float z)
        {
            switch (id)
            {
                case ProgrammeId.UplinkSite: return space != null ? space.CheckPad(owner, hasPoint, x, z) : FrontOutcome.Unavailable;
                case ProgrammeId.DataCenter: return cyber != null ? cyber.CheckDataCenter(owner, hasPoint, x, z) : FrontOutcome.Unavailable;
                case ProgrammeId.EwTruck:
                    if (cyber == null) return FrontOutcome.Unavailable;
                    // A queued data center counts as coming. An automatic truck needs only its capacity, so every queued center counts; a picked point can only be judged
                    // against a queued center that has a place.
                    queuedCenters.Clear();
                    foreach (FrontProgrammeEntry e in side.Queue) if (e.Id == ProgrammeId.DataCenter && (e.HasPoint || !hasPoint)) queuedCenters.Add((e.X, e.Z));
                    return cyber.CheckTruck(owner, hasPoint, x, z, queuedCenters);
                case ProgrammeId.Camp: return sof != null ? sof.CheckCamp(owner, hasPoint, x, z) : FrontOutcome.Unavailable;
                case ProgrammeId.Fob: return sof != null ? sof.CheckFob(owner, hasPoint, x, z) : FrontOutcome.Unavailable; // ruling R15: the strict pick
                case ProgrammeId.ZeroDay: return cyber != null && cyber.TryBestSamNode(owner, out _) ? FrontOutcome.None : FrontOutcome.NoSamNet; // it needs a revealed SAM net to hit
                default: return FrontOutcome.None;
            }
        }

        /// <summary>TRUCK MOVE: Ids = { truck index, packed point }. The truck packs up and redeploys at the point; CYBER judges the 20 km data center reach.</summary>
        private FrontResult MoveTruck(FactionFront f, int[] ids)
        {
            if (ids == null || ids.Length != 2) return new FrontResult(FrontOutcome.BadAmount);
            if (!SofCodes.TryUnpackPoint(ids[1], out float x, out float z)) return new FrontResult(FrontOutcome.NoSpot);
            return new FrontResult(cyber != null ? cyber.MoveTruck(f.Owner, ids[0], x, z) : FrontOutcome.Unavailable);
        }

        private FrontResult Donate(FactionFront f, Player player, int[] ids)
        {
            if (ids == null || ids.Length != 2 || !Unpack(ids[0], out Front front, out int row) || row > ushort.MaxValue) return new FrontResult(FrontOutcome.BadFront);
            if (player.HQ.preventDonation && GameManager.gameState == GameState.Multiplayer) return new FrontResult(FrontOutcome.NoDonations);
            FrontProgrammeEntry entry = null;
            foreach (FrontProgrammeEntry e in f.Book.Side(front).Queue) if (e.Row == row) { entry = e; break; }
            if (entry == null) return new FrontResult(FrontOutcome.NotQueued);
            int wanted = Mathf.Min(Mathf.Max(0, ids[1]), Mathf.CeilToInt(entry.Remaining / FrontRules.DonateRate));
            if (wanted <= 0) return new FrontResult(FrontOutcome.BadAmount);
            bool free = manager.BypassRequirements;
            int give = free ? wanted : Mathf.Min(wanted, Mathf.FloorToInt(Mathf.Max(0f, player.Allocation)));
            if (give <= 0) return new FrontResult(FrontOutcome.LowAllocation, 1);
            if (!free && !manager.TrySpendAllocation(player, give)) return new FrontResult(FrontOutcome.LowAllocation, give);
            DonateResult r = f.Book.Donate(front, (ushort)row, give * FrontRules.DonateRate);
            if (r.Status != DonateStatus.Ok) { if (!free) manager.RefundAllocation(player, give); return new FrontResult(FrontOutcome.NotQueued); }
            int used = Mathf.Min(give, Mathf.CeilToInt(r.Accepted / FrontRules.DonateRate));
            if (!free && used < give) manager.RefundAllocation(player, give - used); // the bar took less than was offered: the rest goes back
            return new FrontResult(FrontOutcome.Donated, 0, used);
        }
    }
}
