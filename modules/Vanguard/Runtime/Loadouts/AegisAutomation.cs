using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Runtime;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    public static partial class VanguardAutomation
    {
        private const string AegisMount = "VG_Aegis_Pod";

        /// <summary>
        /// AEGIS-3 sim hook: fit the pod to the lead on first use (first legal hardpoint set, other stores kept), select it
        /// and pull the real trigger (WeaponManager.Fire -> AegisSnapPatch) with the target list = [target] or empty;
        /// press:0 only reports. The aegis.* counters are mirrored under "aegis" because sim check paths split on dots.
        /// </summary>
        private static Dictionary<string, object> Snap(Aircraft lead, Unit target, bool press)
        {
            if (lead == null || lead.weaponManager == null) return Failure("VanguardAutomation", "snap", "no friendly aircraft");
            WeaponStation station = AegisStation(lead) ?? FitAegis(lead);
            if (station == null) return Failure("VanguardAutomation", "snap", AegisMount + " not fitted");
            int before = station.Ammo;
            if (press)
            {
                WeaponManager weapons = lead.weaponManager;
                weapons.currentWeaponStation = station;
                List<Unit> targets = weapons.GetTargetList();
                targets.Clear();
                if (target != null) targets.Add(target);
                weapons.Fire();
            }
            var aegis = new Dictionary<string, object> { ["ammoBefore"] = before, ["ammo"] = station.Ammo };
            foreach (KeyValuePair<string, int> c in VanguardStats.Counters)
                if (c.Key.StartsWith("aegis.", StringComparison.Ordinal)) aegis[c.Key.Substring(6)] = c.Value;
            if (target != null) aegis["range"] = FastMath.Distance(target.GlobalPosition(), lead.GlobalPosition());
            Dictionary<string, object> state = Status(lead);
            state["aegis"] = aegis;
            return state;
        }

        // `aegisfit`: put the pod on the lead before the first snap; weapon stations rebuild a frame later, so wait ~2 s.
        private static Dictionary<string, object> AegisFit(Aircraft lead)
        {
            if (lead == null || lead.weaponManager == null) return Failure("VanguardAutomation", "aegisfit", "no friendly aircraft");
            if (AegisStation(lead) == null && FitAegis(lead) == null && lead.Networkloadout?.weapons?.Exists(w => w != null && w.jsonKey == AegisMount) != true)
                return Failure("VanguardAutomation", "aegisfit", "no hardpoint set offers " + AegisMount);
            return Status(lead);
        }

        private static WeaponStation AegisStation(Aircraft aircraft)
        {
            foreach (WeaponStation s in aircraft.weaponStations)
                if (AegisTrigger.IsAegis(s)) return s;
            return null;
        }

        private static WeaponStation FitAegis(Aircraft lead)
        {
            WeaponMount pod = null;
            foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                if (w != null && w.jsonKey == AegisMount) pod = w;
            HardpointSet[] sets = lead.weaponManager.hardpointSets;
            int set = pod == null ? -1 : Array.FindIndex(sets, h => h.weaponOptions.Contains(pod));
            if (set < 0) return null;
            var weapons = new List<WeaponMount>(lead.loadout?.weapons ?? new List<WeaponMount>());
            while (weapons.Count < sets.Length) weapons.Add(null);
            weapons[set] = pod;
            lead.Networkloadout = new NuclearOption.SavedMission.Loadout { weapons = weapons };
            return AegisStation(lead);
        }
    }
}
