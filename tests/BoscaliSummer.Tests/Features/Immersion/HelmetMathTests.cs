using System;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class HelmetMathTests
    {
        public static void Run()
        {
            float wu, wv;
            HelmetMath.Warp(.5f, .5f, 1f, out wu, out wv);
            TestAssert.That(Math.Abs(wu - .5f) < 1e-5f && Math.Abs(wv - .5f) < 1e-5f, "The visor warp leaves boresight where it is.");
            HelmetMath.Warp(.9f, .2f, 0f, out wu, out wv);
            TestAssert.That(Math.Abs(wu - .9f) < 1e-5f && Math.Abs(wv - .2f) < 1e-5f, "Zero curvature is the flat visor.");
            HelmetMath.Warp(1f, .5f, .55f, out wu, out wv);
            TestAssert.That(wu > 1f && Math.Abs(wv - .5f) < 1e-5f, "The periphery bows outward on a curved visor.");

            TestAssert.That(HelmetMath.Vignette(0f, 1f) == 1f && HelmetMath.Vignette(1.45f, 1f) < .7f && HelmetMath.Vignette(1.4f, 0f) == 1f,
                "The visor vignette darkens only the periphery, and only with strength.");

            TestAssert.That(Math.Abs(HelmetMath.Fresnel(1f) - HelmetMath.FresnelF0) < 1e-5f && HelmetMath.Fresnel(0f) > .999f && HelmetMath.Fresnel(.5f) < .1f,
                "Visor Fresnel is F0 head-on and reflects at grazing angles.");

            TestAssert.That(HelmetMath.ElectroTint(-20f, 1f, 1f) == 0f && HelmetMath.ElectroTint(30f, 1f, 1f) == 1f &&
                HelmetMath.ElectroTint(30f, 0f, 1f) < HelmetMath.ElectroTint(30f, 1f, 1f),
                "The sun visor darkens with the sun in view and stays clear at night.");

            // cos .9 keeps every value below the clamp, so the comparisons below are real.
            float day = HelmetMath.ReflectionOpacity(.9f, 1f, 1f, 1f), night = HelmetMath.ReflectionOpacity(.9f, 0f, 1f, 1f);
            TestAssert.That(night > day && day > 0f && HelmetMath.ReflectionOpacity(.9f, 0f, 0f, 1f) == 0f && HelmetMath.ReflectionOpacity(.9f, 0f, 1f, 0f) == 0f,
                "The canopy mirror is stronger at night and off when turned off or seen edge-on.");

            TestAssert.That(HelmetMath.Sanitize(float.NaN, .3f) == .3f && HelmetMath.Sanitize(2f, .3f) == 1f && HelmetMath.Sanitize(-1f, .3f) == 0f,
                "Settings outside 0..1 are clamped and invalid ones fall back.");
        }
    }
}
