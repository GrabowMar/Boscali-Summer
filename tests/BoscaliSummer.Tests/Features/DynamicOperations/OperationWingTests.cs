using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.DynamicOperations.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    /// <summary>Annex 04 W4a: which wing task a contract kind allows, and the reason when none does (D6: explicit press only, Recon kinds still need the pilot).</summary>
    internal static class OperationWingTests
    {
        private const WingSortieIntent D = WingSortieIntent.Destroy, C = WingSortieIntent.Cover, O = WingSortieIntent.Observe, N = WingSortieIntent.None;

        // The annex W4a table, written out independently of the rule so a drift in either one fails.
        private static readonly Dictionary<OperationKind, WingSortieIntent> Table = new Dictionary<OperationKind, WingSortieIntent>
        {
            [OperationKind.Interdict] = D,
            [OperationKind.Intercept] = D,
            [OperationKind.SupplyInterdict] = D,
            [OperationKind.ElectronicWarfare] = D,
            [OperationKind.StrikePost] = D,
            [OperationKind.Capture] = C,
            [OperationKind.Defend] = C,
            [OperationKind.Patrol] = C,
            [OperationKind.SupplyEscort] = C,
            [OperationKind.RepairCover] = C,
            [OperationKind.Recon] = O,
            [OperationKind.DamageAssessment] = O,
            [OperationKind.SortieReport] = O,
            [OperationKind.BattlefieldSurvey] = O,
            [OperationKind.Jam] = N,
            [OperationKind.Rappel] = N,
            [OperationKind.Rooftop] = N,
            [OperationKind.Rescue] = N,
        };

        public static void Run()
        {
            EveryKindHasAnExplicitRow();
            IntentFollowsTheTable();
            DestroyIsExactlyTheStrikeKinds();
            HoldKindsAreCoverAndJamIsNeverDestroy();
            EveryNoneKindExplainsItselfAndTheOthersDoNot();
            AnUnmappedKindIsNoTaskAndTheSentenceMatchesTheWingRules();
        }

        private static void EveryKindHasAnExplicitRow()
        {
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
                TestAssert.That(Table.ContainsKey(kind), "Add " + kind + " to OperationWing.IntentFor and to the wing intent table");
        }

        private static void IntentFollowsTheTable()
        {
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
                TestAssert.That(OperationWing.IntentFor(kind, out _) == Table[kind], kind + " maps to " + Table[kind]);
        }

        private static void DestroyIsExactlyTheStrikeKinds()
        {
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
            {
                var op = new Operation(1, 1, kind, OperationReward.None, 0f, 100, 10);
                TestAssert.That((OperationWing.IntentFor(kind, out _) == D) == op.IsStrike, kind + ": Destroy exactly when the card is a strike");
            }
        }

        private static void HoldKindsAreCoverAndJamIsNeverDestroy()
        {
            foreach (OperationKind kind in new[] { OperationKind.SupplyEscort, OperationKind.RepairCover, OperationKind.Capture, OperationKind.Defend, OperationKind.Patrol })
                TestAssert.That(OperationWing.IntentFor(kind, out _) == C, kind + " is a hold kind: the wing flies cover");
            TestAssert.That(OperationWing.IntentFor(OperationKind.Jam, out _) != D, "a wing cannot hold a jammer on an emitter, and an emitter dying would cancel the card unpaid");
            foreach (OperationKind kind in new[] { OperationKind.Recon, OperationKind.DamageAssessment, OperationKind.SortieReport, OperationKind.BattlefieldSurvey })
                TestAssert.That(OperationWing.IntentFor(kind, out _) == O, kind + " is reconnaissance: the wing only scouts");
        }

        private static void EveryNoneKindExplainsItselfAndTheOthersDoNot()
        {
            var reasons = new HashSet<string>();
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
            {
                WingSortieIntent intent = OperationWing.IntentFor(kind, out string why);
                if (intent == N)
                {
                    TestAssert.That(!string.IsNullOrEmpty(why) && why.Length <= 100, kind + " has a reason of at most 100 characters");
                    reasons.Add(why);
                }
                else TestAssert.That(why == "", kind + " has nothing to explain");
            }
            TestAssert.That(reasons.Count >= 3, "Jam, the insertion kinds and Rescue each explain their own case");
            TestAssert.That(OperationWing.IntentFor(OperationKind.Rappel, out string rappel) == N && OperationWing.IntentFor(OperationKind.Rooftop, out string rooftop) == N && rappel == rooftop,
                "Rappel and Rooftop share one reason");
        }

        private static void AnUnmappedKindIsNoTaskAndTheSentenceMatchesTheWingRules()
        {
            WingSortieIntent intent = OperationWing.IntentFor((OperationKind)200, out string why);
            TestAssert.That(intent == N && why == OperationWing.NoTask, "a kind with no row is refused with the generic sentence");
            TestAssert.That(OperationWing.NoTask == WingSortieRules.NoTask, "the generic sentence is the wing rules' sentence (the module boundary forces a copy, this pins it)");
        }
    }
}
