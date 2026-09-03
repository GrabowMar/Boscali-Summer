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
    /// - Deploys Fast-Rope / Rope-sling infantry from the UH-90 Ibis helicopter.
    /// - If executed above civilian buildings: infantry takes and fortifies the building (rooftop AA, ground bunkers, markings).
    /// - If executed above open ground / terrain: infantry establishes a combat encampment (sandbags, MGs, ATGMs, MANPADS).
    /// </summary>
    internal sealed class AirAssaultController : MonoBehaviour, ISceneService
    {
        public static AirAssaultController Instance { get; private set; }

        private float nextDropTime;
        private const float Cooldown = 15f;
        private KeyCode airAssaultKey = KeyCode.J;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void ResetForScene()
        {
            nextDropTime = 0f;
        }

        private void Update()
        {
            if (!Input.GetKeyDown(airAssaultKey)) return;

            Aircraft aircraft = GetLocalAircraft();
            if (aircraft == null) return;

            if (Time.unscaledTime < nextDropTime)
            {
                float remaining = nextDropTime - Time.unscaledTime;
                Plugin.Logger.LogInfo($"[Air Assault] Recharging: {remaining:0.#}s");
                return;
            }

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = def != null ? (def.unitName ?? def.jsonKey ?? "") : "";

            bool isChimera = IsChimera(name, def);
            bool isIbis = IsIbis(name, def);

            // If not specifically Chimera or Ibis, still allow if CanSlingLoad or transport
            if (!isChimera && !isIbis)
            {
                if (def != null && def.CanSlingLoad) isIbis = true;
                else
                {
                    Plugin.Logger.LogInfo($"[Air Assault] Requires Chimera (plane) or Ibis (helo). Current: {name}");
                    return;
                }
            }

            Vector3 origin = aircraft.transform.position;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3500f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
            {
                Plugin.Logger.LogInfo("[Air Assault] Aborted: No clear ground or building below.");
                return;
            }

            if (hit.point.y <= Datum.LocalSeaY + 1f)
            {
                Plugin.Logger.LogInfo("[Air Assault] Aborted: Water landing zone.");
                return;
            }

            nextDropTime = Time.unscaledTime + Cooldown;
            FactionHQ owner = aircraft.NetworkHQ;
            Airbase airbase = FindNearestAirbase(hit.point);

            GameObject shell = ResolveCivilianBuilding(hit.collider);

            if (shell != null)
            {
                if (isChimera)
                {
                    Plugin.Logger.LogInfo($"[CHIMERA] Paratroopers dropped over {shell.name}!");
                    AirAssaultVisuals.SpawnParatrooperDrop(origin, hit.point, aircraft.transform.rotation, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryOccupyBuilding(shell, owner, airbase);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratroopers secured and fortified {shell.name}!");
                    });
                }
                else
                {
                    Plugin.Logger.LogInfo($"[IBIS] Fast-roping infantry onto {shell.name}!");
                    AirAssaultVisuals.SpawnFastRopeDeployment(aircraft.transform, hit.point, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryOccupyBuilding(shell, owner, airbase);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Fast-rope squad secured and fortified {shell.name}!");
                    });
                }
            }
            else
            {
                if (isChimera)
                {
                    Plugin.Logger.LogInfo("[CHIMERA] Paratroopers dropped to establish combat encampment!");
                    AirAssaultVisuals.SpawnParatrooperDrop(origin, hit.point, aircraft.transform.rotation, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryDeployEncampment(hit.point, owner, airbase);
                        Plugin.Logger.LogInfo("[AIR ASSAULT] Paratrooper combat encampment established!");
                    });
                }
                else
                {
                    Plugin.Logger.LogInfo("[IBIS] Fast-roping infantry to establish ground encampment!");
                    AirAssaultVisuals.SpawnFastRopeDeployment(aircraft.transform, hit.point, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryDeployEncampment(hit.point, owner, airbase);
                        Plugin.Logger.LogInfo("[AIR ASSAULT] Fast-rope squad established combat encampment!");
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
            Aircraft[] all = FindObjectsOfType<Aircraft>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].IsLocalPlayer && !all[i].disabled)
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