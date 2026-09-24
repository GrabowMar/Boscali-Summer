using System;
using BoscaliSummer.Features.Support.Domain.SpecOps;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>The SPEC OPS detachment: teams, odds, the single roll, posts, abilities and the wire.</summary>
    internal static class SpecOpsDetachmentTests
    {
        private const int Town = 101;
        private const int Sam = 202;
        private const int Field = 303;

        public static void Run()
        {
            CheckFreshDetachment();
            CheckOdds();
            CheckLaunchRules();
            CheckMissionCycle();
            CheckFailureAndLoss();
            CheckRanksAndReadiness();
            CheckPostsAndAbilities();
            CheckStealAndNewAbilities();
            CheckSnapshots();
            CheckWords();
        }

        private static SpecOpsDetachment Listed()
        {
            var detachment = new SpecOpsDetachment();
            detachment.BeginObjectives();
            detachment.ReportObjective(ObjectiveKind.Town, Town, 10000f, 0f, 5, 0, true, "KERSEY");
            detachment.ReportObjective(ObjectiveKind.AirDefence, Sam, 20000f, 5000f, 3, 2, true, "AIR DEFENCE 20/5");
            detachment.ReportObjective(ObjectiveKind.Airfield, Field, -8000f, 4000f, 0, 1, false, "NORTH FIELD WITH A VERY LONG NAME");
            detachment.EndObjectives();
            return detachment;
        }

        private static void CheckFreshDetachment()
        {
            var detachment = new SpecOpsDetachment();
            TestAssert.That(detachment.Formed == SpecOpsDetachment.StartingTeams &&
                detachment.Team(0).State == TeamState.Ready && detachment.Team(1).State == TeamState.Ready &&
                detachment.Team(2).State == TeamState.Unformed && detachment.Team(3).State == TeamState.Unformed,
                "a fresh detachment fields ALPHA and BRAVO; CHARLIE and DELTA wait to be raised");
            TestAssert.That(detachment.BestRank == 0 && detachment.GroundReadiness == 1,
                "recruits give the untrained ground readiness of one");
            TestAssert.That(detachment.CheckRaise(0) == SpecOpsDenial.AlreadyFormed &&
                detachment.CheckRaise(2) == SpecOpsDenial.None && detachment.CheckRaise(9) == SpecOpsDenial.BadTeam,
                "only an empty slot can be raised");
            TestAssert.That(detachment.TryRaise(2) && detachment.Team(2).State == TeamState.Ready &&
                detachment.Formed == 3 && !detachment.TryRaise(2), "raising forms a READY recruit once");
            TestAssert.That(detachment.NoticeKind(0) == FieldNotice.Raised && detachment.NoticeTeam(0) == 2,
                "raising is logged");
            detachment.Enabled = false;
            TestAssert.That(detachment.CheckRaise(3) == SpecOpsDenial.Disabled &&
                detachment.CheckLaunch(0, FieldMission.Recon, Town) == SpecOpsDenial.Disabled,
                "a host with SPEC OPS off refuses every order");
            detachment.Clear();
            TestAssert.That(detachment.Enabled && detachment.Formed == 2 && detachment.NoticeCount == 0,
                "scene reset returns a fresh detachment");
        }

        private static void CheckOdds()
        {
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Recon, 0, 0, false) == 85 &&
                FieldCatalog.SuccessChance(FieldMission.Sabotage, 0, 0, false) == 70 &&
                FieldCatalog.SuccessChance(FieldMission.Seize, 0, 0, false) == 65, "base odds per mission");
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Seize, 1, 5, true) == 68,
                "rank and scouting add, threat subtracts: 65 + 8 + 10 - 15");
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Seize, 0, 99, false) == 25 &&
                FieldCatalog.SuccessChance(FieldMission.Recon, 3, 0, true) == 95,
                "threat caps at -40 and odds clamp to 95");
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Seize, -3, -5, false) == 65,
                "garbage rank and threat clamp instead of moving the odds");
            int success = FieldCatalog.SuccessChance(FieldMission.Seize, 0, 5, false);
            int loss = FieldCatalog.LossChance(FieldMission.Seize, 0, 5, success);
            TestAssert.That(success == 50 && loss == 20, "a defended town: 50% success, 20% loss");
            TestAssert.That(FieldCatalog.LossChance(FieldMission.Recon, 3, 0, 95) == 1 &&
                FieldCatalog.LossChance(FieldMission.Seize, 0, 40, 60) == 40 &&
                FieldCatalog.LossChance(FieldMission.Seize, 0, 40, 90) == 10,
                "loss has a floor, a ceiling, and never exceeds the failure share");

            TestAssert.That(FieldCatalog.Resolve(60, 10, 0.0) == MissionOutcome.Success &&
                FieldCatalog.Resolve(60, 10, 0.599) == MissionOutcome.Success &&
                FieldCatalog.Resolve(60, 10, 0.6) == MissionOutcome.Failed &&
                FieldCatalog.Resolve(60, 10, 0.899) == MissionOutcome.Failed &&
                FieldCatalog.Resolve(60, 10, 0.9) == MissionOutcome.Lost &&
                FieldCatalog.Resolve(60, 10, 5.0) == MissionOutcome.Lost &&
                FieldCatalog.Resolve(60, 10, double.NaN) == MissionOutcome.Success,
                "one roll: the low share succeeds, the top share loses the team, the rest fails");
            TestAssert.That(FieldCatalog.Resolve(95, 0, 0.99) == MissionOutcome.Failed,
                "zero loss never loses the team");

            TestAssert.That(FieldCatalog.TravelSeconds(0f) == 30f && FieldCatalog.TravelSeconds(20000f) == 60f &&
                FieldCatalog.TravelSeconds(500000f) == 120f && FieldCatalog.TravelSeconds(-1f) == 60f &&
                FieldCatalog.TravelSeconds(float.NaN) == 60f, "travel: 20 s + 2 s/km inside 30..120 s");
            TestAssert.That(FieldCatalog.RankFor(0) == 0 && FieldCatalog.RankFor(1) == 0 && FieldCatalog.RankFor(2) == 1 &&
                FieldCatalog.RankFor(6) == 3 && FieldCatalog.RankFor(200) == 3, "two wins per rank, ELITE caps");
            TestAssert.That(FieldCatalog.WinsToNext(0) == 2 && FieldCatalog.WinsToNext(3) == 1 && FieldCatalog.WinsToNext(9) == 0,
                "the roster can say how far the next rank is");
        }

        private static void CheckLaunchRules()
        {
            SpecOpsDetachment detachment = Listed();
            TestAssert.That(detachment.ObjectiveCount == 3 && detachment.Objective(2).Name.Length == SpecOpsDetachment.NameLength,
                "objectives list in order and names clip to the wire length");
            TestAssert.That(detachment.CheckLaunch(2, FieldMission.Recon, Town) == SpecOpsDenial.Unformed &&
                detachment.CheckLaunch(0, (FieldMission)9, Town) == SpecOpsDenial.BadMission &&
                detachment.CheckLaunch(0, FieldMission.Recon, 999) == SpecOpsDenial.StaleObjective &&
                detachment.CheckLaunch(0, FieldMission.Seize, Sam) == SpecOpsDenial.WrongObjective &&
                detachment.CheckLaunch(0, FieldMission.Sabotage, Town) == SpecOpsDenial.NoRadars,
                "launch refuses empty slots, unknown missions, unlisted anchors, SEIZE on a SAM site, SABOTAGE without radars");
            detachment.SeizeAvailable = false;
            TestAssert.That(detachment.CheckLaunch(0, FieldMission.Seize, Town) == SpecOpsDenial.SeizeUnavailable,
                "SEIZE needs Urban Combat garrisons");
            detachment.SeizeAvailable = true;

            int serial = detachment.NoticeSerial;
            TestAssert.That(detachment.TryLaunch(0, FieldMission.Seize, Town, 10000f, 100.0) == SpecOpsDenial.None,
                "a READY team launches onto a listed town");
            FieldTeam alpha = detachment.Team(0);
            TestAssert.That(alpha.State == TeamState.EnRoute && alpha.Anchor == Town && alpha.Target == "KERSEY" &&
                alpha.Chance == 50 && alpha.Loss == 20 && Math.Abs(alpha.PhaseEnd - 140.0) < 0.001,
                "the launch fixes odds, target and a 40 s trip for 10 km");
            TestAssert.That(detachment.NoticeSerial == serial + 1 && detachment.NoticeKind(0) == FieldNotice.Launched,
                "the launch is logged");
            TestAssert.That(detachment.CheckLaunch(0, FieldMission.Recon, Sam) == SpecOpsDenial.Busy &&
                detachment.CheckLaunch(1, FieldMission.Recon, Town) == SpecOpsDenial.ObjectiveTaken,
                "a deployed team cannot relaunch and one objective takes one team");
            TestAssert.That(detachment.TeamOn(Town) == 0 && detachment.TeamOn(Sam) == -1, "the objective knows its team");

            // A relisting drops the objective; the team keeps its own target and position.
            detachment.BeginObjectives();
            detachment.ReportObjective(ObjectiveKind.AirDefence, Sam, 20000f, 5000f, 3, 2, true, "AIR DEFENCE 20/5");
            detachment.ReportObjective(ObjectiveKind.AirDefence, Sam, 1f, 1f, 3, 2, true, "DUPLICATE");
            detachment.ReportObjective(ObjectiveKind.None, 7, 1f, 1f, 0, 0, true, "NONE");
            detachment.ReportObjective(ObjectiveKind.Town, 8, float.NaN, 1f, 0, 0, true, "NAN");
            detachment.EndObjectives();
            TestAssert.That(detachment.ObjectiveCount == 1 && detachment.SlotOf(Town) < 0,
                "a relisting drops duplicates, unknown kinds and non-finite points");
            TestAssert.That(detachment.Team(0).Target == "KERSEY" && detachment.Team(0).X == 10000f,
                "a dropped objective does not strand the team that is on it");
            TestAssert.That(detachment.CheckLaunch(1, FieldMission.Recon, Town) == SpecOpsDenial.StaleObjective,
                "a launch against a dropped anchor is stale, never retargeted");
        }

        private static void CheckMissionCycle()
        {
            SpecOpsDetachment detachment = Listed();
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            int applied = 0;
            FieldResult landed = default;
            Func<FieldResult, bool> apply = r => { applied++; landed = r; return true; };
            Func<double> lucky = () => 0.0;

            detachment.Tick(29.9, lucky, apply);
            TestAssert.That(detachment.Team(0).State == TeamState.EnRoute && Math.Abs(detachment.Progress(0, 15.0) - 0.5f) < 0.01f,
                "travel runs its clock and reports progress");
            detachment.Tick(1000.0, lucky, apply);
            TestAssert.That(detachment.Team(0).State == TeamState.OnTask && detachment.NoticeKind(0) == FieldNotice.OnTask &&
                applied == 0 && Math.Abs(detachment.Remaining(0, 1000.0) - FieldCatalog.TaskSeconds(FieldMission.Recon)) < 0.001,
                "a late tick starts the task clock and never skips the roll");
            double done = 1000.0 + FieldCatalog.TaskSeconds(FieldMission.Recon);
            detachment.Tick(done, lucky, apply);
            FieldTeam alpha = detachment.Team(0);
            TestAssert.That(applied == 1 && landed.Mission == FieldMission.Recon && landed.Outcome == MissionOutcome.Success &&
                landed.X == 10000f && landed.Rank == 0 && landed.Anchor == Town,
                "a success hands the world effect to the runtime once, at the launch rank");
            TestAssert.That(alpha.State == TeamState.Holding && alpha.Wins == 1 && alpha.Last == MissionOutcome.Success &&
                detachment.Scouted(Town, done) && detachment.Posts(FieldMission.Recon) == 1,
                "RECON success scouts the objective and holds an observation post");
            TestAssert.That(Math.Abs(detachment.Remaining(0, done) - FieldCatalog.HoldSeconds(0)) < 0.001,
                "the post holds for the rank's time");
            TestAssert.That(detachment.ChanceFor(1, FieldMission.Seize, detachment.SlotOf(Town), done) ==
                FieldCatalog.SuccessChance(FieldMission.Seize, 0, 5, true), "a scouted objective improves later odds");
            TestAssert.That(detachment.CheckLaunch(1, FieldMission.Seize, Town) == SpecOpsDenial.None &&
                detachment.TeamOn(Town) == 0 && detachment.WorkingOn(Town) == -1,
                "a held post never blocks the follow-up mission it scouted for");

            detachment.Tick(done + FieldCatalog.HoldSeconds(0), lucky, apply);
            TestAssert.That(detachment.Team(0).State == TeamState.Recovering && detachment.NoticeKind(0) == FieldNotice.PostEnded,
                "the post ends into recovery");
            detachment.Tick(done + FieldCatalog.HoldSeconds(0) + FieldCatalog.RecoverSeconds, lucky, apply);
            TestAssert.That(detachment.Team(0).State == TeamState.Ready && detachment.NoticeKind(0) == FieldNotice.Ready &&
                detachment.TeamOn(Town) == -1 && applied == 1, "recovery returns the team READY with no second payout");
            detachment.Tick(done + FieldCatalog.ScoutSeconds + 1.0, lucky, apply);
            TestAssert.That(!detachment.Scouted(Town, done + FieldCatalog.ScoutSeconds + 1.0), "scouting expires");

            // SEIZE with nothing to hold: the roll succeeded, the world said no.
            detachment.TryLaunch(1, FieldMission.Seize, Town, 0f, 2000.0);
            detachment.Tick(2030.0, lucky, r => false);
            detachment.Tick(2090.0, lucky, r => false);
            TestAssert.That(detachment.Team(1).State == TeamState.Recovering && detachment.Team(1).Last == MissionOutcome.NoBuildings &&
                detachment.NoticeKind(0) == FieldNotice.NoBuildings && detachment.Posts(FieldMission.Seize) == 0,
                "a SEIZE that finds no building returns without a safehouse");

            // Recall from the field.
            SpecOpsDetachment recall = Listed();
            TestAssert.That(recall.CheckRecall(0) == SpecOpsDenial.NotDeployed && recall.CheckRecall(3) == SpecOpsDenial.Unformed,
                "only a deployed team can be recalled");
            recall.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            TestAssert.That(recall.TryRecall(0, 5.0) && recall.Team(0).State == TeamState.Recovering &&
                recall.Team(0).Last == MissionOutcome.Recalled && recall.TeamOn(Town) == -1,
                "a recall abandons the mission without a roll");
        }

        private static void CheckFailureAndLoss()
        {
            SpecOpsDetachment detachment = Listed();
            detachment.TryLaunch(0, FieldMission.Seize, Town, 0f, 0.0);
            detachment.TryLaunch(1, FieldMission.Sabotage, Sam, 0f, 0.0);
            int applied = 0;
            detachment.Tick(30.0, () => 0.0, r => { applied++; return true; });
            double[] rolls = { 0.7, 0.999 };
            int next = 0;
            detachment.Tick(200.0, () => rolls[next++], r => { applied++; return true; });
            FieldTeam alpha = detachment.Team(0);
            FieldTeam bravo = detachment.Team(1);
            TestAssert.That(applied == 0, "failure and loss touch nothing in the world");
            TestAssert.That(alpha.State == TeamState.Recovering && alpha.Last == MissionOutcome.Failed &&
                Math.Abs(detachment.Remaining(0, 200.0) - FieldCatalog.FailedRecoverSeconds) < 0.001 && alpha.Wins == 0,
                "a failed team returns for the longer rest and earns nothing");
            TestAssert.That(bravo.State == TeamState.Unformed && bravo.Last == MissionOutcome.Lost && bravo.Rank == 0 &&
                bravo.Wins == 0 && detachment.Formed == 1, "a lost team is gone with its rank; the slot is open");
            TestAssert.That(detachment.NoticeKind(0) == FieldNotice.Lost && detachment.NoticeTeam(0) == 1 &&
                FieldWords.Alarm(FieldNotice.Lost), "the loss is logged loudly");
            TestAssert.That(detachment.CheckRaise(1) == SpecOpsDenial.None, "a lost slot can be raised again");
        }

        private static void CheckRanksAndReadiness()
        {
            SpecOpsDetachment detachment = Listed();
            double now = 0.0;
            for (int mission = 0; mission < 6; mission++)
            {
                TestAssert.That(detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, now) == SpecOpsDenial.None,
                    "mission " + mission + " launches");
                now += 30.0; detachment.Tick(now, () => 0.0, r => true);
                now += 30.0; detachment.Tick(now, () => 0.0, r => true);
                detachment.TryRecall(0, now);
                now += 60.0; detachment.Tick(now, () => 0.0, r => true);
            }
            FieldTeam alpha = detachment.Team(0);
            TestAssert.That(alpha.Wins == 6 && alpha.Rank == FieldCatalog.MaxRank && alpha.State == TeamState.Ready,
                "six successes make ALPHA elite");
            TestAssert.That(detachment.BestRank == 3 && detachment.GroundReadiness == 4,
                "the best team sets fortification positions and fast-rope camps");
            TestAssert.That(detachment.ChanceFor(0, FieldMission.Recon, detachment.SlotOf(Sam), now) ==
                FieldCatalog.SuccessChance(FieldMission.Recon, 3, 3, false), "odds follow the chosen team's rank");
            TestAssert.That(FieldCatalog.ReconRadius(3) == 4500f && FieldCatalog.SeizeBuildings(3) == 4 &&
                FieldCatalog.HoldSeconds(3) == 480f && FieldCatalog.SuppressSeconds(9) == 60f,
                "rank widens effects and lengthens posts, clamped at ELITE");
        }

        private static void CheckPostsAndAbilities()
        {
            SpecOpsDetachment detachment = Listed();
            TestAssert.That(detachment.Covering(FieldMission.Recon, 10000f, 0f) < 0 && detachment.Posts() == 0,
                "no post, no coverage");
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            detachment.TryLaunch(1, FieldMission.Sabotage, Sam, 0f, 0.0);
            detachment.Tick(30.0, () => 0.0, r => true);
            detachment.Tick(100.0, () => 0.0, r => true);
            TestAssert.That(detachment.Posts() == 2 && detachment.Posts(FieldMission.Recon) == 1 &&
                detachment.Posts(FieldMission.Sabotage) == 1, "each success holds its own kind of post");
            TestAssert.That(detachment.Covering(FieldMission.Recon, 15900f, 0f) == 0 &&
                detachment.Covering(FieldMission.Recon, 16100f, 0f) < 0 &&
                detachment.Covering(FieldMission.Sabotage, 10000f, 0f) < 0 &&
                detachment.Covering(FieldMission.Sabotage, 24900f, 5000f) == 1,
                "a post covers its own reach, only for its own ability");

            TestAssert.That(detachment.TryUseAbility(FieldAbility.Spot, 100.0) &&
                !detachment.TryUseAbility(FieldAbility.Spot, 120.0) &&
                Math.Abs(detachment.AbilityRechargeRemaining(FieldAbility.Spot, 120.0) - 25f) < 0.001f &&
                detachment.TryUseAbility(FieldAbility.Suppress, 120.0) &&
                detachment.TryUseAbility(FieldAbility.Spot, 145.0),
                "each ability keeps its own recharge");
            TestAssert.That(!detachment.TryUseAbility((FieldAbility)7, 0.0) &&
                detachment.AbilityRechargeRemaining((FieldAbility)7, 0.0) == 0f, "unknown abilities are refused");
            TestAssert.That(FieldCatalog.PostFor(FieldAbility.Spot) == FieldMission.Recon &&
                FieldCatalog.PostFor(FieldAbility.Suppress) == FieldMission.Sabotage, "abilities map to their posts");
        }

        private static void CheckSnapshots()
        {
            SpecOpsDetachment host = Listed();
            host.TryRaise(2);
            host.TryLaunch(0, FieldMission.Recon, Town, 10000f, 0.0);
            host.TryLaunch(1, FieldMission.Sabotage, Sam, 0f, 0.0);
            host.Tick(30.0, () => 0.0, r => true);
            host.Tick(80.0, () => 0.0, r => true);
            host.TryUseAbility(FieldAbility.Suppress, 80.0);
            host.SeizeAvailable = false;
            var snapshot = new SpecOpsSnapshot();
            host.Export(80.0, snapshot);

            var client = new SpecOpsDetachment();
            client.Mirror(snapshot, 500.0);
            for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
            {
                FieldTeam a = host.Team(i), b = client.Team(i);
                TestAssert.That(a.State == b.State && a.Rank == b.Rank && a.Wins == b.Wins && a.Mission == b.Mission &&
                    a.Anchor == b.Anchor && a.Chance == b.Chance && a.Loss == b.Loss && a.Target == b.Target,
                    "team " + i + " mirrors");
                TestAssert.That(Math.Abs(host.Remaining(i, 80.0) - client.Remaining(i, 500.0)) < 0.01 &&
                    Math.Abs(host.Progress(i, 80.0) - client.Progress(i, 500.0)) < 0.01f,
                    "team " + i + " clocks rebase onto the client's time");
            }
            TestAssert.That(client.ObjectiveCount == 3 && client.Objective(1).Radars == 2 && client.Objective(0).Hostile &&
                !client.Objective(2).Hostile && client.Scouted(Town, 500.0) == host.Scouted(Town, 80.0),
                "objectives and scouting mirror");
            TestAssert.That(Math.Abs(client.AbilityRechargeRemaining(FieldAbility.Suppress, 500.0) - 90f) < 0.01f &&
                client.AbilityRechargeRemaining(FieldAbility.Spot, 500.0) == 0f, "ability recharges mirror");
            TestAssert.That(client.NoticeSerial == host.NoticeSerial && client.NoticeCount == host.NoticeCount &&
                client.NoticeKind(0) == host.NoticeKind(0), "the notice ring mirrors");
            TestAssert.That(!client.SeizeAvailable && client.Enabled, "host flags mirror");

            // Hostile bytes clamp; nothing throws, nothing exceeds its bound.
            snapshot.TeamState[0] = 99;
            snapshot.TeamRank[1] = 200;
            snapshot.TeamChance[1] = 250;
            snapshot.TeamLoss[1] = 250;
            snapshot.TeamLast[1] = 77;
            snapshot.TeamMission[1] = 44;
            snapshot.TeamX[1] = float.NaN;
            snapshot.TeamDuration[1] = float.PositiveInfinity;
            snapshot.TeamTarget[1] = new string('X', 400);
            snapshot.ObjectiveCount = 250;
            snapshot.ObjectiveKind[0] = 77;
            snapshot.ObjectiveZ[1] = float.NaN;
            snapshot.ObjectiveThreat[2] = 255;
            snapshot.AbilityRecharge[0] = 1e9f;
            snapshot.NoticeCount = 99;
            snapshot.NoticeKind[0] = 200;
            snapshot.NoticeTeam[0] = 200;
            client.Mirror(snapshot, 500.0);
            FieldTeam bent = client.Team(1);
            TestAssert.That(client.Team(0).State == TeamState.Unformed && bent.Rank == FieldCatalog.MaxRank &&
                bent.Chance == 100 && bent.Loss == 0 && bent.Last == MissionOutcome.None && bent.Mission == FieldMission.Recon &&
                bent.X == 0f && bent.Target.Length == SpecOpsDetachment.NameLength,
                "hostile team bytes clamp");
            TestAssert.That(client.ObjectiveCount == 1 && client.Objective(0).Threat == 99,
                "unknown or non-finite objectives are dropped and threat clamps");
            TestAssert.That(client.AbilityRechargeRemaining(FieldAbility.Spot, 500.0) <= FieldCatalog.AbilityRecharge(FieldAbility.Spot),
                "a recharge never exceeds its own length");
            TestAssert.That(client.NoticeCount == SpecOpsDetachment.NoticeSlots && client.NoticeKind(0) == FieldNotice.None &&
                client.NoticeTeam(0) == SpecOpsDetachment.TeamCount - 1, "notices clamp");
            client.Mirror(null, 0.0);
        }

        private static void CheckStealAndNewAbilities()
        {
            TestAssert.That(FieldCatalog.KnownMission((byte)FieldMission.Steal) &&
                FieldCatalog.PostFor(FieldAbility.Skywatch) == FieldMission.Recon &&
                FieldCatalog.PostFor(FieldAbility.Eavesdrop) == FieldMission.Steal &&
                FieldCatalog.PostFor(FieldAbility.Hunt) == FieldMission.Sabotage,
                "new abilities require their intended posts");
            var detachment = Listed();
            TestAssert.That(detachment.TryLaunch(0, FieldMission.Steal, Town, 0f, 0.0) == SpecOpsDenial.None,
                "STEAL launches against a listed objective");
            detachment.Tick(30.0, () => 0.0, _ => true);
            detachment.Tick(75.0, () => 0.0, _ => true);
            TestAssert.That(detachment.Posts(FieldMission.Steal) == 1 &&
                detachment.Covering(FieldMission.Steal, 10000f, 0f) == 0 &&
                detachment.TryUseAbility(FieldAbility.Eavesdrop, 75.0) &&
                !detachment.TryUseAbility(FieldAbility.Eavesdrop, 80.0) &&
                detachment.TryUseAbility(FieldAbility.Skywatch, 75.0) &&
                detachment.TryUseAbility(FieldAbility.Hunt, 75.0),
                "STEAL holds a listening post and new abilities have independent recharges");
            var failed = Listed();
            failed.TryLaunch(0, FieldMission.Steal, Town, 0f, 0.0);
            failed.Tick(30.0, () => 0.0, _ => false);
            failed.Tick(75.0, () => 0.0, _ => false);
            TestAssert.That(failed.Team(0).Wins == 0 && failed.Posts(FieldMission.Steal) == 0,
                "an effect that cannot land does not promote a team or create a post");
        }

        private static void CheckWords()
        {
            SpecOpsDetachment detachment = Listed();
            TestAssert.That(FieldWords.Summary(detachment) == "2 READY", "the banner counts states in words");
            TestAssert.That(FieldWords.Advice(detachment, 0.0).StartsWith("ALPHA READY"), "the advice names the next step");
            detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, 0.0);
            detachment.Tick(30.0, () => 0.0, r => true);
            detachment.Tick(60.0, () => 0.0, r => true);
            TestAssert.That(FieldWords.Advice(detachment, 60.0).Contains("SPOT"), "a held OP points at SPOT");
            TestAssert.That(FieldWords.TeamLine(detachment.Team(0), 300.0) == "HOLDING OP · KERSEY · 05:00",
                "the roster line says what, where and how long");
            TestAssert.That(FieldWords.Km(2500f) == "2.5 km" && FieldWords.Km(6000f) == "6 km" &&
                FieldWords.Seconds(120f) == "2 min" && FieldWords.Seconds(45f) == "45 s", "units read naturally");
            for (int i = 0; i < SpecOpsDetachment.TeamCount; i++)
                TestAssert.That(FieldWords.Callsign(i).Length <= 7, "callsigns fit the roster");
            for (byte d = 0; d <= (byte)SpecOpsDenial.BadMission; d++)
                TestAssert.That(FieldWords.Denial((SpecOpsDenial)d) != "REFUSED", "denial " + d + " has words");
            for (byte n = 1; n <= (byte)FieldNotice.NoBuildings; n++)
                TestAssert.That(FieldWords.Notice((FieldNotice)n, 0, FieldMission.Seize, "KERSEY") != null,
                    "notice " + n + " has words");
            for (int m = 0; m < FieldCatalog.MissionCount; m++)
                TestAssert.That(FieldWords.Effect((FieldMission)m, 0).Length > 20 && FieldWords.MissionTitle((FieldMission)m).Length <= 24,
                    "mission " + m + " explains itself");
        }
    }
}
