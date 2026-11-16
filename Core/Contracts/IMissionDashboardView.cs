namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Read-only mission report. The gamemode owner supplies the latest authoritative snapshot.</summary>
    internal interface IMissionDashboardView
    {
        /// <summary>False when the mission has no dashboard report; Ready is false while awaiting the host.</summary>
        bool TryRead(out MissionDashboardStatus status);
    }

    internal struct MissionDashboardStatus
    {
        public string Name, Phase, Objective, Route, Detail;
        public bool Ready, HasScores, HasTimer;
        public float RemainingSeconds;
        public int Score0, Score1;
    }
}
