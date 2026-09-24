using System;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class RainAudioMathTests
    {
        public static void Run()
        {
            // 1. Volume response curves
            TestAssert.That(RainAudioMath.HissVolume(1f, 0f, 1f, true) == 0f, "Dry hiss must be silent");
            TestAssert.That(RainAudioMath.HissVolume(1f, 1f, 0f, false) == 0f, "Muted master must silence hiss");
            TestAssert.That(Math.Abs(RainAudioMath.HissVolume(1f, 1f, 1f, true) - 0.03f) < 0.0001f,
                "Full storm cockpit hiss must stay subdued at 0.03");
            TestAssert.That(Math.Abs(RainAudioMath.HissVolume(1f, 1f, 1f, false) - 0.20f) < 0.0001f,
                "Full storm external hiss must be 0.20");
            TestAssert.That(RainAudioMath.HissVolume(2f, 1f, 1f, false) == RainAudioMath.HissVolume(1f, 1f, 1f, false),
                "Speed norm above 1 must clamp");
            TestAssert.That(RainAudioMath.HissVolume(1f, -1f, 1f, false) == 0f, "Negative intensity must clamp to 0");

            TestAssert.That(RainAudioMath.PatterVolume(1f, 1f, 1f, false) == 0f, "External patter must be silent");
            TestAssert.That(Math.Abs(RainAudioMath.PatterVolume(1f, 1f, 1f, true) - 0.65f) < 0.0001f,
                "Full storm cockpit patter must be 0.65");
            TestAssert.That(Math.Abs(RainAudioMath.PatterVolume(0f, 1f, 0.5f, true) - (0.35f * 0.65f * 0.5f)) < 0.0001f,
                "Patter scales with master volume");

            // 2. Pitch response
            TestAssert.That(Math.Abs(RainAudioMath.HissPitch(0f) - 0.95f) < 0.0001f, "Slow hiss pitch must be 0.95");
            TestAssert.That(Math.Abs(RainAudioMath.HissPitch(1f) - 1.25f) < 0.0001f, "Fast hiss pitch must be 1.25");
            TestAssert.That(Math.Abs(RainAudioMath.PatterPitch(0f) - 1f) < 0.0001f, "Patter must keep natural pitch at rest");
            TestAssert.That(Math.Abs(RainAudioMath.PatterPitch(1f) - 1f) < 0.0001f, "Airspeed must not turn water taps into chirps");

            // 3. Fade envelope: full-scale travel in exactly FadeSeconds, no overshoot
            TestAssert.That(RainAudioMath.FadeToward(0.2f, 0.9f, 0f) == 0.2f, "Zero dt must hold current");
            TestAssert.That(RainAudioMath.FadeToward(0f, 0.5f, RainAudioMath.FadeSeconds) == 0.5f,
                "Full fade window must reach nearby targets");
            TestAssert.That(Math.Abs(RainAudioMath.FadeToward(0f, 1f, RainAudioMath.FadeSeconds / 2f) - 0.5f) < 0.0001f,
                "Half fade window must travel half scale");
            TestAssert.That(RainAudioMath.FadeToward(0.5f, 0f, RainAudioMath.FadeSeconds) == 0f,
                "Fade-out must reach silence");
            TestAssert.That(RainAudioMath.FadeToward(0.9f, 1f, 10f) == 1f, "Fade must not overshoot target");

            // 4. Crossfade must remove the duplicated head, not play it twice at the seam.
            float[] buf = { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f };
            int frames = RainAudioMath.ApplyLoopCrossfade(buf, 1, 3);
            TestAssert.That(frames == 7 && buf[0] == 3f && buf[frames - 1] == 2f,
                "Loop wrap must continue from the blended head into the first retained frame");
            TestAssert.That(buf[1] == 4f && buf[3] == 6f, "Non-overlap body must be preserved");
            float[] stereo = { 0f, 10f, 1f, 11f, 2f, 12f, 3f, 13f, 4f, 14f, 5f, 15f };
            frames = RainAudioMath.ApplyLoopCrossfade(stereo, 2, 2);
            TestAssert.That(frames == 4 && stereo[0] == 2f && stereo[1] == 12f &&
                stereo[6] == 1f && stereo[7] == 11f, "Overlap must preserve channel alignment");
            float[] tiny = { 1f, 2f, 3f };
            TestAssert.That(RainAudioMath.ApplyLoopCrossfade(tiny, 1, 2) == 3 && tiny[0] == 1f,
                "Invalid overlap must leave the buffer intact");
            TestAssert.That(RainAudioMath.ApplyLoopCrossfade(null, 2, 4410) == 0, "Null is empty");

            // 5. Rain intensity resolution (shared by manager and forecast)
            TestAssert.That(WeatherForecast.ResolveRainIntensity(0.59f, null) == 0f, "Below threshold must be dry");
            TestAssert.That(WeatherForecast.ResolveRainIntensity(0.60f, null) == 0f, "At threshold must be dry");
            TestAssert.That(Math.Abs(WeatherForecast.ResolveRainIntensity(0.95f, null) - 1f) < 0.0001f, "Storm must be full rain");
            TestAssert.That(Math.Abs(WeatherForecast.ResolveRainIntensity(0.775f, null) - 0.5f) < 0.0001f,
                "Mid ramp must be half rain");
            TestAssert.That(WeatherForecast.ResolveRainIntensity(0f, 1.5f) == 1f, "Forced rain must clamp high");
            TestAssert.That(WeatherForecast.ResolveRainIntensity(1f, -0.5f) == 0f, "Forced rain must clamp low");
            TestAssert.That(WeatherForecast.ResolveRainIntensity(0.98f, 0f) == 0f, "Forced dry must win over storm");

            // 6. Transition rate multiplier
            TestAssert.That(WeatherForecast.TransitionRateMultiplier(0f) == 1f, "Unset duration must be neutral");
            TestAssert.That(WeatherForecast.TransitionRateMultiplier(120f) == 1f, "Reference duration must be neutral");
            TestAssert.That(WeatherForecast.TransitionRateMultiplier(60f) == 2f, "Half duration must double rates");
            TestAssert.That(WeatherForecast.TransitionRateMultiplier(240f) == 0.5f, "Double duration must halve rates");
        }
    }
}
