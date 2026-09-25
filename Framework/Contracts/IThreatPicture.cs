namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>What a known air-defence ring stands for.</summary>
    internal enum AirDefenceKind : byte
    {
        /// <summary>Known only from a launch. Avoided like a radar SAM, never a SEAD target.</summary>
        Unknown = 0,
        RadarSam = 1,
        IrSam = 2,
        Gun = 3,
        /// <summary>A radar with no anti-air weapon of its own: SEAD value, no ring.</summary>
        Sensor = 4
    }

    /// <summary>How the observing faction came to know a ring.</summary>
    internal enum RingSource : byte
    {
        Tracked = 0,
        PreWar = 1,
        Launch = 2
    }

    /// <summary>Buffer sizes and kind masks every <see cref="IThreatPicture"/> caller shares.</summary>
    internal static class ThreatPictureLimits
    {
        /// <summary>Rings per faction; size <see cref="IThreatPicture.CopyAirDefence"/> buffers to this.</summary>
        public const int MaximumRings = 96;

        /// <summary>Points a leg is sampled at by <see cref="IThreatPicture.TryGetSegmentExposure"/>.</summary>
        public const int SegmentSamples = 16;

        /// <summary>Radar SAM cones plus launch-only rings, which are avoided as if radar-guided.</summary>
        public const byte RadarConeMask = (1 << (int)AirDefenceKind.Unknown) | (1 << (int)AirDefenceKind.RadarSam);

        /// <summary>IR SAM and gun rings.</summary>
        public const byte PointDefenceMask = (1 << (int)AirDefenceKind.IrSam) | (1 << (int)AirDefenceKind.Gun);

        /// <summary>Every kind that draws a ring. Sensors never do.</summary>
        public const byte AllRingsMask = RadarConeMask | PointDefenceMask;

        public static byte Bit(AirDefenceKind kind) => (byte)(1 << (int)kind);
    }

    /// <summary>
    /// One known air-defence ring as the observing faction knows it: a launcher site's cone,
    /// never truth. Launchers within 1.5 km with the same envelope merge into one ring; rings
    /// of one site share <see cref="SiteHash"/>.
    /// </summary>
    internal readonly struct AirDefenceRing
    {
        public readonly float X;
        public readonly float Z;
        public readonly float MaxRange;
        public readonly float MinAltitude;
        public readonly float MaxAltitude;
        public readonly AirDefenceKind Kind;
        /// <summary>A radar SAM with a known radar at the site (its own or a radar carrier within 1.5 km).</summary>
        public readonly bool RadarGuided;
        /// <summary>The site's radar was emitting at the last rebuild (an RWR sees this).</summary>
        public readonly bool Emitting;
        public readonly RingSource Source;
        /// <summary>At least one member has been in the observer's own tracking.</summary>
        public readonly bool Confirmed;
        /// <summary>Seconds since the freshest member was seen; NaN when unknown.</summary>
        public readonly float AgeSeconds;
        public readonly bool Stale;
        public readonly int Launchers;
        public readonly int SiteHash;
        /// <summary>The emitter a SEAD shot must kill; 0 when none is known.</summary>
        public readonly int RadarHash;

        public AirDefenceRing(float x, float z, float maxRange, float minAltitude, float maxAltitude,
            AirDefenceKind kind, bool radarGuided, bool emitting, RingSource source, bool confirmed,
            float ageSeconds, bool stale, int launchers, int siteHash, int radarHash)
        {
            X = x;
            Z = z;
            MaxRange = maxRange;
            MinAltitude = minAltitude;
            MaxAltitude = maxAltitude;
            Kind = kind;
            RadarGuided = radarGuided;
            Emitting = emitting;
            Source = source;
            Confirmed = confirmed;
            AgeSeconds = ageSeconds;
            Stale = stale;
            Launchers = launchers;
            SiteHash = siteHash;
            RadarHash = radarHash;
        }

        /// <summary>Horizontal reach against a target at <paramref name="agl"/> metres above ground.</summary>
        public float EffectiveRadius(float agl) => EffectiveRadius(MaxRange, MinAltitude, MaxAltitude, agl);

        /// <summary>
        /// Exactly vanilla's <c>CombatAI.AnalyzeTarget</c> gate, solved for distance: a target is
        /// refused when radarAlt &lt; minAltitude × distance / maxRange or radarAlt &gt; maxAltitude,
        /// so coverage is a cone. A negative height reads as ground level; NaN reaches nothing.
        /// </summary>
        public static float EffectiveRadius(float maxRange, float minAltitude, float maxAltitude, float agl)
        {
            if (float.IsNaN(agl) || !(maxRange > 0f) || agl > maxAltitude) return 0f;
            if (agl < 0f) agl = 0f;
            if (minAltitude > 0f)
            {
                float cone = agl * maxRange / minAltitude;
                return cone < maxRange ? cone : maxRange;
            }
            return maxRange;
        }
    }

    /// <summary>A release point and what flying to it costs against the observer's rings.</summary>
    internal readonly struct AttackProfile
    {
        public readonly float ReleaseX;
        public readonly float ReleaseZ;
        /// <summary>Metres of the transit leg inside radar-SAM or launch-only rings at transit height.</summary>
        public readonly float RadarConeMetres;
        /// <summary>Metres of the transit leg inside IR-SAM or gun rings at transit height.</summary>
        public readonly float PointDefenceMetres;
        /// <summary>The release point itself is inside a ring at release height.</summary>
        public readonly bool ReleaseCovered;
        /// <summary>Every ring that costs anything here is stale.</summary>
        public readonly bool StaleOnly;

        public AttackProfile(float releaseX, float releaseZ, float radarConeMetres, float pointDefenceMetres,
            bool releaseCovered, bool staleOnly)
        {
            ReleaseX = releaseX;
            ReleaseZ = releaseZ;
            RadarConeMetres = radarConeMetres;
            PointDefenceMetres = pointDefenceMetres;
            ReleaseCovered = releaseCovered;
            StaleOnly = staleOnly;
        }
    }

    /// <summary>What the observer knows about the ground around a point.</summary>
    internal readonly struct AreaIntel
    {
        /// <summary>What a failed read hands back: nothing known, never a confident zero.</summary>
        public static readonly AreaIntel Unknown =
            new AreaIntel(false, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);

        /// <summary>Own units looked at the area within the last 240 s.</summary>
        public readonly bool Scouted;
        /// <summary>Seconds since the area was last looked at; NaN when never.</summary>
        public readonly float LastObservedAgeSeconds;
        /// <summary>Known hostile ground power (ForceRoles weights), whether or not it is scouted.</summary>
        public readonly float Power;
        public readonly float AntiTank;
        public readonly float AirDefence;
        public readonly float Artillery;

        public AreaIntel(bool scouted, float lastObservedAgeSeconds, float power, float antiTank,
            float airDefence, float artillery)
        {
            Scouted = scouted;
            LastObservedAgeSeconds = lastObservedAgeSeconds;
            Power = power;
            AntiTank = antiTank;
            AirDefence = airDefence;
            Artillery = artillery;
        }
    }

    /// <summary>
    /// Each faction's fog-of-war threat picture (owner: Intel). Every read takes the OBSERVER's
    /// <c>FactionHQ.GetInstanceID()</c> and answers only from that faction's own knowledge:
    /// its tracking, launches its aircraft saw, pre-war intel of fixed sites, and ground its own
    /// units looked at. Coordinates are global X/Z. A read with no ready picture returns
    /// false/0 with NaN or −1 outs — never a confident zero. A client holds only its own
    /// faction's picture, built while something asks.
    /// </summary>
    internal interface IThreatPicture
    {
        /// <summary>Bumped when the observer's rings change; 0 when there is no picture.</summary>
        uint Version(int observer);

        bool IsReady(int observer);

        /// <summary>Copies up to <see cref="ThreatPictureLimits.MaximumRings"/> rings; returns how many.</summary>
        int CopyAirDefence(int observer, AirDefenceRing[] into);

        /// <summary>Deepest penetration into any ring at this point and height, and how many rings cover it.</summary>
        bool TryGetCoverage(int observer, float x, float z, float agl, out float depthMetres, out int rings);

        /// <summary>Metres of the leg a→b inside rings of <paramref name="kindMask"/>, sampled at 16 points.</summary>
        bool TryGetSegmentExposure(int observer, float ax, float az, float bx, float bz, float agl, byte kindMask,
            out float exposedMetres);

        /// <summary>
        /// Tests 9 release points on a ±80° arc of <paramref name="releaseRange"/> around the
        /// target, facing the attacker, and returns the cheapest by 10 × radar-cone metres +
        /// point-defence metres. Allocation-free. False with no picture, or when this frame's
        /// eight searches (all callers) are spent: ask again next frame.
        /// </summary>
        bool TryFindAttackProfile(int observer, float fromX, float fromZ, float targetX, float targetZ,
            float releaseRange, float transitAgl, float releaseAgl, out AttackProfile profile);

        bool TryGetAreaIntel(int observer, float x, float z, float radius, out AreaIntel intel);
    }
}
