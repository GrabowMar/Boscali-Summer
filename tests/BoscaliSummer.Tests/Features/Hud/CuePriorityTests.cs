using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class CuePriorityTests
    {
        public static void Run()
        {
            string[] names = System.Enum.GetNames(typeof(BoardPage));
            TestAssert.Eq(names.Length, CuePriority.PageCount, "Every shared menu page has a HUD registry entry");
            ushort all = 0;
            for (int i = 0; i < names.Length; i++)
            {
                ScopePage page = (ScopePage)i;
                TestAssert.Eq(page.ToString(), names[i], "The contract and HUD agree on each saved page value");
                TestAssert.That(!CuePriority.Has(all, page), "Each page occupies a distinct mask bit");
                all |= CuePriority.Bit(page);
                TestAssert.That(CuePriority.Has(all, page), "A registry bit includes its page");
            }
            TestAssert.Eq(all, (ushort)0x3ff, "All ten pages fit the registry mask");
        }
    }
}
