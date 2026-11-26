using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Modules.Skylark
{
    internal enum VectorMode : byte { Drone, Plane }

    /// <summary>
    /// Drone mode flies a velocity command through bank, pitch and fan collective. Plane mode flies the native airframe
    /// with short-field lift and pull-up vectoring. The owner commands the fan angle through the replicated customAxis1.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    internal sealed class SkylarkFlightMode : MonoBehaviour
    {
        private const float HoverAlt = 1.5f, PlaneSeedSpeed = 60f;
        private const float DroneMaxTilt = 39f, DroneForwardSpeed = 34f, DroneReverseSpeed = 16f, DroneSideSpeed = 23f;
        private const float DroneAttitudeP = 13f, DroneAttitudeD = 5.4f, DroneMaxTiltAccel = 14f;
        private const float DroneYawRate = 1.85f, DroneYawGain = 5.5f, DroneMaxYawAccel = 9f;
        private const float DroneStickDeadzone = .055f, DroneThrottleDeadband = .045f;
        private const float DroneClimbSpeed = 15f, DroneDescentSpeed = 10f, DroneVertGain = 2.2f, DroneHorizGain = 1.35f;
        private const float DroneHoverReserve = .96f, DroneMinHorizontalAccel = 1.5f;
        // Self-setting-up gear in DRONE: down when slow, low and settling; up once climbing away. PLANE leaves it to the pilot.
        private const float GearDownAlt = 10f, GearDownSpeed = 25f, GearUpAlt = 15f, GearSettle = 1f, GearRest = 4f;
        private static readonly string[] Names = { "DRONE", "PLANE" };
        private static readonly FieldInfo ModeField = AccessTools.Field(typeof(DuctedThrustSystem), "mode");
        private static readonly FieldInfo ThrottleField = AccessTools.Field(typeof(PilotPlayerState), "simulatedThrottle");
        private static readonly FieldInfo RadarHitField = AccessTools.Field(typeof(Unit), "hit");
        private Aircraft aircraft;
        private DuctedThrustSystem propulsion;
        private SkylarkDrive drive;
        private Turbofan engine;
        private AnimationCurve availableThrust;
        private ControlInputs inputs;
        private PilotBaseState commandSource;
        private Vector3 stick, fanMix;
        private Vector2 attitudeTrim;
        private object manual, forward;
        private bool seeded;
        private float pilotThrottle, collectiveTrim, mixAuthority, collectiveCommand, gearWant, gearIdle;
#if !PUBLIC_RELEASE
        private float? simAngle;
#endif

        internal VectorMode Mode { get; private set; }
        internal float Angle => propulsion.angle;
        internal float CommandedAngle { get; private set; }
        internal static string Name(VectorMode mode) => Names[(int)mode];

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            propulsion = GetComponent<DuctedThrustSystem>();
            drive = GetComponent<SkylarkDrive>();
            engine = GetComponentInChildren<Turbofan>();
            availableThrust = SkylarkTemplate.Get<AnimationCurve>(propulsion, "thrustAtAirspeed");
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

        /// <summary>Owner peer only. Switches between DRONE (FPV UAV) and PLANE.</summary>
        internal void Set(VectorMode mode, bool announce = true)
        {
#if !PUBLIC_RELEASE
            simAngle = null;
#endif
            if (mode == Mode && seeded) return;
            seeded = true;
            Mode = mode;
            collectiveTrim = 0f;
            attitudeTrim = Vector2.zero;
            mixAuthority = 0f;
            gearWant = 0f;
            ModeField.SetValue(propulsion, manual);
            if (announce) SkylarkControl.Say("MODE: " + Names[(int)mode]);
        }

#if !PUBLIC_RELEASE
        /// <summary>Sim hook only: holds a fixed fan angle for automated wind-tunnel / transition checks.</summary>
        internal void SetSimAngle(float degrees)
        {
            seeded = true;
            simAngle = Mathf.Clamp(degrees, 0f, 90f);
            ModeField.SetValue(propulsion, manual);
        }
#endif

        // The direct control law reads the pilot's commands before native trim and damping. Read the player's throttle setting
        // because the previous physics tick may have left a managed collective in the replicated input slot.
        internal void CaptureCommand(Vector3 rawAxes)
        {
            if (aircraft.pilots.Length == 0) return;
            PilotBaseState source = aircraft.pilots[0].currentState;
            if (!(source is PilotPlayerState)
#if !PUBLIC_RELEASE
                && !(source is SkylarkHoverHold)
#endif
                ) return;

            commandSource = source;
            stick = new Vector3(Shape(rawAxes.z, DroneStickDeadzone), Shape(rawAxes.y, DroneStickDeadzone), Shape(rawAxes.x, DroneStickDeadzone));
            float throttle = inputs.throttle;
            if (source is PilotPlayerState player && ThrottleField != null)
            {
                throttle = (float)ThrottleField.GetValue(player);
                if (PlayerSettings.throttleUseNegative) throttle = .5f * (throttle + 1f);
                if (aircraft.GetAircraftParameters().takeoffDistance == 0f && PlayerSettings.invertCollective) throttle = 1f - throttle;
            }
            pilotThrottle = Mathf.Clamp01(throttle);
        }

        private void FixedUpdate()
        {
            mixAuthority = 0f;
            if (aircraft.disabled || !GameManager.IsLocalAircraft(aircraft)) return;
            if (!seeded) Seed();
#if !PUBLIC_RELEASE
            if (simAngle.HasValue)
            {
                if (!ModeField.GetValue(propulsion).Equals(manual)) ModeField.SetValue(propulsion, manual);
                CommandAngle(simAngle.Value);
                return;
            }
#endif
            if (SkylarkControl.FansHeld)
            {
                // Autoland lands the Skylark the way it lands the native VTOLs: native auto-vectoring sets the fans and the autopilot owns the touchdown hover.
                if (ModeField.GetValue(propulsion).Equals(manual)) ModeField.SetValue(propulsion, forward);
                return;
            }
            // Keep native DuctedThrustSystem in Manual so it never overrides our DRONE / PLANE fan angle.
            if (!ModeField.GetValue(propulsion).Equals(manual)) ModeField.SetValue(propulsion, manual);
            ControlsFilter filter = aircraft.GetControlsFilter();
            float altitude = aircraft.radarAlt;
            if (Mode == VectorMode.Plane)
            {
                CommandAngle(PlaneAngle());
                if (aircraft.IsAutoHoverEnabled()) filter.SetAutoHover(false);
                return;
            }
            Rigidbody body = aircraft.CockpitRB();
            if (body == null)
            {
                CommandAngle(90f);
                return;
            }
            Vector3 relativeVelocity = body.velocity - SurfaceVelocity(altitude);
            AutoGear(altitude, relativeVelocity.y);
            if (aircraft.IsAutoHoverEnabled() || !aircraft.LocalSim || commandSource == null || aircraft.pilots[0].currentState != commandSource)
            {
                collectiveTrim = 0f;
                attitudeTrim = Vector2.zero;
                CommandAngle(90f);
                return;
            }
            ApplyDroneFlight(body, relativeVelocity, altitude);
        }

        private void ApplyDroneFlight(Rigidbody body, Vector3 relativeVelocity, float altitude)
        {
            if (!aircraft.Ignition || drive.Dry && drive.Charge <= 0f)
            {
                CommandAngle(90f);
                collectiveTrim = 0f;
                attitudeTrim = Vector2.zero;
                return;
            }
            bool airborne = altitude > .35f;
            Vector3 up = transform.up;
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (heading.sqrMagnitude < .001f) heading = Vector3.Cross(transform.right, Vector3.up);
            heading.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float g = Physics.gravity.magnitude;
            float mass = Mathf.Max(1f, aircraft.GetMass());
            float nominalThrust = availableThrust.Evaluate(aircraft.speed);
            float driveScale = drive.FanThrustScale;

            Vector3 desiredVelocity = Vector3.ClampMagnitude(new Vector3(stick.x, 0f, stick.z), 1f);
            desiredVelocity = heading * (desiredVelocity.z * (desiredVelocity.z >= 0f ? DroneForwardSpeed : DroneReverseSpeed))
                + right * (desiredVelocity.x * DroneSideSpeed);
            Vector3 planarError = desiredVelocity - Vector3.ProjectOnPlane(relativeVelocity, Vector3.up);
            float maxTilt = Mathf.Lerp(12f, DroneMaxTilt, Mathf.InverseLerp(.35f, 5f, altitude));
            // Reserve lift for altitude while braking. A short boost cannot be counted on throughout the maneuver.
            float reservedAccel = nominalThrust * Mathf.Min(1f, driveScale) * DroneHoverReserve / mass;
            float horizontalBudget = Mathf.Sqrt(Mathf.Max(0f, reservedAccel * reservedAccel - g * g));
            float maxHorizontalAccel = Mathf.Min(Mathf.Tan(maxTilt * Mathf.Deg2Rad) * g, Mathf.Max(DroneMinHorizontalAccel, horizontalBudget));
            Vector3 horizontalAccel = Vector3.ClampMagnitude(planarError * DroneHorizGain, maxHorizontalAccel);
            Vector3 desiredThrust = (Vector3.up * g + horizontalAccel).normalized;

            // Swing the fans into a commanded translation while limiting the attitude change needed for steady flight.
            float forwardAccel = airborne ? Vector3.Dot(heading, horizontalAccel) : 0f;
            float vector = Mathf.Clamp(forwardAccel * 1.7f + Mathf.Abs(stick.y) * 4f, 0f, 15f);
            CommandAngle(90f - vector);
            Vector3 desiredUp = Quaternion.AngleAxis(-vector, right) * desiredThrust;
            float authority = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 74f, propulsion.angle));
            if (authority <= 0f) return;

            float currentYaw = Vector3.Dot(body.angularVelocity, up);
            fanMix = new Vector3(0f, stick.y, 0f);
            if (airborne && engine != null)
            {
                Vector3 tiltOmega = body.angularVelocity - up * currentYaw;
                Vector3 tiltError = Vector3.Cross(up, desiredUp);
                // The cross product vanishes upside down; select a recovery axis through the roll limit.
                if (Vector3.Dot(up, desiredUp) < 0f && tiltError.sqrMagnitude < .05f) tiltError = transform.forward;
                Vector3 tiltAccel = Vector3.ClampMagnitude(tiltError * DroneAttitudeP - tiltOmega * DroneAttitudeD, DroneMaxTiltAccel);
                float yawAccel = Mathf.Clamp((stick.y * DroneYawRate - currentYaw) * DroneYawGain, -DroneMaxYawAccel, DroneMaxYawAccel);
                float thrustAuthority = Mathf.InverseLerp(.06f, .55f, engine.GetThrustRatio());
                // The same inputs move the native control surfaces. As they gain authority, command less differential.
                float differential = Mathf.Lerp(.14f, .07f, Mathf.InverseLerp(8f, 36f, aircraft.speed));
                // Sustained airflow can oppose the requested hover attitude even with zero angular velocity.
                // Let a bounded trim carry that load, then let it fade as the aircraft slows toward a hover.
                float trimAuthority = Mathf.InverseLerp(10f, 30f, aircraft.speed) * thrustAuthority;
                Vector2 trimError = new Vector2(Vector3.Dot(tiltError, transform.right), -Vector3.Dot(tiltError, transform.forward));
                attitudeTrim = Vector2.ClampMagnitude(Vector2.Lerp(attitudeTrim, trimError * 1.2f, .65f * Time.fixedDeltaTime), .32f);
                fanMix.x = Mathf.Clamp(Vector3.Dot(tiltAccel, transform.right) * differential + attitudeTrim.x * trimAuthority, -1f, 1f);
                fanMix.y = Mathf.Clamp(yawAccel * .16f, -1f, 1f);
                fanMix.z = Mathf.Clamp(-Vector3.Dot(tiltAccel, transform.forward) * differential + attitudeTrim.y * trimAuthority, -1f, 1f);
                body.AddTorque((tiltAccel + up * yawAccel) * (.28f * authority * thrustAuthority), ForceMode.Acceleration);
            }
            else attitudeTrim = Vector2.zero;

            // Command the native collective from vertical speed. The fans can saturate, spool down, or run out of power.
            float descent = Mathf.Lerp(.85f, DroneDescentSpeed, Mathf.InverseLerp(.4f, 19f, altitude));
            float climbStick = Shape((pilotThrottle - .5f) * 2f, DroneThrottleDeadband * 2f);
            float targetVertical = climbStick * (climbStick >= 0f ? DroneClimbSpeed : descent);
            float verticalError = targetVertical - relativeVelocity.y;
            float nozzleTilt = (90f - propulsion.angle) * Mathf.Deg2Rad;
            float verticalProjection = Mathf.Max(.45f, up.y * Mathf.Cos(nozzleTilt) + transform.forward.y * Mathf.Sin(nozzleTilt));
            float accelToCollective = mass / Mathf.Max(1f, nominalThrust * driveScale * verticalProjection);
            float demand = (g + Mathf.Clamp(verticalError * DroneVertGain, -8f, 6f)) * accelToCollective;
            if (airborne && demand + collectiveTrim > 0f && demand + collectiveTrim < 1f)
                collectiveTrim = Mathf.Clamp(collectiveTrim + verticalError * .055f * Time.fixedDeltaTime, -.2f, .2f);
            float collective = Mathf.Clamp01(demand + collectiveTrim);
            if (!airborne && climbStick <= 0f) collective = pilotThrottle * Mathf.Min(.65f, g * accelToCollective);
            mixAuthority = authority;
            collectiveCommand = Mathf.Lerp(pilotThrottle, collective, authority);
            ApplyDroneMix();
        }

        // The native input filter may run before or after this component, depending on the simulation step.
        internal void ApplyDroneMix()
        {
            if (mixAuthority <= 0f || aircraft.disabled || SkylarkControl.FansHeld || aircraft.IsAutoHoverEnabled()
                || aircraft.pilots[0].currentState != commandSource) return;
            inputs.pitch = Mathf.Lerp(inputs.pitch, fanMix.x, mixAuthority);
            inputs.yaw = Mathf.Lerp(inputs.yaw, fanMix.y, mixAuthority);
            inputs.roll = Mathf.Lerp(inputs.roll, fanMix.z, mixAuthority);
            inputs.throttle = collectiveCommand;
        }

        private Vector3 SurfaceVelocity(float altitude)
        {
            if (altitude > 18f || RadarHitField == null) return Vector3.zero;
            var hit = (RaycastHit)RadarHitField.GetValue(aircraft);
            Rigidbody surface = hit.rigidbody;
            return surface != null ? surface.GetPointVelocity(hit.point) * (1f - Mathf.InverseLerp(8f, 18f, altitude)) : Vector3.zero;
        }

        private static float Shape(float input, float deadzone)
        {
            float amount = Mathf.InverseLerp(deadzone, 1f, Mathf.Abs(input));
            return Mathf.Sign(input) * amount * Mathf.Lerp(.32f, 1f, amount);
        }

        private void CommandAngle(float degrees)
        {
            CommandedAngle = degrees;
            inputs.customAxis1 = 1f - degrees / 90f;
        }

        // The wings take over with airspeed. With gear down the fans help short rolling takeoffs and approaches.
        private float PlaneAngle()
        {
            float wingLift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 103f, aircraft.speed));
            float nosedown = -Mathf.Asin(Mathf.Clamp(transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float transitionAngle = Mathf.Clamp((aircraft.gearDeployed ? 71f : 68f) + nosedown, 45f, 82f);
            float lowSpeedAngle = transitionAngle * (1f - wingLift);
            if (aircraft.radarAlt < .35f) lowSpeedAngle *= Mathf.InverseLerp(.45f, .9f, inputs.throttle);
            float pull = Mathf.Clamp01(-inputs.pitch);
            // Normal trim stays wing-borne. A deliberate full pull can still call on the full vector assist.
            float pullAssist = pull * Mathf.Lerp(30f, 45f, Mathf.InverseLerp(.75f, 1f, pull));
            return Mathf.Max(lowSpeedAngle, pullAssist);
        }

        // DRONE owns the gear: down for the landing, up for the climb-out (the gun is safed with the gear out).
        // A wish must hold for a second, and after a move the gear is left alone for a few seconds, so a hover near the thresholds never cycles it.
        private void AutoGear(float altitude, float climb)
        {
            if (aircraft.gearState != LandingGear.GearState.LockedExtended && aircraft.gearState != LandingGear.GearState.LockedRetracted) return;
            bool down = aircraft.gearState == LandingGear.GearState.LockedExtended;
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
            CommandAngle(propulsion.angle);
            if (SkylarkWeapons.LoadoutName(aircraft) == SkylarkWeapons.SilentLoadout) SkylarkNet.Request(aircraft, SkylarkNet.DriveAction, (byte)DriveMode.Ev);
        }
    }

    [HarmonyPatch]
    internal static class SkylarkDroneCommandPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ControlsFilter), "Filter");
        private static bool Prepare() => TargetMethod() != null && AccessTools.Field(typeof(ControlsFilter), "aircraft")?.FieldType == typeof(Aircraft);

        private static void Prefix(Aircraft ___aircraft, Vector3 __1, out SkylarkFlightMode __state)
        {
            __state = null;
            try
            {
                if (___aircraft != null && ___aircraft.definition != null && ___aircraft.definition.jsonKey == SkylarkTemplate.Key
                    && GameManager.IsLocalAircraft(___aircraft) && ___aircraft.TryGetComponent(out __state)) __state.CaptureCommand(__1);
            }
            catch (Exception error) { PatchGuard.Report(nameof(SkylarkDroneCommandPatch), error); }
        }

        private static void Postfix(SkylarkFlightMode __state)
        {
            try { if (__state != null) __state.ApplyDroneMix(); }
            catch (Exception error) { PatchGuard.Report(nameof(SkylarkDroneCommandPatch), error); }
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
