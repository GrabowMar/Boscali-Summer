using System;
using BepInEx.Configuration;
using BoscaliSummer.Core.Config;
using UnityEngine;

namespace BoscaliSummer.Modules.Squad.Configuration
{
    internal enum PilotLifeMode { Respawning, OneLife }

    internal sealed class SquadSettings
    {
        public ConfigEntry<bool> EnemyAceHunts { get; }
        public ConfigEntry<PilotLifeMode> PilotLives { get; }
        public ConfigEntry<float> HeatThreshold { get; }
        public ConfigEntry<float> HuntCooldown { get; }
        public ConfigEntry<int> DebugWingTier { get; }
        internal Func<int, string> SpawnDebugWing;
        internal Func<string> ClearDebugWings;
        private string debugResult = "Host only. Fly an aircraft in a running mission.";

        public SquadSettings(ConfigFile config)
        {
            PilotLives = config.Bind("Squad", "PilotLives", PilotLifeMode.Respawning,
                "Pilot career mode (host authoritative). Respawning retains the generated pilot and perks. " +
                "OneLife retires a confirmed dead pilot; the next aircraft receives a new pilot with fresh " +
                "perks and score progress. Ejection alone is not death. Native aircraft respawning remains available.");
            EnemyAceHunts = config.Bind("Squad", "EnemyAceHunts", true,
                "Enemy aces lead escalating wings that hunt a player once enemy heat is full. Requires Wing Command's Squad API. Host authoritative.");
            // New key (was DamageThreshold, 25 part damage): heat is now kill score, so the old saved value must not carry over.
            HeatThreshold = config.Bind("Squad", "HeatThreshold", 24f,
                new ConfigDescription("Enemy HEAT needed before an ace wing is sent after a player. Each kill adds its vanilla kill score " +
                    "(square root of the unit's value, split by damage share); one kill fills at most 40% of the bar. " +
                    "Heat cools after a quiet minute. Threshold grows 35% per defeated ace (up to five times). " +
                    "No heat builds in the first 3 mission minutes or during a hunt and its cooldown. Host authoritative.",
                    new AcceptableValueRange<float>(4f, 200f)));
            HuntCooldown = config.Bind("Squad", "HuntCooldown", 180f,
                new ConfigDescription("Quiet period after a hunt ends. Fresh hostile damage is required afterward. Host authoritative.",
                    new AcceptableValueRange<float>(60f, 600f)));
            DebugWingTier = config.Bind("Debug", "HuntingWingTier", 1,
                new ConfigDescription("Tier for the Spawn hunting wing action: 1-2 have two aircraft, " +
                    "3-4 have three, 5 has four. Higher tiers use stronger native AI. Host only.",
                    new AcceptableValueRange<int>(1, 5)));
            config.Bind("Debug", "SpawnHuntingWing", false,
                new ConfigDescription("Spawn the selected ace tier against your current aircraft, bypassing " +
                    "damage, grace and cooldown. Replaces your previous debug wings, never a normal hunt. " +
                    "Uses normal hunt rewards, chatter and music. Clear removes only your debug wings without kill rewards.",
                    // Whether the entry is advanced is not decided here: ConfigMenu sorts the whole config file in one place.
                    null, new ConfigurationManagerAttributes { CustomDrawer = DrawDebugActions }));
        }

        private void DrawDebugActions(ConfigEntryBase entry)
        {
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && SpawnDebugWing != null;
                if (GUILayout.Button("Spawn tier " + DebugWingTier.Value + " hunting wing"))
                    debugResult = SpawnDebugWing(DebugWingTier.Value);
                GUI.enabled = enabled && ClearDebugWings != null;
                if (GUILayout.Button("Clear my debug wings")) debugResult = ClearDebugWings();
            }
            finally { GUI.enabled = enabled; }
            GUILayout.Label(SpawnDebugWing == null ? "Enable Progression and restart to install Squad." : debugResult);
        }
    }
}
