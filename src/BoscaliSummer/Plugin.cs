using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Fire;
using BoscaliSummer.Garrisons;
using BoscaliSummer.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.marci.boscalisummer";
        public const string PluginName = "Boscali Summer";
        public const string PluginVersion = "0.1.1";

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static ModConfiguration Settings { get; private set; }

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            Settings = new ModConfiguration(Config);

            GameAccess.Initialise();
            harmony = new Harmony(PluginGuid);
            try
            {
                harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception e)
            {
                Logger.LogError("Harmony patch installation was incomplete: " + e);
            }

            var root = new GameObject("BoscaliSummer.Runtime");
            root.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(root);
            root.AddComponent<PackManager>();
            root.AddComponent<ImpactFireManager>();
            root.AddComponent<RuinAftermathManager>();
            root.AddComponent<ZoneGarrisonManager>();
            root.AddComponent<ModNet>();

            ReportPatches();
            CapabilityReport.Log();
            Logger.LogInfo($"Effective fire tuning: bullet ignition={Settings.BulletIgnitionChance:0.####}, " +
                $"explosive ignition={Settings.ExplosiveIgnitionChance:0.####}, intensity={Settings.FireIntensity.Value:0.##}, " +
                $"active-site cap={Settings.MaxActiveFires}.");
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. All world changes remain host authoritative.");
        }

        private void ReportPatches()
        {
            var names = new List<string>();
            foreach (MethodBase method in harmony.GetPatchedMethods())
                names.Add(method.DeclaringType?.Name + "." + method.Name);
            names.Sort(StringComparer.Ordinal);
            Logger.LogInfo($"Harmony patched {names.Count} method(s): {string.Join(", ", names.ToArray())}");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Instance = null;
        }
    }
}
