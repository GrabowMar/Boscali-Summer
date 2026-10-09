using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using NuclearOption.Networking;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// SKYWELL pit-stop sim hooks (tests/ingame/vg-skywell.json). Inert outside a nomodkit scenario, like the rest of Step.
    /// A receiver is named with <c>"rx": "&lt;registered id&gt;"</c> (the runner passes the live aircraft as <c>rxUnit</c>), so a
    /// scenario can queue several receivers on one tanker and read each one's own tally afterwards, with no timing guesses.
    /// A hook that names an <c>rx</c> never falls back to the global <c>receiver</c>: only a call without <c>rx</c> (the legacy
    /// skywell.json path) does.
    /// </summary>
    public static partial class VanguardAutomation
    {
        private static Aircraft Rx(Dictionary<string, object> args) => Has(args, "rx") ? Arg(args, "rxUnit") as Aircraft : receiver;

        /// <summary>The seated local aircraft; with an <c>rx</c> named, that must be it.</summary>
        private static Aircraft SeatedRx(Dictionary<string, object> args)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft me) || me == null) return null;
            return Has(args, "rx") && !ReferenceEquals(Arg(args, "rxUnit"), me) ? null : me;
        }

        /// <summary>The first standard loadout that actually carries weapons (a default AI spawn may be bare).</summary>
        private static NuclearOption.SavedMission.Loadout ArmedLoadout(Aircraft a)
        {
            foreach (StandardLoadout sl in a.definition.aircraftParameters.StandardLoadouts ?? Array.Empty<StandardLoadout>())
                if (sl?.loadout?.weapons != null && sl.loadout.weapons.Exists(w => w != null)) return sl.loadout;
            return null;
        }

        /// <summary>
        /// Arm the named receiver; give the weapons a moment to spawn before draining it. A seated player pays for rounds, so
        /// <c>funds</c> tops its allocation up (AI receivers are free).
        /// </summary>
        private static Dictionary<string, object> SkywellArm(Dictionary<string, object> args)
        {
            Aircraft rx = Rx(args);
            if (rx == null) return Failure("VanguardAutomation", "pitarm", "rx must be a registered aircraft id");
            NuclearOption.SavedMission.Loadout armed = ArmedLoadout(rx);
            if (armed != null) rx.Networkloadout = armed;
            Player player = rx.Player;
            float funds = Number(args, "funds", 0f);
            if (player != null && funds > 0f) player.AddAllocation(funds);
            return new Dictionary<string, object> { ["ok"] = true, ["armed"] = armed != null ? 1 : 0, ["allocation"] = player != null ? player.Allocation : -1f };
        }

        /// <summary>
        /// Set the receiver's standing pit order and priority station (order both/fuel/munitions, station n | -1 | "last").
        /// <c>"via": "radial"</c> goes through the SEATED aircraft's radial contract (ISkywellControl: HUD mirror, order intent,
        /// host SetPrefs) instead of straight into the service.
        /// </summary>
        private static Dictionary<string, object> SkywellPit(Dictionary<string, object> args)
        {
            bool radial = Text(args, "via") == "radial";
            Aircraft rx = radial ? SeatedRx(args) : Rx(args);
            if (rx == null) return Failure("VanguardAutomation", "pit", radial ? "via radial needs the seated local aircraft" : "rx must be a registered aircraft id (or equip first)");
            string word = Text(args, "order") ?? "both";
            PitOrder order = word == "fuel" ? PitOrder.Fuel : word == "munitions" ? PitOrder.Munitions : PitOrder.Both;
            int station = (int)Number(args, "station", -1f);
            if (Text(args, "station") == "last") // the highest station that is missing rounds
            {
                station = -1;
                for (int i = rx.weaponStations.Count - 1; i >= 0 && station < 0; i--)
                    if (!SkywellStations.Skip(rx.weaponStations[i]) && rx.weaponStations[i].FullAmmo - rx.weaponStations[i].GetAmmoTotal() > 0) station = i;
            }
            if (radial)
            {
                if (!ModuleServices.TryGet(out ISkywellControl control)) return Failure("VanguardAutomation", "pit", "ISkywellControl is not registered");
                control.SetPitMode((byte)order);
                control.PrioritiseStation(station);
                return new Dictionary<string, object>
                {
                    ["ok"] = true, ["pitOrder"] = (int)SkywellService.OrderOf(rx), ["pitPriority"] = SkywellService.PriorityOf(rx),
                    ["pitAccepted"] = SkywellService.PriorityOf(rx) == station ? 1 : 0, ["pitAvailable"] = control.PitAvailable ? 1 : 0,
                    ["pitMirror"] = (int)control.PitMode,
                };
            }
            bool accepted = SkywellService.SetPrefs(rx, order, station);
            return new Dictionary<string, object> { ["ok"] = true, ["pitOrder"] = (int)order, ["pitPriority"] = station, ["pitAccepted"] = accepted ? 1 : 0 };
        }

        /// <summary>
        /// The SEATED jet flies itself to the box centre and holds it (<c>on</c> 1, default) through the same autopilot hold the AI receivers
        /// use, or lets go (<c>on</c> 0). A scenario cannot press keys or fly the seated jet, so this is the sim's pilot; it writes controls
        /// only, never the airframe's body.
        /// </summary>
        private static Dictionary<string, object> SkywellFly(Dictionary<string, object> args)
        {
            if (SeatedRx(args) == null || tanker == null) return Failure("VanguardAutomation", "seatfly", "needs the seated local aircraft and equip first");
            bool on = Number(args, "on", 1f) > 0f;
            SkywellVisuals.SimHoldTanker = on ? tanker : null;
            return new Dictionary<string, object> { ["ok"] = true, ["flying"] = on ? 1 : 0 };
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

        /// <summary>
        /// The named receiver's own numbers: health, fuel, rounds missing and what the kit has done for it so far. A receiver the
        /// hot-break threat has killed (even destroyed) reports rxAlive 0 and its tally, never a failure: only an id that was
        /// never registered fails the run.
        /// </summary>
        private static Dictionary<string, object> SkywellRxStatus(Dictionary<string, object> args)
        {
            Aircraft rx = Rx(args);
            if (ReferenceEquals(rx, null)) return Failure("VanguardAutomation", "rxstatus", "rx must be a registered aircraft id");
            bool exists = rx != null; // Unity null: false once the wreck is destroyed
            int leaks = 0;
            if (exists)
                foreach (FuelTank tank in rx.GetFuelTanks())
                    if (LeakRate?.GetValue(tank) is float rate && rate > 0f) leaks++;
            SkywellService.PitTally tally = SkywellService.TallyOf(rx);
            int detached = 0, drainedStations = 0, drainedServed = 0;
            if (exists)
                foreach (UnitPart part in rx.partLookup)
                    if (part != null && part.IsDetached()) detached++;
            if (!ReferenceEquals(rx, null) && Drained.TryGetValue(rx, out int mask))
                for (int i = 0; i < 31; i++)
                    if ((mask & (1 << i)) != 0)
                    {
                        drainedStations++;
                        if (tally.PerStation != null && tally.PerStation[i] > 0) drainedServed++;
                    }
            var status = new Dictionary<string, object>
            {
                ["ok"] = true,
                ["rxAlive"] = exists && !rx.disabled ? 1 : 0,
                ["rxFuel"] = exists ? rx.GetFuelLevel() : -1f,
                ["rxMissing"] = exists ? SkywellService.MissingRounds(rx) : -1,
                ["rxLeaks"] = leaks,
                ["rxStops"] = tally.Stops,
                ["rxHandoffs"] = tally.Handoffs,
                ["rxFuelJobs"] = tally.FuelJobs,
                ["rxPriorityFirst"] = tally.PriorityFirst,
                ["rxPitSeconds"] = tally.Tenths / 10f,
                ["rxOrder"] = (int)SkywellService.OrderOf(rx),
                ["rxDetached"] = detached,
                ["rxDrainedStations"] = drainedStations,
                ["rxDrainedServed"] = drainedServed,
                ["rxDrainedMissed"] = drainedStations - drainedServed,
            };
            for (int i = 0; i < 8; i++) status["rxStn" + i] = tally.PerStation != null ? tally.PerStation[i] : 0;
            return status;
        }

        /// <summary>Numeric mirrors of the pit-stop state (the sim checker has no string equality, and dotted counter keys do not resolve).</summary>
        private static void SkywellStatus(Dictionary<string, object> state)
        {
            state["pitStops"] = VanguardStats.Get("skywell.stops");
            state["pitHandoffs"] = VanguardStats.Get("skywell.handoff");
            state["pitFuelJobs"] = VanguardStats.Get("skywell.fuelJob");
            state["pitBreaks"] = VanguardStats.Get("skywell.hotBreak");
            state["pitBoxLost"] = VanguardStats.Get("skywell.boxLost");
            state["pitPriorityFirst"] = VanguardStats.Get("skywell.priorityFirst");
            state["pitSeconds"] = VanguardStats.Get("skywell.pitTenths") / 10f;
            // What the last hot-break victim held when it was released (the victim itself may be dead by the time anyone asks).
            state["pitBreakHandoffs"] = VanguardStats.Get("skywell.breakHandoffs");
            state["pitBreakFuelJobs"] = VanguardStats.Get("skywell.breakFuelJobs");
            state["pitBreakJobIndex"] = VanguardStats.Get("skywell.breakJobIndex");
            state["kiteReach"] = SkywellVisuals.ProbeReach;
            state["kiteNozzle"] = SkywellVisuals.ProbeNozzle;
            state["kiteReachMax"] = SkywellVisuals.ProbeReachMax;
            state["kiteNozzleMax"] = SkywellVisuals.ProbeNozzleMax;
            state["kiteRoundMin"] = SkywellVisuals.ProbeRoundMin;
            // Glider-to-airframe clearance while a stop is in its job phase (box distance to the receiver's bounds): it must sit under the belly.
            state["serveGapMax"] = SkywellVisuals.ServeGapMax;
            state["serveGapMin"] = SkywellVisuals.ServeGapMin;
            state["serveGapSamples"] = SkywellVisuals.ServeGapSamples;
            if (tanker != null && SkywellBoard.Views.TryGetValue(tanker.persistentID.Id, out SkywellView view))
            {
                state["skywellPhaseN"] = (int)view.Phase;
                state["pitJobKindN"] = (int)view.Job;
                state["pitJobIndex"] = view.JobIndex;
                state["pitJobCount"] = view.JobCount;
                state["pitFlags"] = view.Flags;
                state["pitOrder"] = (int)view.Order;
            }
        }
    }
}
