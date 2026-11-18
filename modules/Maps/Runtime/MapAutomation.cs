using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// Dev tooling for the map generator, called from sim scenarios (the "call" op) and never by the game.
    /// <see cref="Catalogue"/> writes what the generator needs to know about the game's own content: every building and
    /// scenery definition with its size and role, every airbase of the shipped maps with its runway, and the buildings
    /// the shipped missions stand on those maps (so a generated airfield can be laid out like a shipped one).
    /// </summary>
    public static class MapAutomation
    {
        /// <summary>Logs the picker's tabs and every mission of the mod's one tab by its label, for a sim to match.</summary>
        public static Dictionary<string, object> Picker(Dictionary<string, object> args)
        {
            MissionGroup.Init();
            MissionGroup[] tabs = MissionGroup.MissionSelectGroups;
            string[] ours = tabs.Where(g => g != null && g.Name.StartsWith("Boscali Summer", StringComparison.Ordinal)).Select(g => g.Name).ToArray();
            string[] listed = tabs.Where(g => g != null && g.Name == "Boscali Summer").SelectMany(g => g.GetMissions()).Select(k => k.Name).ToArray();
            Plugin.Logger?.LogInfo("[Maps] Picker tabs of the mod: " + string.Join(" | ", ours) + ". Missions: " + string.Join(" | ", listed));
            foreach (MissionKey key in tabs.Where(g => g != null && g.Name == "Boscali Summer").SelectMany(g => g.GetMissions()))
            {
                bool loaded = key.TryLoad(out Mission mission, out string error);
                var said = loaded && mission?.LoadErrors != null ? mission.LoadErrors.Warnings.Concat(mission.LoadErrors.Errors).ToList() : new List<string> { error };
                said.RemoveAll(string.IsNullOrEmpty);
                if (said.Count > 0) Plugin.Logger?.LogWarning("[Maps] Load warnings of '" + key.Name + "' (" + said.Count + "): " + string.Join(" ;; ", said));
            }
            return new Dictionary<string, object> { ["tabs"] = ours.Length, ["missions"] = listed.Length, ["list"] = string.Join(" | ", listed) };
        }

        /// <summary>Checks a hosted game mode. Args: <c>ships</c> and <c>vehicles</c>, the least number of each that must
        /// be in the world; <c>airbases</c>, unique name to owning faction ("" for nobody). Logs "Mode check passed" or an error.</summary>
        public static Dictionary<string, object> Mode(Dictionary<string, object> args)
        {
            int Least(string key) => args != null && args.TryGetValue(key, out object v) && v != null ? Convert.ToInt32(v) : 0;
            int ships = UnityEngine.Object.FindObjectsOfType<Ship>().Length, vehicles = UnityEngine.Object.FindObjectsOfType<GroundVehicle>().Length, bases = 0;
            var wrong = new List<string>();
            if (ships < Least("ships")) wrong.Add(ships + " ships of " + Least("ships"));
            if (vehicles < Least("vehicles")) wrong.Add(vehicles + " vehicles of " + Least("vehicles"));
            if (args != null && args.TryGetValue("airbases", out object given) && given != null)
                foreach (KeyValuePair<string, JToken> want in given as JObject ?? JObject.FromObject(given))
                {
                    bases++;
                    if (!FactionRegistry.airbaseLookup.TryGetValue(want.Key, out Airbase airbase) || airbase == null) { wrong.Add(want.Key + " is missing"); continue; }
                    string owner = airbase.CurrentHQ != null && airbase.CurrentHQ.faction != null ? airbase.CurrentHQ.faction.factionName : "";
                    if (owner != (string)want.Value) wrong.Add(want.Key + " is held by '" + owner + "', not '" + (string)want.Value + "'");
                }
            LoadErrors said = MissionManager.CurrentMission?.LoadErrors;
            if (said != null) wrong.AddRange(said.Warnings.Concat(said.Errors).Concat(said.Exceptions.Select(e => e.ToString())).Select(w => "load warning: " + w));
            if (wrong.Count == 0) Plugin.Logger?.LogInfo("[Maps] Mode check passed: " + ships + " ships, " + vehicles + " vehicles, " + bases + " airbases.");
            else Plugin.Logger?.LogError("[Maps] Mode check failed: " + string.Join("; ", wrong));
            return new Dictionary<string, object> { ["ok"] = wrong.Count == 0, ["ships"] = ships, ["vehicles"] = vehicles, ["wrong"] = string.Join("; ", wrong) };
        }

        /// <summary>Args: <c>path</c>, the JSON file to write (default: bs_map_catalogue.json in the temp folder).</summary>
        public static Dictionary<string, object> Catalogue(Dictionary<string, object> args)
        {
            string path = args != null && args.TryGetValue("path", out object given) && given != null ? given.ToString() : Path.Combine(Path.GetTempPath(), "bs_map_catalogue.json");
            var root = new JObject();
            Encyclopedia book = Encyclopedia.i;
            var defs = new JArray();
            if (book != null)
                foreach (UnitDefinition def in book.buildings.Cast<UnitDefinition>().Concat(book.scenery))
                {
                    if (def == null) continue;
                    GameObject prefab = def.unitPrefab;
                    defs.Add(new JObject
                    {
                        ["key"] = def.jsonKey, ["name"] = def.unitName, ["kind"] = def is BuildingDefinition b ? b.buildingType.ToString() : "SCENERY",
                        ["length"] = def.length, ["width"] = def.width, ["height"] = def.height,
                        ["parts"] = prefab == null ? "" : string.Join(",", prefab.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null)
                            .Select(c => c.GetType().Name).Distinct().Where(n => n == "Hangar" || n == "Factory" || n == "VehicleDepot" || n == "Radar" || n == "Turret" || n == "Building")),
                    });
                }
            root["definitions"] = defs;

            var bases = new JArray();
            MapSettingsManager manager = MapSettingsManager.i;
            if (manager != null)
                foreach (MapSettings map in manager.Maps.Where(m => m != null && m.Prefab != null && !MapGround.IsOurs(m.Prefab)).Select(m => m.Prefab))
                    foreach (Airbase airbase in map.GetComponentsInChildren<Airbase>(true))
                    {
                        var runways = new JArray();
                        foreach (Airbase.Runway runway in airbase.runways ?? new Airbase.Runway[0])
                            if (runway != null && runway.Start != null && runway.End != null)
                                runways.Add(new JArray(runway.Start.position.x, runway.Start.position.y, runway.Start.position.z, runway.End.position.x, runway.End.position.y, runway.End.position.z));
                        SavedAirbase saved = airbase.SavedAirbase;
                        bases.Add(new JObject
                        {
                            ["map"] = map.name, ["object"] = airbase.name, ["unique"] = saved != null ? saved.UniqueName : "", ["display"] = saved != null ? saved.DisplayName : "",
                            ["position"] = new JArray(airbase.transform.position.x, airbase.transform.position.y, airbase.transform.position.z),
                            ["center"] = saved != null ? new JArray(saved.Center.x, saved.Center.y, saved.Center.z) : new JArray(), ["runways"] = runways,
                        });
                    }
            root["airbases"] = bases;

            var missions = new JArray();
            MissionGroup.Init();
            foreach (MissionGroup group in new MissionGroup[] { MissionGroup.BuiltIn, MissionGroup.Default })
                foreach (MissionKey key in group.GetMissions())
                {
                    try
                    {
                        if (!group.TryGetJson(key, out string json) || string.IsNullOrEmpty(json)) continue;
                        JObject mission;
                        using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None }) mission = JObject.Load(reader);
                        missions.Add(new JObject { ["name"] = key.Key, ["group"] = group.Name, ["map"] = mission["MapKey"], ["airbases"] = mission["airbases"], ["buildings"] = mission["buildings"] });
                    }
                    catch (Exception error) when (error is JsonException || error is InvalidOperationException || error is IOException)
                    {
                        missions.Add(new JObject { ["name"] = key.Key, ["error"] = error.Message });
                    }
                }
            root["missions"] = missions;
            File.WriteAllText(path, root.ToString(Formatting.None));
            Plugin.Logger?.LogInfo("[Maps] Catalogue: " + defs.Count + " definitions, " + bases.Count + " airbases, " + missions.Count + " missions -> " + path);
            return new Dictionary<string, object> { ["ok"] = true, ["path"] = path, ["definitions"] = defs.Count, ["airbases"] = bases.Count, ["missions"] = missions.Count };
        }
    }
}
