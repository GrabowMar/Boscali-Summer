using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// The STR revamp's pure core: the corrected control-field rule (COREControl). Every assertion here is a
    /// behaviour a screenshot cannot check — unknown reading as unknown, contested
    /// requiring contact, elapsed time surviving an unchanged snapshot.
    /// </summary>
    internal static class StrRevampTests
    {
        public static void Run()
        {
            ContestedNeedsContact();
            ClassificationStaysHonest();
            ResponseConvergesByPresence();
            ElapsedSurvivesUnchangedSnapshots();
        }

        private static void ContestedNeedsContact()
        {
            // The quiet-map false positive: near-zero hold under strategic influence,
            // with no troops on either side, is open ground — not a battle, not DEFCON 2.
            TestAssert.That(!ControlFieldCore.IsContested(0.005f, 0f, 0f, false),
                "influence alone without troops contests nothing");
            TestAssert.That(!ControlFieldCore.IsContested(0f, 10f, 0f, false),
                "one-sided presence without opposition contests nothing");
            TestAssert.That(ControlFieldCore.IsContested(0f, 10f, 10f, false),
                "balanced forces in contact contest the cell");
            TestAssert.That(!ControlFieldCore.IsContested(0.9f, 10f, 0.04f, false),
                "a trace of opposition below the noise floor contests nothing");
            TestAssert.That(ControlFieldCore.IsContested(-0.8f, 12f, 4f, false),
                "pressure advancing against the holder contests the cell");
            TestAssert.That(!ControlFieldCore.IsContested(0.8f, 12f, 4f, false),
                "pressure agreeing with the holder is an advance, not a clash");
            TestAssert.That(ControlFieldCore.IsContested(0.9f, 0f, 0f, true),
                "an anchored clash still contests with no field presence");
        }

        private static void ClassificationStaysHonest()
        {
            TestAssert.That(ControlFieldCore.Classify(0.004f, 0f, 0f, false) == ControlState.Neutral,
                "near-zero hold without contact is neutral ground");
            TestAssert.That(ControlFieldCore.Classify(0.5f, 0f, 0f, false) == ControlState.Friendly,
                "held ground without contact stays friendly");
            TestAssert.That(ControlFieldCore.Classify(-0.5f, 0f, 0f, false) == ControlState.Hostile,
                "enemy ground without contact stays hostile");
            TestAssert.That(ControlFieldCore.Classify(0f, 8f, 8f, false) == ControlState.Contested,
                "balanced contact classifies contested");
            TestAssert.That(ControlFieldCore.Classify(float.NaN, 8f, 8f, false) == ControlState.Contested,
                "unreadable hold with balanced contact still contests");
            TestAssert.That(ControlFieldCore.Classify(float.NaN, 0f, 0f, false) == ControlState.Neutral,
                "an unreadable empty cell is neutral, never a confident claim");
        }

        private static void ResponseConvergesByPresence()
        {
            TestAssert.That(ControlFieldCore.Response(0f, 10f) == 0f,
                "no elapsed time moves nothing");
            float troops = ControlFieldCore.Response(15f, 10f);
            float empty = ControlFieldCore.Response(15f, 0f);
            TestAssert.That(troops > 0.5f, "troops present converge within seconds");
            TestAssert.That(empty > 0f && empty < troops,
                "empty ground recovers slowly, never frozen and never faster than contact");
            TestAssert.That(ControlFieldCore.Response(float.NaN, 10f) == 0f,
                "an unreadable interval moves nothing");
            TestAssert.That(ControlFieldCore.Response(-5f, 10f) == 0f,
                "a negative interval moves nothing");
        }

        private static void ElapsedSurvivesUnchangedSnapshots()
        {
            // First read evaluates with no history.
            TestAssert.That(ControlFieldCore.TryElapsed(100f, -1f, false, true, out float elapsed, out float consumed),
                "the first read always evaluates");
            TestAssert.That(elapsed == 0f && consumed == 100f, "the first read advances nothing");

            // A changed snapshot at host cadence advances at real speed, capped.
            TestAssert.That(ControlFieldCore.TryElapsed(105f, 100f, true, true, out elapsed, out consumed),
                "a changed snapshot evaluates");
            TestAssert.That(elapsed == 5f && consumed == 105f, "five host seconds advance five seconds");
            TestAssert.That(ControlFieldCore.TryElapsed(200f, 105f, true, true, out elapsed, out _),
                "a long gap still evaluates");
            TestAssert.That(elapsed == 5f, "one pass never advances past the cap");

            // An unchanged snapshot inside the window skips WITHOUT consuming: the
            // interval is preserved for the next pass instead of being dropped.
            TestAssert.That(!ControlFieldCore.TryElapsed(105.5f, 105f, true, false, out elapsed, out consumed),
                "an unchanged snapshot inside the window skips");
            TestAssert.That(elapsed == 0f && consumed == 105f, "the skipped interval is preserved, not dropped");

            // Past the window the field re-evaluates so hold keeps converging.
            TestAssert.That(ControlFieldCore.TryElapsed(107f, 105f, true, false, out elapsed, out consumed),
                "an unchanged snapshot past the window re-evaluates");
            TestAssert.That(elapsed == 2f && consumed == 107f, "the preserved interval advances whole");

            // A clock step backwards re-evaluates from zero instead of stalling.
            TestAssert.That(ControlFieldCore.TryElapsed(10f, 107f, true, false, out elapsed, out consumed),
                "a rewound clock re-evaluates");
            TestAssert.That(elapsed == 0f && consumed == 10f, "a rewound clock carries no phantom interval");
        }
    }
}
