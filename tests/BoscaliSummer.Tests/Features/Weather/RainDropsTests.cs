using System;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The canopy droplet simulation, without Unity: the pool ceiling, seed determinism, gravity,
    /// the wind-driven streak, the precipitation ladder and the hostile-input cases.
    ///
    /// <para>This is the half of the glass rain that can be asserted rather than looked at; the
    /// renderer that turns these droplets into a normal sheet is the half that cannot.</para>
    /// </summary>
    internal static class RainDropsTests
    {
        private const int Seed = 0x5241494E;
        private const float StepSeconds = 0.05f;

        public static void Run()
        {
            ThePoolNeverExceedsItsCeiling();
            TheSameSeedAndStepsProduceTheSamePane();
            GravityPullsTheDropletsDown();
            ACrosswindDriftsThemSidewaysAndStretchesTheTail();
            HeavierPrecipitationFillsThePaneFaster();
            WhereTheRainStopsTheGlassDries();
            TheSteadyStateStepAllocatesNothing();
            ZeroAndHostileStepsChangeNothing();
        }

        private static void ThePoolNeverExceedsItsCeiling()
        {
            var drops = new RainDrops(Seed);
            int highest = 0;
            for (int i = 0; i < 400; i++)
            {
                drops.Step(0.2f, 1f, PrecipitationKind.Hail, 0.1f, 0f);
                if (drops.Count > highest) highest = drops.Count;
                TestAssert.That(drops.Count <= RainDrops.MaxDrops,
                    "the droplet pool must never pass its ceiling, saw " + drops.Count);
            }

            TestAssert.That(highest >= RainDrops.MaxDrops / 3,
                "a long hailstorm must actually populate the pane, saw " + highest);
            TestAssert.That(RainDrops.MaxDrops == 256, "the documented ceiling is 256 droplets");
        }

        private static void TheSameSeedAndStepsProduceTheSamePane()
        {
            var a = new RainDrops(Seed);
            var b = new RainDrops(Seed);
            var c = new RainDrops(Seed + 1);

            for (int i = 0; i < 200; i++)
            {
                float intensity = 0.3f + (i % 7) * 0.1f;
                PrecipitationKind kind = (PrecipitationKind)(i % 5);
                a.Step(StepSeconds, intensity, kind, 0.12f, 0f);
                b.Step(StepSeconds, intensity, kind, 0.12f, 0f);
                c.Step(StepSeconds, intensity, kind, 0.12f, 0f);
            }

            TestAssert.That(a.Count == b.Count, "the same seed and steps keep the same pool size");
            TestAssert.That(SamePane(a, b), "the same seed and steps keep the same pane");
            TestAssert.That(!SamePane(a, c), "a different seed must not reproduce the pane");
        }

        private static void GravityPullsTheDropletsDown()
        {
            var drops = new RainDrops(Seed);
            for (int i = 0; i < 4; i++) drops.Step(StepSeconds, 1f, PrecipitationKind.Rain, 0f, 0f);
            TestAssert.That(drops.Count > 0, "the rain must have put droplets on the pane");

            // Index zero is never coalesced away (a merge only ever removes the later droplet),
            // and a quiet step spawns nothing, so this is the same droplet before and after.
            float before = drops.Y(0);
            drops.Step(StepSeconds, 0f, PrecipitationKind.None, 0f, 0f);
            TestAssert.That(drops.Count > 0, "a quiet step must not empty the pane");
            TestAssert.That(drops.Y(0) > before,
                "gravity must move a droplet down the pane: " + before + " to " + drops.Y(0));
        }

        private static void ACrosswindDriftsThemSidewaysAndStretchesTheTail()
        {
            var windy = new RainDrops(Seed);
            var still = new RainDrops(Seed);
            for (int i = 0; i < 6; i++)
            {
                windy.Step(StepSeconds, 1f, PrecipitationKind.Rain, 0.25f, 0f);
                still.Step(StepSeconds, 1f, PrecipitationKind.Rain, 0f, 0f);
            }

            float windyX = windy.X(0);
            float stillX = still.X(0);
            // Short enough that neither run has reached the tail ceiling, so the comparison is
            // about the streak the wind adds rather than about the clamp both would end at.
            for (int i = 0; i < 10; i++)
            {
                windy.Step(StepSeconds, 1f, PrecipitationKind.Rain, 0.25f, 0f);
                still.Step(StepSeconds, 1f, PrecipitationKind.Rain, 0f, 0f);
            }

            TestAssert.That(windy.X(0) > windyX + 0.01f,
                "a crosswind must drag the droplets sideways, moved " + (windy.X(0) - windyX));
            TestAssert.That(Math.Abs(still.X(0) - stillX) < 1e-4f,
                "no crosswind means no sideways drift");
            TestAssert.That(LongestTail(windy) > LongestTail(still),
                "a crosswind must streak the run longer than still air, " +
                LongestTail(windy) + " against " + LongestTail(still));
        }

        private static void HeavierPrecipitationFillsThePaneFaster()
        {
            var drizzle = new RainDrops(Seed);
            var rain = new RainDrops(Seed);
            var hail = new RainDrops(Seed);
            for (int i = 0; i < 60; i++)
            {
                drizzle.Step(StepSeconds, 1f, PrecipitationKind.Drizzle, 0f, 0f);
                rain.Step(StepSeconds, 1f, PrecipitationKind.Rain, 0f, 0f);
                hail.Step(StepSeconds, 1f, PrecipitationKind.Hail, 0f, 0f);
            }

            TestAssert.That(drizzle.Count > 0, "a drizzle must still bead the glass");
            TestAssert.That(hail.Count > rain.Count, "hail must out-fill rain");
            TestAssert.That(rain.Count > drizzle.Count, "rain must out-fill drizzle");
            TestAssert.That(hail.Radius(LargestDrop(hail)) > drizzle.Radius(LargestDrop(drizzle)),
                "hail must bead larger than drizzle");
        }

        private static void WhereTheRainStopsTheGlassDries()
        {
            var drops = new RainDrops(Seed);
            for (int i = 0; i < 8; i++) drops.Step(StepSeconds, 1f, PrecipitationKind.Showers, 0f, 0f);
            int soaked = drops.Count;

            for (int i = 0; i < 200; i++) drops.Step(StepSeconds, 0f, PrecipitationKind.None, 0f, 0f);
            TestAssert.That(drops.Count < soaked, "the pane must clear when the shower stops");
            TestAssert.That(drops.Count == 0, "nothing survives ten seconds of dry air, left " + drops.Count);
        }

        private static void TheSteadyStateStepAllocatesNothing()
        {
            var drops = new RainDrops(Seed);
            for (int i = 0; i < 64; i++) drops.Step(StepSeconds, 0.8f, PrecipitationKind.Showers, 0.1f, 0.2f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) drops.Step(StepSeconds, 0.8f, PrecipitationKind.Showers, 0.1f, 0.2f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            TestAssert.That(allocated == 0,
                "the droplet step must not allocate in the steady state, allocated " + allocated + " bytes");
        }

        private static void ZeroAndHostileStepsChangeNothing()
        {
            var drops = new RainDrops(Seed);
            for (int i = 0; i < 10; i++) drops.Step(StepSeconds, 1f, PrecipitationKind.Showers, 0.1f, 0f);
            int count = drops.Count;
            float x = drops.X(0);
            float y = drops.Y(0);

            drops.Step(0f, 1f, PrecipitationKind.Hail, 1f, 1f);
            TestAssert.That(drops.Count == count && drops.X(0) == x && drops.Y(0) == y,
                "a zero delta time must change nothing");
            drops.Step(-1f, 1f, PrecipitationKind.Hail, 1f, 1f);
            TestAssert.That(drops.Count == count && drops.X(0) == x && drops.Y(0) == y,
                "a negative delta time must change nothing");
            drops.Step(float.NaN, 1f, PrecipitationKind.Hail, 1f, 1f);
            TestAssert.That(drops.Count == count && drops.X(0) == x && drops.Y(0) == y,
                "an unreadable delta time must change nothing");

            drops.Step(StepSeconds, float.NaN, PrecipitationKind.Hail, float.NaN, float.NaN);
            TestAssert.That(FinitePane(drops), "unreadable weather must not put NaN on the pane");
            TestAssert.That(drops.Count > 0, "an unreadable step must not empty the pane");
            drops.Step(StepSeconds, 1f, PrecipitationKind.Hail, 0.2f, 0f);
            TestAssert.That(FinitePane(drops) && drops.Count > 0,
                "the pane must keep working after an unreadable step");
        }

        // ---- Helpers -------------------------------------------------------------------------

        private static bool SamePane(RainDrops a, RainDrops b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a.X(i) != b.X(i) || a.Y(i) != b.Y(i) || a.Radius(i) != b.Radius(i) ||
                    a.TailX(i) != b.TailX(i) || a.TailY(i) != b.TailY(i) || a.Age(i) != b.Age(i))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool FinitePane(RainDrops drops)
        {
            for (int i = 0; i < drops.Count; i++)
            {
                if (float.IsNaN(drops.X(i)) || float.IsNaN(drops.Y(i)) || float.IsNaN(drops.Radius(i)) ||
                    float.IsNaN(drops.TailX(i)) || float.IsNaN(drops.TailY(i)) || float.IsNaN(drops.Age(i)))
                {
                    return false;
                }
            }
            return true;
        }

        private static float LongestTail(RainDrops drops)
        {
            float longest = 0f;
            for (int i = 0; i < drops.Count; i++)
            {
                float x = drops.TailX(i);
                float y = drops.TailY(i);
                float length = (float)Math.Sqrt(x * x + y * y);
                if (length > longest) longest = length;
            }
            return longest;
        }

        private static int LargestDrop(RainDrops drops)
        {
            int largest = 0;
            for (int i = 1; i < drops.Count; i++)
                if (drops.Radius(i) > drops.Radius(largest)) largest = i;
            return largest;
        }
    }
}
