namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Read-only selection from Command's current faction control field, in global coordinates.</summary>
    internal interface ITerritoryIngress
    {
        bool TryNearestEdge(int factionId, float playerX, float playerZ, out float x, out float z);
        int CopyFrontlineSites(int factionId, FrontlineSite[] destination);
        bool OwnsPosition(int factionId, float x, float z);
    }

    internal readonly struct FrontlineSite
    {
        public readonly float X, Z, ThreatX, ThreatZ, HalfLength;
        public FrontlineSite(float x, float z, float threatX, float threatZ, float halfLength)
        { X = x; Z = z; ThreatX = threatX; ThreatZ = threatZ; HalfLength = halfLength; }
    }
}
