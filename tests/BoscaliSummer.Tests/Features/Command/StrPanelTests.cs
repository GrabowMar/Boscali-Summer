using BoscaliSummer.Features.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// The arithmetic and classification behind the STR panel's readouts.
    ///
    /// <para>These are the parts a screenshot cannot check: whether a share bar adds up,
    /// whether an unknown reads as unknown instead of as zero, and whether a sortie is
    /// filed under what it is attacking rather than what it happens to be firing.</para>
    /// </summary>
    internal static class StrPanelTests
    {
        public static void Run()
        {
            ClassifiesByWhatIsBeingAttacked();
            AnUnseenPilotIsNotAnIdleOne();
            TallyCountsWhatItObserved();
            SharesAccountForContestedGround();
            SharesSurviveAnEmptyBoard();
            PercentRefusesToInventPrecision();
            CountdownReadsAsTimeLeft();
            CompactKeepsLargeFiguresNarrow();
            PressureDoesNotOverstateNoise();
            RailsSeparateHoldingFromLosing();
        }

        private static void ClassifiesByWhatIsBeingAttacked()
        {
            TestAssert.That(SortieClassifier.Classify(SortieTarget.Aircraft) == SortieRole.Cap,
                "an aircraft target is a combat air patrol");
            TestAssert.That(SortieClassifier.Classify(SortieTarget.Emitter) == SortieRole.Sead,
                "a radar target is SEAD, not a strike");
            TestAssert.That(SortieClassifier.Classify(SortieTarget.Vehicle) == SortieRole.Cas,
                "a vehicle target is close air support");
            TestAssert.That(SortieClassifier.Classify(SortieTarget.Infantry) == SortieRole.Cas,
                "infantry is close air support too");
            TestAssert.That(SortieClassifier.Classify(SortieTarget.Ship) == SortieRole.Strike,
                "a ship target is a strike");
            TestAssert.That(SortieClassifier.Classify(SortieTarget.Structure) == SortieRole.Strike,
                "a structure target is a strike");
        }

        private static void AnUnseenPilotIsNotAnIdleOne()
        {
            // A jet with no assigned target is in transit. That is a real state, and it is
            // distinct from a jet whose state could not be read at all -- which never
            // reaches the tally, so Observed stays zero and the board says so.
            TestAssert.That(SortieClassifier.Classify(SortieTarget.None) == SortieRole.Transit,
                "no target means transit");

            var tally = default(SortieTally);
            TestAssert.That(tally.Observed == 0, "an untouched tally has observed nothing");
            TestAssert.That(tally.Tasked == 0, "an untouched tally has tasked nothing");
        }

        private static void TallyCountsWhatItObserved()
        {
            var tally = default(SortieTally);
            tally.Add(SortieRole.Cap);
            tally.Add(SortieRole.Cap);
            tally.Add(SortieRole.Sead);
            tally.Add(SortieRole.Transit);

            TestAssert.That(tally.Cap == 2, "two patrols counted");
            TestAssert.That(tally.Of(SortieRole.Sead) == 1, "one SEAD sortie counted");
            TestAssert.That(tally.Observed == 4, "every added sortie was observed");
            TestAssert.That(tally.Tasked == 3, "transit is airborne, not tasked");

            tally.Reset();
            TestAssert.That(tally.Observed == 0 && tally.Cap == 0, "reset clears the board");
        }

        private static void SharesAccountForContestedGround()
        {
            // 40 held, 40 lost and 20 being fought over must not read the same as a
            // straight 50/50 split, which is exactly what a single friendly-over-total
            // fill would have shown.
            TheaterReadout.Shares(40, 20, 40, 0, out float friendly, out float contested,
                                  out float hostile);

            TestAssert.That(Near(friendly, 0.4f), "friendly share is of the whole board");
            TestAssert.That(Near(contested, 0.2f), "contested ground is its own band");
            TestAssert.That(Near(hostile, 0.4f), "hostile share is of the whole board");
            TestAssert.That(Near(friendly + contested + hostile, 1f), "the bands fill the bar");

            TheaterReadout.Shares(10, 0, 10, 80, out friendly, out contested, out hostile);
            TestAssert.That(Near(friendly + contested + hostile, 0.2f),
                "unclaimed ground leaves the bar deliberately unfilled");
        }

        private static void SharesSurviveAnEmptyBoard()
        {
            TheaterReadout.Shares(0, 0, 0, 0, out float friendly, out float contested,
                                  out float hostile);
            TestAssert.That(friendly == 0f && contested == 0f && hostile == 0f,
                "a board with no sectors divides nothing by nothing and stays at zero");

            TheaterReadout.Shares(-5, 0, 5, 0, out friendly, out _, out hostile);
            TestAssert.That(friendly == 0f && Near(hostile, 1f),
                "a negative count cannot bend the bar backwards");
        }

        private static void PercentRefusesToInventPrecision()
        {
            TestAssert.That(TheaterReadout.Percent(0.5f) == "50%", "a half reads as 50%");
            TestAssert.That(TheaterReadout.Percent(0f) == "0%", "zero reads as zero");
            TestAssert.That(TheaterReadout.Percent(1f) == "100%", "full reads as 100%");
            TestAssert.That(TheaterReadout.Percent(2f) == "100%", "over-full clamps");
            TestAssert.That(TheaterReadout.Percent(float.NaN) == "—",
                "an unknown ratio reads as a dash, never as 0%");
            TestAssert.That(TheaterReadout.Percent(float.PositiveInfinity) == "—",
                "an infinite ratio reads as a dash");
        }

        private static void CountdownReadsAsTimeLeft()
        {
            TestAssert.That(TheaterReadout.Countdown(45f) == "T-45s", "under a minute counts seconds");
            TestAssert.That(TheaterReadout.Countdown(90f) == "T-1:30", "over a minute counts minutes");
            TestAssert.That(TheaterReadout.Countdown(600f) == "T-10:00", "ten minutes pads its seconds");
            TestAssert.That(TheaterReadout.Countdown(0f) == "EXPIRED", "no time left is expired");
            TestAssert.That(TheaterReadout.Countdown(-5f) == "EXPIRED", "past the deadline is expired");
            TestAssert.That(TheaterReadout.Countdown(float.NaN) == "—", "an unknown deadline is a dash");
        }

        private static void CompactKeepsLargeFiguresNarrow()
        {
            TestAssert.That(TheaterReadout.Compact(950f) == "950", "small figures stay exact");
            TestAssert.That(TheaterReadout.Compact(12_400f) == "12.4K", "thousands compress");
            TestAssert.That(TheaterReadout.Compact(4_200_000f) == "4.2M", "millions compress");
            TestAssert.That(TheaterReadout.Compact(float.NaN) == "—", "an unknown figure is a dash");
        }

        private static void PressureDoesNotOverstateNoise()
        {
            TestAssert.That(TheaterReadout.Pressure(0.01f) == "HOLDING",
                "a rounding artefact is not pressure");
            TestAssert.That(TheaterReadout.Pressure(0.4f).StartsWith("PRESSURE"),
                "real pressure is named and quantified");
            TestAssert.That(TheaterReadout.Pressure(0.9f).StartsWith("FALLING"),
                "a node about to change hands says so");
        }

        private static void RailsSeparateHoldingFromLosing()
        {
            TestAssert.That(TheaterReadout.NodeRail(true, false) == "ready",
                "quiet friendly ground is nominal");
            TestAssert.That(TheaterReadout.NodeRail(true, true) == "contested",
                "friendly ground under pressure is contested, not merely ours");
            TestAssert.That(TheaterReadout.NodeRail(false, false) == "hostile",
                "quiet enemy ground is hostile");

            // DEFCON runs the other way: 1 is worst. The rail has to follow the meaning,
            // not the number.
            TestAssert.That(TheaterReadout.DefconRail(1) == "danger", "DEFCON 1 is the alarm");
            TestAssert.That(TheaterReadout.DefconRail(4) == "ready", "DEFCON 4 is nominal");
            TestAssert.That(TheaterReadout.DefconRail(0) == "danger", "below the scale is still the alarm");
        }

        private static bool Near(float a, float b) => a - b < 0.0005f && b - a < 0.0005f;
    }
}
