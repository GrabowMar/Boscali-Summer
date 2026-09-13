using System;
using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class ConstellationTests
    {
        public static void Run()
        {
            var fleet = new Constellation(100000f);

            TestAssert.That(fleet.Satellites.Count == 0, "a fresh constellation is empty");
            TestAssert.That(!fleet.Covers(SatelliteRole.Recon, 0f, 0f), "an empty constellation covers nothing");

            TestAssert.That(fleet.TryDeploy(SatelliteRole.Recon, 0, 20000f, -10000f, 1,
                out Satellite first, out OrbitalFailure failure) && failure == OrbitalFailure.None,
                "deploying inside capacity must succeed");
            TestAssert.That(first.Id == 1 && first.Fuel == Constellation.MaximumFuel,
                "a new satellite starts at id 1 with a full tank");
            TestAssert.That(first.State == SatelliteState.Stationed,
                "a deployed satellite starts on station");

            float swath = first.Orbit.Swath;
            TestAssert.That(fleet.Covers(SatelliteRole.Recon, 20000f, -10000f),
                "a satellite covers the station it was deployed over");
            TestAssert.That(fleet.SatelliteCovers(first, 20000f + swath * 0.8f, -10000f),
                "coverage includes the inside of the swath");
            TestAssert.That(!fleet.Covers(SatelliteRole.Recon, 20000f + swath * 2f, -10000f),
                "coverage ends outside the swath");
            TestAssert.That(!fleet.Covers(SatelliteRole.Strike, 20000f, -10000f),
                "only the matching role provides coverage");

            StationCoverage uncovered = fleet.Query(SatelliteRole.Recon, 20000f + swath * 2f, -10000f);
            TestAssert.That(!uncovered.Covered && uncovered.HasSatellite && uncovered.NearestGap > 0f,
                "an uncovered point reports the nearest gap");
            TestAssert.That(!fleet.Query(SatelliteRole.Ew, 0f, 0f).HasSatellite,
                "a role with no satellite reports no asset");

            TestAssert.That(!fleet.TryDeploy(SatelliteRole.Strike, 0, 0f, 0f, 1,
                out _, out failure) && failure == OrbitalFailure.AtCapacity,
                "deploying past capacity must report capacity");
            TestAssert.That(fleet.TryDeploy(SatelliteRole.Strike, 0, 0f, 0f, 4, out Satellite second, out _),
                "raising capacity must allow a second satellite");
            TestAssert.That(second.Id == 2, "satellite ids must stay monotonic");
            TestAssert.That(!fleet.TryDeploy(SatelliteRole.Ew, 9, 0f, 0f, 4, out _, out OrbitalFailure badAlt) &&
                            badAlt == OrbitalFailure.UnknownAltitude,
                "an unknown altitude must be refused");

            // A transfer costs fuel by distance, goes dark while moving and completes on tick.
            fleet.Tick(0f, false);
            float cost;
            TestAssert.That(fleet.TryRetask(first.Id, 20000f, -10000f, out _, out cost,
                out failure) && failure == OrbitalFailure.None,
                "a zero-distance retask must succeed");
            TestAssert.That(cost == 0f && first.State == SatelliteState.Stationed,
                "a zero-distance retask must not start a transfer");

            TestAssert.That(fleet.TryRetask(first.Id, 40000f, -10000f, out _, out cost,
                out failure) && failure == OrbitalFailure.None,
                "an affordable transfer must be accepted");
            TestAssert.That(cost > 0f && Math.Abs(cost - 20f * first.Orbit.FuelPerKm) < 0.01f,
                "a transfer must cost fuel per kilometre");
            TestAssert.That(first.State == SatelliteState.Transit, "a transfer must move the satellite");
            TestAssert.That(!fleet.Covers(SatelliteRole.Recon, 20000f, -10000f),
                "a satellite in transfer must not provide coverage");
            TestAssert.That(!fleet.Covers(SatelliteRole.Recon, 40000f, -10000f),
                "a transferring satellite does not cover its destination yet");

            float transit = first.TransitTotal;
            TestAssert.That(transit > 0f, "a transfer must have a duration");
            for (int i = 0; i < 2000 && first.State == SatelliteState.Transit; i++)
                fleet.Tick(0.25f, false);
            TestAssert.That(first.State == SatelliteState.Stationed, "a transfer must eventually complete");
            TestAssert.That(first.StationX == 40000f && first.StationZ == -10000f,
                "a completed transfer must hold the requested station");
            TestAssert.That(fleet.Covers(SatelliteRole.Recon, 40000f, -10000f),
                "a stationed satellite covers its new station");

            // A dry satellite cannot transfer.
            first.Fuel = 0f;
            TestAssert.That(!fleet.TryRetask(first.Id, 90000f, 90000f, out _, out _,
                out OrbitalFailure dryFailure) && dryFailure == OrbitalFailure.InsufficientFuel,
                "a dry satellite must refuse a transfer");

            // Fuel regenerates while stationed.
            first.Fuel = 10f;
            fleet.Tick(30f, true);
            TestAssert.That(first.Fuel > 10f && first.Fuel <= Constellation.MaximumFuel,
                "stationed satellites must regenerate fuel");

            // Transfer pricing is shared with the UI.
            TestAssert.That(Math.Abs(Constellation.TransferCost(0f, 0f, 30000f, 40000f, 1) - 50f * 0.32f) < 0.01f,
                "the shared transfer quote must match the model");

            // Higher stations trade reach for cost and speed.
            TestAssert.That(Constellation.Altitude(2).Swath > Constellation.Altitude(0).Swath,
                "high stations must cover wider swaths");
            TestAssert.That(Constellation.Altitude(0).TransitSpeed > Constellation.Altitude(2).TransitSpeed,
                "low stations must transfer faster");
            TestAssert.That(Constellation.Altitude(2).FuelPerKm > Constellation.Altitude(0).FuelPerKm,
                "high stations must cost more fuel to move");

            TestAssert.That(fleet.TryRecall(first.Id, out _), "recall must remove a satellite");
            TestAssert.That(fleet.Find(first.Id) == null, "a recalled satellite must be gone");
            TestAssert.That(!fleet.TryRecall(first.Id, out _), "recalling twice must fail");

            // Call signs are stable display identities per role and id.
            TestAssert.That(SatelliteNaming.Callsign(SatelliteRole.Recon, 1) == "ARGUS-01",
                "recon call signs must be stable");
            TestAssert.That(SatelliteNaming.Callsign(SatelliteRole.Strike, 12) == "DAMOCLES-12",
                "strike call signs must be stable");
            TestAssert.That(SatelliteNaming.Callsign(SatelliteRole.Ew, 3) == "VEIL-03",
                "EW call signs must be stable");
        }
    }
}
