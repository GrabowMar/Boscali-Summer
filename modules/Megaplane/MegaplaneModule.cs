using System;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Modules;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SceneLoading;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Registers the KY-44 Groza and its aircraft systems.</summary>
    internal sealed class MegaplaneModule : IModule
    {
        public ModuleMetadata Metadata => new ModuleMetadata("megaplane", "KY-44 Groza");
        public Type[] PatchTypes => new[] { typeof(MegaplaneBlueprinterPatch), typeof(MegaplaneEncyclopediaPatch), typeof(MegaplaneAeroInitializePatch), typeof(MegaplanePhysicsLodPatch), typeof(MegaplaneFilterPatch), typeof(MegaplaneTurretPatch), typeof(MegaplaneLoadoutPatch), typeof(MegaplaneSavedLoadoutPatch), typeof(MegaplaneCargoStationPatch), typeof(MegaplaneHangarAvailabilityPatch), typeof(MegaplaneHangarAirStartPatch), typeof(MegaplaneBatteryFirePatch), typeof(MegaplaneBatteryAimPatch), typeof(MegaplaneSiloSelectPatch), typeof(MegaplaneSiloFirePatch), typeof(MegaplaneSelfHitPatch), typeof(MegaplaneStatusInitializePatch), typeof(MegaplaneCrashCleanupPatch), typeof(MegaplaneCrashLifecyclePatch) };
        public void Install(ModuleContext context)
        {
            MegaplaneSettings.Current = context.Settings.Megaplane;
            MapLoadPatch.WrapResult = MegaplaneClientMapReady.WaitIfNeeded;
            context.OnTeardown(() => MapLoadPatch.WrapResult = null);
            context.AddService<ICarrierBayControl>(new MegaplaneBayControls());
            if (!GameManager.IsHeadless) context.AddSceneService<MegaplaneOperationsHud>(57);
            MegaplaneNet.Ensure();   // the bay/railgun art replication: up before any scene can hold a carrier
        }

        internal static bool Is(Unit unit) => unit != null && unit.definition != null && unit.definition.jsonKey == MegaplaneTemplate.Key;
    }

    // Network indices must match on every peer. Blueprinter appends its packs from a coroutine after AfterLoad, so register on
    // its EncyclopediaLoader.Load (after the packs, as the Skylark does); without Blueprinter, AfterLoad is the only append.
    [HarmonyPatch]
    internal static class MegaplaneBlueprinterPatch
    {
        private static readonly Type Entry = AccessTools.TypeByName("Blueprinter.LoadedBundle");
        internal static System.Reflection.MethodBase TargetMethod() => Entry?.Assembly.GetType("Blueprinter.Ops.EncyclopediaLoader")?.GetMethod("Load",
            new[] { typeof(Encyclopedia), typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(Entry) });
        internal static bool Available => TargetMethod() != null;
        private static bool Prepare() => Available;
        private static void Postfix(Encyclopedia __0) => MegaplaneTemplate.Register(__0);
    }

    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new Type[] { })]
    internal static class MegaplaneEncyclopediaPatch
    {
        private static void Prefix(Encyclopedia __instance) => __instance.aircraft.RemoveAll(d => d != null && d.jsonKey == MegaplaneTemplate.Key);
        // After SeaStructures' SeaEncyclopediaPatch and Skylark's SkylarkEncyclopediaPatch: each appends to IndexLookup, whose order is the wire index.
        [HarmonyAfter("com.marci.boscalisummer.module.sea-content", "com.marci.boscalisummer.module.skylark")]
        private static void Postfix(Encyclopedia __instance)
        {
            if (MegaplaneTemplate.Definition != null || !MegaplaneBlueprinterPatch.Available) MegaplaneTemplate.Register(__instance);
        }
    }

    // The server immediately sends aircraft and their encyclopedia indices when a joining client finishes its map load.
    // On a cold start Blueprinter can still be reading the bundles, leading Mirage to permanently discard the spawn.
    internal static class MegaplaneClientMapReady
    {
        private static bool Registered => Encyclopedia.Lookup != null && Encyclopedia.Lookup.ContainsKey(MegaplaneTemplate.Key);

        internal static UniTask<MapLoader.LoadResult> WaitIfNeeded(UniTask<MapLoader.LoadResult> loading)
        {
            NetworkManagerNuclearOption manager = GameAccess.NetworkManagerOrNull;
            if (manager?.Client?.Active == true && manager.Server?.Active != true && MegaplaneBlueprinterPatch.Available && !Registered)
                return WaitForCatalogue(loading, manager);
            return loading;
        }

        private static async UniTask<MapLoader.LoadResult> WaitForCatalogue(UniTask<MapLoader.LoadResult> loading, NetworkManagerNuclearOption manager)
        {
            MapLoader.LoadResult result = await loading;
            if (result != MapLoader.LoadResult.ChangedScene && result != MapLoader.LoadResult.ChangedWorldPrefab && result != MapLoader.LoadResult.AlreadyLoaded)
                return result;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (manager != null && manager.Client.Active && !Registered && Time.realtimeSinceStartup < deadline)
                await UniTask.Yield();
            if (manager != null && manager.Client.Active && !Registered)
                Debug.LogWarning("[Megaplane] Groza catalogue did not register before the map-load deadline. Continuing the native connection.");
            return result;
        }
    }

    // Donor colliders are disabled, so UnitPart cached empty bounds; native AeroJob divides by collisionSize.y. Supply real extents.
    [HarmonyPatch(typeof(AeroPart), "AeroPart_OnInitialize")]
    internal static class MegaplaneAeroInitializePatch
    {
        private static void Prefix(AeroPart __instance)
        {
            if (!MegaplaneModule.Is(__instance.parentUnit)) return;
            var bounds = new Bounds(__instance.transform.position, Vector3.one * .02f);
            foreach (MegaplaneHitbox h in __instance.parentUnit.GetComponentsInChildren<MegaplaneHitbox>(true))
                if (h.Owner == __instance) bounds.Encapsulate(h.GetComponent<Collider>().bounds);
            AccessTools.Field(typeof(UnitPart), "collisionSize").SetValue(__instance, Vector3.Max(bounds.extents, Vector3.one * .01f));
            AccessTools.Field(typeof(AeroPart), "centerOfLift").SetValue(__instance, Vector3.zero);
        }
    }

    // One rigidbody, always: 35 jointed islands on the carrier frame would only tear and jitter (BOTE replaces this method too).
    // The prefab is still one body when SetLocalSim asks for complex physics, so marking it simple is enough.
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SetComplexPhysics))]
    internal static class MegaplanePhysicsLodPatch
    {
        private static bool Prefix(Aircraft __instance)
        {
            if (!MegaplaneModule.Is(__instance)) return !MegaplaneCapture.FlyingCapture(__instance);
            Traverse.Create(__instance).Field<bool>("simplePhysics").Value = true;
            Rigidbody rb = __instance.GetComponent<Rigidbody>();
            rb.mass = __instance.definition.mass;
            rb.ResetCenterOfMass();
            rb.ResetInertiaTensor();
            return false;
        }
    }

    // No fly-by-wire: MegaplaneFlight reads the raw stick as turn/climb/speed commands, and the donor FBW would add its own.
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.FilterInputs))]
    internal static class MegaplaneFilterPatch
    {
        private static bool Prefix(Aircraft __instance) => !MegaplaneModule.Is(__instance);
    }
}
