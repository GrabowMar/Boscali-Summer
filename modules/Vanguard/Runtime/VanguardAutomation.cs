#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/vanguard.json); inert outside a sim run.
    /// Launches Vanguard missiles straight from the spawner so a scenario needs no loadout UI.
    /// </summary>
    public static partial class VanguardAutomation
    {
        private static readonly Dictionary<string, Missile> Launched = new Dictionary<string, Missile>();
        private static readonly Dictionary<string, float> TopSpeed = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> Deaths = new Dictionary<string, float>();
        private static Missile threat;
        private static Unit groundTarget;
        private static Unit testShip;
        private static FactionHQ enemyHq;
        private static int launchCount;
        private static Aircraft trackedLead;
        private static int threatsGone;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("VanguardAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            // "seated": the player's own aircraft (the drone link follows the local aircraft), not the first one of the faction
            Aircraft lead = Has(args, "seated") && GameManager.GetLocalAircraft(out Aircraft seat) && seat != null ? seat : FindAircraft(Text(args, "faction") ?? "Boscali");
            Aircraft bandit = FindAircraft(Text(args, "enemy") ?? "Primeva");
            if (bandit != null && bandit.NetworkHQ != null) enemyHq = bandit.NetworkHQ; // REMORA STRIKE may kill the bandit before batch-2 steps; a dropped link must not erase it either
            if (lead != null && lead != trackedLead)
            {
                trackedLead = lead;
                lead.onDisableUnit += _ =>
                {
                    Deaths["lead_deathTime"] = Time.timeSinceLevelLoad;
                    Deaths["lead_deathAltitude"] = (float)lead.GlobalPosition().y;
                };
            }
            switch (Text(args, "action"))
            {
                case "catalog": return Catalog();
                case "aegisfit": return AegisFit(lead, (int)Number(args, "darts", 0f));
                case "snap": return Snap(lead, Text(args, "target") == "none" ? null : (Unit)bandit, Number(args, "press", 1f) > 0f);
                case "launch":
                    string at = Text(args, "at");
                    if (at == "point") return LaunchAtPoint(Text(args, "key"), lead, Number(args, "x", 0f), Number(args, "z", 0f));
                    Dictionary<string, object> launched = Launch(Text(args, "key"), lead, at == "threat" ? threat : at == "ground" ? groundTarget : at == "ship" ? testShip : (Unit)bandit);
                    if (lead != null && LandingZones.TryGet(lead, out GlobalPosition lzAtLaunch)) { probeLaunch = lzAtLaunch; probeLaunchSet = true; }
                    return launched;
                case "lure":
                    Modules.Vanguard.Domain.SeductionRule.TowedRearChance = Number(args, "chance", 0.35f);
                    Modules.Vanguard.Domain.SeductionRule.TowedFrontChance = Number(args, "chance", 0.12f);
                    return Status(lead);
                case "pk": return Failure("VanguardAutomation","pk","AEGIS now requires physical contact; kill rolls are retired");
                case "cut": return AlexCut(lead);
                case "alexfit": return AlexFit(lead);
                case "alexpress": return AlexPress(lead, (int)Number(args, "calls", 1f));
                case "snapg":
                    Modules.Vanguard.Domain.TowedTrail.SnapG = Number(args, "g", 7f);
                    return Status(lead);
                case "ground": return Ground(lead, Number(args, "range", 12000f), Text(args, "type") ?? "UGV1_grenade");
                case "ship": return SpawnTestShip(lead, Number(args, "range", 14000f), Text(args, "type") ?? "Frigate1", (int)Number(args, "from", 0f));
                case "threat": return Threat(bandit, lead, Number(args, "range", 5000f), Text(args, "seeker") ?? "ARH", Number(args, "climb", 200f));
                case "strike":
                    if (lead != null && lead.radar != null) lead.radar.activated = true; // ARCHER fires only on a faction track
                    Modules.Vanguard.Runtime.DroneOrders.Order(lead, Text(args, "at") == "ground" ? groundTarget : bandit);
                    return Status(lead);
                case "equip": return Equip(Text(args, "tanker") ?? "Tarantula", Has(args, "norx"), Text(args, "rxType") ?? "FS-20");
                case "skyview": return SkyView(Text(args, "angle") ?? "side");
                case "drain": return Drain(Rx(args), Number(args, "fuel", 0.3f), (int)Number(args, "stations", 2f), (int)Number(args, "rounds", 1f));
                case "netprefab": return NetPrefab(Text(args, "type"));
                case "pitarm": return SkywellArm(args);
                case "pit": return SkywellPit(args);
                case "threatwhen": return SkywellThreatWhen(bandit, args);
                case "seatfly": return SkywellFly(args);
                case "rxstatus": return SkywellRxStatus(args);
                case "lz": return LandingZoneStep(args, lead);
                case "status": return Status(lead);
                case "orca": return Orca(args, lead);
                case "kestrel": return Kestrel(args, lead);
                case "hamilcar": return Hamilcar(args, lead);
                case "link": return LinkStep(args, lead);
                case "showcase": return Showcase(lead);
                case "view": return View(Text(args, "angle") ?? "front");
                default: return Failure("VanguardAutomation", "Step", "unknown action");
            }
        }

        // ---- showcase: inert copies of every Vanguard prefab, lit by the game, for model review captures

        private static readonly string[] ShowcaseMounts = { "VG_Remora_ARM1_single", "VG_Aegis_Pod", "VG_Lance_Pod" };
        private const float Spacing = 7f;
        private static GameObject showcase;
        private static GlobalPosition showcaseAnchor;

        private static Dictionary<string, object> Showcase(Aircraft lead)
        {
            if (lead == null) return Failure("VanguardAutomation", "showcase", "no friendly aircraft");
            if (showcase != null) UnityEngine.Object.Destroy(showcase);
            showcase = new GameObject("VanguardShowcase");
            showcaseAnchor = lead.GlobalPosition() + Vector3.up * 400f;
            showcase.transform.position = showcaseAnchor.ToLocalPosition();
            var prefabs = new List<GameObject>();
            foreach (MissileDefinition m in Encyclopedia.i.missiles)
                if (m != null && m.unitPrefab != null && VanguardKeys.RoleOf(m.jsonKey) != VanguardRole.None) prefabs.Add(m.unitPrefab);
            foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                if (w != null && w.prefab != null && Array.IndexOf(ShowcaseMounts, w.jsonKey) >= 0) prefabs.Add(w.prefab);
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject copy = Inert(prefabs[i], showcase.transform);
                int row = i / 4, col = i % 4;
                copy.transform.localPosition = new Vector3((col - 1.5f) * Spacing, -row * 4f, row * 2f);
                copy.transform.localRotation = Quaternion.identity;
            }
            return new Dictionary<string, object> { ["ok"] = true, ["shown"] = prefabs.Count };
        }

        // Instantiate under an inactive holder so no Awake runs, then strip everything but visuals.
        private static GameObject Inert(GameObject prefab, Transform parent)
        {
            var holder = new GameObject("hold");
            holder.SetActive(false);
            GameObject g = UnityEngine.Object.Instantiate(prefab, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (Component c in g.GetComponentsInChildren<Component>(true))
                {
                    if (c is Transform || c is MeshFilter || c is Renderer) continue;
                    try { UnityEngine.Object.DestroyImmediate(c); }
                    catch (Exception) { /* a dependent component goes first; next pass gets this one */ }
                }
            g.transform.SetParent(parent, false);
            UnityEngine.Object.Destroy(holder);
            return g;
        }

        private static Dictionary<string, object> View(string angle)
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null || showcase == null) return Failure("VanguardAutomation", "view", "showcase first");
            Vector3 offset;
            Quaternion rotation;
            switch (angle)
            {
                case "rear": offset = new Vector3(8f, 4f, -16f); break;
                case "close": offset = new Vector3(-6f, 2f, 9f); break;
                default: offset = new Vector3(10f, 5f, 20f); break;
            }
            GlobalPosition at = showcaseAnchor + offset + (angle == "close" ? new Vector3(-1.5f * Spacing, 0f, 0f) : Vector3.zero);
            Vector3 look = (showcaseAnchor + (angle == "close" ? new Vector3(-1.5f * Spacing, -1f, 0f) : new Vector3(0f, -2f, 1f))) - at;
            rotation = Quaternion.LookRotation(look);
            camera.SetCameraPosition(at, rotation);
            camera.cameraVelocity = Vector3.zero;
            camera.allowInputs = false;
            camera.SetDesiredFoV(angle == "close" ? 40f : 60f, angle == "close" ? 40f : 60f);
            return new Dictionary<string, object> { ["ok"] = true };
        }

        // ---- SKYWELL: an AI Tarantula with the kit ahead of the lead; the lead (AI) is drained and called in.

        private static Aircraft tanker, receiver;
        /// <summary>Stations each drained aircraft lost rounds from (bit per station index): the sim checks every one of them got its handoff.</summary>
        private static readonly Dictionary<Aircraft, int> Drained = new Dictionary<Aircraft, int>();
        private static readonly System.Reflection.FieldInfo LeakRate =
            typeof(FuelTank).GetField("leakRate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        private static Dictionary<string, object> Equip(string tankerType, bool noReceiver, string receiverType)
        {
            tanker = receiver = null;
            foreach (Aircraft a in UnityEngine.Object.FindObjectsOfType<Aircraft>())
            {
                if (a.disabled || a.definition == null) continue;
                if (a.definition.unitName.IndexOf(tankerType, StringComparison.OrdinalIgnoreCase) >= 0) tanker = a;
                else if (!noReceiver && a.definition.unitName.IndexOf(receiverType, StringComparison.OrdinalIgnoreCase) >= 0) receiver = a;
            }
            WeaponMount kit = null;
            foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                if (w != null && w.jsonKey == VanguardKeys.SkywellMount) kit = w;
            if (tanker == null || kit == null) return Failure("VanguardAutomation", "equip", "need the tanker type and the kit"); // an FS-20 is optional: a seated player is armed with pitarm (and equip says norx)
            var loadout = new NuclearOption.SavedMission.Loadout { weapons = new List<WeaponMount>() };
            // Rear cargo bay (nearest the ramp) if the airframe has one, else the first cargo / mission bay.
            HardpointSet[] sets = tanker.weaponManager.hardpointSets;
            int bay = Array.FindIndex(sets, h => h.name.IndexOf("Rear", StringComparison.OrdinalIgnoreCase) >= 0);
            if (bay < 0) bay = Array.FindIndex(sets, h => h.name.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         h.name.IndexOf("Mission Bay", StringComparison.OrdinalIgnoreCase) >= 0);
            if (bay < 0) bay = 0;
            for (int i = 0; i < sets.Length; i++) loadout.weapons.Add(i == bay ? kit : null);
            tanker.Networkloadout = loadout;
            NuclearOption.SavedMission.Loadout armed = receiver != null ? ArmedLoadout(receiver) : null;
            if (armed != null) receiver.Networkloadout = armed;
            return new Dictionary<string, object> { ["ok"] = true, ["tankerType"] = tanker.definition.unitName, ["bay"] = sets[bay].name,
                ["kit"] = Modules.Vanguard.Runtime.SkywellBoard.KitStation(tanker) != null ? 1 : 0,
                ["armed"] = armed != null ? 1 : 0 };
        }

        private static Dictionary<string, object> SkyView(string angle)
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (tanker == null || camera == null) return Failure("VanguardAutomation", "skyview", "equip first");
            // Eye offsets from the TENDER in tanker axes (x right, y up, z forward). The camera looks just above the tender, at the gap between
            // it and the belly it works under, so both stay in frame wherever the receiver flies (a fixed point of the zone shows empty sky).
            SkyChase.Offset = angle == "off" ? (Vector3?)null
                : angle == "rear" ? new Vector3(7f, -2f, -34f)
                : angle == "close" ? new Vector3(11f, 0.5f, 8f)
                : angle == "below" ? new Vector3(-10f, -13f, -15f) : new Vector3(27f, 1f, 2f);
            SkyChase.Target = tanker;
            if (SkyChase.Offset == null) camera.SetFollowingUnit(tanker); // back to the game's own follow camera
            else
            {
                SkyChase.Free(camera);
                camera.SetDesiredFoV(50f, 50f);
                if (UnityEngine.Object.FindObjectOfType<SkyChase>() == null) new GameObject("SkywellChase").AddComponent<SkyChase>();
            }
            return new Dictionary<string, object> { ["ok"] = true,
                ["mountFromTanker"] = tanker.transform.InverseTransformPoint(Modules.Vanguard.Runtime.SkywellBoard.Mount(tanker).position).ToString("F1"),
                ["rig"] = Modules.Vanguard.Presentation.SkywellVisuals.Probe };
        }

        /// <summary>
        /// Sim-only chase camera on the tender. The game's camera manager goes into its free state once (what the nomodkit camera op does;
        /// SetCameraPosition every frame re-enters the state and rebuilds the reflection probe each time), then the pose is written every
        /// frame after the manager's own LateUpdate, the floating-origin shift and the tender's pose.
        /// </summary>
        [DefaultExecutionOrder(2000)]
        private sealed class SkyChase : MonoBehaviour
        {
            public static Aircraft Target;
            public static Vector3? Offset;
            private const float LookUp = 1.5f; // above the tender root: the gap between it and the receiver's belly

            public static void Free(CameraStateManager camera)
            {
                camera.GetCameraPosition(out GlobalPosition at, out Quaternion rotation);
                camera.SetCameraPosition(at, rotation);
                camera.cameraVelocity = Vector3.zero;
                camera.allowInputs = false;
            }

            private void LateUpdate()
            {
                CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                if (camera == null || Target == null || Offset == null) return;
                if (camera.currentState != camera.freeState) Free(camera); // the game took the camera back
                Transform t = Target.transform;
                if (!Modules.Vanguard.Presentation.SkywellVisuals.TenderWorld(Target.persistentID.Id, out Vector3 tender))
                    tender = Modules.Vanguard.Runtime.SkywellBoard.Mount(Target).position + t.rotation * SkywellZone.Tow;
                Vector3 eye = tender + t.rotation * Offset.Value;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(tender + t.up * LookUp - eye, t.up));
                camera.cameraVelocity = Vector3.zero;
            }
        }

        // Drains the fuel to `fuel`, then fires up to `rounds` rounds from each of the first `stations` armed stations.
        private static Dictionary<string, object> Drain(Aircraft lead, float fuel, int stations, int rounds)
        {
            if (lead == null) return Failure("VanguardAutomation", "drain", "no friendly aircraft");
            foreach (FuelTank t in lead.GetFuelTanks()) t.Refuel(fuel);
            int fired = 0, shots = 0, mask = 0;
            for (int i = 0; i < lead.weaponStations.Count; i++)
            {
                WeaponStation s = lead.weaponStations[i];
                if (s?.WeaponInfo != null && !s.WeaponInfo.cargo && !s.WeaponInfo.gun && s.Ammo > 0 && fired < stations)
                {
                    int before = shots;
                    for (int k = 0; k < rounds && s.Ammo > 0; k++, shots++)
                        s.LaunchMount(lead, null, lead.GlobalPosition() + lead.transform.forward * 3000f);
                    fired++;
                    if (shots > before && i < 31) mask |= 1 << i;
                }
            }
            Drained[lead] = mask;
            return new Dictionary<string, object> { ["ok"] = true, ["fired"] = fired, ["shots"] = shots, ["fuel"] = lead.GetFuelLevel(),
                ["missing"] = Modules.Vanguard.Runtime.SkywellService.MissingRounds(lead) };
        }

        private static Aircraft FindAircraft(string faction)
        {
            foreach (Aircraft a in UnityEngine.Object.FindObjectsOfType<Aircraft>())
                if (!a.disabled && a.NetworkHQ != null && a.NetworkHQ.faction != null &&
                    a.NetworkHQ.faction.factionName.IndexOf(faction, StringComparison.OrdinalIgnoreCase) >= 0) return a;
            return null;
        }

        // ---- landing zone (tests/ingame/landing-zone.json): set/clear the lead's zone and read
        // where each live glider actually is relative to it, the designated target, and a probe point.

        private static float hawProbeMin = -1f;
        private static GlobalPosition lastZone; // last point set through this seam: the default probe
        private static bool lastZoneSet;
        private static GlobalPosition probeLaunch; // the lead's zone at the last launch: where that glider was aimed
        private static bool probeLaunchSet;

        private static float FlatDist(GlobalPosition a, GlobalPosition b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static Dictionary<string, object> LandingZoneStep(Dictionary<string, object> args, Aircraft lead)
        {
            string op0 = Text(args, "do") ?? "set";
            if (lead == null && op0 != "status" && op0 != "kill")
                return Failure("VanguardAutomation", "lz", "no friendly aircraft");

            switch (Text(args, "do") ?? "set")
            {
                case "clear":
                    LandingZones.Order(lead, null);
                    break;
                case "status":
                    break;
                case "kill":
                    // Respawn-clean, registry-wide: give the lead a zone, then disable whichever
                    // friendly aircraft holds one (the scene scan may no longer see a landed one).
                    if (lead != null) LandingZones.Order(lead, new GlobalPosition(Number(args, "x", 0f), 0f, Number(args, "z", 0f)));
                    foreach (Aircraft a in UnitRegistry.allAircraft)
                        if (!a.disabled && LandingZones.TryGet(a, out _)) { a.DisableUnit(); break; }
                    break;
                case "neartarget":
                    if (groundTarget == null) return Failure("VanguardAutomation", "lz", "no ground target yet");
                    GlobalPosition tp = groundTarget.GlobalPosition();
                    var np = new GlobalPosition(tp.x + Number(args, "dx", 400f), 0f, tp.z + Number(args, "dz", 0f));
                    Vector3 nlocal = np.ToLocalPosition();
                    float ny = Physics.Raycast(new Vector3(nlocal.x, Datum.LocalSeaY + 9000f, nlocal.z), Vector3.down, out RaycastHit nhit, 12000f,
                        (int)PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)
                        ? nhit.point.ToGlobalPosition().AsVector3().y : 0f;
                    LandingZones.Order(lead, new GlobalPosition(np.x, ny, np.z));
                    lastZone = np;
                    lastZoneSet = true;
                    break;
                default:
                    GlobalPosition point;
                    if (Has(args, "rel"))
                    {
                        GlobalPosition at = lead.GlobalPosition();
                        point = new GlobalPosition(at.x + Number(args, "dx", 0f), 0f, at.z + Number(args, "dz", 0f));
                    }
                    else point = new GlobalPosition(Number(args, "x", 0f), 0f, Number(args, "z", 0f));
                    Vector3 local = point.ToLocalPosition();
                    float y = Physics.Raycast(new Vector3(local.x, Datum.LocalSeaY + 9000f, local.z), Vector3.down, out RaycastHit hit, 12000f,
                        (int)PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)
                        ? hit.point.ToGlobalPosition().AsVector3().y : 0f;
                    LandingZones.Order(lead, new GlobalPosition(point.x, y, point.z));
                    lastZone = point;
                    lastZoneSet = true;
                    break;
            }
            var state = new Dictionary<string, object> { ["ok"] = true };
            Aircraft banditNow = FindAircraft("Primeva");
            state["enemyHq"] = enemyHq != null ? 1 : 0;
            state["bandit"] = banditNow != null ? 1 : 0;
            state["banditHq"] = banditNow != null && banditNow.NetworkHQ != null ? 1 : 0;
            bool has = LandingZones.TryGet(lead, out GlobalPosition zone);
            state["has"] = has ? 1 : 0;
            if (has) { state["x"] = zone.x; state["z"] = zone.z; }
            bool probe = Has(args, "px") || probeLaunchSet || lastZoneSet;
            var probePoint = Has(args, "px") ? new GlobalPosition(Number(args, "px", 0f), 0f, Number(args, "pz", 0f)) : probeLaunchSet ? probeLaunch : lastZone;
            var hamFlight = Modules.Vanguard.Runtime.VanguardFlight.HamilcarLast;
            if (hamFlight != null)
            {
                var aimField = HarmonyLib.AccessTools.Field(typeof(Modules.Vanguard.Runtime.VanguardFlight), "hamilcarAim");
                var spotField = HarmonyLib.AccessTools.Field(typeof(Modules.Vanguard.Runtime.VanguardFlight), "hamilcarSpot");
                if (aimField?.GetValue(hamFlight) is GlobalPosition aimNow)
                {
                    state["aimX"] = aimNow.x; state["aimZ"] = aimNow.z;
                    if (has) state["aimToZone"] = FlatDist(aimNow, zone);
                }
                if (spotField?.GetValue(hamFlight) is GlobalPosition spotNow) { state["spotX"] = spotNow.x; state["spotZ"] = spotNow.z; }
                var lzField = HarmonyLib.AccessTools.Field(typeof(Modules.Vanguard.Runtime.VanguardFlight), "landingZone");
                state["lzCaptured"] = lzField?.GetValue(hamFlight) is GlobalPosition ? 1 : 0;
            }
            Missile ham = Modules.Vanguard.Runtime.VanguardFlight.HamilcarLast?.Missile;
            if (ham != null && !ham.disabled)
            {
                if (has) state["hamToZone"] = FlatDist(ham.GlobalPosition(), zone);
                if (groundTarget != null) state["hamToTarget"] = FlatDist(ham.GlobalPosition(), groundTarget.GlobalPosition());
                if (probe) state["hamToProbe"] = FlatDist(ham.GlobalPosition(), probePoint);
                if (has && groundTarget != null) state["hamCloser"] = FlatDist(ham.GlobalPosition(), zone) < FlatDist(ham.GlobalPosition(), groundTarget.GlobalPosition()) ? 1 : 0;
            }
            foreach (Modules.Vanguard.Runtime.KestrelDrone drone in UnityEngine.Object.FindObjectsOfType<Modules.Vanguard.Runtime.KestrelDrone>())
                if (!drone.GetComponent<Missile>().disabled)
                {
                    if (has) state["kestrelCentreToZone"] = FlatDist(drone.Centre, zone);
                    if (probe) state["kestrelCentreToProbe"] = FlatDist(drone.Centre, probePoint);
                    break;
                }
            if (Launched.TryGetValue(VanguardKeys.HawcX, out Missile haw) && haw != null && !haw.disabled)
            {
                if (has) state["hawToZone"] = FlatDist(haw.GlobalPosition(), zone);
                if (groundTarget != null) state["hawToTarget"] = FlatDist(haw.GlobalPosition(), groundTarget.GlobalPosition());
                if (probe)
                {
                    float d = FlatDist(haw.GlobalPosition(), probePoint);
                    state["hawToProbe"] = d;
                    hawProbeMin = hawProbeMin < 0f ? d : Math.Min(hawProbeMin, d);
                }
            }
            if (has && groundTarget != null && Launched.TryGetValue(VanguardKeys.HawcX, out Missile haw2) && haw2 != null && !haw2.disabled)
                state["hawCloser"] = FlatDist(haw2.GlobalPosition(), zone) < FlatDist(haw2.GlobalPosition(), groundTarget.GlobalPosition()) ? 1 : 0;
            state["hawProbeMin"] = hawProbeMin;
            state["smoke"] = GameObject.Find("BoscaliSummer.LandingSmoke") != null ? 1 : 0;
            return state;
        }

        private static Dictionary<string, object> Catalog()
        {
            Encyclopedia e = Encyclopedia.i;
            if (e == null) return Failure("VanguardAutomation", "catalog", "no encyclopedia");
            int missiles = 0, mounts = 0;
            foreach (MissileDefinition m in e.missiles)
                if (m != null && VanguardKeys.RoleOf(m.jsonKey) != VanguardRole.None) missiles++;
            foreach (WeaponMount m in e.weaponMounts)
                if (m != null && m.jsonKey != null && m.jsonKey.StartsWith("VG_", StringComparison.Ordinal)) mounts++;
            return new Dictionary<string, object> { ["ok"] = true, ["missiles"] = missiles, ["mounts"] = mounts };
        }

        private static MissileDefinition Find(Func<MissileDefinition, bool> match)
        {
            foreach (MissileDefinition m in Encyclopedia.i.missiles)
                if (m != null && m.unitPrefab != null && match(m)) return m;
            return null;
        }

        private static Dictionary<string, object> Launch(string key, Aircraft lead, Unit target)
        {
            if (lead == null) return Failure("VanguardAutomation", "launch", "no friendly aircraft");
            MissileDefinition def = Find(m => m.jsonKey == key);
            if (def == null) return Failure("VanguardAutomation", "launch", "unknown missile " + key);
            Transform t = lead.transform;
            // Stagger launches sideways: back-to-back spawns at one point collide once they turn tangible (1.5 s).
            Vector3 slot = t.right * (((launchCount++ % 3) - 1) * 20f);
            Missile missile = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, t.position - t.up * 3f + t.forward * 10f + slot,
                t.rotation, lead.rb.velocity, target, lead);
            Launched[key] = missile;
            Unit aimed = target;
            missile.onDisableUnit += _ =>
            {
                Deaths[key + "_deathAge"] = missile.timeSinceSpawn;
                Deaths[key + "_deathAltitude"] = (float)missile.GlobalPosition().y;
                Deaths[key + "_deathAgl"] = missile.radarAlt;
                Deaths[key + "_deathSpeed"] = missile.speed;
                if (aimed != null) Deaths[key + "_deathRange"] = FastMath.Distance(missile.GlobalPosition(), aimed.GlobalPosition());
            };
            TopSpeed[key] = 0f;
            return Status(lead);
        }

        // An enemy Hexhound (or `type`, e.g. RadarSAM1) on dry, flat ground `range` ahead of the lead (sweeps +/-60 deg and +6 km until one fits).
        private static Dictionary<string, object> Ground(Aircraft lead, float range, string type)
        {
            if (lead == null || enemyHq == null) return Failure("VanguardAutomation", "ground", "need lead and an enemy HQ");
            if (!Encyclopedia.Lookup.TryGetValue(type, out UnitDefinition def))
                return Failure("VanguardAutomation", "ground", type + " not in Encyclopedia.Lookup");
            Vector3 fwd = lead.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            for (float d = range; d <= range + 6000f; d += 1000f)
                for (int b = -60; b <= 60; b += 15)
                {
                    Vector3 dir = Quaternion.Euler(0f, b, 0f) * fwd;
                    Vector3 desired = lead.transform.position + dir * d;
                    desired.y = Datum.LocalSeaY + 1400f; // DryGround probes from +500 m: start above any hill
                    if (!GroundPlacement.TryPlace(def, desired, Quaternion.LookRotation(-dir), out Vector3 point)) continue;
                    groundTarget = NetworkSceneSingleton<Spawner>.i.SpawnVehicle(def.unitPrefab, point.ToGlobalPosition(),
                        Quaternion.LookRotation(-dir), Vector3.zero, enemyHq, "VG_TestTarget", 1f, true, null);
                    return new Dictionary<string, object> { ["ok"] = groundTarget != null, ["range"] = d, ["bearing"] = b };
                }
            return Failure("VanguardAutomation", "ground", "no dry flat ground ahead");
        }

        // An enemy ship (default Argus frigate) on deep water near `range` from the lead (sweeps 16 bearings).
        private static Dictionary<string, object> SpawnTestShip(Aircraft lead, float range, string type, int from)
        {
            if (lead == null || enemyHq == null) return Failure("VanguardAutomation", "ship", "need lead and an enemy HQ");
            if (!Encyclopedia.Lookup.TryGetValue(type, out UnitDefinition def))
                return Failure("VanguardAutomation", "ship", type + " not in Encyclopedia.Lookup");
            for (float d = range; d <= range + 8000f; d += 2000f)
                for (int b = from; b < from + 360; b += 22)
                {
                    Vector3 p = lead.transform.position + Quaternion.Euler(0f, b, 0f) * lead.transform.forward * d;
                    p.y = Datum.LocalSeaY + 3000f; // probe from above any hill
                    bool land = Physics.Raycast(p, Vector3.down, out RaycastHit hit, 3500f, (int)PhysicsLayers.StaticsMask) &&
                        hit.point.y > Datum.LocalSeaY - 15f;
                    if (land) continue;
                    p.y = Datum.LocalSeaY;
                    testShip = NetworkSceneSingleton<Spawner>.i.SpawnShip(def.unitPrefab, p.ToGlobalPosition(),
                        Quaternion.Euler(0f, b + 90f, 0f), enemyHq, "VG_TestShip", 1f, false);
                    return new Dictionary<string, object> { ["ok"] = testShip != null, ["range"] = d, ["bearing"] = b };
                }
            return Failure("VanguardAutomation", "ship", "no deep water near the lead");
        }

        // An enemy radar AAM fired at the lead from behind, for the decoy / drone / AEGIS checks.
        private static Dictionary<string, object> Threat(Aircraft bandit, Aircraft lead, float range, string seeker, float climb)
        {
            if (lead == null || bandit == null) return Failure("VanguardAutomation", "threat", "need lead and bandit");
            bool ir = seeker == "IR"; // IR threats ignore lures, so the AEGIS check cannot be stolen by MALD/REMORA
            MissileDefinition def = Find(m => m.jsonKey.StartsWith("AAM", StringComparison.Ordinal) &&
                (ir ? m.unitPrefab.GetComponent<IRSeeker>() != null : m.unitPrefab.GetComponent<ARHSeeker>() != null));
            if (def == null) return Failure("VanguardAutomation", "threat", "no " + seeker + " air-to-air missile");
            Transform t = lead.transform;
            Vector3 from = t.position - t.forward * range + Vector3.up * climb;
            threat = NetworkSceneSingleton<Spawner>.i.SpawnMissile(def, from, Quaternion.LookRotation(t.position - from),
                (t.position - from).normalized * 600f, lead, bandit);
            Missile shot = threat;
            Aircraft victim = lead;
            threat.onDisableUnit += _ =>
            {
                threatsGone++;
                if (victim != null) Deaths["threat_toLead"] = FastMath.Distance(shot.GlobalPosition(), victim.GlobalPosition());
                if (Launched.TryGetValue(VanguardKeys.AleX, out Missile decoy) && decoy != null)
                    Deaths["threat_toDecoy"] = FastMath.Distance(shot.GlobalPosition(), decoy.GlobalPosition());
                Deaths["threat_time"] = Time.timeSinceLevelLoad;
            };
            return Status(lead);
        }

        private static Dictionary<string, object> Status(Aircraft lead)
        {
            var state = new Dictionary<string, object> { ["ok"] = true, ["threatsGone"] = threatsGone, ["time"] = Time.timeSinceLevelLoad };
            state["waterEntries"] = Modules.Vanguard.Runtime.VanguardStats.WaterEntries;
            state["shipHits"] = Modules.Vanguard.Runtime.VanguardStats.ShipHits;
            state["seductions"] = Modules.Vanguard.Runtime.VanguardStats.Seductions;
            state["ugvsSpawned"] = Modules.Vanguard.Runtime.VanguardStats.UgvsSpawned;
            state["turretsDeployed"] = Modules.Vanguard.Runtime.VanguardStats.TurretsDeployed;
            state["turretShots"] = Modules.Vanguard.Runtime.VanguardStats.TurretShots;
            state["ugvsAlive"] = 0; // Compatibility telemetry: ground-vehicle delivery is retired.
            state["refuels"] = Modules.Vanguard.Runtime.VanguardStats.Refuels;
            state["rearms"] = Modules.Vanguard.Runtime.VanguardStats.Rearms;
            foreach (var counter in Modules.Vanguard.Runtime.VanguardStats.Counters) state[counter.Key] = counter.Value;
            // Sim check paths split on '.', so each counter is mirrored as <key>_<event> as well.
            foreach (var counter in Modules.Vanguard.Runtime.VanguardStats.Counters) state[counter.Key.Replace('.', '_')] = counter.Value;
            Modules.Vanguard.Runtime.SentinelDrone.Report(state, Launched);
            AlexStatus(state, lead);
            if (tanker != null)
            {
                state["skywellFuelKg"] = Modules.Vanguard.Runtime.SkywellService.FuelKg(tanker);
                state["skywellRounds"] = Modules.Vanguard.Runtime.SkywellService.Rounds(tanker);
                state["tankerAlive"] = tanker.disabled ? 0 : 1;
                int tLeaks = 0, tDetached = 0;
                foreach (FuelTank tank in tanker.GetFuelTanks())
                    if (LeakRate?.GetValue(tank) is float rate && rate > 0f) tLeaks++;
                var detachedNames = new List<string>();
                foreach (UnitPart part in tanker.partLookup)
                    if (part != null && part.IsDetached())
                    {
                        tDetached++;
                        if (detachedNames.Count < 6) detachedNames.Add(part.name);
                    }
                state["tankerDetachedNames"] = string.Join(",", detachedNames);
                state["tankerLeaks"] = tLeaks;
                state["tankerDetached"] = tDetached;
                state["tankerUp"] = Vector3.Dot(tanker.transform.up, Vector3.up);
                state["tankerAlt"] = (float)tanker.GlobalPosition().y;
                if (Modules.Vanguard.Runtime.SkywellBoard.Views.TryGetValue(tanker.persistentID.Id, out var view))
                {
                    state["skywellActive"] = view.Active ? 1 : 0;
                    state["skywellState"] = view.State.ToString();
                }
                if (receiver != null)
                {
                    Modules.Vanguard.Runtime.SkywellBoard.Rel(tanker, receiver, out Vector3 rel, out float relSpeed, out _, out float roll);
                    state["receiverZoneRel"] = rel.ToString("F1");
                    state["receiverZoneInside"] = SkywellZone.Contains(rel, relSpeed, roll) ? 1 : 0;
                    state["receiverZoneDist"] = (rel - SkywellZone.Tow).magnitude;
                    state["receiverRelSpeed"] = relSpeed;
                }
            }
            if (receiver != null)
            {
                state["receiverAlive"] = receiver.disabled ? 0 : 1;
                state["receiverFuel"] = receiver.GetFuelLevel();
                int leaking = 0;
                foreach (FuelTank tank in receiver.GetFuelTanks())
                    if (LeakRate?.GetValue(tank) is float rate && rate > 0f) leaking++;
                state["receiverLeaks"] = leaking;
                int rDetached = 0;
                foreach (UnitPart part in receiver.partLookup)
                    if (part != null && part.IsDetached()) rDetached++;
                state["receiverDetached"] = rDetached;
                Bounds rb = Modules.Vanguard.Runtime.SkywellBoard.LocalBounds(receiver);
                state["receiverBounds"] = rb.center.ToString("F1") + "/" + rb.size.ToString("F1");
                state["tender"] = Modules.Vanguard.Presentation.SkywellVisuals.TenderProbe;
                state["receiverMissing"] = Modules.Vanguard.Runtime.SkywellService.MissingRounds(receiver);
            }
            foreach (KeyValuePair<string, float> death in Deaths) state[death.Key] = death.Value;
            SkywellStatus(state);
            if (threat != null && !threat.disabled)
                state["threatOnLead"] = lead != null && threat.targetID.Id == lead.persistentID.Id ? 1 : 0;
            foreach (KeyValuePair<string, Missile> pair in Launched)
            {
                Missile m = pair.Value;
                string k = pair.Key;
                bool alive = m != null && !m.disabled;
                state[k + "_alive"] = alive ? 1 : 0;
                if (!alive) continue;
                TopSpeed[k] = Mathf.Max(TopSpeed[k], m.speed);
                state[k + "_speed"] = m.speed;
                state[k + "_topSpeed"] = TopSpeed[k];
                state[k + "_altitude"] = m.GlobalPosition().y;
                state[k + "_guided"] = Modules.Vanguard.Runtime.VanguardRegistry.TryGet(m, out _) ? 1 : 0;
                var gunDrone=m.GetComponent<Modules.Vanguard.Runtime.GlaiveDrone>();
                if (gunDrone != null) state[k + "_phase"]=gunDrone.Phase.ToString();
                if (lead != null) state[k + "_fromLead"] = FastMath.Distance(m.GlobalPosition(), lead.GlobalPosition());
            }
            return state;
        }
    }
}
#endif
