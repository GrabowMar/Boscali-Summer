using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins HAWC-X "SKIP": skip clock, glow rule, sheath emission, shift reach, flap trim, HUD text.</summary>
    internal static class HawcxTests
    {
        public static void Run()
        {
            TestAssert.That(GlideProfile.ApexAge(1) == 29.5f && GlideProfile.ApexAge(2) == 59.5f, "skip apexes at 29.5 s and 59.5 s");
            TestAssert.That(GlideProfile.SkipAt(23.4f) == 0 && GlideProfile.SkipAt(23.6f) == 1 && GlideProfile.SkipAt(35.5f) == 1 &&
                GlideProfile.SkipAt(40f) == 0 && GlideProfile.SkipAt(55f) == 2 && GlideProfile.SkipAt(66f) == 0 && GlideProfile.SkipAt(89.5f) == 0,
                "two +-6 s apex windows, no third skip");

            TestAssert.That(GlideProfile.Plan(5f, 300000f, 2000f).Altitude == 24000f, "boost climbs to 24 km");
            TestAssert.That(GlideProfile.Plan(21.9f, 300000f, 2000f).Phase == GlidePhase.Boost &&
                GlideProfile.Plan(22.1f, 300000f, 20000f).Phase == GlidePhase.Glide, "boost ends at 22 s");
            TestAssert.That(Mathf.Abs(GlideProfile.Plan(29.5f, 300000f, 24000f).Altitude - 27000f) < 1f &&
                Mathf.Abs(GlideProfile.Plan(44.5f, 300000f, 24000f).Altitude - 21000f) < 1f, "skip swings 21..27 km");
            TestAssert.That(GlideProfile.Plan(90f, 300000f, 24000f).Altitude == 24000f, "flat glide after the second skip");
            TestAssert.That(GlideProfile.Plan(30f, 28000f, 24000f).Phase == GlidePhase.Terminal &&
                GlideProfile.Plan(30f, 29000f, 24000f).Phase == GlidePhase.Glide, "terminal inside 1.2x altitude (28.8 km at 24 km)");
            TestAssert.That(GlideProfile.Plan(40f, 45000f, 15000f, 0f, true).Phase == GlidePhase.Terminal &&
                GlideProfile.Plan(10f, 300000f, 2000f, 0f, true).Phase == GlidePhase.Terminal, "terminal latches past a nudge or the 1.2x line");

            TestAssert.That(GlideProfile.Glow(GlidePhase.Glide, 29.5f, 1000f, 15000f) && GlideProfile.Glow(GlidePhase.Glide, 59.5f, 1000f, 15000f),
                "skip apexes glow even when slow");
            TestAssert.That(!GlideProfile.Glow(GlidePhase.Terminal, 59.5f, 1000f, 15000f) && !GlideProfile.Glow(GlidePhase.Boost, 10f, 1000f, 15000f) &&
                !GlideProfile.Glow(GlidePhase.Glide, 45f, 1000f, 24000f), "no glow off the skip clock, in boost, or in the final below the plasma line");
            TestAssert.That(GlideProfile.Glow(GlidePhase.Terminal, 70f, 2100f, 20500f) && !GlideProfile.Glow(GlidePhase.Terminal, 70f, 2100f, 19500f) &&
                GlideProfile.Glow(GlidePhase.Glide, 45f, 2100f, 24000f) && !GlideProfile.Glow(GlidePhase.Glide, 45f, 1900f, 24000f) &&
                GlideProfile.Glow(GlidePhase.Boost, 20f, 2100f, 21000f), "plasma glow above 2000 m/s and 20 km in any phase");

            TestAssert.That(GlideProfile.Glow(GlidePhase.Glide, 45f, 1950f, 24000f, true) && !GlideProfile.Glow(GlidePhase.Glide, 45f, 1950f, 24000f) &&
                !GlideProfile.Glow(GlidePhase.Glide, 45f, 1850f, 24000f, true) && GlideProfile.Glow(GlidePhase.Terminal, 70f, 2100f, 19800f, true) &&
                !GlideProfile.Glow(GlidePhase.Terminal, 70f, 2100f, 19400f, true), "plasma glow holds to 1900 m/s / 19.5 km once lit (no flicker)");

            TestAssert.That(GlideProfile.SheathGlow(1500f) == 0f && Mathf.Abs(GlideProfile.SheathGlow(2100f) - 0.5f) < 1e-4f &&
                GlideProfile.SheathGlow(3600f) == 1f, "sheath emission clamp01((v-1500)/1200)");

            TestAssert.That(GlideProfile.CanNudge(20000f) && !GlideProfile.CanNudge(20001f), "shift reach 20 km");
            TestAssert.That(GlideProfile.ShiftText(3240f) == "SHIFTED +3.2 km" && GlideProfile.ShiftText(0f) == "SHIFTED +0.0 km", "shift HUD text");

            TestAssert.That(GlideProfile.FlapAngle(GlidePhase.Boost, 10f) == 0f && GlideProfile.FlapAngle(GlidePhase.Terminal, 10f) == 18f &&
                Mathf.Abs(GlideProfile.FlapAngle(GlidePhase.Glide, 22f)) < 1e-3f &&
                Mathf.Abs(GlideProfile.FlapAngle(GlidePhase.Glide, 44.5f) - 10f) < 1e-3f &&
                Mathf.Abs(GlideProfile.FlapAngle(GlidePhase.Glide, 29.5f) + 10f) < 1e-3f &&
                Mathf.Abs(GlideProfile.FlapAngle(GlidePhase.Glide, 81.99f)) < 0.05f && GlideProfile.FlapAngle(GlidePhase.Glide, 83f) == 0f,
                "flaps follow the skip curvature (0 at both ends, +10 at the trough pull-up), bite in the dive");
        }
    }
}
