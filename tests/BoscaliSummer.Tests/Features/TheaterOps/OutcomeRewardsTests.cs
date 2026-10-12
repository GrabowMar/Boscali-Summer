using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>Only a SECURED operation for a faction with a human pays, and it pays funds and morale, never score (D5).</summary>
    internal static class OutcomeRewardsTests
    {
        public static void Run()
        {
            SecuredPaysTheTable();
            EveryOtherOutcomePaysNothing();
            AFactionWithoutAHumanIsPaidNothing();
            ABadKindPaysNothing();
        }

        private static void Pays(TheaterOpKind kind, int funds, int morale)
        {
            Reward r = OutcomeRewards.For(kind, TheaterOutcome.Secured, true);
            TestAssert.That(r.Funds == funds && r.Morale == morale, kind + " secured pays " + funds + "/" + morale + " (got " + r.Funds + "/" + r.Morale + ")");
        }

        private static void SecuredPaysTheTable()
        {
            Pays(TheaterOpKind.Assault, 150, 4);
            Pays(TheaterOpKind.Defend, 100, 3);
            Pays(TheaterOpKind.Recon, 40, 1);
        }

        private static void EveryOtherOutcomePaysNothing()
        {
            foreach (TheaterOpKind kind in Enum.GetValues(typeof(TheaterOpKind)))
                foreach (TheaterOutcome outcome in Enum.GetValues(typeof(TheaterOutcome)))
                {
                    if (outcome == TheaterOutcome.Secured) continue;
                    Reward r = OutcomeRewards.For(kind, outcome, true);
                    TestAssert.That(r.Funds == 0 && r.Morale == 0, kind + " " + outcome + " costs and pays nothing");
                }
        }

        private static void AFactionWithoutAHumanIsPaidNothing()
        {
            foreach (TheaterOpKind kind in Enum.GetValues(typeof(TheaterOpKind)))
            {
                Reward r = OutcomeRewards.For(kind, TheaterOutcome.Secured, false);
                TestAssert.That(r.Funds == 0 && r.Morale == 0, kind + " secured by an AI-only faction pays nothing");
            }
        }

        private static void ABadKindPaysNothing()
        {
            Reward r = OutcomeRewards.For((TheaterOpKind)9, TheaterOutcome.Secured, true);
            TestAssert.That(r.Funds == 0 && r.Morale == 0, "an unknown kind pays nothing");
        }
    }
}
