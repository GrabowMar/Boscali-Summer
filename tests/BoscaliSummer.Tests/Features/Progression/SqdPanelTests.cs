using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Progression
{
    internal static class ProgressionPresentationTests
    {
        public static void Run()
        {
            TestPerkClassification();
            TestWingmanPresenceTracking();
        }

        private static void TestPerkClassification()
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition def = PerkCatalog.All[i];
                if (def.Capability != null)
                {
                    TestAssert.That(def.Multiplier == 1f, "authorisation perk must not have a multiplier");
                    TestAssert.That(def.Cost >= 1, "authorisation perk must cost at least 1 point");
                }
                else
                {
                    TestAssert.That(def.Multiplier != 1f, "passive perk must modify multiplier");
                }
            }
        }

        private static void TestWingmanPresenceTracking()
        {
            var prevGuid = PresenceBoard.GetString(PresenceBoard.WingGuid);
            var prevIds = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);

            try
            {
                PresenceBoard.SetString(PresenceBoard.WingGuid, "com.marci.wingcommand");
                TestAssert.That(PresenceBoard.GetString(PresenceBoard.WingGuid) == "com.marci.wingcommand",
                    "wing command GUID must be readable");

                int[] testWing = new[] { 101, 202, 303 };
                PresenceBoard.SetInts(PresenceBoard.WingMemberIds, testWing);

                int[] retrieved = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
                TestAssert.That(retrieved.Length == 3, "wing member count mismatch");
                TestAssert.That(PresenceBoard.Contains(retrieved, 202), "wingman 202 must be found");
                TestAssert.That(!PresenceBoard.Contains(retrieved, 999), "unknown unit 999 must not be found");
            }
            finally
            {
                PresenceBoard.SetString(PresenceBoard.WingGuid, prevGuid);
                PresenceBoard.SetInts(PresenceBoard.WingMemberIds, prevIds);
            }
        }
    }
}
