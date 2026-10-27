#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    // ---- runtime stores (tests/ingame/vg-stores.json): put TORCH / SUNSPEAR / STORMCELL / WATCHTOWER / MANTA on live jets
    // through their real stations, freeze-frame close-ups of the mounts, and fire a store's station the way the trigger does.
    public static partial class VanguardAutomation
    {
        private static readonly string[] RuntimeKeys =
            { VanguardKeys.MantaMount, VanguardKeys.TorchMount, VanguardKeys.SunspearMount, VanguardKeys.StormcellMount, VanguardKeys.WatchtowerMount };

        private static Dictionary<string, object> StoresStep(Dictionary<string, object> args, Aircraft lead, Aircraft bandit)
        {
            Aircraft craft = Text(args, "type") is string type ? FindType(type) : lead;
            switch (Text(args, "do"))
            {
                case "report": return StoresReport();
                case "equip": return StoresEquip(craft, Text(args, "keys"));
                case "look": return StoresLook(Text(args, "target") == "manta" ? (Unit)FindManta() : craft, Text(args, "angle") ?? "below", Number(args, "distance", 18f));
                case "unfreeze":
                    Time.timeScale = 1f;
                    if (SceneSingleton<CameraStateManager>.i is CameraStateManager cam) cam.allowInputs = true;
                    return new Dictionary<string, object> { ["ok"] = true };
                case "fire": return StoresFire(craft, Text(args, "key"), Text(args, "at") == "none" ? null : bandit, Number(args, "seconds", 0f));
                case "status": return StoresStatus(craft);
                default: return Failure("VanguardAutomation", "stores", "do = report | equip | look | unfreeze | fire | status");
            }
        }

        private static Aircraft FindType(string type)
        {
            foreach (Aircraft a in UnityEngine.Object.FindObjectsOfType<Aircraft>())
                if (!a.disabled && a.definition != null && a.definition.unitName.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0) return a;
            return null;
        }

        private static Missile FindManta()
        {
            foreach (Missile m in UnityEngine.Object.FindObjectsOfType<Missile>())
                if (!m.disabled && m.definition != null && m.definition.jsonKey == VanguardKeys.Manta) return m;
            return null;
        }

        private static Dictionary<string, object> StoresReport()
        {
            var result = new Dictionary<string, object> { ["ok"] = true };
            foreach (string key in RuntimeKeys)
            {
                WeaponMount mount = RuntimeStores.Mount(key);
                result[key + "_built"] = mount != null;
                result[key + "_name"] = mount != null ? mount.mountName : "";
                result[key + "_stations"] = RuntimeStores.Placed.Count(p => p.Store == key);
                result[key + "_aircraft"] = string.Join(",", RuntimeStores.Placed.Where(p => p.Store == key).Select(p => p.Aircraft).Distinct());
            }
            MissileDefinition manta = Encyclopedia.i.missiles.Find(d => d != null && d.jsonKey == VanguardKeys.Manta);
            result["manta_definition"] = manta != null;
            result["manta_index"] = manta != null ? (((INetworkDefinition)manta).LookupIndex ?? -1) : -1;
            result["manta_hash"] = manta != null && manta.unitPrefab != null && manta.unitPrefab.TryGetComponent(out Mirage.NetworkIdentity id) ? id.PrefabHash : 0;
            return result;
        }

        // Fills the free stations that offer each store, in the order given; a station the loadout blocks (a MANTA's partner) is skipped.
        private static Dictionary<string, object> StoresEquip(Aircraft craft, string keys)
        {
            if (craft == null || craft.weaponManager == null) return Failure("VanguardAutomation", "stores", "no such aircraft");
            RuntimeStores.Adopt();
            HardpointSet[] sets = craft.weaponManager.hardpointSets;
            var loadout = new NuclearOption.SavedMission.Loadout { weapons = new List<WeaponMount>() };
            for (int i = 0; i < sets.Length; i++) loadout.weapons.Add(null);
            var placed = new List<string>();
            var missing = new List<string>();
            foreach (string key in (keys ?? "").Split(',').Select(k => k.Trim()).Where(k => k.Length > 0))
            {
                WeaponMount mount = RuntimeStores.Mount(key);
                int set = -1;
                for (int i = 0; mount != null && i < sets.Length && set < 0; i++)
                    if (loadout.weapons[i] == null && sets[i].weaponOptions.Contains(mount) && !sets[i].BlockedByOtherHardpoint(loadout)) set = i;
                if (set < 0) { missing.Add(key); continue; }
                loadout.weapons[set] = mount;
                placed.Add(key + "@" + sets[set].name);
            }
            craft.Networkloadout = loadout;
            var blocked = new List<string>();
            var layout = new List<string>();
            for (int i = 0; i < sets.Length; i++)
            {
                if (loadout.weapons[i] == null && sets[i].BlockedByOtherHardpoint(loadout)) blocked.Add(sets[i].name);
                layout.Add(i + " " + sets[i].name + " [" + string.Join("/", RuntimeKeys.Where(k => sets[i].weaponOptions.Contains(RuntimeStores.Mount(k)))) + "]");
            }
            // A store that does not fit is reported, not failed: the scenario hangs it on another jet.
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["aircraft"] = craft.definition.unitName, ["placed"] = string.Join("; ", placed), ["placedCount"] = placed.Count,
                ["missing"] = string.Join(",", missing), ["blocked"] = string.Join(",", blocked), ["stations"] = string.Join(" | ", layout)
            };
        }

        // Freezes the game (Time.timeScale 0) and parks a free camera on the unit: a 200 m/s jet stands still for the capture.
        private static Dictionary<string, object> StoresLook(Unit unit, string angle, float distance)
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null) return Failure("VanguardAutomation", "stores", "no camera");
            if (unit == null) return new Dictionary<string, object> { ["ok"] = true, ["looked"] = false }; // not out yet: the capture shows the last view
            Transform t = unit.transform;
            Vector3 offset;
            switch (angle)
            {
                case "side": offset = t.right * distance + t.up * (distance * 0.15f); break;
                case "front": offset = t.forward * distance - t.up * (distance * 0.25f) + t.right * (distance * 0.35f); break;
                case "rear": offset = -t.forward * distance + t.up * (distance * 0.2f) + t.right * (distance * 0.45f); break;
                default: offset = -t.up * (distance * 0.55f) + t.right * (distance * 0.75f) + t.forward * (distance * 0.35f); break; // below, front quarter
            }
            Time.timeScale = 0f;
            GlobalPosition at = unit.GlobalPosition() + offset;
            camera.SetCameraPosition(at, Quaternion.LookRotation(-offset, t.up));
            camera.cameraVelocity = Vector3.zero;
            camera.allowInputs = false;
            camera.SetDesiredFoV(45f, 45f);
            return new Dictionary<string, object> { ["ok"] = true, ["unit"] = unit.unitName, ["angle"] = angle };
        }

        private static WeaponStation StationOf(Aircraft craft, string key)
        {
            WeaponMount mount = RuntimeStores.Mount(key);
            if (craft == null || mount == null) return null;
            foreach (WeaponStation station in craft.weaponStations)
                if (station.WeaponInfo != null && (station.WeaponInfo == mount.info || (mount.info != null && station.WeaponInfo.weaponName == mount.info.weaponName)))
                    return station;
            return null;
        }

        // The trigger's path: select the store's station, aim it at the target, WeaponManager.Fire. A laser has to be held, so
        // "seconds" keeps pressing every physics step.
        private static Dictionary<string, object> StoresFire(Aircraft craft, string key, Unit target, float seconds)
        {
            WeaponStation station = StationOf(craft, key);
            // Reported, not failed, so the rest of a showcase still runs; the scenario's checks catch it.
            if (station == null) return new Dictionary<string, object> { ["ok"] = true, ["fired"] = 0, ["error"] = key + " has no station on " + (craft != null ? craft.unitName : "nothing") };
            craft.weaponManager.ClearTargetList();
            if (target != null) craft.weaponManager.AddTargetList(target);
            craft.weaponManager.SetActiveStation(station.Number);
            int before = station.Ammo;
            if (seconds > 0f) craft.gameObject.AddComponent<TriggerHold>().Until = Time.timeSinceLevelLoad + seconds;
            else craft.weaponManager.Fire();
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["station"] = station.WeaponInfo.weaponName, ["ammoBefore"] = before, ["ammoAfter"] = station.Ammo,
                ["targetOffNoseDeg"] = target != null ? Vector3.Angle(craft.transform.forward, target.transform.position - craft.transform.position) : -1f,
                ["targetKm"] = target != null ? Vector3.Distance(craft.transform.position, target.transform.position) / 1000f : -1f,
                ["charge"] = craft.GetPowerSupply() != null ? craft.GetPowerSupply().GetCharge() : -1f
            };
        }

        private static Dictionary<string, object> StoresStatus(Aircraft craft)
        {
            if (craft == null) return Failure("VanguardAutomation", "stores", "no such aircraft");
            TorchPod.Burn(craft, out int lit, out float age, out float thrust);
            var status = new Dictionary<string, object>();
            status["torchThrust"] = thrust;
            PowerSupply power = craft.GetPowerSupply();
            Missile manta = FindManta();
            // SUNSPEAR diagnostics: a vanilla Laser fires only when its own forward can be turned onto the target within maxAngle.
            Laser laser = craft.GetComponentsInChildren<Laser>(true).FirstOrDefault();
            Aircraft target = FindAircraft("Primeva");
            if (laser != null)
            {
                status["laserNoseOffDeg"] = Vector3.Angle(laser.transform.forward, craft.transform.forward);
                status["laserTargetOffDeg"] = target != null ? Vector3.Angle(laser.transform.forward, target.transform.position - laser.transform.position) : -1f;
                status["laserEnabled"] = laser.enabled;
                status["laserFireCommanded"] = HarmonyLib.Traverse.Create(laser).Field("fireCommanded").GetValue<bool>();
                status["laserMaxAngle"] = HarmonyLib.Traverse.Create(laser).Field("maxAngle").GetValue<float>();
                Renderer beam = HarmonyLib.Traverse.Create(laser).Field("beamRenderer").GetValue<Renderer>();
                status["laserBeam"] = beam != null && beam.enabled;
            }
            status["laserFound"] = laser != null;
            return new Dictionary<string, object>(status)
            {
                ["ok"] = true, ["aircraft"] = craft.definition.unitName, ["speed"] = craft.rb != null ? craft.rb.velocity.magnitude : 0f,
                ["torchLit"] = lit, ["torchAge"] = age, ["charge"] = power != null ? power.GetCharge() : -1f,
                ["chargeKJ"] = power != null ? power.GetChargeKJ() : -1f, ["relayScale"] = WatchtowerPod.LinkScale(craft),
                ["mantaAirborne"] = manta != null, ["mantaScale"] = manta != null ? manta.transform.lossyScale.x : 0f,
                ["mantaSpeed"] = manta != null && manta.rb != null ? manta.rb.velocity.magnitude : 0f,
                ["stations"] = string.Join(",", craft.weaponStations.Select(s => s.WeaponInfo != null ? s.WeaponInfo.shortName + ":" + s.Ammo : "?"))
            };
        }

        /// <summary>Holds the trigger (the active station's WeaponManager.Fire every physics step) until a game time, then goes.</summary>
        private sealed class TriggerHold : MonoBehaviour
        {
            public float Until;
            private Aircraft craft;

            private void Awake() => craft = GetComponent<Aircraft>();

            private void FixedUpdate()
            {
                if (craft == null || craft.disabled || Time.timeSinceLevelLoad > Until) { Destroy(this); return; }
                craft.weaponManager.Fire();
            }
        }
    }
}
#endif
