#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using Newtonsoft.Json;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>Sandbox evidence and explicitly requested native air-combat fixtures.</summary>
    public static class GamemodeAutomation
    {
        public static Dictionary<string, object> AirCombat(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                !GameAccess.IsServer() || !FuzzballFlight.Active) throw new InvalidOperationException("Host Fuzzball scenario required.");
            string action = args != null && args.TryGetValue("action", out object value) ? Convert.ToString(value) : "state";
            GameManager.GetLocalPlayer<Player>(out Player player);
            if (action == "spawn_player")
            {
                if (player?.HQ == null) throw new InvalidOperationException("Seat a local player first.");
                Aircraft old = player.Aircraft;
                if (old != null)
                {
                    player.RemoveAircraft(old);
                    NetworkSceneSingleton<Spawner>.i.ServerObjectManager.Destroy(old.gameObject);
                }
                FuzzballFlight.GrantAircraft();
                var definition = player.HQ.AircraftSupply.Keys.First(d => d.jsonKey == "SmallFighter1");
                Airbase source = FactionRegistry.airbaseLookup.Values.First(a => a != null && a.CurrentHQ == player.HQ);
                var result = source.TrySpawnAircraft(player, definition, default, definition.aircraftParameters.loadouts[1], 1f);
                return new Dictionary<string, object> { ["allowed"] = result.Allowed };
            }
            string json = action == "fixture" ? GamemodePeerProof.SpawnFixture() :
                action == "credit" ? GamemodePeerProof.CreditEnemyKill() :
                action == "repeat" ? GamemodePeerProof.RepeatKillReport() : GamemodePeerProof.Snapshot();
            var report = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            Aircraft aircraft = player?.Aircraft;
            report["playerAirborne"] = aircraft != null && !aircraft.disabled && !aircraft.IsLanded() &&
                aircraft.transform.position.y > Datum.LocalSeaY + 2000f && aircraft.rb.velocity.magnitude > 100f;
            return report;
        }

        public static Dictionary<string, object> State(Dictionary<string, object> args)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
            { result["ok"] = false; return result; }
            GameManager.GetLocalPlayer<Player>(out Player local);
            var status = GamemodeRuntime.Active != null ? GamemodeRuntime.Active.Snapshot(local) : default;
            int ground = 0, air = 0;
            foreach (PersistentUnit persistent in UnitRegistry.persistentUnitLookup.Values)
                if (persistent.unit is Unit unit && unit != null)
                {
                    if (unit is Aircraft) air++;
                    else if (unit is Building || unit is GroundVehicle || unit is Ship || unit is Container || unit is PilotDismounted) ground++;
                }
            result["ok"] = GamemodeRuntime.Active != null && status.Mode == GamemodeRuntime.Kind;
            result["mode"] = status.Mode.ToString(); result["phase"] = status.Phase.ToString();
            result["leg"] = status.Leg; result["transportingTeam"] = status.TransportingTeam;
            result["score0"] = status.Score0; result["score1"] = status.Score1;
            result["remaining"] = status.Remaining; result["ground"] = ground; result["air"] = air;
            result["route"] = status.Route ?? ""; result["instruction"] = status.Instruction ?? "";
            result["canFly"] = GamemodeRuntime.CanFly;
            result["nativeMission"] = MissionManager.CurrentMission?.Name ?? "";
            result["dynamicOperations"] = MissionProfile.AllowsModule("dynamic-operations");
            result["support"] = MissionProfile.AllowsModule("support");
            int hangars = 0;
            if (FactionRegistry.airbaseLookup.TryGetValue("Maris Airport", out Airbase source))
                foreach (Hangar hangar in source.hangars) if (hangar != null) hangars++;
            result["sourceHangars"] = hangars;
            GameObject hud = GameObject.Find("BoscaliStatusPanel");
            // Read generated glyphs: TMP's player-build .text getter consumes the dirty backing-string flag after SetCharArray,
            // so reading the same unchanged pooled label twice otherwise returns an empty serialized m_text.
            string hudWords = hud != null ? string.Join("|", hud.GetComponentsInChildren<TMP_Text>(false).Select(t => t.GetParsedText())) : "";
            result["hudStatusText"] = hudWords;
            MissionDashboardStatus report = default;
            bool reportReady = GamemodeRuntime.Active != null && GamemodeRuntime.Active.TryRead(out report);
            result["hudHasMode"] = reportReady && hudWords.Contains(report.Name);
            result["hudHasScores"] = hudWords.Contains("BDF " + status.Score0 + " : " + status.Score1 + " PALA");
            result["hudHasPhase"] = reportReady && hudWords.Contains(report.Phase);
            result["standaloneBoards"] = Resources.FindObjectsOfTypeAll<Canvas>().Count(c => c.name == "Boscali / Gamemode status");
            return result;
        }
    }
}
#endif
