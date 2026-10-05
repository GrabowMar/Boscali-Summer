using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>Current host-owned CR wallets, earnings and bounded per-player/faction bookkeeping.</summary>
    internal static class CreditTests
    {
        public static void Run()
        {
            CheckLedger();
            CheckEarning();
            CheckBoundaries();
        }

        private static void CheckLedger()
        {
            var ledger = new CreditLedger();
            Near(ledger.Open(1UL, 7, false, null), 75f, "starter grant");
            Near(ledger.Open(1UL, 7, false, null), 0f, "reopen gives nothing (rejoin)");
            Near(ledger.Balance(1UL), 75f, "balance after grant");
            Near(ledger.Balance(99UL), 0f, "unknown wallet is empty");

            TestAssert.That(ledger.TrySpend(1UL, 25f, 0f), "spend within balance");
            Near(ledger.Balance(1UL), 50f, "balance after spend");
            TestAssert.That(!ledger.TrySpend(1UL, 51f, 0f), "cannot overspend");
            TestAssert.That(!ledger.TrySpend(99UL, 1f, 0f), "unknown wallet cannot spend");
            TestAssert.That(!ledger.TrySpend(1UL, float.NaN, 0f), "NaN cost refused");
            ledger.Refund(1UL, 25f);
            Near(ledger.Balance(1UL), 75f, "refund");

            Near(ledger.Credit(1UL, 500f), 0f, "no overflow below cap");
            Near(ledger.Credit(1UL, 100f), 75f, "overflow above 600");
            Near(ledger.Balance(1UL), 600f, "capped at 600");
            Near(ledger.Credit(1UL, -10f), 0f, "negative credit ignored");
            Near(ledger.Balance(1UL), 600f, "negative credit ignored balance");
            ledger.Refund(1UL, 50f);
            Near(ledger.Balance(1UL), 650f, "refunds may exceed the soft cap");

            // Mid-mission joiner: 75 % of the faction median, cap 200; fewer than 3 others = starter grant.
            ledger.Open(2UL, 7, false, null);
            ledger.Credit(2UL, 125f); // 200
            ledger.Open(3UL, 7, false, null);
            ledger.Credit(3UL, 25f);  // 100
            var others = new List<float>();
            ledger.FactionBalances(7, 4UL, others);
            Eq(others.Count, 3, "three faction wallets");
            Near(ledger.Open(4UL, 7, true, others), 150f, "75 % of median 200");
            var rich = new List<float> { 600f, 600f, 600f };
            Near(ledger.Open(5UL, 7, true, rich), 200f, "join grant capped at 200");
            Near(ledger.Open(6UL, 7, true, new List<float> { 10f }), 75f, "few players = starter grant");

            // Faction switch freezes the wallet for 10 min.
            Eq(ledger.Faction(2UL), 7, "faction recorded");
            TestAssert.That(!ledger.SetFaction(2UL, 7, 100f), "same faction = no switch");
            TestAssert.That(ledger.SetFaction(2UL, 9, 100f), "switch detected");
            Near(ledger.FrozenRemaining(2UL, 160f), 540f, "frozen remaining");
            TestAssert.That(!ledger.TrySpend(2UL, 1f, 160f), "frozen wallet cannot spend");
            TestAssert.That(ledger.TrySpend(2UL, 1f, 700f), "thawed after 10 min");

            var full = new CreditLedger();
            for (ulong i = 1; i <= CreditLedger.MaxWallets; i++) full.Open(i, 1, false, null);
            Near(full.Open(100000UL, 1, false, null), -1f, "bounded wallets");

            var fund = new HqFundLedger();
            fund.Add(7, 75f);
            fund.Add(7, float.NaN);
            fund.Add(7, -5f);
            Near(fund.Balance(7), 75f, "HQ fund adds only sane positive amounts");
            Near(fund.Balance(8), 0f, "other faction empty");

            var round = new CreditLedger();
            round.Open(42UL, 1, false, null);
            TestAssert.That(round.TrySpend(42UL, 70f, 0f), "spend for the request");
            round.Refund(42UL, 70f);
            Near(round.Balance(42UL), 75f, "refused request costs nothing");
        }

        private static void CheckEarning()
        {
            Near(EarningRules.FromReward(EarnKind.Kill, 2f, false, false), 0f, "kills never ride the allocation reward");
            Near(EarningRules.FromKill(10f, 1f, false, false), 15f, "kill = value x 1.5");
            Near(EarningRules.FromKill(10f, 0.4f, false, false), 6f, "kill split by damage share");
            Near(EarningRules.FromKill(60f, 1f, false, false), 50f, "kill cap 50");
            Near(EarningRules.FromKill(10f, 1f, true, false), 7.5f, "repeat type halves");
            Near(EarningRules.FromKill(10f, 1f, true, true), 3.75f, "repeat and assisted quarter");
            Near(EarningRules.FromReward(EarnKind.Capture, 0.1f, false, false), 40f, "capture flat 40");
            Near(EarningRules.FromReward(EarnKind.Recon, 5f, false, false), 20f, "minor cap 20");
            Near(EarningRules.FromReward(EarnKind.Support, 0.5f, false, false), 5f, "minor scaled");
            Near(EarningRules.FromReward(EarnKind.None, 5f, false, false), 0f, "none pays nothing");
            Near(EarningRules.FromReward(EarnKind.Kill, float.NaN, false, false), 0f, "NaN pays nothing");
            Near(EarningRules.FromReward(EarnKind.Kill, -3f, false, false), 0f, "negative pays nothing");

            var trickle = new TrickleMeter();
            float got = 0f;
            for (int i = 0; i < 60; i++) got += trickle.Tick(1UL, true, 1f, i);
            Near(got, 1f, "1 CR per active minute");
            Near(trickle.Tick(1UL, false, 60f, 120f), 0f, "inactive earns nothing");
            float hour = 0f;
            for (int i = 0; i < 3600; i++) hour += trickle.Tick(2UL, true, 1f, i);
            Near(hour, 40f, "hour cap 40");
            Near(trickle.Tick(2UL, true, 60f, 3700f), 1f, "new hour bucket earns again");
            Near(trickle.Tick(3UL, true, float.NaN, 0f), 0f, "NaN dt ignored");

            var repeat = new RepeatTracker();
            TestAssert.That(!repeat.Record(1UL, "T-80", 0f), "first kill not a repeat");
            TestAssert.That(repeat.Record(1UL, "T-80", 30f), "same type within 60 s repeats");
            TestAssert.That(!repeat.Record(1UL, "T-80", 100f), "outside window not a repeat");
            TestAssert.That(!repeat.Record(1UL, "SA-6", 110f), "other type not a repeat");
            TestAssert.That(!repeat.Record(2UL, "SA-6", 111f), "per player");

            var assist = new AssistRegistry();
            assist.Record(7, 1000f, 1000f, 500f, 10f);
            TestAssert.That(assist.IsAssisted(7, 1300f, 1000f, 20f), "inside radius and window");
            TestAssert.That(!assist.IsAssisted(7, 1600f, 1000f, 20f), "outside radius");
            TestAssert.That(!assist.IsAssisted(8, 1000f, 1000f, 20f), "other faction");
            TestAssert.That(!assist.IsAssisted(7, 1000f, 1000f, 71f), "after 60 s");
            for (int i = 0; i < 100; i++) assist.Record(7, i * 10000f, 0f, 100f, 50f);
            TestAssert.That(assist.IsAssisted(7, 990000f, 0f, 60f), "newest kept when full");
        }

        private static void CheckBoundaries()
        {
            var ledger = new CreditLedger();
            ledger.Open(1, 7, false, null);
            ledger.Open(2, 9, false, null);
            foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                TestAssert.That(!ledger.TrySpend(1, bad, 0f), "invalid cost is refused");
                ledger.Credit(1, bad);
                ledger.Refund(1, bad);
            }
            Near(ledger.Balance(1), 75f, "invalid amounts never alter the wallet");
            TestAssert.That(ledger.TrySpend(1, 75f, 0f), "exact balance can be spent");
            Near(ledger.Balance(1), 0f, "exact spending reaches zero");
            Near(ledger.Balance(2), 75f, "another player's wallet is independent");
            ledger.Refund(1, 75f);
            TestAssert.That(ledger.SetFaction(1, 9, 10f) && !ledger.SetFaction(1, 9, 20f),
                "only a real faction change starts a freeze");
            Near(ledger.FrozenRemaining(1, 20f), 590f, "same-faction updates never extend the freeze");
            TestAssert.That(!ledger.TrySpend(1, 25f, 609.99f) && ledger.TrySpend(1, 25f, 610f),
                "wallet thaws exactly at the ten-minute deadline");
            var balances = new List<float> { -999f };
            ledger.FactionBalances(9, 1, balances);
            TestAssert.That(balances.Count == 1 && balances[0] == 75f,
                "join census clears its buffer and excludes the joiner's wallet");
            Near(ledger.Open(3, 9, true, new[] { 400f, 0f, 100f, 200f }), 112.5f,
                "even-sized join census uses the two middle values, including an empty wallet");
            Near(ledger.Open(4, 9, true, new[] { float.NaN, 100f, 200f, 300f, float.PositiveInfinity }), 150f,
                "non-finite census values do not poison a grant");
            ledger.Clear();
            TestAssert.That(!ledger.Has(1) && ledger.Balance(2) == 0f && ledger.FrozenRemaining(1, 20f) == 0f,
                "mission end drops balances, identities and freeze clocks");
            Near(ledger.Open(1, 7, false, null), 75f, "the next mission grants a fresh wallet");

            var fund = new HqFundLedger();
            fund.Add(7, 20f);
            fund.Add(9, 50f);
            fund.Add(7, float.PositiveInfinity);
            Near(fund.Balance(7), 20f, "HQ funds remain separate by faction");
            Near(fund.Balance(9), 50f, "another faction retains its HQ fund");
            fund.Clear();
            Near(fund.Balance(7), 0f, "mission end resets shadow HQ funds");
            Near(EarningRules.FromKill(60f, 1f, false, true), 25f,
                "assisted kill discount applies after the per-kill cap");
            Near(EarningRules.FromReward(EarnKind.Jamming, 9f, true, true), 20f,
                "kill-only discounts do not reduce minor rewards");
            Near(EarningRules.FromReward(EarnKind.Kill, float.PositiveInfinity, false, false), 0f,
                "non-finite native reward pays nothing");

            var trickle = new TrickleMeter();
            Near(trickle.Tick(1, true, 2400f, 0f), 40f, "one hour bucket cannot exceed forty CR");
            Near(trickle.Tick(1, true, 60f, 3599f), 0f, "exhausted bucket remains closed before its rim");
            Near(trickle.Tick(1, true, 60f, 3600f), 1f, "new bucket opens exactly at one hour");
            trickle.Clear();
            Near(trickle.Tick(1, true, 60f, 3601f), 1f, "mission reset clears the hour meter");
            for (ulong id = 2; id <= CreditLedger.MaxWallets; id++) trickle.Tick(id, true, 60f, 0f);
            Near(trickle.Tick(1000, true, 60f, 0f), 0f, "trickle meters respect wallet capacity");

            var repeat = new RepeatTracker();
            TestAssert.That(!repeat.Record(1, "T-80", 0f) && repeat.Record(1, "T-80", 60f),
                "same-type kills include the sixty-second boundary");
            TestAssert.That(!repeat.Record(1, "T-80", 120.01f), "repeat decay follows the most recent kill");
            repeat.Clear();
            TestAssert.That(!repeat.Record(1, "T-80", 121f), "mission end clears repeat penalties");

            var assist = new AssistRegistry();
            assist.Record(7, 0f, 0f, 500f, 10f);
            TestAssert.That(assist.IsAssisted(7, 500f, 0f, 70f), "assists include distance and time boundaries");
            TestAssert.That(!assist.IsAssisted(7, 0f, 0f, 9f), "future effects cannot assist earlier kills");
            for (int i = 0; i < AssistRegistry.Capacity; i++) assist.Record(7, 10000f + i * 1000f, 0f, 10f, 20f);
            TestAssert.That(!assist.IsAssisted(7, 0f, 0f, 30f), "a full assist ring evicts its oldest effect");
            assist.Clear();
            TestAssert.That(!assist.IsAssisted(7, 41000f, 0f, 30f), "mission end clears assist footprints");
        }

        private static void Near(float actual, float expected, string message) =>
            TestAssert.That(Math.Abs(actual - expected) < 0.01f, message + " (got " + actual + ", want " + expected + ")");

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");
    }
}
