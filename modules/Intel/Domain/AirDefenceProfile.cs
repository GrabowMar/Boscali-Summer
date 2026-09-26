using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Intel.Domain
{
    /// <summary>One weapon station's envelope, copied out of <c>WeaponInfo</c> so the profile stays pure.</summary>
    internal readonly struct StationSample
    {
        public readonly float AntiAir;
        public readonly float MaxRange;
        public readonly float MinAltitude;
        public readonly float MaxAltitude;
        public readonly float MinIr;
        public readonly float MinRadar;
        public readonly bool Gun;
        public readonly bool Jammer;

        public StationSample(float antiAir, float maxRange, float minAltitude, float maxAltitude,
            float minIr, float minRadar, bool gun, bool jammer)
        {
            AntiAir = antiAir;
            MaxRange = maxRange;
            MinAltitude = minAltitude;
            MaxAltitude = maxAltitude;
            MinIr = minIr;
            MinRadar = minRadar;
            Gun = gun;
            Jammer = jammer;
        }
    }

    /// <summary>
    /// What one unit type can do to an aircraft, read from its weapon stations the way
    /// vanilla's <c>CombatAI.AnalyzeTarget</c> reads them. One band per unit: its best station,
    /// radar over IR over gun, then the longer reach.
    /// </summary>
    internal readonly struct AirDefenceProfile
    {
        /// <summary>
        /// The anti-air effectiveness a station needs to ring. The spec's 0.5 would also admit
        /// the MLRS rocket (exactly 0.50, 40 km, ceiling 1000 m) and the AGMs (0.50); the weakest
        /// real air-defence weapon is the 30 mm SPAAG gun at 0.63, so 0.55 separates them.
        /// Tank guns (0.35), IFV autocannons and 23 mm AAA (0.32) and the CRAM/laser
        /// counter-missile systems (0.01) stay out.
        /// </summary>
        public const float MinimumAntiAir = 0.55f;

        /// <summary>A station whose ceiling is 50 m or less cannot engage aircraft.</summary>
        public const float MinimumCeiling = 50f;

        public readonly bool HasBand;
        public readonly float MaxRange;
        public readonly float MinAltitude;
        public readonly float MaxAltitude;
        public readonly AirDefenceKind Kind;
        /// <summary>The unit carries its own radar (see <c>UnitProfiles</c> for how that is read).</summary>
        public readonly bool RadarEmitter;
        /// <summary>A station needs its target to emit: an anti-radiation weapon. Never guidance.</summary>
        public readonly bool AntiRadiation;

        private AirDefenceProfile(bool hasBand, float maxRange, float minAltitude, float maxAltitude,
            AirDefenceKind kind, bool radarEmitter, bool antiRadiation)
        {
            HasBand = hasBand;
            MaxRange = maxRange;
            MinAltitude = minAltitude;
            MaxAltitude = maxAltitude;
            Kind = kind;
            RadarEmitter = radarEmitter;
            AntiRadiation = antiRadiation;
        }

        /// <summary>A radar with no ring of its own: SEAD value only.</summary>
        public bool IsSensor => !HasBand && RadarEmitter;

        public bool IsAirDefence => HasBand || RadarEmitter;

        public static AirDefenceProfile Build(StationSample[] stations, int count, bool radarEmitter)
        {
            bool hasBand = false;
            bool antiRadiation = false;
            float range = 0f, minAltitude = 0f, maxAltitude = 0f;
            AirDefenceKind kind = AirDefenceKind.Unknown;
            int n = stations == null || count <= 0 ? 0 : count < stations.Length ? count : stations.Length;
            for (int i = 0; i < n; i++)
            {
                StationSample station = stations[i];
                // minRadar means the TARGET must emit (CombatAI.cs:163-166): an anti-radiation
                // weapon. It says nothing about how this unit guides its own missiles.
                if (station.MinRadar > 0f && !station.Jammer) antiRadiation = true;
                if (!(station.AntiAir >= MinimumAntiAir) || !(station.MaxAltitude > MinimumCeiling) ||
                    !(station.MaxRange > 0f))
                    continue;
                AirDefenceKind stationKind = station.Gun ? AirDefenceKind.Gun
                    : station.MinIr > 0f ? AirDefenceKind.IrSam : AirDefenceKind.RadarSam;
                if (hasBand)
                {
                    int rank = Rank(stationKind), best = Rank(kind);
                    if (rank > best || (rank == best && !(station.MaxRange > range))) continue;
                }
                hasBand = true;
                range = station.MaxRange;
                minAltitude = station.MinAltitude > 0f ? station.MinAltitude : 0f;
                maxAltitude = station.MaxAltitude;
                kind = stationKind;
            }
            if (!hasBand) kind = radarEmitter ? AirDefenceKind.Sensor : AirDefenceKind.Unknown;
            return new AirDefenceProfile(hasBand, range, minAltitude, maxAltitude, kind, radarEmitter, antiRadiation);
        }

        /// <summary>Keep order when rings compete for room: radar, launch-only, IR, gun, sensor.</summary>
        public static int Rank(AirDefenceKind kind)
        {
            switch (kind)
            {
                case AirDefenceKind.RadarSam: return 0;
                case AirDefenceKind.Unknown: return 1;
                case AirDefenceKind.IrSam: return 2;
                case AirDefenceKind.Gun: return 3;
                default: return 4;
            }
        }
    }
}
