using System;
using System.Reflection;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Fires Chimera paratroops and Ibis fast-rope from the troops weapon trigger.
    /// </summary>
    internal sealed class AirAssaultController : MonoBehaviour, ISceneService, IAirAssaultObservation
    {
        public static AirAssaultController Instance { get; private set; }
        public bool Available => TroopAccountingAvailable;
        public event Action<int, float, float, int> Landed;
        public bool TryRooftop(float x, float z, out int shellId, out float roofX, out float roofZ)
        {
            shellId = 0; roofX = roofZ = 0f;
            return Available && ZoneGarrisonManager.Instance != null &&
                ZoneGarrisonManager.Instance.TryMissionRooftop(x, z, out shellId, out roofX, out roofZ);
        }
        public bool IsRooftopAvailable(int shellId) => ZoneGarrisonManager.Instance != null &&
            ZoneGarrisonManager.Instance.IsMissionRooftopAvailable(shellId);

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

        public void DeployFromWeaponStation(Aircraft aircraft, MountedTroops mountedTroops, Vector3 inheritedVelocity, WeaponStation station)
        {
            if (aircraft == null || !TroopAccountingAvailable) return;

            if (Time.unscaledTime < nextDropTime)
                return;

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = def != null ? (def.unitName ?? def.jsonKey ?? "") : aircraft.name ?? "";

            bool isChimera = IsChimeraCarrier(name, def);
            bool isIbis = IsIbis(name, def);

            if (!isChimera && !isIbis) return;
            if (AirAssaultVisuals.HasActiveRappel(aircraft) || !AirAssaultVisuals.HasOperationCapacity) return;

            // Ensure door state and auto-open for immediate paradrop/roping.
            if (!IsCargoDoorOpen(aircraft, out string doorReason))
            {
                if (ForceOpenCargoAccess(aircraft, out string openFailure))
                {
                    Plugin.Logger.LogInfo("[Air Assault] Opening cargo access for paradrop/fast-rope sequence.");
                }
                else if (openFailure != null)
                {
                    Plugin.Logger.LogInfo($"[Air Assault] Could not auto-open cargo access doors: {openFailure}");
                }

                if (!IsCargoDoorOpen(aircraft, out doorReason))
                {
                    Plugin.Logger.LogInfo($"[Air Assault] Cannot deploy: {doorReason}");
                    return;
                }
            }

            // Check stored infantry count
            if (mountedTroops == null)
                mountedTroops = aircraft.GetComponentInChildren<MountedTroops>();

            // Vanilla keeps its station index on the first troop mount, even when empty.
            if (station != null)
                foreach (Weapon weapon in station.Weapons)
                    if (weapon is MountedTroops troops && troops.IsAttached() && troops.ammo >= (isIbis ? TroopDeploymentMath.DefaultSquadSize : 1))
                    {
                        mountedTroops = troops;
                        break;
                    }
            int aboard = mountedTroops != null ? Mathf.Max(0, mountedTroops.ammo) : 0;
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
                int dropCount = TroopDeploymentMath.ComputeDropSize(aboard, Plugin.Settings.UrbanCombat.TroopsPerDeploy.Value);
                if (mountedTroops != null)
                    ConsumeTroops(aircraft, mountedTroops, station, dropCount);

                nextDropTime = Time.unscaledTime + MinFireInterval;
                Vector3 rampPos = ComputeCargoDropExitPosition(aircraft, dropCount);
                Vector3 exitVel = inheritedVelocity - aircraft.transform.forward * 8f;

                Plugin.Logger.LogInfo($"[CHIMERA] Paratrooper company of {dropCount} launched from rear cargo hold ramp at ({rampPos.x:0}, {rampPos.y:0}, {rampPos.z:0}). {mountedTroops?.ammo ?? 0} infantry remaining aboard.");
                AirAssaultVisuals.SpawnParatrooperCargoDrop(aircraft, rampPos, exitVel, owner, airbase, dropCount);
            }
            else
            {
                // UH-90 Ibis: Fast-rope rappelling (requires low hover directly over building)
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

                // Fast-rope deploys a squad per trigger. The drop only costs the
                // infantry once the insertion is confirmed feasible.
                int dropCount = TroopDeploymentMath.DefaultSquadSize;
                if (aboard < dropCount) return;
                ConsumeTroops(aircraft, mountedTroops, station, dropCount);

                nextDropTime = Time.unscaledTime + MinFireInterval;
                if (shell != null)
                    Plugin.Logger.LogInfo($"[IBIS] Fast-rope rappelling squadron of {dropCount} infantry descending to ({hit.point.x:0}, {hit.point.z:0}) on {shell.name}. {mountedTroops?.ammo ?? 0} infantry remaining aboard.");
                else
                    Plugin.Logger.LogInfo($"[IBIS] Fast-rope rappelling squadron of {dropCount} infantry descending to LZ ({hit.point.x:0}, {hit.point.z:0}). {mountedTroops?.ammo ?? 0} infantry remaining aboard.");

                GlobalPosition landing = hit.point.ToGlobalPosition();
                int landingShellId = shell != null ? shell.GetInstanceID() : 0;
                AirAssaultVisuals.SpawnFastRopeRappelling(aircraft, hit.point, owner, dropCount, () =>
                {
                    if (!GameAccess.IsServer() || owner == null || aircraft == null || aircraft.disabled || aircraft.NetworkHQ != owner) return;
                    if (landingShellId != 0 && (shell == null || !shell.activeInHierarchy ||
                        shell.GetComponentInParent<Building>() is Building building && building.disabled)) return;
                    Vector3 global = landing.AsVector3();
                    Landed?.Invoke(owner.GetInstanceID(), global.x, global.z, landingShellId);
                    bool deployed = InfantryEncampmentBuilder.DeployRappelEncampment(landing.ToLocalPosition(), owner, airbase);
                    Plugin.Logger.LogInfo(deployed
                        ? "[AIR ASSAULT] Eight troops established MG / AT / AA / MG encampment."
                        : "[AIR ASSAULT] Encampment placement unavailable after insertion.");
                });
            }
        }

        private static bool IsChimeraCarrier(string name, AircraftDefinition def)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("chimera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("mc260", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("mc-260", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("tarantula", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("tarantulla", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("aryx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (def != null && def.jsonKey != null && def.jsonKey.IndexOf("chimera", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static readonly FieldInfo CaptureStrengthField = HarmonyLib.AccessTools.Field(typeof(MountedTroops), "captureStrength");
        private static readonly FieldInfo CaptureActiveField = HarmonyLib.AccessTools.Field(typeof(MountedTroops), "captureActive");
        private static readonly FieldInfo TroopMassField = HarmonyLib.AccessTools.Field(typeof(MountedTroops), "mass");
        private static readonly FieldInfo TroopHardpointField = HarmonyLib.AccessTools.Field(typeof(Weapon), "hardpoint");

        internal static bool TroopAccountingAvailable => CaptureStrengthField != null &&
            CaptureActiveField != null && TroopMassField != null && TroopHardpointField != null;

        private static void ConsumeTroops(Aircraft aircraft, MountedTroops troops, WeaponStation station, int count)
        {
            troops.ammo = Mathf.Max(0, troops.ammo - count);
            CaptureStrengthField.SetValue(troops, (float)troops.ammo);
            if ((bool)CaptureActiveField.GetValue(troops)) aircraft.ModifyCaptureStrength(-count);
            float removedMass = count * (troops.info != null ? troops.info.massPerRound : 0f);
            TroopMassField.SetValue(troops, (float)TroopMassField.GetValue(troops) - removedMass);
            (TroopHardpointField.GetValue(troops) as Hardpoint)?.ModifyMass(-removedMass);
            // Each native Ibis mount is one eight-man bench. Keep its weapon alive
            // for station bookkeeping while removing the disembarked passengers.
            if (troops.ammo == 0)
                foreach (Renderer renderer in troops.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            station?.AccountAmmo();
        }

        private static Vector3 ComputeCargoDropExitPosition(Aircraft aircraft, int dropCount)
        {
            if (aircraft == null) return Vector3.zero;

            Transform anchor = null;
            float rearOffset = Mathf.Lerp(0f, 2f, Mathf.InverseLerp(1, 16, dropCount));

            CargoRamp ramp = aircraft.GetComponentInChildren<CargoRamp>(true);
            if (ramp != null && ramp.transform != null)
            {
                anchor = ramp.transform;
            }
            else
            {
                BayDoor[] bayDoors = aircraft.GetComponentsInChildren<BayDoor>(true);
                if (bayDoors != null && bayDoors.Length > 0)
                {
                    float best = float.MaxValue;
                    Vector3 aircraftPos = aircraft.transform.position;
                    Vector3 forward = aircraft.transform.forward;
                    for (int i = 0; i < bayDoors.Length; i++)
                    {
                        if (bayDoors[i] == null) continue;
                        Transform candidate = bayDoors[i].transform;
                        if (candidate == null) continue;

                        float score = Vector3.Dot(candidate.position - aircraftPos, forward);
                        if (score < best)
                        {
                            best = score;
                            anchor = candidate;
                        }
                    }
                }
            }

            if (anchor != null)
            {
                Vector3 backward = -aircraft.transform.forward * (5f + rearOffset);
                Vector3 dropOffset = backward + -aircraft.transform.up * (1.4f + 0.08f * dropCount);
                return anchor.position + dropOffset;
            }

            return aircraft.transform.position - aircraft.transform.forward * (11f + rearOffset) - aircraft.transform.up * 1.8f;
        }

        private static bool IsIbis(string name, AircraftDefinition def)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("tarantula", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("tarantulla", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

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

        private static readonly FieldInfo BayDoorOpenAmountField =
            typeof(BayDoor).GetField("openAmount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CargoRampOpenAmountField =
            typeof(CargoRamp).GetField("openAmount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BayDoorOpenMethod =
            typeof(BayDoor).GetMethod("Open", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BayDoorSetOpenMethod =
            typeof(BayDoor).GetMethod("SetOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo CargoRampOpenMethod =
            typeof(CargoRamp).GetMethod("Open", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo CargoRampSetOpenMethod =
            typeof(CargoRamp).GetMethod("SetOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool ForceOpenCargoAccess(Aircraft aircraft, out string failureReason)
        {
            failureReason = null;
            if (aircraft == null) return false;

            bool forced = false;
            bool hasAccessDevice = false;

            CargoRamp ramp = aircraft.GetComponentInChildren<CargoRamp>(true);
            if (ramp != null)
            {
                hasAccessDevice = true;
                bool rampChanged = SetOpenAmount(ramp, CargoRampOpenAmountField, 1f) | TryInvokeOpenMethod(ramp, CargoRampOpenMethod, true) | TryInvokeOpenMethod(ramp, CargoRampSetOpenMethod, true);
                if (rampChanged) forced = true;
            }

            BayDoor[] bayDoors = aircraft.GetComponentsInChildren<BayDoor>(true);
            if (bayDoors != null && bayDoors.Length > 0)
            {
                hasAccessDevice = true;
                for (int i = 0; i < bayDoors.Length; i++)
                {
                    BayDoor door = bayDoors[i];
                    if (door == null) continue;
                    bool doorChanged = SetOpenAmount(door, BayDoorOpenAmountField, 1f) | TryInvokeOpenMethod(door, BayDoorOpenMethod, true) | TryInvokeOpenMethod(door, BayDoorSetOpenMethod, true);
                    if (doorChanged) forced = true;
                }
            }

            if (!hasAccessDevice)
                failureReason = "No cargo ramp or bay doors found on this aircraft.";
            else if (!forced)
                failureReason = "Cargo access controls were found but could not be driven open automatically.";

            return forced;
        }

        private static bool SetOpenAmount(object component, FieldInfo field, float amount)
        {
            if (component == null || field == null) return false;

            try
            {
                field.SetValue(component, amount);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeOpenMethod(object component, MethodInfo explicitMethod, bool open = true)
        {
            try
            {
                if (explicitMethod != null)
                {
                    ParameterInfo[] p = explicitMethod.GetParameters();
                    if (p.Length == 0)
                    {
                        explicitMethod.Invoke(component, null);
                        return true;
                    }
                    if (p.Length == 1)
                    {
                        if (p[0].ParameterType == typeof(bool))
                        {
                            explicitMethod.Invoke(component, new object[] { open });
                            return true;
                        }
                        if (p[0].ParameterType == typeof(float))
                        {
                            explicitMethod.Invoke(component, new object[] { 1f });
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

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
