using System;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The stroke helpers the shared kit lacks (annex 05 P1): phase, arrow, hatch, hex and the lane bar.</summary>
    internal static class StrStrokesTests
    {
        private static readonly Rgba White = new Rgba(1f, 1f, 1f);
        private static readonly Rgba Own = new Rgba(1f, 0f, 0f), Hostile = new Rgba(0f, 0f, 1f), Notch = new Rgba(1f, 0f, 1f),
            Ghost = new Rgba(0f, 1f, 0f), Tick = new Rgba(0.5f, 0.5f, 0.5f);

        public static void Run()
        {
            PhaseZeroIsTheKitDash();
            PhaseMarchesThePattern();
            ArrowEndsExactlyAtTheTip();
            HatchStaysInsideTheRectangle();
            HexIsSixSidesAtRadius();
            LaneBarPlacesTheNotchAtTheValue();
            LaneBarUnknownIsHatchedAndHasNoNotch();
            LaneBarGhostTrailFollowsTheTrend();
            LaneBarFitsItsQuadBudgetEverywhere();
            AFullBufferReturnsFalse();
            NanAddsNothing();
        }

        private static AvQuadBuffer Buffer(int capacity = 256) => new AvQuadBuffer(capacity);

        private static bool SameQuad(AvQuadBuffer a, int qa, AvQuadBuffer b, int qb)
        {
            for (int v = 0; v < 4; v++)
                if (a.X[4 * qa + v] != b.X[4 * qb + v] || a.Y[4 * qa + v] != b.Y[4 * qb + v]) return false;
            return true;
        }

        /// <summary>Start and end of a Line quad: the midpoints of its end edges.</summary>
        private static void Ends(AvQuadBuffer b, int q, out float sx, out float sy, out float ex, out float ey)
        {
            int i = 4 * q;
            sx = (b.X[i] + b.X[i + 3]) * 0.5f; sy = (b.Y[i] + b.Y[i + 3]) * 0.5f;
            ex = (b.X[i + 1] + b.X[i + 2]) * 0.5f; ey = (b.Y[i + 1] + b.Y[i + 2]) * 0.5f;
        }

        private static float DrawnLength(AvQuadBuffer b)
        {
            float sum = 0f;
            for (int q = 0; q < b.Count; q++)
            {
                Ends(b, q, out float sx, out float sy, out float ex, out float ey);
                sum += (float)Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
            }
            return sum;
        }

        private static int CountColour(AvQuadBuffer b, Rgba c)
        {
            int n = 0;
            for (int q = 0; q < b.Count; q++) if (Same(b.C[4 * q], c) && Same(b.C[4 * q + 1], c)) n++;
            return n;
        }

        private static bool Same(Rgba a, Rgba b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;

        private static void PhaseZeroIsTheKitDash()
        {
            var kit = Buffer(); var mine = Buffer();
            AvStrokes.DashedLine(kit, 0f, 0f, 100f, 30f, 10f, 5f, 2f, White);
            bool ok = StrStrokes.DashedLine(mine, 0f, 0f, 100f, 30f, 10f, 5f, 0f, 2f, White);
            TestAssert.That(ok && mine.Count == kit.Count && mine.Count > 0, "phase 0 writes as many dashes as the kit");
            TestAssert.That(SameQuad(kit, 0, mine, 0) && SameQuad(kit, kit.Count - 1, mine, mine.Count - 1), "phase 0 starts and ends where the kit does");
        }

        private static void PhaseMarchesThePattern()
        {
            var zero = Buffer(); var shifted = Buffer(); var full = Buffer();
            StrStrokes.DashedLine(zero, 0f, 0f, 150f, 0f, 10f, 5f, 0f, 2f, White);
            StrStrokes.DashedLine(shifted, 0f, 0f, 150f, 0f, 10f, 5f, 4f, 2f, White);
            StrStrokes.DashedLine(full, 0f, 0f, 150f, 0f, 10f, 5f, 15f, 2f, White);
            TestAssert.That(Math.Abs(DrawnLength(shifted) - 150f * 10f / 15f) <= 10f, "a shifted pattern still draws the duty cycle of the line");
            Ends(shifted, 0, out float sx, out _, out float ex, out _);
            TestAssert.That(Math.Abs(sx) < 0.01f && Math.Abs(ex - 6f) < 0.01f, "phase 4 starts four units into the first dash, so the first dash is 6 long");
            TestAssert.That(full.Count == zero.Count && SameQuad(full, 0, zero, 0) && SameQuad(full, full.Count - 1, zero, zero.Count - 1), "a whole cycle of phase is no phase");
            var negative = Buffer();
            StrStrokes.DashedLine(negative, 0f, 0f, 150f, 0f, 10f, 5f, -11f, 2f, White);
            Ends(negative, 0, out sx, out _, out ex, out _);
            TestAssert.That(Math.Abs(sx) < 0.01f && Math.Abs(ex - 6f) < 0.01f, "a negative phase wraps into the cycle");
        }

        private static void ArrowEndsExactlyAtTheTip()
        {
            var b = Buffer();
            bool ok = StrStrokes.Arrow(b, 0f, 0f, 50f, 0f, 4f, 10f, White);
            TestAssert.That(ok && b.Count == 2, "an arrow is one shaft quad and one head quad");
            bool tip = false; float shaftMax = float.MinValue, headBaseX = float.NaN;
            for (int q = 0; q < b.Count; q++)
                for (int v = 0; v < 4; v++)
                {
                    int i = 4 * q + v;
                    if (Math.Abs(b.X[i] - 50f) < 1e-4f && Math.Abs(b.Y[i]) < 1e-4f) tip = true;
                    if (q == 0) shaftMax = Math.Max(shaftMax, b.X[i]);
                    else if (b.X[i] < 49f) headBaseX = b.X[i];
                }
            TestAssert.That(tip, "a vertex of the head is the tip");
            TestAssert.That(Math.Abs(headBaseX - 40f) < 1e-4f, "the head base is one head length behind the tip");
            TestAssert.That(Math.Abs(shaftMax - 40f) < 1e-4f, "the shaft stops at the head base");

            var dashed = Buffer();
            StrStrokes.DashedArrow(dashed, 0f, 0f, 100f, 0f, 6f, 4f, 3f, 2f, 10f, White);
            float tipX = float.MinValue;
            for (int i = 0; i < 4 * dashed.Count; i++) tipX = Math.Max(tipX, dashed.X[i]);
            TestAssert.That(Math.Abs(tipX - 100f) < 1e-4f && dashed.Count > 3, "a dashed arrow ends at the tip with a solid head");
            for (int q = 0; q < dashed.Count - 1; q++)
            {
                float hi = float.MinValue;
                for (int v = 0; v < 4; v++) hi = Math.Max(hi, dashed.X[4 * q + v]);
                TestAssert.That(hi <= 90.0001f, "no dash reaches into the head");
            }

            var tiny = Buffer();
            StrStrokes.Arrow(tiny, 0f, 0f, 4f, 0f, 2f, 10f, White);
            float reach = float.MinValue;
            for (int i = 0; i < 4 * tiny.Count; i++) reach = Math.Max(reach, tiny.X[i]);
            TestAssert.That(tiny.Count >= 1 && Math.Abs(reach - 4f) < 1e-4f, "an arrow shorter than its head is only a head that still ends at the tip");
        }

        private static void HatchStaysInsideTheRectangle()
        {
            var b = Buffer();
            StrStrokes.HatchRect(b, 10f, 20f, 100f, 40f, 10f, 1f, White);
            float expected = (100f + 40f) / (10f * 1.4142f);
            TestAssert.That(b.Count > 0 && Math.Abs(b.Count - expected) <= 1.01f, "the hatch has about (w+h)/(spacing*sqrt2) lines");
            for (int q = 0; q < b.Count; q++)
            {
                Ends(b, q, out float sx, out float sy, out float ex, out float ey);
                TestAssert.That(sx >= 9.99f && sx <= 110.01f && ex >= 9.99f && ex <= 110.01f &&
                                sy >= 19.99f && sy <= 60.01f && ey >= 19.99f && ey <= 60.01f, "every hatch endpoint is inside the rectangle");
                TestAssert.That(ex > sx && Math.Abs((ey - sy) - (ex - sx)) < 0.01f, "hatch lines rise to the right at 45 degrees");
            }
            var none = Buffer();
            StrStrokes.HatchRect(none, 0f, 0f, 0f, 10f, 4f, 1f, White);
            StrStrokes.HatchRect(none, 0f, 0f, 10f, -1f, 4f, 1f, White);
            StrStrokes.HatchRect(none, 0f, 0f, 10f, 10f, 0f, 1f, White);
            TestAssert.That(none.Count == 0, "an empty rectangle or a zero spacing draws nothing");
            var tall = Buffer(512);
            StrStrokes.HatchRect(tall, 0f, 0f, 12f, 300f, 5f, 1f, White);
            for (int q = 0; q < tall.Count; q++)
            {
                Ends(tall, q, out float sx, out float sy, out float ex, out float ey);
                TestAssert.That(sx >= -0.01f && ex <= 12.01f && sy >= -0.01f && ey <= 300.01f, "a narrow tall rectangle is clipped at its corners too");
            }
        }

        private static void HexIsSixSidesAtRadius()
        {
            var b = Buffer();
            TestAssert.That(StrStrokes.Hex(b, 50f, 60f, 10f, 2f, White) && b.Count == 6, "a hex is six quads");
            for (int q = 0; q < 6; q++)
            {
                Ends(b, q, out float sx, out float sy, out float ex, out float ey);
                float rs = (float)Math.Sqrt((sx - 50f) * (sx - 50f) + (sy - 60f) * (sy - 60f));
                float re = (float)Math.Sqrt((ex - 50f) * (ex - 50f) + (ey - 60f) * (ey - 60f));
                TestAssert.That(Math.Abs(rs - 10f) < 0.01f && Math.Abs(re - 10f) < 0.01f, "every corner lies on the radius");
            }
            var none = Buffer();
            StrStrokes.Hex(none, 0f, 0f, 0f, 2f, White);
            TestAssert.That(none.Count == 0, "a zero radius draws nothing");
        }

        private static StrLane Lane(float value, float trend) => new StrLane
        {
            X = 10f, Y = 5f, W = 200f, H = 12f, Value = value, Trend = trend,
            Own = Own, Hostile = Hostile, Notch = Notch, Ghost = Ghost, Tick = Tick,
        };

        private static void LaneBarPlacesTheNotchAtTheValue()
        {
            var b = Buffer(StrStrokes.LaneQuads);
            StrLane lane = Lane(0.75f, 0f);
            TestAssert.That(StrStrokes.LaneBar(b, in lane), "a lane bar fits its buffer");
            float sum = 0f; int n = 0;
            for (int q = 0; q < b.Count; q++)
                if (Same(b.C[4 * q], Notch))
                    for (int v = 0; v < 4; v++) { sum += b.X[4 * q + v]; n++; }
            TestAssert.That(n >= 4 && Math.Abs(sum / n - (10f + 0.75f * 200f)) < 1f, "the notch sits at x + value * width");

            // Own end on the left, hostile end on the right: the track is the first quad.
            float leftX = float.MaxValue, rightX = float.MinValue; Rgba left = default, right = default;
            for (int v = 0; v < 4; v++)
            {
                if (b.X[v] < leftX) { leftX = b.X[v]; left = b.C[v]; }
                if (b.X[v] > rightX) { rightX = b.X[v]; right = b.C[v]; }
            }
            TestAssert.That(Same(left, Own) && Same(right, Hostile), "the track runs from the own colour on the left to the hostile colour on the right");

            var lo = Buffer(StrStrokes.LaneQuads); var hi = Buffer(StrStrokes.LaneQuads);
            StrLane zero = Lane(-3f, 0f), one = Lane(7f, 0f);
            StrStrokes.LaneBar(lo, in zero); StrStrokes.LaneBar(hi, in one);
            float loX = float.MaxValue, hiX = float.MinValue;
            for (int q = 0; q < lo.Count; q++) if (Same(lo.C[4 * q], Notch)) for (int v = 0; v < 4; v++) loX = Math.Min(loX, lo.X[4 * q + v]);
            for (int q = 0; q < hi.Count; q++) if (Same(hi.C[4 * q], Notch)) for (int v = 0; v < 4; v++) hiX = Math.Max(hiX, hi.X[4 * q + v]);
            TestAssert.That(loX >= 10f - 4f && hiX <= 210f + 4f, "a value outside 0..1 is clamped onto the track");
        }

        private static void LaneBarUnknownIsHatchedAndHasNoNotch()
        {
            var b = Buffer(StrStrokes.LaneQuads);
            StrLane lane = Lane(float.NaN, 0.3f);
            TestAssert.That(StrStrokes.LaneBar(b, in lane), "a hatched lane fits its buffer");
            TestAssert.That(CountColour(b, Notch) == 0, "an unknown value draws no notch");
            TestAssert.That(CountColour(b, Ghost) == 0, "an unknown value draws no ghost trail");
            TestAssert.That(CountColour(b, Tick) >= 1, "an unknown value draws the hatched track");
            TestAssert.That(CountColour(b, Own) == 0 && CountColour(b, Hostile) == 0, "the hatched track claims neither side");
        }

        private static void LaneBarGhostTrailFollowsTheTrend()
        {
            var withTrend = Buffer(StrStrokes.LaneQuads); var noTrend = Buffer(StrStrokes.LaneQuads); var none = Buffer(StrStrokes.LaneQuads);
            StrLane a = Lane(0.5f, 0.2f), b = Lane(0.5f, float.NaN), c = Lane(0.5f, 0f);
            StrStrokes.LaneBar(withTrend, in a); StrStrokes.LaneBar(noTrend, in b); StrStrokes.LaneBar(none, in c);
            TestAssert.That(CountColour(withTrend, Ghost) >= 1, "a trend of 0.2 draws a dashed ghost trail");
            TestAssert.That(CountColour(noTrend, Ghost) == 0 && CountColour(none, Ghost) == 0, "no trend, no ghost");
            float lo = float.MaxValue, hi = float.MinValue;
            for (int q = 0; q < withTrend.Count; q++)
                if (Same(withTrend.C[4 * q], Ghost)) for (int v = 0; v < 4; v++) { lo = Math.Min(lo, withTrend.X[4 * q + v]); hi = Math.Max(hi, withTrend.X[4 * q + v]); }
            TestAssert.That(lo >= 10f + (0.5f - 0.2f) * 200f - 1.5f && hi <= 10f + 0.5f * 200f + 1.5f, "the ghost runs from where the notch was to where it is");
        }

        private static void LaneBarFitsItsQuadBudgetEverywhere()
        {
            foreach (float value in new[] { float.NaN, 0f, 0.5f, 1f })
                foreach (float trend in new[] { float.NaN, 0f, 0.05f, 1f, -1f })
                {
                    var b = Buffer(StrStrokes.LaneQuads);
                    var lane = new StrLane { X = 0f, Y = 0f, W = 440f, H = 12f, Value = value, Trend = trend, Own = Own, Hostile = Hostile, Notch = Notch, Ghost = Ghost, Tick = Tick };
                    TestAssert.That(StrStrokes.LaneBar(b, in lane) && b.Count <= StrStrokes.LaneQuads && !b.Overflowed,
                        "the widest lane fits LaneQuads for value " + value + " trend " + trend);
                }
            var nanW = Buffer(); var lane0 = Lane(0.5f, 0.1f); lane0.W = float.NaN;
            TestAssert.That(StrStrokes.LaneBar(nanW, in lane0) && nanW.Count == 0, "a lane with a NaN width draws nothing");
            var flat = Buffer(); var lane1 = Lane(0.5f, 0.1f); lane1.H = 0f;
            TestAssert.That(StrStrokes.LaneBar(flat, in lane1) && flat.Count == 0, "a lane with no height draws nothing");
        }

        private static void AFullBufferReturnsFalse()
        {
            var b = Buffer(3);
            bool ok = StrStrokes.DashedLine(b, 0f, 0f, 200f, 0f, 5f, 5f, 0f, 2f, White);
            TestAssert.That(!ok && b.Overflowed && b.Count == 3, "ten dashes into three quads overflow and say so");
            var h = Buffer(3);
            TestAssert.That(!StrStrokes.Hex(h, 0f, 0f, 5f, 1f, White) && h.Overflowed, "a hex into three quads overflows");
            var tiny = Buffer(1);
            StrLane lane = Lane(0.5f, 0.2f);
            TestAssert.That(!StrStrokes.LaneBar(tiny, in lane) && tiny.Overflowed, "a lane bar into one quad overflows");
        }

        private static void NanAddsNothing()
        {
            var b = Buffer();
            TestAssert.That(StrStrokes.DashedLine(b, float.NaN, 0f, 10f, 0f, 2f, 2f, 0f, 1f, White) &&
                            StrStrokes.DashedLine(b, 0f, 0f, 10f, 0f, 2f, 2f, float.NaN, 1f, White) &&
                            StrStrokes.Arrow(b, 0f, 0f, float.NaN, 0f, 1f, 4f, White) &&
                            StrStrokes.DashedArrow(b, 0f, float.NaN, 10f, 0f, 2f, 2f, 0f, 1f, 4f, White) &&
                            StrStrokes.HatchRect(b, float.NaN, 0f, 10f, 10f, 4f, 1f, White) &&
                            StrStrokes.Hex(b, 0f, float.NaN, 5f, 1f, White), "NaN input returns true");
            TestAssert.That(b.Count == 0, "NaN input adds nothing");
        }
    }
}
