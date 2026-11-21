using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.SeaStructures.Configuration;
using BoscaliSummer.Modules.SeaStructures.Domain;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>
    /// Every peer keeps the two prefabs registered. The host builds the mission's structures once, a little after the
    /// mission starts: forts close off held airbases, rigs further out, each owned by that airbase's side.
    /// </summary>
    internal sealed class SeaStructureService : MonoBehaviour, ISceneService
    {
        public const string NamePrefix = "SEA#";
        private const float SeedDelay = 30f, Spacing = 3000f, UnitClearance = 1500f;

        public static SeaStructureService Active { get; private set; }
        public bool Seeded => seeded;
        public bool WaitingForPlayer => waitingForPlayer;

        private SeaStructureSettings settings;
        private bool seeded, waitingForPlayer;
        private float runningSince = -1f;
        private int serial;

        public void Configure(SeaStructureSettings value) => settings = value;

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        public void ResetForScene()
        {
            seeded = false;
            runningSince = -1f;
        }

        private void Update()
        {
            SeaTemplates.EnsureRegistered();
            if (seeded || settings == null || !MissionManager.IsRunning || !GameAccess.IsServer()) { if (!MissionManager.IsRunning) runningSince = -1f; return; }
            if (runningSince < 0f) runningSince = Time.time;
            if (Time.time - runningSince < SeedDelay) return;
            // A listen host sitting in the aircraft menu has not chosen a spawn point yet. Seeding then can put
            // a platform exactly where they later appear; wait until their first unit exists.
            if (!LocalPlayerReady())
            {
                if (!waitingForPlayer) Debug.Log("[SeaStructures] waiting for the local player to spawn before seeding");
                waitingForPlayer = true;
                return;
            }
            waitingForPlayer = false;
            seeded = true;
            try { Seed(); }
            catch (Exception error) { Debug.LogError("[SeaStructures] seeding failed: " + error); }
        }

        /// <summary>A local player who is still in the menu has no unit yet; a dedicated host has none at all.</summary>
        private static bool LocalPlayerReady()
        {
            // nomodkit scenarios drive their own owner aircraft and are not attached to the menu player.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return true;
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null) return true;
            if (player.AircraftSpawnPending) return false;
            return player.Aircraft != null || player.UnitID.HasValue;
        }

        private void Seed()
        {
            var bases = new List<Airbase>();
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                    if (airbase != null && airbase.CurrentHQ != null && airbase.GetComponent<Unit>() == null) bases.Add(airbase);
            if (bases.Count == 0) return;
            bases.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var taken = new List<Vector3>();
            int forts = Build(SeaKind.SeaFort, settings.SeaForts.Value, 3500f, 9000f, bases, taken, 0);
            int rigs = Build(SeaKind.OilRig, settings.OilRigs.Value, 8000f, 16000f, bases, taken, bases.Count / 2);
            Debug.Log("[SeaStructures] built " + forts + " sea forts and " + rigs + " oil platforms");
        }

        private int Build(SeaKind kind, int count, float near, float far, List<Airbase> bases, List<Vector3> taken, int first)
        {
            int built = 0;
            for (int i = 0; i < bases.Count && built < count; i++)
            {
                Airbase airbase = bases[(first + i) % bases.Count];
                if (!FindSite(airbase.transform.position, near, far, taken, out Vector3 site)) continue;
                if (Spawn(kind, site.ToGlobalPosition(), UnityEngine.Random.Range(0, 8) * 45f, airbase.CurrentHQ) == null) break;
                taken.Add(site);
                built++;
            }
            return built;
        }

        /// <summary>Open water with depth under it and no shore within 300 m, in a ring round a point, clear of other sites.</summary>
        public static bool FindSite(Vector3 centre, float near, float far, List<Vector3> taken, out Vector3 site)
        {
            float start = UnityEngine.Random.Range(0f, 360f);
            for (float r = near; r <= far; r += 1500f)
                for (int b = 0; b < 12; b++)
                {
                    float a = (start + b * 30f) * Mathf.Deg2Rad;
                    var p = new Vector3(centre.x + Mathf.Sin(a) * r, Datum.LocalSeaY, centre.z + Mathf.Cos(a) * r);
                    if (!Clear(p, taken) || !ClearOfUnits(p) || !OpenWater(p, 12f)) continue;
                    bool open = true;
                    for (int k = 0; k < 8 && open; k++)
                    {
                        float ka = k * 45f * Mathf.Deg2Rad;
                        open = OpenWater(p + new Vector3(Mathf.Sin(ka), 0f, Mathf.Cos(ka)) * 300f, 4f);
                    }
                    if (!open) continue;
                    site = p;
                    return true;
                }
            site = default;
            return false;
        }

        private static bool Clear(Vector3 p, List<Vector3> taken)
        {
            if (taken != null)
                foreach (Vector3 t in taken)
                    if ((t - p).sqrMagnitude < Spacing * Spacing) return false;
            return true;
        }

        /// <summary>Never put a platform on top of a ship, aircraft, vehicle or the local player.</summary>
        private static bool ClearOfUnits(Vector3 p)
        {
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit == null) continue;
                Vector3 at = unit.transform.position;
                float dx = at.x - p.x, dz = at.z - p.z;
                if (dx * dx + dz * dz < UnitClearance * UnitClearance) return false;
            }
            return true;
        }

        private static bool OpenWater(Vector3 local, float depth)
        {
            var from = new Vector3(local.x, Datum.LocalSeaY + 3000f, local.z);
            return !(Physics.Raycast(from, Vector3.down, out RaycastHit hit, 3600f, PhysicsLayers.StaticsMask) && hit.point.y > Datum.LocalSeaY - depth);
        }

        /// <summary>Host: one structure standing at sea level. Spawner.SpawnShip by hand, because the name must be set before the spawn.</summary>
        public Ship Spawn(SeaKind kind, GlobalPosition at, float heading, FactionHQ hq)
        {
            GameObject template = SeaTemplates.Of(kind);
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (template == null || spawner == null || !GameAccess.IsServer()) return null;
            SeaTemplates.EnsureRegistered();
            Vector3 local = at.ToLocalPosition();
            local.y = Datum.LocalSeaY;
            Quaternion rotation = Quaternion.Euler(0f, heading, 0f);
            GameObject go = Instantiate(template);
            go.transform.SetPositionAndRotation(local, rotation);
            Ship ship = go.GetComponent<Ship>();
            ship.NetworkHQ = hq;
            ship.NetworkUniqueName = NamePrefix + (int)kind + ":" + serial++ + ":" + Guid.NewGuid().ToString("N").Substring(0, 6);
            ship.NetworkstartPosition = local.ToGlobalPosition();
            ship.NetworkstartRotation = rotation;
            ship.NetworkunitName = SeaLayouts.Of(kind).UnitName;
            ship.skill = hq != null ? hq.surfaceSkillMultiplier : 1f;
            ship.SetHoldPosition(true);
            if (ship.TryGetComponent(out Airbase airbase)) airbase.SetupAttachedAirbase(ship);
            spawner.ServerObjectManager.Spawn(go.GetComponent<NetworkIdentity>());
            return ship;
        }
    }
}
