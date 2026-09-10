using BoscaliSummer.Bootstrap;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Framework.Features;
using NOAvionics.Ui;

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

            // The panels' look lives in a stylesheet, not in literals. The embedded copy is
            // always valid; pointing the host at the config directory is what lets a player
            // drop their own avionics.avss beside it and retune every panel in both mods
            // without a rebuild. Wing Command configures the same path on purpose.
            AvStyleHost.Configure(Paths.ConfigPath, Logger.LogInfo, Logger.LogWarning);

            featureHost = ModCompositionRoot.Start(Logger, Settings);
            TheaterInteropPush.PublishGuid();
            Logger.LogInfo($"Effective fire tuning: bullet ignition={Settings.FireAndDestruction.BulletIgnitionChance:0.####}, " +
                $"explosive ignition={Settings.FireAndDestruction.ExplosiveIgnitionChance:0.####}, intensity={Settings.FireAndDestruction.FireIntensity.Value:0.##}, " +
                $"active-site cap={Settings.FireAndDestruction.MaxActiveFires}.");
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. All world changes remain host authoritative.");
        }

        private void OnDestroy()
        {
            featureHost?.Dispose();
            featureHost = null;
        }
    }
}
