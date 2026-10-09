using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// SKYWELL pit-stop sim hooks (tests/ingame/vg-skywell.json). Inert outside a nomodkit scenario, like the rest of Step.
    /// A receiver is named with <c>"rx": "&lt;registered id&gt;"</c> (the runner passes the live aircraft as <c>rxUnit</c>), so a
    /// scenario can queue several receivers on one tanker and read each one's own tally afterwards, with no timing guesses.
    /// </summary>
    public static partial class VanguardAutomation
    {
        private static Aircraft Rx(Dictionary<string, object> args) => Arg(args, "rxUnit") as Aircraft ?? receiver;

        /// <summary>The first standard loadout that actually carries weapons (a default AI spawn may be bare).</summary>
        private static NuclearOption.SavedMission.Loadout ArmedLoadout(Aircraft a)
        {
            foreach (StandardLoadout sl in a.definition.aircraftParameters.StandardLoadouts ?? Array.Empty<StandardLoadout>())
                if (sl?.loadout?.weapons != null && sl.loadout.weapons.Exists(w => w != null)) return sl.loadout;
            return null;
        }

        /// <summary>Arm the named receiver; give the weapons a moment to spawn before draining it.</summary>
        private static Dictionary<string, object> SkywellArm(Dictionary<string, object> args)
        {
            Aircraft rx = Rx(args);
            if (rx == null) return Failure("VanguardAutomation", "pitarm", "rx must be a registered aircraft id");
            NuclearOption.SavedMission.Loadout armed = ArmedLoadout(rx);
            if (armed != null) rx.Networkloadout = armed;
            return new Dictionary<string, object> { ["ok"] = true, ["armed"] = armed != null ? 1 : 0 };
        }

        /// <summary>Set the receiver's standing pit order and priority station (order both/fuel/munitions, station n | -1 | "last").</summary>
        private static Dictionary<string, object> SkywellPit(Dictionary<string, object> args)
        {
            Aircraft rx = Rx(args);
            if (rx == null) return Failure("VanguardAutomation", "pit", "rx must be a registered aircraft id (or equip first)");
            string word = Text(args, "order") ?? "both";
            PitOrder order = word == "fuel" ? PitOrder.Fuel : word == "munitions" ? PitOrder.Munitions : PitOrder.Both;
            int station = (int)Number(args, "station", -1f);
            if (Text(args, "station") == "last") // the highest station that is missing rounds
            {
                station = -1;
                for (int i = rx.weaponStations.Count - 1; i >= 0 && station < 0; i--)
                    if (!SkywellStations.Skip(rx.weaponStations[i]) && rx.weaponStations[i].FullAmmo - rx.weaponStations[i].GetAmmoTotal() > 0) station = i;
            }
            bool accepted = SkywellService.SetPrefs(rx, order, station);
            return new Dictionary<string, object> { ["ok"] = true, ["pitOrder"] = (int)order, ["pitPriority"] = station, ["pitAccepted"] = accepted ? 1 : 0 };
        }

        /// <summary>One-shot: when the named receiver next enters Transfer, spawn the usual threat on it (hot-break check).</summary>
        private static Dictionary<string, object> SkywellThreatWhen(Aircraft bandit, Dictionary<string, object> args)
        {
            Aircraft victim = Rx(args);
            if (victim == null || bandit == null) return Failure("VanguardAutomation", "threatwhen", "need a registered rx and a bandit");
            float range = Number(args, "range", 6000f), climb = Number(args, "climb", 0f);
            string seeker = Text(args, "seeker") ?? "IR";
            Action<Aircraft, Aircraft> arm = null;
            arm = (t, r) =>
            {
                if (r != victim) return;
                SkywellService.TransferStarted -= arm;
                Threat(bandit, victim, range, seeker, climb);
            };
            SkywellService.TransferStarted += arm;
            return new Dictionary<string, object> { ["ok"] = true };
        }

        /// <summary>The named receiver's own numbers: health, fuel, rounds missing and what the kit has done for it so far.</summary>
        private static Dictionary<string, object> SkywellRxStatus(Dictionary<string, object> args)
        {
            Aircraft rx = Rx(args);
            if (rx == null) return Failure("VanguardAutomation", "rxstatus", "rx must be a registered aircraft id");
            int leaks = 0;
            foreach (FuelTank tank in rx.GetFuelTanks())
                if (LeakRate?.GetValue(tank) is float rate && rate > 0f) leaks++;
            SkywellService.PitTally tally = SkywellService.TallyOf(rx);
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["rxAlive"] = rx.disabled ? 0 : 1,
                ["rxFuel"] = rx.GetFuelLevel(),
                ["rxMissing"] = SkywellService.MissingRounds(rx),
                ["rxLeaks"] = leaks,
                ["rxStops"] = tally.Stops,
                ["rxHandoffs"] = tally.Handoffs,
                ["rxFuelJobs"] = tally.FuelJobs,
                ["rxPriorityFirst"] = tally.PriorityFirst,
                ["rxPitSeconds"] = tally.Tenths / 10f,
                ["rxOrder"] = (int)SkywellService.OrderOf(rx),
            };
        }

        /// <summary>Numeric mirrors of the pit-stop state (the sim checker has no string equality, and dotted counter keys do not resolve).</summary>
        private static void SkywellStatus(Dictionary<string, object> state)
        {
            state["pitStops"] = VanguardStats.Get("skywell.stops");
            state["pitHandoffs"] = VanguardStats.Get("skywell.handoff");
            state["pitFuelJobs"] = VanguardStats.Get("skywell.fuelJob");
            state["pitBreaks"] = VanguardStats.Get("skywell.hotBreak");
            state["pitPriorityFirst"] = VanguardStats.Get("skywell.priorityFirst");
            state["pitSeconds"] = VanguardStats.Get("skywell.pitTenths") / 10f;
            state["kiteReach"] = SkywellVisuals.ProbeReach;
            state["kiteNozzle"] = SkywellVisuals.ProbeNozzle;
            state["kiteReachMax"] = SkywellVisuals.ProbeReachMax;
            state["kiteNozzleMax"] = SkywellVisuals.ProbeNozzleMax;
            state["kiteRoundMin"] = SkywellVisuals.ProbeRoundMin;
            if (tanker != null && SkywellBoard.Views.TryGetValue(tanker.persistentID.Id, out SkywellView view))
            {
                state["skywellPhaseN"] = (int)view.Phase;
                state["pitJobKindN"] = (int)view.Job;
                state["pitJobIndex"] = view.JobIndex;
                state["pitJobCount"] = view.JobCount;
                state["pitFlags"] = view.Flags;
            }
        }
    }
}
