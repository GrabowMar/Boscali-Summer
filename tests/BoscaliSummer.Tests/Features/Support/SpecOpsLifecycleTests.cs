using System;
using BoscaliSummer.Features.Support.Domain.SpecOps;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>
    /// SPEC OPS lifecycle pins: slot identity, the recall abort path, rank basis, timer semantics,
    /// single payout, and bounded collections. No behavior change rides with these tests.
    /// </summary>
    internal static class SpecOpsLifecycleTests
    {
        private const int Town = 101;
        private const int Sam = 202;

        public static void Run()
        {
            CheckSlotIdentity();
            CheckRecallWhileDisabled();
            CheckRankBasis();
            CheckTimerSemantics();
            CheckSinglePayout();
            CheckWinsCap();
            CheckRelistStability();
            CheckCoveringBestRank();
            CheckNoticeRing();
            CheckScoutRefresh();
            CheckRecallForfeitsWin();
        }

        private static SpecOpsDetachment Listed()
        {
            var detachment = new SpecOpsDetachment();
            detachment.BeginObjectives();
            detachment.ReportObjective(ObjectiveKind.Town, Town, 10000f, 0f, 5, 0, true, "KERSEY");
            detachment.ReportObjective(ObjectiveKind.AirDefence, Sam, 20000f, 5000f, 3, 2, true, "AIR DEFENCE 20/5");
            detachment.EndObjectives();
            return detachment;
        }

        private static void CheckSlotIdentity()
        {
            SpecOpsDetachment detachment = Listed();
            TestAssert.That(!detachment.TryRaise(0) && detachment.Team(0).State == TeamState.Ready,
                "raising a formed slot changes nothing");
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            TestAssert.That(detachment.TryLaunch(0, FieldMission.Recon, Sam, 0f, 1.0) == SpecOpsDenial.Busy,
                "a busy team cannot redeploy");
            TestAssert.That(detachment.CheckRecall(1) == SpecOpsDenial.NotDeployed &&
                detachment.CheckRecall(3) == SpecOpsDenial.Unformed &&
                detachment.CheckRecall(9) == SpecOpsDenial.BadTeam,
                "recall refuses ready, unformed and unknown slots");
            TestAssert.That(!detachment.TryRecall(1, 2.0) && detachment.Team(1).State == TeamState.Ready,
                "a refused recall leaves the team alone");

            // Lose the team, raise the slot again: the replacement is a stranger.
            detachment.Tick(30.0, () => 0.0, r => true);
            detachment.Tick(60.0, () => 0.999, r => true);
            TestAssert.That(detachment.Team(0).State == TeamState.Unformed, "the lost team vacates its slot");
            TestAssert.That(detachment.TryRaise(0), "a lost slot can be raised again");
            FieldTeam recruit = detachment.Team(0);
            TestAssert.That(recruit.State == TeamState.Ready && recruit.Rank == 0 && recruit.Wins == 0 &&
                recruit.Anchor == 0 && recruit.Chance == 0 && recruit.Loss == 0 &&
                recruit.Last == MissionOutcome.None && recruit.Target == "",
                "a raised replacement carries no rank, odds, target or outcome from its predecessor");
            TestAssert.That(detachment.CheckRecall(0) == SpecOpsDenial.NotDeployed,
                "no delayed order can catch the replacement");
        }

        private static void CheckRecallWhileDisabled()
        {
            // D-1: recall is the abort path, so it stays available while new orders are refused.
            SpecOpsDetachment detachment = Listed();
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            detachment.Enabled = false;
            TestAssert.That(detachment.CheckRaise(2) == SpecOpsDenial.Disabled &&
                detachment.CheckLaunch(1, FieldMission.Recon, Sam) == SpecOpsDenial.Disabled,
                "a switched-off detachment refuses raise and launch");
            TestAssert.That(detachment.CheckRecall(0) == SpecOpsDenial.None && detachment.TryRecall(0, 5.0) &&
                detachment.Team(0).State == TeamState.Recovering,
                "recall still stands a deployed team down while SPEC OPS is off");
        }

        private static void CheckRankBasis()
        {
            // D-2: the world effect lands at the rank the team fought with; the longer post is the reward.
            SpecOpsDetachment detachment = Listed();
            FieldResult second = default;
            int wins = 0;
            Func<FieldResult, bool> apply = r => { if (r.Outcome == MissionOutcome.Success) { wins++; second = r; } return true; };
            double now = 0.0;
            for (int mission = 0; mission < 2; mission++)
            {
                detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, now);
                now += FieldCatalog.TravelSeconds(0f);
                detachment.Tick(now, () => 0.0, apply);
                now += FieldCatalog.TaskSeconds(FieldMission.Recon);
                detachment.Tick(now, () => 0.0, apply);
                now += FieldCatalog.HoldSeconds(detachment.Team(0).Rank);
                detachment.Tick(now, () => 0.0, apply);
                now += FieldCatalog.RecoverSeconds;
                detachment.Tick(now, () => 0.0, apply);
            }
            FieldTeam alpha = detachment.Team(0);
            TestAssert.That(wins == 2 && alpha.Wins == 2 && alpha.Rank == 1,
                "two successes promote ALPHA to rank one");
            TestAssert.That(second.Rank == 0,
                "the second success hits the world at the pre-promotion rank");
            TestAssert.That(Math.Abs(FieldCatalog.HoldSeconds(1) - 360f) < 0.001f,
                "the longer hold is the promotion's reward");
        }

        private static void CheckTimerSemantics()
        {
            SpecOpsDetachment detachment = Listed();
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            detachment.Tick(10000.0, () => 0.0, r => true);
            FieldTeam alpha = detachment.Team(0);
            TestAssert.That(alpha.State == TeamState.OnTask &&
                Math.Abs(alpha.PhaseStart - 10000.0) < 0.001 &&
                Math.Abs(alpha.PhaseEnd - 10000.0 - FieldCatalog.TaskSeconds(FieldMission.Recon)) < 0.001,
                "one late tick starts the task clock fresh; travel overrun is discarded, phases never skip");
            TestAssert.That(Math.Abs(detachment.Remaining(0, alpha.PhaseEnd)) < 0.001 &&
                Math.Abs(detachment.Progress(0, alpha.PhaseStart)) < 0.001f &&
                Math.Abs(detachment.Progress(0, alpha.PhaseEnd) - 1f) < 0.001f,
                "clocks read zero remaining and full progress exactly at the phase end");
            detachment.Tick(20000.0, () => 0.0, r => true);
            TestAssert.That(detachment.Team(0).State == TeamState.Holding,
                "even a huge jump resolves only one phase per tick");
            detachment.Tick(30000.0, () => 0.0, r => true);
            TestAssert.That(detachment.Team(0).State == TeamState.Recovering,
                "holding ends into recovery, never straight to READY");
        }

        private static void CheckSinglePayout()
        {
            SpecOpsDetachment detachment = Listed();
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            int applied = 0;
            detachment.Tick(30.0, () => 0.0, r => { applied++; return true; });
            detachment.Tick(60.0, () => 0.0, r => { applied++; return true; });
            TestAssert.That(applied == 1 && detachment.Team(0).State == TeamState.Holding,
                "success pays the world effect once");
            detachment.Tick(100.0, () => 0.0, r => { applied++; return true; });
            TestAssert.That(applied == 1, "holding ticks never re-apply");
            TestAssert.That(detachment.TryRecall(0, 120.0) && applied == 1 &&
                detachment.Team(0).Last == MissionOutcome.Recalled && detachment.Team(0).Wins == 0,
                "recalling a held post abandons it with no win and no second payout");
            detachment.Tick(120.0 + FieldCatalog.RecoverSeconds, () => 0.0, r => { applied++; return true; });
            TestAssert.That(detachment.Team(0).State == TeamState.Ready && applied == 1,
                "recovery returns READY with the single payout intact");
        }

        private static void CheckWinsCap()
        {
            var snapshot = new SpecOpsSnapshot();
            snapshot.TeamState[0] = (byte)TeamState.OnTask;
            snapshot.TeamMission[0] = (byte)FieldMission.Recon;
            snapshot.TeamRank[0] = FieldCatalog.MaxRank;
            snapshot.TeamWins[0] = byte.MaxValue;
            snapshot.TeamChance[0] = 95;
            snapshot.TeamLoss[0] = 1;
            snapshot.TeamDuration[0] = 30f;
            snapshot.TeamRemaining[0] = 10f;
            var detachment = new SpecOpsDetachment();
            detachment.Mirror(snapshot, 500.0);
            detachment.Tick(600.0, () => 0.0, r => true);
            FieldTeam alpha = detachment.Team(0);
            TestAssert.That(alpha.State == TeamState.Holding && alpha.Wins == byte.MaxValue &&
                alpha.Rank == FieldCatalog.MaxRank, "the 256th win neither overflows nor over-promotes");
        }

        private static void CheckRelistStability()
        {
            SpecOpsDetachment detachment = Listed();
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            detachment.BeginObjectives();
            detachment.ReportObjective(ObjectiveKind.AirDefence, Sam, 20000f, 5000f, 3, 2, true, "AIR DEFENCE 20/5");
            detachment.ReportObjective(ObjectiveKind.Town, Town, 10000f, 0f, 5, 0, true, "KERSEY");
            detachment.EndObjectives();
            TestAssert.That(detachment.SlotOf(Town) == 1 && detachment.TeamOn(Town) == 0,
                "a reordered list still resolves the team by anchor, not by slot");
            TestAssert.That(detachment.CheckLaunch(1, FieldMission.Sabotage, Sam) == SpecOpsDenial.None,
                "the reorder does not disturb other launch rules");

            detachment.BeginObjectives();
            for (int i = 0; i < SpecOpsDetachment.ObjectiveSlots + 3; i++)
                detachment.ReportObjective(ObjectiveKind.Town, 1000 + i, i, 0f, 500, 300, true, "T" + i);
            detachment.EndObjectives();
            TestAssert.That(detachment.ObjectiveCount == SpecOpsDetachment.ObjectiveSlots,
                "the list keeps twelve objectives and drops the rest");
            TestAssert.That(detachment.Objective(0).Threat == 99 && detachment.Objective(0).Radars == 99,
                "threat and radar counts clamp to two digits");
        }

        private static void CheckCoveringBestRank()
        {
            var snapshot = new SpecOpsSnapshot();
            snapshot.TeamState[0] = (byte)TeamState.Holding;
            snapshot.TeamMission[0] = (byte)FieldMission.Recon;
            snapshot.TeamRank[0] = 0;
            snapshot.TeamX[0] = 10000f;
            snapshot.TeamZ[0] = 0f;
            snapshot.TeamDuration[0] = 300f;
            snapshot.TeamRemaining[0] = 300f;
            snapshot.TeamState[1] = (byte)TeamState.Holding;
            snapshot.TeamMission[1] = (byte)FieldMission.Recon;
            snapshot.TeamRank[1] = 2;
            snapshot.TeamX[1] = 10000f;
            snapshot.TeamZ[1] = 0f;
            snapshot.TeamDuration[1] = 300f;
            snapshot.TeamRemaining[1] = 300f;
            var detachment = new SpecOpsDetachment();
            detachment.Mirror(snapshot, 500.0);
            TestAssert.That(detachment.Posts(FieldMission.Recon) == 2, "two teams hold overlapping posts");
            TestAssert.That(detachment.Covering(FieldMission.Recon, 10000f, 0f) == 1,
                "overlapping posts do not stack: the higher rank answers");
            TestAssert.That(detachment.Covering(FieldMission.Recon, 16000f, 0f) == 1 &&
                detachment.Covering(FieldMission.Recon, 16010f, 0f) < 0,
                "coverage ends exactly at the post's reach");
            TestAssert.That(detachment.CoveringRank(FieldMission.Recon, 10000f, 0f) == 2 &&
                detachment.CoveringRank(FieldMission.Sabotage, 10000f, 0f) < 0,
                "the answering post reports its own rank for orders it carries");
        }

        private static void CheckNoticeRing()
        {
            SpecOpsDetachment detachment = Listed();
            detachment.TryRaise(2);
            detachment.TryRaise(3);
            int serial = detachment.NoticeSerial;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                double now = cycle * 1000.0;
                detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, now);
                detachment.TryRecall(0, now + 1.0);
                detachment.Tick(now + 1.0 + FieldCatalog.RecoverSeconds, () => 0.0, r => true);
            }
            TestAssert.That(detachment.NoticeSerial == serial + 9,
                "every order and homecoming is logged exactly once");
            TestAssert.That(detachment.NoticeCount == SpecOpsDetachment.NoticeSlots &&
                detachment.NoticeKind(0) == FieldNotice.Ready,
                "the ring keeps the eight newest, newest first");
        }

        private static void CheckScoutRefresh()
        {
            SpecOpsDetachment detachment = Listed();
            double now = 0.0;
            double first = 0.0, second = 0.0;
            for (int mission = 0; mission < 2; mission++)
            {
                detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, now);
                now += FieldCatalog.TravelSeconds(0f);
                detachment.Tick(now, () => 0.0, r => true);
                now += FieldCatalog.TaskSeconds(FieldMission.Recon);
                detachment.Tick(now, () => 0.0, r => true);
                if (mission == 0) first = now; else second = now;
                now += FieldCatalog.HoldSeconds(detachment.Team(0).Rank);
                detachment.Tick(now, () => 0.0, r => true);
                now += FieldCatalog.RecoverSeconds;
                detachment.Tick(now, () => 0.0, r => true);
            }
            TestAssert.That(Math.Abs(detachment.ScoutRemaining(Town, second) - FieldCatalog.ScoutSeconds) < 0.001,
                "a second RECON refreshes the scouting mark to the full watch");
            TestAssert.That(detachment.Scouted(Town, second + FieldCatalog.ScoutSeconds - 1.0) &&
                !detachment.Scouted(Town, second + FieldCatalog.ScoutSeconds),
                "scouting lapses exactly when the watch ends");
            TestAssert.That(first < second, "the two missions ran in order");
        }

        private static void CheckRecallForfeitsWin()
        {
            SpecOpsDetachment detachment = Listed();
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            detachment.Tick(30.0, () => 0.0, r => true);
            detachment.Tick(60.0, () => 0.0, r => true);
            TestAssert.That(detachment.Team(0).Wins == 1, "success banks the win first");
            TestAssert.That(detachment.TryRecall(0, 61.0), "the post is abandoned the moment it is won");
            FieldTeam alpha = detachment.Team(0);
            TestAssert.That(alpha.State == TeamState.Recovering && alpha.Wins == 0 && alpha.Rank == 0 &&
                detachment.Posts(FieldMission.Recon) == 0, "instant recall forfeits the win and the post");
            TestAssert.That(detachment.NoticeKind(0) == FieldNotice.Recalled &&
                detachment.NoticeKind(1) == FieldNotice.PostEnded,
                "abandoning a post logs the recall above the post's end");

            // The same mission held out keeps everything: farming buys nothing over serving.
            SpecOpsDetachment patient = Listed();
            patient.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            patient.Tick(30.0, () => 0.0, r => true);
            patient.Tick(60.0, () => 0.0, r => true);
            patient.Tick(60.0 + FieldCatalog.HoldSeconds(0), () => 0.0, r => true);
            TestAssert.That(patient.Team(0).Wins == 1 && patient.Team(0).State == TeamState.Recovering,
                "holding the full post banks the win for good");

            // Recall from travel or task never had a win to forfeit.
            SpecOpsDetachment early = Listed();
            early.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            early.TryRecall(0, 5.0);
            TestAssert.That(early.Team(0).Wins == 0 && early.Team(0).Rank == 0,
                "recalling travellers touches no wins");

            // At the cap the win stands either way: rank cannot move and no history is destroyed.
            var snapshot = new SpecOpsSnapshot();
            snapshot.TeamState[0] = (byte)TeamState.Holding;
            snapshot.TeamMission[0] = (byte)FieldMission.Recon;
            snapshot.TeamRank[0] = FieldCatalog.MaxRank;
            snapshot.TeamWins[0] = byte.MaxValue;
            snapshot.TeamDuration[0] = 300f;
            snapshot.TeamRemaining[0] = 300f;
            var capped = new SpecOpsDetachment();
            capped.Mirror(snapshot, 500.0);
            capped.TryRecall(0, 501.0);
            TestAssert.That(capped.Team(0).Wins == byte.MaxValue && capped.Team(0).Rank == FieldCatalog.MaxRank,
                "a capped legend recalled from its post stays capped");
        }
    }
}
