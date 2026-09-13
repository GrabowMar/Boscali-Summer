using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class MfdSecondaryObjectivesTests
    {
        public static void Run()
        {
            TestAssert.That(MfdSecondaryObjectives.PageCount(3, 1) == 3 && MfdSecondaryObjectives.ClampPage(9, 3, 1) == 2,
                "Compact MFDs page one readable card at a time");
            TestAssert.That(MfdSecondaryObjectives.PlainObjective("_BDF_PUSH - <color=#B2B2B2>Capture\n airstrip</color>") == "BDF PUSH - Capture airstrip",
                "Markup is removed before rendering and internal separators become readable spaces");
            TestAssert.That(MfdSecondaryObjectives.PlainObjective("<color=#B2B2") == "" && MfdSecondaryObjectives.PlainObjective(null) == "OBJECTIVE",
                "Incomplete markup never leaks into objective labels");
            TestAssert.That(MfdSecondaryObjectives.PageCount(0) == 1 &&
                MfdSecondaryObjectives.PageCount(3) == 2, "Secondary cards keep a bounded two-card page");
            TestAssert.That(MfdSecondaryObjectives.ClampPage(1, 5) == 1 &&
                MfdSecondaryObjectives.ClampPage(2, 3) == 1 &&
                MfdSecondaryObjectives.ClampPage(-1, 3) == 0,
                "Secondary page survives refresh and clamps when the objective list shrinks");
            TestAssert.That(MfdSecondaryObjectives.TimeLabel(false, 60.1f) == "LEFT 01:01" &&
                MfdSecondaryObjectives.TimeLabel(false, 0.1f) == "LEFT 00:01",
                "Remaining time rounds upward until the host deadline");
            TestAssert.That(MfdSecondaryObjectives.TimeLabel(false, 0f) == "ENDED" &&
                MfdSecondaryObjectives.TimeLabel(true, 100f) == "COMPLETE",
                "Only authoritative completion produces the completed timer state");
            TestAssert.That(MfdSecondaryObjectives.TimeLabel(false, float.NaN) == "TIME UNKNOWN" &&
                MfdSecondaryObjectives.TimeLabel(false, float.PositiveInfinity) == "TIME UNKNOWN",
                "Missing deadline data never claims expiration");
        }
    }
}
