using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>
    /// The offensive planner's pure half: phase order, funding, wave pacing, the hold and
    /// refunds.
    ///
    /// <para>These are the parts a screenshot cannot check. An offensive is allowed to run
    /// itself, so the state machine has to be the thing that is right — every phase entered
    /// in order, no wave delivered twice, a hold that can be reinforced, and the escrow
    /// returned exactly once when a plan ends, whatever ended it.</para>
    /// </summary>
    internal static class OffensiveTests
    {
        private static readonly OffensiveTiming Fast =
            new OffensiveTiming(1f, 1f, 1f, 10f, 2f, 5f, 100f);

        public static void Run()
        {
            PlanGathersThenPlansThenWaits();
            TargetOnlyWhenThePlanIsReady();
            CommitAddsWavesUpToTheCap();
            WavesDrawEscrowOnce();
            TwoGroupsAtHHourKeepTheThirdWaveScheduled();
            SpentWavesHoldAndCanBeReinforced();
            TheAssaultCeilingEndsALongFight();
            ConcludeRefundsOnce();
            ConcludedPlansExpire();
            TableBoundsAndPruning();
            ReadoutWordsCarryPhaseAndOutcome();
        }

        private static OffensivePlan Mustered(float setupCost)
        {
            var plan = new OffensivePlan(1, "STEEL RAIN", setupCost);
            plan.Tick(1f, Fast);
            plan.Tick(1f, Fast);
            return plan;
        }

        private static OffensivePlan Assaulting(float setupCost)
        {
            OffensivePlan plan = Mustered(setupCost);
            plan.SetTarget("obj-north", "NORTHERN CORRIDOR", 10f, 20f, 30f, 1f);
            plan.Tick(1f, Fast);
            return plan;
        }

        private static void PlanGathersThenPlansThenWaits()
        {
            var plan = new OffensivePlan(7, "IRON HAMMER", 60f);
            TestAssert.That(plan.Id == 7 && plan.Name == "IRON HAMMER", "identity is kept");
            TestAssert.That(plan.Phase == TheaterOperationPhase.Mustering,
                "a prepared offensive starts by gathering");
            TestAssert.That(plan.WavesPlanned == 1 && plan.Budget == 60f && plan.Committed == 60f,
                "preparation funds the first wave slot and its escrow");
            TestAssert.That(plan.Countdown == -1f && plan.HoldRemaining == -1f,
                "no countdown and no hold run while gathering");
            TestAssert.That(!plan.Launched, "a plan that has not reached H-hour has not launched");

            plan.Tick(0.5f, Fast);
            TestAssert.That(plan.Phase == TheaterOperationPhase.Mustering && plan.Progress > 0.4f,
                "half the muster time is half the progress");

            plan.Tick(0.5f, Fast);
            TestAssert.That(plan.Phase == TheaterOperationPhase.Planning, "the muster ends and planning begins");

            plan.Tick(1f, Fast);
            TestAssert.That(plan.Phase == TheaterOperationPhase.AwaitingTarget,
                "the finished plan waits for the commander's target");
            TestAssert.That(plan.Progress == 1f, "a ready plan reads full");

            plan.Tick(30f, Fast);
            TestAssert.That(plan.Phase == TheaterOperationPhase.AwaitingTarget,
                "an untargeted plan waits indefinitely rather than launching itself blindly");
        }

        private static void TargetOnlyWhenThePlanIsReady()
        {
            var gathering = new OffensivePlan(1, "STEEL RAIN", 60f);
            TestAssert.That(
                !gathering.SetTarget("obj-north", "NORTHERN CORRIDOR", 1f, 2f, 3f, 5f),
                "a plan that is still gathering cannot be aimed");
            TestAssert.That(gathering.Phase == TheaterOperationPhase.Mustering,
                "a refused target leaves the phase alone");

            OffensivePlan plan = Mustered(60f);
            TestAssert.That(
                plan.SetTarget("obj-north", "NORTHERN CORRIDOR", 1f, 2f, 3f, 5f),
                "a ready plan takes a target");
            TestAssert.That(plan.Phase == TheaterOperationPhase.Launching && plan.Countdown == 5f,
                "naming the target starts the H-hour countdown");
            TestAssert.That(plan.TargetKey == "obj-north" && plan.TargetLabel == "NORTHERN CORRIDOR",
                "the target identity and copy are kept");
            TestAssert.That(
                !plan.SetTarget("obj-south", "SOUTHERN REACH", 1f, 2f, 3f, 5f),
                "a launching plan cannot be re-targeted; the offensive is already moving");

            // The commander can end the wait, which is the whole of the H-hour influence.
            TestAssert.That(plan.IsAtHHour == false, "a running countdown is not yet H-hour");
            TestAssert.That(plan.LaunchNow(), "H-hour can be brought forward");
            TestAssert.That(plan.Countdown == 0f && plan.IsAtHHour,
                "an advanced H-hour has no countdown left");
            TestAssert.That(!plan.LaunchNow(), "bringing H-hour forward twice is not a success");

            plan.Tick(0.1f, Fast);
            TestAssert.That(plan.Phase == TheaterOperationPhase.Assault,
                "the countdown runs out and the assault begins");
            TestAssert.That(plan.Launched, "reaching the assault is what makes a plan launched");
            TestAssert.That(plan.WavesLaunched == 0,
                "the domain raises the first wave; the host is the one that delivers it");

            TestAssert.That(
                !Mustered(60f).SetTarget("", "empty", 1f, 2f, 3f, 5f),
                "an empty objective identity is refused");
            TestAssert.That(
                !Mustered(60f).SetTarget("obj", "bad", float.NaN, 2f, 3f, 5f),
                "a non-finite objective position is refused");
        }

        private static void CommitAddsWavesUpToTheCap()
        {
            OffensivePlan plan = Mustered(60f);
            TestAssert.That(plan.Commit(40f), "a second wave can be committed");
            TestAssert.That(plan.WavesPlanned == 2 && plan.Budget == 100f && plan.Committed == 100f,
                "a commitment funds one wave and its escrow");

            while (plan.WavesPlanned < OffensivePlan.MaximumWaves)
                TestAssert.That(plan.Commit(40f), "waves fit under the cap");
            TestAssert.That(!plan.Commit(40f), "the wave cap refuses further commitments");
            TestAssert.That(plan.WavesPlanned == OffensivePlan.MaximumWaves,
                "the cap is exact");

            TestAssert.That(!plan.Commit(float.NaN), "a non-finite cost is refused");
            TestAssert.That(!plan.Commit(-5f), "a negative cost is refused");
        }

        private static void WavesDrawEscrowOnce()
        {
            OffensivePlan plan = Assaulting(60f);
            TestAssert.That(plan.WaveDelivered(25f), "the launch wave is delivered");
            TestAssert.That(plan.WavesLaunched == 1 && plan.Budget == 35f && plan.Spent == 25f,
                "the delivered wave draws its cost from the escrow");

            OffensiveTick settled = plan.Tick(30f, Fast);
            TestAssert.That(settled.LaunchDue == false && !settled.WaveDue,
                "a plan with every funded wave delivered raises no more");
            TestAssert.That(!plan.WaveDelivered(25f),
                "an unfunded second wave is refused rather than drawing an escrow the plan never had");

            OffensivePlan poor = Assaulting(10f);
            TestAssert.That(poor.WaveDelivered(25f), "delivery is not refused by a short escrow");
            TestAssert.That(poor.Budget == 0f && poor.Spent == 10f,
                "the escrow cannot go negative; the draw is clamped to what is left");

            // A wave that finds nothing ready is retried on the short interval, not dropped.
            OffensivePlan pending = Assaulting(60f);
            pending.Commit(40f);
            pending.WaveDelivered(25f);
            OffensiveTick first = pending.Tick(10f, Fast);
            TestAssert.That(first.WaveDue, "the second wave comes due on the wave interval");
            OffensiveTick again = pending.Tick(2f, Fast);
            TestAssert.That(again.WaveDue,
                "a pending wave is raised again after the retry interval, following a failed delivery");

            TestAssert.That(pending.WaveDelivered(70f), "the retried wave is delivered");
            TestAssert.That(pending.WavesLaunched == 2, "the delivered wave is counted once");
            OffensiveTick done = pending.Tick(30f, Fast);
            TestAssert.That(!done.WaveDue, "no waves remain after the last one");
        }

        private static void TwoGroupsAtHHourKeepTheThirdWaveScheduled()
        {
            OffensivePlan plan = Assaulting(60f);
            plan.Commit(40f);
            plan.Commit(40f);
            TestAssert.That(plan.WaveDelivered(25f) && plan.WaveDelivered(30f),
                "two distinct H-hour groups can draw the already funded escrow");
            TestAssert.That(plan.WavesLaunched == 2 && plan.Committed == 140f &&
                            plan.Spent == 55f && plan.Budget == 85f,
                "the rapid release neither duplicates a slot nor adds a pool charge");
            TestAssert.That(!plan.Tick(9f, Fast).WaveDue && plan.Tick(1f, Fast).WaveDue,
                "the remaining wave keeps the ordinary ten-second schedule");
        }

        private static void SpentWavesHoldAndCanBeReinforced()
        {
            // The whole point of the hold: a finished wave list is a pause, not an ending.
            OffensivePlan plan = Assaulting(60f);
            plan.WaveDelivered(25f);
            OffensiveTick spent = plan.Tick(1f, Fast);

            TestAssert.That(plan.Phase == TheaterOperationPhase.Holding,
                "the last funded wave puts the plan into its hold");
            TestAssert.That(!spent.AssaultExpired, "the hold has its own window, not the assault one");
            TestAssert.That(plan.HoldRemaining > 0f && plan.HoldRemaining <= 5f,
                "the hold counts down from the configured window");
            TestAssert.That(plan.AllWavesDelivered, "the plan knows its waves are all on the road");
            TestAssert.That(plan.CanCommit, "a holding push still takes another wave");
            TestAssert.That(plan.ElapsedSeconds > 0f, "a holding push reports how long it has run");

            plan.Tick(2f, Fast);
            TestAssert.That(plan.Phase == TheaterOperationPhase.Holding && plan.HoldRemaining < 5f,
                "the hold ticks down");
            OffensiveTick expired = plan.Tick(5f, Fast);
            TestAssert.That(expired.AssaultExpired, "an unclaimed hold ends the plan");

            OffensivePlan reinforced = Assaulting(60f);
            reinforced.WaveDelivered(25f);
            reinforced.Tick(1f, Fast);
            TestAssert.That(reinforced.Commit(40f), "a holding push can be funded again");
            TestAssert.That(reinforced.Phase == TheaterOperationPhase.Assault,
                "funding another wave puts the push back on the road");
            TestAssert.That(reinforced.HoldRemaining == -1f, "no hold runs once the push resumes");
            OffensiveTick resumed = reinforced.Tick(0.1f, Fast);
            TestAssert.That(resumed.WaveDue, "the funded wave is raised at once, not after an interval");
            TestAssert.That(reinforced.WaveDelivered(40f) && reinforced.WavesLaunched == 2,
                "the reinforced wave is delivered");
        }

        private static void TheAssaultCeilingEndsALongFight()
        {
            // The ceiling is a ceiling: even a push that keeps being fed cannot run forever.
            OffensivePlan plan = Assaulting(60f);
            plan.Commit(40f);
            plan.WaveDelivered(10f);

            OffensiveTick early = plan.Tick(50f, Fast);
            TestAssert.That(!early.AssaultExpired,
                "a push inside its ceiling keeps running, however many waves are still pending");

            OffensiveTick late = plan.Tick(51f, Fast);
            TestAssert.That(late.AssaultExpired, "a push past its ceiling is reported, not run on");
            TestAssert.That(plan.Phase == TheaterOperationPhase.Assault,
                "the domain raises the flag; the host is the one that concludes the plan");
            TestAssert.That(plan.ElapsedSeconds >= 100f, "the fight's duration is kept for the report");
        }

        private static void ConcludeRefundsOnce()
        {
            OffensivePlan plan = Mustered(60f);
            plan.Commit(40f);
            float refund = plan.Conclude(TheaterOperationOutcome.Stalled);

            TestAssert.That(refund == 100f, "concluding returns the whole unspent escrow");
            TestAssert.That(plan.Phase == TheaterOperationPhase.Concluded && plan.Budget == 0f,
                "a concluded plan holds no escrow");
            TestAssert.That(plan.Outcome == TheaterOperationOutcome.Stalled, "the outcome is recorded");
            TestAssert.That(plan.Conclude(TheaterOperationOutcome.ObjectiveSecured) == 0f,
                "a second conclusion refunds nothing");
            TestAssert.That(plan.Outcome == TheaterOperationOutcome.Stalled,
                "a second conclusion cannot rewrite the outcome");
            TestAssert.That(!plan.CanCommit && !plan.CanTarget && !plan.CanAbort,
                "a concluded plan offers no controls");
            TestAssert.That(plan.HoldRemaining == -1f && plan.Countdown == -1f,
                "a concluded plan runs no timers");

            var unlabelled = new OffensivePlan(2, "Y", 10f);
            TestAssert.That(unlabelled.Conclude(TheaterOperationOutcome.None) == 10f,
                "an unlabelled conclusion still refunds");
            TestAssert.That(unlabelled.Outcome == TheaterOperationOutcome.Cancelled,
                "None is read as a cancelled offensive, never as success");

            OffensivePlan spent = Assaulting(10f);
            spent.WaveDelivered(10f);
            float elapsed = spent.ElapsedSeconds;
            TestAssert.That(spent.Conclude(TheaterOperationOutcome.CommitmentSpent) == 0f,
                "a fully spent escrow refunds nothing");
            TestAssert.That(spent.Spent == 10f, "what was drawn stays reported as spent");
            TestAssert.That(spent.ElapsedSeconds == elapsed,
                "the battle report's duration survives the conclusion");
            TestAssert.That(spent.Committed - spent.Spent == 0f,
                "a spent escrow returns nothing to the pool");
        }

        private static void ConcludedPlansExpire()
        {
            var plan = new OffensivePlan(1, "X", 10f);
            plan.Conclude(TheaterOperationOutcome.Cancelled);
            TestAssert.That(!plan.ConcludedExpired(20f), "a fresh conclusion keeps reporting");
            plan.Tick(20f, Fast);
            TestAssert.That(plan.ConcludedExpired(20f), "the conclusion expires after its hold");
            TestAssert.That(plan.ConcludedExpired(5f), "an elapsed conclusion stays expired");
        }

        private static void TableBoundsAndPruning()
        {
            var table = new OffensiveTable();
            TestAssert.That(table.TryCreate("Coalition", out FactionOffensives faction),
                "a faction gets a board");
            TestAssert.That(table.TryCreate("Coalition", out FactionOffensives again) &&
                ReferenceEquals(faction, again),
                "asking twice returns the same board");

            TestAssert.That(faction.TryPrepare(60f, out OffensivePlan first), "the first offensive fits");
            TestAssert.That(faction.TryPrepare(60f, out OffensivePlan second), "the second fits");
            TestAssert.That(first.Id != second.Id, "each offensive gets its own identity");
            TestAssert.That(!faction.TryPrepare(60f, out _),
                "a third offensive is refused rather than growing the board");

            TestAssert.That(faction.Remove(first.Id), "a finished offensive frees its slot");
            TestAssert.That(faction.Count == 1 && faction[0] == second,
                "removing one shifts the rest without duplication");
            TestAssert.That(!faction.Remove(first.Id), "removing twice is not a success");

            for (int i = 1; i < OffensiveTable.MaximumFactions; i++)
            {
                TestAssert.That(table.TryCreate("faction-" + i, out _),
                    "faction " + i + " fits under the ceiling");
            }
            TestAssert.That(!table.TryCreate("faction-overflow", out _),
                "a ninth faction is refused instead of growing the table");
            TestAssert.That(!table.TryCreate("", out _), "an empty faction identity is refused");

            table.Prune("Coalition");
            TestAssert.That(table.TryGet("Coalition", out _),
                "a board that still holds an offensive is not pruned");
            faction.Remove(second.Id);
            table.Prune("Coalition");
            TestAssert.That(!table.TryGet("Coalition", out _),
                "an emptied board is pruned away");

            table.Clear();
            TestAssert.That(table.Count == 0, "scene reset drops every faction");
        }

        private static void ReadoutWordsCarryPhaseAndOutcome()
        {
            TestAssert.That(
                TheaterReadout.OffensivePhaseWord(TheaterOperationPhase.Mustering, TheaterOperationOutcome.None) == "MUSTERING",
                "the gathering phase has a word");
            TestAssert.That(
                TheaterReadout.OffensivePhaseWord(TheaterOperationPhase.AwaitingTarget, TheaterOperationOutcome.None) == "AWAITING TARGET",
                "a plan without a target says so");
            TestAssert.That(
                TheaterReadout.OffensivePhaseWord(TheaterOperationPhase.Holding, TheaterOperationOutcome.None) == "HOLDING",
                "a push whose waves are spent says it is holding");
            TestAssert.That(
                TheaterReadout.OffensivePhaseWord(TheaterOperationPhase.Concluded, TheaterOperationOutcome.ObjectiveSecured) == "SECURED",
                "a concluded plan reads its outcome");
            TestAssert.That(
                TheaterReadout.OffensivePhaseWord(TheaterOperationPhase.Concluded, TheaterOperationOutcome.CommitmentSpent) == "COMMITMENT SPENT",
                "a spent offensive says the money went out, not that it stalled");
            TestAssert.That(
                TheaterReadout.OffensivePhaseWord(TheaterOperationPhase.Concluded, TheaterOperationOutcome.None) == "CONCLUDED",
                "an unlabelled conclusion is still stated");

            TestAssert.That(
                TheaterReadout.OffensiveRail(TheaterOperationPhase.AwaitingTarget, TheaterOperationOutcome.None) == "contested",
                "waiting for the commander is not nominal");
            TestAssert.That(
                TheaterReadout.OffensiveRail(TheaterOperationPhase.Assault, TheaterOperationOutcome.None) == "ready",
                "a running assault reads as an active push");
            TestAssert.That(
                TheaterReadout.OffensiveRail(TheaterOperationPhase.Holding, TheaterOperationOutcome.None) == "cooling",
                "a hold is a pause that wants a decision, not a healthy push");
            TestAssert.That(
                TheaterReadout.OffensiveRail(TheaterOperationPhase.Concluded, TheaterOperationOutcome.ObjectiveSecured) == "ready",
                "a secured objective is the good ending");
            TestAssert.That(
                TheaterReadout.OffensiveRail(TheaterOperationPhase.Concluded, TheaterOperationOutcome.Stalled) == "danger",
                "a stalled offensive is not dressed as success");
            TestAssert.That(
                TheaterReadout.OffensiveRail(TheaterOperationPhase.Concluded, TheaterOperationOutcome.ObjectiveLost) == "danger",
                "a closed objective is not dressed as success");
            TestAssert.That(
                TheaterReadout.OffensiveRail(TheaterOperationPhase.Concluded, TheaterOperationOutcome.Cancelled) == "locked",
                "a cancelled offensive is inert, not a warning");

            // The timeline the board draws must not run off the end of its own captions.
            TestAssert.That(TheaterReadout.OffensiveStages.Length == 5,
                "the staff's timeline has five stages");
            TestAssert.That(TheaterReadout.OffensiveStage(TheaterOperationPhase.Mustering) == 0,
                "gathering is the first stage");
            TestAssert.That(
                TheaterReadout.OffensiveStage(TheaterOperationPhase.AwaitingTarget) == 2,
                "the target decision is the middle stage");
            foreach (TheaterOperationPhase phase in new[]
                     {
                         TheaterOperationPhase.Mustering, TheaterOperationPhase.Planning,
                         TheaterOperationPhase.AwaitingTarget, TheaterOperationPhase.Launching,
                         TheaterOperationPhase.Assault, TheaterOperationPhase.Holding,
                         TheaterOperationPhase.Concluded,
                     })
            {
                int stage = TheaterReadout.OffensiveStage(phase);
                TestAssert.That(stage >= 0 && stage < TheaterReadout.OffensiveStages.Length,
                    phase + " stays inside the timeline");
            }

            TestAssert.That(TheaterReadout.Clock(252f) == "4:12", "a fight's duration reads as clock time");
            TestAssert.That(TheaterReadout.Clock(-5f) == "0:00", "a negative duration never reads backwards");
            TestAssert.That(TheaterReadout.Clock(float.NaN) == "—",
                "an unknown duration is a dash, not a confident zero");

            TestAssert.That(OffensiveNames.At(8) == OffensiveNames.At(0),
                "the name pool wraps");
            TestAssert.That(OffensiveNames.At(-1) == OffensiveNames.At(7),
                "a negative name index wraps instead of throwing");
        }
    }
}
