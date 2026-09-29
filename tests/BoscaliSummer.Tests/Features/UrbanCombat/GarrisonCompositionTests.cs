using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class GarrisonCompositionTests
    {
        public static void Run()
        {
            TestAssert.That(GarrisonComposition.DefinitionIndex(0, 0, 0, 3) == 0, "slot zero reads first");
            TestAssert.That(GarrisonComposition.DefinitionIndex(1, 0, 0, 3) == 1, "slots walk the table");
            TestAssert.That(GarrisonComposition.DefinitionIndex(0, 1, 0, 3) == 1, "seed rotates the order");
            TestAssert.That(GarrisonComposition.DefinitionIndex(2, 1, 0, 3) == 0, "rotation wraps");
            TestAssert.That(GarrisonComposition.DefinitionIndex(0, -1, 0, 3) == 2, "negative seeds wrap");
            TestAssert.That(GarrisonComposition.DefinitionIndex(0, 0, 3, 3) == 2, "metros open heavy");
            TestAssert.That(GarrisonComposition.DefinitionIndex(1, 0, 3, 3) == 1, "heavy rule touches slot zero only");
            TestAssert.That(GarrisonComposition.DefinitionIndex(0, 0, 3, 2) == 0, "short tables skip the heavy rule");
            TestAssert.That(GarrisonComposition.DefinitionIndex(0, 0, 0, 0) == 0, "empty table reads zero");

            TestAssert.That(GarrisonComposition.ZoneSeed("Alpha") == GarrisonComposition.ZoneSeed("Alpha"),
                "zone seed is stable");
            bool varied = false;
            int first = GarrisonComposition.ZoneSeed("Alpha");
            string[] zones = { "Bravo", "Charlie", "Delta", "Echo", "Foxtrot" };
            for (int i = 0; i < zones.Length; i++)
                if (GarrisonComposition.ZoneSeed(zones[i]) != first) varied = true;
            TestAssert.That(varied, "neighbouring zones rotate differently");

            TestAssert.That(GarrisonComposition.EncampmentTypeIndex(100f, 200f) ==
                GarrisonComposition.EncampmentTypeIndex(104f, 203f), "same ground types the same");
            bool[] seen = new bool[3];
            for (int x = 0; x < 32; x++)
                for (int z = 0; z < 32; z++)
                {
                    int type = GarrisonComposition.EncampmentTypeIndex(x * 16f + 1f, z * 16f + 1f);
                    TestAssert.That(type >= 0 && type < 3, "encampment type stays in range");
                    seen[type] = true;
                }
            TestAssert.That(seen[0] && seen[1] && seen[2], "sites vary by ground");
        }
    }
}
