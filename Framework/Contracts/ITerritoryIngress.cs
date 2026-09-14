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

        /// <summary>
        /// How evenly opposing ground forces actually hold this border, 0..1.
        /// Zero means one side (or neither) is present — a quiet, unfortified line.
        /// </summary>
        public readonly float Pressure;

        public FrontlineSite(float x, float z, float threatX, float threatZ, float halfLength, float pressure = 0f)
        { X = x; Z = z; ThreatX = threatX; ThreatZ = threatZ; HalfLength = halfLength; Pressure = pressure; }
    }
}
