using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Tests;
using UnityEngine;

namespace BoscaliSummer.Tests;

internal static class FlightEnvironmentSnapshotTests
{
    public static void Run()
    {
        TestAssert.That(!default(FlightEnvironmentSnapshot).MatchesView(100, 10, 20), "default environment must be unavailable");
        var current = new FlightEnvironmentSnapshot(100, 1, 10, 20, 50, true, true,
            0.4f, 0.5f, 0.2f, 1f, 12f, 9f, default(Vector3), 2f, 0.1f, 0.6f, 0.3f);
        TestAssert.That(current.MatchesView(100, 10, 20), "current view environment");
        TestAssert.That(current.MatchesView(101, 10, 20), "previous completed frame allowed");
        TestAssert.That(!current.MatchesView(102, 10, 20), "stale environment rejected");
        TestAssert.That(!current.MatchesView(99, 10, 20), "future environment rejected");
        TestAssert.That(!current.MatchesView(100, 11, 20), "different aircraft rejected");
        TestAssert.That(!current.MatchesView(100, 10, 21), "different camera rejected");
        TestAssert.That(current.Precipitation01 != current.Condensation01, "rain and condensation remain distinct");
    }
}
