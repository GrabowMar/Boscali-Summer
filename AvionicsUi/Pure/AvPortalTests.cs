using System;

namespace NOAvionics.Tests
{
    /// <summary>Engine-free tests for the Portal widget geometry.</summary>
    public static class AvPortalTests
    {
        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));
            var stripes = AvPortalMath.HazardStripes(0f, 0f, 100f, 10f, 8f);
            assert(stripes.Count >= 12, "hazard stripes cover the width (" + stripes.Count + ")");
            bool inside = true;
            foreach (AvV2[] s in stripes)
                foreach (AvV2 p in s) if (p.X < -0.001f || p.X > 100.001f || p.Y < -0.001f || p.Y > 10.001f) inside = false;
            assert(inside, "hazard stripes are clipped to the rectangle");
            assert(AvPortalMath.HazardStripes(0f, 0f, 0f, 10f, 8f).Count == 0, "empty rectangle yields no stripes");
            AvV2[] miss = AvPortalMath.ClipToRect(new[] { new AvV2(20f, 0f), new AvV2(30f, 0f), new AvV2(30f, 5f) }, 0f, 0f, 10f, 10f);
            assert(miss.Length == 0, "polygon fully outside clips away");
            assert(AvPortalMath.HeatRows(24, 8) == 3 && AvPortalMath.HeatRows(25, 8) == 4, "heat rows round up");
            AvV2 first = AvPortalMath.HeatCell(0, 8, 10f, 9f, 2f, 31f), last = AvPortalMath.HeatCell(23, 8, 10f, 9f, 2f, 31f);
            assert(first.X == 0f && Math.Abs(first.Y - 22f) < 0.001f, "heat cell 0 is top-left");
            assert(Math.Abs(last.X - 84f) < 0.001f && Math.Abs(last.Y) < 0.001f, "heat cell 23 is bottom-right");
            assert(AvPortalMath.Serial("OPS") == AvPortalMath.Serial("OPS") && AvPortalMath.Serial("OPS") != AvPortalMath.Serial("RAD") && AvPortalMath.Serial("OPS").Length == 7, "serial is stable, distinct and 7 chars");
            assert(Math.Abs(AvPortalMath.TickDegrees(0, 24) - 90f) < 0.001f && Math.Abs(AvPortalMath.TickDegrees(6, 24)) < 0.001f, "ticks start at top, run clockwise");
        }
    }
}
