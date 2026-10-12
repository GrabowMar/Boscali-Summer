using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The ground report as the snapshot carries it: unknown stays unknown, every number stays inside the wire bounds, marks stay finite (annex 01 E4, annex 02 G9).</summary>
    internal static class ForcesBuilderTests
    {
        public static void Run()
        {
            AnUnavailableReportIsAllUnknown();
            AMeasuredReportIsCopied();
            NumbersAreClampedToTheWireBounds();
            MarksAreFiniteAtMostEightAndInRange();
            AMissingMarkBufferIsNoMarks();
            PhaseInputFollowsTheReport();
        }

        private static GroundOpReport Measured() => new GroundOpReport
        {
            Reason = GroundReason.Ready, Groups = 3, Vehicles = 14, Formed = 18, Rally = 1, Line = 1, Assault = 1, Contact = 0, Withdraw = 0,
            Moving = 2, Steered = 12, OnSlot = 9, Stalled = 1, EtaSeconds = 240, NearestToFixMeters = 3200f,
        };

        private static void AnUnavailableReportIsAllUnknown()
        {
            TheaterForcesView v = ForcesBuilder.Of(GroundOpReport.Unavailable(GroundReason.TacticsOff), null, 0);
            bool unknown = v.Groups == 255 && v.Vehicles == 255 && v.Formed == 255 && v.Rally == 255 && v.Line == 255 && v.Assault == 255 &&
                v.Contact == 255 && v.Withdraw == 255 && v.Moving == 255 && v.Steered == 255 && v.OnSlot == 255 && v.Stalled == 255;
            TestAssert.That(unknown, "every count is 255, never a confident zero");
            TestAssert.Eq(v.EtaSeconds, (ushort)65535, "eta unknown is 65535");
            TestAssert.Near(v.NearestToFixMeters, -1f, "distance unknown is -1");
            TestAssert.Eq(v.Marks.Length, 0, "no marks");
        }

        private static void AMeasuredReportIsCopied()
        {
            TheaterForcesView v = ForcesBuilder.Of(Measured(), null, 0);
            TestAssert.Eq(v.Groups, (byte)3, "groups");
            TestAssert.Eq(v.Vehicles, (byte)14, "vehicles");
            TestAssert.Eq(v.Formed, (byte)18, "formed");
            TestAssert.Eq(v.Rally, (byte)1, "rally");
            TestAssert.Eq(v.Assault, (byte)1, "assault");
            TestAssert.Eq(v.Contact, (byte)0, "a measured zero stays zero");
            TestAssert.Eq(v.Steered, (byte)12, "steered");
            TestAssert.Eq(v.OnSlot, (byte)9, "on slot");
            TestAssert.Eq(v.Stalled, (byte)1, "stalled");
            TestAssert.Eq(v.EtaSeconds, (ushort)240, "eta");
            TestAssert.Near(v.NearestToFixMeters, 3200f, "nearest");
        }

        private static void NumbersAreClampedToTheWireBounds()
        {
            GroundOpReport r = Measured();
            r.Groups = 99; r.Rally = 40; r.Vehicles = 400; r.Formed = 193; r.Moving = 500; r.Steered = 192; r.EtaSeconds = 99999; r.NearestToFixMeters = 5e9f;
            TheaterForcesView v = ForcesBuilder.Of(r, null, 0);
            TestAssert.Eq(v.Groups, (byte)32, "groups clamp at 32");
            TestAssert.Eq(v.Rally, (byte)32, "stage counts clamp at 32");
            TestAssert.Eq(v.Vehicles, (byte)192, "vehicles clamp at 192");
            TestAssert.Eq(v.Formed, (byte)192, "formed clamps at 192");
            TestAssert.Eq(v.Moving, (byte)192, "moving clamps at 192");
            TestAssert.Eq(v.Steered, (byte)192, "192 is legal");
            TestAssert.Eq(v.EtaSeconds, (ushort)3600, "eta clamps at an hour");
            TestAssert.Near(v.NearestToFixMeters, 1e7f, "distance clamps at 1e7");
            r.NearestToFixMeters = float.NaN; r.EtaSeconds = int.MinValue;
            v = ForcesBuilder.Of(r, null, 0);
            TestAssert.Near(v.NearestToFixMeters, -1f, "a NaN distance is unknown");
            TestAssert.Eq(v.EtaSeconds, (ushort)65535, "a negative eta is unknown");
        }

        private static void MarksAreFiniteAtMostEightAndInRange()
        {
            var marks = new GroundMark[11];
            for (int i = 0; i < marks.Length; i++) marks[i] = new GroundMark(100f * i, -50f * i, GroundStage.Line, 6);
            marks[1] = new GroundMark(float.NaN, 0f, GroundStage.Line, 6);
            marks[2] = new GroundMark(0f, float.PositiveInfinity, GroundStage.Line, 6);
            marks[3] = new GroundMark(2e6f, 0f, GroundStage.Line, 6);
            TheaterForcesView v = ForcesBuilder.Of(Measured(), marks, 11);
            TestAssert.Eq(v.Marks.Length, 8, "at most eight marks");
            TestAssert.Near(v.Marks[0].X, 0f, "the first mark is kept");
            TestAssert.Near(v.Marks[1].X, 400f, "marks 1, 2 and 3 are dropped for a NaN, an infinity and a coordinate past a million metres");
            bool inRange = true;
            foreach (TheaterForceMark m in v.Marks) inRange &= m.Stage <= 5 && m.Alive <= 6 && float.IsFinite(m.X) && float.IsFinite(m.Z);
            TestAssert.That(inRange, "every mark is finite with stage 0..5 and alive 0..6");
            var wild = new[] { new GroundMark(1f, 2f, (GroundStage)9, 200) };
            v = ForcesBuilder.Of(Measured(), wild, 1);
            TestAssert.Eq(v.Marks[0].Stage, (byte)5, "a stage past the enum clamps");
            TestAssert.Eq(v.Marks[0].Alive, (byte)6, "alive clamps at six");
            TestAssert.Eq(ForcesBuilder.Of(Measured(), marks, 3).Marks.Length, 1, "the count argument limits the read");
        }

        private static void AMissingMarkBufferIsNoMarks()
        {
            TestAssert.Eq(ForcesBuilder.Of(Measured(), null, 5).Marks.Length, 0, "a null buffer is no marks");
            TestAssert.Eq(ForcesBuilder.Of(Measured(), new GroundMark[2], -3).Marks.Length, 0, "a negative count is no marks");
            TestAssert.Eq(ForcesBuilder.Of(Measured(), new GroundMark[2], 9).Marks.Length, 2, "a count past the buffer reads the buffer");
        }

        private static void PhaseInputFollowsTheReport()
        {
            PhaseInput p = ForcesBuilder.PhaseOf(Measured(), true);
            TestAssert.That(p.HaveReport && p.Steering, "a measured Ready report is a steering report");
            TestAssert.Eq(p.Groups, 3, "groups"); TestAssert.Eq(p.Rally, 1, "rally"); TestAssert.Eq(p.Assault, 1, "assault");
            TestAssert.Near(p.NearestToFixMeters, 3200f, "nearest");
            p = ForcesBuilder.PhaseOf(GroundOpReport.Unavailable(GroundReason.TacticsOff), true);
            TestAssert.That(p.HaveReport && !p.Steering, "tactics off is a report that says not steering");
            p = ForcesBuilder.PhaseOf(GroundOpReport.Unavailable(GroundReason.Pending), false);
            TestAssert.That(!p.HaveReport, "no report is no report");
        }
    }
}
