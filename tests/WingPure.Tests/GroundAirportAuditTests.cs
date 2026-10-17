using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    /// <summary>Audits every stored field using the production graph, router, runway and service-spot selection.
    /// Distances from road centerlines flag shortcuts; the dump has no pavement boundaries or wheel physics.</summary>
    internal static class GroundAirportAuditTests
    {
        private static readonly MethodInfo BeginLineup = typeof(GroundPilot).GetMethod("BeginLineUp", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PilotPath = typeof(GroundPilot).GetField("path", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Run()
        {
            var fixtures = new[] { "airbases.json", "archipelago-airbases.json", "mission-airfields.json" };
            var stored = fixtures.SelectMany(file => GroundAirfieldFixture.LoadAll(file).Select(field => (file, field))).ToArray();
            AirbaseSample[] fields = stored.Select(s => s.field).ToArray();
            int rawServicePointCount = fields.Sum(f => f.ServicePoints.Length), rawHangarCount = fields.Sum(f => f.Hangars.Length);
            var failures = new List<string>();
            var results = new List<object>();
            TestAssert.That(fields.Length > 0, "the airport fixture contains fields");
            TestAssert.Eq(stored.Select(s => (s.file, s.field.Name)).Distinct().Count(), fields.Length, "every stored airport is audited once per map fixture");
            foreach (var sample in stored) results.Add(Audit(sample.field, sample.file, failures));
            var hangarSnapshot = GroundAirfieldFixture.LoadAll("boscali-north-hangars.json");
            object[] supplemental = hangarSnapshot.Select(field => Audit(field, "boscali-north-hangars.json", failures)).ToArray();

            string output = Environment.GetEnvironmentVariable("BOSCALI_AIRPORT_AUDIT_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(AppContext.BaseDirectory, "all-airports-audit.json");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                evidence = "offline-production-geometry",
                fixtures,
                limitations = new[] { "Stored mission snapshot only; airports absent from this dump remain unverified.",
                    "No native wheel physics, loaded takeoff performance, collision geometry or multiplayer flight was tested.",
                    "Road offset means distance to a taxi-road centerline outside every runway; pavement widths and aprons are absent from the dump.",
                    "Mission-defined service-point forward vectors are placeholders; their native positions and runway geometry were exported.",
                    "Counts include mission-specific airport definitions and overrides; they do not identify distinct geographic airports.",
                    "Native owning-airbase hangar poses were restored from installed assets; availability assumes empty, undamaged hangars.",
                    "Hangar records include helicopter pads; inspect supported aircraft assets before treating a long connector as a fixed-wing taxi issue." },
                airportCount = fields.Length,
                runwayCount = fields.Sum(f => f.Runways.Length),
                taxiRoadCount = fields.Sum(f => f.Roads.Length),
                servicePointCount = fields.Sum(f => f.ServicePoints.Length),
                rawServicePointCount,
                hangarCount = fields.Sum(f => f.Hangars.Length),
                rawHangarCount,
                supplementalHangarCount = hangarSnapshot.Sum(f => f.Hangars.Length),
                legalTakeoffDirections = fields.Sum(f => f.Runways.Where(r => r.Takeoff).Sum(r => r.Reversable ? 2 : 1)),
                failures,
                airports = results,
                supplementalSnapshots = supplemental,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Airport geometry audit: " + fields.Length + " airports; " + output);
            TestAssert.That(failures.Count == 0, string.Join("; ", failures));
        }

        private static object Audit(AirbaseSample field, string fixture, List<string> failures)
        {
            WingPose[] rawServices = field.ServicePoints;
            int rawHangars = field.Hangars.Length;
            string pruned = FieldBounds.Prune(field);
            var coverageGaps = new List<string>();
            if (rawHangars == 0) coverageGaps.Add("Hangars were not captured in this stored snapshot; total ground-launch capacity is unknown.");
            if (fixture == "mission-airfields.json") coverageGaps.Add("Service-point forward vectors are placeholders; taxi headings require native verification.");
            bool picked = FieldTraffic.TryPickRunway(field, out int selectedRunway, out bool selectedReverse);
            TaxiGraph graph = TaxiGraph.Build(field);
            var directions = new List<object>();
            for (int r = 0; r < field.Runways.Length; r++)
            {
                RunwaySample runway = field.Runways[r];
                Check(runway.Width > 0f && runway.Length > 0f && (runway.End - runway.Start).Horizontal.Length > 0f,
                    field.Name + ": runway " + r + " has invalid dimensions", failures);
                if (!runway.Takeoff) continue;
                for (int d = 0; d < (runway.Reversable ? 2 : 1); d++)
                {
                    bool reverse = d == 1;
                    int hold = graph.HoldShort(r, reverse);
                    Check(hold >= 0, field.Name + ": runway " + r + "/" + reverse + " has no hold-short", failures);
                    if (hold < 0) continue;
                    var traffic = new FieldTraffic(field, r, reverse);
                    var services = new List<object>();
                    for (int j = 0; j < field.ServicePoints.Length; j++)
                    {
                        int service = j;
                        Vec3 pos = field.ServicePoints[j].Pos;
                        bool accepted = ServiceSpots.Pick(traffic, p => (p - pos).Horizontal.Length > 0.05f, out ServiceSpot spot);
                        bool reachable = Route(graph, graph.ServiceNode(j), hold, out Vec3[] path, out float length);
                        if (accepted) Check(reachable, field.Name + ": accepted service " + service + " cannot reach runway " + r + "/" + reverse, failures);
                        services.Add(new { index = j, pos = Coordinates(pos), distanceToTaxiRoadM = Rounded(RoadDistance(field, pos)),
                            acceptedForFixedWing = accepted && spot.Point == j,
                            graphReachable = reachable, routeLengthM = Rounded(length),
                            maxRoadOffsetOutsideRunwaysM = Rounded(MaxRoadOffset(field, path)) });
                    }
                    var hangars = new List<object>();
                    for (int h = 0; h < field.Hangars.Length; h++)
                    {
                        int exit = graph.HangarExit(h);
                        bool reachable = Route(graph, exit, hold, out Vec3[] path, out float length);
                        if (field.Hangars[h].Available) Check(reachable, field.Name + ": hangar " + field.Hangars[h].Index + " cannot reach runway " + r + "/" + reverse, failures);
                        hangars.Add(new { index = field.Hangars[h].Index, available = field.Hangars[h].Available,
                            fixedWingEligible = GroundAirfieldFixture.SupportsFixedWing(field.Hangars[h]),
                            supportedAircraftAssets = field.Hangars[h].Types,
                            exit = Coordinates(graph.NodePos(exit)), exitDistanceToTaxiRoadM = Rounded(RoadDistance(field, graph.NodePos(exit))),
                            graphReachable = reachable, routeLengthM = Rounded(length),
                            maxRoadOffsetOutsideRunwaysM = Rounded(MaxRoadOffset(field, path)) });
                    }
                    Vec3[] lineup = Lineup(traffic, graph.NodePos(hold));
                    float lineupRoadOffset = MaxRoadOffset(field, lineup);
                    directions.Add(new { runway = r, reverse, selected = picked && r == selectedRunway && reverse == selectedReverse,
                        lengthM = Rounded(runway.Length), widthM = Rounded(runway.Width), landing = runway.Landing,
                        holdShort = Coordinates(graph.NodePos(hold)), holdShortRoadDistanceM = Rounded(RoadDistance(field, graph.NodePos(hold))),
                        holdShortOnRunway = field.Runways.Any(other => other.Contains(graph.NodePos(hold), 0f)),
                        lineupPath = lineup.Select(Coordinates).ToArray(), lineupMaxRoadOffsetOutsideRunwaysM = Rounded(lineupRoadOffset),
                        requiresLineupReview = lineupRoadOffset > 100f,
                        singleAircraftRollAvailableM = Rounded(runway.Length - LineupPlanner.ThresholdMargin),
                        services, hangars });
                }
            }
            var launches = new List<object>();
            if (picked)
            {
                var traffic = new FieldTraffic(field, selectedRunway, selectedReverse);
                var occupied = new List<Vec3>();
                int capacity = field.ServicePoints.Length * ServiceSpots.MaxLanes;
                for (int i = 0; i < capacity; i++)
                {
                    if (!ServiceSpots.Pick(traffic, at => occupied.Any(p => (p - at).Horizontal.Length < ServiceSpots.ClearRadius), out ServiceSpot spot)) break;
                    occupied.Add(spot.WingPose.Pos);
                    bool reachable = Route(traffic.Graph, spot.StartNode, traffic.Graph.HoldShort(selectedRunway, selectedReverse), out Vec3[] path, out float length);
                    Check(reachable, field.Name + ": selected launch " + i + " cannot reach its hold-short", failures);
                    Vec3[] prefix = spot.TaxiPrefix ?? new[] { spot.WingPose.Pos };
                    Vec3[] fullPath = prefix.Concat(path).ToArray();
                    float taxiRoadOffset = reachable ? MaxRoadOffset(field, fullPath) : float.NaN;
                    launches.Add(new { launch = i + 1, servicePoint = spot.Point, pos = Coordinates(spot.WingPose.Pos),
                        graphReachable = reachable, initialTaxiPath = prefix.Select(Coordinates).ToArray(), remainingRouteLengthM = reachable ? Rounded(PathLength(fullPath)) : null,
                        maxRoadOffsetOutsideRunwaysM = Rounded(taxiRoadOffset), requiresTaxiReview = taxiRoadOffset > 100f });
                }
                if (launches.Count == 0 && rawHangars == 0) coverageGaps.Add("No eligible stored service point; a local hangar snapshot is needed to assess ground launching.");
            }
            else Check(field.Runways.All(r => !r.Takeoff), field.Name + ": takeoff runway was not selected", failures);
            Console.WriteLine("AUDIT [" + fixture + "] " + field.Name + ": " + field.Runways.Length + " runways, " + directions.Count +
                " takeoff directions, " + field.ServicePoints.Length + " service points, " + field.Hangars.Length + " hangars, " + launches.Count + " service launches");
            return new { fixture, name = field.Name, displayName = DisplayName(field.Name, fixture), attached = field.Attached, selectedRunway, selectedReverse,
                rawServicePointCount = rawServices.Length, rawHangarCount = rawHangars, pruned, coverageGaps,
                rawServicePoints = rawServices.Select((p, i) => new { index = i, pos = Coordinates(p.Pos),
                    retainedAfterFieldBounds = field.ServicePoints.Any(kept => kept.Pos.Equals(p.Pos)),
                    distanceToTaxiRoadM = Rounded(RoadDistance(field, p.Pos)) }).ToArray(),
                roadCount = field.Roads.Length, graphNodes = graph.NodeCount, graphEdges = graph.EdgeCount,
                graphComponents = Components(graph), serviceLaunchCapacity = launches.Count, selectedLaunches = launches,
                runways = field.Runways.Select((r, i) => new { index = i, lengthM = Rounded(r.Length), widthM = Rounded(r.Width),
                    r.Takeoff, r.Landing, r.Reversable, nativeEntries = r.Entries.Length, nativeExits = r.Exits.Length }).ToArray(), directions };
        }

        private static bool Route(TaxiGraph graph, int start, int goal, out Vec3[] path, out float length)
        {
            var nodes = new List<int>();
            var edges = new List<int>();
            bool found = TaxiRouter.Route(graph, start, goal, null, nodes, edges);
            path = found ? graph.PathPoints(nodes, edges) : Array.Empty<Vec3>();
            length = found ? edges.Sum(graph.EdgeLength) : float.NaN;
            return found;
        }

        private static Vec3[] Lineup(FieldTraffic traffic, Vec3 hold)
        {
            traffic.Departures.Expect(1, 1);
            traffic.Departures.Enqueue(1, 0f);
            traffic.Departures.MayLineUp(1, 0f);
            var pilot = new GroundPilot(1, traffic, AirframeClass.FixedWing, new WingPose(hold, traffic.Runway.Direction(traffic.Reverse)), -1);
            BeginLineup.Invoke(pilot, new object[] { hold, 0f });
            return (Vec3[])PilotPath.GetValue(pilot);
        }

        private static int Components(TaxiGraph graph)
        {
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            int count = 0;
            for (int n = 0; n < graph.NodeCount; n++)
            {
                if (!seen.Add(n)) continue;
                count++;
                queue.Enqueue(n);
                while (queue.Count > 0)
                {
                    int at = queue.Dequeue();
                    foreach (int e in graph.EdgesOf(at))
                    {
                        int next = graph.OtherEnd(e, at);
                        if (seen.Add(next)) queue.Enqueue(next);
                    }
                }
            }
            return count;
        }

        internal static float MaxRoadOffset(AirbaseSample field, Vec3[] path)
        {
            if (field.Roads.Length == 0 || path.Length == 0) return float.NaN;
            float max = 0f;
            for (int i = 1; i < path.Length; i++)
            {
                int steps = Math.Max(1, (int)Math.Ceiling((path[i] - path[i - 1]).Horizontal.Length / 2f));
                for (int k = 0; k <= steps; k++)
                {
                    Vec3 p = Vec3.Lerp(path[i - 1], path[i], (float)k / steps);
                    if (field.Runways.Any(r => r.Contains(p, 0f))) continue;
                    max = Math.Max(max, RoadDistance(field, p));
                }
            }
            return max;
        }

        private static float RoadDistance(AirbaseSample field, Vec3 p)
        {
            float best = float.PositiveInfinity;
            foreach (Vec3[] road in field.Roads)
                for (int k = 1; k < road.Length; k++)
                {
                    Vec3 a = road[k - 1].Horizontal, ab = road[k].Horizontal - a;
                    float t = ab.SqrLength < 1e-6f ? 0f : Scalar.Clamp(Vec3.Dot(p.Horizontal - a, ab) / ab.SqrLength, 0f, 1f);
                    best = Math.Min(best, (a + ab * t - p.Horizontal).Length);
                }
            return best;
        }

        private static double[] Coordinates(Vec3 p) => new[] { Math.Round(p.X, 2), Math.Round(p.Y, 2), Math.Round(p.Z, 2) };
        private static string DisplayName(string name, string fixture)
        {
            string map = fixture == "airbases.json" || fixture == "boscali-north-hangars.json" ? "Terrain1" :
                fixture == "archipelago-airbases.json" ? "Terrain_naval" : null;
            if (map == null) return name;
            using JsonDocument hangars = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native-land-hangars.json")));
            long owner = hangars.RootElement.GetProperty("airbases").EnumerateArray()
                .Single(a => a.GetProperty("map").GetString() == map && a.GetProperty("name").GetString() == name)
                .GetProperty("componentPathId").GetInt64();
            using JsonDocument identities = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "native-land-identities.json")));
            return identities.RootElement.GetProperty("identities").EnumerateArray().Single(i => i.GetProperty("componentPathId").GetInt64() == owner)
                .GetProperty("displayName").GetString();
        }
        private static float PathLength(Vec3[] path) { float length = 0f; for (int i = 1; i < path.Length; i++) length += (path[i] - path[i - 1]).Horizontal.Length; return length; }
        private static double? Rounded(float n) => float.IsNaN(n) || float.IsInfinity(n) ? null : Math.Round(n, 2);
        private static void Check(bool condition, string failure, List<string> failures) { if (!condition) failures.Add(failure); }
    }

    internal static class GroundAirportServicePrefixTests
    {
        public static void Run()
        {
            AirbaseSample field = GroundAirfieldFixture.LoadAll("mission-airfields.json").Single(f => f.Name == "CustomAirbase_1");
            FieldTraffic.TryPickRunway(field, out int runway, out bool reverse);
            var traffic = new FieldTraffic(field, runway, reverse);
            var occupied = new List<Vec3>();
            ServiceSpot spot = default;
            for (int i = 0; i < 3; i++)
            {
                TestAssert.That(ServiceSpots.Pick(traffic, at => occupied.Any(p => (p - at).Horizontal.Length < ServiceSpots.ClearRadius), out spot),
                    "Feldspar's second runway has a third service lane");
                occupied.Add(spot.WingPose.Pos);
            }
            var pilot = new GroundPilot(1, traffic, AirframeClass.FixedWing, spot.WingPose, -1, spot.StartNode, spot.TaxiPrefix);
            AircraftState state = GroundFixture.At(spot.WingPose.Pos, spot.WingPose.Fwd);
            pilot.Step(state, new AirframeProfile(), null, 0f, 0.1f, null, 0);
            pilot.Step(state, new AirframeProfile(), null, GroundPilot.ParkedSeconds, 0.1f, null, 0);
            Vec3[] path = (Vec3[])typeof(GroundPilot).GetField("path", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pilot);
            TestAssert.That(GroundAirportAuditTests.MaxRoadOffset(field, path) < 50f,
                "a queue spawn follows the rest of its curved taxi edge instead of cutting 125 m away from the roads");
        }
    }
}
