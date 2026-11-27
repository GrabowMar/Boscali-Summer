#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Presentation;
using HarmonyLib;
using Mirage;
using Newtonsoft.Json;
using NuclearOption.MissionEditorScripts;
using NuclearOption.Networking;
using NuclearOption.UI;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    public static class DronePeerProof
    {
        public static bool CatalogReady(Dictionary<string, object> _) => RemoraPrefab() != null;

        public static bool SeatBoth()
        {
            ServerOnly();
            NetworkServer server = GameAccess.NetworkManagerOrNull.Server;
            FactionHQ hq = FactionRegistry.GetSortedHQs()[0];
            AircraftDefinition definition = Encyclopedia.i.aircraft.First(a => a != null && a.jsonKey == "COIN");
            foreach (INetworkPlayer peer in server.AuthenticatedPlayers)
            {
                if (!peer.TryGetPlayer<Player>(out Player player) || player == null) continue;
                if (player.HQ != hq) player.SetFaction(hq, true);
                if (player.Aircraft != null) continue;
                bool local = peer == server.LocalPlayer;
                float x = local ? 10000f : 10300f;
                NetworkSceneSingleton<Spawner>.i.SpawnAircraft(player, definition.unitPrefab,
                    definition.aircraftParameters.loadouts[0], 1f, default, new GlobalPosition(x, 4500f, -35000f),
                    Quaternion.identity, Vector3.forward * 120f, null, hq, local ? "DroneProofHost" : "DroneProofClient", .5f, .5f);
            }
            return true;
        }

        public static uint Launch()
        {
            ServerOnly();
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null || aircraft.disabled)
                throw new InvalidOperationException("Seat the host before launching");
            MissileDefinition definition = Encyclopedia.i.missiles.First(d => d != null && d.jsonKey == VanguardKeys.Remora);
            Transform t = aircraft.transform;
            Missile missile = NetworkSceneSingleton<Spawner>.i.SpawnMissile(definition, t.position - t.up * 5f + t.forward * 15f,
                t.rotation, aircraft.CockpitRB().velocity, null, aircraft);
            return missile.persistentID.Id;
        }

        public static bool Command(uint id, string name, bool raw)
        {
            RequirePeer();
            if (!Enum.TryParse(name, ignoreCase: true, out DroneAction action) || action < DroneAction.Share || action > DroneAction.Release)
                throw new ArgumentException("Unknown drone action", nameof(name));
            if (raw)
            {
                if (GameAccess.IsServer()) throw new InvalidOperationException("Use raw packets only from the loopback client");
                GameAccess.NetworkManagerOrNull.Client.Send(new VanguardDroneOrder
                    { Protocol = VanguardNet.ProtocolVersion, Action = (byte)action, UnitId = id });
                return true;
            }
            Missile drone = UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) ? unit as Missile : null;
            DronePilot pilot = DronePilot.Active;
            VanguardNet net = pilot != null ? pilot.GetComponent<VanguardNet>() : null;
            if (drone == null || net == null) throw new InvalidOperationException("The drone or its network service is unavailable");
            return net.SendDroneAction(drone, action);
        }

        public static bool Fire(bool held)
        {
            RequirePeer();
            DronePilot pilot = DronePilot.Active;
            if (pilot == null || pilot.Current == null) return false;
            pilot.SetTestInput(held ? (Vector3?)Vector3.zero : null, 0.7f, held);
            return true;
        }

        public static string Snapshot(uint id)
        {
            RequirePeer();
            GameManager.GetLocalPlayer(out Player player);
            Missile drone = id != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) ? unit as Missile : null;
            DroneRoster.TryGet(drone, out DroneRoster.Entry entry);
            Aircraft aircraft = player != null ? player.Aircraft : null;
            NetworkIdentity prefab = RemoraPrefab();
            DronePilot controller = DronePilot.Active;
            DroneFlightView flightView = DroneFlightView.Active;
            CameraStateManager stockCamera = SceneSingleton<CameraStateManager>.i;
            return JsonConvert.SerializeObject(new
            {
                aircraft = aircraft != null && !aircraft.disabled ? aircraft.persistentID.Id : 0u,
                pilotReady = DronePilot.SupportsAircraft(aircraft),
                inRange = entry != null && DroneRoster.InRange(player, entry),
                player = DroneRoster.PlayerId(player),
                available = prefab != null,
                prefabHash = prefab != null ? prefab.PrefabHash : 0,
                tracked = entry != null,
                visible = entry != null && entry.VisibleTo(player),
                owner = entry != null && entry.Owner(player),
                shared = entry != null && entry.Shared,
                pilot = entry?.PilotId ?? 0u,
                lease = entry?.Lease ?? 0u,
                input = entry != null && entry.AcceptedInput,
                flying = drone != null && controller != null && controller.Current == drone,
                droneCamera = flightView != null && flightView.Presenting,
                aircraftCamera = aircraft != null && stockCamera != null && stockCamera.followingUnit == aircraft,
                weapon = controller != null ? controller.Weapon.ToString() : "None",
                gameplayOpen = DronePilotInput.GameplayOpen,
                controls = controller != null && controller.ControlsEnabled,
                inputGate = new { DynamicMap.mapMaximized, GameplayUI.GameIsPaused,
                    radial = SceneSingleton<RadialMenuMain>.i != null && RadialMenuMain.IsInUse(),
                    scoreboard = LeaderboardMenu.IsOpen(), InputFieldChecker.InsideInputField, typing = MapTool.Typing() },
                trigger = DronePilot.Trigger(drone).ToString(),
                gunShots = RemoraBoard.Views.TryGetValue(id, out RemoraView view) ? view.Shots : 0,
            });
        }

        public static Dictionary<string, object> PrefabRegistration(Dictionary<string, object> _)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                throw new InvalidOperationException("This proof requires a hosted solo nomodkit scenario");
            ClientObjectManager objects = GameAccess.NetworkManagerOrNull.ClientObjectManager;
            NetworkIdentity prefab = RemoraPrefab();
            if (objects == null || prefab == null || prefab.PrefabHash == 0 ||
                AccessTools.Field(typeof(ClientObjectManager), "_handlers")?.GetValue(objects) is not Dictionary<int, SpawnHandler> handlers)
                throw new InvalidOperationException("The game client manager or the REMORA prefab is not ready");

            int hash = prefab.PrefabHash;
            bool hadHandler = handlers.TryGetValue(hash, out SpawnHandler original);
            handlers.Remove(hash);
            try
            {
                SpawnHandler recovered = objects.GetSpawnHandler(hash);
                bool recoveredRemora = recovered != null && recovered.Prefab == prefab;
                bool registered = handlers.TryGetValue(hash, out SpawnHandler cached) && cached.Prefab == prefab;
                return new Dictionary<string, object>
                {
                    ["ok"] = recoveredRemora && registered, ["prefabHash"] = hash, ["registered"] = registered,
                };
            }
            finally
            {
                if (hadHandler) handlers[hash] = original;
                else handlers.Remove(hash);
            }
        }

        private static NetworkIdentity RemoraPrefab()
        {
            Encyclopedia encyclopedia = GameAccess.EncyclopediaOrNull;
            if (encyclopedia == null) return null;
            MissileDefinition remora = encyclopedia.missiles.FirstOrDefault(d => d != null && d.jsonKey == VanguardKeys.Remora);
            return remora != null && remora.unitPrefab != null ? remora.unitPrefab.GetComponent<NetworkIdentity>() : null;
        }

        private static void ServerOnly()
        {
            RequirePeer();
            if (!GameAccess.IsServer()) throw new InvalidOperationException("This proof action requires the loopback host");
        }

        private static void RequirePeer()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new InvalidOperationException("This proof requires a nomodkit loopback peer");
        }
    }
}
#endif
