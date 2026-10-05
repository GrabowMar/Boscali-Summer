using BoscaliSummer.Modules.Intel.Domain;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Tests.Features.Intel
{
    /// <summary>
    /// Sites and rings: the 1.5 km merge, radar guidance from the site's radar, the 96-ring cap
    /// ranked radar > IR > gun, launch memory and the pre-war rules.
    /// </summary>
    internal static class RingSetTests
    {
        public static void Run()
        {
            R9TrioTakesGuidanceFromItsRadarTruck();
            ALoneR9LauncherIsNotRadarGuided();
            BoltstrikeGuidesItself();
            TwoBandsAtOneSiteShareTheSite();
            MergeStopsAtTheSiteRadius();
            ChainedLaunchersAnchorOnARealLauncher();
            TheCapKeepsRadarOverIrOverGun();
            LaunchRingsStayUnknownUntilATrackedLauncherJoins();
            PreWarRingsAreMarkedUntilConfirmed();
            StaleNeedsEveryMemberStale();
            SensorsNeverRing();
            LaunchMemoryQuantisesRefreshesAndAges();
            LaunchMemoryReplacesItsOldestWhenFull();
            PreWarTakesOnlyFixedMissionSites();
            ADeadPreWarSiteLeavesOnlyOnceSeen();
        }

        private static RingInput Launcher(float x, float z, StationSample band, AirDefenceKind kind, int hash,
            RingSource source = RingSource.Tracked, bool confirmed = true, bool ownRadar = false,
            bool stale = false, bool emitting = false) => new RingInput
        {
            X = x,
            Z = z,
            MaxRange = band.MaxRange,
            MinAltitude = band.MinAltitude,
            MaxAltitude = band.MaxAltitude,
            Kind = kind,
            OwnRadar = ownRadar,
            Emitting = emitting,
            Source = source,
            Confirmed = confirmed,
            AgeSeconds = 5f,
            Stale = stale,
            UnitHash = hash
        };

        private static RingInput Sensor(float x, float z, int hash, bool emitting) => new RingInput
        {
            X = x,
            Z = z,
            Kind = AirDefenceKind.Sensor,
            OwnRadar = true,
            Emitting = emitting,
            Source = RingSource.Tracked,
            Confirmed = true,
            AgeSeconds = 5f,
            UnitHash = hash
        };

        private static int Build(RingInput[] inputs, AirDefenceRing[] into) =>
            new RingSet().Build(inputs, inputs.Length, into);

        private static void R9TrioTakesGuidanceFromItsRadarTruck()
        {
            var into = new AirDefenceRing[ThreatPictureLimits.MaximumRings];
            int n = Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamRadar2, AirDefenceKind.RadarSam, 1),
                Launcher(200f, 0f, AirDefenceTests.SamRadar2, AirDefenceKind.RadarSam, 2),
                Launcher(0f, 200f, AirDefenceTests.SamRadar2, AirDefenceKind.RadarSam, 3),
                Sensor(300f, 300f, 44, true)
            }, into);
            TestAssert.That(n == 1 && into[0].Launchers == 3, "an R9 trio is one site with three launchers");
            TestAssert.That(into[0].Kind == AirDefenceKind.RadarSam && into[0].RadarGuided && into[0].RadarHash == 44 && into[0].Emitting,
                "the trio is radar guided through its radar truck, the emitter a SEAD shot must kill");
            TestAssert.That(into[0].SiteHash == 1 && into[0].X == 0f && into[0].Z == 0f, "the ring sits on its first launcher");
        }

        private static void ALoneR9LauncherIsNotRadarGuided()
        {
            var into = new AirDefenceRing[4];
            Build(new[] { Launcher(0f, 0f, AirDefenceTests.SamRadar2, AirDefenceKind.RadarSam, 1) }, into);
            TestAssert.That(into[0].Kind == AirDefenceKind.RadarSam && !into[0].RadarGuided && into[0].RadarHash == 0,
                "with no radar known at the site the launcher still rings, but nothing guides it and there is no emitter to kill");
        }

        private static void BoltstrikeGuidesItself()
        {
            var into = new AirDefenceRing[4];
            Build(new[] { Launcher(0f, 0f, AirDefenceTests.SamRadar1, AirDefenceKind.RadarSam, 7, ownRadar: true, emitting: true) }, into);
            TestAssert.That(into[0].RadarGuided && into[0].RadarHash == 7 && into[0].Emitting, "a Boltstrike carries its own radar");
        }

        private static void TwoBandsAtOneSiteShareTheSite()
        {
            var into = new AirDefenceRing[4];
            int n = Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamRadar1, AirDefenceKind.RadarSam, 10, ownRadar: true),
                Launcher(100f, 0f, AirDefenceTests.SamRadar2, AirDefenceKind.RadarSam, 11)
            }, into);
            TestAssert.That(n == 2, "different cones never merge into one ring");
            TestAssert.That(into[0].SiteHash == 10 && into[1].SiteHash == 10, "but they are one site");
            TestAssert.That(into[1].RadarGuided && into[1].RadarHash == 10, "the R9 takes the site's Boltstrike radar");
        }

        private static void MergeStopsAtTheSiteRadius()
        {
            var into = new AirDefenceRing[4];
            int near = Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 1),
                Launcher(1400f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 2)
            }, into);
            TestAssert.That(near == 1 && into[0].Launchers == 2, "launchers 1.4 km apart are one site");
            int far = Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 1),
                Launcher(1600f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 2)
            }, into);
            TestAssert.That(far == 2 && into[0].SiteHash != into[1].SiteHash, "1.6 km apart they are two sites");
        }

        private static void ChainedLaunchersAnchorOnARealLauncher()
        {
            var into = new AirDefenceRing[4];
            int n = Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 1),
                Launcher(1400f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 2),
                Launcher(2800f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 3)
            }, into);
            TestAssert.That(n == 2 && into[0].Launchers == 2 && into[1].Launchers == 1,
                "a road of SAMs 1.4 km apart does not chain into one ring");
            TestAssert.That(into[1].X == 2800f && into[1].SiteHash == 3,
                "the far launcher anchors its own ring on its own position and site");
        }

        private static void TheCapKeepsRadarOverIrOverGun()
        {
            var set = new RingSet(3);
            var into = new AirDefenceRing[3];
            int n = set.Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.Gun57Spaag, AirDefenceKind.Gun, 1),
                Launcher(10000f, 0f, AirDefenceTests.Gun57Spaag, AirDefenceKind.Gun, 2),
                Launcher(20000f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 3),
                Launcher(30000f, 0f, AirDefenceTests.SamRadar2, AirDefenceKind.RadarSam, 4),
                Launcher(40000f, 0f, AirDefenceTests.SamRadar1, AirDefenceKind.RadarSam, 5, ownRadar: true)
            }, 5, into);
            TestAssert.That(n == 3 && set.Dropped == 2, "a full table drops two rings");
            TestAssert.That(into[0].Kind == AirDefenceKind.RadarSam && into[1].Kind == AirDefenceKind.RadarSam &&
                            into[2].Kind == AirDefenceKind.IrSam,
                "radar rings are kept first, then IR; the guns are the ones evicted");
        }

        private static void LaunchRingsStayUnknownUntilATrackedLauncherJoins()
        {
            var into = new AirDefenceRing[4];
            Build(new[] { Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.Unknown, 70, RingSource.Launch, confirmed: false) }, into);
            TestAssert.That(into[0].Kind == AirDefenceKind.Unknown && into[0].Source == RingSource.Launch &&
                            into[0].Launchers == 0 && !into[0].RadarGuided,
                "a launch alone is an Unknown ring: avoided like radar, never SEAD-eligible");
            int n = Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.Unknown, 70, RingSource.Launch, confirmed: false),
                Launcher(400f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 71)
            }, into);
            TestAssert.That(n == 1 && into[0].Kind == AirDefenceKind.IrSam && into[0].Source == RingSource.Tracked &&
                            into[0].Launchers == 1,
                "a tracked launcher with the same envelope confirms the launch ring as what it is");
        }

        private static void PreWarRingsAreMarkedUntilConfirmed()
        {
            var into = new AirDefenceRing[4];
            Build(new[] { Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 1, RingSource.PreWar, confirmed: false) }, into);
            TestAssert.That(into[0].Source == RingSource.PreWar && !into[0].Confirmed, "pre-war intel is labelled and unconfirmed");
            Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 1, RingSource.PreWar, confirmed: false),
                Launcher(300f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 2, RingSource.PreWar, confirmed: true)
            }, into);
            TestAssert.That(into[0].Source == RingSource.PreWar && into[0].Confirmed,
                "one tracked member confirms a pre-war site, which keeps its label");
            Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 1, RingSource.PreWar, confirmed: false),
                Launcher(300f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 3)
            }, into);
            TestAssert.That(into[0].Source == RingSource.Tracked, "a live tracked member makes the site tracked");
        }

        private static void StaleNeedsEveryMemberStale()
        {
            var into = new AirDefenceRing[4];
            Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 1, stale: true),
                Launcher(100f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 2, stale: true)
            }, into);
            TestAssert.That(into[0].Stale, "every member unseen: the ring is stale");
            Build(new[]
            {
                Launcher(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 1, stale: true),
                Launcher(100f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam, 2)
            }, into);
            TestAssert.That(!into[0].Stale, "one fresh member keeps it fresh");
            TestAssert.That(RingSet.IsStale(RingSource.Tracked, true, 601f) && !RingSet.IsStale(RingSource.Tracked, true, 599f),
                "a static site goes stale after 600 s unseen");
            TestAssert.That(RingSet.IsStale(RingSource.Tracked, false, 181f) && !RingSet.IsStale(RingSource.Tracked, false, 179f),
                "a mobile one after 180 s");
            TestAssert.That(!RingSet.IsStale(RingSource.PreWar, true, 100000f) && !RingSet.IsStale(RingSource.Launch, false, 100000f),
                "pre-war and launch rings never go stale; launch memory ages out instead");
        }

        private static void SensorsNeverRing()
        {
            var into = new AirDefenceRing[4];
            TestAssert.That(Build(new[] { Sensor(0f, 0f, 1, true), Sensor(5000f, 0f, 2, false) }, into) == 0,
                "radar trucks alone draw no ring");
        }

        private static void LaunchMemoryQuantisesRefreshesAndAges()
        {
            StationSample ir = AirDefenceTests.SamIr1;
            var memory = new LaunchMemory();
            memory.Record(7, 1234f, -10f, false, ir.MaxRange, ir.MinAltitude, ir.MaxAltitude, 100f);
            TestAssert.That(memory.Count == 1 && memory[0].X == 1250f && memory[0].Z == -250f,
                "a launch origin is quantised to the centre of its 500 m cell");
            memory.Record(8, 1400f, -400f, false, ir.MaxRange, ir.MinAltitude, ir.MaxAltitude, 150f);
            TestAssert.That(memory.Count == 1 && memory[0].LastLaunch == 150f && memory[0].OwnerId == 8,
                "a second launch from the same cell and envelope refreshes it");
            memory.Record(9, 1400f, -400f, true, 5500f, 0f, 100000f, 160f);
            TestAssert.That(memory.Count == 2, "a different envelope in the same cell is its own entry");
            memory.Prune(450f);
            TestAssert.That(memory.Count == 2, "300 s after its last launch a mobile origin is still remembered");
            memory.Prune(451f);
            TestAssert.That(memory.Count == 1 && memory[0].OwnerStatic, "past 300 s the mobile origin is forgotten; the static one stays");
            memory.Prune(1060f);
            TestAssert.That(memory.Count == 1, "a static origin lasts 900 s");
            memory.Prune(1061f);
            TestAssert.That(memory.Count == 0, "and then it too is forgotten");
            TestAssert.That(KnownHostileFilter.IsSurface(UnitClass.GroundVehicle) && KnownHostileFilter.IsSurface(UnitClass.Ship) &&
                            KnownHostileFilter.IsSurface(UnitClass.Building) && !KnownHostileFilter.IsSurface(UnitClass.Aircraft),
                "only surface owners leave launch memory; an air-to-air shot is not an air-defence site");
        }

        private static void LaunchMemoryReplacesItsOldestWhenFull()
        {
            var memory = new LaunchMemory(2);
            memory.Record(1, 0f, 0f, false, 5000f, 0f, 10000f, 1f);
            memory.Record(2, 5000f, 0f, false, 5000f, 0f, 10000f, 2f);
            memory.Record(3, 10000f, 0f, false, 5000f, 0f, 10000f, 3f);
            TestAssert.That(memory.Count == 2 && memory[0].OwnerId == 3 && memory[1].OwnerId == 2, "the oldest origin makes room");
        }

        private static void PreWarTakesOnlyFixedMissionSites()
        {
            TestAssert.That(PreWarRules.Qualifies(true, true, UnitClass.GroundVehicle, true), "a mission-placed SAM holding position is pre-war intel");
            TestAssert.That(PreWarRules.Qualifies(true, true, UnitClass.Building, true), "so is an air-defence emplacement");
            TestAssert.That(!PreWarRules.Qualifies(true, false, UnitClass.GroundVehicle, true), "a mobile SAM is never pre-war");
            TestAssert.That(!PreWarRules.Qualifies(false, true, UnitClass.GroundVehicle, true), "factory output is never pre-war");
            TestAssert.That(!PreWarRules.Qualifies(true, true, UnitClass.GroundVehicle, false), "a garrison without air defence is never pre-war");
            TestAssert.That(!PreWarRules.Qualifies(true, true, UnitClass.Ship, true) && !PreWarRules.Qualifies(true, true, UnitClass.Aircraft, true),
                "ships and aircraft are never fixed sites");
        }

        private static void ADeadPreWarSiteLeavesOnlyOnceSeen()
        {
            TestAssert.That(!PreWarRules.ShouldDrop(float.NaN, 500f), "a living site stays");
            TestAssert.That(!PreWarRules.ShouldDrop(100f, float.NaN), "a dead site nobody has looked at stays known");
            TestAssert.That(!PreWarRules.ShouldDrop(100f, 90f), "a look before it died proves nothing");
            TestAssert.That(!PreWarRules.ShouldDrop(100f, 100f), "nor does a look in the same instant");
            TestAssert.That(PreWarRules.ShouldDrop(100f, 105f), "a look after it died drops it");
        }
    }
}
