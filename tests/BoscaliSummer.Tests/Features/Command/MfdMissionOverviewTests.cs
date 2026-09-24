using System;
using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class MfdMissionOverviewTests
    {
        public static void Run()
        {
            TestAssert.That(MfdMissionOverview.Stage(0f, 25f, 50f) == 0 &&
                MfdMissionOverview.Stage(25f, 25f, 50f) == 1 &&
                MfdMissionOverview.Stage(50f, 25f, 50f) == 2,
                "Escalation stages follow the mission's own thresholds");

            TestAssert.That(MfdMissionOverview.Stage(0f, 0f, 0f) == 2 &&
                MfdMissionOverview.Stage(0f, 0f, 50f) == 1 &&
                MfdMissionOverview.Stage(10f, 25f, 0f) == 0,
                "An unset threshold never gates a stage the game already cleared");

            TestAssert.That(Near(MfdMissionOverview.Fraction(0f, 25f, 50f), 0f) &&
                Near(MfdMissionOverview.Fraction(25f, 25f, 50f), 1f / 3f) &&
                Near(MfdMissionOverview.Fraction(50f, 25f, 50f), 2f / 3f),
                "The ladder gives each stage an equal third of the track");

            TestAssert.That(Near(MfdMissionOverview.Fraction(100f, 25f, 50f), 1f) &&
                Near(MfdMissionOverview.Fraction(0f, 0f, 0f), 1f) &&
                Near(MfdMissionOverview.Fraction(0f, 0f, 50f), 1f / 3f),
                "A saturated or unset ladder stays inside the track");

            TestAssert.That(MfdMissionOverview.Caption(0f, 25f, 50f) ==
                "CUR 0 · TAC IN 25",
                "Before the tactical gate the caption states the remaining score");

            TestAssert.That(MfdMissionOverview.Caption(30f, 25f, 50f) ==
                "CUR 30 · TAC ACTIVE · STR IN 20",
                "Between gates the caption reports the active stage and the next one");

            TestAssert.That(MfdMissionOverview.Caption(55f, 25f, 50f) ==
                "CUR 55 · STR ACTIVE" &&
                MfdMissionOverview.Caption(10f, 25f, 0f) ==
                "CUR 10 · TAC IN 15",
                "A cleared or unset strategic gate is reported without inventing a threshold");

            TestAssert.That(MfdMissionOverview.Caption(0f, 0f, 0f) == "NO ESCALATION GATES" &&
                MfdMissionOverview.Caption(0f, 0f, 50f) ==
                "CUR 0 · TAC ACTIVE · STR IN 50",
                "Unset thresholds say so instead of showing a confident zero");

            TestAssert.That(MfdMissionOverview.Whole(1500f) == "1,500" && MfdMissionOverview.Whole(25f) == "25",
                "Threshold figures group with the invariant comma, never the machine's separator");

            TestAssert.That(Near(MfdMissionOverview.NextGateProgress(10f, 25f, 50f), .4f) &&
                Near(MfdMissionOverview.NextGateProgress(30f, 25f, 50f), .2f) &&
                Near(MfdMissionOverview.NextGateProgress(50f, 25f, 50f), 1f) &&
                MfdMissionOverview.NextGateLabel(30f, 25f, 50f).Contains("+20 TO STRATEGIC") &&
                MfdMissionOverview.NextGateLabel(float.NaN, 25f, 50f) == "SCORE LINK UNAVAILABLE",
                "Score tape advances toward the next host gate without implying a time countdown");
        }

        private static bool Near(float value, float expected) => Math.Abs(value - expected) < 0.0005f;
    }
}
