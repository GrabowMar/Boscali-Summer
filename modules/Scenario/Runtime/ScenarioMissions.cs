using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;
using BoscaliSummer.Core.Scenario;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Scenario.Runtime
{
    /// <summary>
    /// Puts the scripted missions in the game's own mission picker: one picker group per script <c>group</c>, one mission per script the pilot may start. A mission
    /// is built when it is asked for, from the installed copy of its base mission, so no mission file is shipped and none of the pilot's files is touched.
    /// </summary>
    internal sealed class ScenarioMissions
    {
        private readonly ScenarioLibrary library;
        private readonly ManualLogSource log;
        private readonly List<MissionGroup> groups = new List<MissionGroup>();
        private readonly Dictionary<string, Built> built = new Dictionary<string, Built>(StringComparer.Ordinal);
        private int revision = -1;

        private struct Built
        {
            public int BaseLength;
            public string Json;
        }

        public ScenarioMissions(ScenarioLibrary library, ManualLogSource logger)
        {
            this.library = library;
            log = logger;
        }

        /// <summary>The picker groups as of now. The picker asks often; scripts are only re-read after a file changed.</summary>
        public IReadOnlyList<MissionGroup> Groups()
        {
            library.Refresh();
            if (revision == library.Revision) return groups;
            revision = library.Revision;
            built.Clear();
            // A group object the picker already holds stays the same object for as long as some script names it.
            var wanted = new List<string>();
            foreach (ScenarioDef script in library.Scripts)
                if (!wanted.Contains(script.Group)) wanted.Add(script.Group);
            groups.RemoveAll(group => !wanted.Contains(group.Name));
            foreach (string name in wanted)
                if (!groups.Exists(group => group.Name == name)) groups.Add(new Group(name, this));
            return groups;
        }

        /// <summary>
        /// The script of a mission, or null for a mission without one: by the marker in its description (which is all a multiplayer client has), else by the
        /// picker entry it was loaded from.
        /// </summary>
        public ScenarioDef ScriptOf(Mission mission)
        {
            if (mission == null) return null;
            ScenarioDef marked = library.Find(ScenarioDef.IdOf(mission.missionSettings?.description));
            if (marked != null || !mission.LoadKey.HasValue) return marked;
            MissionKey key = mission.LoadKey.Value;
            return key.Group != null && groups.Contains(key.Group) ? Script(key.Group.Name, key.Key) : null;
        }

        /// <summary>Loads a scripted mission by script id, the way the picker would: for the game's <c>-mission scenario:&lt;id&gt;</c> launch argument.</summary>
        public Mission Launch(string id)
        {
            // A launch argument is read before any menu has made the game build its mission groups.
            MissionGroup.Init();
            ScenarioDef script = library.Find(id);
            if (script == null)
            {
                log?.LogWarning("[Scenario] -mission scenario:" + id + ": there is no script with that id.");
                return null;
            }
            foreach (MissionGroup group in Groups())
            {
                if (group.Name != script.Group) continue;
                if (MissionSaveLoad.TryLoad(new MissionKey(script.Title, group), out Mission mission, out string error)) return mission;
                log?.LogWarning("[Scenario] '" + id + "' could not be loaded: " + error);
            }
            return null;
        }

        private void Fill(string group, List<MissionKey> keys, MissionGroup owner)
        {
            keys.Clear();
            var listed = new List<ScenarioDef>();
            foreach (ScenarioDef script in library.Scripts)
                if (script.Group == group && library.Progress.Unlocked(script, library.Scripts)) listed.Add(script);
            listed.Sort((a, b) =>
            {
                int byCampaign = string.CompareOrdinal(a.Campaign ?? "", b.Campaign ?? "");
                return byCampaign != 0 ? byCampaign : a.Chapter != b.Chapter ? a.Chapter.CompareTo(b.Chapter) : string.CompareOrdinal(a.Title, b.Title);
            });
            foreach (ScenarioDef script in listed) keys.Add(new MissionKey(script.Title, owner));
        }

        private ScenarioDef Script(string group, string title)
        {
            foreach (ScenarioDef script in library.Scripts)
                if (script.Group == group && script.Title == title) return script;
            return null;
        }

        private bool TryBuild(string group, string title, out string json)
        {
            json = null;
            ScenarioDef script = Script(group, title);
            if (script == null || !TryBase(script.Base, out string baseJson))
            {
                if (script != null) log?.LogWarning("[Scenario] '" + script.Id + "': the mission '" + script.Base + "' it copies is not installed.");
                return false;
            }
            if (built.TryGetValue(script.Id, out Built cached) && cached.BaseLength == baseJson.Length)
            {
                json = cached.Json;
                return true;
            }
            try
            {
                var errors = new List<string>(script.Errors);
                int known = errors.Count;
                json = MissionPatcher.Apply(baseJson, script, errors);
                for (int i = known; i < errors.Count; i++) log?.LogWarning("[Scenario] " + errors[i]);
                built[script.Id] = new Built { BaseLength = baseJson.Length, Json = json };
                return true;
            }
            catch (InvalidOperationException error) { log?.LogWarning("[Scenario] '" + script.Id + "' could not be built: " + error.Message); }
            catch (JsonException error) { log?.LogWarning("[Scenario] '" + script.Id + "': the mission '" + script.Base + "' could not be read: " + error.Message); }
            return false;
        }

        /// <summary>The base mission by name: one that ships with the game first, then one of the pilot's own.</summary>
        private static bool TryBase(string name, out string json)
        {
            foreach (MissionGroup source in new MissionGroup[] { MissionGroup.BuiltIn, MissionGroup.Default, MissionGroup.Tutorial, MissionGroup.User })
            {
                if (source == null || !source.ContainsMission(name)) continue;
                if (source.TryGetJson(new MissionKey(name, source), out json) && !string.IsNullOrEmpty(json)) return true;
            }
            json = null;
            return false;
        }

        private UniTask<Sprite> Preview(string group, string title, CancellationToken token)
        {
            ScenarioDef script = Script(group, title);
            if (script != null)
                foreach (MissionGroup source in new MissionGroup[] { MissionGroup.BuiltIn, MissionGroup.Default, MissionGroup.Tutorial, MissionGroup.User })
                    if (source != null && source.ContainsMission(script.Base)) return source.GetPreview(script.Base, token);
            return UniTask.FromResult<Sprite>(null);
        }

        private sealed class Group : MissionGroup
        {
            private readonly ScenarioMissions owner;
            private readonly List<MissionKey> keys = new List<MissionKey>();

            public Group(string name, ScenarioMissions owner) : base(name)
            {
                this.owner = owner;
            }

            public override IEnumerable<MissionKey> GetMissions()
            {
                owner.Fill(Name, keys, this);
                // A copy: the picker's "All" group reads this lazily and may ask again before it is done.
                return keys.ToArray();
            }

            public override bool TryGetJson(MissionKey key, out string json)
            {
                json = null;
                return ReferenceEquals(key.Group, this) && owner.TryBuild(Name, key.Key, out json);
            }

            public override UniTask<Sprite> GetPreview(string key, CancellationToken token = default(CancellationToken)) =>
                owner.Preview(Name, key, token);
        }
    }
}
