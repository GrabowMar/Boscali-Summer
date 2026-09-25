using System;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Intel.Domain
{
    /// <summary>One known launcher, radar carrier or launch origin, as the ring builder reads it.</summary>
    internal struct RingInput
    {
        public float X;
        public float Z;
        public float MaxRange;
        public float MinAltitude;
        public float MaxAltitude;
        /// <summary>RadarSam/IrSam/Gun for a launcher, Sensor for a radar carrier, Unknown for a launch origin.</summary>
        public AirDefenceKind Kind;
        /// <summary>The unit carries its own radar and can serve as its site's radar.</summary>
        public bool OwnRadar;
        public bool Emitting;
        public RingSource Source;
        public bool Confirmed;
        public float AgeSeconds;
        public bool Stale;
        public int UnitHash;
    }

    /// <summary>
    /// Builds a faction's rings. Launchers with the same envelope within 1.5 km of a ring's
    /// first launcher merge into it (the ring stays on that launcher, so a road of SAMs never
    /// chains into one drifting ring). Rings are built radar, launch-only, IR, gun in that
    /// order, so a full table drops guns first. A radar SAM is radar guided when a member
    /// carries its own radar or a radar carrier stands within 1.5 km; that radar is the ring's
    /// RadarHash. Rings within 1.5 km of an earlier ring share its SiteHash.
    /// </summary>
    internal sealed class RingSet
    {
        public const float SiteMergeMetres = 1500f;
        public const int MaximumSensors = 24;
        public const float StaticStaleSeconds = 600f;
        public const float MobileStaleSeconds = 180f;
        private const float EnvelopeTolerance = 1f;

        private static readonly AirDefenceKind[] PassOrder =
            { AirDefenceKind.RadarSam, AirDefenceKind.Unknown, AirDefenceKind.IrSam, AirDefenceKind.Gun };

        private struct Accumulator
        {
            public float X;
            public float Z;
            public float MaxRange;
            public float MinAltitude;
            public float MaxAltitude;
            public AirDefenceKind Kind;
            public bool AnyTracked;
            public bool AnyPreWar;
            public bool AnyConfirmed;
            public bool AllStale;
            public bool AnyEmitting;
            public float MinAge;
            public int Launchers;
            public int AnchorHash;
            public int OwnRadarHash;
            public int SiteHash;
        }

        private readonly Accumulator[] rings;
        private readonly float[] sensorX = new float[MaximumSensors];
        private readonly float[] sensorZ = new float[MaximumSensors];
        private readonly int[] sensorHash = new int[MaximumSensors];
        private readonly bool[] sensorEmitting = new bool[MaximumSensors];

        public RingSet(int capacity = ThreatPictureLimits.MaximumRings)
        {
            rings = new Accumulator[capacity < 1 ? 1 : capacity];
        }

        /// <summary>Launchers the last build had no room for.</summary>
        public int Dropped { get; private set; }

        /// <summary>A tracked ring goes stale unseen for 600 s (static) or 180 s (mobile); pre-war and launch rings never do.</summary>
        public static bool IsStale(RingSource source, bool isStatic, float ageSeconds) =>
            source == RingSource.Tracked && ageSeconds > (isStatic ? StaticStaleSeconds : MobileStaleSeconds);

        public int Build(RingInput[] inputs, int count, AirDefenceRing[] into)
        {
            Dropped = 0;
            if (inputs == null || into == null) return 0;
            int n = count < inputs.Length ? count : inputs.Length;
            int capacity = rings.Length < into.Length ? rings.Length : into.Length;
            int sensors = CollectSensors(inputs, n);
            int ringCount = 0;
            for (int pass = 0; pass < PassOrder.Length; pass++)
            {
                AirDefenceKind kind = PassOrder[pass];
                for (int i = 0; i < n; i++)
                {
                    ref RingInput input = ref inputs[i];
                    if (input.Kind != kind || !(input.MaxRange > 0f)) continue;
                    int target = FindRing(ringCount, in input);
                    if (target >= 0)
                    {
                        Merge(ref rings[target], in input);
                        continue;
                    }
                    if (ringCount >= capacity)
                    {
                        Dropped++;
                        continue;
                    }
                    Start(ref rings[ringCount++], in input);
                }
            }
            for (int r = 0; r < ringCount; r++) into[r] = Finish(r, sensors);
            return ringCount;
        }

        private int CollectSensors(RingInput[] inputs, int n)
        {
            int sensors = 0;
            for (int i = 0; i < n && sensors < MaximumSensors; i++)
            {
                if (inputs[i].Kind != AirDefenceKind.Sensor && !inputs[i].OwnRadar) continue;
                sensorX[sensors] = inputs[i].X;
                sensorZ[sensors] = inputs[i].Z;
                sensorHash[sensors] = inputs[i].UnitHash;
                sensorEmitting[sensors] = inputs[i].Emitting;
                sensors++;
            }
            return sensors;
        }

        private int FindRing(int ringCount, in RingInput input)
        {
            for (int r = 0; r < ringCount; r++)
            {
                ref Accumulator ring = ref rings[r];
                if (Math.Abs(ring.MaxRange - input.MaxRange) > EnvelopeTolerance ||
                    Math.Abs(ring.MinAltitude - input.MinAltitude) > EnvelopeTolerance ||
                    Math.Abs(ring.MaxAltitude - input.MaxAltitude) > EnvelopeTolerance)
                    continue;
                if (Within(ring.X, ring.Z, input.X, input.Z)) return r;
            }
            return -1;
        }

        private static bool Within(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz <= SiteMergeMetres * SiteMergeMetres;
        }

        private static void Start(ref Accumulator ring, in RingInput input)
        {
            ring.X = input.X;
            ring.Z = input.Z;
            ring.MaxRange = input.MaxRange;
            ring.MinAltitude = input.MinAltitude;
            ring.MaxAltitude = input.MaxAltitude;
            ring.Kind = input.Kind;
            ring.AnyTracked = false;
            ring.AnyPreWar = false;
            ring.AnyConfirmed = false;
            ring.AllStale = true;
            ring.AnyEmitting = false;
            ring.MinAge = float.PositiveInfinity;
            ring.Launchers = 0;
            ring.AnchorHash = input.UnitHash;
            ring.OwnRadarHash = 0;
            ring.SiteHash = 0;
            Merge(ref ring, in input);
        }

        private static void Merge(ref Accumulator ring, in RingInput input)
        {
            if (input.Source != RingSource.Launch)
            {
                ring.Launchers++;
                // A tracked launcher with the same envelope says what a launch-only ring is.
                if (ring.Kind == AirDefenceKind.Unknown) ring.Kind = input.Kind;
            }
            ring.AnyTracked |= input.Source == RingSource.Tracked;
            ring.AnyPreWar |= input.Source == RingSource.PreWar;
            ring.AnyConfirmed |= input.Confirmed;
            ring.AllStale &= input.Stale;
            ring.AnyEmitting |= input.Emitting;
            if (input.AgeSeconds < ring.MinAge) ring.MinAge = input.AgeSeconds;
            if (input.OwnRadar && ring.OwnRadarHash == 0) ring.OwnRadarHash = input.UnitHash;
        }

        private AirDefenceRing Finish(int index, int sensors)
        {
            ref Accumulator ring = ref rings[index];
            int radarHash = ring.OwnRadarHash;
            bool emitting = ring.AnyEmitting;
            if (ring.Kind == AirDefenceKind.RadarSam && radarHash == 0)
            {
                float best = float.PositiveInfinity;
                for (int s = 0; s < sensors; s++)
                {
                    float dx = sensorX[s] - ring.X, dz = sensorZ[s] - ring.Z;
                    float d = dx * dx + dz * dz;
                    if (d > SiteMergeMetres * SiteMergeMetres || !(d < best)) continue;
                    best = d;
                    radarHash = sensorHash[s];
                    emitting = ring.AnyEmitting || sensorEmitting[s];
                }
            }
            bool radarGuided = ring.Kind == AirDefenceKind.RadarSam && radarHash != 0;
            if (!radarGuided) radarHash = 0;

            ring.SiteHash = ring.AnchorHash;
            for (int q = 0; q < index; q++)
            {
                if (!Within(rings[q].X, rings[q].Z, ring.X, ring.Z)) continue;
                ring.SiteHash = rings[q].SiteHash;
                break;
            }

            RingSource source = ring.AnyTracked ? RingSource.Tracked
                : ring.AnyPreWar ? RingSource.PreWar : RingSource.Launch;
            float age = float.IsPositiveInfinity(ring.MinAge) ? float.NaN : ring.MinAge;
            return new AirDefenceRing(ring.X, ring.Z, ring.MaxRange, ring.MinAltitude, ring.MaxAltitude, ring.Kind,
                radarGuided, emitting, source, ring.AnyConfirmed, age, ring.AllStale, ring.Launchers,
                ring.SiteHash, radarHash);
        }
    }
}
