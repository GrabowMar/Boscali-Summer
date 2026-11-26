using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Modules.Gamemodes.Patches;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>Native airborne deployment and surface-unit exclusion for the air-combat mission only.</summary>
    internal static class FuzzballFlight
    {
        private static int spawnSequence;
        private static Mission lastMission;
        // Survives the module's per-mission reset. Peers must not reuse a world whose built-ins were removed.
        private static bool reloadSurfaceScene;
        private static readonly Dictionary<FactionHQ, float> nextBot = new Dictionary<FactionHQ, float>();
        private static readonly List<Aircraft> bots = new List<Aircraft>();
        private static readonly List<Unit> surfaceUnits = new List<Unit>();
        public static bool Active => GamemodeRuntime.Kind == GameModeKind.Fuzzball;
        public static readonly Type[] PatchTypes =
        {
            typeof(FuzzballAircraftListPatch), typeof(FuzzballCanSpawnPatch), typeof(FuzzballHangarsPatch),
            typeof(FuzzballSpawnPatch), typeof(FuzzballDefinitionPatch), typeof(FuzzballSavedSpawnPatch),
            typeof(FuzzballCargoPatch), typeof(FuzzballTroopsPatch), typeof(FuzzballDepotPatch),
            typeof(FuzzballPilotPatch), typeof(FuzzballSceneReadyPatch), typeof(CompetitiveFactoryPatch)
        };

        internal static void SceneReady()
        {
            if (!Active) return;
            reloadSurfaceScene = true;
            Enforce();
        }

        internal static void BeforeMapLoad(MapLoader loader)
        {
            if (!reloadSurfaceScene) return;
            // Verified native seam: clearing both keys bypasses Load/LoadScene's AlreadyLoaded shortcuts.
            // Native loading then recreates terrain objects and performs the normal network scene setup.
            loader.ClearMap();
            reloadSurfaceScene = false;
        }

        public static void Reset()
        {
            lastMission = null;
            spawnSequence = 0;
            surfaceUnits.Clear();
            nextBot.Clear();
            bots.Clear();
        }

        internal static bool Surface(Unit unit) => unit is GroundVehicle || unit is Building ||
            unit is Ship || unit is Container || unit is PilotDismounted;

        internal static bool Surface(UnitDefinition definition) => definition != null &&
            definition.unitPrefab != null && Surface(definition.unitPrefab.GetComponent<Unit>());

        internal static List<AircraftDefinition> AircraftFor(Airbase airbase)
        {
            var choices = new List<AircraftDefinition>();
            FactionHQ hq = airbase != null ? airbase.CurrentHQ : null;
            if (hq == null || airbase.disabled) return choices;
            foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> entry in hq.AircraftSupply)
                if (Allowed(hq, entry.Key)) choices.Add(entry.Key);
            choices.Sort((a, b) => string.CompareOrdinal(a.jsonKey, b.jsonKey));
            return choices;
        }

        internal static bool Allowed(FactionHQ hq, AircraftDefinition definition) =>
            hq != null && definition != null && definition.unitPrefab != null &&
            definition.IsAllowed(MissionManager.AllowEventContent) &&
            !hq.restrictedAircraft.Contains(definition.jsonKey) && hq.AircraftSupply.ContainsKey(definition);

        /// <summary>Called before mission objectives run and once per second by the mode host.</summary>
        public static void Enforce()
        {
            if (!Active || !GameAccess.IsServer()) return;
            if (!ReferenceEquals(lastMission, MissionManager.CurrentMission))
            {
                lastMission = MissionManager.CurrentMission;
                spawnSequence = 0;
                nextBot.Clear();
                bots.Clear();
            }
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) return;
            surfaceUnits.Clear();
            foreach (PersistentUnit persistent in UnitRegistry.persistentUnitLookup.Values)
                if (persistent.unit != null && Surface(persistent.unit)) surfaceUnits.Add(persistent.unit);
            // Snapshot first: native destruction deregisters units from the same collection.
            foreach (Unit unit in surfaceUnits)
            {
                if (unit == null) continue;
                // Airbases are separate scene/network objects. Never remove one as collateral cleanup.
                if (unit.GetComponentInChildren<Airbase>(true) != null) continue;
                spawner.ServerObjectManager.Destroy(unit.gameObject);
            }
            surfaceUnits.Clear();
            for (int i = bots.Count - 1; i >= 0; i--)
            {
                Aircraft bot = bots[i];
                if (bot != null && !bot.disabled && !bot.HasEjected()) continue;
                if (bot != null) spawner.ServerObjectManager.Destroy(bot.gameObject);
                bots.RemoveAt(i);
            }
            GrantAircraft();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                if (hq != null) FillBots(hq, spawner);
        }

        /// <summary>Competitive modes retain native purchases/loadout vetting with free replacement stock.</summary>
        public static void GrantAircraft()
        {
            if (!GamemodeRuntime.Competitive || !GameAccess.IsServer()) return;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null) continue;
                if (float.IsFinite(hq.factionFunds) && hq.factionFunds < 100000f)
                    hq.AddFunds(100000f - hq.factionFunds);
                foreach (var entry in hq.factionPlayers)
                {
                    Player player = entry.Player;
                    if (player == null || player.HQ != hq) continue;
                    if (float.IsFinite(player.Allocation) && player.Allocation < 100000f)
                        player.AddAllocation(100000f - player.Allocation);
                    foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> aircraft in hq.AircraftSupply)
                        if (Allowed(hq, aircraft.Key) && !player.OwnsAirframe(aircraft.Key, includeReserved: true))
                            player.CreditAirframe(aircraft.Key, 1, reserved: false);
                }
            }
        }

        private static void FillBots(FactionHQ hq, Spawner spawner)
        {
            if (!GamemodeRuntime.CanFly || hq.faction == null ||
                hq.faction.factionName != "Boscali" && hq.faction.factionName != "Primeva") return;
            if (!MissionTimeSource.TryNow(out float now)) return;
            if (nextBot.TryGetValue(hq, out float next) && now < next) return;
            nextBot[hq] = now + 8f;
            int count = 0;
            foreach (Unit unit in UnitRegistry.allUnits)
                if (unit is Aircraft aircraft && !aircraft.disabled && !aircraft.HasEjected() && aircraft.NetworkHQ == hq) count++;
            if (count >= 4) return;
            AircraftDefinition choice = null;
            Loadout loadout = null;
            float best = 0f;
            foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> entry in hq.AircraftSupply)
            {
                AircraftDefinition definition = entry.Key;
                if (!Allowed(hq, definition) || definition.aircraftParameters?.StandardLoadouts == null) continue;
                Aircraft prefab = definition.unitPrefab.GetComponent<Aircraft>();
                if (prefab == null || prefab.weaponManager == null) continue;
                foreach (StandardLoadout standard in definition.aircraftParameters.StandardLoadouts)
                {
                    if (standard == null || standard.disabled || standard.loadout == null ||
                        !standard.AllowedByHQ(prefab.weaponManager, hq)) continue;
                    float score = 0f;
                    bool suitable = true;
                    foreach (WeaponMount mount in standard.loadout.weapons)
                    {
                        WeaponInfo info = mount != null ? mount.info : null;
                        if (info == null) continue;
                        if (info.nuclear || info.cargo || info.troops) { suitable = false; break; }
                        score += info.effectiveness.antiAir * Mathf.Max(1, mount.ammo);
                    }
                    if (!suitable || score <= best) continue;
                    best = score; choice = definition; loadout = standard.loadout;
                }
            }
            if (choice == null || !TrySpawnPose(hq, out GlobalPosition position, out Quaternion rotation)) return;
            Aircraft spawned = spawner.SpawnAircraft(null, choice.unitPrefab, loadout, 1f, default,
                position, rotation, rotation * Vector3.forward * 180f, null, hq, null, 0.8f, 0.8f);
            if (spawned != null) bots.Add(spawned);
        }

        internal static Airbase.TrySpawnResult Spawn(Airbase airbase, Player player,
            AircraftDefinition definition, LiveryKey livery, Loadout loadout, float fuelLevel)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (!GamemodeRuntime.CanFly || !GameAccess.IsServer() || spawner == null || !spawner.IsServer || airbase == null ||
                airbase.disabled || !Allowed(airbase.CurrentHQ, definition) || !float.IsFinite(fuelLevel) ||
                fuelLevel < 0f || fuelLevel > 1f) return default;
            if (player != null && (player.HQ != airbase.CurrentHQ ||
                !player.OwnsAirframe(definition, includeReserved: true) ||
                player.Aircraft != null && !player.Aircraft.disabled && !player.Aircraft.HasEjected())) return default;
            FactionHQ hq = airbase.CurrentHQ;
            if (!TrySpawnPose(hq, out GlobalPosition position, out Quaternion rotation)) return default;
            Aircraft aircraft = spawner.SpawnAircraft(player, definition.unitPrefab, loadout, fuelLevel,
                livery, position, rotation, rotation * Vector3.forward * 180f, null, hq, null, 1f, 0.5f);
            if (aircraft == null) return default;
            // The caller has already passed Spawner.AllowedToSpawn and WeaponChecker.VetLoadout.
            if (player != null) player.FlyOwnedAirframe(definition);
            return new Airbase.TrySpawnResult(true, null, false);
        }

        private static bool TrySpawnPose(FactionHQ hq, out GlobalPosition position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            // the arena lies between two airfields: on Heartland, and on the mod's desert
            Airbase north = null, south = null;
            foreach (string[] pair in new[] { new[] { "K92 Highway Strip", "Dustbowl Highway Strip" }, new[] { "Tassara Strip 1", "Tassara Strip 2" } })
                if (FactionRegistry.airbaseLookup.TryGetValue(pair[0], out north) && FactionRegistry.airbaseLookup.TryGetValue(pair[1], out south)) break;
            if (hq?.faction == null || north == null || south == null || north.center == null || south.center == null) return false;
            bool boscali = hq.faction.factionName == "Boscali";
            if (!boscali && hq.faction.factionName != "Primeva") return false;
            Vector3 center = (north.center.position + south.center.position) * 0.5f;
            Vector3 forward = boscali ? Vector3.back : Vector3.forward;
            Vector3 anchor = center - forward * 8000f;
            rotation = Quaternion.LookRotation(forward);
            Vector3 lateral = Vector3.Cross(Vector3.up, forward);
            int slot = spawnSequence++ & 7;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // Keep fallback positions within 3.5 km of the arena's deployment line.
                Vector3 point = anchor - forward * (attempt * 500f) + lateral * ((slot - 3.5f) * 450f);
                point.y = Datum.LocalSeaY + 10000f;
                float ground = Datum.LocalSeaY;
                if (Physics.Raycast(point, Vector3.down, out RaycastHit hit, 20000f,
                    PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)) ground = Mathf.Max(ground, hit.point.y);
                point.y = ground + 2500f + slot * 50f;
                bool clear = true;
                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (unit is Missile missile && !missile.disabled && missile.NetworkHQ != hq &&
                        (missile.transform.position - point).sqrMagnitude < 3000f * 3000f)
                    { clear = false; break; }
                    if (!(unit is Aircraft aircraft) || aircraft.disabled) continue;
                    float minimum = aircraft.NetworkHQ == hq ? 350f : 6000f;
                    if ((aircraft.transform.position - point).sqrMagnitude < minimum * minimum) { clear = false; break; }
                }
                if (!clear) continue;
                position = point.ToGlobalPosition();
                return true;
            }
            return false;
        }
    }
}
