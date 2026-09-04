using System;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Fires Chimera paratroops and Ibis fast-rope from the troops weapon trigger.
    /// </summary>
    internal sealed class AirAssaultController : MonoBehaviour, ISceneService
    {
        public static AirAssaultController Instance { get; private set; }

        private float nextDropTime;
        private const float MinFireInterval = 0.8f;
        private static Airbase[] cachedAirbases;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void ResetForScene()
        {
            nextDropTime = 0f;
            cachedAirbases = null;
            AirAssaultVisuals.ResetForScene();
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

            if (!isChimera && !isIbis) return;

            // Check if cargo ramp / bay door is open
            if (!IsCargoDoorOpen(aircraft, out string doorReason))
            {
                Plugin.Logger.LogInfo($"[Air Assault] Cannot deploy: {doorReason}");
                return;
            }

            // Check stored infantry count
            if (mountedTroops == null)
                mountedTroops = aircraft.GetComponentInChildren<MountedTroops>();

            int aboard = mountedTroops != null ? Mathf.Max(0, mountedTroops.ammo)
                : Plugin.Settings.UrbanCombat.TroopsPerDeploy.Value;
            if (aboard <= 0)
            {
                Plugin.Logger.LogInfo($"[Air Assault] Cannot deploy: 0 infantry remaining aboard {name}! Rearm at an airbase.");
                return;
            }

            FactionHQ owner = aircraft.NetworkHQ;
            Airbase airbase = FindNearestAirbase(aircraft.transform.position);

            if (isChimera)
            {
                // MC-260 Chimera: Deploy out rear cargo hold ramp like cargo/vehicles
                if (mountedTroops != null)
                    mountedTroops.ammo = Mathf.Max(0, mountedTroops.ammo - 1);

                nextDropTime = Time.unscaledTime + MinFireInterval;
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

                // Fast-rope deploys a fixed squad per trigger. The drop only costs the
                // infantry once the insertion is confirmed feasible.
                int dropCount = TroopDeploymentMath.ComputeDropSize(aboard, Plugin.Settings.UrbanCombat.TroopsPerDeploy.Value);
                if (mountedTroops != null)
                    mountedTroops.ammo = Mathf.Max(0, mountedTroops.ammo - dropCount);

                nextDropTime = Time.unscaledTime + MinFireInterval;
                Plugin.Logger.LogInfo($"[IBIS] Fast-rope rappelling squadron of {dropCount} infantry descending to ({hit.point.x:0}, {hit.point.z:0}). {mountedTroops?.ammo ?? 0} infantry remaining aboard.");

                GameObject shell = ResolveCivilianBuilding(hit.collider);
                if (shell != null)
                {
                    AirAssaultVisuals.SpawnFastRopeRappelling(aircraft, hit.point, owner, dropCount, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryOccupyBuilding(shell, owner, airbase);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Fast-rope squadron secured and fortified {shell.name}!");
                    });
                }
                else
                {
                    AirAssaultVisuals.SpawnFastRopeRappelling(aircraft, hit.point, owner, dropCount, () =>
                    {
                        ZoneGarrisonManager.Instance?.TryDeployEncampment(hit.point, owner, airbase, dropCount);
                        Plugin.Logger.LogInfo($"[AIR ASSAULT] Fast-rope squadron of {dropCount} established combat encampment at ({hit.point.x:0}, {hit.point.z:0})!");
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
            if (cachedAirbases == null || cachedAirbases.Length == 0)
            {
                if (FactionRegistry.airbaseLookup != null && FactionRegistry.airbaseLookup.Count > 0)
                {
                    var values = FactionRegistry.airbaseLookup.Values;
                    cachedAirbases = new Airbase[values.Count];
                    values.CopyTo(cachedAirbases, 0);
                }
                else
                {
                    cachedAirbases = UnityEngine.Object.FindObjectsOfType<Airbase>();
                }
            }
            if (cachedAirbases == null || cachedAirbases.Length == 0) return null;

            Airbase best = null;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < cachedAirbases.Length; i++)
            {
                Airbase ab = cachedAirbases[i];
                if (ab == null || !ab.gameObject.scene.IsValid() || ab.AttachedAirbase) continue;
                Vector3 center = ab.center != null ? ab.center.position : ab.transform.position;
                float dSq = (center - pos).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = ab;
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
