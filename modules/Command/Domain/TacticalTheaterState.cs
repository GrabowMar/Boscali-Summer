namespace BoscaliSummer.Features.Command.Domain
{
    /// <summary>
    /// The theater picture, as one mutable bag the strategic panel reads.
    ///
    /// <para>Every field here is written each telemetry pass. Fields that nothing produces
    /// do not belong: a counter that is reset to zero and never filled looks exactly like a
    /// counter reporting "none", and the panel has no way to tell the reader apart.</para>
    /// </summary>
    internal sealed class TacticalTheaterState
    {
        public int FriendlyAircraftCount;
        public int HostileAircraftCount;

        public int FriendlyAirbaseCount;
        public int HostileAirbaseCount;
        public int NeutralAirbaseCount;
        public int ContestedAirbaseCount;

        /// <summary>
        /// Emitters on the friendly network. This counts <em>radars</em>, which is what the
        /// game exposes; it is not a SAM-site count and must not be labelled as one.
        /// </summary>
        public int FriendlyRadarCount;

        public int FriendlyGroundUnitsCount;
        public int HostileGroundUnitsCount;

        public int FriendlySectorCount;
        public int HostileSectorCount;
        public int ContestedSectorCount;
        public int NeutralSectorCount;
        public int ActiveClashesCount;
        public int TotalNodesCount;

        /// <summary>Interpolated front stretches where friendly and hostile control meet.</summary>
        public int FrontlineSegmentCount;

        /// <summary>Total length of those stretches, in metres.</summary>
        public float FrontlineLengthMetres;

        public float TerritoryControlRatio = float.NaN;

        /// <summary>Friendly AI sorties by role. <c>Observed == 0</c> means "not known", not "none".</summary>
        public SortieTally Sorties;

        public float AirSuperiorityRatio = float.NaN;
        public int DefconLevel = 3;
        public string PrimaryThreatDescription = "NOMINAL";
        public string ActiveThreatWarning = "AIRSPACE NOMINAL";

        public void Reset()
        {
            FriendlyAircraftCount = 0;
            HostileAircraftCount = 0;

            FriendlyAirbaseCount = 0;
            HostileAirbaseCount = 0;
            NeutralAirbaseCount = 0;
            ContestedAirbaseCount = 0;

            FriendlyRadarCount = 0;

            FriendlyGroundUnitsCount = 0;
            HostileGroundUnitsCount = 0;

            FriendlySectorCount = 0;
            HostileSectorCount = 0;
            ContestedSectorCount = 0;
            NeutralSectorCount = 0;
            ActiveClashesCount = 0;
            TotalNodesCount = 0;
            FrontlineSegmentCount = 0;
            FrontlineLengthMetres = 0f;

            TerritoryControlRatio = float.NaN;
            Sorties.Reset();

            AirSuperiorityRatio = float.NaN;
            DefconLevel = 3;
            PrimaryThreatDescription = "NOMINAL";
            ActiveThreatWarning = "AIRSPACE NOMINAL";
        }
    }
}
