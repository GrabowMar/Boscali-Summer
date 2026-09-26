using System;
using BoscaliSummer.Features.UrbanCombat.Audio;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class UrbanAmbienceMathTests
    {
        public static void Run()
        {
            // Siren band: full inside the city, released past the outer edge.
            TestAssert.That(UrbanAmbienceMath.SirenInnerMeters < UrbanAmbienceMath.SirenOuterMeters, "siren band is ordered");
            TestAssert.That(UrbanAmbienceMath.SirenOuterMeters < UrbanAmbienceMath.SirenReleaseMeters, "release trails the outer edge");

            // Siren base volume: Unity's 3D falloff handles distance, so the curve is
            // master-only.
            TestAssert.That(UrbanAmbienceMath.SirenVolume(1f) == UrbanAmbienceMath.SirenLevel, "siren level");
            TestAssert.That(UrbanAmbienceMath.SirenVolume(0f) == 0f, "zero master mutes the siren");
            TestAssert.That(UrbanAmbienceMath.SirenVolume(-1f) == 0f, "negative master mutes the siren");
            TestAssert.That(Math.Abs(UrbanAmbienceMath.SirenVolume(0.7f) - UrbanAmbienceMath.SirenLevel * 0.7f) < 1e-6f, "default master scales");

            // Anchor hysteresis: the voice only hops to a clearly nearer city.
            TestAssert.That(UrbanAmbienceMath.ShouldSwitchAnchor(1000f, 800f), "clearly nearer city takes the voice");
            TestAssert.That(!UrbanAmbienceMath.ShouldSwitchAnchor(1000f, 900f), "marginally nearer city keeps the voice");
            TestAssert.That(!UrbanAmbienceMath.ShouldSwitchAnchor(1000f, 1000f), "tied city keeps the voice");
            TestAssert.That(!UrbanAmbienceMath.ShouldSwitchAnchor(1000f, 1200f), "farther city keeps the voice");

            // Fades step toward the target and snap when close.
            TestAssert.That(UrbanAmbienceMath.FadeToward(0f, 1f, 0.25f) == 1f, "full fade quantum snaps");
            TestAssert.That(UrbanAmbienceMath.FadeToward(0f, 1f, 0.125f) == 0.5f, "half fade quantum steps");
            TestAssert.That(UrbanAmbienceMath.FadeToward(1f, 0f, 0.125f) == 0.5f, "fade out steps down");
            TestAssert.That(UrbanAmbienceMath.FadeToward(0.5f, 1f, 0f) == 0.5f, "zero delta holds");
            TestAssert.That(UrbanAmbienceMath.FadeToward(0.5f, 1f, -1f) == 0.5f, "negative delta holds");

            // Loop crossfade: degenerate inputs are rejected, valid loops shorten cleanly.
            TestAssert.That(UrbanAmbienceMath.ApplyLoopCrossfade(null, 2, 100) == 0, "null buffer fades nothing");
            TestAssert.That(UrbanAmbienceMath.ApplyLoopCrossfade(new float[8], 0, 2) == 0, "zero channels fade nothing");
            TestAssert.That(UrbanAmbienceMath.ApplyLoopCrossfade(new float[8], 2, 1) == 4, "one-sample fade is rejected");
            TestAssert.That(UrbanAmbienceMath.ApplyLoopCrossfade(new float[8], 2, 3) == 4, "oversize fade is rejected");
            float[] loop = { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f };
            TestAssert.That(UrbanAmbienceMath.ApplyLoopCrossfade(loop, 1, 2) == 6, "fade shortens the loop");
            TestAssert.That(loop[0] == 2f, "loop content rotates past the seam");
            TestAssert.That(Math.Abs(loop[4] - 6f) < 1e-5f && Math.Abs(loop[5] - 1f) < 1e-5f, "crossfaded tail blends head into tail");
            TestAssert.That(UrbanAmbienceMath.ApplyLoopCrossfade(new float[16], 2, 4) == 4, "stereo loop shortens");
            TestAssert.That(UrbanAmbienceMath.ApplyLoopCrossfade(new float[12], 1, 4) == 8, "mono siren loop shortens");
        }
    }
}
