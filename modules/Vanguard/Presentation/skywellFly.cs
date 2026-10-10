using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// A receiver holds the box on its own. The AI receiver the server is serving (or has called in) flies to the box centre through
    /// the game's autopilot target - never moved by script - and the sim's seated jet does the same on request (a scenario cannot press
    /// keys). The pilot state writes its inputs from <c>Pilot_OnAeroInputsApplied</c>, not from a MonoBehaviour step, so the hold is
    /// applied by a postfix on that call (SkywellHoldPatch) and goes last. Only controls are written; no rigidbody, no velocity.
    /// </summary>
    internal sealed partial class SkywellVisuals
    {
        private static readonly Dictionary<Aircraft, Aircraft> Steered = new Dictionary<Aircraft, Aircraft>(); // receiver -> its tanker
        private readonly Dictionary<Aircraft, SkywellHold> holds = new Dictionary<Aircraft, SkywellHold>();
        private readonly List<Aircraft> stale = new List<Aircraft>();

        /// <summary>Sim only: the seated local jet flies itself into this tanker's box (SkywellHold) instead of waiting for a pilot.</summary>
        internal static Aircraft SimHoldTanker;

        private void FixedUpdate()
        {
            ReinforceRamps();
            Steered.Clear();
            foreach (SkywellView v in SkywellBoard.Views.Values)
            {
                if (v.Receiver == 0 || v.Phase == SkywellPhase.Idle) continue;
                Aircraft rx = Resolve(v.Receiver), tanker = Resolve(v.Tanker);
                if (rx != null && tanker != null && rx.Player == null && rx.LocalSim && !rx.disabled && !tanker.disabled) Steered[rx] = tanker;
            }
            if (SimRun && SimHoldTanker != null && !SimHoldTanker.disabled && GameManager.GetLocalAircraft(out Aircraft me) && me != null && me.LocalSim && !me.disabled)
                Steered[me] = SimHoldTanker;
            if (holds.Count == 0) return;
            stale.Clear();
            foreach (Aircraft a in holds.Keys)
                if (a == null || a.disabled || !Steered.ContainsKey(a)) stale.Add(a);
            foreach (Aircraft a in stale) holds.Remove(a); // a fresh hold (integrator at zero) for the next stop
        }

        /// <summary>Called right after the pilot state wrote this step's inputs (Harmony postfix): a steered receiver's hold overrides them.</summary>
        internal static void SteerAfterPilot(Pilot pilot)
        {
            if (Steered.Count == 0 || Instance == null) return;
            Aircraft rx = pilot.aircraft;
            if (rx == null || rx.pilots == null || rx.pilots.Length == 0 || rx.pilots[0] != pilot || !Steered.TryGetValue(rx, out Aircraft tanker)) return;
            Instance.Steer(rx, tanker);
        }

        private void Steer(Aircraft rx, Aircraft tanker)
        {
            if (rx.rb == null || !(rx.autopilot is AutopilotPlane)) return;
            if (!holds.TryGetValue(rx, out SkywellHold hold)) holds[rx] = hold = new SkywellHold();
            Vector3 slot = SkywellBoard.Anchor(tanker) + tanker.transform.TransformDirection(SkywellBox.Centre);
            SkywellHold.Command cmd = hold.Step(rx.transform.position, rx.rb.velocity, slot, SkywellBoard.Velocity(tanker), Time.fixedDeltaTime);
            ControlInputs inputs = rx.GetInputs();
            inputs.throttle = cmd.Throttle;
            inputs.brake = 0f;
            // effort > 1 restores full turn authority; bankAllowed is the real lever and follows the turn actually asked for.
            rx.autopilot.AutoAim(cmd.Aim.ToGlobalPosition(), false, false, false, 1.01f, cmd.Bank, false, 0f, Vector3.zero);
        }
    }
}
