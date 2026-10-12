using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>Phase and progress are derived from what the ground groups really report, not from a timer (annex 01 E4).</summary>
    internal static class OpPhaseRulesTests
    {
        public static void Run()
        {
            WithoutAReportTheOperationIsAwaitingWithNoNote();
            SteeringOffIsAwaitingWithItsNote();
            NoGroupsIsAwaitingWithItsNote();
            WithdrawingBeatsContact();
            ContactBeatsTheKindRules();
            DefendHoldsOnlyNearTheFix();
            ReconScoutsAndNeverAdvances();
            AssaultAdvancesOnlyWithAnAssaultGroup();
            SectorAssaultProgressIsTheGainFraction();
            DefendAndReconProgress();
            ProgressIsUnknownWhereItCannotBeMeasured();
        }

        private static PhaseInput Report(int groups = 2, int rally = 0, int line = 0, int assault = 0, int contact = 0, int withdraw = 0, float nearest = -1f) =>
            new PhaseInput { HaveReport = true, Steering = true, Groups = groups, Rally = rally, Line = line, Assault = assault, Contact = contact, Withdraw = withdraw, NearestToFixMeters = nearest };

        private static void Is(TheaterOpKind kind, PhaseInput input, TheaterPhase phase, TheaterPhaseNote note, string label)
        {
            TheaterPhase got = OpPhaseRules.Derive(kind, input, out TheaterPhaseNote gotNote);
            TestAssert.That(got == phase && gotNote == note, label + " (got " + got + "/" + gotNote + ", want " + phase + "/" + note + ")");
        }

        private static void WithoutAReportTheOperationIsAwaitingWithNoNote()
        {
            var none = new PhaseInput { HaveReport = false, Steering = true, Groups = 5, Assault = 5, Contact = 5 };
            foreach (TheaterOpKind kind in new[] { TheaterOpKind.Assault, TheaterOpKind.Defend, TheaterOpKind.Recon })
                Is(kind, none, TheaterPhase.Awaiting, TheaterPhaseNote.None, kind + " with no report waits and says nothing about why");
        }

        private static void SteeringOffIsAwaitingWithItsNote()
        {
            PhaseInput i = Report(assault: 2);
            i.Steering = false;
            Is(TheaterOpKind.Assault, i, TheaterPhase.Awaiting, TheaterPhaseNote.NoSteering, "steering off is Awaiting/NoSteering whatever the groups say");
        }

        private static void NoGroupsIsAwaitingWithItsNote()
        {
            Is(TheaterOpKind.Assault, Report(groups: 0), TheaterPhase.Awaiting, TheaterPhaseNote.NoForces, "a report with no groups is Awaiting/NoForces");
            PhaseInput off = Report(groups: 0);
            off.Steering = false;
            Is(TheaterOpKind.Assault, off, TheaterPhase.Awaiting, TheaterPhaseNote.NoSteering, "steering off is named before the missing groups");
        }

        private static void WithdrawingBeatsContact()
        {
            Is(TheaterOpKind.Assault, Report(groups: 2, withdraw: 2, contact: 1), TheaterPhase.Withdrawing, TheaterPhaseNote.None, "every group withdrawing beats contact");
            Is(TheaterOpKind.Assault, Report(groups: 2, withdraw: 1, contact: 1), TheaterPhase.InContact, TheaterPhaseNote.None, "one group still in contact is IN CONTACT");
        }

        private static void ContactBeatsTheKindRules()
        {
            foreach (TheaterOpKind kind in new[] { TheaterOpKind.Assault, TheaterOpKind.Defend, TheaterOpKind.Recon })
                Is(kind, Report(contact: 1, assault: 1, line: 1, nearest: 100f), TheaterPhase.InContact, TheaterPhaseNote.None, kind + " in contact");
        }

        private static void DefendHoldsOnlyNearTheFix()
        {
            Is(TheaterOpKind.Defend, Report(rally: 1, nearest: 2000f), TheaterPhase.Holding, TheaterPhaseNote.None, "a rally group 2 km out holds");
            Is(TheaterOpKind.Defend, Report(line: 1, nearest: 2500f), TheaterPhase.Holding, TheaterPhaseNote.None, "a line group exactly 2.5 km out holds");
            Is(TheaterOpKind.Defend, Report(rally: 1, nearest: 3000f), TheaterPhase.Forming, TheaterPhaseNote.None, "a rally group 3 km away is still forming");
            Is(TheaterOpKind.Defend, Report(rally: 1, nearest: -1f), TheaterPhase.Forming, TheaterPhaseNote.None, "an unknown distance is forming, not holding");
            Is(TheaterOpKind.Defend, Report(assault: 2, nearest: 100f), TheaterPhase.Forming, TheaterPhaseNote.None, "an assault group is not a holding group");
            Is(TheaterOpKind.Defend, Report(rally: 1, nearest: float.NaN), TheaterPhase.Forming, TheaterPhaseNote.None, "a NaN distance is forming");
        }

        private static void ReconScoutsAndNeverAdvances()
        {
            Is(TheaterOpKind.Recon, Report(line: 1), TheaterPhase.Scouting, TheaterPhaseNote.None, "a line group scouts");
            Is(TheaterOpKind.Recon, Report(assault: 1), TheaterPhase.Scouting, TheaterPhaseNote.None, "an assault-stage group on a recon is scouting, never advancing");
            Is(TheaterOpKind.Recon, Report(rally: 2), TheaterPhase.Forming, TheaterPhaseNote.None, "groups still rallying are forming");
        }

        private static void AssaultAdvancesOnlyWithAnAssaultGroup()
        {
            Is(TheaterOpKind.Assault, Report(assault: 1, line: 1), TheaterPhase.Advancing, TheaterPhaseNote.None, "an assault group advances");
            Is(TheaterOpKind.Assault, Report(line: 2), TheaterPhase.Forming, TheaterPhaseNote.None, "groups on the line are still forming");
            Is(TheaterOpKind.Assault, Report(rally: 2), TheaterPhase.Forming, TheaterPhaseNote.None, "groups rallying are forming");
        }

        private static void SectorAssaultProgressIsTheGainFraction()
        {
            const TheaterOpKind a = TheaterOpKind.Assault;
            const FrontSource s = FrontSource.Sector;
            TestAssert.Near(OpPhaseRules.Progress(a, s, true, 0.1f, 0.1f, 0f, false), 0f, "no gain yet is 0");
            TestAssert.Near(OpPhaseRules.Progress(a, s, true, 0.225f, 0.1f, 0f, false), 0.5f, "halfway from the start to the hold level is 0.5");
            TestAssert.Near(OpPhaseRules.Progress(a, s, true, 0.6f, 0.1f, 0f, false), 1f, "past the hold level clamps to 1");
            TestAssert.Near(OpPhaseRules.Progress(a, s, true, -0.5f, 0.1f, 0f, false), 0f, "a loss clamps to 0");
        }

        private static void DefendAndReconProgress()
        {
            TestAssert.Near(OpPhaseRules.Progress(TheaterOpKind.Defend, FrontSource.Base, false, 0f, 0f, 15f, false), 0.5f, "15 s of the 30 s clear time is 0.5");
            TestAssert.Near(OpPhaseRules.Progress(TheaterOpKind.Defend, FrontSource.Sector, true, 0f, 0f, 45f, false), 1f, "past the clear time clamps to 1");
            TestAssert.Near(OpPhaseRules.Progress(TheaterOpKind.Defend, FrontSource.Objective, true, 0f, 0f, 0f, false), 0f, "no quiet yet is 0");
            TestAssert.Near(OpPhaseRules.Progress(TheaterOpKind.Recon, FrontSource.Sector, false, 0f, 0f, 0f, true), 1f, "an observed recon is complete");
            TestAssert.Near(OpPhaseRules.Progress(TheaterOpKind.Recon, FrontSource.Sector, false, 0f, 0f, 0f, false), 0f, "an unobserved recon has made none");
        }

        private static void ProgressIsUnknownWhereItCannotBeMeasured()
        {
            TestAssert.Eq(OpPhaseRules.Progress(TheaterOpKind.Assault, FrontSource.Objective, true, 0.2f, 0.1f, 0f, false), -1f, "an objective assault is not a fraction");
            TestAssert.Eq(OpPhaseRules.Progress(TheaterOpKind.Assault, FrontSource.Base, true, 0.2f, 0.1f, 0f, false), -1f, "a base is not a contour");
            TestAssert.Eq(OpPhaseRules.Progress(TheaterOpKind.Assault, FrontSource.Sector, false, 0.2f, 0.1f, 0f, false), -1f, "no hold reading is unknown");
            TestAssert.Eq(OpPhaseRules.Progress(TheaterOpKind.Assault, FrontSource.Sector, true, float.NaN, 0.1f, 0f, false), -1f, "a NaN hold is unknown");
            TestAssert.Eq(OpPhaseRules.Progress(TheaterOpKind.Assault, FrontSource.Sector, true, 0.5f, 0.4f, 0f, false), -1f, "a start already past the hold level has no fraction");
            TestAssert.Eq(OpPhaseRules.Progress(TheaterOpKind.Defend, FrontSource.Base, false, 0f, 0f, float.NaN, false), -1f, "a NaN clear time is unknown");
        }
    }
}
