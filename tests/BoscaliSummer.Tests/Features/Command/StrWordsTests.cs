using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The layout captions every surface prints (annex 05 P2 test 1 and the tables around it).</summary>
    internal static class StrWordsTests
    {
        public static void Run()
        {
            EveryModeSpeaksAndNoTwoShareAFlavourLine();
            TheTablesCoverTheirVocabularies();
            TheNoOpeningChecklistHasSixLabels();
            AStaleReportStatesItsAge();
            TheDashIsAnEmDash();
        }

        private static void EveryModeSpeaksAndNoTwoShareAFlavourLine()
        {
            var flavours = new HashSet<string>(); var heads = new HashSet<string>(); var statuses = new HashSet<string>();
            foreach (StrBoardMode mode in Enum.GetValues(typeof(StrBoardMode)))
            {
                TestAssert.That(StrWords.Status(mode).Length > 0 && StrWords.Flavour(mode).Length > 0, mode + " has a status strip and a flavour line");
                TestAssert.That(flavours.Add(StrWords.Flavour(mode)), mode + " has a flavour line of its own");
                TestAssert.That(statuses.Add(StrWords.Status(mode)), mode + " has a status strip of its own");
                if (mode == StrBoardMode.Operation)
                    TestAssert.That(StrWords.Chip(mode).Length == 0 && StrWords.Head(mode).Length == 0, "the live board needs no banner words");
                else
                {
                    TestAssert.That(StrWords.Chip(mode).Length >= 4 && StrWords.Head(mode).Length >= 8, mode + " has a chip and a headline");
                    TestAssert.That(heads.Add(StrWords.Head(mode)), mode + " has a headline of its own");
                }
            }
            TestAssert.That(StrWords.Flavour(StrBoardMode.StaffOff) != StrWords.Flavour(StrBoardMode.HostLacksStaff), "staff off here and staff off on the host read differently");
            TestAssert.That(StrWords.Flavour(StrBoardMode.StaffOff).Contains("staff log needs the staff"), "the StaffOff line does not claim the log works");
            TestAssert.That(StrWords.Flavour(StrBoardMode.HostLacksStaff).Contains("dashes"), "the HostLacksStaff line admits the dashes");
            TestAssert.That(StrWords.NoAnswerWhy().Contains("30 s") && StrWords.NoAnswerDo().Contains("F1"), "NoAnswer has its own why and do");
        }

        private static void TheTablesCoverTheirVocabularies()
        {
            TestAssert.That(StrWords.Alert(5) == "NORMAL" && StrWords.Alert(4) == "ELEVATED" && StrWords.Alert(3) == "HIGH" &&
                            StrWords.Alert(2) == "SEVERE" && StrWords.Alert(1) == "MAXIMUM", "the five DEFCON words");
            TestAssert.That(StrWords.Alert(0) == "NOT MEASURED" && StrWords.Alert(6) == "NOT MEASURED" && StrWords.Alert(-1) == "NOT MEASURED", "a level that is not 1 to 5 is not measured");

            TestAssert.That(StrWords.Route(TheaterOpKind.Assault, 1) == "FORMING" && StrWords.Route(TheaterOpKind.Assault, 2) == "ADVANCING" &&
                            StrWords.Route(TheaterOpKind.Assault, 3) == "IN CONTACT", "an assault has three steps");
            TestAssert.That(StrWords.Route(TheaterOpKind.Defend, 2) == "HOLDING" && StrWords.Route(TheaterOpKind.Recon, 2) == "SCOUTING" &&
                            StrWords.Route(TheaterOpKind.Defend, 3) == "" && StrWords.Route(TheaterOpKind.Recon, 0) == "", "defend and recon have two");
            TestAssert.That(StrWords.RouteSteps(TheaterOpKind.Assault) == 3 && StrWords.RouteSteps(TheaterOpKind.Defend) == 2 && StrWords.RouteSteps(TheaterOpKind.Recon) == 2, "route lengths");

            var chips = new HashSet<string>();
            foreach (ObjectiveFamily family in Enum.GetValues(typeof(ObjectiveFamily)))
                if (family != ObjectiveFamily.Unknown) TestAssert.That(chips.Add(StrWords.Family(family)), family + " has its own chip");
            TestAssert.That(chips.Count == 8 && StrWords.Family(ObjectiveFamily.Electronic) == "EW" && StrWords.Family(ObjectiveFamily.Logistics) == "SUPPLY", "eight families");
            TestAssert.That(StrWords.Family(ObjectiveFamily.Unknown).Length > 0, "an unknown family still gets a word");

            TestAssert.That(StrWords.WingLabel(WingSortieIntent.Destroy, false) == "SEND WING" && StrWords.WingLabel(WingSortieIntent.Cover, false) == "COVER" &&
                            StrWords.WingLabel(WingSortieIntent.Observe, false) == "SCOUT" && StrWords.WingLabel(WingSortieIntent.None, false) == "SEND WING", "the wing button names the job");
            TestAssert.That(StrWords.WingLabel(WingSortieIntent.Cover, true) == "RECALL", "a tasked wing reads RECALL whatever the job");

            TestAssert.That(StrWords.ReducedHead(TheaterEmptyReason.NoGroundWar) == "NO GROUND WAR HERE" && StrWords.ReducedHead(TheaterEmptyReason.TacticsOff) == "GROUND STEERING OFF" &&
                            StrWords.ReducedHead(TheaterEmptyReason.RtsOwnsOrders) == "RTS HAS THE GROUND", "the three reduced headlines");

            foreach (WaveBlock block in Enum.GetValues(typeof(WaveBlock)))
                TestAssert.That(block == WaveBlock.None ? StrWords.WaveBlockCaption(block).Length == 0 : StrWords.WaveBlockCaption(block).Length >= 6, block + " has a short caption");
        }

        private static void TheNoOpeningChecklistHasSixLabels()
        {
            var labels = new HashSet<string>();
            foreach (var reason in new[] { TheaterEmptyReason.StaffFault, TheaterEmptyReason.NoTerritoryField, TheaterEmptyReason.NoFronts,
                                           TheaterEmptyReason.NoFriendlyGroundNearFront, TheaterEmptyReason.FrontsQuiet, TheaterEmptyReason.PostureTooCautious })
                TestAssert.That(labels.Add(StrWords.CheckLabel(reason)) && StrWords.CheckLabel(reason).Length > 8, reason + " has a label of its own");
            TestAssert.That(StrWords.CheckLabel(TheaterEmptyReason.CalledOff) == StrWords.CheckLabel(TheaterEmptyReason.PostureTooCautious), "a call-off and a cautious posture share the last row");
            TestAssert.That(labels.Count <= StrViewModel.MaxChecks, "the labels fit the checklist");
            TestAssert.That(StrWords.CheckLabel(TheaterEmptyReason.None) == "" && StrWords.CheckLabel(TheaterEmptyReason.FirstReview) == "", "a reason that is not on the checklist has no label");
        }

        private static void AStaleReportStatesItsAge()
        {
            TestAssert.That(StrWords.StaleReport(20.9f) == "REPORT 20 S OLD" + StrWords.Dot + "ORDERS WAIT FOR THE HOST", "the stale line names whole seconds and what waits");
            TestAssert.That(StrWords.StaleReport(float.NaN) == "REPORT STALE" + StrWords.Dot + "ORDERS WAIT FOR THE HOST" && StrWords.StaleReport(-1f).StartsWith("REPORT STALE"), "an unknown age is not guessed");
        }

        private static void TheDashIsAnEmDash()
        {
            TestAssert.That(StrWords.Dash == "—" && StrWords.Dash == TheaterReadout.Age(float.NaN), "the dash is the one TheaterReadout already prints");
        }
    }
}
