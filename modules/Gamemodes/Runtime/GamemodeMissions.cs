using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;
using BoscaliSummer.Modules.Gamemodes.Domain;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>Additional native picker entries, built from the player's installed mission without editing it.</summary>
    internal sealed class GamemodeMissions
    {
        private readonly Group group;
        private readonly MissionGroup[] groups;
        private readonly ManualLogSource log;
        private readonly Dictionary<GameModeKind, string> built = new Dictionary<GameModeKind, string>();
        private string cachedBase;

        internal GamemodeMissions(ManualLogSource logger)
        {
            log = logger;
            group = new Group(this);
            groups = new MissionGroup[] { group };
        }

        internal IReadOnlyList<MissionGroup> Groups() => groups;

        internal Mission Launch(string id)
        {
            GameModeKind mode = GamemodeMissionBuilder.FromId(id);
            if (mode == GameModeKind.None) return null;
            MissionGroup.Init();
            if (MissionSaveLoad.TryLoad(new MissionKey(GamemodeMissionBuilder.Title(mode), group), out Mission mission, out string error))
                return mission;
            log?.LogWarning("[Gamemodes] Could not load " + id + ": " + error);
            return null;
        }

        private bool TryBuild(GameModeKind mode, out string json)
        {
            json = null;
            if (mode == GameModeKind.None) return false;
            MissionGroup source = MissionGroup.BuiltIn;
            if (source == null || !source.TryGetJson(new MissionKey(GamemodeMissionBuilder.BaseMission, source), out string original) ||
                string.IsNullOrEmpty(original))
            {
                log?.LogWarning("[Gamemodes] The installed Escalation mission is unavailable.");
                return false;
            }
            if (!string.Equals(cachedBase, original, StringComparison.Ordinal))
            {
                cachedBase = original;
                built.Clear();
            }
            if (built.TryGetValue(mode, out json)) return true;
            try
            {
                json = GamemodeMissionBuilder.Apply(original, mode);
                built[mode] = json;
                return true;
            }
            catch (Exception error) when (error is JsonException || error is InvalidOperationException || error is OverflowException)
            {
                log?.LogWarning("[Gamemodes] Could not build " + GamemodeMissionBuilder.Title(mode) + ": " + error.Message);
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
                keys = new[] { new MissionKey("WarBox", this), new MissionKey("Fuzzball", this), new MissionKey("Heavyweight", this) };
            }

            public override IEnumerable<MissionKey> GetMissions() => keys;

            public override bool TryGetJson(MissionKey key, out string json)
            {
                json = null;
                return ReferenceEquals(key.Group, this) && owner.TryBuild(GamemodeMissionBuilder.FromId(key.Key), out json);
            }

            public override UniTask<Sprite> GetPreview(string key, CancellationToken token = default) =>
                MissionGroup.BuiltIn != null ? MissionGroup.BuiltIn.GetPreview(GamemodeMissionBuilder.BaseMission, token) : UniTask.FromResult<Sprite>(null);
        }
    }
}
