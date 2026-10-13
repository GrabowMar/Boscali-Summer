using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using BoscaliSummer.Core.Diagnostics;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>A separate native picker entry backed by the installed game's Escalation data.</summary>
    internal static class BoscaliMission
    {
        internal const string MissionName = "Boscali Summer";
        internal const string DescriptionMarker = MissionProfile.DescriptionMarker;
        private const string SourceName = "Escalation";

        private static CopyGroup group;
        private static ManualLogSource log;
        private static bool warned;

        internal static readonly Type[] PatchTypes =
        [
            typeof(MissionGroupsPatch), typeof(SetMissionPatch), typeof(SceneLoadedPatch),
            typeof(SyncMissionPatch), typeof(StopMissionPatch), typeof(MissionEnvironmentPatch),
            .. MissionContentPatches.PatchTypes
        ];

        internal static bool HasMarker(Mission mission) =>
            MissionProfile.IsFullMission(mission?.missionSettings?.description);

        internal static string CreateCopyJson(string sourceJson)
        {
            using var reader = new JsonTextReader(new StringReader(sourceJson)) { DateParseHandling = DateParseHandling.None };
            var copy = JObject.Load(reader);
            if (copy["missionSettings"] is not JObject settings || settings["description"]?.Type != JTokenType.String)
                throw new InvalidOperationException("Escalation has no valid mission description.");
            settings["description"] = "The complete Boscali Summer experience, based on Escalation.\n\n" +
                settings["description"].Value<string>() + "\n\n" + DescriptionMarker;
            // Keep all source fields verbatim as JSON values, including unknown fields from future
            // game versions. Loading into Mission here would normalize nulls and default values.
            return copy.ToString(Formatting.None);
        }

        internal static void EnsureRegistered(ManualLogSource logger)
        {
            log = logger;
            group ??= new CopyGroup();
        }

        internal static void Unregister()
        {
            group = null;
            warned = false;
            log = null;
        }

        private static void SetProfile(Mission mission)
        {
            try { MissionProfile.SetDescription(mission?.missionSettings?.description); }
            catch (Exception e) { PatchGuard.Report("MissionProfile", e); }
        }

        [HarmonyPatch(typeof(MissionGroup), nameof(MissionGroup.AllGroups), MethodType.Getter)]
        private static class MissionGroupsPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref MissionGroup[] __result) => AddGroup(ref __result);
        }

        [HarmonyPatch(typeof(MissionManager), nameof(MissionManager.SetMission))]
        private static class SetMissionPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Mission mission) => SetProfile(mission);
        }

        [HarmonyPatch(typeof(Mission), nameof(Mission.OnSceneLoaded))]
        private static class SceneLoadedPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Mission __instance) => SetProfile(__instance);
        }

        [HarmonyPatch(typeof(MissionManager), "OnSyncMissionChanged")]
        private static class SyncMissionPatch
        {
            [HarmonyPrefix]
            private static void Prefix((NetworkMission.SyncMission syncMission, NetworkMission.State state, bool stateChangeOnly) tuple) =>
                SetProfile(tuple.state == NetworkMission.State.Unloaded ? null : tuple.syncMission.Mission);
        }

        [HarmonyPatch(typeof(MissionManager), "StopMission")]
        private static class StopMissionPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => SetProfile(null);
        }

        [HarmonyPatch(typeof(LevelInfo), nameof(LevelInfo.LoadFromMission))]
        private static class MissionEnvironmentPatch
        {
            [HarmonyPrefix]
            private static void Prefix(Mission mission)
            {
                // Native map reuse can skip Unity's sceneLoaded event. Reset mission services
                // here, before LoadEnvironment applies the new sky, weather and native baseline.
                try { MissionProfile.PrepareMission(mission?.missionSettings?.description); }
                catch (Exception e) { PatchGuard.Report("MissionEnvironment", e); }
            }
        }

        private static void AddGroup(ref MissionGroup[] __result)
        {
            // The game initializes its ResourceGroups before reading AllGroups. Defer copying until
            // that point so plugin startup does not force native resource initialization early.
            if (__result == null || group == null || !group.EnsureCopy()) return;
            for (int i = 0; i < __result.Length; i++)
                if (ReferenceEquals(__result[i], group)) return;
            var expanded = new MissionGroup[__result.Length + 1];
            Array.Copy(__result, expanded, __result.Length);
            expanded[__result.Length] = group;
            __result = expanded;
        }

        private sealed class CopyGroup : MissionGroup
        {
            private string json;
            private readonly MissionKey[] missions;

            internal CopyGroup() : base(MissionName)
            {
                missions = new[] { new MissionKey(MissionName, this) };
            }

            internal bool EnsureCopy()
            {
                if (json != null) return true;
                try
                {
                    var sourceKey = new MissionKey(SourceName, BuiltIn);
                    if (!BuiltIn.TryGetJson(sourceKey, out string sourceJson))
                        throw new InvalidOperationException("The installed Escalation mission could not be loaded.");
                    // Native mission loading happens only after the player selects this copy. Its
                    // description marker also reaches clients whose AfterLoad(name) clears LoadKey.
                    json = CreateCopyJson(sourceJson);
                    return true;
                }
                catch (Exception e)
                {
                    if (!warned)
                    {
                        warned = true;
                        log?.LogWarning("Boscali Summer mission unavailable: " + e.Message);
                    }
                    return false;
                }
            }

            public override IEnumerable<MissionKey> GetMissions() =>
                EnsureCopy() ? missions : Array.Empty<MissionKey>();

            public override bool TryGetJson(MissionKey key, out string contents)
            {
                contents = null;
                if (!ReferenceEquals(key.Group, this) || key.Key != MissionName || !EnsureCopy()) return false;
                contents = json;
                return true;
            }

            public override UniTask<Sprite> GetPreview(string key, CancellationToken token) =>
                BuiltIn.GetPreview(SourceName, token);
        }
    }
}
