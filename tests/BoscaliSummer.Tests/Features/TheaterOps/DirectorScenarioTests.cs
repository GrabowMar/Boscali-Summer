using System;
using System.Collections.Generic;
using BoscaliSummer.Features.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>
    /// A ten-review war played through the same bookkeeping the director service keeps:
    /// commitment, hysteresis, running targets and effort. The unit tests pin each rule;
    /// this pins that the rules together read like a staff instead of a slot machine.
    /// </summary>
    internal static class DirectorScenarioTests
    {
        private const float Overhead = 2f;
        private const float Wave = 3f;

        private sealed class Plan
        {
            public string Target;
            public int ReviewsOld;
            public bool Launched => ReviewsOld >= 2;
        }

        public static void Run()
        {
            var influence = new InfluenceState();
            var plans = new List<Plan>();
            string committed = null;
            int held = 0;
            string effort = null;
            float funds = 50f;

            for (int review = 1; review <= 10; review++)
            {
                var objectives = new List<ObjectiveRead>
                {
                    new ObjectiveRead("outpost", "Outpost", 2, 0),
                    new ObjectiveRead("stronghold", "Stronghold", 12, 0),
                    new ObjectiveRead("home", "Home", 0, 6),
                };
                if (review == 3) influence.SetStance(0.2f, "VIPER-1");
                if (review >= 4 && review <= 6)
                    objectives[2] = new ObjectiveRead("home", "Home", 8, 6);
                if (review == 6) influence.SetHold(true, "VIPER-1");
                if (review == 7) influence.SetHold(false, "VIPER-1");
                if (review == 8 && plans.Count > 0) plans.RemoveAt(0); // oldest push reported
                if (review >= 8) funds = 4f;

                var running = new List<string>();
                string launchedTarget = null;
                foreach (Plan plan in plans)
                {
                    plan.ReviewsOld++;
                    running.Add(plan.Target);
                    if (plan.Launched) launchedTarget = plan.Target;
                }

                var assessment = new DirectorAssessment(
                    objectives, influence, funds, Overhead, Wave, plans.Count, 2,
                    committed, held, launchedTarget, effort, running);
                DirectorOrders orders = DirectorDecision.Review(assessment);

                if (review == 1)
                {
                    TestAssert.That(orders.OpenOffensive && orders.TargetKey == "outpost",
                        "R1 opens at the weakest objective, not the stronghold");
                    TestAssert.That(orders.Waves == 1, "R1 sizes one wave against two defenders");
                }
                if (review == 2)
                    TestAssert.That(!orders.OpenOffensive || orders.TargetKey != "outpost",
                        "R2 never double-stacks the running push");
                if (review == 4)
                    TestAssert.That(orders.DefenseKey == "home", "R4 names the raid a defense");
                if (review == 5)
                    TestAssert.That(orders.EffortKey == "home" && orders.EffortIsDefense,
                        "R5 threat owns the effort at defensive stance");
                if (review == 6)
                    TestAssert.That(!orders.OpenOffensive && Joined(orders).Contains("STANDING ORDER"),
                        "R6 hold stops openings and says so");
                if (review == 7)
                    TestAssert.That(!orders.OpenOffensive && orders.DefenseKey == null,
                        "R7 raid gone: no defense lingers and the full staff holds");
                if (review == 8)
                    TestAssert.That(Joined(orders).Contains("WAR CHEST"), "R8 drained pool names the chest");

                if (orders.OpenOffensive)
                {
                    plans.Add(new Plan { Target = orders.TargetKey });
                    funds -= Overhead + Wave;
                }
                if (!string.IsNullOrEmpty(orders.PreferredKey))
                {
                    if (!string.Equals(committed, orders.PreferredKey, StringComparison.Ordinal))
                    {
                        committed = orders.PreferredKey;
                        held = 0;
                    }
                    else held++;
                }
                else
                {
                    committed = null;
                    held = 0;
                }
                if (orders.EffortKey != null) effort = orders.EffortKey;
                else if (orders.ClearEffort) effort = null;
                if (plans.Count > 2) plans.RemoveAt(0);
            }
        }

        private static string Joined(DirectorOrders orders) => string.Join(" ", orders.Log);
    }
}
