using BoscaliSummer.Fire;

namespace BoscaliSummer.Tests.Features.FireAndDestruction
{
    internal static class FireScorchTests
    {
        public static void Run()
        {
            TestAssert.That(
                FireScorchPolicy.TreeClearBlastRadius(1f) == FireScorchPolicy.TreeClearBlastBaseRadius,
                "a fresh fire must use the smallest tree-clearing blast");
            TestAssert.That(
                FireScorchPolicy.TreeClearBlastRadius(1f) < 1f,
                "a single fire spawn must remove less than a metre of trees");
            TestAssert.That(
                FireScorchPolicy.BurnMarkRadius(1f) >= 200f,
                "the ash bed must be nuke-scale or it disappears into one blast-map texel");
            TestAssert.That(
                FireScorchPolicy.ScarDiameter(1f) >= 30f,
                "a fresh burn site must still leave a readable soot decal");
            TestAssert.That(
                FireScorchPolicy.ScarLobeDownwind * 1.2f < 1f &&
                FireScorchPolicy.ScarLobeCrosswind * 1.2f < 1f,
                "lobe offsets must stay under one scar diameter so the decals overlap");
            TestAssert.That(
                FireScorchPolicy.ScarLobeDownwind >= FireScorchPolicy.ScarLobeCrosswind,
                "the soot scar must stretch downwind, not across it");

            for (float scale = 0f; scale <= 4f; scale += 0.1f)
            {
                float tree = FireScorchPolicy.TreeClearBlastRadius(scale);
                float mark = FireScorchPolicy.BurnMarkRadius(scale);
                float scar = FireScorchPolicy.ScarDiameter(scale);
                TestAssert.That(
                    tree >= FireScorchPolicy.TreeClearBlastBaseRadius &&
                    tree <= FireScorchPolicy.TreeClearBlastBaseRadius * 1.3f + 0.0001f,
                    "tree-clearing radius must stay inside the 0.72..0.936 m band");
                TestAssert.That(
                    mark >= FireScorchPolicy.BurnMarkBaseRadius &&
                    mark <= FireScorchPolicy.BurnMarkBaseRadius * 1.3f + 0.0001f,
                    "ash-bed radius must stay inside the 260..338 m band");
                TestAssert.That(
                    scar >= FireScorchPolicy.ScarBaseDiameter &&
                    scar <= FireScorchPolicy.ScarBaseDiameter * 1.3f + 0.0001f,
                    "soot decal diameter must stay inside the 30..39 m band");
            }

            TestAssert.That(
                FireScorchPolicy.TreeClearBlastRadius(3f) > FireScorchPolicy.TreeClearBlastRadius(1f),
                "a grown fire front must consume more ground than a fresh ignition");
            TestAssert.That(
                FireScorchPolicy.TreeClearBlastRadius(-10f) == FireScorchPolicy.TreeClearBlastRadius(1f),
                "cluster scale below one must clamp to the fresh-fire case");
            TestAssert.That(
                FireScorchPolicy.TreeClearBlastRadius(99f) == FireScorchPolicy.TreeClearBlastRadius(3f),
                "cluster scale above three must clamp to the maximum case");
            TestAssert.That(
                FireScorchPolicy.ScarDiameter(2f) > FireScorchPolicy.ScarDiameter(1f) &&
                FireScorchPolicy.ScarDiameter(3f) > FireScorchPolicy.ScarDiameter(2f),
                "the soot decal must grow monotonically with the fire front");
        }
    }
}
