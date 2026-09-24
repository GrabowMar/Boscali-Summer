using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Fires Chimera paratroops and Ibis fast-rope from the troops weapon trigger. Only the
    /// server decides where a stick ends up; every peer with a screen just watches it drop.
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

        private readonly Dictionary<int, float> nextDropTimes = new Dictionary<int, float>();
        private int pendingDrops;
        private int doorWaits;
        private const int MaximumTrackedAircraft = 64;
        private const int MaximumPendingDrops = 16;
        private const int MaximumDoorWaits = 8;
        private const float MinFireInterval = 0.8f;
        private const float DoorOpenTimeout = 3.5f;
        private const float CargoDoorHoldSeconds = 10f;
        private const float FastRopeMaxHeight = 45f;
        /// <summary>The last pair of a squad leaves the door about this long after the first.</summary>
        private const float FastRopeStaggerSeconds = 2.5f;
        private static Airbase[] cachedAirbases;

        private void Awake()
        {
            Instance = this;
            // A module switched on after boot missed Encyclopedia.AfterLoad. WeaponLookup is only
            // set once that ran, and registering again is a no-op.
            if (Encyclopedia.WeaponLookup != null)
                ChimeraInfantryLoadoutAdapter.Register(Encyclopedia.i);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void ResetForScene()
        {
            StopAllCoroutines();
            pendingDrops = 0;
            doorWaits = 0;
            nextDropTimes.Clear();
            cachedAirbases = null;
            AirAssaultVisuals.ResetForScene();
        }

        /// <summary>
        /// Runs wherever the troops fire is replayed: the owner locally, the server through
        /// CmdLaunchMissile, observers through RpcLaunchMissile (twice for a remote owner). The
        /// per-aircraft window takes one stick per trigger, so every peer books the same troops.
        /// </summary>
        public void DeployFromWeaponStation(Aircraft aircraft, MountedTroops mountedTroops, WeaponStation station)
        {
            if (aircraft == null || !TroopAccountingAvailable) return;

            if (nextDropTimes.TryGetValue(aircraft.GetInstanceID(), out float readyAt) && Time.unscaledTime < readyAt)
                return;

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = def != null ? (def.unitName ?? def.jsonKey ?? "") : aircraft.name ?? "";

            bool isChimera = IsChimeraCarrier(name, def);
            if (!isChimera && !IsIbis(name, def)) return;

            if (mountedTroops == null)
                mountedTroops = aircraft.GetComponentInChildren<MountedTroops>();

            // Vanilla keeps its station index on the first troop mount, even when empty.
            int needed = isChimera ? 1 : TroopDeploymentMath.DefaultSquadSize;
            if (station != null)
                foreach (Weapon weapon in station.Weapons)
                    if (weapon is MountedTroops troops && troops.IsAttached() && troops.ammo >= needed)
                    {
                        mountedTroops = troops;
                        break;
                    }
            int aboard = mountedTroops != null ? Mathf.Max(0, mountedTroops.ammo) : 0;
            if (aboard < needed)
            {
                Plugin.Logger.LogInfo($"[Air Assault] Cannot deploy: {aboard} infantry remaining aboard {name}! Rearm at an airbase.");
                return;
            }

            if (isChimera)
                DeployParadrop(aircraft, mountedTroops, station, aboard);
            else
                DeployFastRope(aircraft, mountedTroops, station);
        }

        private void DeployParadrop(Aircraft aircraft, MountedTroops troops, WeaponStation station, int aboard)
        {
            // MC-260/Tarantula: one stick out the rear cargo access.
            int desired = Mathf.Clamp(Plugin.Settings.UrbanCombat.TroopsPerDeploy.Value, 2, TroopDeploymentMath.DefaultSquadSize);
            int dropCount = TroopDeploymentMath.ComputeDropSize(aboard, desired);
            Vector3 exit = ComputeCargoDropExitPosition(aircraft, dropCount);
            ConsumeTroops(aircraft, troops, station, dropCount);
            Throttle(aircraft, MinFireInterval);
            Plugin.Logger.LogInfo($"[CHIMERA] Paratrooper stick of {dropCount} exiting the rear cargo access at ({exit.x:0}, {exit.y:0}, {exit.z:0}). {troops.ammo} infantry remaining aboard.");

            if (GameAccess.IsServer())
            {
                // Server state only: straight down from the exit plus the mission wind the
                // canopies drift on, resolved once the slowest canopy is down.
                float ground = Physics.Raycast(exit, Vector3.down, out RaycastHit below, 6000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)
                    ? below.point.y : Datum.LocalSeaY;
                float descent = TroopDeploymentMath.DescentSeconds(exit.y - ground, TroopDeploymentMath.ParachuteDescentRate);
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                Vector3 wind = level != null ? level.GetWind() : Vector3.zero;
                GlobalPosition landing = (exit + new Vector3(wind.x, 0f, wind.z) * (0.75f * descent)).ToGlobalPosition();
                FactionHQ owner = aircraft.NetworkHQ;
                Airbase airbase = FindNearestAirbase(aircraft.transform.position);
                Schedule(descent, () => ResolveParadrop(landing, owner, airbase, dropCount));
            }

            ShowDrop(aircraft, troops, true, dropCount, default);
        }

        private static void ResolveParadrop(GlobalPosition landing, FactionHQ owner, Airbase airbase, int troopCount)
        {
            ZoneGarrisonManager garrisons = ZoneGarrisonManager.Instance;
            if (owner == null || garrisons == null) return;

            Vector3 point = landing.ToLocalPosition();
            if (!Physics.Raycast(point + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 6300f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                hit.point.y <= Datum.LocalSeaY + 0.5f)
            {
                Plugin.Logger.LogInfo($"[Paratroopers] Stick of {troopCount} came down at sea near ({point.x:0}, {point.z:0}); no position established.");
                return;
            }

            GameObject building = ResolveCivilianBuilding(hit.collider);
            if (building != null && garrisons.TryOccupyBuilding(building, owner))
            {
                Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratrooper squad ({troopCount} troops) secured and fortified building: {building.name}!");
                return;
            }

            // Garrisons off, shell already held or no roof fit: dig in where they landed.
            if (garrisons.TryDeployEncampment(hit.point, owner, airbase, troopCount))
                Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratroopers ({troopCount} troops) established combat encampment at ({hit.point.x:0}, {hit.point.z:0})!");
            else
                Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratroopers ({troopCount} troops) landed at ({hit.point.x:0}, {hit.point.z:0}) but found no position to hold.");
        }

        private void DeployFastRope(Aircraft aircraft, MountedTroops troops, WeaponStation station)
        {
            // UH-90 Ibis: fast-rope a squad from a low hover over a building or LZ.
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
            if (height > FastRopeMaxHeight)
            {
                Plugin.Logger.LogInfo($"[Air Assault] Altitude too high for fast-rope ({height:0}m). Fast-rope operations require hovering below {FastRopeMaxHeight:0}m. Descend closer to the rooftop or ground.");
                return;
            }

            GameObject shell = ResolveCivilianBuilding(hit.collider);
            int dropCount = TroopDeploymentMath.DefaultSquadSize;
            ConsumeTroops(aircraft, troops, station, dropCount);
            // One rappel at a time: the helicopter holds until its squad is on the ground.
            float descent = TroopDeploymentMath.DescentSeconds(height, TroopDeploymentMath.FastRopeDescentRate) + FastRopeStaggerSeconds;
            Throttle(aircraft, descent);

            if (shell != null)
                Plugin.Logger.LogInfo($"[IBIS] Fast-rope rappelling squadron of {dropCount} infantry descending to ({hit.point.x:0}, {hit.point.z:0}) on {shell.name}. {troops.ammo} infantry remaining aboard.");
            else
                Plugin.Logger.LogInfo($"[IBIS] Fast-rope rappelling squadron of {dropCount} infantry descending to LZ ({hit.point.x:0}, {hit.point.z:0}). {troops.ammo} infantry remaining aboard.");

            GlobalPosition landing = hit.point.ToGlobalPosition();
            if (GameAccess.IsServer())
            {
                FactionHQ owner = aircraft.NetworkHQ;
                Airbase airbase = FindNearestAirbase(origin);
                int landingShellId = shell != null ? shell.GetInstanceID() : 0;
                Schedule(descent, () => ResolveFastRope(aircraft, owner, airbase, landing, shell, landingShellId));
            }

            ShowDrop(aircraft, troops, false, dropCount, landing);
        }

        private void ResolveFastRope(Aircraft aircraft, FactionHQ owner, Airbase airbase, GlobalPosition landing, GameObject shell, int landingShellId)
        {
            if (owner == null) return;
            if (aircraft == null || aircraft.disabled || aircraft.NetworkHQ != owner)
            {
                Plugin.Logger.LogInfo("[AIR ASSAULT] Fast-rope insertion lost: the helicopter was lost before the squad reached the ground.");
                return;
            }
            if (landingShellId != 0 && (shell == null || !shell.activeInHierarchy ||
                shell.GetComponentInParent<Building>() is Building building && building.disabled))
            {
                Plugin.Logger.LogInfo("[AIR ASSAULT] Fast-rope insertion lost: the target building fell during the descent.");
                return;
            }

            Vector3 global = landing.AsVector3();
            Landed?.Invoke(owner.GetInstanceID(), global.x, global.z, landingShellId);
            bool deployed = InfantryEncampmentBuilder.DeployRappelEncampment(landing.ToLocalPosition(), owner, airbase);
            Plugin.Logger.LogInfo(deployed
                ? "[AIR ASSAULT] Eight troops established MG / AT / AA / MG encampment."
                : "[AIR ASSAULT] Encampment placement unavailable after insertion.");
        }

        private void Throttle(Aircraft aircraft, float seconds)
        {
            // Every entry is a gate of seconds; forgetting them only reopens gates early.
            if (nextDropTimes.Count >= MaximumTrackedAircraft)
                nextDropTimes.Clear();
            nextDropTimes[aircraft.GetInstanceID()] = Time.unscaledTime + seconds;
        }

        private void Schedule(float delay, Action outcome)
        {
            // Past the cap an outcome resolves at once rather than being dropped.
            if (pendingDrops >= MaximumPendingDrops)
            {
                outcome();
                return;
            }
            pendingDrops++;
            StartCoroutine(ResolveLater(delay, outcome));
        }

        private IEnumerator ResolveLater(float delay, Action outcome)
        {
            yield return new WaitForSeconds(delay);
            pendingDrops--;
            outcome();
        }

        private void ShowDrop(Aircraft aircraft, MountedTroops troops, bool paradrop, int count, GlobalPosition landing)
        {
            if (GameManager.IsHeadless) return;

            // Cosmetic: the troops are already booked. Doors animate open, then the stick exits.
            BayDoor[] doors = ResolveCargoDoors(aircraft, troops);
            if (doorWaits >= MaximumDoorWaits || IsCargoDoorOpen(aircraft, doors) || !BeginCargoAccess(aircraft, doors))
            {
                SpawnDropVisual(aircraft, paradrop, count, landing);
                return;
            }
            doorWaits++;
            StartCoroutine(OpenCargoAccessThenShow(aircraft, doors, paradrop, count, landing));
        }

        private IEnumerator OpenCargoAccessThenShow(Aircraft aircraft, BayDoor[] doors, bool paradrop, int count, GlobalPosition landing)
        {
            float deadline = Time.unscaledTime + DoorOpenTimeout;
            while (Time.unscaledTime < deadline && aircraft != null && !aircraft.disabled && !IsCargoDoorOpen(aircraft, doors))
                yield return new WaitForSeconds(0.1f);
            doorWaits--;
            if (aircraft != null && !aircraft.disabled)
                SpawnDropVisual(aircraft, paradrop, count, landing);
        }

        private static void SpawnDropVisual(Aircraft aircraft, bool paradrop, int count, GlobalPosition landing)
        {
            if (paradrop)
            {
                Vector3 exitVel = (aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero) - aircraft.transform.forward * 8f;
                AirAssaultVisuals.SpawnParatrooperCargoDrop(aircraft, ComputeCargoDropExitPosition(aircraft, count), exitVel, count);
            }
            else
            {
                AirAssaultVisuals.SpawnFastRopeRappelling(aircraft, landing.ToLocalPosition(), count);
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

        /// <summary>
        /// Vanilla never syncs weapon ammo: each peer replays the fire and updates its own copy
        /// (MountedMissile.Fire zeroes ammo and hardpoint mass everywhere), which feeds that
        /// peer's HUD, Ready() gate and flight mass. Troops follow suit. Capture strength is only
        /// read by the server's capture, so only the server moves it.
        /// </summary>
        private static void ConsumeTroops(Aircraft aircraft, MountedTroops troops, WeaponStation station, int count)
        {
            troops.ammo = Mathf.Max(0, troops.ammo - count);
            float removedMass = count * (troops.info != null ? troops.info.massPerRound : 0f);
            TroopMassField.SetValue(troops, (float)TroopMassField.GetValue(troops) - removedMass);
            Hardpoint hardpoint = TroopHardpointField.GetValue(troops) as Hardpoint;
            if (hardpoint != null) hardpoint.ModifyMass(-removedMass);
            if (GameAccess.IsServer())
            {
                CaptureStrengthField.SetValue(troops, (float)troops.ammo);
                if ((bool)CaptureActiveField.GetValue(troops)) aircraft.ModifyCaptureStrength(-count);
            }
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
        private static readonly MethodInfo BayDoorOpenDoorMethod =
            typeof(BayDoor).GetMethod("OpenDoor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static BayDoor[] ResolveCargoDoors(Aircraft aircraft, MountedTroops troops)
        {
            if (troops != null && TroopHardpointField != null)
            {
                Hardpoint hardpoint = TroopHardpointField.GetValue(troops) as Hardpoint;
                if (hardpoint != null && hardpoint.bayDoors != null && hardpoint.bayDoors.Length > 0)
                    return hardpoint.bayDoors;
            }

            return aircraft != null ? aircraft.GetComponentsInChildren<BayDoor>(true) : null;
        }

        private static bool BeginCargoAccess(Aircraft aircraft, BayDoor[] doors)
        {
            if (aircraft == null) return false;

            bool opened = false;
            CargoRamp ramp = aircraft.GetComponentInChildren<CargoRamp>(true);
            if (ramp != null && InvokeDoorOpen(ramp)) opened = true;

            if (doors != null)
            {
                for (int i = 0; i < doors.Length; i++)
                {
                    BayDoor door = doors[i];
                    if (door == null || ReferenceEquals(door, ramp)) continue;
                    if (InvokeDoorOpen(door)) opened = true;
                }
            }

            return opened;
        }

        private static bool InvokeDoorOpen(BayDoor door)
        {
            try
            {
                if (BayDoorOpenDoorMethod != null)
                {
                    BayDoorOpenDoorMethod.Invoke(door, new object[] { CargoDoorHoldSeconds });
                    return true;
                }
            }
            catch
            {
                // ignore; the stick exits without the door animation
            }

            return false;
        }

        private static bool IsCargoDoorOpen(Aircraft aircraft, BayDoor[] doors)
        {
            if (aircraft == null) return true;

            // CargoRamp animates its own hinge; the inherited openAmount stays unused.
            CargoRamp ramp = aircraft.GetComponentInChildren<CargoRamp>(true);
            if (ramp != null)
            {
                float amt = CargoRampOpenAmountField != null ? (float)CargoRampOpenAmountField.GetValue(ramp) : 1f;
                return amt >= 0.85f || ramp.IsOpen();
            }

            if (doors != null && doors.Length > 0)
            {
                for (int i = 0; i < doors.Length; i++)
                {
                    if (doors[i] == null) continue;
                    float amt = BayDoorOpenAmountField != null ? (float)BayDoorOpenAmountField.GetValue(doors[i]) : 1f;
                    if (amt >= 0.8f) return true;
                }
                return false;
            }

            return true;
        }
    }
}
