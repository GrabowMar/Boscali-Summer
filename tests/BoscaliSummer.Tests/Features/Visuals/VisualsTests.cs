using System;
using BoscaliSummer.Features.Visuals.Domain;

namespace BoscaliSummer.Tests.Features.Visuals
{
    internal static class VisualsTests
    {
        public static void Run()
        {
            Redout_OnlyUnderNegativeG();
            Strain_OnlyUnderHeavyPositiveG();
            TreeSway_RootsStayPut();
            TreeSway_CrownMovesAndLeansDownwind();
            TreeSway_NeighbouringCrownsDriftOutOfStep();
            TreeSway_StaysBoundedInAGale();
            TreeSway_CalmAirStillBreathes();
            GrassWind_ScalesWithWindAndDial();
        }

        private static void Redout_OnlyUnderNegativeG()
        {
            TestAssert.That(VisualsMath.CalculateGForceRedout(1f) == 0f, "1 G must not red out");
            TestAssert.That(VisualsMath.CalculateGForceRedout(9f) == 0f, "positive G belongs to the game's own G-LOC, not red-out");
            TestAssert.That(VisualsMath.CalculateGForceRedout(-1.5f) == 0f, "-1.5 G is the red-out threshold");
            float mid = VisualsMath.CalculateGForceRedout(-2.75f);
            TestAssert.That(Math.Abs(mid - 0.5f) < 0.05f, $"-2.75 G must be about half red-out, was {mid}");
            TestAssert.That(VisualsMath.CalculateGForceRedout(-6f) == 1f, "extreme negative G clamps at 1");
        }

        private static void Strain_OnlyUnderHeavyPositiveG()
        {
            TestAssert.That(VisualsMath.CalculateGStrain(4f) == 0f, "moderate G must not fringe");
            TestAssert.That(VisualsMath.CalculateGStrain(-5f) == 0f, "negative G must not fringe");
            float seven = VisualsMath.CalculateGStrain(7f);
            TestAssert.That(seven > 0.3f && seven < 0.7f, $"7 G must fringe partly, was {seven}");
            TestAssert.That(VisualsMath.CalculateGStrain(12f) == 1f, "strain clamps at 1");
        }

        private static void TreeSway_RootsStayPut()
        {
            for (float t = 0f; t < 20f; t += 0.37f)
            {
                var (dx, dy, dz) = VisualsMath.TreeSway(t, 3f, 0f, -4f, 26f, 1f, 0f, 1.5f);
                TestAssert.That(dx == 0f && dy == 0f && dz == 0f, "a vertex on the ground plane must not move");
                var buried = VisualsMath.TreeSway(t, 3f, -6f, -4f, 26f, 1f, 0f, 1.5f);
                TestAssert.That(buried == (0f, 0f, 0f), "buried trunk vertices must not move");
            }
            var low = VisualsMath.TreeSway(3f, 3f, 1f, -4f, 26f, 1f, 0f, 1.5f);
            TestAssert.That(Math.Abs(low.dx) < 0.01f && Math.Abs(low.dz) < 0.01f, "the lower trunk barely moves");
        }

        private static void TreeSway_CrownMovesAndLeansDownwind()
        {
            float sumAlong = 0f, max = 0f;
            for (int i = 0; i < 400; i++)
            {
                float t = i * 0.05f;
                var (dx, _, dz) = VisualsMath.TreeSway(t, 5f, 24f, 2f, 26f, 0f, 1f, 1f);
                sumAlong += dz;
                max = Math.Max(max, (float)Math.Sqrt(dx * dx + dz * dz));
            }
            TestAssert.That(max > 0.2f, $"a crown top must visibly sway, peak {max}");
            TestAssert.That(sumAlong / 400f > 0.05f, $"the crown must lean downwind on average, mean {sumAlong / 400f}");
        }

        private static void TreeSway_NeighbouringCrownsDriftOutOfStep()
        {
            var a = VisualsMath.TreeSway(4f, 0f, 20f, 0f, 26f, 1f, 0f, 1f);
            var b = VisualsMath.TreeSway(4f, 20f, 20f, 15f, 26f, 1f, 0f, 1f);
            TestAssert.That(Math.Abs(a.dx - b.dx) > 0.02f, "crowns 20 m apart must not move in lockstep");

            // Two vertices of one crown, 0.5 m apart, must move almost together.
            var c = VisualsMath.TreeSway(4f, 7f, 20f, 3f, 26f, 1f, 0f, 1f);
            var d = VisualsMath.TreeSway(4f, 7.5f, 20f, 3f, 26f, 1f, 0f, 1f);
            TestAssert.That(Math.Abs(c.dx - d.dx) < 0.15f, "adjacent vertices must not tear the crown apart");
        }

        private static void TreeSway_StaysBoundedInAGale()
        {
            float amplitude = VisualsMath.TreeSwayAmplitude(60f) * 2.5f;
            for (float t = 0f; t < 60f; t += 0.21f)
            {
                var (dx, dy, dz) = VisualsMath.TreeSway(t, 11f, 26f, -9f, 26f, 0.6f, 0.8f, amplitude);
                TestAssert.That(Math.Abs(dx) < 8f && Math.Abs(dz) < 8f && Math.Abs(dy) < 4f,
                    $"max dial in a gale must stay bounded, got ({dx}, {dy}, {dz})");
            }
        }

        private static void TreeSway_CalmAirStillBreathes()
        {
            TestAssert.That(VisualsMath.TreeSwayAmplitude(0f) > 0.1f, "calm air still moves crowns a little");
            TestAssert.That(VisualsMath.TreeSwayAmplitude(15f) > VisualsMath.TreeSwayAmplitude(3f), "stronger wind sways more");
            TestAssert.That(VisualsMath.TreeSwayAmplitude(500f) <= 1.8f, "amplitude is capped");
        }

        private static void GrassWind_ScalesWithWindAndDial()
        {
            float calm = VisualsMath.GrassWindStrength(0.25f, 0f, 1f);
            float windy = VisualsMath.GrassWindStrength(0.25f, 12f, 1f);
            TestAssert.That(windy > calm, "grass must wave harder in wind");
            TestAssert.That(VisualsMath.GrassWindStrength(0.25f, 12f, 2f) > windy, "the dial scales grass too");
            TestAssert.That(VisualsMath.GrassWindSpeed(2.12f, 40f) <= 2.12f * 1.8f + 0.001f, "grass speed is capped");
        }
    }
}
