using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Other client mods that share the game, and often Boscali's own code, with us (2026-11-06): NoAtmoFX
    /// (volumetric clouds) and the plugins of Kelly's Client Effects that still load beside Boscali (its core,
    /// weather and cockpit plugins step aside on their own). The Compatibility module sets the yields each tick;
    /// the module that owns an effect reads its flag so one effect is never drawn twice. All false when nothing
    /// else is installed or the Compatibility links are off.
    /// </summary>
    internal static class ForeignMods
    {
        public const string NoAtmoFx = "paul.no.clouds";
        public const string KellyKrop = "aaron.boscali.krop-unified";
        public const string KellyAirfield = "aaron.boscali.airfield-lighting";
        public const string KellyMissileBoom = "aaron.boscali.missile-unified";
        public const string KellyControls = "aaron.boscali.control-unified";

        /// <summary>Another mod draws the clouds: our volumetric clouds, sun shafts and lightning stand down.</summary>
        public static bool SkyYielded;
        /// <summary>Another mod draws rain and canopy rain.</summary>
        public static bool RainYielded;
        /// <summary>Another mod frosts the canopy above the freezing level.</summary>
        public static bool FrostYielded;
        /// <summary>Contrails, dust wake and near-miss crack come from Kelly's KROP plugin.</summary>
        public static bool WorldFxYielded;
        /// <summary>Kelly's KROP plugin muffles every world sound in the cockpit, a superset of our canopy muffle.</summary>
        public static bool MuffleYielded;
        public static bool AirfieldYielded;
        public static bool MissileBoomYielded;

        public static void ClearYields() =>
            SkyYielded = RainYielded = FrostYielded = WorldFxYielded = MuffleYielded = AirfieldYielded = MissileBoomYielded = false;

        /// <summary>The loaded plugin with this GUID, or null. Plugins after ours in load order appear only once the chainloader reaches them.</summary>
        public static BaseUnityPlugin Find(string guid) =>
            Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) ? info.Instance : null;

        /// <summary>
        /// A key press with no Ctrl or Alt held. Our keys are plain keys; a chord another mod owns (Kelly's Ctrl+F8
        /// panel, NoAtmoFX keys moved to Ctrl/Alt chords) must not also fire ours.
        /// </summary>
        public static bool KeyDown(KeyCode key) =>
            key != KeyCode.None && Input.GetKeyDown(key) &&
            !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) &&
            !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt);
    }
}
