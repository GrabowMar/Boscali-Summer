using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Ares.Runtime
{
    /// <summary>UNITS: what the game master does to the map's selection.</summary>
    internal sealed partial class AresHost
    {
        private sealed class Frozen
        {
            public Rigidbody[] Bodies;
            public bool[] Kinematic;
            public bool ByTimeStop;
        }

        internal readonly HashSet<Unit> protectedUnits = new HashSet<Unit>();
        private readonly Dictionary<Unit, Frozen> frozen = new Dictionary<Unit, Frozen>();
        private readonly HashSet<Unit> followers = new HashSet<Unit>();

        private static readonly FieldInfo ImpactPrev = AccessTools.Field(typeof(ImpactDetector), "velocityPrev");
        private static readonly FieldInfo FuelPrev = AccessTools.Field(typeof(FuelTank), "velocityPrev");

        public int FrozenCount => frozen.Count;
        public int ProtectedCount => protectedUnits.Count;
        public int FollowerCount => followers.Count;

        private string ForSelection(string verb, Func<Unit, bool> act)
        {
            if (!Host(out string refusal)) return Say(refusal);
            List<Unit> units = Selection();
            if (units.Count == 0) return Say("Select units on the map first (click a contact or drag a box).");
            int done = 0;
            foreach (Unit unit in units.ToArray())
            {
                try { if (unit != null && act(unit)) done++; }
                catch (Exception error) { log?.LogWarning("[Ares] " + verb + " failed on " + unit.name + ": " + error.Message); }
            }
            return Say(verb + ": " + done + " of " + units.Count + " unit(s).");
        }

        public string RepairSelection() => ForSelection("REPAIRED", RepairUnit);
        public string RearmSelection() => ForSelection("REARMED", RearmUnit);
        public string KillSelection() => ForSelection("DESTROYED", unit => { if (unit.disabled) return false; unit.DisableUnit(); return true; });
        public string DeleteSelection() => ForSelection("DELETED", unit => { Remove(unit); return true; });

        public string CycleSideSelection()
        {
            FactionHQ[] sides = FactionRegistry.GetSortedHQs();
            if (sides == null || sides.Length < 2) return Say("This mission has one side only.");
            return ForSelection("SWITCHED SIDE", unit =>
            {
                int at = Array.IndexOf(sides, unit.NetworkHQ);
                unit.NetworkHQ = sides[(at + 1) % sides.Length];
                return true;
            });
        }

        public string ToggleProtectSelection()
        {
            List<Unit> units = Selection();
            bool all = units.Count > 0;
            foreach (Unit unit in units) all &= protectedUnits.Contains(unit);
            return ForSelection(all ? "VULNERABLE AGAIN" : "INVULNERABLE", unit =>
            {
                if (all) protectedUnits.Remove(unit);
                else protectedUnits.Add(unit);
                return true;
            });
        }

        public string ToggleFreezeSelection()
        {
            List<Unit> units = Selection();
            bool all = units.Count > 0;
            foreach (Unit unit in units) all &= frozen.ContainsKey(unit);
            return ForSelection(all ? "UNFROZEN" : "FROZEN", unit => all ? Thaw(unit) : Freeze(unit, false));
        }

        public string ToggleFollowSelection()
        {
            List<Unit> units = Selection();
            bool all = units.Count > 0;
            foreach (Unit unit in units) all &= followers.Contains(unit);
            return ForSelection(all ? "STOPPED FOLLOWING" : "FOLLOWING YOU", unit =>
            {
                if (!(unit is GroundVehicle) && !(unit is Ship)) return false;
                if (all) followers.Remove(unit);
                else followers.Add(unit);
                return true;
            });
        }

        public string MoveSelectionTo(GlobalPosition at) => ForSelection("ORDERED TO MOVE", unit => Order(unit, at));

        /// <summary>The selection keeps its layout around its centre; aircraft keep their height above the ground.</summary>
        public string TeleportSelectionTo(GlobalPosition at)
        {
            List<Unit> units = Selection();
            if (units.Count == 0) return Say("Select units on the map first.");
            Vector3 centre = Vector3.zero;
            foreach (Unit unit in units) centre += unit.transform.position;
            centre /= units.Count;
            Vector3 target = at.ToLocalPosition();
            return ForSelection("TELEPORTED", unit =>
            {
                Vector3 offset = unit.transform.position - centre;
                return Teleport(unit, new Vector3(target.x + offset.x, 0f, target.z + offset.z));
            });
        }

        public string TakeControl()
        {
            if (!Host(out string refusal)) return Say(refusal);
            List<Unit> units = Selection();
            if (units.Count != 1 || !(units[0] is Aircraft source) || source.disabled) return Say("Select exactly one flying aircraft.");
            if (!(source.definition is AircraftDefinition definition)) return Say("That aircraft has no definition.");
            if (!GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player)) return Say("No local player.");
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null) return Say("No spawner in this scene.");
            Rigidbody body = source.CockpitRB();
            Vector3 position = source.transform.position, velocity = body != null ? body.velocity : source.transform.forward * 150f;
            Quaternion rotation = source.transform.rotation;
            FactionHQ hq = source.NetworkHQ;
            NuclearOption.SavedMission.Loadout loadout = source.Networkloadout;
            float fuel = source.NetworkfuelLevel;
            Remove(source);
            StartCoroutine(Seat(spawner, definition, position, rotation, velocity, hq, player, loadout, fuel));
            return Say("TAKING CONTROL of the " + (definition.unitName ?? "aircraft").ToUpperInvariant() + "...");
        }

        /// <summary>The copy comes one physics step after the original went: spawned inside it (its destroy lands at the end of the
        /// frame), the copy's propellers struck the old airframe and both engines flamed out (seen live).</summary>
        private System.Collections.IEnumerator Seat(Spawner spawner, AircraftDefinition definition, Vector3 position, Quaternion rotation,
            Vector3 velocity, FactionHQ hq, NuclearOption.Networking.Player player, NuclearOption.SavedMission.Loadout loadout, float fuel)
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            position += velocity * Time.fixedDeltaTime * 2f;
            Aircraft mine = spawner == null ? null : SpawnAircraft(spawner, definition, position, rotation, hq,
                "ARES:pilot:" + Guid.NewGuid().ToString("N").Substring(0, 8), player, velocity, loadout, fuel);
            Say(mine != null ? "YOU HAVE CONTROL of the " + (definition.unitName ?? "aircraft").ToUpperInvariant() + "." : "The game refused the seat.");
        }

        // ------------------------------------------------------------------ per-unit actions

        /// <summary>Every attached part to full hit points; a destroyed building comes back. Damage textures and fires stay (no vanilla
        /// repair for vehicles exists, this is the closest).</summary>
        internal static bool RepairUnit(Unit unit)
        {
            if (unit is Building building)
            {
                if (building.disabled) building.Networkdisabled = false;
                building.Repair(null, 1e9f);
            }
            if (unit.partLookup != null)
                foreach (UnitPart part in unit.partLookup)
                    if (part != null && !part.IsDetached()) part.Repair();
            return true;
        }

        internal static bool RearmUnit(Unit unit) => RearmUnit(unit, true);

        /// <summary>Every weapon station to full, flares and chaff too; an aircraft also refuels unless <paramref name="refuel"/> is off
        /// (INFINITE AMMO: a refuel posts a feed line, and twice a second that is spam).</summary>
        internal static bool RearmUnit(Unit unit, bool refuel)
        {
            bool any = false;
            if (unit.weaponStations != null && unit.weaponStations.Count > 0 && unit.NetworkHQ != null)
            {
                var stations = new int[unit.weaponStations.Count];
                for (int i = 0; i < stations.Length; i++)
                {
                    WeaponStation w = unit.weaponStations[i];
                    stations[i] = w == null || w.Cargo ? 0 : Math.Max(0, w.FullAmmo - w.GetAmmoTotal());
                    any |= stations[i] > 0;
                }
                if (any) unit.RpcRearm(new RearmEventArgs { Rearmer = unit, Stations = stations });
            }
            if (unit is Aircraft aircraft)
            {
                if (!any && aircraft.countermeasureManager != null)
                    aircraft.countermeasureManager.Rearm(new RearmEventArgs { Rearmer = aircraft });
                if (refuel)
                {
                    aircraft.NetworkfuelLevel = 1f;
                    aircraft.Refuel(aircraft);
                }
            }
            return true;
        }

        /// <summary>Holds a unit still: aircraft hang in the air (every body kinematic, AI off), ground units and ships hold position.</summary>
        private bool Freeze(Unit unit, bool byTimeStop)
        {
            if (unit == null || unit.disabled || frozen.ContainsKey(unit) || unit is Building) return false;
            var state = new Frozen { ByTimeStop = byTimeStop };
            if (unit is Aircraft aircraft)
            {
                if (aircraft.pilots != null)
                    foreach (Pilot pilot in aircraft.pilots)
                        if (pilot != null) pilot.SwitchState(null);
                state.Bodies = Bodies(aircraft);
                state.Kinematic = new bool[state.Bodies.Length];
                for (int i = 0; i < state.Bodies.Length; i++)
                {
                    state.Kinematic[i] = state.Bodies[i].isKinematic;
                    state.Bodies[i].isKinematic = true;
                }
            }
            else if (unit is GroundVehicle vehicle)
            {
                vehicle.StopImmediately();
                vehicle.SetHoldPosition(true);
            }
            else if (unit is Ship ship) ship.SetHoldPosition(true);
            else return false;
            frozen[unit] = state;
            return true;
        }

        private bool Thaw(Unit unit)
        {
            if (unit == null || !frozen.TryGetValue(unit, out Frozen state)) return false;
            frozen.Remove(unit);
            if (unit is Aircraft aircraft)
            {
                if (state.Bodies != null)
                    for (int i = 0; i < state.Bodies.Length; i++)
                        if (state.Bodies[i] != null) state.Bodies[i].isKinematic = state.Kinematic[i];
                if (!aircraft.disabled && aircraft.Player == null) Wake(aircraft);
            }
            else if (unit is GroundVehicle vehicle) vehicle.SetHoldPosition(false);
            else if (unit is Ship ship) ship.SetHoldPosition(false);
            return true;
        }

        private static bool Order(Unit unit, GlobalPosition at)
        {
            if (unit.disabled) return false;
            if (unit is GroundVehicle vehicle)
            {
                vehicle.SetHoldPosition(false);
                vehicle.UnitCommand.SetDestination(at, true);
                return true;
            }
            if (unit is Ship ship)
            {
                ship.SetHoldPosition(false);
                ship.UnitCommand.SetDestination(at, true);
                return true;
            }
            return false;
        }

        /// <summary>Moves a unit to a point (x and z; height from the ground). An aircraft keeps its height above the ground and its
        /// velocity, every body moved together and the finite-difference state restarted, so the jump is not read as an impact.</summary>
        internal static bool Teleport(Unit unit, Vector3 to)
        {
            if (unit == null || unit.disabled || unit is Building) return false;
            Transform root = unit.transform;
            Vector3 from = root.position;
            Vector3 ground = Ground(to.ToGlobalPosition(), out bool water);
            if (unit is Aircraft aircraft)
            {
                float agl = Mathf.Max(150f, from.y - GroundY(from));
                Vector3 target = new Vector3(to.x, Mathf.Max(ground.y + agl, Datum.LocalSeaY + 150f), to.z);
                foreach (Rigidbody rb in Bodies(aircraft))
                {
                    Vector3 p = target + (rb.position - from);
                    rb.position = p;
                    if (!rb.transform.IsChildOf(root)) rb.transform.position = p;
                }
                root.position = target;
                aircraft.velocityPrev = Vector3.zero;
                if (aircraft.pilots != null)
                    foreach (Pilot pilot in aircraft.pilots)
                        if (pilot != null) pilot.velocityPrev = Vector3.zero;
                ResetPrev(aircraft.gameObject);
                if (aircraft.partLookup != null)
                    foreach (UnitPart part in aircraft.partLookup)
                        if (part != null && !part.transform.IsChildOf(root)) ResetPrev(part.gameObject);
                if (GameManager.IsLocalAircraft(aircraft)) SceneSingleton<CameraStateManager>.i?.CheckOriginShift();
                return true;
            }
            if (unit is Ship)
            {
                if (!water) return false;
                root.position = new Vector3(to.x, Datum.LocalSeaY, to.z);
            }
            else
            {
                if (water) return false;
                float lift = unit.definition != null ? unit.definition.spawnOffset.y : 0f;
                root.position = ground + Vector3.up * lift;
                if (unit is GroundVehicle vehicle) vehicle.StopImmediately();
            }
            if (unit.rb != null)
            {
                unit.rb.position = root.position;
                unit.rb.velocity = Vector3.zero;
            }
            return true;
        }

        private static Rigidbody[] Bodies(Aircraft aircraft)
        {
            var bodies = new List<Rigidbody>(aircraft.GetComponentsInChildren<Rigidbody>());
            if (aircraft.partLookup != null)
                foreach (UnitPart part in aircraft.partLookup)
                    if (part != null && !part.IsDetached() && part.rb != null && !bodies.Contains(part.rb)) bodies.Add(part.rb);
            return bodies.ToArray();
        }

        private static void ResetPrev(GameObject o)
        {
            if (ImpactPrev != null)
                foreach (ImpactDetector d in o.GetComponentsInChildren<ImpactDetector>(true)) ImpactPrev.SetValue(d, Vector3.zero);
            if (FuelPrev != null)
                foreach (FuelTank t in o.GetComponentsInChildren<FuelTank>(true)) FuelPrev.SetValue(t, Vector3.zero);
        }

        /// <summary>FOLLOW ME: every follower is re-ordered to the player's position (every 3 s from the cheats tick).</summary>
        private void TickFollowers()
        {
            if (followers.Count == 0 || !GameAccess.IsServer() || !LocalAircraft(out Aircraft me)) return;
            followers.RemoveWhere(unit => unit == null || unit.disabled);
            GlobalPosition at = me.GlobalPosition();
            foreach (Unit unit in followers) Order(unit, at);
        }
    }
}
