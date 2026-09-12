namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>Read-only selection from Command's current faction control field, in global coordinates.</summary>
    internal interface ITerritoryIngress
    {
        bool TryNearestEdge(int factionId, float playerX, float playerZ, out float x, out float z);
    }
}
