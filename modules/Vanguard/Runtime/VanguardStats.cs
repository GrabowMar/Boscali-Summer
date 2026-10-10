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
        private static readonly Dictionary<string, string> Mirrors = new Dictionary<string, string>();

        public static void Bump(string name, int by = 1) => Set(name, Get(name) + by);
        /// <summary>Sets a counter or gauge and its '_' mirror ("a.b" and "a_b"): sim check paths split on '.', so the status dump needs both.</summary>
        public static void Set(string name, int value)
        {
            if (!Mirrors.TryGetValue(name, out string flat)) Mirrors[name] = flat = name.Replace('.', '_');
            Counters[name] = Counters[flat] = value;
        }
        public static int Get(string name) => Counters.TryGetValue(name, out int v) ? v : 0;

        public static void Clear()
        {
            WaterEntries = ShipHits = Seductions = UgvsSpawned = TurretsDeployed = TurretShots = Refuels = Rearms = 0;
            Counters.Clear();
        }
    }
}
