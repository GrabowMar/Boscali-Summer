using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Scenario;
using BoscaliSummer.Modules.Pmc.Runtime;
using HarmonyLib;
using Mirage;
using NuclearOption.SavedMission;

namespace BoscaliSummer.Modules.Pmc.Patches
{
    /// <summary>What the game needs to treat a third HQ as a side. Each patch is a no-op for BDF and PALA.</summary>
    internal static class PmcPatches
    {
        internal static readonly Type[] All =
        {
            typeof(SpawnSceneObjectsPatch), typeof(SetMissionPatch), typeof(StartMissionPatch), typeof(EditorSpawnPatch),
            typeof(FactionFromNamePatch), typeof(HqFromNamePatch), typeof(HqFromFactionPatch), typeof(RegisterFactionPatch),
            typeof(HqUpdatePatch), typeof(SetupAirbaseFactionsPatch), typeof(RegisterUnitPatch), typeof(RemoveUnitPatch),
            typeof(MunitionCostPatch), typeof(GenerateMapPatch), typeof(SavedUnitPatch), typeof(EditorUnitColorPatch)
        };

        private static bool IsPmc(FactionHQ hq) => PmcSide.Faction != null && hq != null && hq.faction == PmcSide.Faction;

        /// <summary>After Mirage spawns the scene objects: the PMC HQ is made then, so it gets a real net id.</summary>
        [HarmonyPatch(typeof(ServerObjectManager), "SpawnSceneObjects")]
        internal static class SpawnSceneObjectsPatch
        {
            [HarmonyPostfix]
            private static void After(ServerObjectManager __instance)
            {
                try
                {
                    PmcSide.Som = __instance;
                    if (PmcSide.Wanted(MissionManager.CurrentMission)) PmcSide.Ensure();
                }
                catch (Exception e) { PatchGuard.Report("PmcSpawnScene", e); }
            }
        }

        /// <summary>A mission that does not name PMC must not inherit it from the last one.</summary>
        [HarmonyPatch(typeof(MissionManager), "SetMission")]
        internal static class SetMissionPatch
        {
            [HarmonyPostfix]
            private static void After()
            {
                try { if (!PmcSide.Wanted(MissionManager.CurrentMission)) PmcSide.Forget(); }
                catch (Exception e) { PatchGuard.Report("PmcSetMission", e); }
            }
        }

        [HarmonyPatch(typeof(MissionManager), "StartMission")]
        internal static class StartMissionPatch
        {
            [HarmonyPrefix]
            private static void Before()
            {
                try { if (PmcSide.Wanted(MissionManager.CurrentMission)) PmcSide.Ensure(); }
                catch (Exception e) { PatchGuard.Report("PmcStartMission", e); }
            }
        }

        /// <summary>The editor offers PMC in every mission: it joins the faction list before the editor reads it.</summary>
        [HarmonyPatch(typeof(Spawner), "SpawnFromMissionInEditor")]
        internal static class EditorSpawnPatch
        {
            [HarmonyPrefix]
            private static void Before()
            {
                try
                {
                    Mission mission = MissionManager.CurrentMission;
                    if (PmcSide.Faction == null || mission == null) return;
                    if (!mission.TryGetFaction(SideNames.Pmc, out _)) mission.factions.Add(new MissionFaction(SideNames.Pmc));
                    PmcSide.Ensure();
                }
                catch (Exception e) { PatchGuard.Report("PmcEditorSpawn", e); }
            }
        }

        [HarmonyPatch(typeof(FactionRegistry), nameof(FactionRegistry.FactionFromName))]
        internal static class FactionFromNamePatch
        {
            [HarmonyPostfix]
            private static void After(string factionName, ref Faction __result)
            {
                if (__result == null && factionName == SideNames.Pmc && PmcSide.Hq != null) __result = PmcSide.Faction;
            }
        }

        /// <summary>PMC is not in HQLookup, so a lookup by name would throw; it answers here.</summary>
        [HarmonyPatch(typeof(FactionRegistry), nameof(FactionRegistry.HqFromName))]
        internal static class HqFromNamePatch
        {
            [HarmonyPrefix]
            private static bool Before(string factionName, ref FactionHQ __result)
            {
                if (PmcSide.Faction == null || factionName != SideNames.Pmc) return true;
                __result = PmcSide.Hq;
                return false;
            }
        }

        [HarmonyPatch(typeof(FactionRegistry), nameof(FactionRegistry.HQFromFaction))]
        internal static class HqFromFactionPatch
        {
            [HarmonyPrefix]
            private static bool Before(object[] __args, ref FactionHQ __result)
            {
                if (PmcSide.Faction == null || !ReferenceEquals(__args[0], PmcSide.Faction)) return true;
                __result = PmcSide.Hq;
                return false;
            }
        }

        /// <summary>The HQ registers itself on server start; it is taken back out so the two-slot join screen never sees it.</summary>
        [HarmonyPatch(typeof(FactionRegistry), nameof(FactionRegistry.RegisterFaction))]
        internal static class RegisterFactionPatch
        {
            [HarmonyPostfix]
            private static void After(Faction faction)
            {
                if (PmcSide.Faction != null && faction == PmcSide.Faction) FactionRegistry.HQLookup.Remove(faction);
            }
        }

        /// <summary>Between being made and its server setup, the PMC HQ has no threat tracker yet; its update waits for it.</summary>
        [HarmonyPatch(typeof(FactionHQ), "Update")]
        internal static class HqUpdatePatch
        {
            private static readonly FieldInfo Tracker = AccessTools.Field(typeof(FactionHQ), "aircraftThreatTracker");

            [HarmonyPrefix]
            private static bool Before(FactionHQ __instance) => !IsPmc(__instance) || Tracker?.GetValue(__instance) != null;
        }

        /// <summary>The PMC HQ must not hand the BDF and PALA airbases around when it starts.</summary>
        [HarmonyPatch(typeof(FactionHQ), "SetupAirbaseFactions")]
        internal static class SetupAirbaseFactionsPatch
        {
            [HarmonyPrefix]
            private static bool Before(FactionHQ __instance) => !IsPmc(__instance);
        }

        [HarmonyPatch(typeof(FactionHQ), "RegisterFactionUnit")]
        internal static class RegisterUnitPatch
        {
            [HarmonyPrefix]
            private static bool Before(FactionHQ __instance, Unit unit)
            {
                if (!IsPmc(__instance) || unit == null) return true;
                PmcSide.UnitIds.Add(unit.GetInstanceID());
                PmcSide.UnitPids.Add(unit.persistentID);
                // As vanilla does for a synced unit: only a friendly or spectating map shows it.
                try { __instance.DisplayFactionUnit(unit.persistentID); }
                catch (Exception e) { PatchGuard.Report("PmcRegisterUnit", e); }
                return false;
            }
        }

        [HarmonyPatch(typeof(FactionHQ), "RemoveFactionUnit")]
        internal static class RemoveUnitPatch
        {
            [HarmonyPrefix]
            private static bool Before(FactionHQ __instance, Unit unit)
            {
                if (!IsPmc(__instance) || unit == null) return true;
                PmcSide.UnitIds.Remove(unit.GetInstanceID());
                PmcSide.UnitPids.Remove(unit.persistentID);
                return false;
            }
        }

        /// <summary>The PMC stats tracker's dictionary is not set up; costing a PMC round would throw in Gun.SpawnBullet.</summary>
        [HarmonyPatch(typeof(MissionStatsTracker), "MunitionCost")]
        internal static class MunitionCostPatch
        {
            [HarmonyPrefix]
            private static bool Before(Unit unit) => unit == null || PmcSide.Hq == null || !PmcSide.UnitIds.Contains(unit.GetInstanceID());
        }

        /// <summary>PMC units are not in the HQ's synced unit list, so the map is told about the ones this side tracks.</summary>
        [HarmonyPatch(typeof(DynamicMap), "GenerateMap")]
        internal static class GenerateMapPatch
        {
            [HarmonyPostfix]
            private static void After(DynamicMap __instance, FactionHQ HQ)
            {
                if (PmcSide.Hq == null || HQ == null) return;
                try
                {
                    // A PMC pilot's map shows the side's own units; anybody else's only the ones it has found.
                    foreach (PersistentID id in PmcSide.UnitPids)
                        if (HQ == PmcSide.Hq || HQ.trackingDatabase.ContainsKey(id)) __instance.AddIcon(id);
                }
                catch (Exception e) { PatchGuard.Report("PmcGenerateMap", e); }
            }
        }

        /// <summary>A PMC unit saved from the editor keeps its faction name.</summary>
        [HarmonyPatch(typeof(SavedUnit), "AfterCreate")]
        internal static class SavedUnitPatch
        {
            [HarmonyPostfix]
            private static void After(SavedUnit __instance, Unit unit)
            {
                if (PmcSide.Hq == null || unit == null) return;
                if (IsPmc(unit.NetworkHQ) || PmcSide.UnitIds.Contains(unit.GetInstanceID())) __instance.faction = SideNames.Pmc;
            }
        }

        /// <summary>The editor colours a unit by its faction; for PMC it finds none without this.</summary>
        [HarmonyPatch]
        internal static class EditorUnitColorPatch
        {
            private static MethodBase Target() => AccessTools.Method("NuclearOption.MissionEditorScripts.EditorCursor:SetUnitColor");

            private static bool Prepare() => Target() != null;

            private static MethodBase TargetMethod() => Target();

            [HarmonyPrefix]
            private static void Before(Unit unit, ref Faction faction)
            {
                if (faction != null || unit == null || PmcSide.Faction == null) return;
                if (unit.SavedUnit?.faction == SideNames.Pmc || IsPmc(unit.NetworkHQ)) faction = PmcSide.Faction;
            }
        }
    }
}
