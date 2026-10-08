using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Fires HALO paratroopers (Airborne.nobp mount) and Ibis fast-rope from the troops weapon trigger. Only the
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
        private int rappelWaves;
        private const int MaximumTrackedAircraft = 64;
        private const int MaximumPendingDrops = 16;
        private const int MaximumRappelWaves = 4;
        private const float MinFireInterval = 0.8f;
        private const float CargoDoorHoldSeconds = 10f;
        /// <summary>Exfil picks up a friendly position within this of the point below the helicopter.</summary>
        private const float ExfilRadius = 70f;
        private const float ExfilMaxSpeed = 8f;
        private static Airbase[] cachedAirbases;

        /// <summary>
        /// The HALO paratrooper mount from Airborne.nobp (modules/UrbanCombat/Assets/Editor/AirborneBuilder.cs).
        /// Blueprinter registers it and adds it to the Tarantula and Aryx Chimera cargo bays; without
        /// Blueprinter it does not exist.
        /// </summary>
        internal const string HaloMountKey = "AB_Paratroopers_x16";
        /// <summary>Ramp opening before the first pair goes; every peer and the server add the same lead.</summary>
        private const float HaloRampSeconds = 2.5f;

        private void Awake() => Instance = this;

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void ResetForScene()
        {
            StopAllCoroutines();
            pendingDrops = 0;
            rappelWaves = 0;
            nextDropTimes.Clear();
            cachedAirbases = null;
            AirAssaultVisuals.ResetForScene();
        }

        /// <summary>
        /// Runs wherever the troops fire is replayed: the owner locally, the server through
        /// CmdLaunchMissile, observers through RpcLaunchMissile (twice for a remote owner). The
        /// per-aircraft window takes one stick per trigger, so every peer books the same troops.
        /// </summary>
        public void DeployFromWeaponStation(Aircraft aircraft, MountedTroops mountedTroops, WeaponStation station, Unit target, GlobalPosition aimpoint)
        {
            if (aircraft == null || !TroopAccountingAvailable) return;

            if (nextDropTimes.TryGetValue(aircraft.GetInstanceID(), out float readyAt) && Time.unscaledTime < readyAt)
                return;

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = def != null ? (def.unitName ?? def.jsonKey ?? "") : aircraft.name ?? "";

            if (mountedTroops == null)
                mountedTroops = aircraft.GetComponentInChildren<MountedTroops>();

            // The mount decides: HALO sticks jump from anything carrying them; vanilla troop
            // benches only fast-rope from the Ibis and otherwise keep vanilla landing capture.
            bool halo = IsHalo(mountedTroops);
            if (!halo && !IsIbis(name, def)) return;

            // Vanilla keeps its station index on the first troop mount, even when empty.
            int needed = halo ? 1 : TroopDeploymentMath.DefaultSquadSize;
            if (station != null)
                foreach (Weapon weapon in station.Weapons)
                    if (weapon is MountedTroops troops && troops.IsAttached() && troops.ammo >= needed && IsHalo(troops) == halo)
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

            if (halo)
                DeployHalo(aircraft, mountedTroops, station, aboard, target, aimpoint);
            else
                DeployFastRope(aircraft, mountedTroops, station);
        }

        private static readonly FieldInfo WeaponMountField = HarmonyLib.AccessTools.Field(typeof(Weapon), "mount");

        private static bool IsHalo(MountedTroops troops) =>
            troops != null && WeaponMountField?.GetValue(troops) is WeaponMount mount && mount.jsonKey == HaloMountKey;

        private void DeployHalo(Aircraft aircraft, MountedTroops troops, WeaponStation station, int aboard, Unit target, GlobalPosition aimpoint)
        {
            int dropCount = TroopDeploymentMath.ComputeDropSize(aboard, Mathf.Clamp(Plugin.Settings.UrbanCombat.TroopsPerDeploy.Value, 2, HaloGlidePlan.MaxJumpers));
            Vector3 velocity = aircraft.rb != null ? aircraft.rb.velocity : aircraft.transform.forward * 60f;
            // Every peer predicts the same ramp point from replicated state; the stick leaves it once the ramp is down.
            Vector3 exit = ComputeCargoDropExitPosition(aircraft, dropCount) + velocity * HaloRampSeconds;
            Vector3? aim = HaloAim(aircraft, target, aimpoint);
            HaloGlidePlan plan = HaloGlidePlan.Build(exit.ToGlobalPosition().AsVector3(), velocity, aim, HaloGroundAt, dropCount);
            ConsumeTroops(aircraft, troops, station, dropCount);
            Throttle(aircraft, MinFireInterval);
            Vector3 landing = plan.StickLanding;
            Plugin.Logger.LogInfo($"[AIRBORNE] HALO stick of {dropCount} away from {aircraft.name}: " +
                (aim.HasValue ? "gliding onto the designated target" : "no designation, gliding down the heading") +
                $" ({(plan.LowDrop ? "low drop, canopies off the ramp" : "wingsuit glide")}), on the ground in {plan.TotalSeconds:0} s. {troops.ammo} aboard.");

            if (GameAccess.IsServer())
            {
                FactionHQ owner = aircraft.NetworkHQ;
                Airbase airbase = FindNearestAirbase(aircraft.transform.position);
                var ground = new GlobalPosition(landing.x, landing.y, landing.z);
                Schedule(HaloRampSeconds + plan.TotalSeconds, () => ResolveParadrop(ground, owner, airbase, dropCount));
            }

            if (GameManager.IsHeadless) return;
            BeginCargoAccess(aircraft, ResolveCargoDoors(aircraft, troops));
            AirAssaultVisuals.SpawnHaloDrop(plan, HaloRampSeconds);
        }

        /// <summary>The designated target in global coordinates, if any.</summary>
        private static Vector3? HaloAim(Aircraft aircraft, Unit target, GlobalPosition aimpoint)
        {
            if (target != null && !target.disabled)
                return target.GlobalPosition().AsVector3();
            // A point designation arrives as an aimpoint; an unset one sits at the origin or on the aircraft.
            Vector3 point = aimpoint.AsVector3();
            Vector3 self = aircraft.GlobalPosition().AsVector3();
            if (point.sqrMagnitude > 1f && new Vector2(point.x - self.x, point.z - self.z).sqrMagnitude > 200f * 200f)
                return point;
            return null;
        }

        /// <summary>Roof or ground height under a global point (sea level over water), in global metres.</summary>
        internal static float HaloGroundAt(Vector3 global)
        {
            Vector3 local = new GlobalPosition(global.x, global.y, global.z).ToLocalPosition();
            float sea = new Vector3(local.x, Datum.LocalSeaY, local.z).ToGlobalPosition().AsVector3().y;
            var from = new Vector3(local.x, Datum.LocalSeaY + 9000f, local.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 12000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                return Mathf.Max(sea, hit.point.ToGlobalPosition().AsVector3().y);
            return sea;
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
            // UH-90 Ibis: fast-rope (to 45 m) or rappel (to 120 m) a squad from a hover.
            Vector3 origin = aircraft.transform.position;
            if (DeckBelow(aircraft, out Ship ship, out Vector3 deck))
            {
                TryShipBoarding(aircraft, ship, deck);
                return;
            }
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, FastRopePlan.RappelMaxHeight + 400f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
            {
                Plugin.Logger.LogInfo("[Air Assault] Aborted: No surface detected below helicopter.");
                return;
            }
            if (hit.point.y <= Datum.LocalSeaY + 1f)
            {
                Plugin.Logger.LogInfo("[Air Assault] Aborted: Cannot rope down over open water.");
                return;
            }

            float height = origin.y - hit.point.y;
            RopeMode? mode = FastRopePlan.ModeFor(height);
            if (mode == null)
            {
                Plugin.Logger.LogInfo($"[Air Assault] {height:0} m is out of rope range: fast-rope below {FastRopePlan.FastRopeMaxHeight:0} m, rappel below {FastRopePlan.RappelMaxHeight:0} m.");
                return;
            }

            GameObject shell = ResolveCivilianBuilding(hit.collider);
            int dropCount = TroopDeploymentMath.DefaultSquadSize;
            ConsumeTroops(aircraft, troops, station, dropCount);
            float descent = FastRopePlan.InsertionSeconds(mode.Value, height, dropCount);
            // One insertion at a time: the helicopter holds until its squad is off the ropes.
            Throttle(aircraft, descent + 1f);
            HoverHold.Engage(aircraft, descent + 1f);
            string how = mode == RopeMode.FastRope ? "Fast-roping" : "Rappelling";
            Plugin.Logger.LogInfo($"[IBIS] {how} {dropCount} infantry {height:0} m down to ({hit.point.x:0}, {hit.point.z:0})" +
                (shell != null ? $" on {shell.name}" : "") + $"; off the ropes in {descent:0} s. {troops.ammo} infantry remaining aboard.");

            GlobalPosition landing = hit.point.ToGlobalPosition();
            if (GameAccess.IsServer())
            {
                FactionHQ owner = aircraft.NetworkHQ;
                Airbase airbase = FindNearestAirbase(origin);
                int landingShellId = shell != null ? shell.GetInstanceID() : 0;
                Schedule(descent, () => ResolveFastRope(aircraft, owner, airbase, landing, shell, landingShellId));
            }

            if (GameManager.IsHeadless) return;
            BeginCargoAccess(aircraft, ResolveCargoDoors(aircraft, troops));
            AirAssaultVisuals.SpawnRopeInsertion(aircraft, mode.Value, height, hit.point, shell, dropCount);
        }

        /// <summary>The first thing under the helicopter (its own colliders skipped) is a ship's deck.</summary>
        private static bool DeckBelow(Aircraft aircraft, out Ship ship, out Vector3 deck)
        {
            ship = null;
            deck = default;
            RaycastHit[] hits = Physics.RaycastAll(aircraft.transform.position, Vector3.down, FastRopePlan.RappelMaxHeight + 10f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponentInParent<Aircraft>() == aircraft) continue;
                ship = hit.collider.GetComponentInParent<Ship>();
                deck = hit.point;
                return ship != null;
            }
            return false;
        }

        /// <summary>
        /// Future: roping boarding parties onto cargo and disabled ships. Not implemented yet, so
        /// the squad stays aboard and no troops are spent.
        /// </summary>
        private static bool TryShipBoarding(Aircraft aircraft, Ship ship, Vector3 deck)
        {
            Plugin.Logger.LogInfo($"[Air Assault] Ship boarding ({ship.name}) is not available yet; the squad stays aboard {aircraft.name}.");
            return false;
        }

        /// <summary>The vanilla troop bench with room for a squad, if this helicopter has one.</summary>
        private static MountedTroops Bench(Aircraft aircraft)
        {
            foreach (MountedTroops troops in aircraft.GetComponentsInChildren<MountedTroops>())
                if (troops.IsAttached() && !IsHalo(troops) && troops.ammo < TroopDeploymentMath.DefaultSquadSize) return troops;
            return null;
        }

        /// <summary>Any peer: a slow Ibis in rope range with room on a troop bench.</summary>
        public bool CanExfil(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.disabled || aircraft.rb == null || !TroopAccountingAvailable) return false;
            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            string name = def != null ? (def.unitName ?? def.jsonKey ?? "") : aircraft.name ?? "";
            return IsIbis(name, def) && aircraft.rb.velocity.magnitude < ExfilMaxSpeed &&
                FastRopePlan.ModeFor(aircraft.radarAlt) != null && Bench(aircraft) != null;
        }

        /// <summary>
        /// Server: strikes the nearest friendly encampment or held building below and hands back
        /// where the squad comes from (global), how many board and the rope length.
        /// </summary>
        public bool TryExfil(Aircraft aircraft, out Vector3 site, out int count, out float height)
        {
            site = default;
            count = 0;
            height = 0f;
            if (!GameAccess.IsServer() || !CanExfil(aircraft)) return false;
            Vector3 origin = aircraft.transform.position;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, FastRopePlan.RappelMaxHeight + 10f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                return false;
            FactionHQ owner = aircraft.NetworkHQ;
            int troops = TroopDeploymentMath.DefaultSquadSize;
            bool found = InfantryEncampmentBuilder.TryRelease(hit.point, ExfilRadius, owner, out Vector3 centre, out troops) ||
                (ZoneGarrisonManager.Instance != null && ZoneGarrisonManager.Instance.TryReleaseOccupation(hit.point, ExfilRadius, owner, out centre));
            if (!found)
            {
                Plugin.Logger.LogInfo($"[Air Assault] Exfil: no friendly squad position within {ExfilRadius:0} m below {aircraft.name}.");
                return false;
            }
            height = origin.y - hit.point.y;
            count = Mathf.Min(TroopDeploymentMath.DefaultSquadSize - Bench(aircraft).ammo, Mathf.Max(1, troops));
            site = centre.ToGlobalPosition().AsVector3();
            Plugin.Logger.LogInfo($"[IBIS] Exfil: {count} infantry clipping onto the ropes of {aircraft.name}; their position is abandoned.");
            return true;
        }

        /// <summary>Every peer: the squad boards (bench refilled locally, as vanilla never syncs ammo) and rides up.</summary>
        public void PlayExfil(Aircraft aircraft, Vector3 site, int count, float height)
        {
            MountedTroops bench = aircraft != null ? Bench(aircraft) : null;
            if (bench == null || count <= 0) return;
            RefillTroops(aircraft, bench, count);
            float seconds = height / FastRopePlan.ReelSpeed + 25f;
            Throttle(aircraft, seconds);
            HoverHold.Engage(aircraft, seconds);
            if (GameManager.IsHeadless) return;
            BeginCargoAccess(aircraft, ResolveCargoDoors(aircraft, bench));
            AirAssaultVisuals.SpawnRopeExtraction(aircraft, new GlobalPosition(site.x, site.y, site.z).ToLocalPosition(), count, height);
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
            if (rappelWaves >= MaximumRappelWaves)
            {
                bool deployed = InfantryEncampmentBuilder.DeployRappelEncampment(landing.ToLocalPosition(), owner, airbase);
                Plugin.Logger.LogInfo(deployed
                    ? "[AIR ASSAULT] Eight troops established MG / AT / AA / MG encampment."
                    : "[AIR ASSAULT] Encampment placement unavailable after insertion.");
                return;
            }
            rappelWaves++;
            StartCoroutine(ResolveRappelCamps(landing.ToLocalPosition(), owner, airbase));
        }

        private IEnumerator ResolveRappelCamps(Vector3 position, FactionHQ owner, Airbase airbase)
        {
            // One camp per frame: a full stick no longer bursts ~150 raycasts at once.
            int wanted = InfantryEncampmentBuilder.RappelWanted(owner);
            InfantryEncampmentBuilder.PruneFallenSites();
            int placed = 0;
            for (int camp = 0; camp < wanted && InfantryEncampmentBuilder.ActiveSiteCount < InfantryEncampmentBuilder.MaximumSites; camp++)
            {
                if (InfantryEncampmentBuilder.TryCreateRappelCamp(position, owner, airbase)) placed++;
                yield return null;
            }
            rappelWaves--;
            Plugin.Logger.LogInfo(placed > 0
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

        private static void RefillTroops(Aircraft aircraft, MountedTroops troops, int count)
        {
            troops.ammo += count;
            float addedMass = count * (troops.info != null ? troops.info.massPerRound : 0f);
            TroopMassField.SetValue(troops, (float)TroopMassField.GetValue(troops) + addedMass);
            Hardpoint hardpoint = TroopHardpointField.GetValue(troops) as Hardpoint;
            if (hardpoint != null) hardpoint.ModifyMass(addedMass);
            if (GameAccess.IsServer())
            {
                CaptureStrengthField.SetValue(troops, (float)troops.ammo);
                if ((bool)CaptureActiveField.GetValue(troops)) aircraft.ModifyCaptureStrength(count);
            }
            foreach (Renderer renderer in troops.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;
            (TroopStationField?.GetValue(troops) as WeaponStation)?.AccountAmmo();
        }

        private static readonly FieldInfo TroopStationField = HarmonyLib.AccessTools.Field(typeof(Weapon), "weaponStation");

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

        internal static GameObject ResolveCivilianBuilding(Collider col)
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
    }
}
