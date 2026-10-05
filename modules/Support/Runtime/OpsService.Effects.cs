using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
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
            public float X, Z, EndsAt;
        }

        private sealed partial class FactionOps
        {
            public Unit Launcher;
            public bool LauncherSpawned;
        }

        private readonly List<Flight> flights = new List<Flight>(2);
        private int flightSerial;

        partial void ResetEffects()
        {
            foreach (var pair in factions) DiscardLauncher(pair.Value);
            flights.Clear();
            flightSerial = 0;
        }

        partial void TickEffects(float now)
        {
            TickFlights(now);
            // A FOB that ended early (the building was retaken) stops showing as up.
            foreach (var pair in factions)
            {
                OpSlot s = pair.Value.Desk.Slot(OpDomain.Sof);
                if (s.Kind == OpKind.Fob && s.State == OpState.Done && s.EffectEnds > now && sof != null && !sof.FobUp(pair.Key)) pair.Value.Desk.EndEffect(OpDomain.Sof);
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
        private void FireFob(FactionOps f, in OpTarget target)
        {
            if (sof == null || !sof.StartFob(f.Owner, target.Id, out float until))
            {
                Plugin.Logger?.LogWarning("[Support.Ops] " + f.Owner.name + " FOB could not start: the held building " + target.Id + " is gone; every member is refunded.");
                f.Desk.Fail(OpDomain.Sof);
                return;
            }
            f.Desk.SetEffectEnd(OpDomain.Sof, until);
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
                flights.Add(new Flight { Id = ++flightSerial, Attacker = f, Victim = victim, Bird = (int)bird, X = placed ? (float)at.x : 0f, Z = placed ? (float)at.z : 0f, EndsAt = end });
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
                bool hit = fl.Victim != null && space != null && space.TryGetStateCoarse(fl.Victim, out SpaceState state) && state.KillBird((BirdKind)fl.Bird, now);
                if (hit) fl.Attacker.Desk.AddPing(OpKind.Asat, OpPingPhase.Loss, OpsRules.LossPingSeconds, fl.Bird, manager != null ? manager.FactionKeyOf(fl.Victim) : 0);
                Plugin.Logger?.LogInfo("[Support.Ops] " + fl.Attacker.Owner.name + " ASAT " + (hit ? "HIT: SAT LOST " + OpsWords.Bird(fl.Bird) + " of " + fl.Victim.name : "fizzled: nothing to hit") + ".");
                DiscardLauncher(fl.Attacker);
            }
        }

        /// <summary>The viewer's own dead satellites with their rebuild progress, and every faction's ASAT in flight (a smoke column and a rising light all clients play from this row).</summary>
        partial void FillBirdsAndFlights(FactionHQ viewer, float now, OpsStateData into)
        {
            if (space != null && space.TryGetStateCoarse(viewer, out SpaceState state))
            {
                into.BirdsDown = state.DownMask;
                for (int i = 0; i < SpaceRules.BirdCount; i++)
                    if (state.BirdDown((BirdKind)i)) into.BirdPercent[i] = (byte)Mathf.Clamp(state.RebuildPercent((BirdKind)i, now), 0, 100);
            }
            foreach (Flight fl in flights)
                if (into.Flights.Count < OpsWire.MaxFlights && now < fl.EndsAt)
                {
                    // The launching faction sees the exact point; everyone else sees it on a 5 km grid (a launch is visible in the sky, the site stays unknown).
                    bool own = fl.Attacker != null && fl.Attacker.Owner == viewer;
                    into.Flights.Add(new OpsFlightRow { Id = fl.Id, X = own ? fl.X : Mathf.Round(fl.X / 5000f) * 5000f, Z = own ? fl.Z : Mathf.Round(fl.Z / 5000f) * 5000f, EndsAt = fl.EndsAt, Seconds = (byte)OpsRules.AsatFlightSeconds });
                }
        }

        private void FireZeroDay(FactionOps f, in OpTarget target)
        {
            float now = SupportManager.MissionNow();
            bool truckNear = cyber != null && cyber.EnemyTruckWithin(target.Victim, target.X, target.Z, OpsRules.EwHalveMetres);
            float seconds = OpsRules.ZeroDayEffectSeconds(truckNear);
            if (cyber == null || !cyber.AddSamNetFail(f.Owner, target, seconds))
            {
                Plugin.Logger?.LogWarning("[Support.Ops] " + f.Owner.name + " SAM NET FAIL could not be applied (CYBER desk missing or the effect book is full); every member is refunded.");
                f.Desk.Fail(OpDomain.Cyber);
                return;
            }
            f.Desk.SetEffectEnd(OpDomain.Cyber, now + seconds);
            Plugin.Logger?.LogInfo("[Support.Ops] " + f.Owner.name + " SAM NET FAIL on node " + target.Id + " for " + (int)seconds + " s" + (truckNear ? " (enemy EW truck within 18 km: halved)." : "."));
        }
    }
}
