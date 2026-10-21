using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Scenario;
using BoscaliSummer.Modules.Pmc.Runtime;
using HarmonyLib;
using Mirage;
using NuclearOption.Jobs;
using NuclearOption.SavedMission;

namespace BoscaliSummer.Modules.Pmc.Patches
{
    /// <summary>
    /// What the game needs to treat the extra HQs (PMC, Pirates) as sides, and to let sides be at peace. Each patch is a no-op for a mission without
    /// an extra side or a <c>peace</c> line.
    /// </summary>
    internal static class PmcPatches
    {
        internal static readonly Type[] All =
        {
            typeof(SpawnSceneObjectsPatch), typeof(SetMissionPatch), typeof(StartMissionPatch), typeof(EditorSpawnPatch),
            typeof(FactionFromNamePatch), typeof(HqFromNamePatch), typeof(HqFromFactionPatch), typeof(RegisterFactionPatch),
            typeof(HqUpdatePatch), typeof(SetupAirbaseFactionsPatch), typeof(RegisterUnitPatch), typeof(RemoveUnitPatch),
            typeof(MunitionCostPatch), typeof(GenerateMapPatch), typeof(SavedUnitPatch), typeof(EditorUnitColorPatch),
            typeof(DetectTargetPatch), typeof(TrackingPatch), typeof(RadarCheckPatch), typeof(RadarReturnPatch), typeof(RequestRearmPatch), typeof(RearmerPatch), typeof(ForgetAtPeacePatch)
        };

        /// <summary>Two different sides a script put at peace.</summary>
        private static bool AtPeace(FactionHQ a, FactionHQ b) =>
            a != null && b != null && a != b && a.faction != null && b.faction != null && FactionPeace.Between(a.faction.factionName, b.faction.factionName);

        /// <summary>After Mirage spawns the scene objects: the extra HQs are made then, so they get a real net id.</summary>
        [HarmonyPatch(typeof(ServerObjectManager), "SpawnSceneObjects")]
        internal static class SpawnSceneObjectsPatch
        {
            [HarmonyPostfix]
            private static void After(ServerObjectManager __instance)
            {
                try
                {
                    ExtraSides.Som = __instance;
                    foreach (ExtraSide side in ExtraSides.All)
                        if (side.Wanted(MissionManager.CurrentMission)) side.Ensure();
                }
                catch (Exception e) { PatchGuard.Report("PmcSpawnScene", e); }
            }
        }

        /// <summary>A mission that does not name a side must not inherit it from the last one.</summary>
        [HarmonyPatch(typeof(MissionManager), "SetMission")]
        internal static class SetMissionPatch
        {
            [HarmonyPostfix]
            private static void After()
            {
                try
                {
                    foreach (ExtraSide side in ExtraSides.All)
                        if (!side.Wanted(MissionManager.CurrentMission)) side.Forget();
                }
                catch (Exception e) { PatchGuard.Report("PmcSetMission", e); }
            }
        }

        [HarmonyPatch(typeof(MissionManager), "StartMission")]
        internal static class StartMissionPatch
        {
            [HarmonyPrefix]
            private static void Before()
            {
                try
                {
                    foreach (ExtraSide side in ExtraSides.All)
                        if (side.Wanted(MissionManager.CurrentMission)) side.Ensure();
                }
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
                    ExtraSide pmc = ExtraSides.Pmc;
                    if (pmc?.Faction == null || mission == null) return;
                    if (!mission.TryGetFaction(pmc.Key, out _)) mission.factions.Add(new MissionFaction(pmc.Key));
                    pmc.Ensure();
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
                if (__result != null) return;
                ExtraSide side = ExtraSides.Named(factionName);
                if (side?.Hq != null) __result = side.Faction;
            }
        }

        /// <summary>The extra sides are not in HQLookup, so a lookup by name would throw; they answer here.</summary>
        [HarmonyPatch(typeof(FactionRegistry), nameof(FactionRegistry.HqFromName))]
        internal static class HqFromNamePatch
        {
            [HarmonyPrefix]
            private static bool Before(string factionName, ref FactionHQ __result)
            {
                ExtraSide side = ExtraSides.Named(factionName);
                if (side == null) return true;
                __result = side.Hq;
                return false;
            }
        }

        [HarmonyPatch(typeof(FactionRegistry), nameof(FactionRegistry.HQFromFaction))]
        internal static class HqFromFactionPatch
        {
            [HarmonyPrefix]
            private static bool Before(object[] __args, ref FactionHQ __result)
            {
                ExtraSide side = ExtraSides.Of(__args[0] as Faction);
                if (side == null) return true;
                __result = side.Hq;
                return false;
            }
        }

        /// <summary>An extra HQ registers itself on server start; it is taken back out so the two-slot join screen never sees it.</summary>
        [HarmonyPatch(typeof(FactionRegistry), nameof(FactionRegistry.RegisterFaction))]
        internal static class RegisterFactionPatch
        {
            [HarmonyPostfix]
            private static void After(Faction faction)
            {
                if (ExtraSides.Of(faction) != null) FactionRegistry.HQLookup.Remove(faction);
            }
        }

        /// <summary>Between being made and its server setup, an extra HQ has no threat tracker yet; its update waits for it.</summary>
        [HarmonyPatch(typeof(FactionHQ), "Update")]
        internal static class HqUpdatePatch
        {
            private static readonly FieldInfo Tracker = AccessTools.Field(typeof(FactionHQ), "aircraftThreatTracker");

            [HarmonyPrefix]
            private static bool Before(FactionHQ __instance) => ExtraSides.Of(__instance) == null || Tracker?.GetValue(__instance) != null;
        }

        /// <summary>An extra HQ must not hand the BDF and PALA airbases around when it starts.</summary>
        [HarmonyPatch(typeof(FactionHQ), "SetupAirbaseFactions")]
        internal static class SetupAirbaseFactionsPatch
        {
            [HarmonyPrefix]
            private static bool Before(FactionHQ __instance) => ExtraSides.Of(__instance) == null;
        }

        [HarmonyPatch(typeof(FactionHQ), "RegisterFactionUnit")]
        internal static class RegisterUnitPatch
        {
            [HarmonyPrefix]
            private static bool Before(FactionHQ __instance, Unit unit)
            {
                ExtraSide side = ExtraSides.Of(__instance);
                if (side == null || unit == null) return true;
                side.UnitIds.Add(unit.GetInstanceID());
                side.UnitPids.Add(unit.persistentID);
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
                ExtraSide side = ExtraSides.Of(__instance);
                if (side == null || unit == null) return true;
                side.UnitIds.Remove(unit.GetInstanceID());
                side.UnitPids.Remove(unit.persistentID);
                return false;
            }
        }

        /// <summary>
        /// An extra HQ has no rearm controller: a unit of its side asks for rearming after every shot it fires (ships, vehicles, a player's aircraft), and
        /// the game's request would throw each time. The extra sides do without rearming.
        /// </summary>
        [HarmonyPatch(typeof(Unit), nameof(Unit.RequestRearm))]
        internal static class RequestRearmPatch
        {
            [HarmonyPrefix]
            private static bool Before(Unit __instance) => ExtraSides.Of(__instance.NetworkHQ) == null;
        }

        /// <summary>
        /// The same missing controller for a unit that carries a rearmer (a corvette's deck): it registers as it starts, leaves as it is sunk and moves when it
        /// changes sides, and each would throw for an extra side.
        /// </summary>
        [HarmonyPatch]
        internal static class RearmerPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (string name in new[] { "Rearmer_OnInitialize", "Rearmer_OnUnitDisabled", "Rearmer_OnChangeFaction" })
                {
                    MethodBase method = AccessTools.Method(typeof(Rearmer), name);
                    if (method != null) yield return method;
                }
            }

            [HarmonyPrefix]
            private static bool Before(Rearmer __instance) => __instance == null || __instance.Unit == null || ExtraSides.Of(__instance.Unit.NetworkHQ) == null;
        }

        /// <summary>An extra stats tracker's dictionary is not set up; costing one of its rounds would throw in Gun.SpawnBullet.</summary>
        [HarmonyPatch(typeof(MissionStatsTracker), "MunitionCost")]
        internal static class MunitionCostPatch
        {
            [HarmonyPrefix]
            private static bool Before(Unit unit) => unit == null || !ExtraSides.Holds(unit.GetInstanceID());
        }

        /// <summary>Extra-side units are not in an HQ's synced unit list, so the map is told about the ones this side tracks.</summary>
        [HarmonyPatch(typeof(DynamicMap), "GenerateMap")]
        internal static class GenerateMapPatch
        {
            [HarmonyPostfix]
            private static void After(DynamicMap __instance, FactionHQ HQ)
            {
                if (HQ == null) return;
                try
                {
                    // A pilot's map shows the side's own units; anybody else's only the ones it has found.
                    foreach (ExtraSide side in ExtraSides.All)
                        if (side.Hq != null)
                            foreach (PersistentID id in side.UnitPids)
                                if (HQ == side.Hq || HQ.trackingDatabase.ContainsKey(id)) __instance.AddIcon(id);
                }
                catch (Exception e) { PatchGuard.Report("PmcGenerateMap", e); }
            }
        }

        /// <summary>An extra-side unit saved from the editor keeps its faction name.</summary>
        [HarmonyPatch(typeof(SavedUnit), "AfterCreate")]
        internal static class SavedUnitPatch
        {
            [HarmonyPostfix]
            private static void After(SavedUnit __instance, Unit unit)
            {
                if (unit == null) return;
                foreach (ExtraSide side in ExtraSides.All)
                    if (side.Hq != null && (side.Owns(unit.NetworkHQ) || side.UnitIds.Contains(unit.GetInstanceID()))) __instance.faction = side.Key;
            }
        }

        /// <summary>The editor colours a unit by its faction; for an extra side it finds none without this.</summary>
        [HarmonyPatch]
        internal static class EditorUnitColorPatch
        {
            private static MethodBase Target() => AccessTools.Method("NuclearOption.MissionEditorScripts.EditorCursor:SetUnitColor");

            private static bool Prepare() => Target() != null;

            private static MethodBase TargetMethod() => Target();

            [HarmonyPrefix]
            private static void Before(Unit unit, ref Faction faction)
            {
                if (faction != null || unit == null) return;
                foreach (ExtraSide side in ExtraSides.All)
                    if (side.Faction != null && (unit.SavedUnit?.faction == side.Key || side.Owns(unit.NetworkHQ))) faction = side.Faction;
            }
        }

        /// <summary>
        /// Peace, part one. Every radar and eye of a unit reports what it sees here: into its own list (turrets and fire control pick from it) and into
        /// its HQ's tracking database. A sighting between two sides at peace never happens.
        /// </summary>
        [HarmonyPatch(typeof(TargetDetector), nameof(TargetDetector.DetectTarget))]
        internal static class DetectTargetPatch
        {
            [HarmonyPrefix]
            private static bool Before(TargetDetector __instance, Unit target)
            {
                if (!FactionPeace.Any || target == null) return true;
                Unit own = __instance.GetAttachedUnit();
                return own == null || !AtPeace(own.NetworkHQ, target.NetworkHQ);
            }
        }

        /// <summary>
        /// Peace, part two. Everything else that tells an HQ where a unit is (laser spots, missile warnings, radar warning receivers, submunitions) goes
        /// through this RPC, and AI aircraft, ships and strategy all pick their targets from the database it fills.
        /// </summary>
        [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.RpcUpdateTrackingInfo))]
        internal static class TrackingPatch
        {
            [HarmonyPrefix]
            private static bool Before(FactionHQ __instance, PersistentID id) =>
                !FactionPeace.Any || !(UnitRegistry.TryGetUnit(id, out Unit unit) && AtPeace(__instance, unit.NetworkHQ));
        }

        /// <summary>
        /// Peace, part four. A unit can be sighted before it has a side (a fresh spawn, an ejected pilot) and keep the track once it joins one: the game forgets
        /// a track only when the unit comes over to the tracking side. A side at peace with the one it joined forgets it the same way.
        /// </summary>
        [HarmonyPatch(typeof(FactionHQ), "HQ_OnUnitChangeFaction")]
        internal static class ForgetAtPeacePatch
        {
            [HarmonyPostfix]
            private static void After(FactionHQ __instance, Unit unit)
            {
                if (FactionPeace.Any && unit != null && AtPeace(__instance, unit.NetworkHQ)) __instance.DeregisterTrackedUnit(unit);
            }
        }

        /// <summary>
        /// A radar sweeps only the HQs in HQLookup, which the extra sides are kept out of: without this nobody's radar ever sees a PMC or pirate unit,
        /// and a pilot cannot lock one. The same sweep for their units; peace is decided where it reports, in <see cref="DetectTargetPatch"/>.
        /// </summary>
        [HarmonyPatch(typeof(Radar), "RadarCheck")]
        internal static class RadarCheckPatch
        {
            private static readonly AccessTools.FieldRef<Radar, float> Cone = AccessTools.FieldRefAccess<Radar, float>("radarCone");

            [HarmonyPostfix]
            private static void After(Radar __instance)
            {
                bool any = false;
                foreach (ExtraSide side in ExtraSides.All) any |= side.Hq != null;
                if (!any) return;
                try
                {
                    Unit own = __instance.GetAttachedUnit();
                    UnityEngine.Transform scanner = __instance.GetScanPoint();
                    if (own == null || scanner == null) return;
                    GlobalPosition at = scanner.GlobalPosition();
                    float range = __instance.RadarParameters.maxRange * 2f, cone = Cone(__instance);
                    foreach (ExtraSide side in ExtraSides.All)
                    {
                        if (side.Hq == null || side.Hq == own.NetworkHQ || AtPeace(own.NetworkHQ, side.Hq)) continue;
                        foreach (PersistentID id in side.UnitPids)
                            if (UnitRegistry.TryGetUnit(id, out Unit unit) && unit is IRadarReturn radarReturn && FastMath.InRange(unit.GlobalPosition(), at, range) &&
                                (!(cone > 0f) || !(UnityEngine.Vector3.Angle(unit.transform.position - scanner.position, scanner.forward) > cone)))
                                DetectorManager.RequestRadarCheck(__instance, unit, radarReturn);
                    }
                }
                catch (Exception e) { PatchGuard.Report("PmcRadarCheck", e); }
            }
        }

        /// <summary>
        /// Peace, part three. A radar that paints an aircraft sets off its warning receiver before the sighting is ever reported, so a pilot at peace with
        /// a SAM site would still hear it lock on. A side at peace never sees the other's radar return at all.
        /// </summary>
        [HarmonyPatch(typeof(Radar), nameof(Radar.CanSeeRadarReturn))]
        internal static class RadarReturnPatch
        {
            [HarmonyPrefix]
            private static bool Before(Radar __instance, IRadarReturn radarReturn, ref bool __result)
            {
                if (!FactionPeace.Any || !(radarReturn is Unit target)) return true;
                Unit own = __instance.GetAttachedUnit();
                if (own == null || !AtPeace(own.NetworkHQ, target.NetworkHQ)) return true;
                __result = false;
                return false;
            }
        }
    }
}
