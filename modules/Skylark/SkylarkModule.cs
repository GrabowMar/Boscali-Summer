using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    internal sealed class SkylarkModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("skylark", "VS-4 Skylark");
        public Type[] PatchTypes => new[] { typeof(SkylarkEncyclopediaPatch), typeof(SkylarkBlueprinterPatch), typeof(SkylarkLiveryPatch), typeof(SkylarkAeroInitializePatch), typeof(SkylarkNozzleGaugePatch), typeof(SkylarkHangarAvailabilityPatch), typeof(SkylarkSilentRunningPatch), typeof(SkylarkBatteryFuelPatch), typeof(SkylarkGearSelfContactPatch), typeof(SkylarkCoilsDryPatch) };
        public void Install(ModuleContext context)
        {
            context.AddService<ISkylarkControl>(context.AddSceneService<SkylarkControl>(80));
            context.AddSceneService<SkylarkNet>(81);
            ScopeGauges.Register("drive", SkylarkDrive.ReadGauge);
            // A Skylark is a command platform while, and only while, its C2 mast is out (the mast costs radar signature).
            CommandPlatform.Provider = a => a.definition != null && a.definition.jsonKey == SkylarkTemplate.Key && !a.disabled
                && a.TryGetComponent(out SkylarkMast mast) && mast.Deployed;
        }
    }

    // Register against the exact pack Blueprinter accepted, on the same call that establishes its network indices.
    [HarmonyPatch]
    internal static class SkylarkBlueprinterPatch
    {
        private static readonly Type Entry = AccessTools.TypeByName("Blueprinter.LoadedBundle");
        private static readonly FieldInfo Asset = Entry?.GetField("AssetBundle");
        private static MethodBase TargetMethod() => Entry?.Assembly.GetType("Blueprinter.Ops.EncyclopediaLoader")?.GetMethod("Load",
            new[] { typeof(Encyclopedia), typeof(IReadOnlyList<>).MakeGenericType(Entry) });
        private static bool Prepare() => Asset?.FieldType == typeof(AssetBundle) && TargetMethod() != null;

        private static void Postfix(Encyclopedia __0, IEnumerable __1)
        {
            foreach (object entry in __1)
                if (Asset.GetValue(entry) is AssetBundle pack && pack != null && pack.name == "boscalisummer")
                {
                    SkylarkTemplate.Register(__0, pack);
                    return;
                }
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
        private static void Postfix(Encyclopedia __instance)
        {
            if (SkylarkTemplate.Definition != null) SkylarkTemplate.Register(__instance);
        }
    }
}
