#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>ALE-X KITE hooks for tests/ingame/vg-alex.json: fit the pod, press the trigger, cut, and the status keys (numeric only).</summary>
    public static partial class VanguardAutomation
    {
        // `alexfit`: the lead carries the ALE-X pod (and nothing else) on the first hardpoint set that offers it,
        // set the way Equip does it. The pod rebuilds the lead's weapon stations, so wait a second before pressing.
        private static Dictionary<string, object> AlexFit(Aircraft lead)
        {
            if (lead == null) return Failure("VanguardAutomation", "alexfit", "no friendly aircraft");
            WeaponMount pod = null;
            foreach (WeaponMount mount in Encyclopedia.i.weaponMounts)
                if (mount != null && mount.jsonKey == AlexKite.MountKey) pod = mount;
            AdoptPrefabOptions(lead);
            HardpointSet[] sets = lead.weaponManager.hardpointSets;
            int slot = pod == null ? -1 : Array.FindIndex(sets, set => set.weaponOptions.Contains(pod));
            if (slot < 0) return Failure("VanguardAutomation", "alexfit", pod == null ? "no " + AlexKite.MountKey + " mount in the Encyclopedia" : "no hardpoint set offers the pod");
            var loadout = new NuclearOption.SavedMission.Loadout { weapons = new List<WeaponMount>() };
            for (int i = 0; i < sets.Length; i++) loadout.weapons.Add(i == slot ? pod : null);
            lead.Networkloadout = loadout;
            Dictionary<string, object> state = Status(lead);
            state["fit_set"] = slot;
            return state;
        }

        // `alexpress` (calls = N): select the pod and call the REAL WeaponManager.Fire N times in this frame, the way a
        // held trigger calls it once per frame. The press path (AlexPressPatch) must act once and swallow the rest,
        // also with the pod empty (press_ready 0), where vanilla would never reach LaunchMount.
        private static Dictionary<string, object> AlexPress(Aircraft lead, int calls)
        {
            if (lead == null) return Failure("VanguardAutomation", "alexpress", "no friendly aircraft");
            WeaponStation pod = AlexService.PodStation(lead, false);
            if (pod == null) return Failure("VanguardAutomation", "alexpress", "the lead has no ALE-X pod (alexfit first)");
            lead.weaponManager.SetActiveStation(pod.Number);
            uint id = lead.persistentID.Id;
            bool ready = pod.Ready(), towed = AlexBoard.TowedBy(id, out _);
            int ammo = pod.Ammo;
            for (int i = 0; i < calls; i++) lead.weaponManager.Fire();
            Dictionary<string, object> state = Status(lead);
            state["press_calls"] = calls;
            state["press_ready"] = ready ? 1 : 0;
            state["press_ammoBefore"] = ammo;
            state["press_ammoAfter"] = pod.Ammo;
            state["press_used"] = ammo - pod.Ammo;
            state["press_towedBefore"] = towed ? 1 : 0;
            state["press_towedAfter"] = AlexBoard.TowedBy(id, out _) ? 1 : 0;
            return state;
        }

        // `cut`: the cut itself, straight through the door AlexFire / AlexNet use (no trigger, no Ready gate).
        private static Dictionary<string, object> AlexCut(Aircraft lead)
        {
            if (lead == null) return Failure("VanguardAutomation", "cut", "no friendly aircraft");
            bool done = AlexService.Cut(lead, KiteCut.Pilot);
            Dictionary<string, object> state = Status(lead);
            state["cutDone"] = done ? 1 : 0;
            return state;
        }

        // alex_<counter> mirrors every "alex.<counter>" (the scenario's dotted paths cannot reach a dotted key),
        // alex_state = KiteState (-1 none), plus the lead's decoy line, pips and geometry.
        private static void AlexStatus(Dictionary<string, object> state, Aircraft lead)
        {
            foreach (KeyValuePair<string, int> counter in VanguardStats.Counters)
                if (counter.Key.StartsWith("alex.", StringComparison.Ordinal))
                    state["alex_" + counter.Key.Substring(5).Replace('.', '_')] = counter.Value;
            state["alex_state"] = -1;
            if (lead == null || !AlexBoard.ForHost(lead.persistentID.Id, out AlexView view)) return;
            state["alex_state"] = (int)view.State;
            state["alex_line"] = view.Line;
            state["alex_lineTarget"] = view.Target;
            state["alex_pips"] = (int)view.Pips;
            state["alex_captured"] = (int)view.Captured;
            state["alex_dragged"] = (int)view.Dragged;
            state["alex_freeLeft"] = view.FreeLeft;
            if (UnitRegistry.TryGetUnit(new PersistentID { Id = view.Decoy }, out Unit unit) && unit is Missile decoy && !decoy.disabled)
            {
                state["alex_fromAnchor"] = Vector3.Distance(TowAnchor.RootFor(decoy, lead), decoy.transform.position);
                state["alex_fromHost"] = Vector3.Distance(lead.transform.position, decoy.transform.position);
            }
        }
    }
}
#endif
