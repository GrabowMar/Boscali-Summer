using System;

namespace NOAvionics.Tests
{
    /// <summary>
    /// Engine-free tests for the HUD kit primitives added across the HUD remake slices.
    /// Extended in place by later tasks (geometry, strokes, stylesheet).
    /// </summary>
    public static class AvHudKitTests
    {
        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));

            TestNumFormat(assert);
            TestUnits(assert);

            TestQuadBuffer(assert);
            TestLine(assert);
            TestArcRing(assert);
            TestDashes(assert);
            TestShapes(assert);


            TestHudStyle(assert);
        }

        private static string S(char[] b, int n) => new string(b, 0, n);

        private static readonly Rgba White = new Rgba(1f, 1f, 1f, 1f);

        private static void TestNumFormat(Action<bool, string> assert)
        {
            char[] b = new char[32];

            assert(S(b, AvNumFormat.Write(b, 0, 1234.5678, 2)) == "1234.57", "F2 rounds away from zero");
            assert(S(b, AvNumFormat.Write(b, 0, -0.004, 2)) == "0.00", "a magnitude that rounds to zero drops the sign");
            assert(S(b, AvNumFormat.Write(b, 0, -12.46, 1)) == "-12.5", "F1 keeps a negative sign when the magnitude survives");
            assert(S(b, AvNumFormat.Write(b, 0, double.NaN, 1)) == "--", "NaN prints the no-data marker");
            assert(S(b, AvNumFormat.Write(b, 0, 7, 0, true)) == "+7", "plusSign marks a positive non-zero magnitude");
            assert(S(b, AvNumFormat.Write(b, 0, 0, 0, true)) == "0", "plusSign is suppressed at zero magnitude");
            assert(S(b, AvNumFormat.Write(b, 0, 3, 5)) == "3.000", "decimals clamp to 3");

            int overflowed = AvNumFormat.Write(new char[3], 0, 123456, 0);
            assert(overflowed == 3, "a full buffer stops writing and returns its own length, without throwing");

            assert(S(b, AvNumFormat.Append(b, AvNumFormat.Write(b, 0, 42, 0), "kt")) == "42kt", "Append continues from Write's returned index");
        }

        private static void TestUnits(Action<bool, string> assert)
        {
            char[] b = new char[32];

            assert(S(b, AvUnitTable.SpeedReading(b, 0, 100f, AvUnits.Metric)) == "360km/h", "speed metric uses km/h");
            assert(S(b, AvUnitTable.SpeedReading(b, 0, 100f, AvUnits.Imperial)) == "194kt", "speed imperial uses kt");

            assert(S(b, AvUnitTable.AltitudeReading(b, 0, 5.24f, AvUnits.Metric)) == "5.2m", "altitude metric under 10m keeps a decimal");
            assert(S(b, AvUnitTable.AltitudeReading(b, 0, 1234.4f, AvUnits.Metric)) == "1234m", "altitude metric at/above 10m drops the decimal");
            assert(S(b, AvUnitTable.AltitudeReading(b, 0, 1000f, AvUnits.Imperial)) == "3281ft", "altitude imperial is always whole feet");

            assert(S(b, AvUnitTable.DistanceReading(b, 0, 12345f, AvUnits.Metric)) == "12km", "distance metric beyond 10km drops the decimal");
            assert(S(b, AvUnitTable.DistanceReading(b, 0, 1500f, AvUnits.Metric)) == "1.5km", "distance metric between 1-10km keeps a decimal");
            assert(S(b, AvUnitTable.DistanceReading(b, 0, 800f, AvUnits.Metric)) == "800m", "distance metric under 1km stays in metres");
            assert(S(b, AvUnitTable.DistanceReading(b, 0, 500f, AvUnits.Imperial)) == "547yd", "distance imperial under 1000yd stays in yards");
            assert(S(b, AvUnitTable.DistanceReading(b, 0, 5000f, AvUnits.Imperial)) == "2.7nm", "distance imperial beyond 1000yd switches to nautical miles");

            assert(S(b, AvUnitTable.ClimbRateReading(b, 0, 3.2f, AvUnits.Metric)) == "+3.2m/s", "climb metric under 10 m/s keeps a decimal and a plus");
            assert(S(b, AvUnitTable.ClimbRateReading(b, 0, 0.4f, AvUnits.Metric)) == "0.4m/s", "climb at or below 0.5 m/s has no plus");
            assert(S(b, AvUnitTable.ClimbRateReading(b, 0, -12.4f, AvUnits.Metric)) == "-12m/s", "climb metric at/above 10 m/s drops the decimal");
            assert(S(b, AvUnitTable.ClimbRateReading(b, 0, 5f, AvUnits.Imperial)) == "+984fpm", "climb imperial is always whole fpm");

            string nan = S(b, AvUnitTable.ClimbRateReading(b, 0, float.NaN, AvUnits.Metric));
            assert(nan.StartsWith("--", StringComparison.Ordinal), "climb of NaN reads as the no-data marker");
        }

        private static void TestQuadBuffer(Action<bool, string> assert)
        {
            AvQuadBuffer buf = new AvQuadBuffer(2);
            bool r1 = AvStrokes.Fill(buf, 0f, 0f, 1f, 1f, White);
            bool r2 = AvStrokes.Fill(buf, 0f, 0f, 1f, 1f, White);
            bool r3 = AvStrokes.Fill(buf, 0f, 0f, 1f, 1f, White);
            assert(r1 && r2, "the first two fills inside capacity both succeed");
            assert(!r3, "a fill past capacity is rejected");
            assert(buf.Overflowed, "the buffer flags the overflow");
            assert(buf.Count == 2, "count stays capped at capacity, not 3");

            buf.Clear();
            assert(buf.Count == 0 && !buf.Overflowed, "Clear resets both Count and Overflowed");
        }

        private static void TestLine(Action<bool, string> assert)
        {
            AvQuadBuffer buf = new AvQuadBuffer(8);

            bool ok = AvStrokes.Line(buf, 0f, 0f, 10f, 0f, 2f, White);
            assert(ok, "a plain line reports success");
            assert(buf.Count == 1, "a line without feather adds exactly one quad");
            for (int i = 0; i < 4; i++)
                Near(assert, Math.Abs(buf.Y[i]), 1f, "every core vertex sits half the width off the centreline");
            bool sawZeroX = false, sawTenX = false;
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(buf.X[i]) < 1e-3f) sawZeroX = true;
                if (Math.Abs(buf.X[i] - 10f) < 1e-3f) sawTenX = true;
            }
            assert(sawZeroX && sawTenX, "the core quad spans both endpoints' X");

            buf.Clear();
            AvStrokes.Line(buf, 0f, 0f, 10f, 0f, 2f, White, 1f);
            assert(buf.Count == 3, "a feathered line adds the core quad plus an outer and inner strip");
            int q1 = 4; // quad index 1 (the outer strip) starts at vertex 4
            assert(buf.C[q1 + 2].A == 0f && buf.C[q1 + 3].A == 0f,
                   "the outer strip's far edge fades to zero alpha");
            Near(assert, Math.Abs(buf.Y[q1 + 2]), 2f, "the outer strip's far edge sits at width/2 + feather");
            Near(assert, Math.Abs(buf.Y[q1 + 3]), 2f, "the outer strip's far edge sits at width/2 + feather");

            int before = buf.Count;
            bool nanOk = AvStrokes.Line(buf, float.NaN, 0f, 10f, 0f, 2f, White);
            assert(nanOk, "a NaN endpoint is reported as handled, not a failure");
            assert(buf.Count == before, "a NaN endpoint adds nothing");
        }

        private static void TestArcRing(Action<bool, string> assert)
        {
            AvQuadBuffer arc = new AvQuadBuffer(8);
            AvStrokes.Arc(arc, 0f, 0f, 10f, 0f, 90f, 3, 2f, White);
            assert(arc.Count == 3, "3 segments produce 3 quads");
            double angle0 = Math.Atan2(arc.Y[0], arc.X[0]) * 180.0 / Math.PI;
            assert(Math.Abs(angle0) < 1e-1, "the first vertex sits at the arc's start angle");

            AvQuadBuffer ring = new AvQuadBuffer(16);
            AvStrokes.Ring(ring, 0f, 0f, 10f, 12, 2f, White);
            assert(ring.Count == 12, "a ring with 12 segments makes 12 quads");
            for (int i = 0; i < ring.Count * 4; i++)
            {
                float dist = (float)Math.Sqrt(ring.X[i] * ring.X[i] + ring.Y[i] * ring.Y[i]);
                assert(Math.Abs(dist - 9f) < 1e-3f || Math.Abs(dist - 11f) < 1e-3f,
                       "every ring vertex sits on the inner or outer radius");
            }
        }

        private static void TestDashes(Action<bool, string> assert)
        {
            AvQuadBuffer line = new AvQuadBuffer(8);
            AvStrokes.DashedLine(line, 0f, 0f, 20f, 0f, 6f, 4f, 2f, White);
            assert(line.Count == 2, "dash 6 / gap 4 over a 20-unit line draws 2 dashes");

            AvQuadBuffer ring = new AvQuadBuffer(32);
            AvStrokes.DashedRing(ring, 0f, 0f, 10f, 8, 0.5f, 2f, White);
            assert(ring.Count >= 16, "8 dashes at 2 segments each place at least 16 quads");
        }

        private static void TestShapes(Action<bool, string> assert)
        {
            AvQuadBuffer bracket = new AvQuadBuffer(8);
            AvStrokes.Bracket(bracket, 0f, 0f, 40f, 30f, 6f, 2f, White);
            assert(bracket.Count == 8, "a bracket draws 8 lines, one pair per corner");

            AvQuadBuffer diamond = new AvQuadBuffer(4);
            AvStrokes.Diamond(diamond, 0f, 0f, 5f, 1f, White);
            assert(diamond.Count == 4, "a diamond draws its 4 sides");

            AvQuadBuffer cross = new AvQuadBuffer(2);
            AvStrokes.Cross(cross, 0f, 0f, 5f, 1f, White);
            assert(cross.Count == 2, "a cross draws 2 lines");

            AvQuadBuffer chevron = new AvQuadBuffer(2);
            AvStrokes.Chevron(chevron, 0f, 0f, 10f, 0f, 1f, White);
            assert(chevron.Count == 2, "a chevron draws 2 lines");
        }

        private static void TestHudStyle(Action<bool, string> assert)
        {
            AvStyleSheet s = AvStyleSheet.Parse(
                ".hud-x { opacity: 1.5; stroke: 2; dash: 6 4; glow: 0.12; color: hostile 80; }");
            assert(!s.HasErrors, "a sheet using the new HUD properties parses without errors: " +
                                  string.Join("; ", s.Errors.ToArray()));

            AvStyle x = s.Resolve("hud-x");
            Near(assert, x.Opacity, 1f, "opacity clamps to 1");
            assert(x.HasStroke, "stroke sets its Has flag");
            Near(assert, x.StrokeWidth, 2f, "stroke width is read");
            Near(assert, x.DashOn, 6f, "dash-on is the first dash number");
            Near(assert, x.DashOff, 4f, "dash-off is the second dash number");
            Near(assert, x.Glow, 0.12f, "glow is read");
            assert(x.Color.Kind == AvColorRef.Hostile, "color: hostile resolves to the hostile theme reference");

            AvStyleSheet badDash = AvStyleSheet.Parse(".b { dash: 6; }");
            assert(badDash.HasErrors, "dash with a single number is an error");

            AvStyleSheet merged = AvStyleSheet.Parse(".a { stroke: 1 } .a.b { stroke: 3 }");
            Near(assert, merged.Resolve("a b").StrokeWidth, 3f, "a later, more specific rule overrides the stroke width");

        }

        private static void Near(Action<bool, string> assert, float actual, float expected, string what)
        {
            assert(Math.Abs(actual - expected) < 1e-3f,
                   what + " (expected " + expected.ToString("0.###") + ", got " + actual.ToString("0.###") + ")");
        }
    }
}
