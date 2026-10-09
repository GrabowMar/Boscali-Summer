using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// SKYWELL PIT STOP (server): the docked receiver is served one job at a time, in the order the pilot chose
    /// (FUEL / MUNITIONS / BOTH, one station first). Every round is a visible hot-swap handoff; a hostile missile
    /// closing on either aircraft is a HOT BREAK that releases the receiver but keeps what was already loaded.
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
        }

        private readonly Dictionary<Aircraft, PitPrefs> prefs = new Dictionary<Aircraft, PitPrefs>();
        private static readonly List<PitStation> stationScratch = new List<PitStation>(16);
        private static readonly List<PitJob> planScratch = new List<PitJob>(16);
        private static readonly List<Missile> inbound = new List<Missile>(8);

        /// <summary>Sim hook: a stop just entered Transfer (tanker, receiver).</summary>
        internal static event Action<Aircraft, Aircraft> TransferStarted;

        private PitPrefs PrefsOf(Aircraft receiver)
        {
            if (!prefs.TryGetValue(receiver, out PitPrefs p)) prefs[receiver] = p = new PitPrefs();
            return p;
        }

        /// <summary>Read-only: scanning for receivers must not create an entry per aircraft.</summary>
        private PitOrder OrderFor(Aircraft receiver) => prefs.TryGetValue(receiver, out PitPrefs p) ? p.Order : PitOrder.Both;

        /// <summary>Forget orders of aircraft that are gone (called from the periodic scan).</summary>
        private void PrunePrefs()
        {
            if (prefs.Count < 32) return;
            var gone = new List<Aircraft>();
            foreach (Aircraft a in prefs.Keys)
                if (a == null || a.disabled) gone.Add(a);
            foreach (Aircraft a in gone) prefs.Remove(a);
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
                if (kit.Receiver == receiver && kit.Phase == SkywellPhase.Transfer) kit.Replan = true; // applied at the next job boundary
            return accepted;
        }

        public static PitOrder OrderOf(Aircraft receiver) =>
            instance != null && receiver != null && instance.prefs.TryGetValue(receiver, out PitPrefs p) ? p.Order : PitOrder.Both;

        public static int PriorityOf(Aircraft receiver) =>
            instance != null && receiver != null && instance.prefs.TryGetValue(receiver, out PitPrefs p) ? p.Priority : -1;

        public static PitTally TallyOf(Aircraft receiver) =>
            instance != null && receiver != null && instance.prefs.TryGetValue(receiver, out PitPrefs p) ? p.Tally : default;

        // ---- the stop

        /// <summary>The probe is in: build the job list from the receiver's real state and start job 0 (or finish at once).</summary>
        private void BeginStop(Kit kit, Aircraft r, float now)
        {
            kit.Jobs.Clear();
            kit.JobIndex = 0;
            kit.Flags = 0;
            kit.Replan = kit.FuelCommitted = false;
            PrefsOf(r).Tally.Stops++;
            VanguardStats.Bump("skywell.stops");
            BuildPlan(kit, r);
            if (kit.Jobs.Count == 0)
            {
                kit.Flags |= PitFlags.Nothing;
                kit.PitTenths = 0;
                Enter(kit, SkywellPhase.Served, now + SkywellContact.ReleaseSeconds);
                return;
            }
            kit.TransferStart = kit.JobStart = now;
            kit.JobEnd = now + kit.Jobs[0].Seconds;
            Enter(kit, SkywellPhase.Transfer, 0f);
            TransferStarted?.Invoke(kit.Tanker, r);
        }

        /// <summary>Plans everything after the committed jobs from the receiver's LIVE state (so a re-plan drops finished work).</summary>
        private void BuildPlan(Kit kit, Aircraft r)
        {
            kit.Jobs.RemoveRange(kit.JobIndex, kit.Jobs.Count - kit.JobIndex);
            SkywellStations.Collect(r, stationScratch);
            PitPrefs p = PrefsOf(r);
            float capacity = FuelCapacityKg(r), need = FuelNeedKg(r);
            if (need < Mathf.Max(PitPlan.FuelNeedFloorKg, 0.08f * capacity)) need = 0f;
            Player player = r.Player;
            PitPlan.Plan(p.Order, need, r.GetFuelLevel(), kit.FuelKg, stationScratch, p.Priority,
                kit.StockKg, player != null ? player.Allocation : 0f, player == null, planScratch);
            kit.Jobs.AddRange(planScratch);

            if (p.Order != PitOrder.Munitions && need >= PitPlan.FuelNeedFloorKg && kit.FuelKg < 1f) kit.Flags |= PitFlags.FuelStockOut;
            if (p.Order != PitOrder.Fuel)
            {
                float lightest = float.MaxValue;
                foreach (PitStation s in stationScratch)
                    if (!s.Skip && s.Missing > 0 && s.MassPerRound > 0f) lightest = Mathf.Min(lightest, s.MassPerRound);
                if (lightest < float.MaxValue && kit.StockKg < lightest) kit.Flags |= PitFlags.MunitionStockOut;
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

        /// <summary>Transfer tick: commit the job that is due, re-plan if the order changed, start the next, or finish.</summary>
        private void RunJobs(Kit kit, Aircraft r, float now)
        {
            if (kit.JobIndex >= kit.Jobs.Count)
            {
                Finish(kit, r, now);
                return;
            }
            PitJob job = kit.Jobs[kit.JobIndex];
            // The tank is topped off as the boom starts to unplug, so the gauge jumps before the job's last 0.6 s.
            if (job.Kind == PitJobKind.Fuel && !kit.FuelCommitted && now >= kit.JobEnd - PitPlan.FuelUnplug) CommitFuel(kit, r);
            if (now < kit.JobEnd) return;
            if (job.Kind != PitJobKind.Fuel) CommitRearm(kit, r, job);
            kit.JobIndex++;
            kit.FuelCommitted = false;
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

        private void Finish(Kit kit, Aircraft r, float now)
        {
            kit.PitTenths = (ushort)Mathf.Clamp(Mathf.RoundToInt((now - kit.TransferStart) * 10f), 0, ushort.MaxValue);
            VanguardStats.Counters["skywell.pitTenths"] = kit.PitTenths;
            PitPrefs p = PrefsOf(r);
            p.Tally.Tenths = kit.PitTenths;
            p.Priority = -1;
            Enter(kit, SkywellPhase.Served, now + SkywellContact.ReleaseSeconds);
        }

        /// <summary>Vanilla refuel is all-or-nothing; the stock is charged what the tanks actually needed.</summary>
        private void CommitFuel(Kit kit, Aircraft r)
        {
            kit.FuelCommitted = true;
            float need = FuelNeedKg(r);
            if (need <= 1f) return;
            r.Refuel(kit.Tanker); // ponytail: no partial-fill API, so a near-empty stock still tops off
            kit.FuelKg = Mathf.Max(0f, kit.FuelKg - need);
            VanguardStats.Refuels++;
            PrefsOf(r).Tally.FuelJobs++;
            VanguardStats.Bump("skywell.fuelJob");
        }

        /// <summary>One-hot RpcRearm: vanilla reloads exactly this station and re-shows one missile on its rail.</summary>
        private void CommitRearm(Kit kit, Aircraft r, PitJob job)
        {
            if (r.weaponStations == null || job.Station < 0 || job.Station >= r.weaponStations.Count) return;
            WeaponStation station = r.weaponStations[job.Station];
            WeaponInfo info = station?.WeaponInfo;
            if (info == null) return;
            int missing = Mathf.Max(0, station.FullAmmo - station.GetAmmoTotal());
            if (missing == 0) return; // reloaded some other way while we were queued
            Player player = r.Player;
            float funds = player != null ? player.Allocation : 0f, before = funds;
            var live = new PitStation { Index = job.Station, Missing = missing, Full = station.FullAmmo, MassPerRound = info.massPerRound, CostPerRound = info.costPerRound };
            int take = PitPlan.Take(live, Mathf.Min(job.Rounds, missing), ref kit.StockKg, ref funds, player == null);
            if (take <= 0)
            {
                if (kit.StockKg < info.massPerRound) kit.Flags |= PitFlags.MunitionStockOut;
                Publish(kit);
                return;
            }
            var rounds = new int[r.weaponStations.Count];
            rounds[job.Station] = take;
            r.RpcRearm(new RearmEventArgs { Rearmer = kit.Tanker, Stations = rounds });
            if (player != null && before > funds) player.AddAllocation(funds - before);
            VanguardStats.Rearms++;
            PrefsOf(r).Tally.Handoffs++;
            VanguardStats.Bump("skywell.handoff");
        }

        // ---- hot break

        /// <summary>Every 0.5 s while docking or serving: a hostile missile within 7 s of either aircraft kicks the receiver out.</summary>
        private bool HotBreakCheck(Kit kit, float now)
        {
            if (kit.Receiver == null || (kit.Phase != SkywellPhase.Engage && kit.Phase != SkywellPhase.Transfer) || now < kit.NextHotCheck) return false;
            kit.NextHotCheck = now + HotBreakCheckInterval;
            if (!HostileInbound(kit.Receiver, kit.Tanker) && !HostileInbound(kit.Tanker, kit.Tanker)) return false;
            kit.HotUntil = now + HotFlagSeconds;
            VanguardStats.Bump("skywell.hotBreak");
            Drop(kit, false, true);
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
