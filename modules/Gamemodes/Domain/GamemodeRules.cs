using System;

namespace BoscaliSummer.Modules.Gamemodes.Domain
{
    internal enum GameModeKind { None, WarBox, Fuzzball, Heavyweight }

    internal static class GamemodeRules
    {
        public const int FuzzballSeconds = 20 * 60;
        public const int FuzzballKillTarget = 75;
        public const int HeavyweightLegSeconds = 20 * 60;
        public const int HeavyweightIntermissionSeconds = 60;
        public const int WarBoxEscalationScale = 4;

        public static string Marker(GameModeKind mode) => mode switch
        {
            GameModeKind.WarBox => "[BoscaliGamemode:warbox]",
            GameModeKind.Fuzzball => "[BoscaliGamemode:fuzzball]",
            GameModeKind.Heavyweight => "[BoscaliGamemode:heavyweight]",
            _ => ""
        };

        /// <summary>Use an exact description line preserved by native mission sync, never a matching title.</summary>
        public static GameModeKind DetectMode(string description)
        {
            GameModeKind result = GameModeKind.None;
            foreach (string line in (description ?? "").Split('\n'))
            {
                string marker = line.Trim();
                for (GameModeKind mode = GameModeKind.WarBox; mode <= GameModeKind.Heavyweight; mode++)
                {
                    if (!string.Equals(marker, Marker(mode), StringComparison.Ordinal)) continue;
                    if (result != GameModeKind.None && result != mode) return GameModeKind.None;
                    result = mode;
                }
            }
            return result;
        }
    }
}