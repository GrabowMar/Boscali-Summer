using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// The STR revamp's pure core: the corrected control-field rule (COREControl), the
    /// operations-board view model, and the room-plot layout. Every assertion here is a
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
            OperationsClassifyFromTheGround();
            PostureAndCopyReadPlainly();
            SectorWindowsStayBounded();
            RoomLayoutSurvivesDegeneratePanels();
            TracesDecimateInsideTheCeiling();
            TargetsResolveOnlyFromPosition();
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

        private static void OperationsClassifyFromTheGround()
        {
            TestAssert.That(StrOperationsBoard.ClassifyOperation(0.2f, false, 0f, 0f, 120f) ==
                StrOperationPhase.Forming, "unformed groups are still forming");
            TestAssert.That(StrOperationsBoard.ClassifyOperation(0.8f, false, 0f, 0f, 120f) ==
                StrOperationPhase.OnLine, "formed groups without contact hold the line");
            TestAssert.That(StrOperationsBoard.ClassifyOperation(0.8f, true, 0f, 30f, 120f) ==
                StrOperationPhase.InContact, "contact without movement is contact, not a stall");
            TestAssert.That(StrOperationsBoard.ClassifyOperation(0.8f, true, 0.1f, 30f, 120f) ==
                StrOperationPhase.Advancing, "contact with hold gain is an advance");
            TestAssert.That(StrOperationsBoard.ClassifyOperation(0.8f, true, 0f, 300f, 120f) ==
                StrOperationPhase.Stalled, "contact without movement past the bound is a stall");
            TestAssert.That(StrOperationsBoard.ClassifyOperation(0.8f, true, float.NaN, 30f, 120f) ==
                StrOperationPhase.InContact, "unknown trend never reads as advancing");
            TestAssert.That(StrOperationsBoard.ClassifyOperation(float.NaN, true, 0.5f, 0f, 120f) ==
                StrOperationPhase.Forming, "an unreadable formed share never leaves forming");
            TestAssert.That(StrOperationsBoard.FinishOperation(true) == StrOperationPhase.Consolidating,
                "a secured objective consolidates");
            TestAssert.That(StrOperationsBoard.FinishOperation(false) == StrOperationPhase.Repulsed,
                "a spent push without the objective is repulsed");
        }

        private static void PostureAndCopyReadPlainly()
        {
            TestAssert.That(StrOperationsBoard.PostureWord(StrPosture.Cautious) == "CAUTIOUS",
                "caution reads cautious");
            TestAssert.That(StrOperationsBoard.PostureWord(StrPosture.Bold) == "BOLD",
                "boldness reads bold");
            TestAssert.That(StrOperationsBoard.PostureWord(StrPosture.Steady) == "STEADY",
                "the default reads steady");
            TestAssert.That(StrOperationsBoard.PostureWord((StrPosture)99) == "STEADY",
                "an unrecognised posture reads steady, never invented aggression");
            TestAssert.That(StrOperationsBoard.GroundPhaseWord(StrOperationPhase.OnLine) == "ON LINE",
                "the line phase reads as two words");
            TestAssert.That(StrOperationsBoard.TrendGlyph(0.05f) == "▲", "rising hold points up");
            TestAssert.That(StrOperationsBoard.TrendGlyph(-0.05f) == "▼", "falling hold points down");
            TestAssert.That(StrOperationsBoard.TrendGlyph(0.001f) == "■", "noise reads as holding");
            TestAssert.That(StrOperationsBoard.TrendGlyph(float.NaN) == "—",
                "an unreadable trend is a dash, never steady");
            TestAssert.That(StrOperationsBoard.OwnerWord(ControlState.Neutral) == "OPEN",
                "unclaimed ground reads open");
            TestAssert.That(StrOperationsBoard.OwnerWord(ControlState.Contested) == "CONTESTED",
                "fought ground reads contested");
        }

        private static void SectorWindowsStayBounded()
        {
            TestAssert.That(StrOperationsBoard.SectorRows(20) == StrOperationsBoard.MaxSectors,
                "sector rows stop at the window");
            TestAssert.That(StrOperationsBoard.SectorRows(-3) == 0, "a negative sector count draws nothing");
            TestAssert.That(StrOperationsBoard.OperationCards(5) == StrOperationsBoard.MaxOperations,
                "operation cards stop at the window");
            StrSectorRow row = StrOperationsBoard.BuildSectorRow(3, ControlState.Hostile, -0.1f, 0.7f, true, true);
            TestAssert.That(row.SectorIndex == 3 && row.Owner == ControlState.Hostile,
                "a sector row carries its index and owner");
            StrSectorRow clamped = StrOperationsBoard.BuildSectorRow(-9, ControlState.Friendly, 0f, 0f, false, false);
            TestAssert.That(clamped.SectorIndex == 0, "a negative sector index clamps instead of crashing");
        }

        private static void RoomLayoutSurvivesDegeneratePanels()
        {
            RoomPlotModel.Layout(400f, 300f, out PlotRect map, out PlotRect legend);
            TestAssert.That(!map.IsEmpty && !legend.IsEmpty, "a roomy panel holds a map and a legend");
            TestAssert.That(map.Width == 400f - 16f && legend.Height == 24f,
                "padding and the legend strip come off the panel exactly once");
            RoomPlotModel.Layout(400f, 30f, out map, out legend);
            TestAssert.That(!map.IsEmpty && legend.IsEmpty, "a short panel keeps the map and drops the legend");
            foreach (float bad in new[] { -50f, 0f, 1f, float.NaN, float.PositiveInfinity })
            {
                RoomPlotModel.Layout(bad, 300f, out map, out legend);
                TestAssert.That(map.IsEmpty && legend.IsEmpty, "a collapsed width divides nothing at " + bad + "px");
                RoomPlotModel.Layout(400f, bad, out map, out legend);
                TestAssert.That(map.IsEmpty && legend.IsEmpty, "a collapsed height divides nothing at " + bad + "px");
            }
        }

        private static void TracesDecimateInsideTheCeiling()
        {
            var source = new PlotPoint[1000];
            for (int i = 0; i < source.Length; i++) source[i] = new PlotPoint(i, -i);
            var plot = new PlotPoint[RoomPlotModel.MaxPlotPoints];
            int written = RoomPlotModel.Decimate(source, source.Length, plot);
            TestAssert.That(written > 0 && written <= RoomPlotModel.MaxPlotPoints,
                "a long trace decimates inside the ceiling");
            TestAssert.That(plot[0].X == 0f && plot[written - 1].X > plot[0].X,
                "decimation keeps the trace's order and reach");

            var short_ = new PlotPoint[40];
            for (int i = 0; i < short_.Length; i++) short_[i] = new PlotPoint(i, i);
            TestAssert.That(RoomPlotModel.Decimate(short_, short_.Length, plot) == 40,
                "a short trace draws whole");
            TestAssert.That(RoomPlotModel.Decimate(null, 10, plot) == 0, "a null trace draws nothing");
            TestAssert.That(RoomPlotModel.Decimate(short_, short_.Length, null) == 0,
                "a null plot buffer writes nothing");
            TestAssert.That(RoomPlotModel.Decimate(short_, 0, plot) == 0, "an empty trace writes nothing");
            TestAssert.That(RoomPlotModel.ArrowCount(9) == RoomPlotModel.MaxArrows,
                "axis arrows stop at the ceiling");
            TestAssert.That(RoomPlotModel.ArrowCount(-2) == 0, "a negative arrow ask draws nothing");
        }

        private static void TargetsResolveOnlyFromPosition()
        {
            TestAssert.That(RoomPlotModel.ResolveTarget(true, 12000f, -4000f),
                "an explicit finite position resolves");
            TestAssert.That(!RoomPlotModel.ResolveTarget(false, 12000f, -4000f),
                "no carried position means no marker, whatever the label says");
            TestAssert.That(!RoomPlotModel.ResolveTarget(true, float.NaN, 0f),
                "an unreadable easting resolves nothing");
            TestAssert.That(!RoomPlotModel.ResolveTarget(true, 0f, float.PositiveInfinity),
                "an infinite northing resolves nothing");
        }
    }
}
