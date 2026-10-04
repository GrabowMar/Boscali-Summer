using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>
    /// Where a contributor payout went. <c>Wallet + Hq + Unapplied</c> always equals the requested amount.
    /// <c>Hq</c> is soft-cap overflow the paying method already deposited; <c>Unapplied</c> is refused, switched, frozen
    /// or operator-capped money the caller must send to the earned faction's HQ FUND exactly once.
    /// </summary>
    internal readonly struct ContributorCreditReceipt
    {
        public readonly float Wallet, Hq, Unapplied;
        public ContributorCreditReceipt(float wallet, float hq, float unapplied) { Wallet = wallet; Hq = hq; Unapplied = unapplied; }
    }

    /// <summary>Core §6.2: operator fee/assist income is capped at 360 CR per hour per identity.</summary>
    internal sealed class OperatorIncomeMeter
    {
        public const float HourCap = 360f, WindowSeconds = 3600f;

        private sealed class Meter { public float WindowStart = float.NegativeInfinity, Paid; }

        private readonly Dictionary<ulong, Meter> meters = new Dictionary<ulong, Meter>();

        /// <summary>Reserves up to <paramref name="amount"/> of the player's remaining hourly room and returns what fits.</summary>
        public float Take(ulong id, float amount, float now)
        {
            if (id == 0 || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f ||
                float.IsNaN(now) || float.IsInfinity(now)) return 0f;
            if (!meters.TryGetValue(id, out Meter meter))
            {
                if (meters.Count >= CreditLedger.MaxWallets) return 0f;
                meters[id] = meter = new Meter();
            }
            if (now - meter.WindowStart >= WindowSeconds) { meter.WindowStart = now; meter.Paid = 0f; }
            float granted = Math.Max(0f, Math.Min(amount, HourCap - meter.Paid));
            meter.Paid += granted;
            return granted;
        }

        public void Clear() => meters.Clear();
    }

    /// <summary>
    /// The one host money path TASKED calls use: identity wallets, the HQ FUND shadow and the operator income cap.
    /// Engine-free so the escrow, refund and payout rules are testable; <see cref="Changed"/> lets the runtime mirror a balance.
    /// </summary>
    internal sealed class TaskedWallets
    {
        public CreditLedger Ledger { get; }
        public HqFundLedger Fund { get; }
        public OperatorIncomeMeter Income { get; }
        public Action<ulong> Changed { get; set; }

        public TaskedWallets(CreditLedger ledger, HqFundLedger fund, OperatorIncomeMeter income)
        {
            Ledger = ledger; Fund = fund; Income = income;
        }

        public float Balance(ulong id) => Ledger.Balance(id);
        public bool IsActive(ulong id, int faction, float now) => Ledger.IsActive(id, faction, now);
        public float FrozenRemaining(ulong id, float now) => Ledger.FrozenRemaining(id, now);

        public bool TrySpend(ulong id, float amount, float now)
        {
            if (!Ledger.TrySpend(id, amount, now)) return false;
            Changed?.Invoke(id);
            return true;
        }

        public void Refund(ulong id, float amount)
        {
            Ledger.Refund(id, amount);
            Changed?.Invoke(id);
        }

        public void AddHq(int faction, float amount) => Fund.Add(faction, amount);

        /// <summary>
        /// Pays a verified contributor by identity, never by a connected Player. Refuses switched/frozen identities and
        /// applies the operator cap; the soft-cap overflow is deposited here, refused money is returned for the caller.
        /// </summary>
        public ContributorCreditReceipt EarnContributor(ulong playerId, int earnedFaction, float amount, float missionNow)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return default;
            if (!Ledger.IsActive(playerId, earnedFaction, missionNow)) return new ContributorCreditReceipt(0f, 0f, amount);
            float allowed = Income.Take(playerId, amount, missionNow);
            float overflow = allowed > 0f ? Ledger.Credit(playerId, allowed) : 0f;
            float wallet = allowed - overflow;
            if (overflow > 0f) Fund.Add(earnedFaction, overflow);
            if (wallet > 0f) Changed?.Invoke(playerId);
            return new ContributorCreditReceipt(wallet, overflow, amount - allowed);
        }
    }
}
