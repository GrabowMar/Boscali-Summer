using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;
using Newtonsoft.Json;
using NuclearOption.SavedMission;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// The seams into the game's missions: which modules run in the mission being loaded, and the picker groups that modules add to the game's mission list.
    /// </summary>
    internal static class BoscaliMission
    {
        private static ModConfiguration settings;
        private static string answered;

        /// <summary>The mission whose start prompt is still unanswered; null when there is nothing to ask.</summary>
        internal static string PendingMission { get; private set; }

        /// <summary>Picker groups to add to the game's mission list (the scripted missions). Asked whenever the game reads its list.</summary>
        internal static Func<IReadOnlyList<MissionGroup>> ExtraGroups;

        /// <summary>
        /// For a scripted mission, the modules its script switches off; null for every other mission. A scripted mission is never prompted for: its script has
        /// already said what runs.
        /// </summary>
        internal static Func<Mission, string[]> ScriptedModulesOff;

        internal static readonly Type[] PatchTypes =
        [
            typeof(MissionGroupsPatch), typeof(SetMissionPatch), typeof(SceneLoadedPatch),
            typeof(SyncMissionPatch), typeof(StopMissionPatch), typeof(MissionEnvironmentPatch),
            .. MissionContentPatches.PatchTypes
        ];

        internal static void EnsureRegistered(ModConfiguration configuration) => settings = configuration;

        internal static void Unregister()
        {
            settings = null;
            PendingMission = answered = null;
        }

        private static Dictionary<string, string> Remembered()
        {
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(settings.MissionRemembered.Value) ??
                    new Dictionary<string, string>();
            }
            catch (JsonException) { return new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Applies the player's module selection for a mission before it spawns anything: the
        /// choice remembered for that mission, else the last one confirmed anywhere.
        /// </summary>
        private static void SelectFor(Mission mission)
        {
            try
            {
                if (mission == null || settings == null) return;
                string[] scripted = ScriptedModulesOff?.Invoke(mission);
                if (scripted != null)
                {
                    MissionProfile.SetDisabled(scripted);
                    PendingMission = null;
                    return;
                }
                string name = mission.Name ?? "";
                bool remembered = Remembered().TryGetValue(name, out string list);
                MissionProfile.SetDisabled(MissionProfile.Parse(remembered ? list : settings.MissionDisabledModules.Value));
                PendingMission = !remembered && settings.MissionAskAtStart.Value && name != answered ? name : null;
            }
            catch (Exception e) { PatchGuard.Report("MissionProfile", e); }
        }

        /// <summary>The start prompt's answer. A plain start also becomes the selection the next prompt opens with.</summary>
        internal static void Confirm(string mission, IEnumerable<string> disabled, bool rememberMission, bool neverAsk)
        {
            string list = MissionProfile.Format(disabled);
            if (rememberMission)
            {
                Dictionary<string, string> map = Remembered();
                map[mission] = list;
                settings.MissionRemembered.Value = JsonConvert.SerializeObject(map);
            }
            else settings.MissionDisabledModules.Value = list;
            if (neverAsk) settings.MissionAskAtStart.Value = false;
            settings.MissionAskAtStart.ConfigFile.Save();
            answered = mission;
            PendingMission = null;
            MissionProfile.SetDisabled(MissionProfile.Parse(list));
        }

        [HarmonyPatch(typeof(MissionGroup), nameof(MissionGroup.AllGroups), MethodType.Getter)]
        private static class MissionGroupsPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref MissionGroup[] __result) => AddGroups(ref __result);
        }

        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.SetMission))]
        private static class SetMissionPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Mission mission) => SelectFor(mission);
        }

        [HarmonyPatch(typeof(Mission), nameof(Mission.OnSceneLoaded))]
        private static class SceneLoadedPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Mission __instance) => SelectFor(__instance);
        }

        [HarmonyPatch(typeof(MissionManager), "OnSyncMissionChanged")]
        private static class SyncMissionPatch
        {
            [HarmonyPrefix]
            private static void Prefix((NetworkMission.SyncMission syncMission, NetworkMission.State state, bool stateChangeOnly) tuple)
            {
                if (tuple.state == NetworkMission.State.Unloaded) PendingMission = answered = null;
                else if (!tuple.stateChangeOnly) SelectFor(tuple.syncMission.Mission);
            }
        }

        [HarmonyPatch(typeof(MissionManager), "StopMission")]
        private static class StopMissionPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => PendingMission = answered = null;
        }

        [HarmonyPatch(typeof(LevelInfo), nameof(LevelInfo.LoadFromMission))]
        private static class MissionEnvironmentPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                // Native map reuse can skip Unity's sceneLoaded event. Reset mission services
                // here, before LoadEnvironment applies the new sky, weather and native baseline.
                try { MissionProfile.PrepareMission(); }
                catch (Exception e) { PatchGuard.Report("MissionEnvironment", e); }
            }
        }

        private static void AddGroups(ref MissionGroup[] __result)
        {
            // Asked here and not at startup: the game builds its own groups before it reads this list.
            if (__result == null || ExtraGroups == null) return;
            try
            {
                IReadOnlyList<MissionGroup> extra = ExtraGroups();
                if (extra == null || extra.Count == 0) return;
                var expanded = new List<MissionGroup>(__result);
                foreach (MissionGroup group in extra)
                    if (group != null && !expanded.Contains(group)) expanded.Add(group);
                if (expanded.Count != __result.Length) __result = expanded.ToArray();
            }
            catch (Exception e) { PatchGuard.Report("MissionGroups", e); }
        }
    }
}
