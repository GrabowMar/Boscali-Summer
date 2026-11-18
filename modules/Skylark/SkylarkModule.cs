using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Modules;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    internal sealed class SkylarkModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("skylark", "VS-4 Skylark");
        public Type[] PatchTypes => new[] { typeof(SkylarkEncyclopediaPatch), typeof(SkylarkPrefabPatch), typeof(SkylarkLiveryPatch), typeof(SkylarkAeroInitializePatch), typeof(SkylarkNozzleGaugePatch), typeof(SkylarkHangarAvailabilityPatch), typeof(SkylarkSilentRunningPatch), typeof(SkylarkBatteryFuelPatch), typeof(SkylarkGearSelfContactPatch), typeof(SkylarkCoilsDryPatch) };
        public void Install(ModuleContext context)
        {
            context.AddSceneService<SkylarkService>(79);
            context.AddService<ISkylarkControl>(context.AddSceneService<SkylarkControl>(80));
            context.AddSceneService<SkylarkNet>(81);
            ScopeGauges.Register("drive", SkylarkDrive.ReadGauge);
            // A Skylark is a command platform while, and only while, its C2 mast is out (the mast costs radar signature).
            CommandPlatform.Provider = a => a.definition != null && a.definition.jsonKey == SkylarkTemplate.Key && !a.disabled
                && a.TryGetComponent(out SkylarkMast mast) && mast.Deployed;
        }
    }

    internal sealed class SkylarkService : MonoBehaviour, ISceneService
    {
        private float nextCheck;
        public void ResetForScene() => nextCheck = 0f;
        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 2f;
            SkylarkTemplate.Ensure(GameAccess.EncyclopediaOrNull);
        }
    }

    // Rebuilding the native catalogue must retain every vanilla network index. Append our definition only afterwards.
    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new Type[] { })]
    internal static class SkylarkEncyclopediaPatch
    {
        private static void Prefix(Encyclopedia __instance)
        {
            __instance.aircraft.RemoveAll(d => d != null && d.jsonKey == SkylarkTemplate.Key);
            __instance.weaponMounts.RemoveAll(m => m != null && m.jsonKey == SkylarkWeapons.TurretKey);
        }
        private static void Postfix(Encyclopedia __instance) => SkylarkTemplate.Ensure(__instance);
    }

    [HarmonyPatch(typeof(NetworkManagerNuclearOption), "RegisterPrefabs")]
    internal static class SkylarkPrefabPatch
    {
        private static void Prefix() => SkylarkTemplate.Ensure(GameAccess.EncyclopediaOrNull);
    }
}
