using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    /// <summary>
    /// Deterministic controller smoke test over the dumped airport geometry. The planar bicycle model has no
    /// native suspension, tyre friction, terrain, obstacles, collisions or aerodynamics. Success means reaching
    /// the start of Roll, never a simulated or claimed flight acceptance.
    /// </summary>
    internal static class GroundAirportSimulationTests
    {
        public static void Run()
        {
            int starts = 0, rejected = 0, failures = 0;
            var cases = new List<ModelResult>();
            var groups = new List<GroupResult>();
            bool groupsOnly = Environment.GetEnvironmentVariable("BOSCALI_AIRPORT_SIM_GROUPS_ONLY") == "1";
            string[] selectedFields = (Environment.GetEnvironmentVariable("BOSCALI_AIRPORT_SIM_FIELDS") ?? "").Split(';');
            foreach ((string map, string file) in new[] { ("Heartland", "airbases.json"),
                ("Archipelago", "archipelago-airbases.json"), ("Heartland hangar snapshot", "boscali-north-hangars.json"),
                ("Mission-defined (placeholder headings)", "mission-airfields.json") })
            foreach (AirbaseSample sample in GroundAirfieldFixture.LoadAll(file))
            {
                if (selectedFields[0].Length > 0 && Array.IndexOf(selectedFields, map + "/" + sample.Name) < 0) continue;
                string pruned = FieldBounds.Prune(sample);
                if (pruned != null) Console.WriteLine("MODEL pruned " + map + "/" + sample.Name + ": " + pruned);
                if (!FieldTraffic.TryPickRunway(sample, out int runway, out bool reverse)) continue;
                var field = new FieldTraffic(sample, runway, reverse);
                for (int service = 0; service < sample.ServicePoints.Length; service++)
                {
                    int selected = service;
                    Vec3 stand = sample.ServicePoints[service].Pos;
                    bool accepted = ServiceSpots.Pick(field, pos => (pos - stand).Horizontal.Length > 0.5f, out ServiceSpot spot)
                        && spot.Point == selected;
                    if (!accepted)
                    {
                        rejected++;
                        Console.WriteLine("MODEL rejected " + map + "/" + sample.Name + " service=" + service);
                        continue;
                    }
                }
                // Enumerate the same stand and queue spots the runtime selector offers as preceding places fill.
                var occupied = new List<Vec3>();
                var queueOf = new Dictionary<int, int>();
                for (int selection = 0; selection < ServiceSpots.MaxLanes * sample.ServicePoints.Length; selection++)
                {
                    if (!ServiceSpots.Pick(field, pos => occupied.Exists(at => (at - pos).Horizontal.Length < ServiceSpots.ClearRadius), out ServiceSpot spot)) break;
                    occupied.Add(spot.WingPose.Pos);
                    queueOf.TryGetValue(spot.Point, out int queue);
                    queueOf[spot.Point] = queue + 1;
                    if (!groupsOnly) RunStart(map, sample, runway, reverse, spot.WingPose, -1, spot.StartNode,
                        "service=" + spot.Point + "/queue=" + queue, cases, ref starts, ref failures, spot.TaxiPrefix);
                }
                for (int hangar = 0; hangar < sample.Hangars.Length; hangar++)
                {
                    if (!sample.Hangars[hangar].Available || !GroundAirfieldFixture.SupportsFixedWing(sample.Hangars[hangar])) continue;
                    if (!groupsOnly) RunStart(map, sample, runway, reverse, sample.Hangars[hangar].Spawn, hangar, -1,
                        "hangar=" + hangar, cases, ref starts, ref failures);
                }
                if (map != "Heartland hangar snapshot")
                {
                    groups.Add(SimulateGroup(map, sample, runway, reverse, 2, SmallProfile()));
                    groups.Add(SimulateGroup(map, sample, runway, reverse, 4, LargeProfile()));
                }
            }
            int groupFailures = 0;
            foreach (GroupResult group in groups)
            {
                if (group.Started > 0 && group.HandedOff != group.Started) groupFailures++;
                Console.WriteLine("MODEL group " + group.Map + "/" + group.Field + " " + group.Profile + " requested=" + group.Requested
                    + " started=" + group.Started + " rollHandoffs=" + group.HandedOff + " t=" + group.Seconds.ToString("0.0", CultureInfo.InvariantCulture)
                    + "s taxiRecoveries=" + group.TaxiRecoveries + " lineupRecoveries=" + group.LineupRecoveries + " remaining=" + group.Remaining);
            }
            Console.WriteLine("MODEL summary eligible starts=" + starts + " rejected service starts=" + rejected + " failures=" + failures);
            string output = Environment.GetEnvironmentVariable("BOSCALI_AIRPORT_SIM_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                string path = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    model = "planar kinematic bicycle; actual GroundPilot and controller; success is start of Roll only",
                    excludes = "native tyres/suspension, terrain/slopes, obstacles/collisions, multiplayer, aerodynamics and lift-off",
                    stepSeconds = 0.05f, maxSeconds = 900f, thrustAcceleration = 8f, brakeDeceleration = 4f,
                    roadOffsetSampleSeconds = 0.5f,
                    dragCoefficient = 0.015f, rollingDeceleration = 0.08f, steerRateDegreesPerSecond = 60f,
                    genericSmall = new { wheelbase = 6f, steerLock = 45f, span = 12f, length = 15f },
                    genericLarge = new { wheelbase = 14f, steerLock = 35f, span = 42f, length = 40f },
                    starts, rejectedServiceStarts = rejected, failures, cases,
                    groupBoundary = "aircraft removed from shared traffic only after Roll outputs full throttle; queue handoff is not lift-off",
                    groupFailures, groups,
                }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true,
                    Converters = { new JsonStringEnumConverter(), new VectorConverter() } }));
            }
            if (!groupsOnly) TestAssert.That(starts > 0, "airport model covers actual eligible launch starts");
            TestAssert.Eq(failures, 0, "every eligible airport start reaches Roll in the bounded planar controller model");
            TestAssert.Eq(groupFailures, 0, "shared-field groups hand off every member at the start of an authorized takeoff roll");
        }

        private static void RunStart(string map, AirbaseSample sample, int runway, bool reverse, WingPose pose, int hangar, int startNode,
            string start, List<ModelResult> cases, ref int starts, ref int failures, Vec3[] taxiPrefix = null)
        {
            foreach (AirframeProfile profile in new[] { SmallProfile(), LargeProfile() })
            {
                starts++;
                ModelResult result = Simulate(sample, runway, reverse, pose, hangar, startNode, profile, taxiPrefix);
                result.Map = map; result.Field = sample.Name; result.Start = start; result.Profile = profile.Id;
                result.Runway = runway; result.Reverse = reverse;
                cases.Add(result);
                if (result.Phase != GroundPhase.Roll) failures++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "MODEL {0}/{1} {2} {3} runway={4}/{5} phase={6} t={7:0.0}s moves[tow/taxi/lineup]={8}/{9}/{10} route={11:0}m driven={12:0}m maxTaxiRoadOffset={13:0.0}m finalHoldDistance={14:0.0}m stop={15}",
                    map, sample.Name, start, profile.Id, runway, reverse ? "reverse" : "forward", result.Phase,
                    result.Seconds, result.Tows, result.TaxiRecoveries, result.LineupRecoveries, result.RouteMetres, result.DrivenMetres, result.MaxTaxiRoadOffset,
                    result.HoldDistance, result.Stop));
            }
        }

        private static AirframeProfile SmallProfile() => new AirframeProfile
            { Id = "generic-small", WheelbaseM = 6f, SteerLockDeg = 45f, SpanM = 12f, LengthM = 15f };

        private static AirframeProfile LargeProfile() => new AirframeProfile
            { Id = "generic-large", WheelbaseM = 14f, SteerLockDeg = 35f, SpanM = 42f, LengthM = 40f };

        private static GroupResult SimulateGroup(string map, AirbaseSample sample, int runway, bool reverse, int requested, AirframeProfile profile)
        {
            const float dt = 0.05f, maxSeconds = 1200f;
            var field = new FieldTraffic(sample, runway, reverse);
            var pilots = new List<GroundPilot>();
            var models = new List<Bicycle>();
            var spots = new List<Vec3>();
            var events = new WingEventRing();
            int nextHangar = 0;
            for (int i = 0; i < requested; i++)
            {
                WingPose pose;
                int hangar = -1, startNode;
                Vec3[] taxiPrefix = null;
                if (ServiceSpots.Pick(field, pos => spots.Exists(at => (at - pos).Horizontal.Length < ServiceSpots.ClearRadius), out ServiceSpot spot))
                {
                    pose = spot.WingPose;
                    startNode = spot.StartNode;
                    taxiPrefix = spot.TaxiPrefix;
                }
                else
                {
                    while (nextHangar < sample.Hangars.Length && (!sample.Hangars[nextHangar].Available || !GroundAirfieldFixture.SupportsFixedWing(sample.Hangars[nextHangar]) ||
                        spots.Exists(at => (at - sample.Hangars[nextHangar].Spawn.Pos).Horizontal.Length < ServiceSpots.ClearRadius))) nextHangar++;
                    if (nextHangar >= sample.Hangars.Length) break;
                    hangar = nextHangar++;
                    pose = sample.Hangars[hangar].Spawn;
                    startNode = -1;
                }
                spots.Add(pose.Pos);
                field.Departures.Expect(i + 1, LineupPlanner.Abreast(field.Runway.Width, profile.SpanM));
                pilots.Add(new GroundPilot(i + 1, field, AirframeClass.FixedWing, pose, hangar, startNode, taxiPrefix));
                models.Add(new Bicycle(pose, profile));
            }
            var result = new GroupResult { Map = map, Field = sample.Name, Profile = profile.Id, Requested = requested, Started = pilots.Count };
            var removed = new bool[pilots.Count];
            if (pilots.Count == 0) { result.Remaining = "no eligible service start"; return result; }
            for (int tick = 0; tick <= (int)(maxSeconds / dt); tick++)
            {
                float time = tick * dt;
                for (int member = 0; member < pilots.Count; member++)
                    if (!removed[member]) field.Report(member + 1, models[member].Position);
                field.Step(dt);
                for (int member = 0; member < pilots.Count; member++)
                {
                    if (removed[member]) continue;
                    GroundPilot pilot = pilots[member];
                    ControlOutput output;
                    try { output = pilot.Step(models[member].State(), profile, null, time, dt, events, member); }
                    catch (Exception error)
                    {
                        result.Seconds = time;
                        result.Remaining = "member=" + (member + 1) + " " + pilot.Phase + "/" + pilot.Stop + " " + error;
                        result.MemberDiagnostics = Snapshot(field, pilots, models, removed);
                        return result;
                    }
                    if (pilot.TakeRelocation(out WingPose relocation))
                    {
                        if (pilot.Phase == GroundPhase.LineUp) result.LineupRecoveries++;
                        else if (time >= GroundPilot.ParkedSeconds + dt) result.TaxiRecoveries++;
                        models[member].Relocate(relocation);
                    }
                    // Explicit model boundary: consume an authorized takeoff roll, without simulating flight.
                    if (pilot.Phase == GroundPhase.Roll && output.Throttle >= 0.99f)
                    {
                        removed[member] = true;
                        result.HandedOff++;
                        field.Departures.Airborne(member + 1);
                        field.Leave(member + 1);
                    }
                    else if (pilot.Phase != GroundPhase.Aborted) models[member].Integrate(output, dt);
                }
                result.Seconds = time;
                if (result.HandedOff == result.Started) return result;
            }
            var remaining = new List<string>();
            for (int i = 0; i < pilots.Count; i++)
                if (!removed[i]) remaining.Add((i + 1) + ":" + pilots[i].Phase + "/" + pilots[i].Stop);
            result.Remaining = string.Join(",", remaining) + " " + field.Describe();
            result.MemberDiagnostics = Snapshot(field, pilots, models, removed);
            return result;
        }

        private static List<MemberDiagnostic> Snapshot(FieldTraffic field, List<GroundPilot> pilots, List<Bicycle> models, bool[] removed)
        {
            var result = new List<MemberDiagnostic>();
            List<int>[] edgeUsers = Get<List<int>[]>(field.Reservations, "edgeUsers");
            for (int member = 0; member < pilots.Count; member++)
            {
                if (removed[member]) continue;
                GroundPilot p = pilots[member];
                var owned = new List<int>();
                for (int node = 0; node < field.Graph.NodeCount; node++)
                    if (field.Reservations.OwnerOfNode(node) == member + 1) owned.Add(node);
                var users = new Dictionary<int, int[]>();
                foreach (int edge in Get<List<int>>(p, "edges")) users[edge] = edgeUsers[edge].ToArray();
                result.Add(new MemberDiagnostic
                {
                    Owner = member + 1, Position = models[member].Position, Phase = p.Phase, Stop = p.Stop,
                    StopWho = p.StopWho, StopDistance = p.StopDistance, WaitsFor = field.WaitsFor(member + 1),
                    ReservationWaitsFor = field.Reservations.WaitingForAny(member + 1), OwnedNodes = owned.ToArray(),
                    RouteNodes = Get<List<int>>(p, "nodes").ToArray(), RouteEdges = Get<List<int>>(p, "edges").ToArray(),
                    Progress = Get<int>(p, "progress"), NodePathIndex = Get<List<int>>(p, "nodePathIndex").ToArray(),
                    Path = Get<Vec3[]>(p, "path"), CumulativePath = Get<float[]>(p, "cum"),
                    RouteEdgeUsers = users,
                });
            }
            return result;
        }

        private static T Get<T>(object source, string name) =>
            (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);

        private static ModelResult Simulate(AirbaseSample sample, int runway, bool reverse, WingPose pose, int hangar,
            int startNode, AirframeProfile profile, Vec3[] taxiPrefix)
        {
            const float dt = 0.05f, limitSeconds = 900f;
            var field = new FieldTraffic(sample, runway, reverse);
            field.Departures.Expect(1, LineupPlanner.Abreast(field.Runway.Width, profile.SpanM));
            var pilot = new GroundPilot(1, field, AirframeClass.FixedWing, pose, hangar, startNode, taxiPrefix);
            var model = new Bicycle(pose, profile);
            var result = new ModelResult { RouteMetres = RouteLength(field, pose.Pos, hangar, startNode, taxiPrefix) };
            for (int tick = 0; tick <= (int)(limitSeconds / dt); tick++)
            {
                float time = tick * dt;
                field.Step(dt);
                AircraftState state = model.State();
                ControlOutput output = pilot.Step(state, profile, null, time, dt, null, 0);
                if (pilot.TakeRelocation(out WingPose relocation))
                {
                    if (pilot.Phase == GroundPhase.LineUp) result.LineupRecoveries++;
                    else if (time < GroundPilot.ParkedSeconds + dt) result.Tows++;
                    else result.TaxiRecoveries++;
                    model.Relocate(relocation);
                }
                result.Seconds = time;
                result.Phase = pilot.Phase;
                result.Stop = pilot.Stop;
                result.HoldDistance = (model.Position - field.Graph.NodePos(field.Graph.HoldShort(runway, reverse))).Horizontal.Length;
                if (tick % 10 == 0 && (pilot.Phase == GroundPhase.TaxiOut || pilot.Phase == GroundPhase.HoldShort))
                    result.MaxTaxiRoadOffset = Math.Max(result.MaxTaxiRoadOffset, DistanceToRoad(sample, model.Position));
                if (pilot.Phase == GroundPhase.Roll || pilot.Phase == GroundPhase.Aborted) return result;
                result.DrivenMetres += model.Integrate(output, dt);
            }
            return result;
        }

        private static float RouteLength(FieldTraffic field, Vec3 position, int hangar, int startNode, Vec3[] taxiPrefix)
        {
            var nodes = new List<int>();
            var edges = new List<int>();
            int start = hangar >= 0 ? field.Graph.HangarExit(hangar) : startNode;
            if (!TaxiRouter.Route(field.Graph, start, field.Graph.HoldShort(field.RunwayIndex, field.Reverse), null, nodes, edges)) return -1f;
            float length = 0f;
            Vec3 previous = position;
            if (taxiPrefix != null)
                foreach (Vec3 point in taxiPrefix) { length += (point - previous).Horizontal.Length; previous = point; }
            length += (previous - field.Graph.NodePos(start)).Horizontal.Length;
            foreach (int edge in edges) length += field.Graph.EdgeLength(edge);
            return length;
        }

        private static float DistanceToRoad(AirbaseSample sample, Vec3 pos)
        {
            float nearest = float.MaxValue;
            foreach (Vec3[] road in sample.Roads)
                for (int i = 1; i < road.Length; i++)
                {
                    Vec3 a = road[i - 1].Horizontal, ab = road[i].Horizontal - a;
                    float t = ab.SqrLength < 1e-6f ? 0f : Scalar.Clamp(Vec3.Dot(pos.Horizontal - a, ab) / ab.SqrLength, 0f, 1f);
                    nearest = Math.Min(nearest, (pos.Horizontal - a - ab * t).Length);
                }
            return nearest == float.MaxValue ? 0f : nearest;
        }

        private sealed class ModelResult
        {
            public GroundPhase Phase;
            public GroundStop Stop;
            public string Map, Field, Start, Profile;
            public int Runway, Tows, TaxiRecoveries, LineupRecoveries;
            public bool Reverse;
            public float Seconds, RouteMetres, DrivenMetres, MaxTaxiRoadOffset, HoldDistance;
        }

        private sealed class GroupResult
        {
            public string Map, Field, Profile, Remaining;
            public int Requested, Started, HandedOff, TaxiRecoveries, LineupRecoveries;
            public float Seconds;
            public List<MemberDiagnostic> MemberDiagnostics;
        }

        private sealed class MemberDiagnostic
        {
            public int Owner, StopWho, WaitsFor, ReservationWaitsFor, Progress;
            public Vec3 Position;
            public GroundPhase Phase;
            public GroundStop Stop;
            public float StopDistance;
            public int[] OwnedNodes, RouteNodes, RouteEdges, NodePathIndex;
            public Vec3[] Path;
            public float[] CumulativePath;
            public Dictionary<int, int[]> RouteEdgeUsers;
        }

        // Vec3's computed Horizontal/Normalized properties recursively produce Vec3; serialize coordinates only.
        private sealed class VectorConverter : JsonConverter<Vec3>
        {
            public override Vec3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
            public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber("X", value.X); writer.WriteNumber("Y", value.Y); writer.WriteNumber("Z", value.Z);
                writer.WriteEndObject();
            }
        }

        private sealed class Bicycle
        {
            private readonly AirframeProfile profile;
            private Vec3 position;
            private float heading, speed, steer;
            public Vec3 Position => position;

            public Bicycle(WingPose pose, AirframeProfile profile)
            {
                this.profile = profile;
                Relocate(pose);
            }

            public void Relocate(WingPose pose)
            {
                position = pose.Pos;
                heading = Vec3.HeadingDeg(pose.Fwd.Horizontal.SqrLength > 1e-6f ? pose.Fwd : Vec3.Forward) * Scalar.Deg2Rad;
                speed = steer = 0f;
            }

            public AircraftState State()
            {
                var fwd = new Vec3((float)Math.Sin(heading), 0f, (float)Math.Cos(heading));
                return new AircraftState { Pos = position, Fwd = fwd, Up = Vec3.Up, Right = Vec3.Cross(Vec3.Up, fwd), Vel = fwd * speed, Tas = speed };
            }

            public float Integrate(ControlOutput output, float dt)
            {
                float wantedSteer = output.Yaw * profile.SteerLockDeg;
                steer += Scalar.Clamp(wantedSteer - steer, -profile.SteerRateDps * dt, profile.SteerRateDps * dt);
                // These declared generic coefficients are a bounded controller model, never fitted native physics.
                float acceleration = output.Throttle * profile.ThrustAccelMax - output.Brake * profile.BrakeDecel - 0.015f * speed * speed - 0.08f;
                speed = Math.Max(0f, speed + acceleration * dt);
                float yawRate = speed * (float)Math.Tan(steer * Scalar.Deg2Rad) / profile.WheelbaseM;
                float middleHeading = heading + 0.5f * yawRate * dt;
                position += new Vec3((float)Math.Sin(middleHeading), 0f, (float)Math.Cos(middleHeading)) * (speed * dt);
                heading += yawRate * dt;
                return speed * dt;
            }
        }
    }
}
