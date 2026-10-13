using System;

namespace BoscaliSummer.Core.Game
{
    /// <summary>Only the dedicated Escalation copy opts into the mission overhaul.</summary>
    internal static class MissionProfile
    {
        public const string DescriptionMarker = "Full Boscali Summer experience enabled for this dedicated Escalation scenario.";
        public static bool FullExperience { get; private set; }
        public static event Action<bool> Changed;
        public static event Action Preparing;

        public static bool IsFullMission(string description) =>
            description != null && description.EndsWith("\n\n" + DescriptionMarker, StringComparison.Ordinal);

        public static bool IsBoscaliContent(string key) =>
            key != null && (key.StartsWith("VG_", StringComparison.Ordinal) ||
                key == "AB_Paratroopers_x16" || key == "BS_ContainerFeeder");

        public static void SetDescription(string description)
        {
            bool full = IsFullMission(description);
            if (full == FullExperience) return;
            FullExperience = full;
            Changed?.Invoke(full);
        }

        public static void PrepareMission(string description)
        {
            SetDescription(description);
            Preparing?.Invoke();
        }

        public static bool AllowsModule(string id, bool fullExperience)
        {
            switch (id)
            {
                case "mission-profile":
                case "session":
                case "hud":
                case "radio":
                case "performance":
                case "immersion":
                case "effects":
                case "qol":
                case "autopilot":
                case "chatter":
                case "wing":
                case "command":
                case "comms":
                case "cinematography":
                    return true;
                case "fire-and-destruction":
                case "urban-combat":
                case "player-spawn-priority":
                case "squad":
                case "progression":
                case "support":
                case "dynamic-operations":
                case "high-command":
                case "intel":
                case "theater-ops":
                case "air-survival":
                case "trenches":
                case "events":
                case "weather":
                case "vanguard":
                case "strategic-command":
                    return fullExperience;
                default:
                    return false;
            }
        }
    }
}
