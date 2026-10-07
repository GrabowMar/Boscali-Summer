namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Server counters the nomodkit sim reads through VanguardAutomation. Cleared per scene.</summary>
    internal static class VanguardStats
    {
        public static int WaterEntries;
        public static int ShipHits;
        public static int Seductions;
        public static int UgvsSpawned;

        public static void Clear() => WaterEntries = ShipHits = Seductions = UgvsSpawned = 0;
    }
}
