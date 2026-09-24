using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class MfdPanelTests
    {
        public static void Run()
        {
            FactionResourceTests.Run();
            TargetPresetTests.Run();
            string[] classes = { "AircraftDefinition", "VehicleDefinition", "BuildingDefinition", "ShipDefinition", "MissileDefinition" };
            foreach (string type in classes)
            {
                TestAssert.That(MfdTargetPresets.UnitClass(MfdTargetPreset.All, type), "ALL restores every unit class");
                TestAssert.That(MfdTargetPresets.UnitClass(MfdTargetPreset.Laser, type), "LASER retains every unit class");
                TestAssert.That(MfdTargetPresets.UnitClass(MfdTargetPreset.Air, type) == (type == "AircraftDefinition"),
                    "AIR includes aircraft only, excluding missiles");
                TestAssert.That(MfdTargetPresets.UnitClass(MfdTargetPreset.Ground, type) ==
                    (type == "VehicleDefinition" || type == "BuildingDefinition"), "GROUND includes vehicles and buildings");
                TestAssert.That(MfdTargetPresets.UnitClass(MfdTargetPreset.Sea, type) == (type == "ShipDefinition"),
                    "SEA includes ships only");
                TestAssert.That(MfdTargetPresets.UnitClass(MfdTargetPreset.Sead, type) == (type == "VehicleDefinition"),
                    "SEAD restricts the parent class to vehicles");
            }
            foreach (MfdTargetPreset preset in System.Enum.GetValues(typeof(MfdTargetPreset)))
            {
                bool both = preset == MfdTargetPreset.All || preset == MfdTargetPreset.Laser;
                TestAssert.That(MfdTargetPresets.Faction(preset, true) == (both || preset == MfdTargetPreset.Friendly),
                    "Friendly faction policy for " + preset);
                TestAssert.That(MfdTargetPresets.Faction(preset, false) == (both || preset != MfdTargetPreset.Friendly),
                    "Hostile faction policy for " + preset);
            }
            foreach (string type in new[] { "AAA", "IR_SAM", "R_SAM", "RDR" })
                TestAssert.That(MfdTargetPresets.Vehicle(MfdTargetPreset.Sead, type), "SEAD includes " + type);
            foreach (string type in new[] { "TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "FUTURE_TYPE", null })
            {
                TestAssert.That(!MfdTargetPresets.Vehicle(MfdTargetPreset.Sead, type), "SEAD excludes non-air-defense definitions");
                TestAssert.That(MfdTargetPresets.Vehicle(MfdTargetPreset.All, type), "ALL resets SEAD platform exclusions");
            }
            TestAssert.That(!MfdTargetPresets.UnitClass(MfdTargetPreset.Air, "FutureDefinition"), "Restricted presets exclude unknown classes");
            TestAssert.That(MfdChartScale.Fraction(0f, 0f) == 0f, "Empty graphs have zero bars");
            TestAssert.That(MfdChartScale.Fraction(25f, 100f) == .25f, "All chart series use a common linear scale");
            TestAssert.That(MfdChartScale.Fraction(200f, 100f) == 1f && MfdChartScale.Fraction(-5f, 100f) == 0f,
                "Bars cannot escape their track");
            TestAssert.That(MfdChartScale.Fraction(float.NaN, 100f) == 0f &&
                MfdChartScale.Fraction(10f, float.PositiveInfinity) == 0f, "Unavailable chart data cannot corrupt geometry");
            TestAssert.That(BoscaliSummer.Runtime.MfdSlots.Set == "SET", "SET slot identifier is canonical");
            RunRailCatalogTests();
            RunHostedEntryTests();
            RunLogToneTests();
        }

        private static void RunRailCatalogTests()
        {
            MfdRailEntry set = MfdRailCatalog.For("SET");
            TestAssert.That(set.Code == "SET" && set.Name == "SETTINGS" && set.Glyph == "settings",
                "SET maps to a readable rail entry");
            MfdRailEntry adm = MfdRailCatalog.For("ADM");
            TestAssert.That(adm.Code == "ADM" && !adm.HasName && adm.Glyph == null,
                "the deleted ADM bezel leaves no rail entry behind");
            MfdRailEntry bdf = MfdRailCatalog.For("bdf");
            TestAssert.That(bdf.Code == "BDF" && bdf.Glyph == "faction", "codes are case-insensitive");
            MfdRailEntry unknown = MfdRailCatalog.For("<SUD>");
            TestAssert.That(unknown.Code == "SUD" && !unknown.HasName && unknown.Glyph == null,
                "unknown codes survive sanitising without an invented meaning");
            TestAssert.That(MfdRailCatalog.For(null).Code == "" && MfdRailCatalog.For("  ").Code == "",
                "empty labels stay empty");
            TestAssert.That(MfdRailCatalog.OrderRank("BDF") < MfdRailCatalog.OrderRank("PALA"),
                "BDF leads the rail");
            TestAssert.That(MfdRailCatalog.OrderRank("PALA") < MfdRailCatalog.OrderRank("MAP"),
                "PALA sits directly below BDF, ahead of every unranked button");
            TestAssert.That(MfdRailCatalog.OrderRank("MAP") == MfdRailCatalog.OrderRank("WMC"),
                "unranked buttons share the tail of the order");
            TestAssert.That(MfdRailCatalog.OrderRank("<PALA>") == MfdRailCatalog.OrderRank("PALA"),
                "order rank uses the sanitised code");
            TestAssert.That(MfdRailCatalog.Sanitise("M<AP>", 8) == "MAP",
                "markup characters never leak into a composed label");
            TestAssert.That(MfdRailCatalog.Sanitise("A\t B", 8) == "A B",
                "control characters collapse into a single space");
            TestAssert.That(MfdRailCatalog.Sanitise("A\tB", 8) == "A B",
                "tabs collapse like any other whitespace");
            TestAssert.That(MfdRailCatalog.Sanitise("BDF\n<size=11>BOSCALI HQ</size>", 8) == "BDF SIZE",
                "a branded line sanitises to readable text, never to the tag glued onto the code");
            TestAssert.That(MfdRailCatalog.Sanitise("BDF\n<size=11>", 8) == "BDF SIZE",
                "a line break before markup survives as a space");
            TestAssert.That(MfdRailCatalog.Sanitise("ABCDEFGHIJ", 4) == "ABCD",
                "labels respect the rail's width ceiling");
        }

        private static void RunHostedEntryTests()
        {
            MfdRailEntry events = MfdRailCatalog.For("EVN");
            TestAssert.That(events.HasName && events.Glyph == "pulse",
                "the hosted EVN button carries a readable rail entry");
            TestAssert.That(events.Glyph != MfdRailCatalog.For("MIS").Glyph,
                "the event feed must not wear the mission flag");
            MfdRailEntry comms = MfdRailCatalog.For("COM");
            TestAssert.That(comms.HasName && comms.Name == "COMMS" && comms.Glyph == "comms",
                "the hosted COM button reads as COMMS with its own glyph");
        }

        private static void RunLogToneTests()
        {
            TestAssert.That(MfdLogTone.Classify("FGA-57 Anvil demolished Vehicle Depot") == MfdLogTone.Kind.Danger,
                "destruction reads as danger");
            TestAssert.That(MfdLogTone.Classify("Airbase Alpha captured") == MfdLogTone.Kind.Ready,
                "capture reads as ready");
            TestAssert.That(MfdLogTone.Classify("LiveFire SAM intercepted ASK-48") == MfdLogTone.Kind.Caution,
                "interception reads as caution");
            TestAssert.That(MfdLogTone.Classify("Weather clear over the strait") == MfdLogTone.Kind.Neutral,
                "unrecognised lines stay neutral");
            TestAssert.That(MfdLogTone.Classify(null) == MfdLogTone.Kind.Neutral,
                "missing text cannot be classified");
            TestAssert.That(MfdLogTone.Paint(null) == "" &&
                MfdLogTone.Paint("x").Contains(MfdLogTone.NeutralHex),
                "painting wraps one line in its tone colour");
        }
    }
}
