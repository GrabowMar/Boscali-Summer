using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class WarzoneDressingMathTests
    {
        public static void Run()
        {
            // Deterministic: same name, same cluster, every peer.
            WarzoneDressingMath.ClusterLayout("BoscaliSummer:Garrison:7", out float a1, out float r1, out int v1);
            WarzoneDressingMath.ClusterLayout("BoscaliSummer:Garrison:7", out float a2, out float r2, out int v2);
            TestAssert.That(a1 == a2 && r1 == r2 && v1 == v2, "same identity lays out identically");

            // Ranges hold across a sweep of names.
            for (int i = 0; i < 64; i++)
            {
                WarzoneDressingMath.ClusterLayout("BoscaliSummer:Garrison:" + i, out float a, out float r, out int v);
                TestAssert.That(a >= 0f && a < 6.2833f, "angle stays in a full turn");
                TestAssert.That(r >= WarzoneDressingMath.MinRadius && r < WarzoneDressingMath.MaxRadius, "radius stays off the roof");
                TestAssert.That(v >= 0 && v < WarzoneDressingMath.VariantCount, "variant stays known");
            }

            // Null and empty names still lay out.
            WarzoneDressingMath.ClusterLayout(null, out float an, out float rn, out int vn);
            TestAssert.That(rn >= WarzoneDressingMath.MinRadius && vn >= 0, "null identity lays out");
            WarzoneDressingMath.ClusterLayout(string.Empty, out float ae, out float re, out int ve);
            TestAssert.That(an == ae && rn == re && vn == ve, "null matches empty");

            // Bounds are sane for street dressing.
            TestAssert.That(WarzoneDressingMath.MaxClusters == 48, "cluster ceiling holds");
            TestAssert.That(WarzoneDressingMath.MinRadius >= 6f, "clusters clear the walls");
        }
    }
}
