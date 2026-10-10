using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
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

        private static Dictionary<string, object> Status() => new Dictionary<string, object>
        {
            ["ok"] = true,
            ["jumpers"] = Visible("BoscaliSummer.HaloDrop"),
            ["inserting"] = Visible("BoscaliSummer.RopeInsertion"),
            ["extracting"] = Visible("BoscaliSummer.RopeExtraction"),
        };
    }
}
