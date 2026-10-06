using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Tests.Framework
{
    internal static class WingApiVersionTests
    {
        public static void Run()
        {
            System.Func<int, bool> api = hash => hash == 7;
            TestAssert.That(WingApiVersions.IsWingMember(new int[0], api, 7),
                "Wing Command 0.9.x publishes no presence board: its membership API still identifies the wing");
            TestAssert.That(!WingApiVersions.IsWingMember(new[] { 3, 4 }, api, 7),
                "a published board is authoritative over the API");
            TestAssert.That(WingApiVersions.IsWingMember(new[] { 3, 7 }, null, 7), "a board member is a wing member");
            TestAssert.That(!WingApiVersions.IsWingMember(null, null, 7), "no board and no API: nobody is a wing member");
        }
    }
}
