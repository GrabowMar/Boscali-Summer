#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    public static partial class VanguardAutomation
    {
        private const string AegisMount = "VG_Aegis_Pod";

        /// <summary>
        /// AEGIS-3 sim hook: fit the pod to the lead on first use (first legal hardpoint set, other stores kept), select
        /// AEGIS in the countermeasures menu and pull the CM trigger; press:0 only reports.
        /// The aegis.* counters are mirrored under "aegis" because sim check paths split on dots.
        /// </summary>
        private static Dictionary<string, object> Snap(Aircraft lead, Unit target, bool press)
        {
            if (lead == null || lead.weaponManager == null) return Failure("VanguardAutomation", "snap", "no friendly aircraft");
            if (!AegisTrigger.HasPods(lead) && !FitAegis(lead))
                return Failure("VanguardAutomation", "snap", AegisMount + " not fitted");
            int before = AegisTrigger.TotalAmmo(lead);
            if (press && lead.countermeasureManager != null)
            {
                SelectAegisCm(lead);
                lead.countermeasureManager.DeployCountermeasure(lead);
            }
            var aegis = new Dictionary<string, object> { ["ammoBefore"] = before, ["ammo"] = AegisTrigger.TotalAmmo(lead) };
            foreach (KeyValuePair<string, int> c in VanguardStats.Counters)
                if (c.Key.StartsWith("aegis.", StringComparison.Ordinal)) aegis[c.Key.Substring(6)] = c.Value;
            if (target != null) aegis["range"] = FastMath.Distance(target.GlobalPosition(), lead.GlobalPosition());
            Dictionary<string, object> state = Status(lead);
            state["aegis"] = aegis;
            return state;
        }

        // `aegisfit`: put the pod on the lead before the first deploy; mounts rebuild a frame later, so wait ~2 s.
        // A second call with `darts` empties the cells past that count.
        private static Dictionary<string, object> AegisFit(Aircraft lead, int darts)
        {
            if (lead == null || lead.weaponManager == null) return Failure("VanguardAutomation", "aegisfit", "no friendly aircraft");
            if (!AegisTrigger.HasPods(lead) && !FitAegis(lead) && lead.Networkloadout?.weapons?.Exists(w => w != null && w.jsonKey == AegisMount) != true)
                return Failure("VanguardAutomation", "aegisfit", "no hardpoint set offers " + AegisMount);
            if (darts > 0 && AegisTrigger.HasPods(lead)) AegisTrigger.TrimAircraftDarts(lead, darts);
            return Status(lead);
        }

        private static void SelectAegisCm(Aircraft lead)
        {
            string[] names = new string[8];
            int count = Core.Game.AircraftReadout.CountermeasureOrder(lead, names);
            for (int i = 0; i < count; i++)
            {
                if (names[i] == AegisIntercept.DisplayName)
                {
                    lead.Countermeasures(false, (byte)i);
                    return;
                }
            }
        }

        private static bool FitAegis(Aircraft lead)
        {
            WeaponMount pod = MountOf(AegisMount);
            RuntimeStores.Adopt(lead);
            HardpointSet[] sets = lead.weaponManager.hardpointSets;
            int set = pod == null ? -1 : Array.FindIndex(sets, h => h.weaponOptions.Contains(pod));
            if (set < 0) return false;
            var weapons = new List<WeaponMount>(lead.loadout?.weapons ?? new List<WeaponMount>());
            while (weapons.Count < sets.Length) weapons.Add(null);
            weapons[set] = pod;
            lead.Networkloadout = new NuclearOption.SavedMission.Loadout { weapons = weapons };
            return AegisTrigger.HasPods(lead);
        }
    }
}
#endif
