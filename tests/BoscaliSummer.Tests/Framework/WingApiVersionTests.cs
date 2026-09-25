using BoscaliSummer.Runtime;

namespace BoscaliSummer.Tests.Framework
{
    internal static class WingApiVersionTests
    {
        public static void Run()
        {
            TestAssert.That(WingApiVersions.SupportsSquad(1), "WingSquad 1 (Wing Command 0.9.x) is accepted");
            TestAssert.That(WingApiVersions.SupportsSquad(2), "WingSquad 2 (Wing Command 1.0, same method names) is accepted");
            TestAssert.That(!WingApiVersions.SupportsSquad(0), "an unversioned build fails closed");
            TestAssert.That(!WingApiVersions.SupportsSquad(3), "an unknown future shape fails closed");
        }
    }
}
