using BoscaliSummer.Features.PlayerSpawnPriority;
using BoscaliSummer.Tests.Framework;

namespace BoscaliSummer.Tests.Features.PlayerSpawnPriority
{
    internal static class PlayerSpawnPriorityTests
    {
        internal static void Run()
        {
            TestAssert.That(SpawnPriorityPolicy.CanEvict(true, true, false, true),
                "nearby unpiloted AI yields to a player");
            TestAssert.That(!SpawnPriorityPolicy.CanEvict(false, true, false, true) &&
                !SpawnPriorityPolicy.CanEvict(true, false, false, true) &&
                !SpawnPriorityPolicy.CanEvict(true, true, true, true) &&
                !SpawnPriorityPolicy.CanEvict(true, true, false, false),
                "AI requests, player aircraft, wing members and departed aircraft are protected");
        }
    }
}
