using System;
using BoscaliSummer.Features.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class ImmersionTests
    {
        public static void Run()
        {
            Head_RestsLevelAtOneG();
            Head_DipsUnderLoadAndBracesAtHighG();
            Head_LiftsUnderNegativeG();
            Head_LeansAwayFromSideForce();
            Head_StaysWithinLimits();
            Spring_SettlesWithoutOvershootingMuch();
            Spring_SurvivesAHitch();
            Shots_ScaleWithMomentum();
            Rumble_OnlyWhileRolling();
            Touchdown_HarderLandingShakesMore();
            SunGlare_HiddenBelowHorizonTerrainAndCloud();
        }

        private static void Head_RestsLevelAtOneG()
        {
            var (p, y, r) = ImmersionMath.HeadTarget(0f, 1f, 0f, 0f, 0f, 1f);
            TestAssert.That(Math.Abs(p) < 1e-4f && Math.Abs(y) < 1e-4f && Math.Abs(r) < 1e-4f, "1 G level flight must leave the head centred");
            TestAssert.That(ImmersionMath.HeadTarget(0.5f, 7f, 0.3f, 90f, 20f, 0f) == (0f, 0f, 0f), "strength 0 must disable the head");
        }

        private static void Head_DipsUnderLoadAndBracesAtHighG()
        {
            float three = ImmersionMath.HeadTarget(0f, 3f, 0f, 0f, 0f, 1f).pitch;
            float nine = ImmersionMath.HeadTarget(0f, 9f, 0f, 0f, 0f, 1f).pitch;
            TestAssert.That(three > 0.5f, $"3 G must dip the head, was {three}");
            TestAssert.That(nine > three && nine < three * 3f, $"9 G dips further but braces (not 3x), {three} vs {nine}");
        }

        private static void Head_LiftsUnderNegativeG()
        {
            TestAssert.That(ImmersionMath.HeadTarget(0f, -1f, 0f, 0f, 0f, 1f).pitch < -0.5f, "negative G must lift the head");
        }

        private static void Head_LeansAwayFromSideForce()
        {
            TestAssert.That(ImmersionMath.HeadTarget(0.3f, 1f, 0f, 0f, 0f, 1f).roll < 0f, "side force to the right must tilt the head left");
            TestAssert.That(ImmersionMath.HeadTarget(0f, 1f, 0f, 60f, 0f, 1f).roll > 0f, "a right roll leads the head right");
            TestAssert.That(ImmersionMath.HeadTarget(0f, 1f, 0f, 0f, 15f, 1f).yaw > 0f, "a right yaw looks right");
        }

        private static void Head_StaysWithinLimits()
        {
            var (p, y, r) = ImmersionMath.HeadTarget(3f, 14f, -4f, 400f, 200f, 2f);
            TestAssert.That(Math.Abs(p) <= ImmersionMath.MaxPitchDeg && Math.Abs(y) <= ImmersionMath.MaxYawDeg
                && Math.Abs(r) <= ImmersionMath.MaxRollDeg, $"head angles must stay clamped, got ({p}, {y}, {r})");
        }

        private static void Spring_SettlesWithoutOvershootingMuch()
        {
            float x = 0f, v = 0f, peak = 0f;
            for (int i = 0; i < 300; i++)
            {
                (x, v) = ImmersionMath.SpringStep(x, v, 3f, 1f / 144f);
                peak = Math.Max(peak, x);
            }
            TestAssert.That(Math.Abs(x - 3f) < 0.05f, $"spring must settle on target, was {x}");
            TestAssert.That(peak < 3f * 1.1f, $"spring overshoot must stay under 10%, peak {peak}");
        }

        private static void Spring_SurvivesAHitch()
        {
            var (x, v) = ImmersionMath.SpringStep(0f, 0f, 3f, 0.5f);
            TestAssert.That(!float.IsNaN(x) && Math.Abs(x) < 6f && Math.Abs(v) < 60f, $"a 0.5 s frame must not explode the spring ({x}, {v})");
        }

        private static void Shots_ScaleWithMomentum()
        {
            var small = ImmersionMath.ShotShake(0.1f * 1000f, 1f);
            var big = ImmersionMath.ShotShake(0.4f * 1000f, 1f);
            TestAssert.That(small.high > 0f && small.low == 0f, "a 20 mm round buzzes but does not thump");
            TestAssert.That(big.high > small.high && big.low > 0f, "a 30 mm round thumps");
            TestAssert.That(ImmersionMath.ShotShake(100f, 0f) == (0f, 0f), "strength 0 disables gun shake");
        }

        private static void Rumble_OnlyWhileRolling()
        {
            TestAssert.That(ImmersionMath.GroundRumble(0.5f, 1f) == (0f, 0f), "parked aircraft must not rumble");
            TestAssert.That(ImmersionMath.GroundRumble(60f, 1f).low > ImmersionMath.GroundRumble(15f, 1f).low, "faster roll rumbles more");
            TestAssert.That(ImmersionMath.GroundRumble(500f, 2f).low <= 0.36f + 1e-4f, "rumble is capped");
        }

        private static void Touchdown_HarderLandingShakesMore()
        {
            TestAssert.That(ImmersionMath.TouchdownShake(-4f, 1f) > ImmersionMath.TouchdownShake(-1f, 1f), "a firm landing thumps harder");
            TestAssert.That(ImmersionMath.TouchdownShake(-50f, 1f) <= 1f, "touchdown shake clamps at vanilla's maximum");
        }

        private static void SunGlare_HiddenBelowHorizonTerrainAndCloud()
        {
            TestAssert.That(ImmersionMath.SunVisibility(-5f, 0f, false) == 0f, "no glare with the sun below the horizon");
            TestAssert.That(ImmersionMath.SunVisibility(30f, 0f, true) == 0f, "terrain hides the glare");
            TestAssert.That(ImmersionMath.SunVisibility(30f, 1f, false) == 0f, "full cloud cover hides the glare");
            TestAssert.That(ImmersionMath.SunVisibility(30f, 0f, false) == 1f, "a clear high sun glares fully");
            float low = ImmersionMath.SunVisibility(1f, 0f, false);
            TestAssert.That(low > 0f && low < 1f, "the glare fades in near the horizon");
        }
    }
}
