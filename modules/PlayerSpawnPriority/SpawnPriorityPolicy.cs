namespace BoscaliSummer.Features.PlayerSpawnPriority
{
    internal static class SpawnPriorityPolicy
    {
        internal static bool CanEvict(bool playerRequested, bool aiOccupied,
            bool wingMember, bool closeToSpawn) =>
            playerRequested && aiOccupied && !wingMember && closeToSpawn;
    }
}
