namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>One host-owned station for idle mission AI aircraft of a faction.</summary>
    internal interface ITheaterAirStationView
    {
        bool TryGetStation(string faction, out float x, out float z, out float radiusMeters);
    }
}
