using System;
using BoscaliSummer.Features.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    internal static class OperationDirectorTests
    {
        public static void Run()
        {
            ChainMapIsExplicitAndExhaustedKindsStop();
            ChainDepthCeilingStopsPingPong();
            UnsetThresholdsMeanConventionalTempo();
            StageBoundariesFollowTheMissionsOwnThresholds();
            TempoClampsNonsenseInputs();
            AbortPenaltyOnlyForAcceptedContracts();
        }

        private static void ChainMapIsExplicitAndExhaustedKindsStop()
        {
            TestAssert.That(OperationChains.FollowOn(OperationKind.Capture) == OperationKind.Defend,
                "Capture chains into Defend");
            foreach (OperationKind found in new[] { OperationKind.Recon, OperationKind.SortieReport })
                TestAssert.That(OperationChains.FollowOn(found) == OperationKind.Interdict ||
                    OperationChains.FollowOn(found) == OperationKind.DamageAssessment,
                    "Reconnaissance must chain into a strike or its confirmation: " + found);
            TestAssert.That(OperationChains.FollowOn(OperationKind.DamageAssessment) == OperationKind.Interdict,
                "Strike confirmation chains into Interdict");
            TestAssert.That(OperationChains.FollowOn(OperationKind.SupplyEscort) == OperationKind.SupplyInterdict,
                "SupplyEscort chains into SupplyInterdict");
            foreach (OperationKind found in new[] { OperationKind.Jam, OperationKind.ElectronicWarfare })
                TestAssert.That(OperationChains.FollowOn(found) == OperationKind.Intercept,
                    "Jamming chains into Intercept: " + found);
            foreach (OperationKind exhausted in new[] { OperationKind.Rescue, OperationKind.BattlefieldSurvey })
                TestAssert.That(OperationChains.FollowOn(exhausted) == null,
                    "Exhausted families leave no follow-on: " + exhausted);
            foreach (OperationKind kind in (OperationKind[])Enum.GetValues(typeof(OperationKind)))
            {
                OperationKind? next = OperationChains.FollowOn(kind);
                TestAssert.That(next != kind, "A completed contract can never chain into itself: " + kind);
            }
        }

        private static void ChainDepthCeilingStopsPingPong()
        {
            OperationKind[] chain = { OperationKind.Capture, OperationKind.Defend };
            int depth = 0;
            for (int links = 0; links < chain.Length; links++)
            {
                if (!OperationChains.CanSeed(chain[links], depth)) break;
                depth++;
            }
            TestAssert.That(depth == 1, "A chain must stop at its first unchainable link");
            for (int exhausted = OperationChains.MaximumDepth; exhausted <= OperationChains.MaximumDepth + 3; exhausted++)
            {
                TestAssert.That(!OperationChains.CanSeed(OperationKind.Capture, exhausted),
                    "A chain past its depth ceiling cannot seed another link");
                TestAssert.That(!OperationChains.CanSeed(OperationKind.Recon, exhausted),
                    "A chain past its depth ceiling cannot seed another link");
            }
            TestAssert.That(OperationChains.CanSeed(OperationKind.Capture, OperationChains.MaximumDepth - 1),
                "A chain below its depth ceiling may still seed one link");

            var clamped = new Operation(1, 1, OperationKind.Capture, OperationReward.None, 0f, 1, 1,
                OperationChains.MaximumDepth + 7);
            TestAssert.That(clamped.ChainDepth == OperationChains.MaximumDepth,
                "Operation depth is clamped to the chain ceiling");
        }

        private static void UnsetThresholdsMeanConventionalTempo()
        {
            TestAssert.That(OperationTempo.Stage(0f, 0f, 0f) == OperationTempoStage.Strategic,
                "Neither threshold set means the mission never gated a combat stage");
            foreach (float current in new[] { -5f, 0f, 1f, 9999f })
            {
                TestAssert.That(OperationTempo.RewardScale(current, 0f, 0f) == OperationTempo.StrategicReward,
                    "An ungated mission is never demoted by a quantity its author passed");
            }
            TestAssert.That(OperationTempo.Interval(75f, 0f, 0f) == OperationTempo.StrategicInterval,
                "An ungated mission still uses the fastest declared interval");
        }

        private static void StageBoundariesFollowTheMissionsOwnThresholds()
        {
            TestAssert.That(OperationTempo.Stage(9f, 10f, 50f) == OperationTempoStage.Conventional,
                "Below the tactical threshold is conventional");
            TestAssert.That(OperationTempo.Stage(10f, 10f, 50f) == OperationTempoStage.Tactical,
                "Reaching the tactical threshold enters tactical");
            TestAssert.That(OperationTempo.Stage(49.9f, 10f, 50f) == OperationTempoStage.Tactical,
                "Just below the strategic threshold is still tactical");
            TestAssert.That(OperationTempo.Stage(50f, 10f, 50f) == OperationTempoStage.Strategic,
                "Reaching the strategic threshold enters strategic");
            TestAssert.That(OperationTempo.Stage(-3f, 10f, 0f) == OperationTempoStage.Conventional,
                "Tactical-only missions start conventional");
            TestAssert.That(OperationTempo.Stage(12f, 10f, 0f) == OperationTempoStage.Tactical,
                "Tactical-only missions never report strategic");
            TestAssert.That(OperationTempo.Interval(0f, 10f, 50f) == OperationTempo.ConventionalInterval &&
                OperationTempo.Interval(10f, 10f, 50f) == OperationTempo.TacticalInterval &&
                OperationTempo.Interval(50f, 10f, 50f) == OperationTempo.StrategicInterval,
                "Each stage uses its own declared interval");
            TestAssert.That(OperationTempo.RewardScale(50f, 10f, 50f) == OperationTempo.StrategicReward &&
                OperationTempo.RewardScale(50f, 10f, 50f) > OperationTempo.RewardScale(10f, 10f, 50f) &&
                OperationTempo.RewardScale(10f, 10f, 50f) > OperationTempo.RewardScale(0f, 10f, 50f),
                "Reward scale rises with the stage and stays hard-bounded");
        }

        private static void TempoClampsNonsenseInputs()
        {
            foreach (float current in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                TestAssert.That(OperationTempo.Stage(current, 10f, 50f) == OperationTempoStage.Conventional,
                    "Nonfinite escalation falls back to conventional");
            }
            foreach (float threshold in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                TestAssert.That(OperationTempo.Stage(5f, threshold, 50f) == OperationTempoStage.Conventional &&
                    OperationTempo.Stage(5f, 10f, threshold) == OperationTempoStage.Conventional,
                    "Nonfinite thresholds fall back to conventional");
            }
            TestAssert.That(OperationTempo.RewardScale(float.NaN, float.NaN, float.NaN) == OperationTempo.ConventionalReward &&
                OperationTempo.Scale(float.NaN) == 1f, "Nonfinite tempo values degrade to the neutral multiplier");
            foreach (OperationTempoStage stage in (OperationTempoStage[])Enum.GetValues(typeof(OperationTempoStage)))
            {
                float interval = OperationTempo.IntervalFor(stage);
                float reward = OperationTempo.RewardFor(stage);
                TestAssert.That(interval >= OperationTempo.MinimumInterval && interval <= OperationTempo.MaximumInterval,
                    "Stage interval stays inside its clamp: " + stage);
                TestAssert.That(reward >= OperationTempo.MinimumReward && reward <= OperationTempo.MaximumReward,
                    "Stage reward stays inside its clamp: " + stage);
            }
            TestAssert.That(OperationTempo.Scale(-1000f) == OperationTempo.MinimumReward &&
                OperationTempo.Scale(1000f) == OperationTempo.MaximumReward,
                "Absurd multipliers are clamped, never trusted");
            foreach (float escalation in new[] { -1000f, 0f, 1000f, float.MaxValue })
            {
                float scale = OperationTempo.Scale(OperationTempo.RewardScale(escalation, 10f, 50f));
                TestAssert.That(OperationTempo.Finite(scale) &&
                    scale >= OperationTempo.MinimumReward && scale <= OperationTempo.MaximumReward,
                    "Every escalation input yields a finite bounded scale");
            }
        }

        private static void AbortPenaltyOnlyForAcceptedContracts()
        {
            TestAssert.That(OperationFailure.DeliberateAbort(OperationState.Active),
                "Aborting an accepted contract costs morale");
            TestAssert.That(!OperationFailure.DeliberateAbort(OperationState.Offered) &&
                !OperationFailure.DeliberateAbort(OperationState.Expired) &&
                !OperationFailure.DeliberateAbort(OperationState.Completed) &&
                !OperationFailure.DeliberateAbort(OperationState.Cancelled),
                "Only a deliberate abort of an Active contract is penalised");
            TestAssert.That(!OperationFailure.DeliberateAbort(null), "A missing contract is not a deliberate abort");

            var offered = new Operation(1, 1, OperationKind.Capture, OperationReward.None, 0f, 1, 1);
            offered.Cancel(1f);
            TestAssert.That(offered.State == OperationState.Cancelled && !OperationFailure.DeliberateAbort(offered),
                "Dismissing an offer releases it without a penalty");
            var accepted = new Operation(2, 2, OperationKind.Capture, OperationReward.None, 0f, 1, 1);
            TestAssert.That(accepted.Accept(1f) && OperationFailure.DeliberateAbort(accepted),
                "A contract the faction accepted is punished when aborted");
            accepted.Observe(accepted.Deadline, 1f, true, true, true);
            TestAssert.That(accepted.State == OperationState.Expired && !OperationFailure.DeliberateAbort(accepted),
                "Expiry never counts as a deliberate abort");

            TestAssert.That(OperationFailure.AbortMoralePenalty < 0f, "The abort penalty is a morale loss");
            TestAssert.That(OperationFailure.DismissalMessage(true).IndexOf("morale", StringComparison.OrdinalIgnoreCase) >= 0 &&
                OperationFailure.DismissalMessage(false).IndexOf("morale", StringComparison.OrdinalIgnoreCase) < 0,
                "Each dismissal message states exactly whether morale was lost");
            TestAssert.That(OperationFailure.DismissalMessage(false).IndexOf("No reward", StringComparison.OrdinalIgnoreCase) >= 0,
                "A penalty-free dismissal still says so");
        }
    }
}
