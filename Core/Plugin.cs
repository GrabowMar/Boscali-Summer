using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Core;
using BoscaliSummer.Core.Config;
using NOAvionics;

namespace BoscaliSummer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Avionics is compiled in; consumers already handle an unavailable optional Wing service.
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.marci.boscalisummer";
        public const string PluginName = "Boscali Summer";
        public const string PluginVersion = "0.1.1";

        internal static new ManualLogSource Logger { get; private set; }
        internal static ModConfiguration Settings { get; private set; }

        private BoscaliMod mod;

        private void Awake()
        {
            Logger = base.Logger;
            // The built-in wing replaces the stand-alone Wing Command plugin; stop it before it loads.
            BoscaliSummer.Core.Game.ExternalWingGuard.Install(Logger);
            Settings = new ModConfiguration(Config);

            // The panels' look lives in stylesheets, not in literals. The embedded sheets are
            // always valid; pointing the host at the config directory is what lets a player
            // drop their own palette sheets under NOAvionics/ and retune every panel
            // without a rebuild. This is the one Configure+Load call; Command's bridge applies the Avionics keys on top.
            AvStyleHost.Configure(Paths.ConfigPath, Logger.LogInfo, Logger.LogWarning);
            AvBundle.Load(Logger.LogInfo);

            mod = BoscaliMod.Start(Logger, Settings);
            Logger.LogInfo($"Effective fire tuning: bullet ignition={Settings.FireAndDestruction.BulletIgnitionChance:0.####}, " +
                $"explosive ignition={Settings.FireAndDestruction.ExplosiveIgnitionChance:0.####}, intensity={Settings.FireAndDestruction.FireIntensity.Value:0.##}, " +
                $"active-site cap={Settings.FireAndDestruction.MaxActiveFires}.");
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. All world changes remain host authoritative.");
        }

        private void OnDestroy()
        {
            mod?.Dispose();
            mod = null;
        }
    }
}
