using BoscaliSummer.Bootstrap;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.marci.boscalisummer";
        public const string PluginName = "Boscali Summer";
        public const string PluginVersion = "0.1.1";

        internal static new ManualLogSource Logger { get; private set; }
        internal static ModConfiguration Settings { get; private set; }

        private FeatureHost featureHost;

        private void Awake()
        {
            Logger = base.Logger;
            Settings = new ModConfiguration(Config);

            featureHost = ModCompositionRoot.Start(Logger, Settings);
            Logger.LogInfo($"Effective fire tuning: bullet ignition={Settings.BulletIgnitionChance:0.####}, " +
                $"explosive ignition={Settings.ExplosiveIgnitionChance:0.####}, intensity={Settings.FireIntensity.Value:0.##}, " +
                $"active-site cap={Settings.MaxActiveFires}.");
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. All world changes remain host authoritative.");
        }

        private void OnDestroy()
        {
            featureHost?.Dispose();
            featureHost = null;
        }
    }
}
