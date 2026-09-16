using BoscaliSummer.Features.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    /// <summary>
    /// The copy rules every contract surface shares. Locale-proof: no assertion depends on
    /// grouped digits or on a decimal separator.
    /// </summary>
    internal static class OperationMarkerCopyTests
    {
        public static void Run()
        {
            FamiliesAreShortAndComplete();
            TitleAndDetailCarryTheContract();
            UnknownPartsAreDroppedNeverGuessed();
            ToneFollowsReturnAndTheClock();
            ApproachAndBarStayInRange();
        }

        private static void FamiliesAreShortAndComplete()
        {
            foreach (OperationKind kind in (OperationKind[])System.Enum.GetValues(typeof(OperationKind)))
            {
                string family = OperationTitles.Family(kind);
                TestAssert.That(!string.IsNullOrEmpty(family), "every kind has a family word: " + kind);
                TestAssert.That(family.Length <= 6, "a family word must fit the detail line: " + kind + " -> " + family);
                TestAssert.That(family == family.ToUpperInvariant(), "families print uppercase: " + family);
            }
            TestAssert.That(OperationTitles.FamilyFor(OperationTitles.Title(OperationKind.Rescue)) == "RESCUE",
                "a wire title maps back to its family");
            TestAssert.That(OperationTitles.FamilyFor("NOT A CONTRACT") == "CONTRACT",
                "an unknown title still prints a family word");
        }

        private static void TitleAndDetailCarryTheContract()
        {
            TestAssert.That(OperationMarkerCopy.Title(5, "SURVEY THE AFTERMATH") == "#5 SURVEY THE AFTERMATH",
                "the title line is the board's number and name");
            TestAssert.That(OperationMarkerCopy.Title(5, null) == "#5 SECONDARY OBJECTIVE",
                "a missing title still names the contract");

            string approach = OperationMarkerCopy.Detail("RECON", 20400f, 1500f, 161f, 0.2f, MarkerField.Approach);
            TestAssert.That(approach == "RECON · 20.4 KM TO AREA · T-2:41",
                "an approaching contract prints family, distance and clock: " + approach);
            string hold = OperationMarkerCopy.Detail("HOLD", 400f, 1500f, 161f, 0.42f, MarkerField.Hold);
            TestAssert.That(hold == "HOLD · HOLD 42% · T-2:41", "a held contract prints its hold progress: " + hold);
            string deliver = OperationMarkerCopy.Detail("RECON", 400f, 1500f, 59f, 1f, MarkerField.Deliver);
            TestAssert.That(deliver == "RECON · LAND TO DELIVER · T-59s",
                "a returning contract says what it wants: " + deliver);
        }

        private static void UnknownPartsAreDroppedNeverGuessed()
        {
            TestAssert.That(OperationMarkerCopy.Distance(float.NaN) == "—", "an unknown distance is not a number");
            TestAssert.That(OperationMarkerCopy.Distance(-5f) == "—", "a negative distance is not printed");
            TestAssert.That(OperationMarkerCopy.Clock(float.PositiveInfinity) == "", "an unknown clock prints nothing");
            TestAssert.That(OperationMarkerCopy.Detail("STRIKE", float.NaN, 0f, float.NaN, 0f, MarkerField.Approach) == "STRIKE",
                "with nothing known the family still prints");
            TestAssert.That(OperationMarkerCopy.Distance(820f) == "820 M", "sub-kilometre reads in metres");
            TestAssert.That(OperationMarkerCopy.Distance(1000f) == "1.0 KM", "a kilometre reads in kilometres");
        }

        private static void ToneFollowsReturnAndTheClock()
        {
            TestAssert.That(OperationMarkerCopy.Tone("ACTIVE / RETURN TO BASE", 900f) == MarkerTone.Ready,
                "a returning contract reads ready");
            TestAssert.That(OperationMarkerCopy.Tone("ACTIVE", 119f) == MarkerTone.Caution,
                "a contract inside its last two minutes reads caution");
            TestAssert.That(OperationMarkerCopy.Tone("ACTIVE", OperationMarkerCopy.UrgentSeconds) == MarkerTone.Caution,
                "the urgency boundary is inclusive");
            TestAssert.That(OperationMarkerCopy.Tone("ACTIVE", 0f) == MarkerTone.Info,
                "a contract at its deadline is not treated as urgent");
            TestAssert.That(OperationMarkerCopy.Tone("ACTIVE", 0.01f) == MarkerTone.Caution,
                "a hair of clock left is urgent");
            TestAssert.That(OperationMarkerCopy.Tone("ACTIVE", float.NaN) == MarkerTone.Info,
                "an unknown clock is never urgent");
            TestAssert.That(OperationMarkerCopy.Tone(null, 600f) == MarkerTone.Info,
                "a missing status is not a return");
        }

        private static void ApproachAndBarStayInRange()
        {
            const float radius = 1500f;
            TestAssert.That(OperationMarkerCopy.VicinityBand(radius) == radius + 20000f,
                "the vicinity floor dominates a small area");
            TestAssert.That(OperationMarkerCopy.VicinityBand(0f) == OperationMarkerCopy.VicinityFloorMetres,
                "a point target still has a floor");
            TestAssert.That(OperationMarkerCopy.Approach(radius, radius) == 1f, "the area edge is full");
            TestAssert.That(OperationMarkerCopy.Approach(radius + 20000f, radius) == 0f, "the band edge is empty");
            TestAssert.That(OperationMarkerCopy.Approach(radius + 10000f, radius) == 0.5f, "the band is linear");
            TestAssert.That(OperationMarkerCopy.Approach(float.NaN, radius) == 0f,
                "an unknown distance is never full");
        }
    }
}
