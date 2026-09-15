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
            KilometresReadsAsDistance();
            RailsSeparateHoldingFromLosing();
            PressureStateDoesNotOverstateNoise();
            RosterDrawsTheChainOfCommand();
            RosterSurvivesAMissingParent();
        }

        private static void RosterDrawsTheChainOfCommand()
        {
            // The generated staff: one theater commander, two component commanders, and
            // three base commanders that answer to components 2 and 1 alternately. The
            // host lists slots in generation order, which is not reporting order, so the
            // console has to re-order before its trunk lines mean anything.
            int[] ids = { 0, 1, 2, 3, 4, 5 };
            int[] parents = { -1, 0, 0, 2, 1, 2 };
            int[] order = new int[6];
            bool[] visited = new bool[6];
            int written = CommandRosterOrder.Sort(ids.Length, ids, parents, order, visited);

            TestAssert.That(written == 6, "every post is placed");
            TestAssert.That(order[0] == 0, "the theater commander leads the chain");
            TestAssert.That(Before(order, 1, 4), "a component is drawn above the base that answers to it");
            TestAssert.That(Before(order, 2, 3) && Before(order, 2, 5),
                "the other component is drawn above its own bases");
            TestAssert.That(Before(order, 4, 2),
                "one component's whole branch is drawn before the next component");
            TestAssert.That(Before(order, 1, 2), "posts that share a parent keep their given order");
            TestAssert.That(EachOnce(order), "no post is drawn twice");
        }

        private static void RosterSurvivesAMissingParent()
        {
            // An absent parent is a root; a cycle the host does not produce still has to
            // terminate with every post placed exactly once.
            int[] ids = { 7, 8, 9 };
            int[] parents = { 99, 9, 8 };
            int[] order = new int[3];
            bool[] visited = new bool[3];
            int written = CommandRosterOrder.Sort(3, ids, parents, order, visited);

            TestAssert.That(written == 3, "a cycle still places every post");
            TestAssert.That(order[0] == 0, "a post whose parent is absent leads its own branch");
            TestAssert.That(EachOnce(order), "a cycle does not duplicate a post");
            TestAssert.That(CommandRosterOrder.Sort(0, ids, parents, order, visited) == 0,
                "an empty roster writes nothing");
            TestAssert.That(CommandRosterOrder.Sort(3, null, parents, order, visited) == 0,
                "a null roster is not a crash");
        }

        private static bool Before(int[] order, int first, int second) =>
            IndexOf(order, first) < IndexOf(order, second);

        private static int IndexOf(int[] order, int value)
        {
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == value) return i;
            }
            return int.MaxValue;
        }

        private static bool EachOnce(int[] order)
        {
            for (int i = 0; i < order.Length; i++)
            {
                for (int j = i + 1; j < order.Length; j++)
                {
                    if (order[i] == order[j]) return false;
                }
            }
            return true;
        }

        private static void PressureStateDoesNotOverstateNoise()
        {
            TestAssert.That(TheaterReadout.PressureState(0.01f) == "HOLDING",
                "a rounding artefact is not pressure");
            TestAssert.That(TheaterReadout.PressureState(0.4f) == "UNDER PRESSURE",
                "real pressure is named, with its number in the figure column");
            TestAssert.That(TheaterReadout.PressureState(0.9f) == "FALLING",
                "a node about to change hands says so");
            TestAssert.That(TheaterReadout.PressureState(float.NaN) == "UNKNOWN",
                "an unreadable balance is unknown, not holding");
        }


        private static void KilometresReadsAsDistance()
        {
            TestAssert.That(TheaterReadout.Kilometres(412500f) == "412.5 km",
                "metres become one-decimal kilometres");
            TestAssert.That(TheaterReadout.Kilometres(0f) == "0.0 km",
                "zero is a measured distance, not a dash");
            TestAssert.That(TheaterReadout.Kilometres(float.NaN) == "—",
                "an unknown front length is a dash");
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
