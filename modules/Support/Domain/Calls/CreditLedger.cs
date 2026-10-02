using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>
    /// Host-owned personal CR wallets (core §6.1). Keyed by <c>PlayerIdentity.Of(player)</c>, so a rejoin finds the same
    /// wallet. Cleared at mission end. Bounded to <see cref="MaxWallets"/>.
    /// </summary>
    internal sealed class CreditLedger
    {
        public const float SoftCap = 600f, StarterGrant = 75f, JoinCap = 200f, JoinShare = 0.75f, FreezeSeconds = 600f;
        public const int MaxWallets = 128;

        private sealed class Wallet
        {
            public float Balance;
            public int Faction;
            public float FrozenUntil;
        }

        private readonly Dictionary<ulong, Wallet> wallets = new Dictionary<ulong, Wallet>();
        private readonly List<float> scratch = new List<float>();

        public bool Has(ulong id) => wallets.ContainsKey(id);

        public float Open(ulong id, int faction, bool midMission, IReadOnlyList<float> factionBalances)
        {
            if (wallets.ContainsKey(id)) return 0f;
            if (wallets.Count >= MaxWallets) return -1f;
            float grant = midMission && factionBalances != null && factionBalances.Count >= 3
                ? Math.Min(JoinCap, JoinShare * Median(factionBalances))
                : StarterGrant;
            wallets[id] = new Wallet { Balance = grant, Faction = faction };
            return grant;
        }

        public float Balance(ulong id) => wallets.TryGetValue(id, out Wallet w) ? w.Balance : 0f;

        public int Faction(ulong id) => wallets.TryGetValue(id, out Wallet w) ? w.Faction : 0;

        public float Credit(ulong id, float amount)
        {
            if (!Sane(amount) || !wallets.TryGetValue(id, out Wallet w)) return 0f;
            float room = Math.Max(0f, SoftCap - w.Balance);
            float taken = Math.Min(room, amount);
            w.Balance += taken;
            return amount - taken;
        }

        public bool TrySpend(ulong id, float cost, float now)
        {
            if (!Sane(cost) || !wallets.TryGetValue(id, out Wallet w)) return false;
            if (now < w.FrozenUntil || w.Balance + 0.001f < cost) return false;
            w.Balance = Math.Max(0f, w.Balance - cost);
            return true;
        }

        public void Refund(ulong id, float amount)
        {
            if (Sane(amount) && wallets.TryGetValue(id, out Wallet w)) w.Balance += amount;
        }

        public bool SetFaction(ulong id, int faction, float now)
        {
            if (!wallets.TryGetValue(id, out Wallet w) || w.Faction == faction) return false;
            w.Faction = faction;
            w.FrozenUntil = now + FreezeSeconds;
            return true;
        }

        public float FrozenRemaining(ulong id, float now) =>
            wallets.TryGetValue(id, out Wallet w) ? Math.Max(0f, w.FrozenUntil - now) : 0f;

        public void FactionBalances(int faction, ulong except, List<float> into)
        {
            into.Clear();
            foreach (KeyValuePair<ulong, Wallet> pair in wallets)
                if (pair.Key != except && pair.Value.Faction == faction) into.Add(pair.Value.Balance);
        }

        public void Clear() => wallets.Clear();

        private float Median(IReadOnlyList<float> values)
        {
            scratch.Clear();
            for (int i = 0; i < values.Count; i++) if (Sane(values[i]) || values[i] == 0f) scratch.Add(values[i]);
            if (scratch.Count == 0) return 0f;
            scratch.Sort();
            int mid = scratch.Count / 2;
            return scratch.Count % 2 == 1 ? scratch[mid] : 0.5f * (scratch[mid - 1] + scratch[mid]);
        }

        private static bool Sane(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f;
    }

    /// <summary>Shadow HQ FUND per faction (core §6.4). M0 only accumulates; no vanilla funds are touched.</summary>
    internal sealed class HqFundLedger
    {
        private readonly Dictionary<int, float> funds = new Dictionary<int, float>();

        public void Add(int faction, float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return;
            funds.TryGetValue(faction, out float now);
            funds[faction] = now + amount;
        }

        public float Balance(int faction) => funds.TryGetValue(faction, out float v) ? v : 0f;

        public void Clear() => funds.Clear();
    }
}
