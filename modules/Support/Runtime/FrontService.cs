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
    /// OPS FRONTS S1b: the host owner of every faction's three fronts (spec 3, 5). One <see cref="FrontBook"/> per faction, AI-only factions included. Every 60 s a share of the
    /// faction's vanilla funds moves into the fronts; every 5 s the real services are read into a <see cref="FrontWorld"/>, lost anchors queue their rebuild programme, the books tick and
    /// each finished programme is turned into its world effect through the existing spawners and effects. Superiority is recomputed against the strongest enemy and, at +40, presses the
    /// enemy front it beats (SPACE on SOF exposure, CYBER on SPACE cooldowns, SOF on CYBER trace). The same service answers the faction's commands, builds its mirror and gates perks
    /// (<see cref="IFrontReadiness"/>): the host reads the books, a client reads the mirror.
    /// </summary>
    internal sealed class FrontService : MonoBehaviour, ISceneService, IFrontReadiness
    {
        private const int MaximumFactions = 8;
        private const float WorldSeconds = 5f, SnapshotSeconds = 1f;
        private const int AiTeams = 2;

        private sealed class FactionFront
        {
            public FactionHQ Owner;
            public readonly FrontBook Book = new FrontBook();
            public FrontWorld World;
            public FrontSides Own;
            public readonly bool[] CounterOn = new bool[FrontRules.FrontCount];
            public CounterPressure Pressure = CounterPressure.None;
            public FrontStateData Cache;
            public float CacheAt = float.NegativeInfinity;
        }

        private readonly Dictionary<FactionHQ, FactionFront> factions = new Dictionary<FactionHQ, FactionFront>();
        private readonly List<FactionFront> order = new List<FactionFront>(MaximumFactions);
        private SupportManager manager;
        private SpaceService space;
        private CyberService cyber;
        private SofService sof;
        private OpsService ops;
        private float nextShare = -1f, nextWorld, nextWarning;

        internal static FrontService Active { get; private set; }

        /// <summary>True while the host ticks the fronts (the anchor rebuilds then wait for their programme).</summary>
        internal bool Running { get; private set; }

        public void Configure(SupportManager support, SpaceService spaceService, CyberService cyberService, SofService sofService, OpsService opsService)
        {
            manager = support; space = spaceService; cyber = cyberService; sof = sofService; ops = opsService;
            Active = this;
            manager.ConfigureReadiness(this);
            manager.AttachFronts(this);
        }

        public void ResetForScene()
        {
            ClearPressure();
            factions.Clear();
            order.Clear();
            Running = false;
            nextShare = -1f;
            nextWorld = 0f;
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

        public int Readiness(FactionHQ hq, Front front)
        {
            if (hq == null) return FrontRules.MaxReadiness;
            if (GameAccess.IsServer()) return factions.TryGetValue(hq, out FactionFront f) ? f.Book.Readiness(front) : FrontRules.MaxReadiness;
            return IsLocalFaction(hq) ? manager.FrontMirror.Readiness(front) : FrontRules.MaxReadiness;
        }

        public float Quality(FactionHQ hq, Front front)
        {
            if (hq == null) return 1f;
            if (GameAccess.IsServer()) return factions.TryGetValue(hq, out FactionFront f) ? FrontSuperiority.Quality(f.Book.Side(front).Superiority) : 1f;
            return IsLocalFaction(hq) ? manager.FrontMirror.Quality(front) : 1f;
        }

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

        /// <summary>Dev readout for the solo acceptance sim: the first other faction's readiness sum, queued programmes and superiority sum, and the viewer's host budget.</summary>
        internal bool EnemyReadout(FactionHQ viewer, out int readinessSum, out int queued, out int superioritySum, out float hostBudget)
        {
            readinessSum = queued = superioritySum = 0; hostBudget = 0f;
            if (viewer != null && factions.TryGetValue(viewer, out FactionFront mine))
                for (int i = 0; i < FrontRules.FrontCount; i++) hostBudget += mine.Book.Side((Front)i).Budget;
            foreach (FactionFront f in order)
            {
                if (f.Owner == viewer) continue;
                for (int i = 0; i < FrontRules.FrontCount; i++)
                {
                    FrontSide side = f.Book.Side((Front)i);
                    readinessSum += side.Readiness; queued += side.Queue.Count; superioritySum += side.Superiority;
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
            if (!SpaceRules.MissionTime(now) || now < nextWorld) return;
            nextWorld = now + WorldSeconds;
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
                var made = new FactionFront { Owner = hq };
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
                f.Book.AutoQueueRebuilds(f.World, now);
                if (manager.HumanCount(f.Owner) == 0) AiQueue(f, now);
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
            if (space != null && space.TryGetStateCoarse(hq, out SpaceState state))
            {
                int down = 0;
                for (int i = 0; i < SpaceRules.BirdCount; i++) if (state.BirdDown((BirdKind)i)) down++;
                w.BirdsDown = down; s.BirdsUp = SpaceRules.BirdCount - down;
                w.Uplinks = s.UplinksLive = state.LiveUplinkCount; w.UplinkMax = s.UplinksTotal = state.UplinkCount;
            }
            if (cyber != null && cyber.TryDesk(hq, out CyberDesk cd))
            {
                w.DataCenters = cd.Anchors.LiveCount(AnchorKind.DataCenter); w.DataCenterMax = cd.Anchors.Count(AnchorKind.DataCenter);
                w.Trucks = cd.Anchors.LiveCount(AnchorKind.EwTruck); w.TruckMax = cd.Anchors.Count(AnchorKind.EwTruck);
                s.AnchorsLive = w.DataCenters + w.Trucks;
                int held = 0; float trace = 0f;
                foreach (CyberIntrusion x in cd.Network.Active) { held += x.HeldCount; trace = Mathf.Max(trace, x.Trace); }
                s.HeldEnemyNodes = held; s.Trace = Mathf.Clamp01(trace / CyberRules.TraceMax);
            }
            if (sof != null && sof.TryDesk(hq, out SofDesk sd))
            {
                w.Camps = s.CampsLive = sd.Camps.LiveCount(); w.CampMax = sd.Camps.Count;
                w.Teams = sd.ActiveCount; w.HasHeldBuilding = sd.Held.Count > 0;
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

        /// <summary>A faction with no humans raises its fronts itself: when a front has nothing building it queues the next programme of its doctrine (READINESS, a team, a ZERO-DAY, a FOB).</summary>
        private void AiQueue(FactionFront f, float now)
        {
            for (int i = 0; i < FrontRules.FrontCount; i++)
            {
                var front = (Front)i;
                if (f.Book.Side(front).Queue.Count > 0) continue;
                bool sam = front == Front.Cyber && cyber != null && cyber.TryBestSamNode(f.Owner, out _);
                ProgrammeId? next = FrontAi.Next(front, f.Book.Readiness(front), f.World, sam);
                if (next.HasValue) f.Book.Queue(front, next.Value, f.World, now, out _);
            }
        }

        // ---- Programme effects ----------------------------------------------------------------------

        private void Apply(FactionFront f, in FrontEvent e, float now)
        {
            FactionHQ owner = f.Owner;
            bool ok = true;
            string why = "nothing to do";
            switch (e.Id)
            {
                case ProgrammeId.Readiness: break;
                case ProgrammeId.LaunchSatellite:
                {
                    BirdKind bird = BirdKind.Optical;
                    ok = space != null && space.TryLaunchBird(owner, out bird);
                    if (ok) f.Book.NoteLog(Front.Space, FrontLogCode.Effect, (int)e.Id, (int)bird, now);
                    break;
                }
                case ProgrammeId.UplinkSite: ok = space != null && space.TryRebuildUplink(owner); break;
                case ProgrammeId.DataCenter: ok = cyber != null && cyber.CompleteRebuild(owner, AnchorKind.DataCenter); break;
                case ProgrammeId.EwTruck: ok = cyber != null && cyber.CompleteRebuild(owner, AnchorKind.EwTruck); break;
                case ProgrammeId.Camp: ok = sof != null && sof.CompleteCampRebuild(owner); break;
                case ProgrammeId.TrainTeam:
                    SofResult r = sof != null ? sof.TrainTeam(owner) : new SofResult(SofOutcome.Unavailable);
                    ok = r.Ok; why = r.Words;
                    break;
                case ProgrammeId.ZeroDay:
                case ProgrammeId.Asat:
                case ProgrammeId.Fob:
                    ok = ops != null && ops.RunProgramme(owner, e.Id);
                    break;
            }
            if (ok) { Plugin.Logger?.LogInfo("[Support.Fronts] " + owner.name + " " + FrontRules.Name(e.Front) + " " + FrontWords.Programme(e.Id, e.Rung) + " complete."); return; }
            // The world no longer needs it (the anchor was rebuilt another way, the camp is down): the funds come back to the front's budget.
            if (e.Id != ProgrammeId.Readiness) f.Book.Side(e.Front).Budget += FrontRules.Cost(e.Id, 1, f.Book.CostScale);
            Plugin.Logger?.LogInfo("[Support.Fronts] " + owner.name + " " + FrontRules.Name(e.Front) + " " + FrontWords.Programme(e.Id) + " had no effect (" + why + "); funds returned.");
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
                case SpaceCommandKind.FrontQueue: result = Queue(f, c.Target, now); break;
                case SpaceCommandKind.FrontDonate: result = Donate(f, player, c.Ids); break;
                case SpaceCommandKind.RelocateBird: result = Relocate(f, c.Ids, now); break;
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

        private FrontResult Queue(FactionFront f, int target, float now)
        {
            if (!Unpack(target, out Front front, out int programme)) return new FrontResult(FrontOutcome.BadFront);
            if (programme < 0 || programme >= FrontRules.Programmes.Length) return new FrontResult(FrontOutcome.NotAProgramme);
            var id = (ProgrammeId)programme;
            // The host knows what the book cannot: a ZERO-DAY needs a revealed SAM net to hit.
            if (id == ProgrammeId.ZeroDay && FrontRules.BelongsTo(front, id) && !f.Book.Side(front).Has(id) && !(cyber != null && cyber.TryBestSamNode(f.Owner, out _)))
                return new FrontResult(FrontOutcome.NoSamNet);
            if (f.Book.Queue(front, id, f.World, now, out _)) return new FrontResult(FrontOutcome.Queued);
            return new FrontResult(f.Book.RefusalCode(front, id, f.World));
        }

        private FrontResult Donate(FactionFront f, Player player, int[] ids)
        {
            if (ids == null || ids.Length != 2 || !Unpack(ids[0], out Front front, out int programme)) return new FrontResult(FrontOutcome.BadFront);
            if (programme < 0 || programme >= FrontRules.Programmes.Length) return new FrontResult(FrontOutcome.NotAProgramme);
            if (player.HQ.preventDonation && GameManager.gameState == GameState.Multiplayer) return new FrontResult(FrontOutcome.NoDonations);
            var id = (ProgrammeId)programme;
            FrontProgrammeEntry entry = null;
            foreach (FrontProgrammeEntry e in f.Book.Side(front).Queue) if (e.Id == id) { entry = e; break; }
            if (entry == null) return new FrontResult(FrontOutcome.NotQueued);
            int wanted = Mathf.Min(Mathf.Max(0, ids[1]), Mathf.CeilToInt(entry.Remaining / FrontRules.DonateRate));
            if (wanted <= 0) return new FrontResult(FrontOutcome.BadAmount);
            bool free = manager.BypassRequirements;
            int give = free ? wanted : Mathf.Min(wanted, Mathf.FloorToInt(Mathf.Max(0f, player.Allocation)));
            if (give <= 0) return new FrontResult(FrontOutcome.LowAllocation, 1);
            if (!free && !manager.TrySpendAllocation(player, give)) return new FrontResult(FrontOutcome.LowAllocation, give);
            DonateResult r = f.Book.Donate(front, id, give * FrontRules.DonateRate);
            if (r.Status != DonateStatus.Ok) { if (!free) manager.RefundAllocation(player, give); return new FrontResult(FrontOutcome.NotQueued); }
            int used = Mathf.Min(give, Mathf.CeilToInt(r.Accepted / FrontRules.DonateRate));
            if (!free && used < give) manager.RefundAllocation(player, give - used); // the bar took less than was offered: the rest goes back
            return new FrontResult(FrontOutcome.Donated, 0, used);
        }
    }
}
