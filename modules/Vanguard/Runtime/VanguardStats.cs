using System.Collections.Generic;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Server counters the nomodkit sim reads through VanguardAutomation. Cleared per scene.</summary>
    internal static class VanguardStats
    {
        public static int WaterEntries;
        public static int ShipHits;
        public static int Seductions;
        public static int UgvsSpawned;
        public static int TurretsDeployed;
        public static int TurretShots;
        public static int Refuels;
        public static int Rearms;

        /// <summary>Named per-loadout counters ("<key>.<event>"); the sim status action dumps every entry.</summary>
        public static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();

        public static void Bump(string name, int by = 1) => Counters[name] = (Counters.TryGetValue(name, out int v) ? v : 0) + by;
        public static int Get(string name) => Counters.TryGetValue(name, out int v) ? v : 0;

        public static void Clear()
        {
            WaterEntries = ShipHits = Seductions = UgvsSpawned = TurretsDeployed = TurretShots = Refuels = Rearms = 0;
            Counters.Clear();
        }
    }
}
