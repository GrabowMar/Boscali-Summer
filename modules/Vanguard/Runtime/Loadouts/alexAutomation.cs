using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>ALE-X KITE hooks for tests/ingame/vg-alex.json: the cut press, and the status keys (numeric only).</summary>
    public static partial class VanguardAutomation
    {
        // `cut`: the pilot's second fire press on the pod, through the same door AlexFire / AlexNet use.
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
                if (counter.Key.StartsWith("alex.", System.StringComparison.Ordinal))
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
