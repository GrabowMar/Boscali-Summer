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
        private readonly Dictionary<ulong, long> sent = new Dictionary<ulong, long>();
        private readonly Dictionary<FactionHQ, (float at, ObjectiveCount count)> census =
            new Dictionary<FactionHQ, (float, ObjectiveCount)>();

        public CreditLedger Ledger { get; } = new CreditLedger();
        public HqFundLedger Fund { get; } = new HqFundLedger();
        public TrickleMeter Trickle { get; } = new TrickleMeter();
        public RepeatTracker Repeats { get; } = new RepeatTracker();
        public AssistRegistry Assists { get; } = new AssistRegistry();

        public CreditService(SupportNet network) => this.network = network;

        public void Tick(float now, float dt)
        {
            if (FactionRegistry.GetAllHQs() == null) return;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null) continue;
                foreach (Player player in hq.GetPlayers(false))
                {
                    if (player == null || player.HQ == null) continue;
                    ulong id = PlayerIdentity.Of(player);
                    if (id == PlayerIdentity.None) continue;
                    int faction = FactionKey(player.HQ);
                    if (!Ledger.Has(id))
                    {
                        Ledger.FactionBalances(faction, id, balances);
                        if (Ledger.Open(id, faction, now > JoinAfterSeconds, balances) < 0f) continue;
                    }
                    else Ledger.SetFaction(id, faction, now);

                    bool active = player.Aircraft != null && !player.Aircraft.disabled;
                    float trickle = Trickle.Tick(id, active, dt, now);
                    if (trickle > 0f) Fund.Add(faction, Ledger.Credit(id, trickle));
                    SendIfChanged(player, id, now);
                }
            }
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
            sent.Clear();
            census.Clear();
        }

        private void SendIfChanged(Player player, ulong id, float now)
        {
            int balance = (int)Ledger.Balance(id);
            int frozen = (int)Ledger.FrozenRemaining(id, now);
            long key = (long)balance * 100000L + frozen;
            if (sent.TryGetValue(id, out long last) && last == key) return;
            sent[id] = key;
            network?.SendCredit(player, balance, frozen);
        }
    }
}
