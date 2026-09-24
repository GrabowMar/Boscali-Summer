using System;
using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Features.Squad.Configuration
{
    internal enum PilotLifeMode { Respawning, OneLife }

    internal sealed class SquadSettings
    {
        public ConfigEntry<bool> EnemyAceHunts { get; }
        public ConfigEntry<PilotLifeMode> PilotLives { get; }
        public ConfigEntry<float> DamageThreshold { get; }
        public ConfigEntry<float> HuntCooldown { get; }
        public ConfigEntry<int> DebugWingTier { get; }
        internal Func<int, string> SpawnDebugWing;
        internal Func<string> ClearDebugWings;
        private string debugResult = "Host only. Fly an aircraft in a running mission.";

        // ConfigurationManager reads these public fields by name; no plugin assembly dependency.
        // Whether the entry is advanced is not decided here - ConfigMenu sorts the whole
        // config file into the window's two halves in one place.
        private sealed class ConfigurationManagerAttributes
        {
            public Action<ConfigEntryBase> CustomDrawer;
            public bool? HideDefaultButton = true;
        }

        public SquadSettings(ConfigFile config)
        {
            PilotLives = config.Bind("Squad", "PilotLives", PilotLifeMode.Respawning,
                "Pilot career mode (host authoritative). Respawning retains the generated pilot and perks. " +
                "OneLife retires a confirmed dead pilot; the next aircraft receives a new pilot with fresh " +
                "perks and score progress. Ejection alone is not death. Native aircraft respawning remains available.");
            EnemyAceHunts = config.Bind("Squad", "EnemyAceHunts", true,
                "Enemy aces lead escalating wings that hunt a player after hostile damage. Requires Wing Command's Squad API. Host authoritative.");
            DamageThreshold = config.Bind("Squad", "DamageThreshold", 25f,
                new ConfigDescription("Initial hostile part damage needed for an ace hunt, after native armor. " +
                    "Threshold increases 35% per defeated ace (capped at five times this value). " +
                    "First 60 mission seconds and active/cooling hunts do not accumulate threat. Host authoritative.",
                    new AcceptableValueRange<float>(5f, 500f)));
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
