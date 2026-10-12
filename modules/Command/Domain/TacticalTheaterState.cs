namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The theater picture, as one mutable bag the strategic panel reads.
    ///
    /// <para>Every field here is written by <c>CommandManager.ApplyCensus</c> from the host's census, once a second, whether or not any
    /// panel is open. An unknown count is -1 and an unknown ratio is NaN: a counter that is zero and never filled looks exactly like a
    /// counter reporting "none", and a reader has no way to tell the two apart.</para>
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

        /// <summary>Interpolated front stretches where friendly and hostile control meet.</summary>
        public int FrontlineSegmentCount;

        /// <summary>Total length of those stretches, in metres.</summary>
        public float FrontlineLengthMetres;

        public float TerritoryControlRatio = float.NaN;

        /// <summary>Friendly AI sorties by role. <c>Observed == 0</c> means "not known", not "none".</summary>
        public SortieTally Sorties;

        public float AirSuperiorityRatio = float.NaN;

        /// <summary>1 maximum to 5 nominal; 0 = not measured (the host census has not arrived), which the ticker never reads as an alert.</summary>
        public int DefconLevel;
        public string PrimaryThreatDescription = "";
        public string ActiveThreatWarning = "";

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
            FrontlineSegmentCount = 0;
            FrontlineLengthMetres = 0f;

            TerritoryControlRatio = float.NaN;
            Sorties.Reset();

            AirSuperiorityRatio = float.NaN;
            DefconLevel = 0;
            PrimaryThreatDescription = "";
            ActiveThreatWarning = "";
        }
    }
}
