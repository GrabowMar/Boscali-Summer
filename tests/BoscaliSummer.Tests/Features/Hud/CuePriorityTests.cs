using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>Which feed wins the cue strip in which situation.</summary>
    internal static class CuePriorityTests
    {
        public static void Run()
        {
            CueContext none = CueContext.None;
            TestAssert.That(CuePriority.Rank("missile", none) > CuePriority.Rank("radio", none), "Without a context a missile preview outranks the radio line");
            TestAssert.That(CuePriority.Rank("nobody-declared-this", none) == CuePriority.Default, "An unknown feed ranks at the default");

            CueContext approach = CuePriority.Detect(true, false, false);
            TestAssert.That(CuePriority.Rank("ils", approach) > CuePriority.Rank("theater", approach) &&
                CuePriority.Rank("ils", approach) > CuePriority.Rank("support", approach) &&
                CuePriority.Rank("autopilot", approach) > CuePriority.Rank("radio", approach), "On approach the ILS words win, then the autopilot");
            TestAssert.That(CuePriority.Rank("ils", none) < CuePriority.Rank("ils", approach), "...and only on approach");

            CueContext dock = CuePriority.Detect(false, true, false);
            TestAssert.That(CuePriority.Rank("skywell", dock) > CuePriority.Rank("missile", dock) && CuePriority.Rank("skywell", dock) > CuePriority.Rank("ace-hunt", dock),
                "At the boom the SKYWELL director cues win");

            CueContext combat = CuePriority.Detect(false, false, true);
            TestAssert.That(CuePriority.Rank("missile", combat) > CuePriority.Rank("ils", combat) && CuePriority.Rank("ace-hunt", combat) > CuePriority.Rank("radio", combat) &&
                CuePriority.Rank("lance", combat) > CuePriority.Rank("support", combat), "In combat the missile, then strike lines win");

            CueContext both = CuePriority.Detect(true, false, true);
            TestAssert.That(both == (CueContext.Approach | CueContext.Combat), "Contexts combine");
            TestAssert.That(CuePriority.Rank("ils", both) == 100 && CuePriority.Rank("missile", both) == 100, "Overlapping contexts take the best boost, never the sum");
            TestAssert.That(CuePriority.Detect(false, false, false) == CueContext.None, "Nothing special: no context");
        }
    }
}
