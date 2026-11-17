#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Runtime;
using NuclearOption.SavedMission;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>Native carriage/release acceptance fixture; never available outside a nomodkit host scenario.</summary>
    public static class Sa74Automation
    {
        private static Aircraft carrier;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) || !GameAccess.IsServer())
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "requires a nomodkit host scenario" };
            Sa74Store.Resolve();
            Aircraft craft = Find(Text(args, "type") ?? "Tarantula");
            switch (Text(args, "action"))
            {
                case "equip":
                    if (craft == null || !Sa74Store.Pair(craft.definition?.jsonKey, out Sa74Store.CargoPair pair)) return Fail("aircraft has no two-slot cargo bay");
                    RuntimeStores.Adopt(craft);
                    var loadout = new Loadout { weapons = new List<WeaponMount>(new WeaponMount[craft.weaponManager.hardpointSets.Length]) };
                    loadout.weapons[pair.Lead] = RuntimeStores.Mount(Sa74Store.MountKey);
                    craft.Networkloadout = loadout;
                    carrier = craft;
                    return Report(craft);
                case "release":
                    if (craft == null) return Fail("no carrier");
                    WeaponStation station = Station(craft);
                    if (station == null) return Fail("bomb station has not spawned");
                    craft.weaponManager.SetActiveStation(station.Number);
                    craft.weaponManager.ClearTargetList();
                    craft.weaponManager.Fire();
                    return Report(craft);
                case "look": return Look(craft, Number(args, "distance", 28f));
                case "lookBomb": return Look(Bombs().FirstOrDefault(), Number(args, "distance", 16f));
                case "unfreeze":
                    Time.timeScale = 1f;
                    if (SceneSingleton<CameraStateManager>.i is CameraStateManager camera) camera.allowInputs = true;
                    return Report(craft);
                default: return Report(craft);
            }
        }

        private static Aircraft Find(string type)
        {
            if (carrier != null && !carrier.disabled && carrier.definition?.unitName.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0) return carrier;
            return UnityEngine.Object.FindObjectsOfType<Aircraft>().FirstOrDefault(a => a != null && !a.disabled &&
                a.definition != null && a.definition.unitName.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static IEnumerable<Missile> Bombs() => UnityEngine.Object.FindObjectsOfType<Missile>().Where(m => Sa74Store.Is(m) && !m.disabled);
        private static WeaponStation Station(Aircraft craft) => craft?.weaponStations.FirstOrDefault(s => s.Weapons.Any(Sa74Store.Is));

        private static Dictionary<string, object> Report(Aircraft craft)
        {
            WeaponMount mount = RuntimeStores.Mount(Sa74Store.MountKey);
            bool eligible = Sa74Store.Pair(craft?.definition?.jsonKey, out Sa74Store.CargoPair pair);
            WeaponStation station = Station(craft);
            Missile bomb = Bombs().FirstOrDefault();
            bool conflictRejected = false;
            if (eligible && craft?.weaponManager != null && mount != null)
            {
                var loadout = new Loadout { weapons = new List<WeaponMount>(new WeaponMount[craft.weaponManager.hardpointSets.Length]) };
                loadout.weapons[pair.Lead] = mount;
                conflictRejected = pair.Lead == pair.Partner ||
                    !WeaponChecker.MountAllowedConflict(craft.weaponManager.hardpointSets[pair.Partner], loadout);
            }
            int renderers = station != null ? station.Weapons.Where(Sa74Store.Is).Sum(w => w.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled)) : 0;
            Weapon rail = station?.Weapons.FirstOrDefault(Sa74Store.Is);
            Renderer[] art = rail != null ? rail.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray() : Array.Empty<Renderer>();
            Bounds bounds = art.Length > 0 ? art[0].bounds : default;
            foreach (Renderer renderer in art) bounds.Encapsulate(renderer.bounds);
            Vector3 localCenter = craft != null ? craft.transform.InverseTransformPoint(bounds.center) : Vector3.zero;
            Vector3 scale = rail != null ? rail.transform.lossyScale : Vector3.zero;
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["built"] = mount != null, ["eligibleAircraft"] = string.Join(",", Sa74Store.EligibleAircraft),
                ["pairCount"] = Sa74Store.PairCount, ["capacitySlots"] = eligible ? 2 : 0,
                ["massKg"] = Sa74Store.MassKg, ["leadSet"] = eligible ? pair.Lead : -1, ["partnerSet"] = eligible ? pair.Partner : -1,
                ["partnerBlocked"] = eligible && pair.Lead != pair.Partner && craft?.loadout != null &&
                    craft.weaponManager.hardpointSets[pair.Partner].BlockedByOtherHardpoint(craft.loadout),
                ["conflictRejected"] = conflictRejected, ["loadedRounds"] = station?.Ammo ?? 0,
                ["modelRenderers"] = renderers, ["flightCount"] = Bombs().Count(),
                ["activeModelRenderers"] = art.Count(r => r.gameObject.activeInHierarchy), ["railLayer"] = rail != null ? rail.gameObject.layer : -1,
                ["railScale"] = new[] { scale.x, scale.y, scale.z },
                ["modelCenterInCarrier"] = new[] { localCenter.x, localCenter.y, localCenter.z },
                ["modelWorldSize"] = new[] { bounds.size.x, bounds.size.y, bounds.size.z },
                ["armed"] = bomb?.GetComponent<Sa74Flight>()?.Armed ?? false,
                ["bombAltitude"] = bomb != null ? bomb.transform.position.y - Datum.LocalSeaY : -1f,
                ["aircraft"] = craft?.definition?.unitName ?? ""
            };
        }

        private static Dictionary<string, object> Look(Unit unit, float distance)
        {
            if (unit == null || SceneSingleton<CameraStateManager>.i == null) return Fail("nothing to frame");
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            Vector3 offset = unit.transform.right * distance - unit.transform.up * (distance * .25f) + unit.transform.forward * (distance * .3f);
            camera.SetCameraPosition(unit.GlobalPosition() + offset, Quaternion.LookRotation(-offset, unit.transform.up));
            camera.cameraVelocity = Vector3.zero;
            camera.allowInputs = false;
            camera.SetDesiredFoV(45f, 45f);
            Time.timeScale = 0f;
            return new Dictionary<string, object> { ["ok"] = true, ["looked"] = true };
        }
        private static Dictionary<string, object> Fail(string message) => new Dictionary<string, object> { ["ok"] = false, ["error"] = message };
    }
}
#endif
