using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Modules.Gamemodes.Runtime;
using Newtonsoft.Json.Linq;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Verify(string source, bool installed)
{
    var original = JObject.Parse(source);
    foreach (var mode in new[] { GameModeKind.WarBox, GameModeKind.Fuzzball, GameModeKind.Heavyweight })
    {
        var result = JObject.Parse(GamemodeMissionBuilder.Apply(source, mode));
        var settings = (JObject)result["missionSettings"];
        Check(GamemodeRules.DetectMode((string)settings["description"]) == mode, "network marker");
        Check((bool)settings["allowRespawn"] && !(bool)settings["allowEventContent"], "respawn and no seasonal content");
        foreach (string field in new[] { "MapKey", "scenery", "futureField" })
            Check(JToken.DeepEquals(original[field], result[field]), "preserve " + field);
        if (mode == GameModeKind.WarBox)
        {
            foreach (string field in new[] { "nuclearEscalationThreshold", "strategicEscalationThreshold" })
                Check((double)settings[field] == (double)original["missionSettings"][field] * 4, "scale " + field);
            foreach (string field in new[] { "outcomes", "vehicles", "aircraft", "ships" })
                Check(JToken.DeepEquals(original[field], result[field]), "native campaign preserved " + field);
            foreach (JObject before in original["objectives"].Children<JObject>())
            {
                var after = result["objectives"].Children<JObject>().Single(x => (string)x["UniqueName"] == (string)before["UniqueName"]);
                if ((string)before["Type"] == "WaitSeconds")
                    Check((double)after["seconds"] == (double)before["seconds"] * ((double)before["seconds"] >= 300 ? 4 : 1), "strategic delay only");
                else Check(JToken.DeepEquals(before, after), "non-timer objectives preserved");
            }
            foreach (JObject faction in result["factions"].Children<JObject>())
            {
                Check((int)faction["AIAircraftLimit"] == 8 && (int)faction["addAIPerEnemyPlayer"] == 0, "bounded AI wing");
                var before = original["factions"].Children<JObject>().Single(x => (string)x["factionName"] == (string)faction["factionName"]);
                Check((double)faction["startingBalance"] == (double)before["startingBalance"] * 4, "campaign reserve funds");
            }
        }
        else
        {
            foreach (string list in new[] { "aircraft", "vehicles", "ships", "buildings", "containers", "missiles", "pilots" })
                Check(((JArray)result[list]).Count == 0, "no inherited " + list);
            Check(((JArray)result["objectives"]).Count == 1 && (string)result["objectives"][0]["UniqueName"] == "Mission Start", "native start marker only");
            Check(((JArray)result["outcomes"]).Count == 0, "no native win or spawn chains");
            var factions = result["factions"].Children<JObject>().ToArray();
            Check(JToken.DeepEquals(factions[0]["supplies"], factions[1]["supplies"]), "symmetric aircraft stocks");
            foreach (var side in factions)
                Check((int)side["AIAircraftLimit"] == 0 && (int)side["addAIPerEnemyPlayer"] == 0 && (int)side["startingWarheads"] == 0, "no autonomous aircraft or warheads");
            foreach (JObject airbase in result["airbases"].Children<JObject>())
                Check(!(bool)airbase["Capturable"] && !((string)airbase["UniqueName"]).StartsWith("<UNIT_AIRBASE>++"), "no capture or orphan carrier");
            if (installed)
                foreach (string route in new[] { "Maris Airport", "K92 Highway Strip", "South Boscali General Aviation" })
                    Check(result["airbases"].Any(x => (string)x["UniqueName"] == route), "runtime route exists: " + route);
        }
    }
}

const string fixture = """
{
  "JsonVersion":6,"MapKey":{"Path":"Terrain2","FutureMapValue":42},"futureField":{"dateText":"2026-11-16T12:00:00Z","other":[1,2]},
  "missionSettings":{"description":"Base","nuclearEscalationThreshold":625,"strategicEscalationThreshold":1225,"FutureSetting":"preserve"},
  "environment":{"timeOfDay":8,"weatherIntensity":0.4},
  "factions":[{"factionName":"Boscali","startingBalance":1000,"reserveAirframes":1,"extraReservesPerPlayer":1,"supplies":[{"UnitType":"COIN","Count":8}]},{"factionName":"Primeva","startingBalance":1000,"reserveAirframes":1,"extraReservesPerPlayer":1,"supplies":[{"UnitType":"COIN","Count":8}]}],
  "airbases":[{"UniqueName":"Maris Airport","faction":"Boscali"},{"UniqueName":"<UNIT_AIRBASE>++Carrier","faction":"Primeva"}],
  "aircraft":[{"type":"trainer"}],"vehicles":[{"type":"Truck"}],"ships":[{"type":"Carrier"}],"buildings":[{"factoryOptions":{"productionType":"Nuclear Warhead","productionTime":120}}],"containers":[{}],"missiles":[{}],"pilots":[{}],"scenery":[{"type":"Tree"}],
  "objectives":[{"Type":"None","UniqueName":"Mission Start","Outcomes":["Start"]},{"Type":"WaitSeconds","UniqueName":"MessageDelay","seconds":10},{"Type":"WaitSeconds","UniqueName":"FleetDelay","seconds":600}],
  "outcomes":[{"Type":"EndGame","UniqueName":"Victory"},{"Type":"SpawnUnit","UniqueName":"Reinforcements","UnitsToSpawn":["Truck"]}]
}
""";
Verify(fixture, false);
var warbox = JObject.Parse(GamemodeMissionBuilder.Apply(fixture, GameModeKind.WarBox));
Check((double)warbox["buildings"][0]["factoryOptions"]["productionTime"] == 480, "slower nuclear production");
try { GamemodeMissionBuilder.Apply("{}", GameModeKind.Fuzzball); throw new Exception("invalid base accepted"); }
catch (InvalidOperationException) { checks++; }
Check(GamemodeMissionBuilder.FromId("warbox") == GameModeKind.WarBox, "case insensitive launch id");
Check(GamemodeMissionBuilder.FromId("other") == GameModeKind.None, "unknown id rejected");
if (args.Contains("--stdin")) Verify(Console.In.ReadToEnd(), true);
Console.WriteLine($"Gamemode mission construction: {checks} assertions passed.");
