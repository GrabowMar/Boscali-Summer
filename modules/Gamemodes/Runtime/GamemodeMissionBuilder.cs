using System;
using System.IO;
using BoscaliSummer.Modules.Gamemodes.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>Copies the installed Escalation mission without round-tripping away unknown game fields.</summary>
    internal static class GamemodeMissionBuilder
    {
        internal const string BaseMission = "Escalation";

        /// <summary>WarBox is built from the largest installed mission of each shipped map: picker key, launch id, the
        /// mission it copies, the map's name. (The mod's own maps bring their WarBox themselves, see Maps.)</summary>
        internal static readonly (string Key, string Id, string Base, string Map)[] WarBoxes =
        {
            ("WarBox", "warbox", BaseMission, "Heartland"),
            ("WarBox Archipelago", "warbox-archipelago", "Terminal Control", "Ignus Archipelago"),
        };

        /// <summary>The WarBox of a picker key or launch id, or -1.</summary>
        internal static int WarBoxOf(string keyOrId)
        {
            for (int n = 0; n < WarBoxes.Length; n++)
                if (string.Equals(WarBoxes[n].Key, keyOrId, StringComparison.OrdinalIgnoreCase) || string.Equals(WarBoxes[n].Id, keyOrId, StringComparison.OrdinalIgnoreCase)) return n;
            return -1;
        }
        internal static readonly string[] FuzzballAircraft =
            { "COIN", "trainer", "VTOLTrainer1", "BS_Skylark", "CAS1", "SmallFighter1", "Fighter1", "Multirole1" };
        internal static readonly string[] HeavyweightAircraft =
            { "COIN", "trainer", "VTOLTrainer1", "BS_Skylark", "CAS1", "SmallFighter1", "Fighter1", "Multirole1", "QuadVTOL1", "UtilityHelo1" };

        internal static string Title(GameModeKind mode) => mode switch
        {
            GameModeKind.WarBox => "WarBox",
            GameModeKind.Fuzzball => "Fuzzball",
            GameModeKind.Heavyweight => "Heavyweight",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        internal static GameModeKind FromId(string id)
        {
            if (WarBoxOf(id) >= 0) return GameModeKind.WarBox;
            foreach (GameModeKind mode in new[] { GameModeKind.WarBox, GameModeKind.Fuzzball, GameModeKind.Heavyweight })
                if (string.Equals(Title(mode), id, StringComparison.OrdinalIgnoreCase)) return mode;
            return GameModeKind.None;
        }

        internal static string Apply(string baseJson, GameModeKind mode, string baseName = BaseMission)
        {
            if (mode == GameModeKind.None) throw new ArgumentOutOfRangeException(nameof(mode));
            JObject root;
            using (var reader = new JsonTextReader(new StringReader(baseJson ?? "")) { DateParseHandling = DateParseHandling.None })
                root = JObject.Load(reader);
            if (!(root["missionSettings"] is JObject settings) || !(root["factions"] is JArray factions) ||
                !(root["objectives"] is JArray) || !(root["outcomes"] is JArray))
                throw new InvalidOperationException("Gamemodes require the installed Escalation mission in the current objective format.");
            bool boscali = false, primeva = false;
            foreach (JObject faction in factions.Children<JObject>())
            {
                boscali |= (string)faction["factionName"] == "Boscali";
                primeva |= (string)faction["factionName"] == "Primeva";
            }
            if (!boscali || !primeva) throw new InvalidOperationException("The base mission must contain Boscali and Primeva.");

            settings["allowRespawn"] = true;
            settings["allowEventContent"] = false;
            settings["playerMode"] = "SingleAndMultiplayer";
            string briefing = Briefing(mode);
            if (baseName != BaseMission)   // the briefing speaks of Escalation's own win conditions
                briefing = briefing.Replace("the Escalation baseline", "the " + baseName + " baseline")
                    .Replace("Win by destroying the enemy aircraft factories and reinforcement carrier.", "Win through the objectives of " + baseName + ".");
            settings["description"] = briefing + "\n\n" + GamemodeRules.Marker(mode);
            if (mode == GameModeKind.WarBox) BuildWarBox(root, settings, factions);
            else BuildCompetitive(root, settings, factions, mode);
            return root.ToString(Formatting.None);
        }

        private static void BuildWarBox(JObject root, JObject settings, JArray factions)
        {
            Scale(settings, "nuclearEscalationThreshold", GamemodeRules.WarBoxEscalationScale);
            Scale(settings, "strategicEscalationThreshold", GamemodeRules.WarBoxEscalationScale);
            foreach (JObject faction in factions.Children<JObject>())
            {
                if (!IsTeam(faction)) continue;
                Scale(faction, "startingBalance", 4);
                Scale(faction, "reserveAirframes", 4);
                Scale(faction, "extraReservesPerPlayer", 4);
                faction["AIAircraftLimit"] = 8;
                faction["reduceAIPerFriendlyPlayer"] = 1;
                faction["addAIPerEnemyPlayer"] = 0;
                if (faction["supplies"] is JArray supplies)
                    foreach (JObject stock in supplies.Children<JObject>()) Scale(stock, "Count", 4);
            }
            foreach (JObject objective in ((JArray)root["objectives"]).Children<JObject>())
                if ((string)objective["Type"] == "WaitSeconds" && (double?)objective["seconds"] >= 300)
                    Scale(objective, "seconds", GamemodeRules.WarBoxEscalationScale);
            // Existing factories and ground formations remain the population mechanism. No duplicate armies.
            if (root["buildings"] is JArray buildings)
                foreach (JObject building in buildings.Children<JObject>())
                    if (building["factoryOptions"] is JObject factory && (string)factory["productionType"] == "Nuclear Warhead")
                        Scale(factory, "productionTime", GamemodeRules.WarBoxEscalationScale);
        }

        private static void BuildCompetitive(JObject root, JObject settings, JArray factions, GameModeKind mode)
        {
            // Map scenery and built-in airfield infrastructure are separate from these saved mission units.
            // Fuzzball's runtime also suppresses built-in ground units and every subsequent non-aircraft spawn.
            foreach (string list in new[] { "aircraft", "vehicles", "ships", "buildings", "containers", "missiles", "pilots" })
                root[list] = new JArray();
            root["objectives"] = new JArray(new JObject
            {
                ["Type"] = "None", ["UniqueName"] = "Mission Start", ["Faction"] = "",
                ["DisplayName"] = "", ["Hidden"] = true, ["Outcomes"] = new JArray()
            });
            root["outcomes"] = new JArray();
            settings["playerStartingRank"] = 10;
            settings["rankMultiplier"] = 0;
            settings["successfulSortieBonus"] = 0;
            settings["nuclearEscalationThreshold"] = 1000000000;
            settings["strategicEscalationThreshold"] = 1000000000;
            settings["wrecksMaxNumber"] = 16;
            settings["wrecksDecayTime"] = 60;
            if (root["environment"] is JObject environment)
            {
                environment["timeOfDay"] = 12;
                environment["weatherIntensity"] = 0;
            }
            string[] roster = mode == GameModeKind.Fuzzball ? FuzzballAircraft : HeavyweightAircraft;
            foreach (JObject faction in factions.Children<JObject>())
            {
                faction["preventJoin"] = !IsTeam(faction);
                faction["AIAircraftLimit"] = 0;
                faction["addAIPerEnemyPlayer"] = 0;
                faction["startingWarheads"] = 0;
                faction["reserveWarheads"] = 0;
                faction["reserveAirframes"] = 0;
                faction["extraReservesPerPlayer"] = 0;
                faction["killReward"] = 0;
                faction["startingBalance"] = 100000;
                faction["playerJoinAllowance"] = 100000;
                faction["regularIncome"] = 0;
                faction["playerTaxRate"] = 0;
                var stock = new JArray();
                if (IsTeam(faction))
                    foreach (string aircraft in roster)
                        stock.Add(new JObject { ["UnitType"] = aircraft, ["Count"] = 1000 });
                faction["supplies"] = stock;
            }
            if (root["airbases"] is JArray airbases)
                for (int i = airbases.Count - 1; i >= 0; i--)
                {
                    if (!(airbases[i] is JObject airbase)) continue;
                    if (((string)airbase["UniqueName"] ?? "").StartsWith("<UNIT_AIRBASE>++", StringComparison.Ordinal))
                    {
                        airbases.RemoveAt(i);
                        continue;
                    }
                    airbase["Capturable"] = false;
                    airbase["Disabled"] = false;
                    // Saved towers can reference mission-owned buildings removed above. Map towers remain native.
                    airbase["Tower"] = "";
                }
        }

        private static bool IsTeam(JObject faction) =>
            (string)faction["factionName"] == "Boscali" || (string)faction["factionName"] == "Primeva";

        private static void Scale(JObject item, string field, double multiplier)
        {
            JToken value = item[field];
            if (value == null || value.Type != JTokenType.Integer && value.Type != JTokenType.Float) return;
            double scaled = value.Value<double>() * multiplier;
            if (double.IsNaN(scaled) || double.IsInfinity(scaled)) throw new InvalidOperationException("Invalid mission value: " + field);
            item[field] = value.Type == JTokenType.Integer ? new JValue(checked((long)scaled)) : new JValue(scaled);
        }

        private static string Briefing(GameModeKind mode) => mode switch
        {
            GameModeKind.WarBox => "WARBOX | Sustained war sandbox\n\n" +
                "A long combined-arms campaign with slower escalation, larger reserves and sustained fronts. " +
                "Brief your flight, choose a role, fly the task, recover and report. Use the MIS contract board for reconnaissance, rescue, escort, logistics and strike assignments. " +
                "Protect production and supply routes: aircraft and weapons still depend on the native economy. " +
                "Strategic escalation and major reinforcement delays are four times the Escalation baseline. " +
                "Win by destroying the enemy aircraft factories and reinforcement carrier. There is no match clock; the battlefield decides the end.",
            GameModeKind.Fuzzball => "FUZZBALL | Airborne team deathmatch\n\n" +
                "Choose a plane and launch straight into the air. First team to 75 credited enemy aircraft kills wins; otherwise the higher score after 20 minutes wins. " +
                "Equal scores are a draw. Friendly, neutral and environmental losses do not score. " +
                "No ground forces, ships, capture objectives or ground-war reinforcements. Both teams have the same aircraft roster.",
            GameModeKind.Heavyweight => "HEAVYWEIGHT | Cargo run and interception\n\n" +
                "The transport team flies real cargo from its assigned origin to the designated destination airfields; the other team intercepts. " +
                "Fit cargo containers to a QuadVTOL or Utility Helicopter (munitions container or fuel container), depart Maris Airport and follow the assigned route. " +
                "Only successfully unloaded cargo earns points. Kills do not score. " +
                "After a 20-minute leg and a 60-second changeover, teams exchange roles on the same routes for another 20-minute leg. " +
                "The highest cargo total wins; equal totals draw. Cargo eligibility resets at changeover. Check the mode status for the active carrier side and route.",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
}
