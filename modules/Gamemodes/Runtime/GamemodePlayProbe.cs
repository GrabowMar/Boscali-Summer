#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Gamemodes.Domain;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Gamemodes.Runtime
{
    /// <summary>Host-only native cargo fixture. Repositioning and shorter deadlines are explicit;
    /// scoring, eligibility, receipts and match phases are never written by the probe.</summary>
    public static class GamemodePlayProbe
    {
        private static readonly FieldInfo MatchField = typeof(GamemodeRuntime).GetField("match", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DeadlineField = typeof(GamemodeMatch).GetField("<Deadline>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RampPartField = typeof(CargoRamp).GetField("rampPart", BindingFlags.Instance | BindingFlags.NonPublic);
        private static MountedCargo fired;
        private static WeaponStation firedStation;
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                !GameAccess.IsServer() || GamemodeRuntime.Active == null || GamemodeRuntime.Kind != GameModeKind.Heavyweight)
                return Fail("Requires a host Heavyweight nomodkit scenario.");
            string action = args != null && args.TryGetValue("action", out object value) ? Convert.ToString(value) : "state";
            try
            {
                switch (action)
                {
                    case "prepare": return Prepare();
                    case "hold_source": return HoldSource();
                    case "destination": return Destination();
                    case "fire": return Fire();
                    case "duplicate": return Duplicate();
                    case "deadline": return ShortenDeadline();
                    default: return Report();
                }
            }
            catch (Exception error) { return Fail(action + ": " + error.GetType().Name + ": " + error.Message); }
        }
        private static Dictionary<string, object> Prepare()
        {
            fired = null; firedStation = null;
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player?.HQ == null)
                return Fail("Seat a local BDF player first.");
            if (!FactionRegistry.airbaseLookup.TryGetValue("Maris Airport", out Airbase source) ||
                source == null || source.CurrentHQ != player.HQ)
                return Fail("Local player must belong to the current Maris transport faction.");
            AircraftDefinition definition = null;
            foreach (var entry in player.HQ.AircraftSupply)
                if (entry.Key != null && entry.Key.jsonKey == "UtilityHelo1") { definition = entry.Key; break; }
            if (definition == null) return Fail("Native UtilityHelo1 unavailable.");
            Aircraft prefab = definition.unitPrefab.GetComponent<Aircraft>();
            if (prefab?.weaponManager?.hardpointSets == null) return Fail("Native transport hardpoints unavailable.");
            var loadout = new Loadout();
            bool found = false;
            foreach (HardpointSet set in prefab.weaponManager.hardpointSets)
            {
                WeaponMount selected = null;
                if (!found && set.weaponOptions != null)
                    foreach (WeaponMount option in set.weaponOptions)
                        if (option != null && option.jsonKey == "FuelContainer2x1") { selected = option; found = true; break; }
                loadout.weapons.Add(selected);
            }
            if (!found) return Fail("FuelContainer2x1 is not offered by the native transport.");
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer) return Fail("Native spawner unavailable.");
            Aircraft previous = player.Aircraft;
            if (previous != null) { player.RemoveAircraft(previous); spawner.ServerObjectManager.Destroy(previous.gameObject); }
            player.CreditAirframe(definition, 1, reserved: false);
            Airbase.TrySpawnResult spawned = source.TrySpawnAircraft(player, definition, default, loadout, 1f);
            return new Dictionary<string, object> { ["ok"] = spawned.Allowed, ["allowed"] = spawned.Allowed,
                ["aircraft"] = definition.jsonKey, ["cargoMount"] = "FuelContainer2x1",
                ["fixture"] = "Native Maris hangar spawn; wait for source enrollment." };
        }
        private static Dictionary<string, object> HoldSource()
        {
            if (!Local(out Aircraft aircraft)) return Fail("Native transport has not spawned.");
            if (!FactionRegistry.airbaseLookup.TryGetValue("Maris Airport", out Airbase source)) return Fail("Source missing.");
            if (Vector3.Distance(aircraft.transform.position, source.center.position) > 2200f)
                return Fail("Native hangar did not spawn at source.");
            Hold(aircraft, aircraft.transform.position);
            return Report();
        }
        private static Dictionary<string, object> Destination()
        {
            if (!Local(out Aircraft aircraft)) return Fail("Local transport unavailable.");
            GameManager.GetLocalPlayer<Player>(out Player player);
            var status = GamemodeRuntime.Active.Snapshot(player);
            if (status.Cargo <= 0) return Fail("The real source load has not been enrolled.");
            string[] route = (status.Route ?? "").Split(new[] { " -> " }, StringSplitOptions.None);
            if (route.Length != 2 || !FactionRegistry.airbaseLookup.TryGetValue(route[1], out Airbase destination) ||
                destination?.center == null) return Fail("Assigned destination unavailable.");
            Vector3 target = destination.center.position + Vector3.right * 120f;
            if (!Physics.Raycast(target + Vector3.up * 500f, Vector3.down, out RaycastHit ground, 1500f,
                (int)PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)) return Fail("No destination surface.");
            target.y = ground.point.y + Mathf.Max(2f, aircraft.definition.spawnOffset.y);
            Hold(aircraft, target);
            var report = Report();
            report["fixture"] = "Aircraft repositioned and held at assigned field; cargo release remains native.";
            report["destination"] = route[1];
            return report;
        }
        private static void Hold(Aircraft aircraft, Vector3 target)
        {
            var bodies = new List<Rigidbody>(aircraft.GetComponentsInChildren<Rigidbody>());
            var movable = new HashSet<Rigidbody>();
            void KeepRampsMoving(Component root)
            {
                foreach (CargoRamp ramp in root.GetComponentsInChildren<CargoRamp>(true))
                    if (RampPartField?.GetValue(ramp) is UnitPart part && part.rb != null) movable.Add(part.rb);
            }
            KeepRampsMoving(aircraft);
            if (aircraft.partLookup != null)
                foreach (UnitPart part in aircraft.partLookup)
                    if (part != null && !part.IsDetached())
                    {
                        KeepRampsMoving(part);
                        if (part.rb != null && !bodies.Contains(part.rb)) bodies.Add(part.rb);
                    }
            Vector3 origin = aircraft.transform.position;
            var positions = new Vector3[bodies.Count];
            for (int i = 0; i < bodies.Count; i++) positions[i] = bodies[i].position;
            aircraft.transform.position = target;
            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody body = bodies[i];
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                // The native hinged ramp must retain physics to open and release its real cargo.
                body.isKinematic = !movable.Contains(body); body.position = positions[i] + target - origin;
            }
            aircraft.velocityPrev = Vector3.zero;
            if (aircraft.pilots != null)
                foreach (Pilot pilot in aircraft.pilots) if (pilot != null) pilot.velocityPrev = Vector3.zero;
            Physics.SyncTransforms();
        }
        private static Dictionary<string, object> Fire()
        {
            if (!Local(out Aircraft aircraft)) return Fail("Local transport unavailable.");
            foreach (WeaponStation station in aircraft.weaponStations)
                foreach (Weapon weapon in station.Weapons)
                    if (weapon is MountedCargo mounted && mounted.GetAmmoTotal() > 0 &&
                        mounted.cargo?.unitPrefab?.GetComponent<Container>() != null)
                    {
                        fired = mounted; firedStation = station;
                        int before = mounted.GetAmmoTotal();
                        mounted.Fire(aircraft, null, Vector3.zero, station, default);
                        return new Dictionary<string, object> { ["ok"] = mounted.GetAmmoTotal() < before,
                            ["ammoBefore"] = before, ["ammoAfter"] = mounted.GetAmmoTotal(),
                            ["fixture"] = "Native MountedCargo.Fire; wait for rail launch and settlement." };
                    }
            return Fail("No loaded physical container mount found.");
        }
        private static Dictionary<string, object> Duplicate()
        {
            if (!Local(out Aircraft aircraft) || fired == null || firedStation == null) return Fail("No earlier native release.");
            int before = fired.GetAmmoTotal();
            fired.Fire(aircraft, null, Vector3.zero, firedStation, default);
            var report = Report();
            report["duplicateAmmoBefore"] = before; report["duplicateAmmoAfter"] = fired.GetAmmoTotal();
            return report;
        }
        private static Dictionary<string, object> ShortenDeadline()
        {
            var match = MatchField?.GetValue(GamemodeRuntime.Active) as GamemodeMatch;
            if (match == null || DeadlineField == null || match.Phase == GamemodePhase.Finished) return Fail("No active deadline.");
            DeadlineField.SetValue(match, (double)MissionTimeSource.Now() + 2d);
            var report = Report();
            report["fixture"] = "Deadline shortened to two seconds; ordinary host Tick applies transition.";
            return report;
        }
        private static bool Local(out Aircraft aircraft)
        {
            aircraft = null;
            return GameManager.GetLocalPlayer<Player>(out Player player) && player != null &&
                (aircraft = player.Aircraft) != null && !aircraft.disabled && aircraft.definition?.jsonKey == "UtilityHelo1";
        }
        private static Dictionary<string, object> Report()
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            var status = GamemodeRuntime.Active.Snapshot(player);
            int containers = 0, aircraftCount = 0, mounted = 0;
            foreach (Unit unit in UnitRegistry.allUnits)
                if (unit != null) { if (unit is Container) containers++; if (unit is Aircraft) aircraftCount++; }
            Aircraft aircraft = player?.Aircraft;
            if (aircraft?.weaponStations != null)
                foreach (WeaponStation station in aircraft.weaponStations)
                    foreach (Weapon weapon in station.Weapons)
                        if (weapon is MountedCargo cargo && cargo.GetAmmoTotal() > 0 &&
                            cargo.cargo?.unitPrefab?.GetComponent<Container>() != null) mounted++;
            return new Dictionary<string, object> { ["ok"] = true, ["mode"] = status.Mode.ToString(),
                ["phase"] = status.Phase.ToString(), ["leg"] = status.Leg, ["transportingTeam"] = status.TransportingTeam,
                ["score0"] = status.Score0, ["score1"] = status.Score1, ["cargo"] = status.Cargo,
                ["sourceReady"] = status.Cargo > 0 && mounted > 0, ["mounted"] = mounted, ["containers"] = containers,
                ["aircraftCount"] = aircraftCount, ["landed"] = aircraft != null && aircraft.IsLanded(),
                ["route"] = status.Route ?? "", ["instruction"] = status.Instruction ?? "" };
        }
        private static Dictionary<string, object> Fail(string message) => new Dictionary<string, object> { ["ok"] = false, ["error"] = message };
    }
}
#endif
