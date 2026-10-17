#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/airborne*.json); inert outside a sim
    /// run. Plays HALO sticks and rope work straight from the plans, so a scenario needs no loadout
    /// UI, and parks the camera on them for captures.
    /// </summary>
    public static class AirborneAutomation
    {
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("AirborneAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            Aircraft lead = FindAircraft(Text(args, "faction") ?? "Boscali");
            switch (Text(args, "action"))
            {
                case "catalog": return Catalog();
                case "halo": return Halo(lead, Number(args, "ahead", 3000f));
                case "rope": return Rope(lead, Text(args, "surface") == "roof");
                case "exfil": return Exfil(lead);
                case "watch": return Watch(Text(args, "op") ?? "BoscaliSummer.HaloDrop", Text(args, "child") ?? "Jumper_1",
                    Number(args, "distance", 12f), Number(args, "height", 3f));
                case "lz": return Zone(args, lead);
                case "lzfit": return Fit(lead);
                case "lzdrop": return Drop(lead, args);
                case "lzstatus": return ZoneStatus();
                case "status": return Status();
                case "hover": return Hover(lead, Number(args, "height", 30f), Text(args, "over") == "building");
                default: return Failure("AirborneAutomation", "Step", "unknown action");
            }
        }

        /// <summary>Test only: parks the helicopter frozen at a height over the ground or the nearest roof.</summary>
        private static Dictionary<string, object> Hover(Aircraft lead, float height, bool overBuilding)
        {
            if (lead == null || lead.rb == null) return Failure("AirborneAutomation", "hover", "no friendly aircraft");
            Vector3 spot = lead.transform.position;
            if (overBuilding)
            {
                MapBuilding best = null;
                float bestSq = 3000f * 3000f;
                foreach (MapBuilding b in UnityEngine.Object.FindObjectsOfType<MapBuilding>())
                {
                    Vector3 d = b.transform.position - spot;
                    d.y = 0f;
                    if (d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; best = b; }
                }
                if (best == null) return Failure("AirborneAutomation", "hover", "no building within 3 km");
                spot = best.transform.position;
            }
            if (!Physics.Raycast(new Vector3(spot.x, Datum.LocalSeaY + 4000f, spot.z), Vector3.down, out RaycastHit hit, 8000f,
                    PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                return Failure("AirborneAutomation", "hover", "nothing below");
            lead.rb.isKinematic = true;
            lead.rb.position = hit.point + Vector3.up * height;
            lead.transform.position = lead.rb.position;
            lead.transform.rotation = Quaternion.Euler(0f, lead.transform.eulerAngles.y, 0f);
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["onRoof"] = AirAssaultController.ResolveCivilianBuilding(hit.collider) != null ? 1 : 0,
                ["ground"] = hit.point.y - Datum.LocalSeaY,
            };
        }

        private static Aircraft FindAircraft(string faction)
        {
            foreach (Aircraft a in UnityEngine.Object.FindObjectsOfType<Aircraft>())
                if (!a.disabled && a.NetworkHQ != null && a.NetworkHQ.faction != null &&
                    a.NetworkHQ.faction.factionName.IndexOf(faction, StringComparison.OrdinalIgnoreCase) >= 0) return a;
            return null;
        }

        private static int Shapes(WeaponMount mount, string child)
        {
            Transform t = mount != null && mount.prefab != null ? mount.prefab.transform.Find(child) : null;
            SkinnedMeshRenderer smr = t != null ? t.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            return smr != null && smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;
        }

        private static Dictionary<string, object> Catalog()
        {
            WeaponMount halo = null;
            Encyclopedia.WeaponLookup?.TryGetValue(AirAssaultController.HaloMountKey, out halo);
            int bays = 0;
            foreach (AircraftDefinition def in Encyclopedia.i.aircraft)
            {
                if (def == null || def.jsonKey != "QuadVTOL1" || def.unitPrefab == null) continue;
                WeaponManager manager = def.unitPrefab.GetComponentInChildren<WeaponManager>(true);
                if (manager == null) continue;
                foreach (HardpointSet set in manager.hardpointSets)
                    if (set != null && set.weaponOptions != null && halo != null && set.weaponOptions.Contains(halo)) bays++;
            }
            return new Dictionary<string, object>
            {
                ["ok"] = halo != null, ["haloMount"] = halo != null ? 1 : 0, ["tarantulaBays"] = bays,
                ["jumperShapes"] = Shapes(halo, "AirborneJumper"), ["trooperShapes"] = Shapes(halo, "AirborneTrooper"),
                ["canopyShapes"] = Shapes(halo, "AirborneCanopy"),
            };
        }

        private static Dictionary<string, object> Halo(Aircraft lead, float ahead)
        {
            if (lead == null) return Failure("AirborneAutomation", "halo", "no friendly aircraft");
            Vector3 exit = lead.GlobalPosition().AsVector3() - lead.transform.forward * 12f;
            Vector3 velocity = lead.rb != null ? lead.rb.velocity : lead.transform.forward * 80f;
            Vector3 flat = new Vector3(lead.transform.forward.x, 0f, lead.transform.forward.z).normalized;
            Vector3 aim = exit + flat * ahead + Vector3.Cross(Vector3.up, flat) * (ahead * 0.3f);
            aim.y = AirAssaultController.HaloGroundAt(aim);
            HaloGlidePlan plan = HaloGlidePlan.Build(exit, velocity, aim, AirAssaultController.HaloGroundAt, 8);
            AirAssaultVisuals.SpawnHaloDrop(plan, 0f);
            Vector3 miss = plan.StickLanding - aim;
            miss.y = 0f;
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["total"] = plan.TotalSeconds, ["lowDrop"] = plan.LowDrop ? 1 : 0, ["miss"] = miss.magnitude,
            };
        }

        private static Dictionary<string, object> Rope(Aircraft lead, bool roof)
        {
            if (lead == null) return Failure("AirborneAutomation", "rope", "no friendly aircraft");
            Vector3 origin = lead.transform.position;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 500f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                return Failure("AirborneAutomation", "rope", "nothing below");
            float height = origin.y - hit.point.y;
            RopeMode mode = FastRopePlan.ModeFor(height) ?? RopeMode.Rappel;
            GameObject shell = roof ? AirAssaultController.ResolveCivilianBuilding(hit.collider) : null;
            AirAssaultVisuals.SpawnRopeInsertion(lead, mode, height, hit.point, shell, 8);
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["height"] = height, ["rappel"] = mode == RopeMode.Rappel ? 1 : 0,
                ["seconds"] = FastRopePlan.InsertionSeconds(mode, height, 8), ["onRoof"] = shell != null ? 1 : 0,
            };
        }

        private static Dictionary<string, object> Exfil(Aircraft lead)
        {
            if (lead == null) return Failure("AirborneAutomation", "exfil", "no friendly aircraft");
            Vector3 origin = lead.transform.position;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 500f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                return Failure("AirborneAutomation", "exfil", "nothing below");
            AirAssaultVisuals.SpawnRopeExtraction(lead, hit.point + lead.transform.forward * 14f, 8, origin.y - hit.point.y);
            return new Dictionary<string, object> { ["ok"] = true, ["height"] = origin.y - hit.point.y };
        }

        private static Dictionary<string, object> Watch(string op, string child, float distance, float height)
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            GameObject root = GameObject.Find(op);
            Transform subject = root != null ? root.transform.Find(child) : null;
            if (camera == null || subject == null || !subject.gameObject.activeInHierarchy)
                return Failure("AirborneAutomation", "watch", op + "/" + child + " not visible");
            Vector3 side = Vector3.Cross(Vector3.up, subject.forward).normalized;
            Vector3 at = subject.position - subject.forward * distance * 0.6f + side * distance * 0.8f + Vector3.up * height;
            camera.SetCameraPosition(at.ToGlobalPosition(), Quaternion.LookRotation(subject.position - at));
            camera.cameraVelocity = Vector3.zero;
            camera.allowInputs = false;
            camera.SetDesiredFoV(50f, 50f);
            return new Dictionary<string, object> { ["ok"] = true, ["altitude"] = subject.position.y - Datum.LocalSeaY };
        }

        private static int Visible(string op)
        {
            GameObject root = GameObject.Find(op);
            if (root == null) return 0;
            int n = 0;
            foreach (Transform t in root.transform)
                if (t.gameObject.activeSelf && (t.name.StartsWith("Jumper_", StringComparison.Ordinal) || t.name.StartsWith("Trooper_", StringComparison.Ordinal))) n++;
            return n;
        }

        // ---- landing zone (tests/ingame/landing-zone.json): store ops on the lead, a real
        // controller drop against a far designated aimpoint, and where the stick actually goes.

        private static GlobalPosition dropZone, dropAim;
        private static bool dropRecorded;

        private static GlobalPosition Point(Dictionary<string, object> args, Aircraft lead)
        {
            if (Has(args, "rel") && lead != null)
            {
                GlobalPosition at = lead.GlobalPosition();
                return new GlobalPosition(at.x + Number(args, "dx", 0f), 0f, at.z + Number(args, "dz", 0f));
            }
            return new GlobalPosition(Number(args, "x", 0f), 0f, Number(args, "z", 0f));
        }

        private static Dictionary<string, object> Zone(Dictionary<string, object> args, Aircraft lead)
        {
            if (lead == null) return Failure("AirborneAutomation", "lz", "no friendly aircraft");
            switch (Text(args, "do") ?? "set")
            {
                case "clear":
                    LandingZones.Order(lead, null);
                    break;
                case "status":
                    break;
                case "localset":
                    if (!ModuleServices.TryGet(out ILandingZone local)) return Failure("AirborneAutomation", "lz", "no ILandingZone service");
                    local.SetLocal(Point(args, lead));
                    break;
                case "localclear":
                    if (!ModuleServices.TryGet(out ILandingZone zoneSvc)) return Failure("AirborneAutomation", "lz", "no ILandingZone service");
                    zoneSvc.ClearLocal();
                    break;
                default:
                    GlobalPosition point = Point(args, lead);
                    point.y = AirAssaultController.HaloGroundAt(point.AsVector3());
                    LandingZones.Order(lead, point);
                    break;
            }
            var state = new Dictionary<string, object> { ["ok"] = true };
            state["has"] = LandingZones.TryGet(lead, out GlobalPosition zone) ? 1 : 0;
            if (LandingZones.TryGet(lead, out zone)) { state["x"] = zone.x; state["z"] = zone.z; }
            state["localHas"] = ModuleServices.TryGet(out ILandingZone svc) && svc.HasLocal ? 1 : 0;
            return state;
        }

        private static Dictionary<string, object> Fit(Aircraft lead)
        {
            if (lead == null) return Failure("AirborneAutomation", "lzfit", "no friendly aircraft");
            if (!Encyclopedia.WeaponLookup.TryGetValue(AirAssaultController.HaloMountKey, out WeaponMount mount) || mount == null)
                return Failure("AirborneAutomation", "lzfit", "HALO mount missing from the bundle");
            WeaponManager prefab = (lead.definition as AircraftDefinition)?.unitPrefab.GetComponentInChildren<WeaponManager>(true);
            HardpointSet[] sets = lead.weaponManager.hardpointSets;
            for (int i = 0; prefab != null && i < sets.Length && i < prefab.hardpointSets.Length; i++)
                foreach (WeaponMount option in prefab.hardpointSets[i].weaponOptions)
                    if (!sets[i].weaponOptions.Contains(option)) sets[i].weaponOptions.Add(option);
            var loadout = new NuclearOption.SavedMission.Loadout { weapons = new List<WeaponMount>() };
            int bays = 0;
            for (int i = 0; i < sets.Length; i++)
            {
                bool bay = sets[i].weaponOptions.Contains(mount);
                loadout.weapons.Add(bay ? mount : null);
                if (bay) bays++;
            }
            if (bays == 0) return Failure("AirborneAutomation", "lzfit", "no cargo bay offers the HALO mount");
            lead.Networkloadout = loadout;
            lead.weaponManager.SpawnWeapons();
            int troops = 0;
            foreach (MountedTroops t in lead.GetComponentsInChildren<MountedTroops>(true)) troops++;
            if (troops == 0) { MountedTroops bay = BayTroops(lead); if (bay != null) troops = bay.ammo; }
            int hardpoints = 0, spawned = 0;
            var spawnedField = HarmonyLib.AccessTools.Field(typeof(Hardpoint), "spawnedPrefab");
            foreach (HardpointSet s in sets)
                foreach (Hardpoint h in s.hardpoints)
                {
                    hardpoints++;
                    if (spawnedField != null && spawnedField.GetValue(h) is GameObject go && go != null) spawned++;
                }
            string inside = "";
            foreach (HardpointSet s in sets)
            {
                foreach (Hardpoint h in s.hardpoints)
                {
                    if (spawnedField == null || !(spawnedField.GetValue(h) is GameObject go) || go == null) continue;
                    var types = new List<string>();
                    foreach (Component c in go.GetComponentsInChildren<Component>(true)) types.Add(c.GetType().Name);
                    inside = go.name + ": " + string.Join("|", types);
                    break;
                }
                if (inside.Length > 0) break;
            }
            var names = new List<string>();
            foreach (HardpointSet s in sets) names.Add(s.weaponMount != null ? s.weaponMount.name : "-");
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["bays"] = bays, ["troops"] = troops,
                ["stations"] = lead.weaponStations != null ? lead.weaponStations.Count : -1,
                ["sets"] = string.Join(",", names),
                ["restricted"] = lead.NetworkHQ != null && lead.NetworkHQ.restrictedWeapons.Contains(mount.name) ? 1 : 0,
                ["hardpoints"] = hardpoints, ["spawned"] = spawned,
                ["prefabNull"] = mount.prefab == null ? 1 : 0,
                ["inside"] = inside,
                ["troopsAll"] = UnityEngine.Object.FindObjectsOfType<MountedTroops>(true).Length,
                ["children"] = lead.GetComponentsInChildren<Transform>(true).Length,
                ["chain"] = Chain(lead),
            };
        }

        private static string Chain(Aircraft lead)
        {
            var field = HarmonyLib.AccessTools.Field(typeof(Hardpoint), "spawnedPrefab");
            if (field == null) return "nofield";
            foreach (HardpointSet s in lead.weaponManager.hardpointSets)
                foreach (Hardpoint h in s.hardpoints)
                {
                    if (!(field.GetValue(h) is GameObject go) || go == null) continue;
                    var names = new List<string>();
                    for (Transform t = go.transform; t != null; t = t.parent) names.Add(t.name);
                    return string.Join("<", names) + " | rootIsLead=" + (go.transform.root == lead.transform);
                }
            return "none";
        }

        // Sim spawns hang the airframe (and its bay mounts) off a separate root, so troops are
        // resolved through the hardpoints' spawned prefabs, not the aircraft hierarchy.
        private static MountedTroops BayTroops(Aircraft lead)
        {
            var field = HarmonyLib.AccessTools.Field(typeof(Hardpoint), "spawnedPrefab");
            if (field == null || lead.weaponManager == null) return null;
            foreach (HardpointSet s in lead.weaponManager.hardpointSets)
                foreach (Hardpoint h in s.hardpoints)
                    if (field.GetValue(h) is GameObject go && go != null)
                    {
                        MountedTroops troops = go.GetComponentInChildren<MountedTroops>(true);
                        if (troops != null && troops.ammo > 0) return troops;
                    }
            return null;
        }

        private static int Aboard(Aircraft lead)
        {
            int aboard = 0;
            foreach (MountedTroops troops in lead.GetComponentsInChildren<MountedTroops>())
                aboard = Math.Max(aboard, troops.ammo);
            MountedTroops bay = BayTroops(lead);
            return bay != null ? Math.Max(aboard, bay.ammo) : aboard;
        }

        private static Dictionary<string, object> Drop(Aircraft lead, Dictionary<string, object> args)
        {
            AirAssaultController controller = AirAssaultController.Instance;
            if (lead == null || controller == null) return Failure("AirborneAutomation", "lzdrop", "need lead and controller");
            dropAim = new GlobalPosition(Number(args, "ax", 0f), 0f, Number(args, "az", 0f));
            LandingZones.TryGet(lead, out dropZone);
            dropRecorded = true;
            int aboard = Aboard(lead);
            controller.DeployFromWeaponStation(lead, BayTroops(lead), null, null, dropAim);
            int troops = 0, active = 0, ammo = 0;
            foreach (MountedTroops t in lead.GetComponentsInChildren<MountedTroops>(true))
            {
                troops++;
                if (t.gameObject.activeInHierarchy) active++;
                ammo = Math.Max(ammo, t.ammo);
            }
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["aboard"] = aboard, ["left"] = Aboard(lead),
                ["troops"] = troops, ["troopsActive"] = active, ["troopsAmmo"] = ammo,
                ["stations"] = lead.weaponStations != null ? lead.weaponStations.Count : -1,
            };
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static Dictionary<string, object> ZoneStatus()
        {
            var state = new Dictionary<string, object> { ["ok"] = true, ["camps"] = InfantryEncampmentBuilder.ActiveSiteCount };
            state["smoke"] = GameObject.Find("BoscaliSummer.LandingSmoke") != null ? 1 : 0;
            float toZone = -1f, toAim = -1f;
            int jumpers = 0;
            GameObject root = GameObject.Find("BoscaliSummer.HaloDrop");
            if (root != null && dropRecorded)
                foreach (Transform t in root.transform)
                {
                    if (!t.gameObject.activeSelf || !t.name.StartsWith("Jumper_", StringComparison.Ordinal)) continue;
                    jumpers++;
                    Vector3 g = t.position.ToGlobalPosition().AsVector3();
                    float dz = Flat(g, dropZone.AsVector3()), da = Flat(g, dropAim.AsVector3());
                    toZone = toZone < 0f ? dz : Math.Min(toZone, dz);
                    toAim = toAim < 0f ? da : Math.Min(toAim, da);
                }
            state["jumpers"] = jumpers;
            state["toZone"] = toZone;
            state["toAim"] = toAim;
            state["campNearZone"] = dropRecorded && InfantryEncampmentBuilder.FindNearbySite(dropZone.ToLocalPosition(), 500f) != null ? 1 : 0;
            return state;
        }

        private static Dictionary<string, object> Status() => new Dictionary<string, object>
        {
            ["ok"] = true,
            ["jumpers"] = Visible("BoscaliSummer.HaloDrop"),
            ["inserting"] = Visible("BoscaliSummer.RopeInsertion"),
            ["extracting"] = Visible("BoscaliSummer.RopeExtraction"),
        };
    }
}
#endif
