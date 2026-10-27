using System;
using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Modules.Kaiju.Configuration
{
    internal sealed class KaijuSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> Toughness;
        public readonly ConfigEntry<float> SummonDistanceKm;
        public readonly ConfigEntry<float> WeakSpotScale;
        public Func<string> Summon, Dismiss;
        /// <summary>Raised on the host when the kaiju is killed (not on a Dismiss); clients see KaijuAction.Death instead.</summary>
        public Action Defeated;
        private string result = "";

        public KaijuSettings(ConfigFile config)
        {
            Enabled = config.Bind("Kaiju", "Enabled", true,
                "Let the host summon a mountain-sized kaiju that every aircraft has to fight.");
            Toughness = config.Bind("Kaiju", "HardpointToughness", 2.5f, new ConfigDescription(
                "How much punishment each glowing weak spot takes, as a multiple of the small building its hitbox is cloned " +
                "from. At 2.5 a weak spot takes 2-3 AGM-1 hits and the heart, which takes three times this, about 8. Host only.",
                new AcceptableValueRange<float>(0.5f, 20f)));
            WeakSpotScale = config.Bind("Kaiju", "WeakSpotScale", 0.6f, new ConfigDescription(
                "Size of the glowing weak spots (blister and hitbox together) as a multiple of the original art. Read when " +
                "the kaiju spawns.", new AcceptableValueRange<float>(0.3f, 1.5f)));
            SummonDistanceKm = config.Bind("Kaiju", "SummonDistanceKm", 12f, new ConfigDescription(
                "How far ahead of your aircraft the kaiju rises.", new AcceptableValueRange<float>(4f, 40f)));
            config.Bind("Kaiju", "Summon", false, new ConfigDescription(
                "Summon the kaiju in front of you, or send it away. Host only.", null,
                new Core.Config.ConfigurationManagerAttributes { CustomDrawer = Draw, HideDefaultButton = true }));
        }

        private void Draw(ConfigEntryBase entry)
        {
            bool enabled = GUI.enabled;
            try
            {
                GUI.enabled = enabled && Summon != null;
                if (GUILayout.Button("SUMMON KAIJU")) result = Summon();
                GUI.enabled = enabled && Dismiss != null;
                if (GUILayout.Button("Dismiss")) result = Dismiss();
            }
            finally { GUI.enabled = enabled; }
            GUILayout.Label(Summon == null ? "Enable Kaiju and restart the game." : result);
        }
    }
}
