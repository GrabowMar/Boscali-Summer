using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Modules.Skylark
{
    internal enum VectorMode : byte { Auto, Drone, Plane, Manual }

    /// <summary>
    /// Fan tilt mode of the pilot's own aircraft. The native DuctedThrustSystem keeps doing the physics; this only holds it in its
    /// Manual mode and writes the replicated customAxis1 (1 = fans aft/plane, 0 = fans down/drone) on the peer that flies it; forward flight keeps the native pull assist.
    /// DRONE: fans down, auto-hover engages itself above 1.5 m. PLANE: fans aft, hover off. AUTO: tilt scheduled by airspeed.
    /// MANUAL: the pilot's own Custom Axis 1, entered by touching it. Remote peers never write: the axis arrives with the aircraft's inputs.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    internal sealed class SkylarkFlightMode : MonoBehaviour
    {
        private const float AutoSlow = 20f, AutoFast = 60f, AutoRate = .6f, Hysteresis = 5f;
        private const float HoverAlt = 1.5f, HoverMaxSpeed = 30f, PlaneSeedSpeed = 60f;
        // MANUAL entry: the tilt axis must leave its resting value by more than the deadzone for a tenth of a second (pad noise never counts).
        private const float AxisDeadzone = .05f, AxisHold = .1f, AxisDrift = .05f;
        // Self-setting-up gear in AUTO and DRONE: down when slow, low and settling; up once climbing away. PLANE and MANUAL leave it to the pilot.
        private const float GearDownAlt = 10f, GearDownSpeed = 25f, GearUpAlt = 15f, GearSettle = 1f, GearRest = 4f;
        private static readonly string[] Names = { "AUTO", "DRONE", "PLANE", "MANUAL" };
        private static readonly FieldInfo ModeField = AccessTools.Field(typeof(DuctedThrustSystem), "mode");
        private Aircraft aircraft;
        private DuctedThrustSystem propulsion;
        private ControlInputs inputs;
        private object manual, forward;
        private bool seeded, hoverArmed = true, touched;
        private float axis, rest, held, gearWant, gearIdle;

        internal VectorMode Mode { get; private set; }
        internal float Angle => propulsion.angle;
        internal static string Name(VectorMode mode) => Names[(int)mode];

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            propulsion = GetComponent<DuctedThrustSystem>();
            inputs = aircraft.GetInputs();
            manual = Enum.Parse(ModeField.FieldType, "Manual");
            forward = Enum.Parse(ModeField.FieldType, "Forward");
            foreach (LandingGear gear in GetComponentsInChildren<LandingGear>(true)) gear.onGearBreak += Broke;
        }

        // A broken wheel is destroyed by the game and takes the gear state machine with it: say why, once per wheel.
        private void Broke(LandingGear gear)
        {
            Collider contact = SkylarkTemplate.Get<Collider>(gear, "contactCollider");
            UnitPart owner = contact != null && contact.attachedRigidbody != null ? contact.attachedRigidbody.GetComponentInParent<UnitPart>() : null;
            BoscaliSummer.Plugin.Logger?.LogWarning("[Skylark] gear " + gear.name + " broke: contact " + (contact != null ? contact.name + " layer " + contact.gameObject.layer : "none")
                + (owner != null && owner.parentUnit == aircraft ? " (own airframe)" : "") + ", fold " + SkylarkTemplate.Get<float>(gear, "foldAmount").ToString("0.00")
                + ", hinge " + SkylarkTemplate.Get<Transform>(gear, "gearHinge").localEulerAngles.x.ToString("0") + ", alt " + aircraft.radarAlt.ToString("0.0") + ", state " + aircraft.gearState);
        }

        private void Update()
        {
            if (Mode == VectorMode.Manual || !GameManager.IsLocalAircraft(aircraft)) return;
            Rewired.Player player = Rewired.ReInput.players.GetPlayer(0);
            float now = Mathf.Clamp(player.GetAxisRaw("Custom Axis 1"), -1f, 1f);
            // "The pilot is moving the tilt axis": it sits away from where it rested for a tenth of a second (the rest point follows slow
            // drift, so a noisy pad axis or slider never counts), or the modifier chord.
            held = Mathf.Abs(now - rest) > AxisDeadzone ? held + Time.unscaledDeltaTime : 0f;
            rest = Mathf.MoveTowards(rest, now, AxisDrift * Time.unscaledDeltaTime);
            if (held >= AxisHold || player.GetButton("Axis Modifier") && player.GetAxisRaw("Throttle") != 0f) touched = true;
        }

        /// <summary>Owner peer only. Selecting a mode re-arms auto-hover, so DRONE from the air takes the hover straight away.</summary>
        internal void Set(VectorMode mode, bool announce = true)
        {
            if (mode == Mode && seeded) return;
            seeded = true;
            Mode = mode;
            touched = false;
            held = 0f;
            rest = Rewired.ReInput.isReady ? Mathf.Clamp(Rewired.ReInput.players.GetPlayer(0).GetAxisRaw("Custom Axis 1"), -1f, 1f) : 0f;
            hoverArmed = true;
            axis = inputs.customAxis1;
            ModeField.SetValue(propulsion, manual);
            if (announce) SkylarkControl.Say("VECTOR: " + Names[(int)mode]);
        }

        private void FixedUpdate()
        {
            if (aircraft.disabled || !GameManager.IsLocalAircraft(aircraft)) return;
            if (!seeded) Seed();
            if (touched) { Set(VectorMode.Manual); return; }
            if (SkylarkControl.FansHeld)
            {
                // Autoland lands the Skylark the way it lands the native VTOLs: native auto-vectoring sets the fans (held in Manual, its crude
                // axis rule kept them straight down at approach speed) and the autopilot owns the touchdown hover. The mode resumes when it lets go.
                if (ModeField.GetValue(propulsion).Equals(manual)) ModeField.SetValue(propulsion, forward);
                axis = inputs.customAxis1;
                return;
            }
            // Native auto-switching (Forward/Reverse/Hover) would take the fans away from the pilot in every mode, MANUAL included.
            if (!ModeField.GetValue(propulsion).Equals(manual)) ModeField.SetValue(propulsion, manual);
            if (Mode == VectorMode.Manual) return;
            ControlsFilter filter = aircraft.GetControlsFilter();
            float altitude = aircraft.radarAlt;
            if (altitude < .5f) hoverArmed = true;
            if (Mode != VectorMode.Plane) AutoGear(altitude);
            switch (Mode)
            {
                case VectorMode.Plane:
                    inputs.customAxis1 = Pull();
                    if (aircraft.IsAutoHoverEnabled()) filter.SetAutoHover(false);
                    break;
                case VectorMode.Drone:
                    inputs.customAxis1 = 0f;
                    if (hoverArmed && altitude > HoverAlt && aircraft.speed < HoverMaxSpeed && !aircraft.IsAutoHoverEnabled())
                    {
                        filter.SetAutoHover(true);
                        hoverArmed = false;
                    }
                    break;
                default:
                    // Once forward, hold the fans forward 5 m/s longer on the way back, so the tilt does not chatter around the threshold.
                    float shift = axis > .5f ? -Hysteresis : 0f;
                    axis = Mathf.MoveTowards(axis, Mathf.InverseLerp(AutoSlow + shift, AutoFast + shift, aircraft.speed), AutoRate * Time.fixedDeltaTime);
                    inputs.customAxis1 = Mathf.Min(axis, Pull());
                    break;
            }
        }

        // The native forward-flight rule: a hard pull tilts the fans down by up to 45 degrees, which adds lift and pitch authority.
        private float Pull() => Mathf.Min(1f, 1f + inputs.pitch * .5f);

        // AUTO and DRONE own the gear the way they own the fans: down for the landing, up for the climb-out (the gun is safed with the gear out).
        // A wish must hold for a second, and after a move the gear is left alone for a few seconds, so a hover near the thresholds never cycles it.
        private void AutoGear(float altitude)
        {
            if (aircraft.gearState != LandingGear.GearState.LockedExtended && aircraft.gearState != LandingGear.GearState.LockedRetracted) return;
            bool down = aircraft.gearState == LandingGear.GearState.LockedExtended;
            float climb = aircraft.rb.velocity.y;
            float want = !down && altitude < GearDownAlt && aircraft.speed < GearDownSpeed && (climb < -.5f || altitude < 4f) ? 1f
                : down && altitude > GearUpAlt && climb > 1f ? -1f : 0f;
            gearIdle += Time.fixedDeltaTime;
            gearWant = want != 0f && Mathf.Sign(want) == Mathf.Sign(gearWant) ? gearWant + want * Time.fixedDeltaTime : want * Time.fixedDeltaTime;
            if (Mathf.Abs(gearWant) < GearSettle || gearIdle < GearRest) return;
            aircraft.SetGear(gearWant > 0f);
            SkylarkControl.Say(gearWant > 0f ? "GEAR DOWN (AUTO)" : "GEAR UP (AUTO)");
            gearWant = 0f;
            gearIdle = 0f;
        }

        // A ground spawn lifts off like a drone with the fans already down; one that starts fast in the air is a plane.
        // The loadout sets the drive up: SILENT starts on the battery.
        private void Seed()
        {
            bool fast = aircraft.speed > PlaneSeedSpeed && aircraft.radarAlt > HoverAlt;
            Set(fast ? VectorMode.Plane : VectorMode.Drone, false);
            propulsion.angle = fast ? 0f : 90f;
            inputs.customAxis1 = fast ? 1f : 0f;
            if (SkylarkWeapons.LoadoutName(aircraft) == SkylarkWeapons.SilentLoadout) SkylarkNet.Request(aircraft, SkylarkNet.DriveAction, (byte)DriveMode.Ev);
        }
    }
}

namespace BoscaliSummer.Modules.Skylark
{
    // The native wheel breaks when its ground line meets anything while the strut is swung more than 10 degrees. On the Skylark the folding
    // strut's line sweeps through its own airframe's hitboxes, which destroyed the wheel (and froze the gear state) on every retraction.
    // A wheel never breaks against its own aircraft; real ground contact still breaks it.
    [HarmonyLib.HarmonyPatch(typeof(LandingGear), "BreakWheel")]
    internal static class SkylarkGearSelfContactPatch
    {
        private static bool Prefix(LandingGear __instance)
        {
            Aircraft aircraft = SkylarkTemplate.Get<Aircraft>(__instance, "aircraft");
            if (aircraft == null || aircraft.definition == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return true;
            Collider contact = SkylarkTemplate.Get<Collider>(__instance, "contactCollider");
            Rigidbody body = contact != null ? contact.attachedRigidbody : null;
            UnitPart part = body != null ? body.GetComponentInParent<UnitPart>() : null;
            if (part == null || part.parentUnit != aircraft || part.IsDetached()) return true;
            BoscaliSummer.Plugin.Logger?.LogInfo("[Skylark] wheel " + __instance.name + " kept: its ground line touched its own " + contact.name);
            return false;
        }
    }
}
