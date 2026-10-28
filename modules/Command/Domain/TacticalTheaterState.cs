namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The theater picture the news ticker reads, as one mutable bag.
    ///
    /// <para>Every field here is written by <c>CommandManager.ApplyCensus</c> from the host's census, once a second, whether or not any
    /// panel is open. An unknown count is -1 and an unknown ratio is NaN: a counter that is zero and never filled looks exactly like a
    /// counter reporting "none", and a reader has no way to tell the two apart.</para>
    /// </summary>
    internal sealed class TacticalTheaterState
    {
        public int ContestedAirbaseCount;
        public int ActiveClashesCount;

        public float TerritoryControlRatio = float.NaN;
        public float AirSuperiorityRatio = float.NaN;

        /// <summary>1 maximum to 5 nominal; 0 = not measured (the host census has not arrived), which the ticker never reads as an alert.</summary>
        public int DefconLevel;
        public string ActiveThreatWarning = "";

        public void Reset()
        {
            ContestedAirbaseCount = 0;
            ActiveClashesCount = 0;
            TerritoryControlRatio = float.NaN;
            AirSuperiorityRatio = float.NaN;
            DefconLevel = 0;
            ActiveThreatWarning = "";
        }
    }
}
