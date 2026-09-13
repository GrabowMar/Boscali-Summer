using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class MfdPanelTests
    {
        public static void Run()
        {
            FactionResourceTests.Run();
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
        }
    }
}
