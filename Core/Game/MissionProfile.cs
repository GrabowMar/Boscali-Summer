using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Which optional modules the player switched off for the mission being played. Nothing is
    /// off unless the player said so, at the mission-start prompt or in a remembered choice.
    /// </summary>
    internal static class MissionProfile
    {
        /// <summary>Modules that change the mission itself; the "immersion only" preset switches these off.</summary>
        public static readonly string[] BattlefieldModules =
        {
            "fire-and-destruction", "urban-combat", "player-spawn-priority", "squad", "progression", "support",
            "dynamic-operations", "high-command", "intel", "theater-ops", "air-survival", "trenches", "events",
            "weather", "vanguard", "strategic-command"
        };

        private static HashSet<string> disabled = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The selection changed; the module host loads and unloads to match.</summary>
        public static event Action Changed;
        public static event Action Preparing;

        /// <summary>The plumbing every other module leans on; never offered as a switch.</summary>
        public static bool IsCore(string id) =>
            id == "mission-profile" || id == "scenario" || id == "session" || id == "hud" || id == "performance";

        public static bool IsBattlefield(string id) => Array.IndexOf(BattlefieldModules, id) >= 0;

        public static bool AllowsModule(string id) => id != null && (IsCore(id) || !disabled.Contains(id));

        /// <summary>Free Flight keeps the terrain and civilian buildings clear of mod fortifications.</summary>
        public static bool AllowsFortifications(string missionName) =>
            !string.IsNullOrWhiteSpace(missionName) &&
            !string.Equals(missionName.Trim(), "Free Flight", StringComparison.OrdinalIgnoreCase);

        /// <summary>The module whose switch governs an embedded Boscali asset, or null for native and workshop content.</summary>
        public static string ContentOwner(string key)
        {
            if (key == null) return null;
            if (key == "AB_Paratroopers_x16") return "urban-combat";
            return key.StartsWith("VG_", StringComparison.Ordinal) || key == "BS_ContainerFeeder" ? "vanguard" : null;
        }

        public static bool BlocksContent(string key)
        {
            string owner = ContentOwner(key);
            return owner != null && !AllowsModule(owner);
        }

        public static string[] Parse(string list) =>
            (list ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        public static string Format(IEnumerable<string> ids)
        {
            var sorted = new List<string>(ids);
            sorted.Sort(StringComparer.Ordinal);
            return string.Join(",", sorted);
        }

        public static void SetDisabled(IEnumerable<string> ids)
        {
            var next = new HashSet<string>(ids ?? Array.Empty<string>(), StringComparer.Ordinal);
            next.RemoveWhere(IsCore);
            if (next.SetEquals(disabled)) return;
            disabled = next;
            Changed?.Invoke();
        }

        public static void PrepareMission() => Preparing?.Invoke();
    }
}
