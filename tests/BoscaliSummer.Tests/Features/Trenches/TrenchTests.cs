using System;
using BoscaliSummer.Features.Trenches.Domain;

namespace BoscaliSummer.Tests.Features.Trenches
{
    internal static class TrenchTests
    {
        public static void Run()
        {
            TestBuildableGround();
            TestResample();
            TestNormalsAndClosure();
            TestTraverseWave();
            TestDensifyAndWave();
            TestRoutePlanning();
            TestRunSplitting();
            TestStageRules();
        }

        private static void TestBuildableGround()
        {
            TestAssert.That(TrenchTraceMath.IsBuildableGround(20f, 1f), "Dry level ground accepts trenches");
            TestAssert.That(!TrenchTraceMath.IsBuildableGround(2f, 1f), "Shore and water reject trenches");
            TestAssert.That(!TrenchTraceMath.IsBuildableGround(20f, 0.98f), "Steep ground rejects trenches");
            TestAssert.That(!TrenchTraceMath.IsBuildableGround(float.NaN, 1f), "Unknown ground fails closed");
        }

        private static void TestResample()
        {
            // A sparse two-point trace is one straight Bezier run at the requested spacing.
            var x = new[] { 0f, 100f };
            var z = new[] { 0f, 0f };
            var outX = new float[64];
            var outZ = new float[64];
            int count = TrenchTraceMath.Resample(x, z, 2, 10f, false, outX, outZ);
            TestAssert.That(count >= 10 && count <= 12, "A 100m run yields about ten 10m stations");
            TestAssert.That(outX[0] == 0f && outZ[0] == 0f, "The near end stays put");
            TestAssert.That(Near(outX[count - 1], 100f), "The far end stays put");
            float maximumStep = 0f;
            for (int i = 1; i < count; i++)
                maximumStep = Math.Max(maximumStep, Math.Abs(outX[i] - outX[i - 1]));
            TestAssert.That(maximumStep <= 10.001f, "Resampling never exceeds the requested spacing");

            // A bent chain eases through its corner: no station-to-station chord turns more
            // than a fraction of the original 90 degrees, instead of one abrupt hinge.
            var bentX = new[] { 0f, 100f, 100f };
            var bentZ = new[] { 0f, 0f, 100f };
            int bent = TrenchTraceMath.Resample(bentX, bentZ, 3, 10f, false, outX, outZ);
            TestAssert.That(bent >= 18, "The bend keeps its full length");
            float worstTurn = 0f;
            for (int i = 2; i < bent; i++)
            {
                float ax = outX[i - 1] - outX[i - 2], az = outZ[i - 1] - outZ[i - 2];
                float bx = outX[i] - outX[i - 1], bz = outZ[i] - outZ[i - 1];
                float la = Math.Max(0.001f, (float)Math.Sqrt(ax * ax + az * az));
                float lb = Math.Max(0.001f, (float)Math.Sqrt(bx * bx + bz * bz));
                float dot = (ax * bx + az * bz) / (la * lb);
                dot = Math.Max(-1f, Math.Min(1f, dot));
                worstTurn = Math.Max(worstTurn, (float)Math.Acos(dot) * 57.29578f);
            }
            TestAssert.That(worstTurn < 30f, "A Bezier chain eases through the contour corner");
            for (int i = 1; i < bent; i++)
                TestAssert.That(Math.Abs(outX[i] - outX[i - 1]) + Math.Abs(outZ[i] - outZ[i - 1]) < 20f,
                    "The curve stays continuous across the join");

            // A closed loop resamples with wraparound tangents and returns to its start.
            var loopX = new[] { 0f, 100f, 100f, 0f };
            var loopZ = new[] { 0f, 0f, 100f, 100f };
            int loop = TrenchTraceMath.Resample(loopX, loopZ, 4, 10f, true, outX, outZ);
            TestAssert.That(loop >= 30, "A closed pocket keeps its full ring");
            TestAssert.That(Near(outX[loop - 1], 0f) && Near(outZ[loop - 1], 0f),
                "A closed ring returns to its first station");
        }

        private static void TestNormalsAndClosure()
        {
            var x = new[] { 0f, 0f, 0f };
            var z = new[] { 0f, 10f, 20f };
            TrenchTraceMath.Normal(x, z, 3, 1, out float nx, out float nz);
            TestAssert.That(Math.Abs(nx) > 0.99f && Math.Abs(nz) < 0.01f,
                "A north-running trace has an east-west normal");

            // Command repeats a pocket ring's first point last; that is the closure signal.
            var ringX = new[] { 0f, 100f, 100f, 0f, 0f };
            var ringZ = new[] { 0f, 0f, 100f, 100f, 0f };
            TestAssert.That(TrenchTraceMath.IsClosed(ringX, ringZ, 5), "A returning pocket reads as closed");
            TestAssert.That(!TrenchTraceMath.IsClosed(new[] { 0f, 1000f }, new[] { 0f, 0f }, 2),
                "An open trace is not closed");
        }

        private static void TestTraverseWave()
        {
            const float span = 5.5f;
            TestAssert.That(Near(TrenchTraceMath.TraverseWave(0f, span), 1f) &&
                Near(TrenchTraceMath.TraverseWave(span, span), -1f) &&
                Near(TrenchTraceMath.TraverseWave(span * 2f, span), 1f),
                "Traverse wave corners alternate every bay");
            TestAssert.That(Near(TrenchTraceMath.TraverseWave(span * 0.5f, span), 0f),
                "A traverse crosses the line between two bays");
            TestAssert.That(Near(TrenchTraceMath.TraverseWave(1.37f + span, span),
                -TrenchTraceMath.TraverseWave(1.37f, span)),
                "One bay ahead the wave is exactly on the other side of the line");
            TestAssert.That(Math.Abs(TrenchTraceMath.TraverseWave(span - 0.01f, span) -
                TrenchTraceMath.TraverseWave(span + 0.01f, span)) < 0.01f,
                "The wave is continuous across a traverse corner");
            TestAssert.That(Math.Abs(TrenchTraceMath.TraverseWave(0.2f, span) -
                TrenchTraceMath.TraverseWave(0f, span)) < 0.02f,
                "A bay runs straight through a rounded traverse extreme");
        }

        private static void TestDensifyAndWave()
        {
            var x = new[] { 0f, 22f };
            var z = new[] { 0f, 0f };
            var outX = new float[512];
            var outZ = new float[512];
            int count = TrenchTraceMath.DensifyTraversed(x, z, 2, TrenchTraceMath.MeshSpacing,
                TrenchTraceMath.TraverseSpacing, TrenchTraceMath.TraverseAmplitude, 0f, outX, outZ);
            TestAssert.That(count >= 7, "A 22m ditch is cut into mesh rings");
            float excursion = 0f;
            for (int i = 0; i < count; i++)
            {
                excursion = Math.Max(excursion, Math.Abs(outZ[i]));
                if (i == 0) continue;
                float step = Math.Abs(outX[i] - outX[i - 1]) + Math.Abs(outZ[i] - outZ[i - 1]);
                TestAssert.That(step < TrenchTraceMath.MeshSpacing * 3f, "Mesh rings stay closely spaced");
            }
            TestAssert.That(excursion > 1.0f && excursion <= TrenchTraceMath.TraverseAmplitude + 0.001f,
                "The ditch weaves through traverses within a trench width");
        }

        private static void TestRoutePlanning()
        {
            const int stations = 3, candidates = 5;
            int[] Route(Func<int, int, float> ground)
            {
                var height = new float[stations, candidates];
                var cost = new float[stations, candidates];
                for (int s = 0; s < stations; s++)
                    for (int k = 0; k < candidates; k++)
                    {
                        height[s, k] = ground(s, k);
                        float offset = (k - 2) * 2f;
                        cost[s, k] = height[s, k] + 0.05f * offset * offset;
                    }
                var planned = new int[stations];
                TestAssert.That(TrenchTraceMath.PlanRoute(height, cost, stations, 6f, planned),
                    "A route is planned");
                return planned;
            }

            TestAssert.That(Route((s, k) => 0f)[1] == 2,
                "Level ground keeps the trench on its intended line");
            TestAssert.That(Route((s, k) => k == 1 ? -1.5f : 0f)[1] == 1,
                "A hollow off the line is where the trench settles");
            TestAssert.That(Route((s, k) => k == 2 ? 3f : 0f)[1] != 2,
                "A rise on the intended line is avoided instead of climbed");

            // An unbuildable station breaks the line instead of failing it whole; the
            // stretches on either side still get a level each.
            var blockedHeight = new float[stations, candidates];
            var blockedCost = new float[stations, candidates];
            for (int s = 0; s < stations; s++)
                for (int k = 0; k < candidates; k++)
                {
                    blockedHeight[s, k] = 0f;
                    blockedCost[s, k] = k == 0 ? float.NaN : 0f;
                }
            var blockedRoute = new int[stations];
            TestAssert.That(TrenchTraceMath.PlanRoute(blockedHeight, blockedCost, stations, 6f, blockedRoute),
                "A partially blocked corridor still routes");
            TestAssert.That(blockedRoute[0] != 0 && blockedRoute[1] != 0 && blockedRoute[2] != 0,
                "Unbuildable ground is never routed through");

            var allBlocked = new float[stations, candidates];
            for (int s = 0; s < stations; s++)
                for (int k = 0; k < candidates; k++)
                    allBlocked[s, k] = float.NaN;
            TestAssert.That(!TrenchTraceMath.PlanRoute(allBlocked, allBlocked, stations, 6f, new int[stations]),
                "A wholly blocked band fails closed");
        }

        private static void TestRunSplitting()
        {
            var valid = new[] { false, true, true, true, false, true, false, true, true, true, true };
            var starts = new float[8];
            var lengths = new float[8];
            int runs = TrenchTraceMath.SplitRuns(valid, valid.Length, 2, starts, lengths, 8);
            TestAssert.That(runs == 2, "Short fragments between valid runs are dropped");
            TestAssert.That((int)starts[0] == 1 && (int)lengths[0] == 3, "The first run is kept whole");
            TestAssert.That((int)starts[1] == 7 && (int)lengths[1] == 4, "The far side of a gap is kept too");
            TestAssert.That(TrenchTraceMath.SplitRuns(valid, valid.Length, 4, starts, lengths, 8) == 1,
                "The minimum run length is enforced");
        }

        private static void TestStageRules()
        {
            TestAssert.That(TrenchTraceMath.DefenderBudget(TrenchStage.Scrape) == 1 &&
                TrenchTraceMath.DefenderBudget(TrenchStage.FireTrench) == 2 &&
                TrenchTraceMath.DefenderBudget(TrenchStage.Support) == 3 &&
                TrenchTraceMath.DefenderBudget(TrenchStage.Redoubt) == 4 &&
                TrenchTraceMath.DefenderBudget(TrenchStage.Saps) == 4,
                "Defenses grow 1/2/3/4 and stay sparse");
            TestAssert.That(TrenchTraceMath.WorksBudget(TrenchStage.Scrape) == 0 &&
                TrenchTraceMath.WorksBudget(TrenchStage.FireTrench) > 0 &&
                TrenchTraceMath.WorksBudget(TrenchStage.Redoubt) == 8,
                "Works unlock with the fire trench and cap at eight");
            TestAssert.That(!TrenchTraceMath.CanAdvance(false, 59, 60, 45),
                "Damage suppresses construction");
            TestAssert.That(TrenchTraceMath.CanAdvance(false, 60, 60, 45),
                "Survivors resume after a full quiet minute");
            TestAssert.That(!TrenchTraceMath.CanAdvance(true, 600, 60, 45),
                "An overrun position never rebuilds");
            TestAssert.That(TrenchTraceMath.HasSupport(TrenchStage.Redoubt) &&
                !TrenchTraceMath.HasSupport(TrenchStage.FireTrench) &&
                TrenchTraceMath.HasSaps(TrenchStage.Saps) && !TrenchTraceMath.HasSaps(TrenchStage.Redoubt),
                "Belt predicates follow the stage order");
            TestAssert.That(TrenchTraceMath.SupportDepth > TrenchTraceMath.FireDepth &&
                TrenchTraceMath.RedoubtDepth > TrenchTraceMath.SupportDepth &&
                TrenchTraceMath.MinRunLength > 0f,
                "The belt sits in deliberate field depth order");
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }
}
