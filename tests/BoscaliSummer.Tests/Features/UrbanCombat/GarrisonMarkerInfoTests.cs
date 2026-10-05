using System.Globalization;
using System.Threading;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class GarrisonMarkerInfoTests
    {
        public static void Run()
        {
            string name = GarrisonMarkerInfo.Append("BoscaliSummer:Garrison:Roof:Alpha:2:1", -18.25f, 14.5f, -9f, 9.25f);
            TestAssert.That(GarrisonMarkerInfo.TryParse(name,
                out float minX, out float maxX, out float minZ, out float maxZ),
                "encoded roof patch parses");
            TestAssert.That(minX == -18.25f && maxX == 14.5f, "patch X extents round-trip");
            TestAssert.That(minZ == -9f && maxZ == 9.25f, "patch Z extents round-trip");

            TestAssert.That(!GarrisonMarkerInfo.TryParse("BoscaliSummer:Garrison:Roof:Alpha:2:1",
                out _, out _, out _, out _), "legacy names without marker data are rejected");
            TestAssert.That(!GarrisonMarkerInfo.TryParse(null,
                out _, out _, out _, out _), "null names are rejected");
            TestAssert.That(!GarrisonMarkerInfo.TryParse("Base$m1_2_3",
                out _, out _, out _, out _), "truncated marker data is rejected");
            TestAssert.That(!GarrisonMarkerInfo.TryParse("Base$m1_2_3_4_5",
                out _, out _, out _, out _), "extra marker data is rejected");
            TestAssert.That(!GarrisonMarkerInfo.TryParse("Base$m1_2_3_x",
                out _, out _, out _, out _), "non-numeric marker data is rejected");
            TestAssert.That(!GarrisonMarkerInfo.TryParse("Base$m0_1_0_1",
                out _, out _, out _, out _), "too-narrow patches are rejected");
            TestAssert.That(!GarrisonMarkerInfo.TryParse("Base$m5_1_0_10",
                out _, out _, out _, out _), "inverted X extents are rejected");

            string collision = GarrisonMarkerInfo.Append("Site$m1_1_1_1", 3.5f, 15f, 4f, 12f);
            TestAssert.That(GarrisonMarkerInfo.TryParse(collision,
                out minX, out maxX, out minZ, out maxZ) &&
                minX == 3.5f && maxX == 15f && minZ == 4f && maxZ == 12f,
                "the last marker wins over a token inside the base name");

            TestAssert.That(GarrisonMarkerInfo.TryReadTier(
                GarrisonMarkerInfo.Append("BoscaliSummer:Garrison:Roof:Alpha:2:1:t2", 0f, 10f, 0f, 10f),
                out int tier) && tier == 2,
                "zone nests carry their tier before the marker suffix");
            TestAssert.That(GarrisonMarkerInfo.TryReadTier(
                GarrisonMarkerInfo.Append("BoscaliSummer:Garrison:Roof:Alpha:2:1:t0", 0f, 10f, 0f, 10f),
                out tier) && tier == 0,
                "tier zero parses");
            TestAssert.That(!GarrisonMarkerInfo.TryReadTier(name, out _),
                "untiered legacy names report no tier");
            TestAssert.That(!GarrisonMarkerInfo.TryReadTier(
                GarrisonMarkerInfo.Append("BoscaliSummer:Garrison:Roof:Assault:412:1:0", 0f, 10f, 0f, 10f),
                out _),
                "assault nests carry no tier");
            TestAssert.That(!GarrisonMarkerInfo.TryReadTier(
                GarrisonMarkerInfo.Append("BoscaliSummer:Garrison:Roof:theater:2:1", 0f, 10f, 0f, 10f),
                out _),
                "a zone starting with 't' is not a tier segment");
            TestAssert.That(!GarrisonMarkerInfo.TryReadTier(null, out _),
                "null names report no tier");
            TestAssert.That(!GarrisonMarkerInfo.TryReadTier("BoscaliSummer:Garrison:Roof:Alpha:2:1:t2",
                out _),
                "names without marker data report no tier");

            const string prefix = "BoscaliSummer:Garrison:";
            TestAssert.That(GarrisonMarkerInfo.TryReadZone(
                GarrisonMarkerInfo.Append(prefix + "Roof:Alpha:2:1:t2", 0f, 10f, 0f, 10f),
                prefix, out string zone) && zone == "Alpha",
                "zone nests report their sanitized zone");
            TestAssert.That(!GarrisonMarkerInfo.TryReadZone(
                GarrisonMarkerInfo.Append(prefix + "Roof:Assault:412:1:0", 0f, 10f, 0f, 10f),
                prefix, out _),
                "assault nests have no zone membership");
            TestAssert.That(!GarrisonMarkerInfo.TryReadZone(
                GarrisonMarkerInfo.Append(prefix + "Roof:Seize:77:1:0", 0f, 10f, 0f, 10f),
                prefix, out _),
                "seized buildings have no zone membership");
            TestAssert.That(!GarrisonMarkerInfo.TryReadZone("Civilian Hut", prefix, out _),
                "ordinary building names report no zone");
            TestAssert.That(GarrisonMarkerInfo.SanitizeZone("Airbase Alpha:2") == "Airbase_Alpha_2",
                "zone segments carry no colons or spaces");
            TestAssert.That(GarrisonMarkerInfo.SanitizeZone(null) == "Airbase",
                "null airbase names fall back");

            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string localized = GarrisonMarkerInfo.Append("Base", -10.5f, 20.25f, -5f, 5f);
                TestAssert.That(localized.IndexOf(',') < 0, "encoded floats never use culture decimal commas");
                TestAssert.That(GarrisonMarkerInfo.TryParse(localized,
                    out minX, out _, out _, out _) && minX == -10.5f,
                    "encoded values parse independent of current culture");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
