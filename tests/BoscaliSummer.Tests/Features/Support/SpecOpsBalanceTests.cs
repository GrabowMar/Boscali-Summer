using System;
using BoscaliSummer.Features.Support.Domain.SpecOps;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>
    /// SPEC OPS balance model, pinned quantitatively: odds tables, expected costs, cycle times,
    /// rank pacing, post share, threat shape and the mission/kind matrix. A balance change (F-7,
    /// F-8, F-15) updates these tables deliberately; nothing here may drift by accident.
    /// </summary>
    internal static class SpecOpsBalanceTests
    {
        private const int Town = 101;

        public static void Run()
        {
            CheckExpectedCosts();
            CheckCycleTimes();
            CheckRankPacing();
            CheckPostShare();
            CheckThreatCurve();
            CheckMissionKindMatrix();
        }

        private static SpecOpsDetachment Listed()
        {
            var detachment = new SpecOpsDetachment();
            detachment.BeginObjectives();
            detachment.ReportObjective(ObjectiveKind.Town, Town, 10000f, 0f, 5, 0, true, "KERSEY");
            detachment.EndObjectives();
            return detachment;
        }

        private static void CheckExpectedCosts()
        {
            // Unscouted rank-0 teams: fee plus the loss share times a 1000-alloc replacement.
            TestAssert.That(Expected(FieldMission.Recon, 0) == 440f, "RECON undefended: 400 + 4% of a team");
            TestAssert.That(Expected(FieldMission.Recon, 5) == 540f, "RECON defended: 400 + 14% of a team");
            TestAssert.That(Expected(FieldMission.Sabotage, 3) == 840f, "SABOTAGE guarded: 700 + 14% of a team");
            TestAssert.That(Expected(FieldMission.Steal, 5) == 760f, "STEAL defended: 600 + 16% of a team");
            TestAssert.That(Expected(FieldMission.Seize, 5) == 1100f, "SEIZE defended: 900 + 20% of a team");
            TestAssert.That(Expected(FieldMission.Seize, 8) == 1160f, "SEIZE hard: 900 + 26% of a team");
        }

        private static float Expected(FieldMission mission, int threat)
        {
            int success = FieldCatalog.SuccessChance(mission, 0, threat, false);
            int loss = FieldCatalog.LossChance(mission, 0, threat, success);
            return FieldCatalog.MissionCost(mission) + loss / 100f * FieldCatalog.RaiseCost;
        }

        private static void CheckCycleTimes()
        {
            // 10 km out: 40 s travel, then task, the recruit's 300 s hold and 60 s recovery.
            TestAssert.That(Math.Abs(Cycle(FieldMission.Recon) - 430f) < 0.001f, "RECON turns in 7 min 10 s");
            TestAssert.That(Math.Abs(Cycle(FieldMission.Sabotage) - 445f) < 0.001f, "SABOTAGE turns in 7 min 25 s");
            TestAssert.That(Math.Abs(Cycle(FieldMission.Steal) - 445f) < 0.001f, "STEAL turns in 7 min 25 s");
            TestAssert.That(Math.Abs(Cycle(FieldMission.Seize) - 460f) < 0.001f, "SEIZE turns in 7 min 40 s");
        }

        private static float Cycle(FieldMission mission) =>
            FieldCatalog.TravelSeconds(10000f) + FieldCatalog.TaskSeconds(mission) +
            FieldCatalog.HoldSeconds(0) + FieldCatalog.RecoverSeconds;

        private static void CheckRankPacing()
        {
            // Fastest possible ELITE: six RECON successes back to back, every post held out.
            SpecOpsDetachment detachment = Listed();
            double now = 0.0;
            for (int mission = 0; mission < 6; mission++)
            {
                detachment.TryLaunch(0, FieldMission.Recon, Town, 0f, now);
                now += FieldCatalog.TravelSeconds(0f);
                detachment.Tick(now, () => 0.0, r => true);
                now += FieldCatalog.TaskSeconds(FieldMission.Recon);
                detachment.Tick(now, () => 0.0, r => true);
                now += FieldCatalog.HoldSeconds(detachment.Team(0).Rank);
                detachment.Tick(now, () => 0.0, r => true);
                now += FieldCatalog.RecoverSeconds;
                detachment.Tick(now, () => 0.0, r => true);
            }
            FieldTeam alpha = detachment.Team(0);
            TestAssert.That(alpha.Wins == 6 && alpha.Rank == FieldCatalog.MaxRank, "six held posts make ELITE");
            TestAssert.That(Math.Abs(now - 3060.0) < 0.001, "even flawless, ELITE costs 51 minutes in the field");
        }

        private static void CheckPostShare()
        {
            // Share of a success cycle the post (and its ACTIONS) is actually up, rank 0, 10 km out.
            TestAssert.That(Math.Abs(Share(FieldMission.Recon) - 300f / 430f) < 0.001f, "RECON posts 70% of its cycle");
            TestAssert.That(Math.Abs(Share(FieldMission.Sabotage) - 300f / 445f) < 0.001f, "SABOTAGE posts 67% of its cycle");
            TestAssert.That(Math.Abs(Share(FieldMission.Seize) - 300f / 460f) < 0.001f, "SEIZE posts 65% of its cycle");
        }

        private static float Share(FieldMission mission) => FieldCatalog.HoldSeconds(0) / Cycle(mission);

        private static void CheckThreatCurve()
        {
            // F-15 baseline: -3 per threat to a -40 cap; one word spans a 12-point swing.
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Recon, 0, 0, false) == 85, "clear RECON is 85");
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Recon, 0, 4, false) == 73, "threat 4 costs 12");
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Recon, 0, 8, false) == 61, "threat 8 costs 24");
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Recon, 0, 13, false) == 46, "threat 13 costs 39");
            TestAssert.That(FieldCatalog.SuccessChance(FieldMission.Recon, 0, 14, false) == 45 &&
                FieldCatalog.SuccessChance(FieldMission.Recon, 0, 40, false) == 45, "the curve caps at threat 14");
            TestAssert.That(FieldWords.Threat(4) == "MODERATE" && FieldWords.Threat(8) == "MODERATE",
                "MODERATE spans threat 4 to 8 while the odds swing twelve points");
        }

        private static void CheckMissionKindMatrix()
        {
            // F-7 baseline: every mission goes almost everywhere; kind barely matters.
            foreach (FieldMission mission in new[] { FieldMission.Recon, FieldMission.Sabotage, FieldMission.Steal, FieldMission.Seize })
            {
                foreach (ObjectiveKind kind in new[] { ObjectiveKind.Airfield, ObjectiveKind.Outpost, ObjectiveKind.Town, ObjectiveKind.AirDefence })
                {
                    bool open = FieldCatalog.Allowed(mission, kind);
                    bool barred = mission == FieldMission.Seize && kind == ObjectiveKind.AirDefence;
                    TestAssert.That(open == !barred, mission + " on " + kind + (barred ? " is barred" : " is open"));
                }
                TestAssert.That(!FieldCatalog.Allowed(mission, ObjectiveKind.None), mission + " needs a real objective");
            }
        }
    }
}
