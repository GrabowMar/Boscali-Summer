using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class SunDiscMathTests
    {
        public static void Run()
        {
            TestAssert.That(Math.Abs(SunDiscMath.AirMass(90f, 0f) - 1f) < .01f && Math.Abs(SunDiscMath.AirMass(0f, 0f) - 38f) < 1f,
                "Air mass is 1 overhead and about 38 at the horizon.");
            TestAssert.That(SunDiscMath.AirMass(5f, 10000f) < SunDiscMath.AirMass(5f, 0f) * .35f, "Altitude thins the air on the line to the sun.");
            float last = -1f;
            for (float elevation = 0f; elevation <= 90f; elevation += 1f)
            {
                SunDiscMath.Transmission(elevation, 0f, out float r, out float g, out float b);
                TestAssert.That(r > g && g > b && b >= 0f && r <= 1f, "The air always takes blue first, at " + elevation);
                float bright = SunDiscMath.Brightness(elevation, 0f);
                TestAssert.That(bright >= last, "The sun brightens as it climbs, at " + elevation);
                last = bright;
            }
            SunDiscMath.Transmission(0f, 0f, out float red, out float green, out float blue);
            TestAssert.That(red > green * 5f && green > blue * 50f && red < .1f, "The setting sun is dim and deep orange-red.");
            SunDiscMath.Transmission(60f, 0f, out red, out green, out blue);
            TestAssert.That(blue > .7f && red < .93f, "The high sun is near white.");
            TestAssert.That(SunDiscMath.AboveHorizon(1f) == 1f && SunDiscMath.AboveHorizon(-.3f) == 0f &&
                Math.Abs(SunDiscMath.AboveHorizon(0f) - .5f) < .001f && SunDiscMath.AboveHorizon(float.NaN) == 0f,
                "The disc is whole above the horizon, half at it and gone below.");
            TestAssert.That(SunDiscMath.AirMass(float.NaN, 0f) == 1f, "Invalid input reads as clear air.");
        }
    }
}
