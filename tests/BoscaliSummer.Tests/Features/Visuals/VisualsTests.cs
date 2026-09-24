using System;
using BoscaliSummer.Features.Visuals.Domain;

namespace BoscaliSummer.Tests.Features.Visuals
{
    internal static class VisualsTests
    {
        public static void Run()
        {
            GForceVignette_BelowThreshold_RemainsBaseline();
            GForceVignette_AboveThreshold_RampsTowardsMax();
            GForceSaturation_LossAtHighG();
            GForceRedout_NegativeGLoad();
            TransonicBlur_ScalesWithMachAndRoll();
            WindSwayDisplacement_RootAnchoredAtZero();
            WindSwayDisplacement_CanopySwaysNaturally();
        }

        private static void GForceVignette_BelowThreshold_RemainsBaseline()
        {
            float v = VisualsMath.CalculateGForceVignette(1.0f, 0.18f, 0.05f);
            TestAssert.That(Math.Abs(v - 0.18f) < 0.001f, "1G steady flight must retain baseline vignette");

            float vPull = VisualsMath.CalculateGForceVignette(3.0f, 0.18f, 0.05f);
            TestAssert.That(Math.Abs(vPull - 0.18f) < 0.001f, "3G maneuver below threshold must retain baseline vignette");
        }

        private static void GForceVignette_AboveThreshold_RampsTowardsMax()
        {
            float current = 0.18f;
            // Simulate 1 second of sustained 8.5G pull at 60 FPS
            for (int i = 0; i < 60; i++)
            {
                current = VisualsMath.CalculateGForceVignette(8.5f, current, 1f / 60f);
            }

            TestAssert.That(current > 0.80f, $"8.5G sustained pull must approach max blackout vignette, was {current}");
            TestAssert.That(current <= 0.85f, "Blackout vignette must not exceed ceiling");
        }

        private static void GForceSaturation_LossAtHighG()
        {
            float satNormal = VisualsMath.CalculateGForceSaturation(2.5f, 5f);
            TestAssert.That(Math.Abs(satNormal - 5f) < 0.01f, "Normal G must preserve color saturation");

            float satGreyout = VisualsMath.CalculateGForceSaturation(6.5f, 5f);
            TestAssert.That(satGreyout < 0f, $"6.5G must cause greying out (negative saturation), was {satGreyout}");

            float satBlackout = VisualsMath.CalculateGForceSaturation(8.5f, 5f);
            TestAssert.That(Math.Abs(satBlackout - (-100f)) < 0.01f, $"8.5G peak must cause total desaturation (-100), was {satBlackout}");
        }

        private static void GForceRedout_NegativeGLoad()
        {
            float redoutNormal = VisualsMath.CalculateGForceRedout(1.0f);
            TestAssert.That(redoutNormal == 0f, "Positive G must have zero redout");

            float redoutMild = VisualsMath.CalculateGForceRedout(-1.5f);
            TestAssert.That(redoutMild == 0f, "-1.5G threshold must have zero redout");

            float redoutSevere = VisualsMath.CalculateGForceRedout(-2.75f);
            TestAssert.That(Math.Abs(redoutSevere - 0.5f) < 0.05f, $"-2.75G must yield approximately 0.5 redout, was {redoutSevere}");

            float redoutExtreme = VisualsMath.CalculateGForceRedout(-5.0f);
            TestAssert.That(redoutExtreme == 1.0f, "Extreme negative G must clamp at 1.0 redout");
        }

        private static void TransonicBlur_ScalesWithMachAndRoll()
        {
            float blurSubsonic = VisualsMath.CalculateTransonicBlur(0.70f, 0f);
            TestAssert.That(blurSubsonic == 0f, "Subsonic cruise must have zero motion blur");

            float blurTransonic = VisualsMath.CalculateTransonicBlur(0.95f, 0f);
            TestAssert.That(blurTransonic > 0.10f, $"Mach 0.95 straight flight must engage mild motion blur, was {blurTransonic}");

            float blurSupersonicRoll = VisualsMath.CalculateTransonicBlur(1.20f, 180f);
            TestAssert.That(blurSupersonicRoll > blurTransonic, "Supersonic rolling break must intensify motion blur");
        }

        private static void WindSwayDisplacement_RootAnchoredAtZero()
        {
            var (dxBase, dzBase) = VisualsMath.CalculateWindSwayDisplacement(10.0f, 100f, 200f, 0f, 16f);
            TestAssert.That(dxBase == 0f && dzBase == 0f, "Tree root at ground level (localY = 0) must have exactly zero displacement");

            var (dxUnder, dzUnder) = VisualsMath.CalculateWindSwayDisplacement(10.0f, 100f, 200f, -2f, 16f);
            TestAssert.That(dxUnder == 0f && dzUnder == 0f, "Negative localY must have zero displacement");
        }

        private static void WindSwayDisplacement_CanopySwaysNaturally()
        {
            var (dxCanopy1, dzCanopy1) = VisualsMath.CalculateWindSwayDisplacement(2.5f, 100f, 200f, 16f, 16f);
            TestAssert.That(Math.Abs(dxCanopy1) > 0.01f || Math.Abs(dzCanopy1) > 0.01f, "Canopy at tree top must sway");

            // Spatial phase shift: distant tree at same height and time must have different displacement phase
            var (dxCanopy2, dzCanopy2) = VisualsMath.CalculateWindSwayDisplacement(2.5f, 500f, 800f, 16f, 16f);
            TestAssert.That(dxCanopy1 != dxCanopy2 || dzCanopy1 != dzCanopy2, "Distant trees must have traveling wave phase differences");
        }
    }
}
