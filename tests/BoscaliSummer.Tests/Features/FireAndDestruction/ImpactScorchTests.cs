using BoscaliSummer.Fire;

namespace BoscaliSummer.Tests.Features.FireAndDestruction
{
    internal static class ImpactScorchTests
    {
        public static void Run()
        {
            TestAssert.That(
                ImpactScorchPolicy.DecalSize(0f) == ImpactScorchPolicy.MinimumSize,
                "a zero-yield hit must still leave the smallest readable scorch");
            TestAssert.That(
                ImpactScorchPolicy.DecalSize(-50f) == ImpactScorchPolicy.MinimumSize,
                "a negative yield must clamp up to the minimum size");
            TestAssert.That(
                ImpactScorchPolicy.DecalSize(10000f) == ImpactScorchPolicy.MaximumSize,
                "a heavy bomb must clamp down to the maximum size");
            TestAssert.That(
                ImpactScorchPolicy.DecalSize(12f) > ImpactScorchPolicy.DecalSize(4f) &&
                ImpactScorchPolicy.DecalSize(4f) > ImpactScorchPolicy.DecalSize(0f),
                "scorch size must grow monotonically with blast yield inside the band");
            TestAssert.That(ImpactScorchPolicy.DecalCount(0.5f) == 1,
                "small impacts should use one scorch decal");
            TestAssert.That(ImpactScorchPolicy.DecalCount(1f) == 2,
                "medium impacts should use a two-mark scorch cluster");
            TestAssert.That(ImpactScorchPolicy.DecalCount(10f) == 3,
                "large impacts should use a bounded three-mark scorch cluster");
            for (float yield = -20f; yield <= 200f; yield += 7f)
            {
                float size = ImpactScorchPolicy.DecalSize(yield);
                TestAssert.That(
                    size >= ImpactScorchPolicy.MinimumSize && size <= ImpactScorchPolicy.MaximumSize,
                    "scorch size must stay inside the 4..16 m band for every yield");
            }

            for (uint i = 0; i < 4096; i++)
            {
                uint seed = BoscaliSummer.Core.Deterministic.Hash((int)i, (int)i * 31, -(int)i, 0x11);
                float tangent = ImpactScorchPolicy.TangentJitter(seed);
                float bitangent = ImpactScorchPolicy.BitangentJitter(seed);
                float roll = ImpactScorchPolicy.RollDegrees(seed);
                TestAssert.That(tangent >= -1f && tangent <= 1f, "tangent jitter escaped [-1, 1]");
                TestAssert.That(bitangent >= -1f && bitangent <= 1f, "bitangent jitter escaped [-1, 1]");
                TestAssert.That(roll >= 0f && roll < 360f, "roll degrees escaped [0, 360)");
            }

            uint sample = BoscaliSummer.Core.Deterministic.Hash(17, 91, -5, 0x11);
            TestAssert.That(
                ImpactScorchPolicy.TangentJitter(sample) == ImpactScorchPolicy.TangentJitter(sample),
                "jitter must be deterministic for a given seed");
            TestAssert.That(
                ImpactScorchPolicy.TangentJitter(sample) != ImpactScorchPolicy.BitangentJitter(sample),
                "the two jitter axes must be independent draws from one seed");

            float offset = ImpactScorchPolicy.JitterOffset(16f, 1f);
            TestAssert.That(
                offset <= 16f * 0.25f + 0.0001f && offset >= -16f * 0.25f - 0.0001f,
                "jitter offset must stay within a quarter of the mark so it still covers the hit");
            TestAssert.That(
                ImpactScorchPolicy.JitterOffset(16f, -1f) == -ImpactScorchPolicy.JitterOffset(16f, 1f),
                "jitter offset must be symmetric about the impact point");
        }
    }
}
