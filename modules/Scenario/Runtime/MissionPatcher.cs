using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Core.Scenario;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BoscaliSummer.Modules.Scenario.Runtime
{
    /// <summary>
    /// Builds a scripted mission: a copy of the base mission's JSON with the script's world statements written in as the game's own units, objectives and outcomes
    /// (the same shapes the mission editor saves), so the game runs and networks them like any mission. Everything the script does not name is copied value for
    /// value, fields this code has never heard of included. A statement that names something the mission does not have is reported and left out.
    /// </summary>
    internal static class MissionPatcher
    {
        private const string StartName = "Mission Start";
        private static readonly string[] UnitLists = { "aircraft", "vehicles", "ships", "buildings", "scenery", "containers", "missiles", "pilots" };

        /// <summary>Reads <paramref name="scriptText"/> and builds: the one call a tool or the patch probe needs.</summary>
        internal static string Apply(string baseJson, string source, string scriptText, List<string> errors)
        {
            ScenarioDef script = ScenarioParser.Parse(source, scriptText);
            errors.AddRange(script.Errors);
            return Apply(baseJson, script, errors);
        }

        /// <summary>The scripted mission's JSON. Problems go to <paramref name="errors"/>; throws when <paramref name="baseJson"/> is not a mission at all.</summary>
        internal static string Apply(string baseJson, ScenarioDef script, List<string> errors)
        {
            JObject root;
            // Dates stay text: a date-shaped string must come out exactly as it went in.
            using (var reader = new JsonTextReader(new StringReader(baseJson ?? "")) { DateParseHandling = DateParseHandling.None })
                root = JObject.Load(reader);
            if (!(root["missionSettings"] is JObject settings) || settings["description"]?.Type != JTokenType.String)
                throw new InvalidOperationException("The base mission has no valid description.");

            if (root["environment"] is JObject environment)
            {
                if (script.Time >= 0f) environment["timeOfDay"] = script.Time;
                if (script.Weather >= 0f) environment["weatherIntensity"] = script.Weather;
            }

            if (root["factions"] is JArray factions)
                foreach (JToken token in factions)
                    if (token is JObject faction)
                        foreach (ScenarioSide locked in script.Locked)
                            if ((string)faction["factionName"] == Faction(locked)) faction["preventJoin"] = true;

            var build = new Build(root, script, errors);
            // PMC and Pirates are no factions of the base game: a script that uses one brings it, modelled on the mission's first faction but with nothing
            // in store. Nobody joins the pirates.
            foreach (ScenarioSide extra in new[] { ScenarioSide.Pmc, ScenarioSide.Pirates })
            {
                if (!script.Uses(extra)) continue;
                if (!(root["factions"] is JArray all) || all.Count == 0 || !(all[0] is JObject first))
                {
                    build.Error(1, "the base mission has no factions to model a " + SideNames.Of(extra) + " side on");
                    continue;
                }
                bool present = false;
                foreach (JToken token in all) present |= token is JObject named && (string)named["factionName"] == SideNames.Of(extra);
                if (present) continue;
                var side = (JObject)first.DeepClone();
                side["factionName"] = SideNames.Of(extra);
                side["preventJoin"] = extra == ScenarioSide.Pirates || script.Locked.Contains(extra);
                side["supplies"] = new JArray();
                all.Add(side);
            }
            bool world = script.Units.Count + script.Copies.Count + script.Objectives.Count + script.Hold.Count + script.Begin.Count > 0 || script.Ending >= 0f;
            if (root["objectives"] is JArray objectives && root["outcomes"] is JArray outcomes) build.World(objectives, outcomes);
            else if (world || script.Beats.Count > 0)
                build.Error(1, "the base mission is saved in an old format; open it in the mission editor and save it again before scripting it");

            string about = script.About.Length > 0 ? script.About : settings["description"].Value<string>();
            if (errors.Count > 0) about += "\n\nSCRIPT PROBLEMS: " + errors.Count + ". They are listed in BepInEx/LogOutput.log.";
            // The marker goes last: it is how every peer, a multiplayer client included, knows which script runs.
            settings["description"] = about + "\n\n" + ScenarioDef.Marker(script.Id);
            return root.ToString(Formatting.None);
        }

        private static string Faction(ScenarioSide side) => SideNames.Of(side);

        private sealed class Build
        {
            private readonly JObject root;
            private readonly ScenarioDef script;
            private readonly List<string> errors;
            private readonly Dictionary<string, JObject> units = new Dictionary<string, JObject>(StringComparer.Ordinal);
            private readonly Dictionary<string, JObject> objectives = new Dictionary<string, JObject>(StringComparer.Ordinal);
            private readonly Dictionary<string, JObject> outcomes = new Dictionary<string, JObject>(StringComparer.Ordinal);
            private readonly HashSet<string> airbases = new HashSet<string>(StringComparer.Ordinal);

            public Build(JObject root, ScenarioDef script, List<string> errors)
            {
                this.root = root;
                this.script = script;
                this.errors = errors;
            }

            public void Error(int line, string text) => errors.Add((script.Source ?? "script") + ":" + line + ": " + text);

            public void World(JArray objectiveList, JArray outcomeList)
            {
                foreach (string list in UnitLists) Index(root[list] as JArray, units);
                Index(objectiveList, objectives);
                Index(outcomeList, outcomes);
                if (root["airbases"] is JArray bases)
                    foreach (JToken token in bases)
                        if (token is JObject airbase && airbase["UniqueName"]?.Type == JTokenType.String) airbases.Add((string)airbase["UniqueName"]);

                foreach (ScenarioUnit unit in script.Units) AddUnit(unit);
                foreach (ScenarioCopy copy in script.Copies) AddCopy(copy);

                var made = new List<KeyValuePair<ScenarioObjective, JObject>>();
                foreach (ScenarioObjective objective in script.Objectives)
                {
                    JObject target;
                    if (objective.Kind == ObjectiveKind.Existing)
                    {
                        if (!objectives.TryGetValue(objective.Id, out target))
                        {
                            Error(objective.Line, "the base mission has no objective '" + objective.Id + "'");
                            continue;
                        }
                    }
                    else
                    {
                        if (objectives.ContainsKey(objective.Id))
                        {
                            Error(objective.Line, "the base mission already has an objective '" + objective.Id + "'; use 'on' to add to it");
                            continue;
                        }
                        target = Objective(objective);
                        objectiveList.Add(target);
                        objectives[objective.Id] = target;
                    }
                    made.Add(new KeyValuePair<ScenarioObjective, JObject>(objective, target));
                }

                // Second pass: an outcome may name an objective written further down the script.
                foreach (KeyValuePair<ScenarioObjective, JObject> pair in made)
                {
                    ScenarioObjective objective = pair.Key;
                    if (objective.Kind == ObjectiveKind.After)
                        pair.Value["targetObjectives"] = Known(objective.Targets, objectives.ContainsKey, objective.Line, "objective");
                    if (!(pair.Value["Outcomes"] is JArray list)) pair.Value["Outcomes"] = list = new JArray();
                    for (int i = 0; i < objective.Then.Count; i++)
                    {
                        string name = "sc:" + objective.Id + ":" + (i + 1);
                        while (outcomes.ContainsKey(name)) name += "+";
                        JObject outcome = Outcome(objective.Then[i], name);
                        outcomeList.Add(outcome);
                        outcomes[name] = outcome;
                        list.Add(name);
                    }
                }

                Starts(outcomeList);
                if (script.Ending >= 0f)
                    foreach (JObject outcome in outcomes.Values)
                        if ((string)outcome["Type"] == "EndGame") outcome["endDelay"] = script.Ending;
                foreach (ScenarioBeat beat in script.Beats)
                    if ((beat.Trigger == TriggerKind.Started || beat.Trigger == TriggerKind.Done) && !objectives.ContainsKey(beat.Arg))
                        Error(beat.Line, "beat '" + beat.Id + "' waits for an objective the mission does not have: '" + beat.Arg + "'");
            }

            private static void Index(JArray list, Dictionary<string, JObject> into)
            {
                if (list == null) return;
                foreach (JToken token in list)
                    if (token is JObject item && item["UniqueName"]?.Type == JTokenType.String) into[(string)item["UniqueName"]] = item;
            }

            /// <summary>
            /// Copies every unit a spawn outcome of the base mission brings in, under new names. No outcome names the copies, so the game puts them in the
            /// world when the mission starts; the outcome's own units still arrive when the mission says so.
            /// </summary>
            private void AddCopy(ScenarioCopy copy)
            {
                if (!outcomes.TryGetValue(copy.Outcome, out JObject outcome) || (string)outcome["Type"] != "SpawnUnit" || !(outcome["UnitsToSpawn"] is JArray names))
                {
                    Error(copy.Line, "the base mission has no spawn outcome '" + copy.Outcome + "'");
                    return;
                }
                int number = 0;
                foreach (JToken token in names)
                {
                    if (token.Type != JTokenType.String || !units.TryGetValue((string)token, out JObject like)) continue;
                    if (Array.IndexOf(copy.Without, (string)like["type"]) >= 0) continue;
                    string name = copy.Prefix + "_" + ++number;
                    if (units.ContainsKey(name)) { Error(copy.Line, "the mission already has a unit called '" + name + "'"); continue; }
                    var made = (JObject)like.DeepClone();
                    made["UniqueName"] = name;
                    ((JArray)like.Parent).Add(made);
                    units[name] = made;
                }
                if (number == 0) Error(copy.Line, "'" + copy.Outcome + "' brings in no unit that can be copied");
            }

            private void AddUnit(ScenarioUnit unit)
            {
                if (units.ContainsKey(unit.Name)) { Error(unit.Line, "the mission already has a unit called '" + unit.Name + "'"); return; }
                JArray home;
                string list;
                JObject copy;
                if (unit.Kind != null)
                {
                    // A unit made from nothing, with the fields the mission editor saves for its kind.
                    list = unit.Kind == "aircraft" ? "aircraft" : unit.Kind == "ship" ? "ships" : "vehicles";
                    home = root[list] as JArray;
                    if (home == null) { Error(unit.Line, "the base mission has no list of " + list); return; }
                    copy = new JObject
                    {
                        ["type"] = unit.Type, ["faction"] = "", ["UniqueName"] = unit.Name, ["globalPosition"] = null, ["rotation"] = null,
                        ["CaptureStrength"] = new JObject { ["IsOverride"] = false, ["Value"] = 0.0 },
                        ["CaptureDefense"] = new JObject { ["IsOverride"] = false, ["Value"] = 0.0 }
                    };
                    if (unit.Kind == "aircraft")
                    {
                        var selected = new JArray();
                        foreach (string key in unit.Loadout) selected.Add(new JObject { ["Key"] = key });
                        copy["playerControlled"] = false;
                        copy["playerControlledPriority"] = 0;
                        copy["savedLoadout"] = new JObject { ["Selected"] = selected };
                        copy["livery"] = unit.Side == ScenarioSide.Bdf ? 1 : 2;
                        copy["liveryType"] = "Builtin";
                        copy["liveryName"] = "";
                        copy["fuel"] = 1.0;
                        copy["skill"] = 1.0;
                        copy["bravery"] = 0.5;
                        copy["startingSpeed"] = 0.0;
                    }
                    else
                    {
                        var route = new JArray();
                        if (unit.Sails)
                            route.Add(new JObject
                            {
                                ["position"] = new JObject { ["x"] = unit.SailX, ["y"] = unit.Y, ["z"] = unit.SailZ }, ["objective"] = "Unit Spawn"
                            });
                        copy["holdPosition"] = true;
                        copy["skill"] = 0.7;
                        copy["waypoints"] = route;
                    }
                }
                else
                {
                    if (!units.TryGetValue(unit.Like, out JObject like)) { Error(unit.Line, "the base mission has no unit '" + unit.Like + "' to copy"); return; }
                    home = (JArray)like.Parent;
                    list = ((JProperty)home.Parent).Name;
                    if (list != "aircraft" && list != "vehicles" && list != "ships")
                    {
                        Error(unit.Line, "'" + unit.Like + "' is in '" + list + "'; only aircraft, vehicles and ships can be copied");
                        return;
                    }
                    copy = (JObject)like.DeepClone();
                }
                copy["UniqueName"] = unit.Name;
                if (unit.Side != ScenarioSide.Any) copy["faction"] = Faction(unit.Side);
                copy["globalPosition"] = new JObject { ["x"] = unit.X, ["y"] = unit.Y, ["z"] = unit.Z };
                // Compass heading to a rotation about the vertical axis.
                double half = unit.Heading * Math.PI / 360.0;
                copy["rotation"] = new JObject { ["x"] = 0.0, ["y"] = Math.Sin(half), ["z"] = 0.0, ["w"] = Math.Cos(half) };
                if (copy["playerControlled"] != null) copy["playerControlled"] = false;
                if (unit.Speed >= 0f)
                {
                    if (copy["startingSpeed"] != null) copy["startingSpeed"] = unit.Speed;
                    else Error(unit.Line, "speed is for aircraft; '" + unit.Name + "' is in '" + list + "'");
                }
                foreach (KeyValuePair<string, float> set in unit.Sets)
                {
                    JProperty field = null;
                    foreach (JProperty property in copy.Properties())
                        if (string.Equals(property.Name, set.Key, StringComparison.OrdinalIgnoreCase)) { field = property; break; }
                    if (field == null || field.Value.Type != JTokenType.Float && field.Value.Type != JTokenType.Integer)
                        Error(unit.Line, "'" + set.Key + "' is not a number field of '" + unit.Name + "'");
                    else field.Value = set.Value;
                }
                home.Add(copy);
                units[unit.Name] = copy;
            }

            private JObject Objective(ScenarioObjective objective)
            {
                var made = new JObject
                {
                    ["Type"] = Type(objective.Kind),
                    ["UniqueName"] = objective.Id,
                    ["Faction"] = Faction(objective.Side),
                    ["DisplayName"] = objective.Display ?? objective.Id,
                    ["Hidden"] = objective.Display == null,
                    ["Outcomes"] = new JArray()
                };
                switch (objective.Kind)
                {
                    case ObjectiveKind.Wait:
                        made["seconds"] = objective.Seconds;
                        break;
                    case ObjectiveKind.Sortie:
                        made["minimumScore"] = objective.Score;
                        made["additive"] = true;
                        break;
                    case ObjectiveKind.Reach:
                        made["completeOrder"] = "CompleteAll";
                        made["completeSomePercent"] = 0.5;
                        made["completeOnEnterRange"] = true;
                        made["waypoints"] = new JArray(new JObject
                        {
                            ["Position"] = new JObject { ["x"] = objective.X, ["y"] = objective.Y, ["z"] = objective.Z },
                            ["Range"] = objective.Range
                        });
                        break;
                    default:
                        made["completeOrder"] = objective.Need == "any" ? "CompleteAny" : objective.Need == "order" ? "InOrder" :
                            objective.Need == "some" ? "CompleteSome" : "CompleteAll";
                        made["completeSomePercent"] = objective.Percent;
                        if (objective.Kind == ObjectiveKind.Destroy)
                            made["targetUnits"] = Known(objective.Targets, units.ContainsKey, objective.Line, "unit");
                        else if (objective.Kind == ObjectiveKind.Capture)
                            made["targetAirbases"] = Known(objective.Targets, airbases.Contains, objective.Line, "airbase");
                        else made["targetObjectives"] = new JArray();
                        break;
                }
                return made;
            }

            private static string Type(ObjectiveKind kind)
            {
                switch (kind)
                {
                    case ObjectiveKind.Wait: return "WaitSeconds";
                    case ObjectiveKind.Reach: return "ReachWaypoints";
                    case ObjectiveKind.Destroy: return "DestroyUnits";
                    case ObjectiveKind.Capture: return "CaptureAirbase";
                    case ObjectiveKind.Sortie: return "SuccessfulSortie";
                    default: return "CompleteOtherObjective";
                }
            }

            private JObject Outcome(ScenarioThen then, string name)
            {
                switch (then.Kind)
                {
                    case ThenKind.Message:
                        return new JObject
                        {
                            ["Type"] = "ShowMessage", ["UniqueName"] = name, ["Message"] = then.Text,
                            ["PlaySound"] = true, ["ObjectiveFactionOnly"] = then.SideOnly
                        };
                    case ThenKind.Victory:
                        return new JObject { ["Type"] = "EndGame", ["UniqueName"] = name, ["endType"] = "Victory", ["endDelay"] = then.Seconds };
                    case ThenKind.Spawn:
                        return new JObject
                        {
                            ["Type"] = "SpawnUnit", ["UniqueName"] = name,
                            ["UnitsToSpawn"] = Known(then.Names, units.ContainsKey, then.Line, "unit")
                        };
                    case ThenKind.Start:
                        return new JObject
                        {
                            ["Type"] = "StartObjective", ["UniqueName"] = name,
                            ["objectivesToStart"] = Known(then.Names, objectives.ContainsKey, then.Line, "objective")
                        };
                    default:
                        return new JObject
                        {
                            ["Type"] = "StopOrCompleteObjective", ["UniqueName"] = name,
                            ["options"] = then.Kind == ThenKind.Finish ? "Complete" : "Stop",
                            ["objectivesToStart"] = Known(then.Names, objectives.ContainsKey, then.Line, "objective")
                        };
                }
            }

            /// <summary>The names the mission has, as a JSON list; each one it does not have is reported and dropped.</summary>
            private JArray Known(IEnumerable<string> names, Func<string, bool> exists, int line, string what)
            {
                var known = new JArray();
                foreach (string name in names)
                {
                    if (exists(name)) known.Add(name);
                    else Error(line, "the mission has no " + what + " '" + name + "'");
                }
                return known;
            }

            /// <summary><c>hold</c> takes objectives out of what the base mission starts with; <c>begin</c> adds the script's own.</summary>
            private void Starts(JArray outcomeList)
            {
                if (script.Hold.Count == 0 && script.Begin.Count == 0) return;
                if (!objectives.TryGetValue(StartName, out JObject start))
                {
                    Error(1, "the base mission has no '" + StartName + "' objective");
                    return;
                }
                if (!(start["Outcomes"] is JArray startOutcomes)) start["Outcomes"] = startOutcomes = new JArray();
                var held = new HashSet<string>(StringComparer.Ordinal);
                foreach (JToken token in startOutcomes)
                {
                    if (token.Type != JTokenType.String || !outcomes.TryGetValue((string)token, out JObject outcome)) continue;
                    if ((string)outcome["Type"] != "StartObjective" || !(outcome["objectivesToStart"] is JArray started)) continue;
                    for (int i = started.Count - 1; i >= 0; i--)
                    {
                        string name = started[i].Type == JTokenType.String ? (string)started[i] : null;
                        if (name == null || !script.Hold.Contains(name)) continue;
                        started.RemoveAt(i);
                        held.Add(name);
                    }
                }
                foreach (string name in script.Hold)
                    if (!held.Contains(name)) Error(1, "hold: the base mission does not start with an objective '" + name + "'");
                if (script.Begin.Count == 0) return;
                string beginName = "sc:begin";
                while (outcomes.ContainsKey(beginName)) beginName += "+";
                var begin = new JObject
                {
                    ["Type"] = "StartObjective", ["UniqueName"] = beginName,
                    ["objectivesToStart"] = Known(script.Begin, objectives.ContainsKey, 1, "objective")
                };
                outcomeList.Add(begin);
                outcomes[beginName] = begin;
                startOutcomes.Add(beginName);
            }
        }
    }
}
