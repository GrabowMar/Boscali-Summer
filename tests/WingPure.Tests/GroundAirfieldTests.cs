using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    internal static class GroundRunwayAccessTests
    {
        public static void Run()
        {
            AirbaseSample south = GroundAirfieldFixture.Load("airstrip_city2");
            TestAssert.That(FieldTraffic.TryPickRunway(south, out int runway, out bool reverse), "South Boscali has a departure runway");
            TestAssert.Eq(runway, 0, "South Boscali keeps its native runway index");
            TestAssert.That(!reverse, "South Boscali uses the threshold reached by its actual taxi roads");

            var field = GroundAirfieldFixture.Synthetic();
            field.Center = Vec3.Zero;
            field.Roads = new[] { new[] { new Vec3(40f, 0f, 900f), new Vec3(40f, 0f, 1000f), field.Runways[0].End } };
            FieldTraffic.TryPickRunway(field, out _, out reverse);
            TestAssert.That(reverse, "taxi access at the far threshold overrides a nearer field center");
            field.Runways[0].Reversable = false;
            FieldTraffic.TryPickRunway(field, out _, out reverse);
            TestAssert.That(!reverse, "a one-way runway never reverses for taxi access");

            field.Runways[0].Reversable = true;
            field.Roads = Array.Empty<Vec3[]>();
            field.Center = field.Runways[0].End;
            FieldTraffic.TryPickRunway(field, out _, out reverse);
            TestAssert.That(reverse, "a roadless field retains the center-based direction fallback");
            field.Center = Vec3.Zero;
            field.Runways[0].Entries = new[] { new WingPose(new Vec3(40f, 0f, 1000f), -Vec3.Forward) };
            FieldTraffic.TryPickRunway(field, out _, out reverse);
            TestAssert.That(reverse, "native off-runway entries also provide threshold access");
        }
    }

    internal static class GroundServiceRoadAccessTests
    {
        public static void Run()
        {
            foreach (string name in new[] { "highwaystrip1", "highwaystrip2", "airstrip_city2" })
            {
                AirbaseSample sample = GroundAirfieldFixture.Load(name);
                FieldTraffic.TryPickRunway(sample, out int runway, out bool reverse);
                var field = new FieldTraffic(sample, runway, reverse);
                Vec3 occupied = sample.ServicePoints[0].Pos;
                TestAssert.That(ServiceSpots.Pick(field, at => (at - occupied).Horizontal.Length < ServiceSpots.ClearRadius, out ServiceSpot spot),
                    name + " has a second launch position on its usable taxi route");
                TestAssert.Eq(spot.Point, 0, name + " skips the service point over 600 m from any taxi road");
                TestAssert.That((spot.WingPose.Pos - occupied).Horizontal.Length >= ServiceSpots.ClearRadius,
                    name + " places the second launch clear of the first");
            }

            var sampleField = GroundAirfieldFixture.Synthetic();
            sampleField.Roads = new[] { new[] { new Vec3(40f, 0f, 0f), new Vec3(40f, 0f, 1000f) } };
            sampleField.ServicePoints = new[] { new WingPose(new Vec3(40f, 0f, 500f), Vec3.Forward) };
            TestAssert.That(ServiceSpots.Pick(new FieldTraffic(sampleField, 0, false), _ => false, out _),
                "road access is measured against segment interiors, not only endpoints");
            sampleField.ServicePoints = new[] { new WingPose(new Vec3(500f, 0f, 500f), Vec3.Forward) };
            TestAssert.That(!ServiceSpots.Pick(new FieldTraffic(sampleField, 0, false), _ => false, out _),
                "a graph's synthetic cross-country connector does not make a remote service point usable");
            TestAssert.That(ServiceSpots.Pick(new FieldTraffic(sampleField, 0, false), _ => false, out _, taxiRequired: false),
                "vertical launches retain service points that do not need a taxi road");
            sampleField.Roads = Array.Empty<Vec3[]>();
            TestAssert.That(ServiceSpots.Pick(new FieldTraffic(sampleField, 0, false), _ => false, out _),
                "roadless fields retain their direct service-point launch behavior");
        }
    }

    internal static class GroundAirfieldFixture
    {
        private static readonly string[] FixedWingAssets = { "CI-22", "Trainer", "FS-12", "EW1", "SFB", "Multirole1", "SmallFighter1", "CAS1", "FastBomber1" };
        public static bool SupportsFixedWing(HangarSample hangar) => hangar.Types.Length == 0 || hangar.Types.Any(t => Array.IndexOf(FixedWingAssets, t) >= 0);

        public static AirbaseSample Synthetic() => new AirbaseSample
        {
            Name = "taxi-access-fixture",
            Runways = new[] { new RunwaySample { Start = Vec3.Zero, End = Vec3.Forward * 1000f, Length = 1000f, Width = 20f, Takeoff = true, Reversable = true } },
        };

        public static AirbaseSample Load(string name) => LoadAll().First(f => f.Name == name);

        public static AirbaseSample[] LoadAll(string fileName = "airbases.json")
        {
            using JsonDocument dump = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, fileName)));
            AirbaseSample[] fields = dump.RootElement.GetProperty("airbases").EnumerateArray().Select(Read).ToArray();
            string map = fileName == "airbases.json" ? "Terrain1" : fileName == "archipelago-airbases.json" ? "Terrain_naval" : null;
            if (map == null) return fields;
            // Cached native geometry dumps preceded hangar registration. Restore actual owning-airbase spawn poses
            // from the installed assets; availability here assumes an empty, undamaged hangar for controller tests.
            using JsonDocument native = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native-land-hangars.json")));
            foreach (AirbaseSample field in fields)
            {
                JsonElement owner = native.RootElement.GetProperty("airbases").EnumerateArray()
                    .Single(a => a.GetProperty("map").GetString() == map && a.GetProperty("name").GetString() == field.Name);
                long ownerId = owner.GetProperty("componentPathId").GetInt64();
                field.Hangars = native.RootElement.GetProperty("hangars").EnumerateArray()
                    .Where(h => h.GetProperty("serializedAirbaseMatches").EnumerateArray().Any(id => id.GetInt64() == ownerId))
                    .Select((h, i) => new HangarSample
                    {
                        Index = i, Available = true, CarrierDoors = Flag(h, "carrierDoors"),
                        Spawn = new WingPose(Vector(h.GetProperty("spawn").GetProperty("position")), Vector(h.GetProperty("spawn").GetProperty("forward"))),
                        Types = h.GetProperty("aircraftAssets").EnumerateArray().Select(t => t.GetString()).ToArray(),
                    }).ToArray();
            }
            return fields;
        }

        public static AirbaseSample Read(JsonElement field)
        {
            return new AirbaseSample
            {
                Name = field.GetProperty("name").GetString(),
                Center = Vector(field.GetProperty("center")),
                Radius = field.GetProperty("radius").GetSingle(),
                Attached = field.TryGetProperty("attached", out JsonElement attached) && attached.GetBoolean(),
                Runways = field.GetProperty("runways").EnumerateArray().Select(r => new RunwaySample
                {
                    Index = r.GetProperty("index").GetInt32(), Start = Vector(r.GetProperty("start")), End = Vector(r.GetProperty("end")),
                    Width = r.GetProperty("width").GetSingle(), Length = r.GetProperty("length").GetSingle(),
                    Takeoff = r.GetProperty("takeoff").GetBoolean(), Reversable = r.GetProperty("reversable").GetBoolean(),
                    Landing = Flag(r, "landing"), Arrestor = Flag(r, "arrestor"), SkiJump = Flag(r, "skiJump"),
                    Entries = Poses(r.GetProperty("entryPoints")), Exits = Poses(r.GetProperty("exitPoints")),
                }).ToArray(),
                Roads = field.GetProperty("taxiRoads").EnumerateArray().Select(r => r.GetProperty("points").EnumerateArray().Select(Vector).ToArray()).ToArray(),
                Hangars = field.GetProperty("hangars").EnumerateArray().Select(h => new HangarSample
                {
                    Index = h.GetProperty("index").GetInt32(),
                    Spawn = new WingPose(Vector(h.GetProperty("position")), Vector(h.GetProperty("forward"))),
                    Available = Flag(h, "available"), CarrierDoors = Flag(h, "carrierDoors"),
                    Types = h.GetProperty("types").EnumerateArray().Select(t => t.GetString()).ToArray(),
                }).ToArray(),
                ServicePoints = Poses(field.GetProperty("servicePoints")),
                Pads = Poses(field.GetProperty("verticalPads")),
            };
        }

        private static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out JsonElement flag) && flag.GetBoolean();

        private static WingPose[] Poses(JsonElement poses) => poses.EnumerateArray()
            .Select(p => new WingPose(Vector(p.GetProperty("position")), Vector(p.GetProperty("forward")))).ToArray();

        private static Vec3 Vector(JsonElement p) => new Vec3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
    }
}
