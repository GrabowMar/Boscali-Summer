using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    internal static class GroundConvoyTests
    {
        public static void Run()
        {
            foreach (bool reverse in new[] { false, true })
            {
                TaxiGraph graph = TaxiGraph.Build(new AirbaseSample { Roads = new[] { new[] { Vec3.Zero, Vec3.Forward * 100f } } });
                var positions = new Dictionary<int, Vec3>();
                var claims = new TaxiReservations(graph, positions);
                int[] nodes = reverse ? new[] { 1, 0 } : new[] { 0, 1 }, edges = { 0 };
                for (int owner = 1; owner <= 3; owner++)
                {
                    positions[owner] = Vec3.Forward * (reverse ? 100f - owner * 25f : owner * 25f);
                    claims.TryAdvance(owner, TaxiPriority.Departing, nodes, edges, 0, 1, claimStart: false, lastNode: false);
                }
                TestAssert.Eq(claims.TryAdvance(3, TaxiPriority.Departing, nodes, edges, 0, 1, claimStart: false), 2,
                    "the physical convoy leader claims the far node even when a rear aircraft registered first");
                TestAssert.Eq(claims.OwnerOfNode(nodes[1]), 3, "the far node remains exclusive to the front aircraft");
                TestAssert.Eq(claims.TryAdvance(1, TaxiPriority.Departing, nodes, edges, 0, 1, claimStart: false), 1,
                    "a rear aircraft still yields the far node");
                TestAssert.Eq(claims.WaitingFor(1), 3, "the rear aircraft waits for the physical leader");
                claims.ReleaseAll(3);
                TestAssert.Eq(claims.TryAdvance(2, TaxiPriority.Departing, nodes, edges, 0, 1, claimStart: false), 2,
                    "the next physical aircraft leads when the first leaves");
            }
        }
    }
}
