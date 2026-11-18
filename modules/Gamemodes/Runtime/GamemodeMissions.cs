using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BepInEx.Logging;
using BoscaliSummer.Modules.Gamemodes.Domain;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>
    /// Additional native picker entries, built from the player's installed missions without editing them: WarBox on each
    /// shipped map.
    /// Fuzzball (airborne team deathmatch) and Heavyweight (cargo run and interception) are no longer built here from
    /// Escalation: they are played on the mod's desert map, whose missions the Maps module lists ("Tassara - Fuzzball").
    /// This module still runs both matches. TODO: more maps of their own.
    /// </summary>
    internal sealed class GamemodeMissions
    {
        private readonly Group group;
        private readonly MissionGroup[] groups;
        private readonly ManualLogSource log;
        private readonly Dictionary<string, KeyValuePair<string, string>> built = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);

        internal GamemodeMissions(ManualLogSource logger)
        {
            log = logger;
            group = new Group(this);
            groups = new MissionGroup[] { group };
        }

        internal IReadOnlyList<MissionGroup> Groups() => groups;

        /// <summary>The picker label of one of this group's missions.</summary>
        internal string Label(MissionKey key)
        {
            int n = ReferenceEquals(key.Group, group) ? GamemodeMissionBuilder.WarBoxOf(key.Key) : -1;
            return n < 0 ? null : "WARBOX / " + GamemodeMissionBuilder.WarBoxes[n].Map;
        }

        internal Mission Launch(string id)
        {
            int n = GamemodeMissionBuilder.WarBoxOf(id);
            if (n < 0)
            {
                // Fuzzball and Heavyweight live on the desert map: its mission is in the mod's one picker group.
                GameModeKind kind = GamemodeMissionBuilder.FromId(id);
                if (kind == GameModeKind.None) return null;
                MissionGroup.Init();
                MissionGroup all = MissionGroup.MissionSelectGroups.FirstOrDefault(g => g != null && g.Name == "Boscali Summer");
                string why = "the mod's mission list is not there";
                if (all != null && MissionSaveLoad.TryLoad(new MissionKey("Tassara - " + GamemodeMissionBuilder.Title(kind), all), out Mission desert, out why)) return desert;
                log?.LogWarning("[Gamemodes] Could not load " + id + " on Tassara: " + why);
                return null;
            }
            MissionGroup.Init();
            if (MissionSaveLoad.TryLoad(new MissionKey(GamemodeMissionBuilder.WarBoxes[n].Key, group), out Mission mission, out string error))
                return mission;
            log?.LogWarning("[Gamemodes] Could not load " + id + ": " + error);
            return null;
        }

        private bool TryBuild(string key, out string json)
        {
            json = null;
            int n = GamemodeMissionBuilder.WarBoxOf(key);
            if (n < 0) return false;
            string baseName = GamemodeMissionBuilder.WarBoxes[n].Base;
            MissionGroup source = MissionGroup.BuiltIn;
            if (source == null || !source.TryGetJson(new MissionKey(baseName, source), out string original) || string.IsNullOrEmpty(original))
            {
                log?.LogWarning("[Gamemodes] The installed " + baseName + " mission is unavailable.");
                return false;
            }
            if (built.TryGetValue(key, out KeyValuePair<string, string> cached) && string.Equals(cached.Key, original, StringComparison.Ordinal))
            {
                json = cached.Value;
                return true;
            }
            try
            {
                json = GamemodeMissionBuilder.Apply(original, GameModeKind.WarBox, baseName);
                built[key] = new KeyValuePair<string, string>(original, json);
                return true;
            }
            catch (Exception error) when (error is JsonException || error is InvalidOperationException || error is OverflowException)
            {
                log?.LogWarning("[Gamemodes] Could not build " + key + ": " + error.Message);
                return false;
            }
        }

        private sealed class Group : MissionGroup
        {
            private readonly GamemodeMissions owner;
            private readonly MissionKey[] keys;

            internal Group(GamemodeMissions owner) : base("Boscali Summer Gamemodes")
            {
                this.owner = owner;
                keys = GamemodeMissionBuilder.WarBoxes.Select(w => new MissionKey(w.Key, this)).ToArray();
            }

            public override IEnumerable<MissionKey> GetMissions() => keys;

            public override bool TryGetJson(MissionKey key, out string json)
            {
                json = null;
                return ReferenceEquals(key.Group, this) && owner.TryBuild(key.Key, out json);
            }

            public override UniTask<Sprite> GetPreview(string key, CancellationToken token = default)
            {
                int n = GamemodeMissionBuilder.WarBoxOf(key);
                return n >= 0 && MissionGroup.BuiltIn != null ? MissionGroup.BuiltIn.GetPreview(GamemodeMissionBuilder.WarBoxes[n].Base, token) : UniTask.FromResult<Sprite>(null);
            }
        }
    }
}
