using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// A receiver holds the service zone on its own. The AI receiver the server is serving (or has called in, or has just sent off to a
    /// break lane) flies to its slot - the zone's hold slot astern of the tow position - through the stick - SkywellHold, the loop the
    /// nomodkit sim flies its own leaders with (the game's AutoAim rings at a tanker's slow speed) - never moved by script, and the sim's
    /// seated jet does the same on request (a scenario cannot press keys). The pilot state writes its inputs
    /// from <c>Pilot_OnAeroInputsApplied</c>, not from a MonoBehaviour step, so the hold is applied by a postfix on that call and goes
    /// last. Only controls are written; no rigidbody, no velocity. An aircraft that is no longer flyable by it (landing, taxiing,
    /// parked) is handed straight back.
    /// </summary>
    internal sealed partial class SkywellVisuals
    {
        /// <summary>One steered aircraft: its hold loop, the load factor measured from its own motion, and the flight-assist setting to give back.</summary>
        private sealed class SteerState
        {
            public readonly SkywellHold Hold = new SkywellHold();
            public Vector3 LastVel, Acc;
            public bool Primed, PrevAssist;
        }

        private static readonly Dictionary<Aircraft, SkywellSlot> Steered = new Dictionary<Aircraft, SkywellSlot>(); // receiver -> the slot it flies to
        private readonly Dictionary<Aircraft, SteerState> holds = new Dictionary<Aircraft, SteerState>();
        private readonly List<Aircraft> stale = new List<Aircraft>();

        private const float StallMargin = 1.1f;    // x the airframe's landing speed (itself ~1.3 x the stall)
        private static float nextTrace;
        internal static string HoldProbe = "none";

        /// <summary>Sim only: the seated local jet flies itself into this tanker's zone (SkywellHold) instead of waiting for a pilot.</summary>
        internal static Aircraft SimHoldTanker;

        private static bool steering; // Steer is running its own FilterInputs

        /// <summary>
        /// Aircraft.FilterInputs prefix: the fly-by-wire filter is stateful (a pitch-rate PID that integrates the stick it returns), and
        /// every pilot state runs it on its own inputs before the hold replaces them. Two passes in one step with two different sticks
        /// cancel in the PID's derivative term and freeze the pitch channel, so a steered aircraft's own pass is skipped and the hold's
        /// pass is the only one of the step.
        /// </summary>
        internal static bool MayFilter(Aircraft a) => steering || Instance == null || !Steered.ContainsKey(a);

        private void FixedUpdate()
        {
            ReinforceRamps();
            Steered.Clear();
            SkywellService.CollectSlots(Steered); // host: the AI receivers the server is serving, has called in, or has sent off to a break lane
            stale.Clear();
            foreach (KeyValuePair<Aircraft, SkywellSlot> pair in Steered)
                if (pair.Key == null || !pair.Key.LocalSim || !SkywellService.AiFlyable(pair.Key)) stale.Add(pair.Key); // landed, taxiing, ... : hands off
            foreach (Aircraft a in stale) Steered.Remove(a);
            if (SimRun && SimHoldTanker != null && !SimHoldTanker.disabled && GameManager.GetLocalAircraft(out Aircraft me) && me != null && me.LocalSim && !me.disabled)
                Steered[me] = new SkywellSlot(SimHoldTanker, SkywellZone.HoldSlot);
            if (holds.Count == 0) return;
            stale.Clear();
            foreach (Aircraft a in holds.Keys)
                if (a == null || a.disabled || !Steered.ContainsKey(a)) stale.Add(a);
            foreach (Aircraft a in stale)
            {
                if (a != null && !a.disabled) a.SetFlightAssist(holds[a].PrevAssist); // its own pilot flies it again, as it was set up
                holds.Remove(a); // a fresh hold (integrators at zero) for the next stop
            }
        }

        /// <summary>Called right after the pilot state wrote this step's inputs (Harmony postfix): a steered receiver's hold overrides them.</summary>
        internal static void SteerAfterPilot(Pilot pilot)
        {
            if (Steered.Count == 0 || Instance == null) return;
            Aircraft rx = pilot.aircraft;
            if (rx == null || rx.pilots == null || rx.pilots.Length == 0 || rx.pilots[0] != pilot || !Steered.TryGetValue(rx, out SkywellSlot slot)) return;
            Instance.Steer(rx, slot);
        }

        private void Steer(Aircraft rx, SkywellSlot target)
        {
            Rigidbody rb = rx.CockpitRB();
            if (rb == null) return;
            if (!holds.TryGetValue(rx, out SteerState st))
            {
                holds[rx] = st = new SteerState { PrevAssist = rx.flightAssist };
                rx.SetFlightAssist(true); // the pitch stick is a g command with it on: the loop below is written for that
            }
            float dt = Time.fixedDeltaTime;
            Vector3 vel = rb.velocity;
            if (st.Primed) st.Acc = Vector3.Lerp(st.Acc, (vel - st.LastVel) / dt, 0.2f);
            st.LastVel = vel;
            st.Primed = true;
            Transform t = rb.transform;
            Vector3 fwd = t.forward, right = t.right, up = t.up;
            Vector3 horizon = Vector3.Cross(Vector3.up, fwd).normalized;
            var state = new SkywellHold.FlightState
            {
                Pos = rx.transform.position,
                Vel = vel,
                BankDeg = Mathf.Atan2(Vector3.Dot(Vector3.Cross(right, horizon), fwd), Vector3.Dot(right, horizon)) * Mathf.Rad2Deg,
                RollRateDeg = -t.InverseTransformDirection(rb.angularVelocity).z * Mathf.Rad2Deg,
                Nz = Vector3.Dot(st.Acc, up) / 9.81f + up.y,
                Dt = dt,
            };
            ControlInputs inputs = rx.GetInputs();
            AircraftParameters parameters = rx.definition != null ? rx.definition.aircraftParameters : null;
            float floor = parameters != null ? parameters.landingSpeed * StallMargin : 0f; // never flown slower than this
            Aircraft tanker = target.Tanker;
            Vector3 slot = target.World;
            SkywellHold.Command cmd = st.Hold.Step(state, slot, SkywellBoard.Velocity(tanker), inputs.throttle, floor);
            inputs.roll = cmd.Roll;
            inputs.pitch = cmd.Pitch;
            inputs.yaw = 0f;
            inputs.throttle = cmd.Throttle;
            inputs.brake = 0f;
            float rawPitch = inputs.pitch, rawRoll = inputs.roll;
            steering = true;
            try { rx.FilterInputs(); }
            finally { steering = false; }
            if (SimRun && Time.time >= nextTrace)
            {
                nextTrace = Time.time + 2f;
                Vector3 e = tanker.transform.InverseTransformDirection(slot - rx.transform.position);
                Vector3 lv = SkywellBoard.Velocity(tanker);
                HoldProbe = $"t={Time.time:F0} {rx.unitName} err={e.ToString("F0")} floor={floor:F0} v={vel.magnitude:F0}/{lv.magnitude:F0}>{cmd.SpeedTarget:F0} vy={vel.y:F1}/{lv.y:F1}>{cmd.VsTarget:F1} " +
                            $"bank={state.BankDeg:F0}>{cmd.BankTarget:F0} nz={state.Nz:F2} thr={cmd.Throttle:F2} stick p={rawPitch:F2}>{inputs.pitch:F2} r={rawRoll:F2}>{inputs.roll:F2} assist={rx.flightAssist} pitch={Mathf.Asin(fwd.y) * Mathf.Rad2Deg:F1} alt={rx.radarAlt:F0}";
                BoscaliSummer.Plugin.Logger?.LogInfo("[SKYWELL hold] " + HoldProbe);
            }
        }
    }
}
