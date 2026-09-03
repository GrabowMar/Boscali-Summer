using System;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Controls air assault insertions:
    /// - Deployed strictly by selecting Troops/Paratroopers in the weapon stations menu and pulling
    ///   the normal fire trigger (no special J hotkey).
    /// - From MC-260 Chimera: Paratroopers deploy out of the rear cargo hold ramp like cargo/vehicles,
    ///   decelerating under the official ejected-pilot parachute canopy and lines.
    /// - From UH-90 Ibis: Fast-rope rappelling squad descends when hovering under 40m.
    /// </summary>
    internal sealed class AirAssaultController : MonoBehaviour, ISceneService
    {
        public static AirAssaultController Instance { get; private set; }

        private float nextDropTime;
        private const float MinFireInterval = 0.8f;
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
                Plugin.Logger.LogInfo("[Air Assault] Controller active. Select Troops/Paratroopers in weapons menu and pull trigger to deploy.");
            }
        }

        public void DeployFromWeaponStation(Aircraft aircraft, MountedTroops mountedTroops, Vector3 inheritedVelocity)
        {
            if (aircraft == null) return;

            if (Time.unscaledTime < nextDropTime)
                return;

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = def != null ? (def.unitName ?? def.jsonKey ?? "") : aircraft.name ?? "";

            bool isChimera = IsChimera(name, def);
            bool isIbis = IsIbis(name, def);

            if (!isChimera && !isIbis)
            {
                if (def != null && def.CanSlingLoad) isIbis = true;
                else isChimera = true; // allow other heavy transports
            }

            // Check if cargo ramp / bay door is open
            if (!IsCargoDoorOpen(aircraft, out string doorReason))
            {
                Plugin.Logger.LogInfo($"[Air Assault] Cannot deploy: {doorReason}");
                return;
            }

            // Check stored infantry count
            if (mountedTroops == null)
                mountedTroops = aircraft.GetComponentInChildren<MountedTroops>();

            if (mountedTroops != null)
            {
                int ammo = mountedTroops.ammo;
                if (ammo <= 0)
                {
                    Plugin.Logger.LogInfo($"[Air Assault] Cannot deploy: 0 squads remaining aboard {name}! Rearm at an airbase.");
                    return;
                }

                mountedTroops.ammo--;
                Plugin.Logger.LogInfo($"[Air Assault] Deployed 1 infantry squad from {name}. {mountedTroops.ammo} squad(s) remaining aboard.");
            }
            else
            {
                Plugin.Logger.LogInfo($"[Air Assault] Deploying tactical infantry squad from internal {name} bay.");
            }

            nextDropTime = Time.unscaledTime + MinFireInterval;
            FactionHQ owner = aircraft.NetworkHQ;
            Airbase airbase = FindNearestAirbase(aircraft.transform.position);

            if (isChimera)
            {
                // MC-260 Chimera: Deploy out rear cargo hold ramp like cargo/vehicles
                Vector3 rampPos = aircraft.transform.position - aircraft.transform.forward * 12f - aircraft.transform.up * 1.8f;
                Vector3 exitVel = inheritedVelocity - aircraft.transform.forward * 8f;

                Plugin.Logger.LogInfo($"[CHIMERA] Paratrooper squad launched from rear cargo hold ramp at ({rampPos.x:0}, {rampPos.y:0}, {rampPos.z:0}).");
                AirAssaultVisuals.SpawnParatrooperCargoDrop(aircraft, rampPos, exitVel, owner, airbase);
            }
            else
            {
                // UH-90 Ibis: Fast-rope rappelling (requires low hover)
                Vector3 origin = aircraft.transform.position;
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 500f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                {
                    Plugin.Logger.LogInfo("[Air Assault] Aborted: No surface detected below helicopter.");
                    return;
                }

                if (hit.point.y <= Datum.LocalSeaY + 1f)
                {
                    Plugin.Logger.LogInfo("[Air Assault] Aborted: Cannot fast-rope over open water.");
                    return;
                }

                float height = origin.y - hit.point.y;
                if (height > 45f)
                {
                    Plugin.Logger.LogInfo($"[Air Assault] Altitude too high for fast-rope ({height:0}m). Fast-rope operations require hovering below 40m. Descend closer to the rooftop or ground.");
                    return;
                }

                GameObject shell = ResolveCivilianBuilding(hit.collider);
                if (shell != null)
                {
                    Plugin.Logger.LogInfo($"[IBIS] Fast-rope rappelling squad inserting onto building {shell.name}!");
                    AirAssaultVisuals.SpawnFastRopeRappelling(aircraft.transform, hit.point, owner, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryOccupyBuilding(shell, owner, airbase);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Fast-rope squad secured and fortified {shell.name}!");
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

        private static readonly System.Reflection.FieldInfo BayDoorOpenAmountField =
            typeof(BayDoor).GetField("openAmount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo CargoRampOpenAmountField =
            typeof(CargoRamp).GetField("openAmount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static bool IsCargoDoorOpen(Aircraft aircraft, out string reason)
        {
            reason = null;
            if (aircraft == null) return true;

            // Check CargoRamp (used by Chimera and heavy transports)
            CargoRamp ramp = aircraft.GetComponentInChildren<CargoRamp>();
            if (ramp != null)
            {
                float amt = CargoRampOpenAmountField != null ? (float)CargoRampOpenAmountField.GetValue(ramp) : 1f;
                if (amt < 0.35f && !ramp.IsOpen())
                {
                    reason = "Cargo ramp is closed! Open the cargo ramp before deploying troops.";
                    return false;
                }
                return true;
            }

            // Check BayDoors (used by Ibis and cargo/troop bays)
            BayDoor[] bayDoors = aircraft.GetComponentsInChildren<BayDoor>();
            if (bayDoors != null && bayDoors.Length > 0)
            {
                bool anyOpen = false;
                for (int i = 0; i < bayDoors.Length; i++)
                {
                    if (bayDoors[i] != null)
                    {
                        float amt = BayDoorOpenAmountField != null ? (float)BayDoorOpenAmountField.GetValue(bayDoors[i]) : 1f;
                        if (amt > 0.35f)
                        {
                            anyOpen = true;
                            break;
                        }
                    }
                }
                if (!anyOpen)
                {
                    reason = "Cargo bay door is closed! Open the door before deploying troops.";
                    return false;
                }
            }

            return true;
        }
    }
}