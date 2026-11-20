using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Scenario;
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

        /// <summary>Read the native mission on both host and client; do not rewrite the player's module/settings choices.</summary>
        internal static bool FortificationsAllowed
        {
            get
            {
                Mission mission = MissionManager.CurrentMission;
                return mission != null && !ReferenceEquals(mission, Mission.NullMission) &&
                    MissionProfile.AllowsFortifications(mission.Name);
            }
        }

        /// <summary>Picker groups to add to the game's mission list (the scripted missions). Asked whenever the game reads its list.</summary>
        internal static Func<IReadOnlyList<MissionGroup>> ExtraGroups;

        /// <summary>The picker label of a provider's own mission ("WARBOX / Heartland"); null for a key that is not the
        /// provider's. A mission nobody labels is a campaign chapter.</summary>
        internal static Func<MissionKey, string> Labels;

        private static OneGroup one;

        /// <summary>
        /// The one "Boscali Summer" tab of the picker: every mission of every provider's group, named KIND / TITLE and
        /// ordered campaign, WarBox, the other modes, free flight. The providers' own groups are not shown; each still
        /// builds its missions and loads them for the launch argument.
        /// </summary>
        private sealed class OneGroup : MissionGroup
        {
            private readonly Dictionary<string, MissionKey> own = new Dictionary<string, MissionKey>(StringComparer.Ordinal);

            internal OneGroup() : base("Boscali Summer") { }

            private static string Label(MissionKey key)
            {
                if (Labels != null)
                    foreach (Func<MissionKey, string> provider in Labels.GetInvocationList())
                    {
                        string label = provider(key);
                        if (!string.IsNullOrEmpty(label)) return label;
                    }
                return "CAMPAIGN / " + key.Name;
            }

            private static int Rank(string label) =>
                label.StartsWith("CAMPAIGN /", StringComparison.Ordinal) ? 0 : label.StartsWith("WARBOX /", StringComparison.Ordinal) ? 1 :
                label.StartsWith("FREE FLIGHT /", StringComparison.Ordinal) ? 3 : 2;

            public override IEnumerable<MissionKey> GetMissions()
            {
                var listed = new List<MissionKey>();
                own.Clear();
                if (ExtraGroups != null)
                    foreach (Func<IReadOnlyList<MissionGroup>> provider in ExtraGroups.GetInvocationList())
                        foreach (MissionGroup group in provider() ?? Array.Empty<MissionGroup>())
                        {
                            if (group == null) continue;
                            foreach (MissionKey key in group.GetMissions())
                            {
                                if (own.ContainsKey(key.Key)) continue;
                                own[key.Key] = key;
                                listed.Add(new MissionKey(key.Key, Label(key), null, this));
                            }
                        }
                return listed.OrderBy(k => Rank(k.Name)).ToArray();   // a stable sort: a provider's own order (campaign chapters) is kept
            }

            private bool Find(string key, out MissionKey found)
            {
                if (!own.ContainsKey(key)) GetMissions();   // a recent mission is loaded before the list was ever read
                return own.TryGetValue(key, out found);
            }

            public override bool TryGetJson(MissionKey key, out string json)
            {
                json = null;
                return Find(key.Key, out MissionKey found) && found.Group.TryGetJson(found, out json);
            }

            public override UniTask<Sprite> GetPreview(string key, CancellationToken token = default(CancellationToken)) =>
                Find(key, out MissionKey found) ? found.Group.GetPreview(found.Key, token) : UniTask.FromResult<Sprite>(null);
        }

        /// <summary>Required rules of a mod gamemode, or null for ordinary missions.</summary>
        internal static Func<Mission, string[]> PresetModulesOff;

        /// <summary>
        /// For a scripted mission, the modules its script switches off; null for every other mission. A scripted mission is never prompted for: its script has
        /// already said what runs.
        /// </summary>
        internal static Func<Mission, string[]> ScriptedModulesOff;

        /// <summary>For a scripted mission, the faction-name pairs its script puts at peace (<see cref="FactionPeace"/>); null for every other mission.</summary>
        internal static Func<Mission, IEnumerable<KeyValuePair<string, string>>> ScriptedPeace;

        internal static readonly Type[] PatchTypes =
        [
            typeof(MissionGroupsPatch), typeof(MissionSelectGroupsPatch), typeof(SetMissionPatch), typeof(SceneLoadedPatch),
            typeof(SyncMissionPatch), typeof(StopMissionPatch), typeof(MissionEnvironmentPatch),
            .. MissionContentPatches.PatchTypes,
            // Guards for game bugs: on in every mission, whatever modules the mission switches off.
            typeof(MissileMotorGuardPatch), typeof(ThreatItemNoAircraftPatch),
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
                if (mission == null) return;
                // Before anything spawns: the first sightings of the mission already have to respect it. Cleared first, so a script that fails to
                // resolve leaves no peace from the mission before.
                FactionPeace.Set(null);
                FactionPeace.Set(ScriptedPeace?.Invoke(mission));
                string[] preset = PresetModulesOff?.Invoke(mission);
                string[] scripted = preset == null ? ScriptedModulesOff?.Invoke(mission) : null;
                MissionProfile.SetRequiredDisabled(preset ?? scripted);
                if (settings == null) return;
                string name = mission.Name ?? "";
                bool remembered = Remembered().TryGetValue(name, out string list);
                if (preset != null)
                {
                    // Required mode rules are a separate mask; preserve the pilot's optional choices.
                    MissionProfile.SetDisabled(MissionProfile.Parse(remembered ? list : settings.MissionDisabledModules.Value));
                    PendingMission = null;
                    return;
                }
                if (scripted != null)
                {
                    MissionProfile.SetDisabled(scripted);
                    PendingMission = null;
                    return;
                }
                MissionProfile.SetDisabled(MissionProfile.Parse(remembered ? list : settings.MissionDisabledModules.Value));
                // A nomodkit sim has no one to answer the prompt, and it would cover the mission it drives.
                bool sim = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));
                PendingMission = !sim && !remembered && settings.MissionAskAtStart.Value && name != answered ? name : null;
            }
            catch (Exception e) { PatchGuard.Report("MissionProfile", e); }
        }

        /// <summary>The start prompt's answer. A plain start also becomes the selection the next prompt opens with.</summary>
        internal static void Confirm(string mission, IEnumerable<string> disabled, bool rememberMission, bool neverAsk)
        {
            var optional = new HashSet<string>(disabled, StringComparer.Ordinal);
            optional.RemoveWhere(MissionProfile.RequiredOff);
            string prior = Remembered().TryGetValue(mission, out string saved) ? saved : settings.MissionDisabledModules.Value;
            foreach (string id in MissionProfile.Parse(prior))
                if (MissionProfile.RequiredOff(id)) optional.Add(id);
            string list = MissionProfile.Format(optional);
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

        // The picker's tab row and GetGroup read this list, not AllGroups (split in a game update).
        [HarmonyPatch(typeof(MissionGroup), nameof(MissionGroup.MissionSelectGroups), MethodType.Getter)]
        private static class MissionSelectGroupsPatch
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
                one = one ?? new OneGroup();
                if (Array.IndexOf(__result, one) < 0) __result = __result.Concat(new MissionGroup[] { one }).ToArray();
            }
            catch (Exception e) { PatchGuard.Report("MissionGroups", e); }
        }
    }
}
