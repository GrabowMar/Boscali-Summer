using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// SKYWELL PIT STOP (server): the receiver the tender holds station under is served one job at a time, in the order the pilot
    /// chose (FUEL / MUNITIONS / BOTH, one station first). Every round is a visible hand-off from the tender's magazine; a hostile
    /// missile closing on either aircraft is a HOT BREAK that releases the receiver but keeps what was already loaded; so is the
    /// receiver leaving the service zone (ZONE LOST).
    /// </summary>
    internal sealed partial class SkywellService
    {
        private const float HotBreakCheckInterval = 0.5f;
        private const float HotFlagSeconds = 3f;

        /// <summary>A receiver's standing order (the priority station is one-shot, cleared when the stop ends) and its running tally.</summary>
        private sealed class PitPrefs
        {
            public PitOrder Order = PitOrder.Both;
            public int Priority = -1;
            public PitTally Tally;
        }

        /// <summary>What this receiver has been served so far (the sim reads it per aircraft, so queued stops need no timing guesses).</summary>
        internal struct PitTally
        {
            public int Stops, Handoffs, FuelJobs, PriorityFirst, Tenths;
            public int[] PerStation;   // hand-offs committed per receiver station index (null until the first)
        }

        private readonly Dictionary<Aircraft, PitPrefs> prefs = new Dictionary<Aircraft, PitPrefs>();
        private static readonly List<PitStation> stationScratch = new List<PitStation>(16);
        private static readonly List<PitJob> planScratch = new List<PitJob>(16);
        private static readonly List<Missile> inbound = new List<Missile>(8);
        private static readonly List<Aircraft> pruneScratch = new List<Aircraft>(8);

        /// <summary>Sim hook: a stop just entered Service (tanker, receiver).</summary>
        internal static event Action<Aircraft, Aircraft> TransferStarted;

        private PitPrefs PrefsOf(Aircraft receiver)
        {
            if (!prefs.TryGetValue(receiver, out PitPrefs p)) prefs[receiver] = p = new PitPrefs();
            return p;
        }

        /// <summary>Read-only: scanning for receivers must not create an entry per aircraft.</summary>
        private PitOrder OrderFor(Aircraft receiver) => prefs.TryGetValue(receiver, out PitPrefs p) ? p.Order : PitOrder.Both;

        /// <summary>
        /// Forget aircraft that are gone and re-entry timers that ran out (called from the periodic scan). Orders (and their tallies,
        /// which the sim reads after a victim dies) are only swept once there are 32 of them.
        /// </summary>
        private void PruneState()
        {
            float now = Time.timeSinceLevelLoad;
            PruneTimers(cooldown, now);
            PruneTimers(lockout, now);
            foreach (Kit kit in kits.Values) PruneGone(kit.InZone);
            PruneGone(strikes);
            if (breaks.Count > 0)
            {
                pruneScratch.Clear();
                foreach (KeyValuePair<Aircraft, BreakOrder> pair in breaks)
                    if (pair.Key == null || pair.Key.disabled || now >= pair.Value.Until) pruneScratch.Add(pair.Key);
                foreach (Aircraft a in pruneScratch) breaks.Remove(a);
            }
            if (prefs.Count < 32) return;
            pruneScratch.Clear();
            foreach (Aircraft a in prefs.Keys)
                if (a == null || a.disabled) pruneScratch.Add(a);
            foreach (Aircraft a in pruneScratch) prefs.Remove(a);
        }

        private static void PruneGone<T>(Dictionary<Aircraft, T> map)
        {
            if (map.Count == 0) return;
            pruneScratch.Clear();
            foreach (Aircraft a in map.Keys)
                if (a == null || a.disabled) pruneScratch.Add(a);
            foreach (Aircraft a in pruneScratch) map.Remove(a);
        }

        private static void PruneTimers(Dictionary<Aircraft, float> timers, float now)
        {
            if (timers.Count == 0) return;
            pruneScratch.Clear();
            foreach (KeyValuePair<Aircraft, float> pair in timers)
                if (pair.Key == null || pair.Key.disabled || now >= pair.Value) pruneScratch.Add(pair.Key);
            foreach (Aircraft a in pruneScratch) timers.Remove(a);
        }

        /// <summary>Server: set the receiver's order and (optionally) the station served first. False when the priority is refused.</summary>
        public static bool SetPrefs(Aircraft receiver, PitOrder order, int priority)
        {
            if (instance == null || receiver == null || (byte)order > (byte)PitOrder.Munitions) return false;
            PitPrefs p = instance.PrefsOf(receiver);
            p.Order = order;
            bool accepted = true;
            if (priority < 0) p.Priority = -1;
            else if (receiver.weaponStations != null && priority < receiver.weaponStations.Count && !SkywellStations.Skip(receiver.weaponStations[priority]))
                p.Priority = priority;
            else accepted = false;
            foreach (Kit kit in instance.kits.Values)
                if (kit.Receiver == receiver && kit.State == TenderState.Service) kit.Replan = true; // applied at the next job boundary
            return accepted;
        }

        public static PitOrder OrderOf(Aircraft receiver) =>
            instance != null && receiver != null && instance.prefs.TryGetValue(receiver, out PitPrefs p) ? p.Order : PitOrder.Both;

        public static int PriorityOf(Aircraft receiver) =>
            instance != null && receiver != null && instance.prefs.TryGetValue(receiver, out PitPrefs p) ? p.Priority : -1;

        /// <summary>A destroyed aircraft still keys its tally (Unity compares destroyed objects by instance id), so a dead victim stays readable.</summary>
        public static PitTally TallyOf(Aircraft receiver) =>
            instance != null && !ReferenceEquals(receiver, null) && instance.prefs.TryGetValue(receiver, out PitPrefs p) ? p.Tally : default;

        // ---- the stop

        /// <summary>The tender is on station under the belly: build the job list from the receiver's real state and start job 0 (or let go at once).</summary>
        private void BeginStop(Kit kit, Aircraft r, float now)
        {
            kit.Jobs.Clear();
            kit.JobIndex = 0;
            kit.Flags = 0;
            kit.Replan = kit.JobCommitted = false;
            PrefsOf(r).Tally.Stops++;
            VanguardStats.Bump("skywell.stops");
            BuildPlan(kit, r);
            if (kit.Jobs.Count == 0)
            {
                kit.Flags |= PitFlags.Nothing;
                kit.PitTenths = 0;
                Drop(kit, PitRelease.Served);
                return;
            }
            kit.TransferStart = kit.JobStart = now;
            kit.JobEnd = now + kit.Jobs[0].Seconds;
            Enter(kit, TenderState.Service, 0f, now);
            TransferStarted?.Invoke(kit.Tanker, r);
        }

        /// <summary>Fills the planner's inputs from the receiver's LIVE state.</summary>
        private int Plan(Kit kit, Aircraft r, out bool capped)
        {
            SkywellStations.Collect(r, stationScratch);
            PitPrefs p = PrefsOf(r);
            float capacity = FuelCapacityKg(r), need = FuelNeedKg(r);
            if (need < Mathf.Max(PitPlan.FuelNeedFloorKg, 0.08f * capacity)) need = 0f;
            Player player = r.Player;
            return PitPlan.Plan(p.Order, need, r.GetFuelLevel(), kit.FuelKg, stationScratch, p.Priority,
                kit.Rounds, player != null ? player.Allocation : 0f, player == null, planScratch, out capped);
        }

        /// <summary>The job the tender will start with, planned without side effects (flags, tallies) so it can fly straight to its pose.</summary>
        private void PlanPreview(Kit kit, Aircraft r)
        {
            Plan(kit, r, out _);
            kit.Preview = planScratch.Count > 0 ? planScratch[0] : default;
        }

        /// <summary>Plans everything after the committed jobs from the receiver's LIVE state (so a re-plan drops finished work).</summary>
        private void BuildPlan(Kit kit, Aircraft r)
        {
            kit.Jobs.RemoveRange(kit.JobIndex, kit.Jobs.Count - kit.JobIndex);
            PitPrefs p = PrefsOf(r);
            float need = FuelNeedKg(r);
            Plan(kit, r, out bool capped);
            kit.Jobs.AddRange(planScratch);
            if (capped) kit.Flags |= PitFlags.Capped; // the HUD tells the pilot to fly the zone again for the dropped jobs

            if (p.Order != PitOrder.Munitions && need >= PitPlan.FuelNeedFloorKg && kit.FuelKg < 1f) kit.Flags |= PitFlags.FuelStockOut;
            if (p.Order != PitOrder.Fuel && kit.Rounds <= 0)
            {
                foreach (PitStation s in stationScratch)
                    if (!s.Skip && s.Missing > 0 && s.MassPerRound > 0f)
                    {
                        kit.Flags |= PitFlags.MunitionStockOut; // something to load, an empty magazine
                        break;
                    }
            }
            if (p.Priority >= 0 && kit.JobIndex == 0) // counted once per stop, not again by a re-plan
                foreach (PitJob job in planScratch)
                    if (job.Kind == PitJobKind.Handoff || job.Kind == PitJobKind.Cassette)
                    {
                        if (job.Station == p.Priority)
                        {
                            p.Tally.PriorityFirst++;
                            VanguardStats.Bump("skywell.priorityFirst");
                        }
                        break;
                    }
        }

        /// <summary>Service tick: commit the job that is due, re-plan if the order changed, start the next, or finish.</summary>
        private void RunJobs(Kit kit, Aircraft r, float now)
        {
            if (kit.JobIndex >= kit.Jobs.Count)
            {
                Finish(kit, r, now);
                return;
            }
            PitJob job = kit.Jobs[kit.JobIndex];
            // A round is handed over when the latch ends (1.2 s in); the tank is topped off as the boom starts to unplug.
            if (!kit.JobCommitted && now >= kit.JobStart + job.Commit) Commit(kit, r, job);
            if (now < kit.JobEnd) return;
            if (!kit.JobCommitted) Commit(kit, r, job);
            kit.JobIndex++;
            kit.JobCommitted = false;
            if (kit.Replan)
            {
                kit.Replan = false;
                BuildPlan(kit, r);
            }
            if (kit.JobIndex >= kit.Jobs.Count)
            {
                Finish(kit, r, now);
                return;
            }
            kit.JobStart = Mathf.Max(kit.JobEnd, now - 0.25f);
            kit.JobEnd = kit.JobStart + kit.Jobs[kit.JobIndex].Seconds;
            Publish(kit);
        }

        private void Commit(Kit kit, Aircraft r, PitJob job)
        {
            kit.JobCommitted = true;
            if (job.Kind == PitJobKind.Fuel) CommitFuel(kit, r);
            else CommitRearm(kit, r, job);
        }

        private void Finish(Kit kit, Aircraft r, float now)
        {
            kit.PitTenths = (ushort)Mathf.Clamp(Mathf.RoundToInt((now - kit.TransferStart) * 10f), 0, ushort.MaxValue);
            VanguardStats.Counters["skywell.pitTenths"] = kit.PitTenths;
            PitPrefs p = PrefsOf(r);
            p.Tally.Tenths = kit.PitTenths;
            p.Priority = -1;
            Drop(kit, PitRelease.Served);
        }

        /// <summary>Vanilla refuel is all-or-nothing; the stock is charged what the tanks actually needed.</summary>
        private void CommitFuel(Kit kit, Aircraft r)
        {
            float need = FuelNeedKg(r);
            if (need <= 1f) return;
            r.Refuel(kit.Tanker); // ponytail: no partial-fill API, so a near-empty stock still tops off
            kit.FuelKg = Mathf.Max(0f, kit.FuelKg - need);
            VanguardStats.Refuels++;
            PrefsOf(r).Tally.FuelJobs++;
            VanguardStats.Bump("skywell.fuelJob");
            Publish(kit);
        }

        /// <summary>One-hot RpcRearm: vanilla reloads exactly this station and re-shows one missile on its rail. One hand-off, one magazine round.</summary>
        private void CommitRearm(Kit kit, Aircraft r, PitJob job)
        {
            if (r.weaponStations == null || job.Station < 0 || job.Station >= r.weaponStations.Count) return;
            WeaponStation station = r.weaponStations[job.Station];
            WeaponInfo info = station?.WeaponInfo;
            if (info == null) return;
            int missing = Mathf.Max(0, station.FullAmmo - station.GetAmmoTotal());
            if (missing == 0) return; // reloaded some other way while we were queued
            if (kit.Rounds <= 0)
            {
                kit.Flags |= PitFlags.MunitionStockOut;
                Publish(kit);
                return;
            }
            Player player = r.Player;
            float funds = player != null ? player.Allocation : 0f, before = funds;
            var live = new PitStation { Index = job.Station, Missing = missing, Full = station.FullAmmo, MassPerRound = info.massPerRound, CostPerRound = info.costPerRound };
            int take = PitPlan.Afford(live, Mathf.Min(job.Rounds, missing), ref funds, player == null);
            if (take <= 0) return; // the pilot cannot pay for it
            var rounds = new int[r.weaponStations.Count];
            rounds[job.Station] = take;
            r.RpcRearm(new RearmEventArgs { Rearmer = kit.Tanker, Stations = rounds });
            if (player != null && before > funds) player.AddAllocation(funds - before);
            kit.Rounds = Mathf.Max(0, kit.Rounds - 1);
            VanguardStats.Rearms++;
            PitPrefs prefsOf = PrefsOf(r);
            prefsOf.Tally.Handoffs++;
            (prefsOf.Tally.PerStation ??= new int[32])[Mathf.Clamp(job.Station, 0, 31)]++;
            VanguardStats.Bump("skywell.handoff");
            Publish(kit);
        }

        // ---- hot break

        /// <summary>Every 0.5 s while the tender comes or serves: a hostile missile within 7 s of either aircraft kicks the receiver out.</summary>
        private bool HotBreakCheck(Kit kit, float now)
        {
            if (kit.Receiver == null || !SkywellTender.Serving(kit.State) || now < kit.NextHotCheck) return false;
            kit.NextHotCheck = now + HotBreakCheckInterval;
            if (!HostileInbound(kit.Receiver, kit.Tanker) && !HostileInbound(kit.Tanker, kit.Tanker)) return false;
            kit.HotUntil = now + HotFlagSeconds;
            VanguardStats.Bump("skywell.hotBreak");
            // What the victim held at the break, as numbers: the missile may destroy it before any status read (the sim reads these).
            PitTally tally = TallyOf(kit.Receiver);
            VanguardStats.Counters["skywell.breakHandoffs"] = tally.Handoffs;
            VanguardStats.Counters["skywell.breakFuelJobs"] = tally.FuelJobs;
            VanguardStats.Counters["skywell.breakJobIndex"] = kit.JobIndex;
            Drop(kit, PitRelease.HotBreak);
            return true;
        }

        private static bool HostileInbound(Unit host, Aircraft tanker)
        {
            ThreatScan.Inbound(host, inbound);
            Vector3 hostVelocity = host.rb != null ? host.rb.velocity : Vector3.zero;
            foreach (Missile m in inbound)
            {
                if (m.NetworkHQ == tanker.NetworkHQ) continue;
                Vector3 velocity = m.rb != null ? m.rb.velocity : m.transform.forward * m.speed; // clients' remote missiles have no body
                if (PitPlan.TimeToImpact(host.transform.position, hostVelocity, m.transform.position, velocity) < PitPlan.HotBreakTti) return true;
            }
            return false;
        }
    }
}
