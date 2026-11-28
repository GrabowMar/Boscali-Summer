using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal enum SatelliteBus : byte { Balanced, Agile, Wideband }

    internal readonly struct OrbitPass
    {
        public readonly float StartAt, PeakAt, EndAt;

        public OrbitPass(float startAt, float peakAt, float endAt)
        { StartAt = startAt; PeakAt = peakAt; EndAt = endAt; }

        public bool Contains(float now) => now >= StartAt && now <= EndAt;
    }

    /// <summary>Two circular reference orbits and the time at which a pending transfer switches between them. Coordinates name ground-track nodes.</summary>
    internal readonly struct GeoBird
    {
        // The legacy percentage helpers remain for callers which have not installed the orbit view.
        public const float Slew = 0.011f, BurnBase = 1.5f, BurnPerUnit = 36f;

        public readonly float FromU, FromV, ToU, ToV;
        public readonly float DepartAt, FromEpochAt, EpochAt, ArrivalAt;
        public readonly float Fuel;
        public readonly SatelliteBus Bus;
        public readonly BirdKind Payload;
        public readonly int OrbitRevision;

        // A position-only value is a coarse track or an unlaunched preview. It carries no orbital elements.
        public GeoBird(float u, float v, float fuel, SatelliteBus bus = SatelliteBus.Balanced) : this(u, v, u, v, 0f, fuel, bus) { }

        public GeoBird(float fromU, float fromV, float toU, float toV, float departAt, float fuel, SatelliteBus bus = SatelliteBus.Balanced)
            : this(fromU, fromV, toU, toV, departAt, fuel, bus, BirdKind.Optical, 0f, 0f, departAt, 0) { }

        public GeoBird(float fromU, float fromV, float toU, float toV, float departAt, float fuel, SatelliteBus bus,
            BirdKind payload, float fromEpochAt, float epochAt, float arrivalAt, int orbitRevision)
        {
            FromU = fromU; FromV = fromV; ToU = toU; ToV = toV;
            DepartAt = departAt; Fuel = Math.Clamp(fuel, 0f, 100f); Bus = bus; Payload = payload;
            FromEpochAt = fromEpochAt; EpochAt = epochAt; ArrivalAt = arrivalAt; OrbitRevision = orbitRevision;
        }

        /// <summary>Map height over width, set from the shared theatre frame by the host and peers.</summary>
        public static float Aspect = 1f;

        public bool Orbital => OrbitRevision > 0;
        public float AltitudeKm => GeoOrbit.AltitudeKm(Payload);
        public float InclinationDeg => GeoOrbit.InclinationDeg(Payload);
        public float PeriodSeconds => GeoOrbit.PeriodSeconds(Payload);
        public float DeltaVCapacity => GeoOrbit.DeltaVCapacity(Bus);
        public float DeltaV => DeltaVCapacity * Fuel / 100f;
        public float TrackSpeed => GeoOrbit.TrackRevolutionWidths / PeriodSeconds;

        public static float Distance(float u0, float v0, float u1, float v1) => Scalar.Distance2D(u0, v0 * Aspect, u1, v1 * Aspect);
        public static float ClampStation(float coordinate) => Scalar.Clamp(coordinate, 0.02f, 0.98f);
        public static float Cost(float distance) => distance < 0.005f ? 0f : BurnBase + BurnPerUnit * distance;
        public static float SpeedFor(SatelliteBus bus) => Slew * GeoOrbit.TransferRate(bus);
        public float Speed => SpeedFor(Bus);
        public float Length => Distance(FromU, FromV, ToU, ToV);
        public float Duration => Math.Max(0f, ArrivalAt - DepartAt);
        public bool Moving(float now) => Orbital && Duration > 0f && now < ArrivalAt;
        public float Eta(float now) => Moving(now) ? Math.Max(0f, ArrivalAt - now) : 0f;
        public float Progress(float now) => Duration <= 0f ? 1f : Math.Clamp((now - DepartAt) / Duration, 0f, 1f);

        public float BurnCost
        {
            get
            {
                GeoOrbit.Components(Payload, ToU - FromU, (ToV - FromV) * Aspect, out _, out float across);
                float deltaV = Length > 0f ? GeoOrbit.NodeDeltaV(Payload, Math.Abs(across)) : GeoOrbit.PhaseDeltaV(Payload, EpochAt - FromEpochAt);
                return deltaV / DeltaVCapacity * 100f;
            }
        }

        public float U(float now) { Project(now, out float u, out _); return u; }
        public float V(float now) { Project(now, out _, out float v); return v; }

        public void Project(float now, out float u, out float v)
        {
            bool transfer = Moving(now);
            u = transfer ? FromU : ToU;
            v = transfer ? FromV : ToV;
            if (!Orbital) return;
            float epoch = transfer ? FromEpochAt : EpochAt;
            float cycles = (now - epoch) / PeriodSeconds;
            float distance = (cycles - MathF.Floor(cycles + 0.5f)) * GeoOrbit.TrackRevolutionWidths;
            GeoOrbit.Direction(Payload, out float x, out float y);
            u += x * distance;
            v += y * distance / Aspect;
        }

        /// <summary>The current sub-satellite point, including the old track while a transfer remains underway.</summary>
        public bool Covers(float now, float u, float v, float radius)
        {
            Project(now, out float x, out float y);
            return Distance(x, y, u, v) <= radius;
        }

        public bool NextPass(float now, float u, float v, float radius, out OrbitPass pass)
        {
            pass = default;
            if (!Orbital || !SpaceRules.MissionTime(now) || !float.IsFinite(u) || !float.IsFinite(v) || !float.IsFinite(radius) || radius < 0f) return false;
            return Moving(now) && TryTargetPass(now, 0f, ArrivalAt, FromU, FromV, FromEpochAt, u, v, radius, out pass) ||
                TryTargetPass(Math.Max(now, ArrivalAt), ArrivalAt, float.PositiveInfinity, ToU, ToV, EpochAt, u, v, radius, out pass);
        }

        public bool NextTheatrePass(float now, out OrbitPass pass)
        {
            pass = default;
            if (!Orbital || !SpaceRules.MissionTime(now)) return false;
            return Moving(now) && TryTheatrePass(now, 0f, ArrivalAt, FromU, FromV, FromEpochAt, out pass) ||
                TryTheatrePass(Math.Max(now, ArrivalAt), ArrivalAt, float.PositiveInfinity, ToU, ToV, EpochAt, out pass);
        }

        private bool TryTargetPass(float now, float since, float until, float nodeU, float nodeV, float epoch, float u, float v, float radius, out OrbitPass pass)
        {
            pass = default;
            GeoOrbit.Components(Payload, u - nodeU, (v - nodeV) * Aspect, out float along, out float cross);
            if (Math.Abs(cross) > radius) return false;
            float half = MathF.Sqrt(Math.Max(0f, radius * radius - cross * cross));
            return TryWindow(now, since, until, epoch, along, along - half, along + half, out pass);
        }

        private bool TryTheatrePass(float now, float since, float until, float nodeU, float nodeV, float epoch, out OrbitPass pass)
        {
            pass = default;
            GeoOrbit.Direction(Payload, out float x, out float y);
            float begin = float.NegativeInfinity, end = float.PositiveInfinity;
            return ClipAxis(nodeU, x, ref begin, ref end) && ClipAxis(nodeV, y / Aspect, ref begin, ref end) &&
                TryWindow(now, since, until, epoch, 0f, begin, end, out pass);
        }

        private static bool ClipAxis(float node, float direction, ref float begin, ref float end)
        {
            if (Math.Abs(direction) < 0.00001f) return node >= 0f && node <= 1f;
            float from = -node / direction, to = (1f - node) / direction;
            begin = Math.Max(begin, Math.Min(from, to)); end = Math.Min(end, Math.Max(from, to));
            return begin < end;
        }

        private bool TryWindow(float now, float since, float until, float epoch, float center, float begin, float end, out OrbitPass pass)
        {
            pass = default;
            float speed = TrackSpeed, period = PeriodSeconds;
            float halfRevolution = GeoOrbit.TrackRevolutionWidths * 0.5f;
            begin = Math.Max(begin, -halfRevolution); end = Math.Min(end, halfRevolution);
            if (begin > end) return false;
            float cycle = MathF.Ceiling((now - epoch - end / speed) / period);
            float crossing = epoch + cycle * period;
            float start = Math.Max(since, crossing + begin / speed), stop = Math.Min(until, crossing + end / speed);
            if (stop < now - 0.001f)
            {
                crossing += period;
                start = Math.Max(since, crossing + begin / speed); stop = Math.Min(until, crossing + end / speed);
            }
            if (start >= until || stop < start) return false;
            pass = new OrbitPass(start, Math.Clamp(crossing + center / speed, start, stop), stop);
            return true;
        }

        public bool CanBurn(float now, float u, float v) => GeoSpace.PlanBurn(this, now, u, v, out _) == GeoSpace.Refusal.None;
        public GeoBird Relocate(float now, float u, float v) => GeoSpace.PlanBurn(this, now, u, v, out GeoBird after) == GeoSpace.Refusal.None ? after : this;
    }
}
