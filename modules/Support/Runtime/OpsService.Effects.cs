using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// What a finished countdown does. ZERO-DAY: a SAM NET FAIL on one revealed SAM cluster through the launch block a held SAM C2 node uses (3 minutes, halved when an enemy EW truck stands
    /// within 18 km of the cluster). ASAT: a real launcher vehicle beside the data center for the countdown (the vulnerable anchor with the data center), then a scripted ascent of 60 s that
    /// every client plays from the mirror, then the host-timed kill of one chosen enemy satellite through <see cref="Domain.Space.SpaceState.KillBird"/>.
    /// </summary>
    internal sealed partial class OpsService
    {
        private sealed class Flight
        {
            public int Id, Bird;
            public FactionOps Attacker;
            public FactionHQ Victim;
            public float X, Z, EndsAt, LaunchedAt;
        }

        private sealed partial class FactionOps
        {
            public Unit Launcher;
            public bool LauncherSpawned;
            /// <summary>A finished ASAT programme waits on its launcher: the satellite class it will fire at (-1 = none pending), when it launches, and the next reveal pulse.</summary>
            public int PendingBird = -1;
            public float LaunchAt, NextReveal;
        }

        private readonly List<Flight> flights = new List<Flight>(2);
        private int flightSerial;

        partial void ResetEffects()
        {
            foreach (var pair in factions) { DiscardLauncher(pair.Value); pair.Value.PendingBird = -1; }
            flights.Clear();
            flightSerial = 0;
        }

        partial void TickEffects(float now)
        {
            TickLaunchers(now);
            TickFlights(now);
            // A FOB that ended early (the building was retaken) stops showing as up.
            foreach (var pair in factions)
            {
                OpSlot s = pair.Value.Desk.Slot(OpDomain.Sof);
                if (s.Kind == OpKind.Fob && s.State == OpState.Done && s.EffectEnds > now && sof != null && !sof.FobUp(pair.Key)) pair.Value.Desk.EndEffect(OpDomain.Sof);
            }
        }

        /// <summary>
        /// OPS FRONTS S1b: a finished ZERO-DAY, ASAT or FOB programme runs its effect directly, with no operation bar behind it (the programme was the bar). ZERO-DAY takes the faction's nearest
        /// revealed SAM C2 node, ASAT the first satellite class (RADAR, OPTICAL, KINETIC) an enemy still has up, FOB the first building the faction holds. False when there is nothing to hit.
        /// </summary>
        internal bool RunProgramme(FactionHQ owner, ProgrammeId id)
        {
            if (!Enabled || owner == null || !factions.TryGetValue(owner, out FactionOps f)) return false;
            switch (id)
            {
                case ProgrammeId.ZeroDay:
                    return cyber != null && cyber.TryBestSamNode(owner, out int node) && cyber.TryNodeTarget(owner, node, out OpTarget sam) && FireZeroDay(f, sam);
                case ProgrammeId.Fob:
                    int held = sof != null ? sof.FirstHeldId(owner) : 0;
                    return held != 0 && sof.TryHeldTarget(owner, held, out OpTarget spot) && FireFob(f, spot);
                case ProgrammeId.Asat:
                    if (f.PendingBird >= 0) return false; // one launcher on the pad at a time
                    foreach (int cls in new[] { 1, 0, 2 })
                    {
                        FactionHQ victim = ChooseVictim(f, (BirdKind)cls);
                        if (victim == null) continue;
                        ArmLauncher(f, cls, victim);
                        return true;
                    }
                    return false;
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
                case OpKind.Asat: FireAsat(f, e.Target); break;
                case OpKind.Fob: FireFob(f, e.Target); break;
            }
        }

        // ---- FOB -------------------------------------------------------------------------------------

        /// <summary>
        /// The FORWARD OPERATING BASE goes live on the held building: 20 minutes (a second FOB renews it), +1 team cap, teams raised there, and a rearm vehicle (vanilla Rearmer) and a fuel vehicle (vanilla Refueler) beside it when
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

        /// <summary>One real launcher beside the data center. With no legal site the countdown still runs: the data center is then the only vulnerable anchor (logged).</summary>
        private void SpawnLauncher(FactionOps f)
        {
            DiscardLauncher(f);
            if (cyber == null || !cyber.TryDataCenterSpot(f.Owner, out GlobalPosition spot, out Airbase parent)) return;
            if (cyber.Spawner.TryCreateLauncher(f.Owner, 0, spot, parent, out Unit unit))
            {
                f.Launcher = unit; f.LauncherSpawned = true;
                Plugin.Logger?.LogInfo("[Support.Ops] " + f.Owner.name + " ASAT launcher up (" + CyberAnchorSpawner.DescribeLauncher() + "): kill it within 60 s to break the strike.");
            }
            else Plugin.Logger?.LogWarning("[Support.Ops] " + f.Owner.name + " ASAT launcher found no legal site: the countdown runs with the data center as its only vulnerable anchor.");
        }

        /// <summary>
        /// A finished ASAT programme puts its launcher on the pad beside the data center for <see cref="OpsRules.CountdownSeconds"/> before it fires (core: killing it in the countdown aborts
        /// the strike). The victim is warned at once and sees the launcher (a native sighting every few seconds), so it can be hunted.
        /// </summary>
        private void ArmLauncher(FactionOps f, int bird, FactionHQ victim)
        {
            float now = SupportManager.MissionNow();
            SpawnLauncher(f);
            f.PendingBird = bird; f.LaunchAt = now + OpsRules.CountdownSeconds; f.NextReveal = now;
            int victimKey = manager != null ? manager.FactionKeyOf(victim) : 0;
            f.Desk.AddPing(OpKind.Asat, OpPingPhase.Execute, OpsRules.CountdownSeconds + 5f, 0, victimKey);
            RodGuard.Tell(victim, "ENEMY ASAT LAUNCHER ON THE PAD — KILL IT WITHIN " + (int)OpsRules.CountdownSeconds + " S TO STOP THE STRIKE ON OUR " + OpsWords.Bird(bird) + " BIRD");
            Plugin.Logger?.LogInfo("[Support.Ops] " + f.Owner.name + " ASAT armed at the " + OpsWords.Bird(bird) + " bird of " + victim.name + ": launch in " + (int)OpsRules.CountdownSeconds + " s unless the launcher dies.");
        }

        /// <summary>The launcher countdown: a dead launcher aborts the strike, a live one is shown to every enemy, and the strike fires when the countdown ends.</summary>
        private void TickLaunchers(float now)
        {
            foreach (var pair in factions)
            {
                FactionOps f = pair.Value;
                if (f.PendingBird < 0) continue;
                if (f.LauncherSpawned && !LauncherUp(f))
                {
                    f.PendingBird = -1;
                    DiscardLauncher(f);
                    RodGuard.Tell(f.Owner, "ASAT LAUNCHER DESTROYED — STRIKE ABORTED");
                    RodGuard.Warn(f.Owner, "ENEMY ASAT LAUNCHER DESTROYED — STRIKE ABORTED");
                    Plugin.Logger?.LogInfo("[Support.Ops] " + f.Owner.name + " ASAT aborted: the launcher was destroyed in its countdown.");
                    continue;
                }
                if (now >= f.LaunchAt)
                {
                    int bird = f.PendingBird;
                    f.PendingBird = -1;
                    FactionHQ victim = ChooseVictim(f, (BirdKind)bird);
                    FireAsat(f, new OpTarget(bird, 0f, 0f, victim != null && manager != null ? manager.FactionKeyOf(victim) : 0));
                    continue;
                }
                if (now < f.NextReveal || !LauncherUp(f)) continue;
                f.NextReveal = now + 5f;
                var hqs = FactionRegistry.GetAllHQs();
                if (hqs == null) continue;
                foreach (FactionHQ enemy in hqs)
                {
                    if (enemy == null || enemy == f.Owner) continue;
                    try { enemy.RpcUpdateTrackingInfo(f.Launcher.persistentID); } // a real sighting: the launcher is a target
                    catch (System.Exception e) { Plugin.Logger?.LogDebug("[Support.Ops] Launcher reveal refused: " + e.Message); }
                }
            }
        }

        /// <summary>
        /// The launch: the ascent starts (60 s, played by every client from the flight row), the victim is warned (ASAT LAUNCH DETECTED), and the hit is host-timed in <see cref="TickFlights"/>.
        /// The victim is the other faction with the chosen satellite class up and the highest objective share; with none the strike flies and fizzles (logged).
        /// </summary>
        private void FireAsat(FactionOps f, in OpTarget target)
        {
            float now = SupportManager.MissionNow();
            var bird = (BirdKind)Mathf.Clamp(target.Id, 0, SpaceRules.BirdCount - 1);
            FactionHQ victim = ChooseVictim(f, bird);
            int victimKey = victim != null && manager != null ? manager.FactionKeyOf(victim) : 0;
            GlobalPosition at = default;
            bool placed = f.Launcher != null && !f.Launcher.disabled;
            if (placed) at = f.Launcher.transform.position.ToGlobalPosition();
            else if (cyber != null && cyber.TryDataCenterSpot(f.Owner, out GlobalPosition dc, out _)) { at = dc; placed = true; }
            float end = now + OpsRules.AsatFlightSeconds;
            if (flights.Count < 4)
                flights.Add(new Flight { Id = ++flightSerial, Attacker = f, Victim = victim, Bird = (int)bird, X = placed ? (float)at.x : 0f, Z = placed ? (float)at.z : 0f, EndsAt = end, LaunchedAt = now });
            if (victim != null)
                RodGuard.Tell(victim, "ASAT LAUNCH DETECTED — IMPACT ON OUR " + OpsWords.Bird((int)bird) + " BIRD IN " + (int)OpsRules.AsatFlightSeconds + " S · RELOCATE IT OR SET SPACE TO DEFEND TO DODGE");
            f.Desk.SetEffectEnd(OpDomain.Cyber, end);
            f.Desk.AddPing(OpKind.Asat, OpPingPhase.Launch, OpsRules.AsatFlightSeconds + 5f, 0, victimKey);
            Plugin.Logger?.LogInfo("[Support.Ops] " + f.Owner.name + " ASAT launched at the " + OpsWords.Bird((int)bird) + " bird of " + (victim != null ? victim.name : "nobody (no such bird is up)") + ", impact in " + (int)OpsRules.AsatFlightSeconds + " s.");
        }

        /// <summary>The victim of an ASAT on <paramref name="bird"/>: the other faction that still has that class up and the highest objective share (with two factions, simply the enemy).</summary>
        private FactionHQ ChooseVictim(FactionOps f, BirdKind bird)
        {
            FactionHQ best = null;
            float bestShare = -1f;
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null || space == null) return null;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null || hq == f.Owner || !space.TryGetStateCoarse(hq, out SpaceState state) || !state.HasBird(bird)) continue;
                float share = manager != null ? manager.ObjectiveShare(hq) : 0f;
                if (share > bestShare) { best = hq; bestShare = share; }
            }
            return best;
        }

        /// <summary>The ascent ended: the chosen bird dies through the SPACE state's own loss and rebuild path, the victim is told SAT LOST, and the launcher is removed.</summary>
        private void TickFlights(float now)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                Flight fl = flights[i];
                if (now < fl.EndsAt) continue;
                flights.RemoveAt(i);
                bool dodged = fl.Victim != null && Dodges(fl);
                bool hit = !dodged && fl.Victim != null && space != null && space.TryGetStateCoarse(fl.Victim, out SpaceState state) && state.KillBird((BirdKind)fl.Bird, now);
                if (dodged)
                {
                    RodGuard.Tell(fl.Victim, "ASAT EVADED — OUR " + OpsWords.Bird(fl.Bird) + " BIRD REPHASED IN TIME");
                    RodGuard.Tell(fl.Attacker.Owner, "ASAT MISSED — THE TARGET BIRD REPHASED");
                }
                if (hit) fl.Attacker.Desk.AddPing(OpKind.Asat, OpPingPhase.Loss, OpsRules.LossPingSeconds, fl.Bird, manager != null ? manager.FactionKeyOf(fl.Victim) : 0);
                Plugin.Logger?.LogInfo("[Support.Ops] " + fl.Attacker.Owner.name + " ASAT " + (hit ? "HIT: SAT LOST " + OpsWords.Bird(fl.Bird) + " of " + fl.Victim.name : "fizzled: nothing to hit") + ".");
                DiscardLauncher(fl.Attacker);
            }
        }

        /// <summary>
        /// The two-sided rule for ASAT: the victim dodges when its SPACE front stands on DEFEND (the bird rephases on the launch warning), or when someone burned that bird to a new
        /// station after the launch (a manual rephase from the ORBIT room).
        /// </summary>
        private bool Dodges(Flight fl)
        {
            if (manager?.Fronts != null && manager.Fronts.BiasFor(fl.Victim, Front.Space).Directive == FrontDirective.Defend) return true;
            return space != null && space.TryGetStateCoarse(fl.Victim, out SpaceState s) && s.HasBird((BirdKind)fl.Bird) && s.Geo((BirdKind)fl.Bird).DepartAt >= fl.LaunchedAt;
        }

        /// <summary>The viewer's own dead satellites with their rebuild progress, its own uplink sites, and every faction's ASAT in flight (a smoke column and a rising light all clients play from this row).</summary>
        partial void FillBirdsAndFlights(FactionHQ viewer, float now, OpsStateData into)
        {
            into.Sites.Clear();
            if (space != null) space.CopySites(viewer, into.Sites); // the viewer's own uplink sites: where they stand and how
            if (space != null && space.TryGetStateCoarse(viewer, out SpaceState state))
            {
                into.BirdsDown = state.DownMask;
                for (int i = 0; i < SpaceRules.BirdCount; i++) into.Geo[i] = state.Geo((BirdKind)i);
                for (int i = 0; i < SpaceRules.BirdCount; i++)
                    if (state.BirdDown((BirdKind)i)) into.BirdPercent[i] = (byte)Mathf.Clamp(state.RebuildPercent((BirdKind)i, now), 0, 100);
            }
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs != null)
                foreach (FactionHQ enemy in hqs)
                    if (enemy != null && enemy != viewer && space != null && space.TryGetStateCoarse(enemy, out SpaceState theirs)) { into.EnemyBirdsDown = theirs.DownMask; for (int i = 0; i < SpaceRules.BirdCount; i++) into.Geo[SpaceRules.BirdCount + i] = theirs.Geo((BirdKind)i); break; }
            foreach (Flight fl in flights)
                if (into.Flights.Count < OpsWire.MaxFlights && now < fl.EndsAt)
                {
                    // The launching faction sees the exact point; everyone else sees it on a 5 km grid (a launch is visible in the sky, the site stays unknown).
                    bool own = fl.Attacker != null && fl.Attacker.Owner == viewer;
                    into.Flights.Add(new OpsFlightRow { Id = fl.Id, X = own ? fl.X : Mathf.Round(fl.X / 5000f) * 5000f, Z = own ? fl.Z : Mathf.Round(fl.Z / 5000f) * 5000f, EndsAt = fl.EndsAt, Seconds = (byte)OpsRules.AsatFlightSeconds });
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
