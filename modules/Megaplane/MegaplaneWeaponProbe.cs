#if !PUBLIC_RELEASE
using System.Collections.Generic;
using System.Linq;
using NuclearOption.SavedMission;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    public static class MegaplaneWeaponProbe
    {
        private sealed class Launches : MonoBehaviour
        {
            internal Aircraft Aircraft;
            internal readonly Dictionary<uint, Vector3> Origins = new Dictionary<uint, Vector3>();

            private void LateUpdate()
            {
                foreach (Unit unit in UnitRegistry.allUnits)
                    if (unit is Missile missile && missile.ownerID == Aircraft.persistentID && missile.definition.jsonKey == "AAM2")
                        Origins.TryAdd(missile.persistentID.Id, Aircraft.transform.InverseTransformDirection(missile.NetworkstartOffsetFromOwner));
            }
        }

        public static object WingFit(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("idUnit", out object unit) || !(unit is Aircraft aircraft) || !MegaplaneModule.Is(aircraft))
                return new { ok = false };

            WeaponManager manager = aircraft.weaponManager;
            HardpointSet strike = manager.hardpointSets[0], escort = manager.hardpointSets[1];
            var available = new List<WeaponMount>();
            WeaponChecker.GetAvailableWeaponsNonAlloc(aircraft.Player, strike, null, aircraft.NetworkHQ, false, available);
            bool strikeOptions = available.Any(m => m.jsonKey == "AGM_heavy_single") && available.Any(m => m.jsonKey == "ARM1_double") &&
                available.Any(m => m.jsonKey == "bomb_500_glide_single") && available.All(m => m.jsonKey != "AAM2_double");
            WeaponChecker.GetAvailableWeaponsNonAlloc(aircraft.Player, escort, null, aircraft.NetworkHQ, false, available);
            bool escortOptions = available.Any(m => m.jsonKey == "AAM2_double") && available.Any(m => m.jsonKey == "AAM3_double") &&
                available.All(m => m.jsonKey != "AGM_heavy_single");

            if (args.ContainsKey("fit"))
            {
                if (!aircraft.TryGetComponent(out Launches launches)) launches = aircraft.gameObject.AddComponent<Launches>();
                launches.Aircraft = aircraft;
                launches.Origins.Clear();
                WeaponMount strikeMount = strike.weaponOptions.Find(m => m != null && m.jsonKey == "AGM_heavy_single");
                WeaponMount escortMount = escort.weaponOptions.Find(m => m != null && m.jsonKey == "AAM2_double");
                var fit = new Loadout { weapons = new List<WeaponMount>(aircraft.loadout.weapons) };
                fit.weapons[0] = strikeMount;
                fit.weapons[1] = escortMount;
                aircraft.Networkloadout = fit;
            }

            WeaponStation scythe = aircraft.weaponStations.Find(s => s.WeaponInfo != null && s.WeaponInfo.weaponName.Contains("Scythe") && !s.HasTurret());
            int before = scythe != null ? scythe.GetAmmoTotal() : 0;
            args.TryGetValue("idTargetUnit", out object target);
            if (args.ContainsKey("release") && scythe != null && target is Unit enemy)
            {
                aircraft.SetActiveStation(scythe.Number);
                scythe.LaunchMount(aircraft, enemy, enemy.GlobalPosition());
            }

            int spacedRacks(HardpointSet set) => set.hardpoints.Count(p => set.hardpoints.Any(q => q != p && q.part == p.part &&
                Vector3.Distance(p.transform.position, q.transform.position) > 17f));
            Launches observed = aircraft.GetComponent<Launches>();
            return new
            {
                ok = true, strikeOptions, escortOptions, distinctRacks = spacedRacks(strike) == 4 && spacedRacks(escort) == 4,
                strikeMount = strike.weaponMount != null ? strike.weaponMount.jsonKey : "",
                escortMount = escort.weaponMount != null ? escort.weaponMount.jsonKey : "",
                before, after = scythe != null ? scythe.GetAmmoTotal() : 0,
                fired = observed != null ? observed.Origins.Count : 0,
                bothWingsReleased = observed != null && observed.Origins.Values.Any(offset => offset.x < -140f) &&
                    observed.Origins.Values.Any(offset => offset.x > 140f),
            };
        }
    }
}
#endif
