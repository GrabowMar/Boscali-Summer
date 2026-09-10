namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal enum MfdTargetPreset { All, Hostile, Air, Ground, Sea, Sead, Friendly, Laser }

    internal static class MfdTargetPresets
    {
        public static readonly string[] Names =
            { "ALL", "HOSTILE", "AIR", "GROUND", "SEA", "SEAD", "FRIENDLY", "LASER" };
        public static readonly string[] Descriptions =
        {
            "All factions and unit classes; laser filter off.",
            "Enemy units across all classes.",
            "Enemy aircraft; missiles excluded.",
            "Enemy ground vehicles and buildings.",
            "Enemy ships only.",
            "Enemy AAA, IR SAM, radar SAM and radar vehicles.",
            "Friendly units across all classes.",
            "All classes and factions, restricted to lased targets.",
        };

        public static bool Faction(MfdTargetPreset preset, bool friendly) =>
            preset == MfdTargetPreset.All || preset == MfdTargetPreset.Laser ||
            (preset == MfdTargetPreset.Friendly ? friendly : !friendly);

        public static bool UnitClass(MfdTargetPreset preset, string type)
        {
            switch (preset)
            {
                case MfdTargetPreset.Air: return type == "AircraftDefinition";
                case MfdTargetPreset.Ground: return type == "VehicleDefinition" || type == "BuildingDefinition";
                case MfdTargetPreset.Sea: return type == "ShipDefinition";
                case MfdTargetPreset.Sead: return type == "VehicleDefinition";
                default: return true;
            }
        }

        public static bool Vehicle(MfdTargetPreset preset, string type) =>
            preset != MfdTargetPreset.Sead || type == "AAA" || type == "IR_SAM" ||
            type == "R_SAM" || type == "RDR";
    }
}
