using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Networking;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal readonly struct ObjectiveCount
    {
        public readonly int held, contested, n;

        public ObjectiveCount(int held, int contested, int n)
        {
            this.held = held;
            this.contested = contested;
            this.n = n;
        }
    }

    /// <summary>
    /// Host-side CR economy (core §6). Opens wallets with the starter / joiner grant, pays the trickle, freezes wallets
    /// on a faction switch and mirrors each owner's balance. Clients only read <see cref="CreditStateMessage"/>.
    /// </summary>
    internal sealed class CreditService
    {
        public const float JoinAfterSeconds = 120f; // a wallet opened later than this is a mid-mission joiner
        private const float CensusSeconds = 2f;

        private readonly SupportNet network;
        private readonly List<float> balances = new List<float>();
        private readonly Dictionary<ulong, (int balance, int frozen, float ev, float silent, float at)> sent =
            new Dictionary<ulong, (int, int, float, float, float)>();
        private readonly HashSet<ulong> seen = new HashSet<ulong>();
        private readonly List<ulong> gone = new List<ulong>();
        private const float ResendSeconds = 10f;

        /// <summary>Host's combined price factor for a player (knob x events x perk); sent so clients quote like the host.</summary>
        public System.Func<Player, (float eventFactor, float silentFactor)> PriceFactors { get; set; }
        private readonly Dictionary<FactionHQ, (float at, ObjectiveCount count)> census =
            new Dictionary<FactionHQ, (float, ObjectiveCount)>();

        public CreditLedger Ledger { get; } = new CreditLedger();
        public HqFundLedger Fund { get; } = new HqFundLedger();
        public TrickleMeter Trickle { get; } = new TrickleMeter();
        public RepeatTracker Repeats { get; } = new RepeatTracker();
        public AssistRegistry Assists { get; } = new AssistRegistry();
        public CreditActivity Activity { get; } = new CreditActivity();
        /// <summary>Operator fee income cap (core §6.2); kill-assist income shares this meter when it is built.</summary>
        public OperatorIncomeMeter OperatorIncome { get; } = new OperatorIncomeMeter();
        /// <summary>The TASKED call money path over this service's own wallets and HQ FUND.</summary>
        public TaskedWallets Tasked { get; }

        public CreditService(SupportNet network)
        {
            this.network = network;
            Tasked = new TaskedWallets(Ledger, Fund, OperatorIncome) { Changed = Mirror };
        }

        private void Mirror(ulong id)
        {
            // Never throws: a failed mirror retries on the next Tick, the debit/refund it follows already happened.
            try
            {
                if (FactionRegistry.GetAllHQs() == null) return;
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq == null) continue;
                    foreach (Player player in hq.GetPlayers(false))
                        if (player != null && PlayerIdentity.Of(player) == id) { SendIfChanged(player, id, SupportManager.MissionNow()); return; }
                }
            }
            catch (System.Exception e) { Plugin.Logger?.LogWarning("[Support.Credit] Wallet mirror skipped: " + e.Message); }
        }

        public void Tick(float now, float dt)
        {
            if (FactionRegistry.GetAllHQs() == null) return;
            seen.Clear();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null) continue;
                foreach (Player player in hq.GetPlayers(false))
                {
                    if (player == null || player.HQ == null) continue;
                    ulong id = PlayerIdentity.Of(player);
                    if (id == PlayerIdentity.None) continue;
                    seen.Add(id);
                    int faction = FactionKey(player.HQ);
                    if (!Ledger.Has(id))
                    {
                        Ledger.FactionBalances(faction, id, balances);
                        if (Ledger.Open(id, faction, now > JoinAfterSeconds, balances) < 0f) continue;
                    }
                    else Ledger.SetFaction(id, faction, now);

                    Aircraft aircraft = player.Aircraft;
                    // radarAlt already subtracts spawnOffset; 0.2 m is vanilla's airborne threshold.
                    bool airborne = aircraft != null && !aircraft.disabled && aircraft.radarAlt > 0.2f &&
                        !float.IsNaN(aircraft.radarAlt) && !float.IsInfinity(aircraft.radarAlt);
                    bool active = Activity.IsActive(id, airborne, now);
                    float trickle = Trickle.Tick(id, active, dt, now);
                    if (trickle > 0f) Fund.Add(faction, Ledger.Credit(id, trickle));
                    SendIfChanged(player, id, now);
                }
            }

            // A player who left must get a fresh state when they rejoin.
            gone.Clear();
            foreach (ulong id in sent.Keys) if (!seen.Contains(id)) gone.Add(id);
            for (int i = 0; i < gone.Count; i++) sent.Remove(gone[i]);
            Activity.Prune(seen);
        }

        /// <summary>Observe a raw pilot snapshot; applied aircraft controls may include autopilot commands.</summary>
        public bool RecordAircraftInput(Aircraft aircraft, ActivityControls controls, float now)
        {
            Player player = aircraft?.Player;
            if (player == null || player.HQ == null || aircraft.disabled || !ReferenceEquals(player.Aircraft, aircraft)) return false;
            return Activity.Observe(PlayerIdentity.Of(player), aircraft.GetInstanceID(), controls, now);
        }

        public void RecordInput(Player player, float now)
        {
            if (player != null && player.HQ != null) Activity.Record(PlayerIdentity.Of(player), now);
        }

        public void RecordPulse(Player player, float now, float wallTime)
        {
            if (player != null && player.HQ != null) Activity.Pulse(PlayerIdentity.Of(player), now, wallTime);
        }

        public bool TrySpend(Player player, float cost, float now)
        {
            ulong id = PlayerIdentity.Of(player);
            bool ok = Ledger.TrySpend(id, cost, now);
            if (ok) SendIfChanged(player, id, now);
            return ok;
        }

        public void Refund(Player player, float amount, float now)
        {
            ulong id = PlayerIdentity.Of(player);
            Ledger.Refund(id, amount);
            SendIfChanged(player, id, now);
        }

        public void Earn(Player player, float credit, float now)
        {
            if (player == null || player.HQ == null || credit <= 0f) return;
            ulong id = PlayerIdentity.Of(player);
            Fund.Add(FactionKey(player.HQ), Ledger.Credit(id, credit));
            SendIfChanged(player, id, now);
        }

        public int FactionKey(FactionHQ hq) => hq == null ? 0 : hq.GetInstanceID();

        /// <summary>
        /// Ground airbases (carriers excluded): held by <paramref name="hq"/>, contested (being captured by it), total.
        /// Capturability is not readable on this build, so every non-carrier airbase counts. Cached for 2 s per faction.
        /// </summary>
        public ObjectiveCount Census(FactionHQ hq)
        {
            if (hq == null) return default;
            float t = Time.unscaledTime;
            if (census.TryGetValue(hq, out var cached) && t - cached.at < CensusSeconds) return cached.count;

            int held = 0, contested = 0, n = 0;
            if (FactionRegistry.airbaseLookup != null)
            {
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;
                    n++;
                    if (airbase.CurrentHQ == hq) held++;
                    else if (airbase.capture != null && airbase.capture.capturingHQ == hq) contested++;
                }
            }
            var count = new ObjectiveCount(held, contested, n);
            census[hq] = (t, count);
            return count;
        }

        public void Clear()
        {
            Ledger.Clear();
            Fund.Clear();
            Trickle.Clear();
            Repeats.Clear();
            Assists.Clear();
            Activity.Clear();
            OperatorIncome.Clear();
            sent.Clear();
            census.Clear();
        }

        private void SendIfChanged(Player player, ulong id, float now)
        {
            // Presentation failure cannot turn an already-mutated host debit into a thrown transaction.
            try { SendState(player, id, now); }
            catch (System.Exception e) { Plugin.Logger?.LogWarning("[Support.Credit] State mirror will retry: " + e.Message); }
        }

        private void SendState(Player player, ulong id, float now)
        {
            int balance = (int)Ledger.Balance(id);
            int frozen = (int)Ledger.FrozenRemaining(id, now);
            (float ev, float silent) = PriceFactors != null ? PriceFactors(player) : (1f, 1f);
            float clock = Time.unscaledTime;
            if (sent.TryGetValue(id, out var last) && last.balance == balance && last.frozen == frozen &&
                last.ev == ev && last.silent == silent && clock - last.at < ResendSeconds) return;
            if (network == null) return;
            if (!network.SendCredit(player, balance, frozen, ev, silent)) return;
            sent[id] = (balance, frozen, ev, silent, clock);
        }
    }
}
