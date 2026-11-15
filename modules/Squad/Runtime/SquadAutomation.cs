#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using NuclearOption.Networking;
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

        /// <summary>
        /// tests/ingame/ace-heat.json: kills up to <c>kill</c> of the nearest hostile aircraft as the seated player (their parts take damage credited to the
        /// player's jet, so the native ReportKilled path runs) and reports the local HEAT, the hunt flag and the mission clock.
        /// </summary>
        public static Dictionary<string, object> Heat(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failure("SquadAutomation", "guard", "requires a nomodkit scenario");
            SquadManager manager = SquadRuntime.Active;
            if (manager == null || !GameManager.GetLocalAircraft(out Aircraft me) || me == null) return Failure("SquadAutomation", "Heat", "no squad manager or seat");
            int want = (int)Number(args, "kill", 0f), killed = 0;
            var targets = new List<Aircraft>();
            foreach (Unit unit in UnitRegistry.allUnits)
                if (unit is Aircraft a && a != me && !a.disabled && a.NetworkHQ != null && me.NetworkHQ != null && a.NetworkHQ != me.NetworkHQ) targets.Add(a);
            targets.Sort((x, y) => Vector3.Distance(x.transform.position, me.transform.position).CompareTo(Vector3.Distance(y.transform.position, me.transform.position)));
            for (int i = 0; i < targets.Count && killed < want; i++)
            {
                foreach (UnitPart part in targets[i].GetComponentsInChildren<UnitPart>())
                    if (!targets[i].disabled) part.TakeDamage(0f, 0f, 1f, 0f, 100000f, me.persistentID);
                // An airframe has no critical part: it dies later when it crashes. The damage credit is recorded, so finish the kill natively now.
                if (!targets[i].disabled) { targets[i].Networkdisabled = true; targets[i].ReportKilled(); }
                if (targets[i].disabled) killed++;
            }
            // The host reads its own board in-process: take this tick's snapshot rather than the last 1 Hz poll.
            if (GameManager.GetLocalPlayer<Player>(out Player player) && player != null)
                manager.Apply(manager.Snapshot(player), BoscaliSummer.Core.Contracts.PlayerIdentity.Of(player));
            float heat = manager.Heat;
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "killed", killed }, { "hostiles", targets.Count }, { "heat", float.IsNaN(heat) ? -1f : heat },
                { "hunt", manager.HuntActive }, { "mission_s", NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? -1f },
            };
            Debug.Log("[SquadAutomation] Heat: " + Describe(result));
            return result;
        }
    }
}
#endif
