using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>
    /// JTAC mark resolution (pure selection over candidate positions) and the wire
    /// identity pins for the mark/unlase actions and the NoMarkTarget result.
    /// Live HQ lase/refcount lifetime and network delivery still need game verification.
    /// </summary>
    internal static class JtacMarkTests
    {
        public static void Run()
        {
            CheckWireIds();
            CheckSelectNearest();
            CheckRadiusFor();
        }

        private static void CheckWireIds()
        {
            TestAssert.That((byte)SupportActionId.JtacMark == 28, "JtacMark rides the wire as 28");
            TestAssert.That((byte)SupportActionId.JtacUnlase == 29, "JtacUnlase rides the wire as 29");
            TestAssert.That((byte)SupportResult.NoMarkTarget == 45, "NoMarkTarget rides the wire as 45");
        }

        private static void CheckSelectNearest()
        {
            var empty = new List<MarkCandidate>();
            TestAssert.That(JtacResolve.SelectNearest(empty, 0f, 0f, JtacResolve.MarkRadius) < 0, "empty ground resolves to no one");
            TestAssert.That(JtacResolve.SelectNearest(null, 0f, 0f, JtacResolve.MarkRadius) < 0, "null resolves to no one");
            TestAssert.That(JtacResolve.SelectNearest(empty, 0f, 0f, 0f) < 0, "a dead radius resolves to no one");

            var one = new List<MarkCandidate> { new MarkCandidate(100f, 0f) };
            TestAssert.That(JtacResolve.SelectNearest(one, 0f, 0f, JtacResolve.MarkRadius) == 0, "the one in radius wins");
            TestAssert.That(JtacResolve.SelectNearest(one, 0f, 0f, 50f) < 0, "outside the radius resolves to no one");

            var two = new List<MarkCandidate> { new MarkCandidate(400f, 0f), new MarkCandidate(100f, 0f) };
            TestAssert.That(JtacResolve.SelectNearest(two, 0f, 0f, JtacResolve.MarkRadius) == 1, "the nearest wins, not the first");
            TestAssert.That(JtacResolve.SelectNearest(two, 400f, 0f, JtacResolve.MarkRadius) == 0, "selection follows the mark");

            var edge = new List<MarkCandidate> { new MarkCandidate(JtacResolve.MarkRadius, 0f) };
            TestAssert.That(JtacResolve.SelectNearest(edge, 0f, 0f, JtacResolve.MarkRadius) == 0, "the rim is inside");

            var crowd = new List<MarkCandidate>();
            for (int i = 0; i < JtacResolve.MaxCandidates; i++) crowd.Add(new MarkCandidate(10000f, 0f));
            crowd.Add(new MarkCandidate(0f, 0f));
            TestAssert.That(JtacResolve.SelectNearest(crowd, 0f, 0f, JtacResolve.MarkRadius) < 0,
                "a perfect candidate beyond the cap must not be examined");
            var malformed = new List<MarkCandidate> { new MarkCandidate(float.NaN, 0f), new MarkCandidate(100f, 0f) };
            TestAssert.That(JtacResolve.SelectNearest(malformed, 0f, 0f, JtacResolve.MarkRadius) == 1,
                "invalid candidates cannot displace a valid nearest unit");
            TestAssert.That(JtacResolve.SelectNearest(one, float.NaN, 0f, JtacResolve.MarkRadius) < 0 &&
                JtacResolve.SelectNearest(one, 0f, 0f, float.NaN) < 0, "unknown aim or radius finds no unit");
        }

        private static void CheckRadiusFor()
        {
            TestAssert.That(JtacResolve.RadiusFor(1f) == JtacResolve.MarkRadius, "best intel uses the full radius");
            TestAssert.That(JtacResolve.RadiusFor(0f) == JtacResolve.MarkRadiusFloor, "dead intel bottoms at the floor");
            TestAssert.That(JtacResolve.RadiusFor(9f) == JtacResolve.MarkRadius, "quality clamps high");
            TestAssert.That(JtacResolve.RadiusFor(-9f) == JtacResolve.MarkRadiusFloor, "quality clamps low");
            float mid = JtacResolve.RadiusFor(0.5f);
            TestAssert.That(mid > JtacResolve.MarkRadiusFloor && mid < JtacResolve.MarkRadius, "mid intel interpolates");
        }
    }
}
