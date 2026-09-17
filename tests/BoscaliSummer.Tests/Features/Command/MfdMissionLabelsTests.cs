using System;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Features.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class MfdMissionLabelsTests
    {
        public static void Run()
        {
            string[] titles =
            {
                "SECURE THE FRONT", "HOLD THE LINE", "BREAK ENEMY PRESSURE", "AIR INTERCEPT",
                "WATCH THE APPROACH", "SILENCE THE RADAR", "ESTABLISH A BEACHHEAD", "ROOFTOP INSERTION",
                "BRING THEM HOME", "RECONNAISSANCE PASS", "CONFIRM THE STRIKE", "COVER THE SUPPLY RUN",
                "CUT THE SUPPLY LINE", "COVER THE ENGINEERS", "HUNT THE JAMMER",
                "BRING BACK THE INTELLIGENCE", "SURVEY THE AFTERMATH",
            };
            foreach (string title in titles)
            {
                TestAssert.That(MfdMissionLabels.ContractFamily(title) != MfdMissionLabels.UnknownFamily,
                    "every contract title maps to a family: " + title);
                TestAssert.That(!string.IsNullOrEmpty(MfdMissionLabels.ContractGlyph(title)),
                    "every contract title maps to a glyph shape: " + title);
            }

            foreach (OperationKind kind in (OperationKind[])Enum.GetValues(typeof(OperationKind)))
            {
                string issued = OperationTitles.Title(kind);
                TestAssert.That(MfdMissionLabels.KnownContract(issued),
                    "every operation kind the director can issue is mapped, not the generic flag: " + kind + " -> " + issued);
                TestAssert.That(MfdMissionLabels.ContractFamily(issued) != MfdMissionLabels.UnknownFamily,
                    "the board knows the family a new operation kind issues: " + kind + " -> " + issued);
            }

            TestAssert.That(MfdMissionLabels.ContractGlyph("CONFIRM THE STRIKE") == "target" &&
                MfdMissionLabels.ContractFamily("HUNT THE JAMMER") == "ELECTRONIC WAR" &&
                MfdMissionLabels.ContractGlyph("COVER THE SUPPLY RUN") == "convoy",
                "battle damage, jamming hunts and logistics carry their own symbols");

            TestAssert.That(MfdMissionLabels.ContractGlyph("A FUTURE CONTRACT") == MfdMissionLabels.UnknownGlyph &&
                MfdMissionLabels.ContractFamily("A FUTURE CONTRACT") == MfdMissionLabels.UnknownFamily &&
                MfdMissionLabels.ContractGlyph(null) == MfdMissionLabels.UnknownGlyph,
                "an unknown title degrades to the generic flag, never to a broken panel");

            TestAssert.That(MfdMissionLabels.ObjectiveGlyph("DestroyUnits") == "target" &&
                MfdMissionLabels.ObjectiveGlyph("CaptureAirbase") == "flag" &&
                MfdMissionLabels.ObjectiveGlyph("ReachWaypoints") == "nav" &&
                MfdMissionLabels.ObjectiveGlyph("SpotUnit") == "eye",
                "vanilla objective types get distinct glyph shapes");

            TestAssert.That(MfdMissionLabels.ObjectiveTypeLabel("DestroyUnits") == "DESTROY" &&
                MfdMissionLabels.ObjectiveTypeLabel("CaptureAirbase") == "CAPTURE" &&
                MfdMissionLabels.ObjectiveTypeLabel("ReachWaypoints") == "REACH" &&
                MfdMissionLabels.ObjectiveTypeLabel("SpotUnit") == "SURVEIL",
                "vanilla objective types get readable tags");

            TestAssert.That(MfdMissionLabels.ObjectiveTypeLabel("CompleteOtherObjective") == "LINKED" &&
                MfdMissionLabels.ObjectiveTypeLabel("WaitSeconds") == "HOLD" &&
                MfdMissionLabels.ObjectiveTypeLabel("ReachUnits") == "REACH" &&
                MfdMissionLabels.ObjectiveTypeLabel("CrashAircraft") == "AIR LOSS",
                "every vanilla objective type used by missions reads in plain language");

            TestAssert.That(MfdMissionLabels.TypeSummary(new[] { "DestroyUnits", "DestroyUnits", "SpotUnit" }) ==
                "DESTROY 2   ·   SURVEIL 1",
                "the objective summary counts by type, most common first");
            TestAssert.That(MfdMissionLabels.TypeSummary(new[] { "ReachWaypoints", "DestroyUnits", "ReachUnits" }) ==
                "REACH 2   ·   DESTROY 1",
                "ties keep first-seen order so the header never reshuffles");
            TestAssert.That(MfdMissionLabels.TypeSummary(null) == "" &&
                MfdMissionLabels.TypeSummary(new string[0]) == "",
                "no objectives means no summary");

            TestAssert.That(MfdMissionLabels.ObjectiveGlyph("SomethingNew") == MfdMissionLabels.UnknownGlyph &&
                MfdMissionLabels.ObjectiveTypeLabel("SomethingNew") == "OBJECTIVE" &&
                MfdMissionLabels.ObjectiveGlyph(null) == MfdMissionLabels.UnknownGlyph,
                "a new objective type titles itself instead of guessing");
        }
    }
}
