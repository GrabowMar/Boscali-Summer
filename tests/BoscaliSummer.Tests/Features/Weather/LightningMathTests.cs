using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class LightningMathTests
    {
        public static void Run()
        {
            TestAssert.That(float.IsPositiveInfinity(LightningMath.NextDelay(0.5f, 0.5f)),
                "Fair weather must never schedule lightning");
            float gap = LightningMath.NextDelay(0.5f, 1f);
            TestAssert.That(gap >= 3f && gap <= 12f,
                "Full storm strikes every few seconds: " + gap);
            TestAssert.That(LightningMath.NextDelay(0.5f, 0.65f) > gap,
                "Weaker rain must strike less often");
            TestAssert.That(Math.Abs(LightningMath.FlashEnvelope(0f) - 1f) < 0.001f,
                "Flash peaks at the bolt");
            TestAssert.That(LightningMath.FlashEnvelope(0.3f) < LightningMath.FlashEnvelope(0.1f),
                "Flash must decay after the strokes");
            TestAssert.That(LightningMath.FlashEnvelope(-1f) == 0f,
                "No flash before the bolt");
            TestAssert.That(Math.Abs(LightningMath.ThunderDelay(343f) - 1f) < 0.01f,
                "Thunder travels at sound speed");
            TestAssert.That(LightningMath.ThunderGain(100f, 1f) > LightningMath.ThunderGain(1400f, 1f),
                "Close thunder must hit harder than distant");
            TestAssert.That(LightningMath.ThunderGain(500f, 0f) == 0f,
                "Muted master must mute thunder");
        }
    }
}
