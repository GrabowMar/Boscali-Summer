using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx.Bootstrap;
using BoscaliSummer.Modules.Maps.Domain;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// One empty mission per map in the game's mission list ("Boscali Summer Maps"), made from the game's own default
    /// mission with the map swapped and the map's own weather (cloud cover, cloud base, wind) written in. It gives a new
    /// map something to fly on before anyone has authored a mission for it, and gives <c>-mission "Korvath Range"</c>
    /// (dedicated servers, sims) something to host. A mission somebody authors on the map keeps its own weather.
    /// A map's game modes ("Marisol Islands - Naval War") are further missions of the same group: the same mission with
    /// the mode's fleets, garrisons, airbase owners and objectives laid over it.
    /// </summary>
    internal sealed class MapMissions
    {
        private readonly Group group;
        private readonly MissionGroup[] groups;

        internal MapMissions()
        {
            group = new Group();
            groups = new MissionGroup[] { group };
        }

        internal IReadOnlyList<MissionGroup> Groups() => MapCatalog.All.Count > 0 ? groups : Array.Empty<MissionGroup>();

        /// <summary>The map's mission by its display name, or null when the name is not one of the mod's maps.</summary>
        internal Mission Launch(string name)
        {
            if (!Find(name, out _, out _)) return null;
            MissionGroup.Init();
            if (MissionSaveLoad.TryLoad(new MissionKey(name, group), out Mission mission, out string error)) return mission;
            Plugin.Logger?.LogWarning("[Maps] Could not load the " + name + " mission: " + error);
            return null;
        }

        /// <summary>The picker label of one of this group's missions: its mode (or FREE FLIGHT), then the map.</summary>
        internal string Label(MissionKey key) =>
            ReferenceEquals(key.Group, group) && Find(key.Key, out MapEntry entry, out MapMode mode)
                ? (mode != null ? mode.name.ToUpperInvariant() : "FREE FLIGHT") + " / " + entry.Manifest.displayName : null;

        private static string Title(MapEntry entry, MapMode mode) => entry.Manifest.displayName + " - " + mode.name;

        /// <summary>The map of a mission name, and its mode when the name is one of the map's modes.</summary>
        private static bool Find(string name, out MapEntry entry, out MapMode mode)
        {
            entry = null;
            mode = null;
            foreach (MapEntry e in MapCatalog.All)
            {
                MapMode m = e.Modes.FirstOrDefault(x => string.Equals(Title(e, x), name, StringComparison.OrdinalIgnoreCase));
                if (m == null && !string.Equals(e.Manifest.displayName, name, StringComparison.OrdinalIgnoreCase)) continue;
                entry = e;
                mode = m;
                return true;
            }
            return false;
        }

        private sealed class Group : MissionGroup
        {
            internal Group() : base("Boscali Summer Maps") { }

            public override IEnumerable<MissionKey> GetMissions() =>
                MapCatalog.All.SelectMany(e => new[] { e.Manifest.displayName }.Concat(e.Modes.Select(m => Title(e, m)))).Select(name => new MissionKey(name, this));

            public override bool TryGetJson(MissionKey key, out string json)
            {
                json = null;
                if (!ReferenceEquals(key.Group, this) || !Find(key.Key, out MapEntry entry, out MapMode mode)) return false;
                try
                {
                    JObject root;
                    using (var reader = new JsonTextReader(new StringReader(MissionGroup.Default.ReadFirst() ?? "")) { DateParseHandling = DateParseHandling.None })
                        root = JObject.Load(reader);
                    if (!(root["MapKey"] is JObject map)) root["MapKey"] = map = new JObject();
                    map["Type"] = 1; // GameWorldPrefab
                    map["Path"] = entry.Manifest.prefabName;
                    if (root["missionSettings"] is JObject settings)
                        settings["description"] = string.IsNullOrEmpty(entry.Manifest.description)
                            ? entry.Manifest.displayName + ". An empty mission; build your own in the mission editor." : entry.Manifest.description;
                    // The map's own weather: these three saved fields become LevelInfo.conditions, cloudHeight and windSpeed.
                    MapManifest.MissionWeather weather = entry.Manifest.mission;
                    if (weather != null)
                    {
                        if (!(root["environment"] is JObject environment)) root["environment"] = environment = new JObject();
                        environment["weatherIntensity"] = Mathf.Clamp01(weather.conditions);
                        environment["cloudAltitude"] = weather.cloudBaseM;
                        environment["windSpeed"] = ((float?)environment["windSpeed"] ?? 0f) * weather.windScale;
                    }
                    // A map with no sea: the default mission's fleets would sit under the sand.
                    if (entry.Manifest.stats != null && entry.Manifest.stats.landPercent >= 99.9f) root["ships"] = new JArray();
                    if (mode != null)
                    {
                        // The mode's lists replace the default mission's; with BOTE (player-sailed ships) its extras are added.
                        if (mode.mission != null) root.Merge(mode.mission, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
                        if (mode.bote != null && Chainloader.PluginInfos.ContainsKey("com.minec.bote"))
                            root.Merge(mode.bote, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Concat });
                    }
                    Stand(root, entry.Manifest, mode?.sides);
                    json = root.ToString(Formatting.None);
                    return true;
                }
                catch (Exception error) when (error is JsonException || error is InvalidOperationException)
                {
                    Plugin.Logger?.LogWarning("[Maps] Could not make the " + key.Key + " mission: " + error.Message);
                    return false;
                }
            }

            /// <summary>The map's own structures as the mission's buildings and scenery: red is Primeva's, blue Boscali's,
            /// the rest stand neutral; an airfield's buildings are tied to its strip. A mode says who owns red and blue in it.</summary>
            private static void Stand(JObject root, MapManifest manifest, Dictionary<string, string> sides)
            {
                if (manifest.structures == null || manifest.structures.Length == 0) return;
                if (!(root["buildings"] is JArray buildings)) root["buildings"] = buildings = new JArray();
                if (!(root["scenery"] is JArray scenery)) root["scenery"] = scenery = new JArray();
                for (int n = 0; n < manifest.structures.Length; n++)
                {
                    MapManifest.Structure s = manifest.structures[n];
                    if (s == null || string.IsNullOrEmpty(s.type)) continue;
                    Quaternion turn = Quaternion.Euler(0f, s.yaw, 0f);
                    // "loose": the owner of everything that belongs to no airfield (a mode with no ground war stands its guns down)
                    string owner = sides != null && s.airbase < 0 && sides.TryGetValue("loose", out string free) ? free :
                        sides != null && sides.TryGetValue(s.side ?? "", out string named) ? named : s.side == "red" ? "Primeva" : s.side == "blue" ? "Boscali" : "";
                    var unit = new JObject
                    {
                        ["type"] = s.type,
                        ["faction"] = !string.IsNullOrEmpty(owner) ? owner : s.scenery ? "" : "Neutral",
                        ["UniqueName"] = "bs_" + s.type + "_" + n,
                        ["globalPosition"] = new JObject { ["x"] = s.x, ["y"] = s.y, ["z"] = s.z },
                        ["rotation"] = new JObject { ["x"] = turn.x, ["y"] = turn.y, ["z"] = turn.z, ["w"] = turn.w },
                        ["CaptureStrength"] = new JObject { ["IsOverride"] = false, ["Value"] = 0f },
                        ["CaptureDefense"] = new JObject { ["IsOverride"] = false, ["Value"] = 25f },
                    };
                    if (s.scenery)
                    {
                        unit["indestructible"] = false;
                        scenery.Add(unit);
                        continue;
                    }
                    unit["capturable"] = false;
                    string field = s.airbase >= 0 && s.airbase < manifest.strips.Length ? manifest.displayName + " Strip " + (s.airbase + 1) : "";
                    unit["Airbase"] = field;
                    // An airfield's buildings are whoever's the mission says the airfield is.
                    JToken strip = field == "" ? null : (root["airbases"] as JArray)?.FirstOrDefault(a => (string)a["UniqueName"] == field);
                    if (strip != null) unit["faction"] = (string)strip["faction"] == "None" || string.IsNullOrEmpty((string)strip["faction"]) ? "Neutral" : (string)strip["faction"];
                    unit["factoryOptions"] = new JObject { ["productionType"] = "", ["productionTime"] = 900f };
                    buildings.Add(unit);
                }
            }

            public override UniTask<Sprite> GetPreview(string key, CancellationToken token = default) =>
                UniTask.FromResult(Find(key, out MapEntry entry, out _) ? entry.Details.MapImage : null);
        }
    }
}
