using BoscaliSummer.Features.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// How the OPERATIONS page divides its bay between the target list and the reinforcement
    /// list. The page is Unity UI, but this arithmetic is not, and it is the arithmetic that
    /// decides whether a bay is filled or left half empty.
    /// </summary>
    internal static class OperationsBoardTests
    {
        private const float Pitch = 44f;
        private const float PitchMax = 56f;

        public static void Run()
        {
            ARoomyBayHoldsAFullPage();
            AShortBayScrollsInsteadOfStubbing();
            TheSplitNeverOverfillsTheBay();
            ThePitchFillsSlackButNeverUnbounded();
            DegenerateHeightsAreSurvivable();
        }

        private static void ARoomyBayHoldsAFullPage()
        {
            // The bay a 1080p-ish panel gives the lists after the cards and the readiness block.
            const float space = 272f;
            bool fits = OperationsBoardFit.Fits(space, Pitch);
            int rows = OperationsBoardFit.Rows(space, Pitch, fits);
            int targets = OperationsBoardFit.Targets(rows);
            int reinforce = OperationsBoardFit.Reinforce(rows, targets);
            float pitch = OperationsBoardFit.Pitch(space, targets + reinforce, Pitch, PitchMax);

            TestAssert.That(fits, "a bay with room for six rows takes the page");
            TestAssert.That(rows == 6, "six rows is what the bay holds");
            TestAssert.That(targets + reinforce == rows,
                "the split spends every row the bay holds instead of reserving a bay it will not fill");
            TestAssert.That(targets >= 2 && reinforce >= 2,
                "neither list is squeezed to a single row");
            TestAssert.That(pitch >= Pitch && pitch <= PitchMax,
                "the pitch spreads into the slack without leaving the list looking unlike a list");
        }

        private static void AShortBayScrollsInsteadOfStubbing()
        {
            // A short bay gets the full windows and scrolls: a page that silently showed two
            // rows would be hiding objectives the player has to choose from.
            const float space = 100f;
            bool fits = OperationsBoardFit.Fits(space, Pitch);
            TestAssert.That(!fits, "a bay that cannot hold the minimum does not claim to fit");
            TestAssert.That(OperationsBoardFit.Rows(space, Pitch, fits) ==
                            OperationsBoardFit.TargetMaximum + OperationsBoardFit.ReinforceMaximum,
                "a short bay builds the full windows and scrolls");
        }

        private static void TheSplitNeverOverfillsTheBay()
        {
            for (float space = 150f; space <= 900f; space += 7f)
            {
                bool fits = OperationsBoardFit.Fits(space, Pitch);
                int rows = OperationsBoardFit.Rows(space, Pitch, fits);
                int targets = OperationsBoardFit.Targets(rows);
                int reinforce = OperationsBoardFit.Reinforce(rows, targets);

                TestAssert.That(targets <= OperationsBoardFit.TargetMaximum,
                    "the target list stays inside its ceiling at " + space + "px");
                TestAssert.That(reinforce <= OperationsBoardFit.ReinforceMaximum,
                    "the reinforcement list stays inside its ceiling at " + space + "px");
                TestAssert.That(targets + reinforce <= rows,
                    "the split never claims more rows than the page has at " + space + "px");
                if (fits)
                    TestAssert.That(rows >= OperationsBoardFit.RowMinimum,
                        "a bay that fits holds at least the minimum at " + space + "px");
            }
        }

        private static void ThePitchFillsSlackButNeverUnbounded()
        {
            TestAssert.That(OperationsBoardFit.Pitch(900f, 6, Pitch, PitchMax) == PitchMax,
                "a very tall bay spreads to the cap and no further");
            TestAssert.That(OperationsBoardFit.Pitch(200f, 6, Pitch, PitchMax) == Pitch,
                "a bay with no slack keeps the base pitch rather than squashing the rows");
            TestAssert.That(OperationsBoardFit.Pitch(272f, 4, Pitch, PitchMax) == PitchMax,
                "two rows' worth of slack over a short list spreads, capped");
        }

        private static void DegenerateHeightsAreSurvivable()
        {
            foreach (float space in new[] { -50f, 0f, 1f })
            {
                bool fits = OperationsBoardFit.Fits(space, Pitch);
                int rows = OperationsBoardFit.Rows(space, Pitch, fits);
                float pitch = OperationsBoardFit.Pitch(space, rows, Pitch, PitchMax);

                TestAssert.That(!fits, "a collapsed bay is not a fit at " + space + "px");
                TestAssert.That(rows > 0, "a collapsed bay still builds the scrollable page at " + space + "px");
                TestAssert.That(pitch == Pitch, "a collapsed bay divides by nothing at " + space + "px");
            }

            TestAssert.That(OperationsBoardFit.Pitch(272f, 0, Pitch, PitchMax) == Pitch,
                "an empty board keeps the base pitch instead of dividing by zero");
        }
    }
}
