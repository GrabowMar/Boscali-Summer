using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Controls air assault insertions:
    /// - Drops Paratroopers from the MC-260 Chimera transport plane.
    /// - Deploys Fast-Rope Rappelling infantry squads from the UH-90 Ibis helicopter,
    ///   consuming infantry stored in the helicopter.
    /// - If executed above civilian buildings: infantry fast-ropes/parachutes in and takes
    ///   the building as a fortified stronghold (rooftop AA, ground bunkers, markings).
    /// - If executed above open ground / terrain: infantry establishes a combat encampment
    ///   (sandbag bunkers, heavy MGs, ATGMs, MANPADS).
    /// </summary>
    internal sealed class AirAssaultController : MonoBehaviour, ISceneService
    {
        public static AirAssaultController Instance { get; private set; }

        private float nextDropTime;
        private const float Cooldown = 10f;
        private KeyCode airAssaultKey = KeyCode.J;
        private bool loggedActive;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void ResetForScene()
        {
            nextDropTime = 0f;
            loggedActive = false;
        }

        private void Update()
        {
            if (!loggedActive)
            {
                loggedActive = true;
                Plugin.Logger.LogInfo("[Air Assault] Controller active. Deploy troops via [J] or firing Troops weapon station.");
            }

            if (Input.GetKeyDown(airAssaultKey))
            {
                Aircraft local = GetLocalAircraft();
                if (local != null)
                {
                    Plugin.Logger.LogInfo($"[Air Assault] Key [J] pressed while piloting {local.name}.");
                    TriggerAirAssault(local, null);
                }
                else
                {
                    Plugin.Logger.LogInfo("[Air Assault] Key [J] pressed but local aircraft could not be found.");
                }
            }
        }

        public void TriggerAirAssault(Aircraft aircraft, MountedTroops mountedTroops)
        {
            if (aircraft == null) return;

            if (Time.unscaledTime < nextDropTime)
            {
                float rem = nextDropTime - Time.unscaledTime;
                Plugin.Logger.LogInfo($"[Air Assault] Cooldown active ({rem:0.#}s remaining).");
                return;
            }

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = def != null ? (def.unitName ?? def.jsonKey ?? "") : "";

            bool isChimera = IsChimera(name, def);
            bool isIbis = IsIbis(name, def);

            // If not specifically Chimera or Ibis, still allow if CanSlingLoad or helicopter
            if (!isChimera && !isIbis)
            {
                if (def != null && def.CanSlingLoad) isIbis = true;
                else
                {
                    Plugin.Logger.LogInfo($"[Air Assault] Aircraft {name} does not support air assault. Chimera or Ibis required.");
                    return;
                }
            }

            // Check stored infantry aboard the helicopter
            if (isIbis)
            {
                if (mountedTroops == null)
                    mountedTroops = aircraft.GetComponentInChildren<MountedTroops>();

                if (mountedTroops != null)
                {
                    int ammo = mountedTroops.ammo;
                    if (ammo <= 0)
                    {
                        Plugin.Logger.LogInfo("[Air Assault] Cannot deploy: 0 infantry squads stored in helicopter! Return to base to embark troops.");
                        return;
                    }

                    // Consume one stored squad from the helicopter
                    mountedTroops.ammo--;
                    Plugin.Logger.LogInfo($"[Air Assault] Deployed 1 infantry squad from helicopter. {mountedTroops.ammo} squad(s) remaining aboard.");
                }
                else
                {
                    Plugin.Logger.LogInfo("[Air Assault] Deploying tactical infantry squad from cabin transport bay.");
                }
            }

            // Raycast down to surface
            Vector3 origin = aircraft.transform.position;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3500f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
            {
                Plugin.Logger.LogInfo("[Air Assault] Aborted: No solid surface detected below.");
                return;
            }

            if (hit.point.y <= Datum.LocalSeaY + 1f)
            {
                Plugin.Logger.LogInfo("[Air Assault] Aborted: Cannot deploy over open water.");
                return;
            }

            nextDropTime = Time.unscaledTime + Cooldown;
            FactionHQ owner = aircraft.NetworkHQ;
            Airbase airbase = FindNearestAirbase(hit.point);

            GameObject shell = ResolveCivilianBuilding(hit.collider);

            if (shell != null)
            {
                // TARGET: CIVILIAN BUILDING TAKEOVER
                if (isChimera)
                {
                    Plugin.Logger.LogInfo($"[CHIMERA] Paratroopers airdropped over building {shell.name}!");
                    AirAssaultVisuals.SpawnParatrooperDrop(origin, hit.point, aircraft.transform.rotation, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryOccupyBuilding(shell, owner, airbase);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratroopers secured and fortified {shell.name}!");
                    });
                }
                else
                {
                    Plugin.Logger.LogInfo($"[IBIS] Fast-rope rappelling squad inserting onto building {shell.name}!");
                    AirAssaultVisuals.SpawnFastRopeRappelling(aircraft.transform, hit.point, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryOccupyBuilding(shell, owner, airbase);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Fast-rope squad secured and fortified {shell.name}!");
                    });
                }
            }
            else
            {
                // TARGET: OPEN GROUND COMBAT ENCAMPMENT
                if (isChimera)
                {
                    Plugin.Logger.LogInfo($"[CHIMERA] Paratroopers airdropped to establish ground combat encampment at ({hit.point.x:0}, {hit.point.z:0})!");
                    AirAssaultVisuals.SpawnParatrooperDrop(origin, hit.point, aircraft.transform.rotation, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryDeployEncampment(hit.point, owner, airbase);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratroopers established combat encampment at ({hit.point.x:0}, {hit.point.z:0})!");
                    });
                }
                else
                {
                    Plugin.Logger.LogInfo($"[IBIS] Fast-rope rappelling squad deploying ground combat encampment at ({hit.point.x:0}, {hit.point.z:0})!");
                    AirAssaultVisuals.SpawnFastRopeRappelling(aircraft.transform, hit.point, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryDeployEncampment(hit.point, owner, airbase);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Fast-rope squad established combat encampment at ({hit.point.x:0}, {hit.point.z:0})!");
                    });
                }
            }
        }

        private static bool IsChimera(string name, AircraftDefinition def)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("chimera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("mc260", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("mc-260", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (def != null && def.jsonKey != null && def.jsonKey.IndexOf("chimera", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsIbis(string name, AircraftDefinition def)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("ibis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("utilityhelo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("uh-90", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("uh-80", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (def != null && def.CanSlingLoad);
        }

        private static GameObject ResolveCivilianBuilding(Collider col)
        {
            if (col == null) return null;
            MapBuilding mb = col.GetComponentInParent<MapBuilding>();
            if (mb != null) return mb.gameObject;

            Building b = col.GetComponentInParent<Building>();
            if (b != null && b.definition is BuildingDefinition bDef && bDef.buildingType == BuildingType.CIV)
                return b.gameObject;

            return null;
        }

        private static Aircraft GetLocalAircraft()
        {
            if (GameManager.GetLocalPlayer<Player>(out var player) && player != null && player.Aircraft != null)
                return player.Aircraft;

            if (Camera.main != null)
            {
                Aircraft fromCam = Camera.main.GetComponentInParent<Aircraft>();
                if (fromCam != null) return fromCam;
            }

            Aircraft[] all = FindObjectsOfType<Aircraft>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && (all[i].LocalSim || all[i].IsLocalPlayer) && !all[i].disabled)
                    return all[i];
            }
            return null;
        }

        private static Airbase FindNearestAirbase(Vector3 pos)
        {
            Airbase[] all = Resources.FindObjectsOfTypeAll<Airbase>();
            Airbase best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].gameObject.scene.IsValid() || all[i].AttachedAirbase) continue;
                Vector3 center = all[i].center != null ? all[i].center.position : all[i].transform.position;
                float d = Vector3.Distance(center, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = all[i];
                }
            }
            return best;
        }
    }
}