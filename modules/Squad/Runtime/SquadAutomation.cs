#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Squad.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/ace-alerts.json); inert outside a sim run. Spawns an ace wing on the seated player through the same path as
    /// the MISC cheat (<see cref="SquadManager.DebugSpawnWing"/>) and reports the ace it put up and how far away.
    /// </summary>
    public static class SquadAutomation
    {
        public static Dictionary<string, object> Spawn(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failure("SquadAutomation", "guard", "requires a nomodkit scenario");
            SquadManager manager = SquadRuntime.Active;
            if (manager == null) return Failure("SquadAutomation", "Spawn", "no squad manager");
            string said = manager.DebugSpawnWing((int)Number(args, "tier", 3f));
            bool up = manager.TryAce(out Aircraft ace, out string callsign);
            if (!up) return Failure("SquadAutomation", "Spawn", said);
            float range = up && GameManager.GetLocalAircraft(out Aircraft me) && me != null ? Vector3.Distance(ace.transform.position, me.transform.position) : -1f;
            var result = new Dictionary<string, object>
            {
                { "ok", up }, { "said", said }, { "ace", up ? callsign : "" }, { "range_m", range },
            };
            Debug.Log("[SquadAutomation] Spawn: " + Describe(result));
            return result;
        }
    }
}
#endif
