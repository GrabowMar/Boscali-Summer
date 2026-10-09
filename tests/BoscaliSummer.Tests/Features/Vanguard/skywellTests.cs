using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins the SKYWELL pit-stop planner, hot-break timing, staging curves and HUD pips with numbers.</summary>
    internal static class SkywellTests
    {
        private static bool Near(float a, float b, float eps = 0.01f) => Mathf.Abs(a - b) <= eps;

        private static PitStation A => new PitStation { Index = 0, Missing = 2, Full = 4, MassPerRound = 120f, CostPerRound = 0.1f };

        private static PitStation B => new PitStation { Index = 2, Missing = 1, Full = 2, MassPerRound = 120f, CostPerRound = 0.1f };

        private static List<PitJob> Plan(PitOrder order, float need, float fraction, int priority = -1, float fuelStock = 8000f,
            float munStock = 1500f, float funds = 0f, bool free = true, params PitStation[] stations)
        {
            var jobs = new List<PitJob>();
            int count = PitPlan.Plan(order, need, fraction, fuelStock, stations, priority, munStock, funds, free, jobs);
            TestAssert.That(count == jobs.Count, "Plan returns the list length");
            return jobs;
        }

        public static void Run()
        {
            // 1-2: durations
            TestAssert.That(Near(PitPlan.FuelSeconds(0f), 2.7f) && Near(PitPlan.FuelSeconds(2000f), 6.5f) &&
                Near(PitPlan.FuelSeconds(2400f), 7.5f) && Near(PitPlan.FuelSeconds(8000f), 13.5f), "fuel job: plug 0.9 + flow (clamped 1.2..12 s) + unplug 0.6");
            TestAssert.That(Near(PitPlan.HandoffSeconds(true, false), 1.7f) && Near(PitPlan.HandoffSeconds(false, false), 1.1f) &&
                Near(PitPlan.HandoffSeconds(true, true), 2.4f), "handoff 1.7 s first, 1.1 s follow-on, cassette 2.4 s");
            TestAssert.That(PitPlan.FuelFirst(0.34f) && !PitPlan.FuelFirst(0.35f), "fuel goes first below 35 %");

            // 3: both, comfortable fuel -> rounds first (emptiest tie broken by index), fuel last
            List<PitJob> both = Plan(PitOrder.Both, 2400f, 0.55f, -1, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(both.Count == 4 && both[0].Kind == PitJobKind.Handoff && both[1].Kind == PitJobKind.Handoff &&
                both[2].Kind == PitJobKind.Handoff && both[3].Kind == PitJobKind.Fuel, "both: three handoffs then fuel");
            TestAssert.That(both[0].Station == 0 && both[1].Station == 0 && both[2].Station == 2 && both[3].Station == -1, "stations 0,0,2 then fuel");
            TestAssert.That(Near(both[0].Seconds, 1.7f) && Near(both[1].Seconds, 1.1f) && Near(both[2].Seconds, 1.7f) && Near(both[3].Seconds, 7.5f) &&
                Near(PitPlan.Total(both), 12f), "job seconds 1.7 1.1 1.7 7.5, total 12.0");
            TestAssert.That(Near(PitPlan.Total(both, 2), 9.2f), "remaining time from job 2 is 9.2 s");

            // 4: priority station first
            List<PitJob> priority = Plan(PitOrder.Both, 2400f, 0.55f, 2, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(priority[0].Station == 2 && priority[1].Station == 0 && priority[2].Station == 0 &&
                Near(priority[0].Seconds, 1.7f) && Near(priority[1].Seconds, 1.7f) && Near(priority[2].Seconds, 1.1f) &&
                Near(PitPlan.Total(priority), 12f), "priority 2: stations 2,0,0 then fuel, total 12.0");

            // 5: low fuel -> fuel first
            List<PitJob> low = Plan(PitOrder.Both, 2400f, 0.20f, -1, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(low.Count == 4 && low[0].Kind == PitJobKind.Fuel && low[1].Kind == PitJobKind.Handoff && Near(PitPlan.Total(low), 12f),
                "fuel fraction 0.20: fuel first");

            // 6: single-purpose orders and fuel thresholds
            List<PitJob> fuelOnly = Plan(PitOrder.Fuel, 2400f, 0.55f, -1, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(fuelOnly.Count == 1 && fuelOnly[0].Kind == PitJobKind.Fuel, "order FUEL plans only the fuel job");
            List<PitJob> munitions = Plan(PitOrder.Munitions, 2400f, 0.55f, -1, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(munitions.Count == 3 && Near(PitPlan.Total(munitions), 4.5f), "order MUNITIONS plans three handoffs, 4.5 s");
            TestAssert.That(Plan(PitOrder.Both, 30f, 0.55f, -1, 8000f, 1500f, 0f, true, A, B).Count == 3, "a need under 50 kg plans no fuel job");
            TestAssert.That(Plan(PitOrder.Both, 2400f, 0.55f, -1, 0.5f, 1500f, 0f, true, A, B).Count == 3, "an empty fuel stock plans no fuel job");

            // 7: munitions stock limits the rounds
            List<PitJob> thin = Plan(PitOrder.Both, 2400f, 0.55f, -1, 8000f, 130f, 0f, true, A, B);
            TestAssert.That(thin.Count == 2 && thin[0].Kind == PitJobKind.Handoff && thin[0].Station == 0 && thin[1].Kind == PitJobKind.Fuel &&
                Near(PitPlan.Total(thin), 9.2f), "130 kg of rounds loads one round (A) then fuel, 9.2 s");

            // 8: players are limited by funds
            List<PitJob> poor = Plan(PitOrder.Munitions, 0f, 0.55f, -1, 8000f, 1500f, 0.15f, false, A, B);
            TestAssert.That(poor.Count == 1 && poor[0].Station == 0, "0.15 funds at 0.1 per round buys exactly one round");
            float stockLeft = 1500f, fundsLeft = 0.15f;
            TestAssert.That(PitPlan.Take(A, 2, ref stockLeft, ref fundsLeft, false) == 1 && Near(fundsLeft, 0.05f, 1e-4f) && Near(stockLeft, 1380f),
                "Take charges stock and funds for the one affordable round");

            // 9-10: cassettes, skipped and full stations
            var belt = new PitStation { Index = 1, Missing = 300, Full = 400, MassPerRound = 0.024f, Belt = true };
            List<PitJob> gun = Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 1500f, 0f, true, belt);
            TestAssert.That(gun.Count == 1 && gun[0].Kind == PitJobKind.Cassette && gun[0].Rounds == 300 && Near(gun[0].Seconds, 2.4f),
                "a belt station swaps as one 300-round cassette in 2.4 s");
            var cargo = new PitStation { Index = 3, Missing = 1, Full = 1, MassPerRound = 100f, Skip = true };
            var full = new PitStation { Index = 4, Missing = 0, Full = 2, MassPerRound = 100f };
            TestAssert.That(Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 1500f, 0f, true, cargo, full).Count == 0, "skipped and full stations are never planned");

            // 11: caps
            var many = new PitStation { Index = 0, Missing = 40, Full = 40, MassPerRound = 1f };
            TestAssert.That(Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 1500f, 0f, true, many).Count == 14, "a stop never exceeds 14 jobs");
            List<PitJob> capped = Plan(PitOrder.Both, 8000f, 0.10f, -1, 8000f, 1500f, 0f, true, many);
            TestAssert.That(capped.Count == 14 && PitPlan.Total(capped) <= 30f && capped[0].Kind == PitJobKind.Fuel, "capped plan: fuel first, 14 jobs, within 30 s");
            var heavy = new PitStation { Index = 0, Missing = 20, Full = 20, MassPerRound = 1f, Belt = false };
            List<PitJob> timed = Plan(PitOrder.Both, 8000f, 0.10f, -1, 8000f, 1500f, 0f, true, heavy, B);
            TestAssert.That(PitPlan.Total(timed) <= PitPlan.MaxSeconds + 1e-3f, "total pit time stays within 30 s");

            // 12: live-state clamp at commit
            float stock = 130f, funds = 0f;
            TestAssert.That(PitPlan.Take(A, 2, ref stock, ref funds, true) == 1 && Near(stock, 10f), "Take(2) with 130 kg returns 1 and leaves 10 kg");

            // 13: hot-break timing
            TestAssert.That(Near(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, 600f)), 5f),
                "missile 3 km out at 600 m/s impacts in 5.0 s");
            TestAssert.That(Near(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -6000f), new Vector3(0f, 0f, 600f)), 10f),
                "missile 6 km out at 600 m/s impacts in 10.0 s");
            TestAssert.That(float.IsPositiveInfinity(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, -600f))),
                "a missile flying away never impacts");
            TestAssert.That(float.IsPositiveInfinity(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, 15f))),
                "closing under 20 m/s is no threat");
            TestAssert.That(Near(PitPlan.TimeToImpact(new Vector3(0f, 0f, 100f), new Vector3(0f, 0f, 200f), new Vector3(0f, 0f, -2900f), new Vector3(0f, 0f, 500f)), 10f),
                "closing speed is relative to the host velocity (3000 m at 300 m/s)");

            // 14: staging curves
            TestAssert.That(Near(PitStaging.Reach(0f, PitFrom.SameStation), 1f) && Near(PitStaging.Reach(0.35f, PitFrom.SameStation), 0.2f) &&
                Near(PitStaging.Reach(0.45f, PitFrom.SameStation), 0.2f) && Near(PitStaging.Reach(0.95f, PitFrom.SameStation), 1f), "same-station reach dips to 0.2 and returns");
            TestAssert.That(Near(PitStaging.Reach(0.40f, PitFrom.OtherStation), 0f) && Near(PitStaging.Reach(1.15f, PitFrom.OtherStation), 1f), "other-station reach retracts then extends");
            TestAssert.That(Near(PitStaging.Reach(0f, PitFrom.Home), 0f) && Near(PitStaging.Reach(0.75f, PitFrom.Home), 1f), "home reach extends in 0.75 s");
            TestAssert.That(Near(PitStaging.RoundScale(0.30f, true), 0f) && Near(PitStaging.RoundScale(0.55f, true), 1f) && Near(PitStaging.RoundScale(0f, false), 1f),
                "cradle round regrows 0.30..0.55 s after a commit");
            TestAssert.That(Near(PitStaging.Connected(0.25f, 6f), 0f) && Near(PitStaging.Connected(0.9f, 6f), 1f) && Near(PitStaging.Connected(6.9f, 6f), 1f) &&
                Near(PitStaging.Connected(7.3f, 6f), 0f), "boom plugs by 0.9 s, holds through the flow, unplugs in 0.4 s");

            // 15: the retired helper stays pinned, the HUD pips read as pit lane progress
            TestAssert.That(SkywellContact.TransferSeconds(8000f, 0) == 20f && SkywellContact.TransferSeconds(0f, 0) == 3f, "retired TransferSeconds keeps its 3..20 clamp");
            TestAssert.That(PitCopy.Pips(2, 5) == "[##>--]" && PitCopy.Pips(0, 4) == "[>----]" && PitCopy.Pips(4, 4) == "[#####]" && PitCopy.Pips(0, 0) == "[>----]",
                "pips: [##>--] at job 3 of 5");
            TestAssert.That(PitCopy.Kg(2344f) == "2,340" && PitCopy.OrderLabel(PitOrder.Munitions) == "MUNITIONS ONLY" && PitCopy.OrderLabel(PitOrder.Both) == "BOTH",
                "HUD kilograms round to 10 with a thousands comma; order words");
        }
    }
}
