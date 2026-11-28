using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Applies completed operations. ASAT puts a real launcher on the ground, then contests one forecast pass of a tracked satellite.
    /// </summary>
    internal sealed partial class OpsService
    {
        private sealed class Flight
        {
            public int Id;
            public FactionOps Attacker;
            public AsatTarget Target;
            public float X, Z, ConjunctionAt, PlumeEndsAt;
        }

        private readonly struct AsatTarget
        {
            public readonly FactionHQ Owner;
            public readonly BirdKind Kind;
            public readonly int Instance, TrackId;
            public readonly SpaceState Origin;

            public AsatTarget(FactionHQ owner, BirdKind kind, int instance, int trackId, SpaceState origin)
            { Owner = owner; Kind = kind; Instance = instance; TrackId = trackId; Origin = origin; }
        }

        private sealed partial class FactionOps
        {
            public Unit Launcher;
            public bool LauncherSpawned;
            public AsatTarget PendingTarget;
            public float LaunchAt, ConjunctionAt, NextReveal;
            public readonly int[] TrackIds = new int[SpaceRules.BirdCount];
            public readonly int[] TrackInstances = new int[SpaceRules.BirdCount];
            public SpaceState TrackedState;
        }

        private readonly List<Flight> flights = new List<Flight>(OpsWire.MaxFlights);
        private int flightSerial, trackSerial;

        partial void ResetEffects()
        {
            foreach (var pair in factions) DiscardLauncher(pair.Value);
            flights.Clear();
            flightSerial = trackSerial = 0;
        }

        partial void TickEffects(float now)
        {
            TickFlights(now);
            TickLaunchers(now);
            // A FOB that ended early (the building was retaken) stops showing as up.
            foreach (var pair in factions)
            {
                OpSlot s = pair.Value.Desk.Slot(OpDomain.Sof);
                if (s.Kind == OpKind.Fob && s.State == OpState.Done && s.EffectEnds > now && sof != null && !sof.FobUp(pair.Key)) pair.Value.Desk.EndEffect(OpDomain.Sof);
            }
        }

        /// <summary>
        /// A finished programme runs its effect directly. ZERO-DAY takes the faction's nearest revealed SAM C2 node and FOB uses the picked building while it is still held, else the
        /// first building the faction holds. False when there is nothing to hit.
        /// </summary>
        internal bool RunProgramme(FactionHQ owner, ProgrammeId id, int heldId = 0)
        {
            if (!Enabled || owner == null || !factions.TryGetValue(owner, out FactionOps f)) return false;
            switch (id)
            {
                case ProgrammeId.ZeroDay:
                    return cyber != null && cyber.TryBestSamNode(owner, out int node) && cyber.TryNodeTarget(owner, node, out OpTarget sam) && FireZeroDay(f, sam);
                case ProgrammeId.Fob:
                    return sof != null && ((heldId != 0 && sof.TryHeldTarget(owner, heldId, out OpTarget spot)) || sof.TryHeldTarget(owner, sof.FirstHeldId(owner), out spot)) && FireFob(f, spot);
                case ProgrammeId.Asat: return RunAsatProgramme(owner, 0) == FrontOutcome.None;
                default:
                    return false;
            }
        }

        /// <summary>The countdown started: an ASAT puts its launcher on the ground. Anything that ends the countdown without a launch (BROKEN, a counter-trace, a cancel) removes it.</summary>
        partial void OpEventReact(FactionOps f, OpEvent e)
        {
            if (e.Op != OpKind.Asat) return;
            if (e.Kind == OpEventKind.Execute) SpawnLauncher(f);
            else if (e.Kind != OpEventKind.Fired && f.Desk.Slot(OpDomain.Cyber).State != OpState.Execute) DiscardLauncher(f);
        }

        /// <summary>The countdown ended and the bar held: run the operation's effect. The desk is already DONE.</summary>
        private void Fire(FactionOps f, OpEvent e)
        {
            switch (e.Op)
            {
                case OpKind.ZeroDay: FireZeroDay(f, e.Target); break;
                case OpKind.Asat:
                    float now = SupportManager.MissionNow();
                    if (TryFindTrack(e.Target.Id, out AsatTarget target) && AsatRules.TryForecast(target.Origin.Geo(target.Kind), now, out float conjunctionAt))
                        FireAsat(f, target, now, conjunctionAt);
                    break;
                case OpKind.Fob: FireFob(f, e.Target); break;
            }
        }

        // ---- FOB -------------------------------------------------------------------------------------

        /// <summary>
        /// The FORWARD OPERATING BASE goes live on the held building: 20 minutes (a second FOB renews it), +2 team capacity, teams raised there, and a rearm vehicle (vanilla Rearmer) and a fuel vehicle (vanilla Refueler) beside it when
        /// the encyclopedia allows them. A field spawn point for players is not built (vanilla spawns only at airbases); the page says so. Retaking the building ends it (SofService.TickFob).
        /// </summary>
        private bool FireFob(FactionOps f, in OpTarget target)
        {
            if (sof == null || !sof.StartFob(f.Owner, target.Id, out float until))
            {
                Plugin.Logger?.LogWarning("[Support.Ops] " + f.Owner.name + " FOB could not start: the held building " + target.Id + " is gone; every member is refunded.");
                f.Desk.Fail(OpDomain.Sof);
                return false;
            }
            f.Desk.SetEffectEnd(OpDomain.Sof, until);
            f.Desk.AddPing(OpKind.Fob, OpPingPhase.Execute, 30f); // victim 0: a FOB ping reaches every other faction
            RodGuard.Warn(f.Owner, "ENEMY FORWARD BASE LIVE — THEIR SOF TEAMS NOW RAISE IN THE FIELD");
            return true;
        }

        // ---- ASAT ------------------------------------------------------------------------------------

        private bool LauncherUp(FactionOps f) => f.Launcher != null && !UplinkSpawner.Down(f.Launcher, f.Owner);

        private void DiscardLauncher(FactionOps f)
        {
            if (!ReferenceEquals(f.Launcher, null) && cyber != null) cyber.Spawner.DiscardGroup(f.Launcher); // a destroyed launcher still owns its spawner slot
            f.Launcher = null; f.LauncherSpawned = false;
        }

        private bool SpawnLauncher(FactionOps f)
        {
            DiscardLauncher(f);
            if (cyber == null) return false;
            if (cyber.TryDataCenterSpot(f.Owner, out GlobalPosition spot, out Airbase parent) &&
                cyber.Spawner.TryCreateLauncher(f.Owner, 0, spot, parent, out f.Launcher)) return f.LauncherSpawned = true;
            if (space == null) return false;
            foreach (Unit pad in space.UplinksFor(f.Owner))
            {
                if (pad == null || UplinkSpawner.Down(pad, f.Owner)) continue;
                spot = pad.transform.position.ToGlobalPosition();
                if (cyber.Spawner.TryCreateLauncher(f.Owner, 0, spot, null, out f.Launcher)) return f.LauncherSpawned = true;
            }
            return false;
        }

        private FactionOps TrackedEnemy(FactionHQ owner, out SpaceState enemyState)
        {
            enemyState = null;
            if (space == null) return null;
            FactionOps best = null;
            float share = -1f;
            foreach (FactionOps side in factions.Values)
            {
                if (side.Owner == null || side.Owner == owner || !space.TryGetStateCoarse(side.Owner, out SpaceState state) || state.DownMask == 7) continue;
                float rank = manager.ObjectiveShare(side.Owner);
                if (!float.IsFinite(rank)) rank = 0f;
                if (rank <= share) continue;
                best = side;
                enemyState = state;
                share = rank;
            }
            return best;
        }

        private int TrackId(FactionOps side, SpaceState state, BirdKind kind)
        {
            if (!ReferenceEquals(side.TrackedState, state))
            {
                side.TrackedState = state;
                System.Array.Clear(side.TrackInstances, 0, side.TrackInstances.Length);
            }
            int instance = state.BirdInstance(kind), index = (int)kind;
            if (instance == 0) return 0;
            if (side.TrackInstances[index] != instance)
            {
                side.TrackInstances[index] = instance;
                side.TrackIds[index] = ++trackSerial;
            }
            return side.TrackIds[index];
        }

        internal bool TryResolveAsatQueueTrack(FactionHQ owner, int requestedTrackId, out int targetTrack)
        {
            targetTrack = 0;
            if (!Enabled || owner == null || !factions.ContainsKey(owner)) return false;
            FactionOps enemy = TrackedEnemy(owner, out SpaceState state);
            if (enemy == null) return false;
            for (int i = 0; i < SpaceRules.BirdCount; i++)
            {
                var kind = (BirdKind)(i < 2 ? 1 - i : i); // prefer radar in automatic doctrine
                int id = TrackId(enemy, state, kind);
                if (id == 0 || (requestedTrackId != 0 && id != requestedTrackId)) continue;
                targetTrack = id;
                return true;
            }
            return false;
        }

        private bool TryFindTrack(int id, out AsatTarget target)
        {
            target = default;
            if (id <= 0 || space == null) return false;
            foreach (FactionOps side in factions.Values)
            {
                if (!space.TryGetStateCoarse(side.Owner, out SpaceState state) || !ReferenceEquals(side.TrackedState, state)) continue;
                for (int i = 0; i < SpaceRules.BirdCount; i++)
                {
                    var kind = (BirdKind)i;
                    int instance = state.BirdInstance(kind);
                    if (side.TrackIds[i] != id || instance == 0 || instance != side.TrackInstances[i]) continue;
                    target = new AsatTarget(side.Owner, kind, instance, id, state);
                    return true;
                }
            }
            return false;
        }

        private bool TrackLive(in AsatTarget target, out SpaceState state)
        {
            state = null;
            return target.Instance > 0 && space != null && space.TryGetStateCoarse(target.Owner, out state) &&
                AsatRules.SameSatellite(target.Origin, state, target.Kind, target.Instance);
        }

        internal FrontResult Rephase(FactionHQ owner, int[] ids, float now)
        {
            if (!Enabled || space == null) return new FrontResult(FrontOutcome.Unavailable);
            if (ids == null || ids.Length != 2 || ids[0] < 0 || ids[0] >= SpaceRules.BirdCount || ids[1] < 0 || ids[1] > 1)
                return new FrontResult(FrontOutcome.BadAmount);
            if (!space.TryGetStateCoarse(owner, out SpaceState state)) return new FrontResult(FrontOutcome.BirdLost);
            GeoBird before = state.Geo((BirdKind)ids[0]);
            switch (space.TryRephase(owner, ids[0], AsatRules.RetimeShift(ids[1] == 1), now, out GeoBird after, holdStation: true))
            {
                case GeoSpace.Refusal.None:
                    return new FrontResult(FrontOutcome.Rephased, Mathf.CeilToInt(after.Eta(now)), Mathf.RoundToInt(before.DeltaV - after.DeltaV));
                case GeoSpace.Refusal.Moving: return new FrontResult(FrontOutcome.Burning);
                case GeoSpace.Refusal.NoFuel: return new FrontResult(FrontOutcome.NoDeltaV);
                case GeoSpace.Refusal.NoMove: return new FrontResult(FrontOutcome.NoMove);
                default: return new FrontResult(FrontOutcome.BirdLost);
            }
        }

        internal FrontOutcome RunAsatProgramme(FactionHQ owner, int trackId)
        {
            if (!Enabled || owner == null || !factions.TryGetValue(owner, out FactionOps side) || side.PendingTarget.Instance > 0 ||
                flights.Exists(flight => flight.Attacker == side)) return FrontOutcome.Unavailable;
            int reserved = flights.Count;
            foreach (FactionOps faction in factions.Values) if (faction.PendingTarget.Instance > 0) reserved++;
            if (reserved >= OpsWire.MaxFlights) return FrontOutcome.Unavailable;
            if (trackId == 0 && !TryResolveAsatQueueTrack(owner, 0, out trackId)) return FrontOutcome.NoEnemyTrack;
            if (!TryFindTrack(trackId, out AsatTarget target) || target.Owner == owner) return FrontOutcome.NoEnemyTrack;
            float now = SupportManager.MissionNow();
            float launchAt = now + OpsRules.CountdownSeconds;
            if (!AsatRules.TryForecast(target.Origin.Geo(target.Kind), launchAt, out float conjunctionAt)) return FrontOutcome.NoPass;
            if (!SpawnLauncher(side)) return FrontOutcome.NoSite;

            side.PendingTarget = target;
            side.LaunchAt = launchAt;
            side.ConjunctionAt = conjunctionAt;
            side.NextReveal = now;
            side.Desk.AddPing(OpKind.Asat, OpPingPhase.Execute, OpsRules.CountdownSeconds + 5f, 0, manager.FactionKeyOf(target.Owner));
            RodGuard.Tell(owner, "ASAT LAUNCHER STAGED — " + OpsWords.Bird((int)target.Kind) + " TRACK · T-" + (int)OpsRules.CountdownSeconds + " S");
            RodGuard.Tell(target.Owner, "ENEMY ASAT LAUNCHER ON THE PAD — KILL IT WITHIN " + (int)OpsRules.CountdownSeconds + " S TO STOP THE STRIKE ON OUR " + OpsWords.Bird((int)target.Kind) + " BIRD");
            return FrontOutcome.None;
        }

        private void TickLaunchers(float now)
        {
            foreach (FactionOps f in factions.Values)
            {
                AsatTarget target = f.PendingTarget;
                if (target.Instance == 0) continue;
                bool launcherUp = LauncherUp(f);
                if (!launcherUp || !TrackLive(target, out SpaceState state))
                {
                    f.PendingTarget = default;
                    f.Desk.RemovePing(OpKind.Asat, OpPingPhase.Execute);
                    DiscardLauncher(f);
                    string reason = launcherUp ? "TARGET TRACK LOST" : "ASAT LAUNCHER DESTROYED";
                    RodGuard.Tell(f.Owner, reason + " — STRIKE ABORTED");
                    RodGuard.Tell(target.Owner, reason + " — STRIKE ABORTED");
                    continue;
                }
                if (now >= f.LaunchAt)
                {
                    f.PendingTarget = default;
                    f.Desk.RemovePing(OpKind.Asat, OpPingPhase.Execute);
                    if (now >= f.ConjunctionAt || !AsatRules.OpportunityRemains(state.Geo(target.Kind), f.ConjunctionAt))
                    {
                        RodGuard.Tell(f.Owner, "ASAT ABORTED — COMMITTED PASS NO LONGER LINES UP");
                        RodGuard.Tell(target.Owner, "CONJUNCTION BROKEN — ENEMY LAUNCH ABORTED");
                        DiscardLauncher(f);
                    }
                    else if (!FireAsat(f, target, now, f.ConjunctionAt))
                    {
                        RodGuard.Tell(f.Owner, "ASAT LAUNCH ABORTED");
                        DiscardLauncher(f);
                    }
                    continue;
                }
                if (now < f.NextReveal) continue;
                f.NextReveal = now + 5f;
                try { target.Owner.RpcUpdateTrackingInfo(f.Launcher.persistentID); }
                catch (System.Exception e) { Plugin.Logger?.LogDebug("[Support.Ops] Launcher reveal refused: " + e.Message); }
            }
        }

        private bool FireAsat(FactionOps f, in AsatTarget target, float now, float conjunctionAt)
        {
            if (flights.Count >= OpsWire.MaxFlights || !LauncherUp(f) || !TrackLive(target, out _)) return false;
            GlobalPosition at = f.Launcher.transform.position.ToGlobalPosition();
            flights.Add(new Flight { Id = ++flightSerial, Attacker = f, Target = target, X = (float)at.x, Z = (float)at.z,
                ConjunctionAt = conjunctionAt, PlumeEndsAt = now + AsatRules.PlumeSeconds });
            RodGuard.Tell(f.Owner, "ASAT AWAY — CONJUNCTION FORECAST T-" + Mathf.CeilToInt(AsatRules.ForecastBand(conjunctionAt) - now) + " S");
            RodGuard.Tell(target.Owner, "ASAT LAUNCH DETECTED — " + OpsWords.Bird((int)target.Kind) + " BIRD CONJUNCTION T-" + Mathf.CeilToInt(conjunctionAt - now) + " S · RETIME PASS");
            f.Desk.SetEffectEnd(OpDomain.Cyber, conjunctionAt);
            f.Desk.AddPing(OpKind.Asat, OpPingPhase.Launch, conjunctionAt - now + 5f, 0, manager.FactionKeyOf(target.Owner));
            return true;
        }

        private void TickFlights(float now)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                Flight flight = flights[i];
                AsatTarget target = flight.Target;
                bool live = TrackLive(target, out SpaceState state);
                if (now < flight.ConjunctionAt)
                {
                    if (live && manager.Fronts != null && manager.Fronts.BiasFor(target.Owner, Front.Space).Directive == FrontDirective.Defend &&
                        AsatRules.TryDefenseRetime(state.Geo(target.Kind), now, flight.ConjunctionAt, out bool advance) &&
                        space.TryRephase(target.Owner, (int)target.Kind, AsatRules.RetimeShift(advance), now, out _, holdStation: true) == GeoSpace.Refusal.None)
                        RodGuard.Tell(target.Owner, "OVERLORD — EVASIVE RETIMING UNDERWAY");
                    continue;
                }
                flights.RemoveAt(i);
                flight.Attacker.Desk.RemovePing(OpKind.Asat, OpPingPhase.Launch);
                bool dodged = live && !AsatRules.OpportunityRemains(state.Geo(target.Kind), flight.ConjunctionAt);
                bool hit = live && !dodged && state.KillBird(target.Kind, now);
                if (hit)
                {
                    flight.Attacker.Desk.AddPing(OpKind.Asat, OpPingPhase.Loss, OpsRules.LossPingSeconds, (int)target.Kind, manager.FactionKeyOf(target.Owner));
                    RodGuard.Tell(target.Owner, "ASAT IMPACT — " + OpsWords.Bird((int)target.Kind) + " BIRD LOST");
                    RodGuard.Tell(flight.Attacker.Owner, "ASAT HIT — ENEMY BIRD LOST");
                }
                else if (dodged)
                {
                    RodGuard.Tell(target.Owner, "ASAT EVADED — OUR " + OpsWords.Bird((int)target.Kind) + " BIRD MISSED THE CONJUNCTION");
                    RodGuard.Tell(flight.Attacker.Owner, "ASAT MISSED — COMMITTED PASS RETIMED");
                }
                else RodGuard.Tell(flight.Attacker.Owner, "ASAT MISSED — TARGET TRACK LOST");
                DiscardLauncher(flight.Attacker);
            }
        }

        partial void FillBirdsAndFlights(FactionHQ viewer, float now, OpsStateData into)
        {
            if (space != null) space.CopySites(viewer, into.Sites);
            if (space != null && space.TryGetStateCoarse(viewer, out SpaceState state))
            {
                into.BirdsDown = state.DownMask;
                into.BirdsLaunched = state.LaunchedMask;
                for (int i = 0; i < SpaceRules.BirdCount; i++) into.Geo[i] = state.Geo((BirdKind)i);
            }
            FactionOps enemy = TrackedEnemy(viewer, out SpaceState theirs);
            if (enemy != null)
            {
                into.EnemyBirdsDown = theirs.DownMask;
                for (int i = 0; i < SpaceRules.BirdCount; i++)
                {
                    into.EnemyTrackIds[i] = TrackId(enemy, theirs, (BirdKind)i);
                    if (into.EnemyTrackIds[i] == 0) continue;
                    GeoBird bird = theirs.Geo((BirdKind)i);
                    into.Geo[SpaceRules.BirdCount + i] = new GeoBird(AsatRules.TrackSector(bird.U(now)), AsatRules.TrackSector(bird.V(now)), 0f);
                }
            }
            foreach (FactionOps side in factions.Values)
            {
                AsatTarget target = side.PendingTarget;
                if (target.Instance == 0 || now >= side.LaunchAt) continue;
                if (side.Owner == viewer)
                {
                    into.OutgoingTrackId = target.TrackId;
                    into.OutgoingDeadline = side.LaunchAt;
                }
                if (target.Owner == viewer && (into.IncomingBird < 0 || side.LaunchAt < into.IncomingDeadline) && TrackLive(target, out SpaceState pendingState))
                {
                    into.IncomingBird = (int)target.Kind;
                    into.IncomingDeadline = side.LaunchAt;
                    into.IncomingConjunctionAt = side.ConjunctionAt;
                    into.IncomingClear = !AsatRules.OpportunityRemains(pendingState.Geo(target.Kind), side.ConjunctionAt);
                }
            }
            foreach (Flight flight in flights)
            {
                if (now >= flight.ConjunctionAt) continue;
                bool own = flight.Attacker.Owner == viewer;
                if (own)
                {
                    into.OutgoingTrackId = flight.Target.TrackId;
                    into.OutgoingDeadline = AsatRules.ForecastBand(flight.ConjunctionAt);
                    into.OutgoingLaunched = true;
                }
                if (now < flight.PlumeEndsAt)
                    into.Flights.Add(new OpsFlightRow { Id = flight.Id, X = own ? flight.X : Mathf.Round(flight.X / 5000f) * 5000f,
                        Z = own ? flight.Z : Mathf.Round(flight.Z / 5000f) * 5000f, EndsAt = flight.PlumeEndsAt, Seconds = (byte)AsatRules.PlumeSeconds });
                if (flight.Target.Owner != viewer || (into.IncomingLaunched && flight.ConjunctionAt >= into.IncomingDeadline) || !TrackLive(flight.Target, out SpaceState flightState)) continue;
                into.IncomingBird = (int)flight.Target.Kind;
                into.IncomingDeadline = into.IncomingConjunctionAt = flight.ConjunctionAt;
                into.IncomingLaunched = true;
                into.IncomingClear = !AsatRules.OpportunityRemains(flightState.Geo(flight.Target.Kind), flight.ConjunctionAt);
            }
        }

        private FactionHQ HqOfKey(int key)
        {
            var hqs = FactionRegistry.GetAllHQs();
            if (key == 0 || hqs == null || manager == null) return null;
            foreach (FactionHQ hq in hqs) if (hq != null && manager.FactionKeyOf(hq) == key) return hq;
            return null;
        }

        private bool FireZeroDay(FactionOps f, in OpTarget target)
        {
            float now = SupportManager.MissionNow();
            bool truckNear = cyber != null && cyber.EnemyTruckWithin(target.Victim, target.X, target.Z, OpsRules.EwHalveMetres);
            float seconds = OpsRules.ZeroDayEffectSeconds(truckNear);
            if (cyber == null || !cyber.AddSamNetFail(f.Owner, target, seconds))
            {
                Plugin.Logger?.LogWarning("[Support.Ops] " + f.Owner.name + " SAM NET FAIL could not be applied (CYBER desk missing or the effect book is full); every member is refunded.");
                f.Desk.Fail(OpDomain.Cyber);
                return false;
            }
            f.Desk.SetEffectEnd(OpDomain.Cyber, now + seconds);
            f.Desk.AddPing(OpKind.ZeroDay, OpPingPhase.Execute, seconds, 0, target.Victim);
            FactionHQ hit = HqOfKey(target.Victim);
            if (hit != null) RodGuard.Tell(hit, "SAM NET FAILURE — ENEMY ZERO-DAY ON OUR SAM CLUSTER FOR " + (int)seconds + " S" + (truckNear ? "" : " · AN EW TRUCK WITHIN 18 KM HALVES IT"));
            Plugin.Logger?.LogInfo("[Support.Ops] " + f.Owner.name + " SAM NET FAIL on node " + target.Id + " for " + (int)seconds + " s" + (truckNear ? " (enemy EW truck within 18 km: halved)." : "."));
            return true;
        }
    }
}
