using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
            if (!MapCatalog.All.Any(e => string.Equals(e.Manifest.displayName, name, StringComparison.OrdinalIgnoreCase))) return null;
            MissionGroup.Init();
            if (MissionSaveLoad.TryLoad(new MissionKey(name, group), out Mission mission, out string error)) return mission;
            Plugin.Logger?.LogWarning("[Maps] Could not load the " + name + " mission: " + error);
            return null;
        }

        private sealed class Group : MissionGroup
        {
            internal Group() : base("Boscali Summer Maps") { }

            public override IEnumerable<MissionKey> GetMissions() => MapCatalog.All.Select(e => new MissionKey(e.Manifest.displayName, this));

            public override bool TryGetJson(MissionKey key, out string json)
            {
                json = null;
                MapEntry entry = MapCatalog.All.FirstOrDefault(e => string.Equals(e.Manifest.displayName, key.Key, StringComparison.OrdinalIgnoreCase));
                if (entry == null || !ReferenceEquals(key.Group, this)) return false;
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
                    json = root.ToString(Formatting.None);
                    return true;
                }
                catch (Exception error) when (error is JsonException || error is InvalidOperationException)
                {
                    Plugin.Logger?.LogWarning("[Maps] Could not make the " + key.Key + " mission: " + error.Message);
                    return false;
                }
            }

            public override UniTask<Sprite> GetPreview(string key, CancellationToken token = default) =>
                UniTask.FromResult(MapCatalog.All.FirstOrDefault(e => string.Equals(e.Manifest.displayName, key, StringComparison.OrdinalIgnoreCase))?.Details.MapImage);
        }
    }
}
