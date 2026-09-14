using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Autopilot.Configuration;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    /// <summary>Flies the local player's own aircraft to a landing with the game's native
    /// Autopilot (AutoAim/Hover), mirroring the native AI landing states at the input layer.
    /// Client-local and reversible: manual stick input or the radial action releases control
    /// and restores the flight-assist and auto-hover state the takeover changed.</summary>
    internal sealed class AutopilotLandController : MonoBehaviour, ISceneService
    {
        private enum Phase
        {
            None,
            Join,
            Final,
            Approach,
            HoverTouchdown,
            Rollout,
            PadApproach,
            PadDescent
        }

        private const float RunwayRecheckSeconds = 2f;
        private const float AlignmentHoldSeconds = 1f;

        public static AutopilotLandController Instance { get; private set; }

        private AutopilotSettings settings;
        private Aircraft aircraft;
        private AircraftParameters parameters;
        private Airbase airbase;
        private Airbase.Runway.RunwayUsage runwayUsage;
        private Airbase.VerticalLandingPoint pad;
        private Phase phase;
        private float adjustedLandingSpeed;
        private float alignedTime;
        private float touchdownTime;
        private float hoverTargetHeight;
        private float timeOnGround;
        private float lastRunwayCheck;
        private bool reachedFinal;
        private bool padRegistered;
        private bool inputSettled;
        private bool storedFlightAssist;
        private bool storedAutoHover;
        private bool padLanding;
        private bool failureReported;
        private Vector3 glideslopeCorrection;

        public bool IsEngaged => phase != Phase.None;

        private void Awake() => Instance = this;
        private void Update() => BoscaliRadialMenu.Tick();
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Configure(AutopilotSettings config) => settings = config;

        public bool IsEngagedOn(Aircraft candidate) => IsEngaged && candidate != null && candidate == aircraft;

        public bool CanEngage(Aircraft candidate)
        {
            if (settings != null && !settings.Enabled.Value) return false;
            if (IsEngaged || candidate == null || !GameManager.IsLocalAircraft(candidate)) return false;
            if (candidate.disabled || candidate.HasEjected() || candidate.IsLanded()) return false;
            if (candidate.cockpit == null || candidate.cockpit.IsDetached() || candidate.autopilot == null) return false;
            if (candidate.NetworkHQ == null || candidate.definition == null) return false;
            if (!GameManager.flightControlsEnabled || GameplayUI.GameIsPaused) return false;
            return true;
        }

        public void Toggle()
        {
            if (IsEngaged)
            {
                Disengage("Autopilot: cancelled");
                return;
            }
            Engage();
        }

        /// <summary>Runs from the player fixed-step prefix. Returning true suppresses the native
        /// player input pass for this step so the native autopilot has sole axis authority.</summary>
        public bool TakeOver(Pilot pilot)
        {
            if (!IsEngaged) return false;
            Aircraft local = pilot != null ? pilot.aircraft : null;
            if (local == null || local != aircraft || !CanContinue(local))
            {
                Disengage("Autopilot: released");
                return false;
            }
            if (!inputSettled)
            {
                if (!ManualInput()) inputSettled = true;
            }
            else if (ManualInput())
            {
                Disengage("Autopilot: manual input");
                return false;
            }

            try
            {
                Flight();
            }
            catch (Exception e)
            {
                if (!failureReported)
                {
                    failureReported = true;
                    Plugin.Logger?.LogError("Autopilot landing failed: " + e);
                }
                Disengage("Autopilot: released");
                return false;
            }
            return IsEngaged;
        }

        private void Engage()
        {
            Aircraft candidate;
            GameManager.GetLocalAircraft(out candidate);
            if (!CanEngage(candidate))
            {
                Report("Autopilot: unavailable");
                return;
            }

            aircraft = candidate;
            parameters = aircraft.GetAircraftParameters();
            padLanding = !(aircraft.autopilot is AutopilotPlane);
            adjustedLandingSpeed = AutopilotLandPolicy.AdjustedLandingSpeed(
                aircraft.GetMass(), aircraft.definition.aircraftInfo.maxWeight, parameters.landingSpeed);
            pad = null;
            airbase = null;

            if (padLanding)
            {
                var padQuery = new RunwayQuery
                {
                    RunwayType = RunwayQueryType.Vertical,
                    MinSize = aircraft.maxRadius
                };
                if (!aircraft.NetworkHQ.TryGetNearestAirbase(aircraft.transform.position, out airbase, padQuery) ||
                    !airbase.TryRequestVerticalLanding(aircraft, padQuery, out pad))
                {
                    Fail("no landing pad available");
                    return;
                }
            }
            else
            {
                var runwayQuery = new RunwayQuery
                {
                    RunwayType = RunwayQueryType.Landing,
                    MinSize = parameters.verticalLanding ? aircraft.definition.length : parameters.takeoffDistance,
                    LandingSpeed = parameters.verticalLanding ? 0f : adjustedLandingSpeed,
                    TailHook = aircraft.weaponManager != null && aircraft.weaponManager.HasTailHook()
                };
                airbase = aircraft.NetworkHQ.GetNearestAirbase(aircraft.transform.position, runwayQuery);
                Airbase.Runway.RunwayUsage? usage =
                    airbase != null ? airbase.RequestLanding(aircraft, runwayQuery) : null;
                if (usage == null)
                {
                    Fail("no landing runway available");
                    return;
                }
                runwayUsage = usage.Value;
            }

            storedFlightAssist = aircraft.flightAssist;
            ControlsFilter filter = aircraft.GetControlsFilter();
            storedAutoHover = filter != null && filter.IsAutoHoverEnabled();
            aircraft.SetFlightAssist(true);
            if (!padLanding && filter != null) filter.SetAutoHover(false);

            hoverTargetHeight = padLanding ? 45f : 10f;
            phase = padLanding ? Phase.PadApproach : Phase.Join;
            inputSettled = !ManualInput();
            failureReported = false;
            Report(padLanding ? "Autopilot: landing at " + airbase.name : "Autopilot: " + runwayUsage.GetName());
        }

        private void Fail(string reason)
        {
            Report("Autopilot: " + reason);
            ResetState();
        }

        private void Disengage(string message)
        {
            Aircraft local = aircraft;
            bool landed = local != null && local.IsLanded();
            ControlsFilter filter = local != null ? local.GetControlsFilter() : null;
            if (filter != null)
            {
                if (local.flightAssist != storedFlightAssist) local.SetFlightAssist(storedFlightAssist);
                if (!landed) filter.SetAutoHover(storedAutoHover);
            }
            DeregisterPad(local);
            if (message != null) Report(message);
            ResetState();
        }

        private void DeregisterPad(Aircraft local)
        {
            if (pad == null || local == null || !padRegistered) return;
            padRegistered = false;
            Queue<Aircraft> queue = pad.GetLandingQueue();
            int count = queue.Count;
            for (int i = 0; i < count; i++)
            {
                Aircraft queued = queue.Dequeue();
                if (queued != null && queued != local) queue.Enqueue(queued);
            }
        }

        private static void Report(string message)
        {
            AircraftActionsReport report = SceneSingleton<AircraftActionsReport>.i;
            if (report != null) report.ReportText(message, 5f);
        }

        private bool CanContinue(Aircraft local) =>
            !local.disabled && !local.HasEjected() && local.cockpit != null && !local.cockpit.IsDetached() &&
            local.NetworkHQ != null && local.autopilot != null &&
            GameManager.flightControlsEnabled && !GameplayUI.GameIsPaused;

        private static bool ManualInput()
        {
            Rewired.Player player = GameManager.playerInput;
            if (player != null && AutopilotLandPolicy.ManualAxisOverride(
                    player.GetAxis("Pitch"), player.GetAxis("Roll"), player.GetAxis("Yaw"),
                    AutopilotLandPolicy.ManualAxisThreshold)) return true;
            if (PlayerSettings.virtualJoystickEnabled)
            {
                FlightHud hud = SceneSingleton<FlightHud>.i;
                if (hud != null && hud.virtualJoystickPos != null &&
                    AutopilotLandPolicy.VirtualJoystickOverride(
                        hud.virtualJoystickPos.transform.localPosition.magnitude,
                        AutopilotLandPolicy.ManualAxisThreshold)) return true;
            }
            return false;
        }

        private void Flight()
        {
            switch (phase)
            {
                case Phase.Join: FlyJoin(); break;
                case Phase.Final: FlyFinal(); break;
                case Phase.Approach: FlyApproach(); break;
                case Phase.HoverTouchdown: FlyHoverTouchdown(); break;
                case Phase.Rollout: Rollout(); break;
                case Phase.PadApproach: FlyToPad(); break;
                case Phase.PadDescent: DescendToPad(); break;
            }
        }

        private void FlyJoin()
        {
            if (!RunwayStillUsable()) return;
            Vector3 direction = runwayUsage.GetDirection().normalized;
            GlobalPosition aim = runwayUsage.GetGlideslopeAimpoint(aircraft, parameters.turningRadius * 3f, 30f)
                .ToGlobalPosition();
            Vector3 toAim = aim - aircraft.GlobalPosition();
            float offset = (Mathf.Sin((Vector3.Angle(toAim, direction) - 90f) * Mathf.Deg2Rad) + 1f) *
                parameters.turningRadius * 2f;
            aim += Vector3.RotateTowards(-direction * offset, -toAim, Mathf.PI * 0.5f, 0f);

            float target = parameters.cornerSpeed + FastMath.Distance(aircraft.GlobalPosition(), aim) * 0.02f;
            ControlInputs inputs = aircraft.GetInputs();
            inputs.throttle = AutopilotLandPolicy.ApproachThrottle(aircraft.speed, target, parameters.cruiseThrottle);
            aircraft.autopilot.AutoAim(aim, false, false, false, 0.9f, 135f, true,
                parameters.turningRadius * 3f * 0.05f, Vector3.zero);

            if (FastMath.InRange(aim, aircraft.GlobalPosition(), parameters.turningRadius) &&
                Vector3.Dot(aircraft.transform.forward, toAim) < 0f)
                phase = Phase.Final;
        }

        private void FlyFinal()
        {
            if (!RunwayStillUsable()) return;
            GlobalPosition touchdown = runwayUsage.GetTouchdownPoint();
            float distance = FastMath.Distance(touchdown, aircraft.cockpit.xform.GlobalPosition());
            GlobalPosition aim = runwayUsage.GetGlideslopeAimpoint(aircraft, parameters.turningRadius * 3f, 30f)
                .ToGlobalPosition();
            if (FastMath.InRange(aim, aircraft.GlobalPosition(), parameters.turningRadius * 0.5f)) reachedFinal = true;
            if (reachedFinal) aim = touchdown + distance * 0.05f * Vector3.up;

            float target = adjustedLandingSpeed + 0.015f * Mathf.Max(distance - 500f, 0f);
            ControlInputs inputs = aircraft.GetInputs();
            inputs.throttle = AutopilotLandPolicy.ApproachThrottle(aircraft.speed, target, parameters.cruiseThrottle);
            aircraft.autopilot.AutoAim(aim, false, false, false, 1.1f, 135f, false, distance * 0.05f, Vector3.zero);

            if (Vector3.Angle(touchdown - aircraft.GlobalPosition(), aircraft.transform.forward) <
                AutopilotLandPolicy.AlignmentDegrees)
            {
                alignedTime += Time.fixedDeltaTime;
                if (alignedTime > AlignmentHoldSeconds) phase = Phase.Approach;
            }
            else
            {
                alignedTime = 0f;
            }
        }

        private void FlyApproach()
        {
            if (!RunwayStillUsable()) return;
            if (aircraft.gearState == LandingGear.GearState.LockedRetracted) aircraft.SetGear(true);

            GlobalPosition touchdown = runwayUsage.GetTouchdownPoint();
            float distance = FastMath.Distance(touchdown, aircraft.GlobalPosition());
            if (aircraft.radarAlt < 0.1f)
            {
                phase = Phase.Rollout;
                timeOnGround = 0f;
                return;
            }

            ControlsFilter filter = aircraft.GetControlsFilter();
            float runwaySpeed = runwayUsage.Runway.GetVelocity().magnitude;
            if (touchdownTime < 10f)
            {
                GlobalPosition nearest = runwayUsage.GetNearestGlideslopePoint(
                    aircraft.GlobalPosition(), aircraft.definition.spawnOffset.y, touchdownTime);
                Vector3 delta = aircraft.GlobalPosition() - nearest;
                if (glideslopeCorrection == Vector3.zero)
                    glideslopeCorrection = new Vector3(0f, Mathf.Clamp(delta.y, -1f, 1f), 0f);
                else
                    glideslopeCorrection += new Vector3(
                        Mathf.Clamp(delta.x, -4f, 4f) * 0.2f,
                        Mathf.Clamp(delta.y, -1f, 1f) * 0.2f,
                        Mathf.Clamp(delta.z, -4f, 4f) * 0.2f) * Time.fixedDeltaTime;
            }
            else
            {
                glideslopeCorrection = Vector3.zero;
            }

            Vector3 toTouchdown = touchdown - aircraft.GlobalPosition();
            float closing = Vector3.Dot(aircraft.rb.velocity - runwayUsage.Runway.GetVelocity(), toTouchdown.normalized);
            touchdownTime = closing <= 0.01f ? 30f : Mathf.Min(distance / closing, 30f);

            GlobalPosition aim = runwayUsage.GetNearestGlideslopePoint(
                aircraft.GlobalPosition() + aircraft.transform.forward * (200f + distance * 0.2f),
                aircraft.definition.spawnOffset.y, touchdownTime) - glideslopeCorrection;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Vector3 wind = level != null ? level.GetWind(aircraft.GlobalPosition()) : Vector3.zero;
            float forwardAirspeed = Vector3.Dot(aircraft.transform.forward, aircraft.rb.velocity - wind);
            ControlInputs inputs = aircraft.GetInputs();
            inputs.throttle = AutopilotLandPolicy.ApproachThrottle(
                forwardAirspeed, adjustedLandingSpeed + 5f, parameters.cruiseThrottle);

            if (parameters.verticalLanding)
            {
                float targetSpeed = Mathf.Sqrt(runwaySpeed * runwaySpeed + 3f * distance);
                float speedError = aircraft.speed - Mathf.Max(targetSpeed, parameters.shortLandingSpeed);
                float glideError = runwayUsage.GetGlideslopeError(aircraft, touchdownTime);
                inputs.customAxis1 = aircraft.speed > 130f ? 0f : 0.35f - speedError * 0.1f;
                float throttle = 0.8f - speedError * 0.05f +
                    AutopilotLandPolicy.Clamp(0.5f - glideError * 0.1f, 0f, 1f);
                if (filter != null && filter.ReverseThrust && speedError > 10f) throttle = 1f;
                throttle = Mathf.Max(throttle, 0.6f);
                if (aircraft.speed > 130f) throttle = 0f;
                if (touchdownTime < 10f) throttle = Mathf.Max(0.8f - glideError * 0.05f, 0.6f);
                inputs.throttle = Mathf.Clamp01(throttle);
                if (touchdownTime < 4f)
                {
                    hoverTargetHeight = 15f;
                    if (filter != null) filter.SetAutoHover(true);
                    phase = Phase.HoverTouchdown;
                    return;
                }
            }

            bool arrestor = runwayUsage.Runway.Arrestor && aircraft.weaponManager != null &&
                aircraft.weaponManager.HasTailHook();
            if (touchdownTime < (parameters.verticalLanding ? 1f : 2f) && !arrestor)
            {
                inputs.throttle = 0f;
                aim += Vector3.up * 5f;
            }
            aircraft.autopilot.AutoAim(aim, true, true, touchdownTime < 5f, 1.01f, 65f, false, 0f, Vector3.zero);
        }

        private void FlyHoverTouchdown()
        {
            if (aircraft.radarAlt < 0.5f)
            {
                phase = Phase.Rollout;
                timeOnGround = 0f;
                return;
            }
            if (Vector3.Dot(aircraft.cockpit.xform.forward,
                    runwayUsage.GetEnd().position - aircraft.transform.position) < 0f)
            {
                Disengage("Autopilot: missed the runway");
                return;
            }

            ControlsFilter filter = aircraft.GetControlsFilter();
            if (filter != null && !filter.IsAutoHoverEnabled())
            {
                filter.SetAutoHover(true);
                aircraft.SetFlightAssist(false);
            }

            Vector3 direction = runwayUsage.GetDirection().normalized;
            GlobalPosition pad = runwayUsage.Runway.GetNearestPoint(aircraft.transform, false).ToGlobalPosition();
            if (Vector3.Dot(aircraft.cockpit.xform.forward,
                    runwayUsage.GetTouchdownPoint() - aircraft.GlobalPosition()) < 0f)
                hoverTargetHeight -= 4f * Time.fixedDeltaTime;
            else
                hoverTargetHeight = 10f;
            aircraft.autopilot.Hover(pad, hoverTargetHeight, direction);
        }

        private void Rollout()
        {
            timeOnGround += Time.fixedDeltaTime;
            ControlInputs inputs = aircraft.GetInputs();
            inputs.throttle = 0f;
            inputs.brake = AutopilotLandPolicy.BrakeRamp(timeOnGround);
            inputs.customAxis1 = 1f;

            Vector3 direction = runwayUsage.GetDirection().normalized;
            Vector3 ahead = runwayUsage.Runway.GetNearestPoint(aircraft.transform.position, false) + direction * 100f;
            // Keep the rollout aim level with the aircraft. The native landing state reuses the
            // glideslope height here, which commands a climb while still fast enough to fly and
            // makes a landed aircraft lift off again.
            ahead.y = aircraft.transform.position.y;
            aircraft.autopilot.AutoAim(ahead.ToGlobalPosition(), false, true, true, 1.01f, 5f, false, 0f, Vector3.zero);

            if (AutopilotLandPolicy.Stopped(aircraft.speed, aircraft.radarAlt))
                Disengage("Autopilot: landed, brakes set");
        }

        private void FlyToPad()
        {
            if (pad == null || !pad.IsAvailable())
            {
                Disengage("Autopilot: landing pad unavailable");
                return;
            }
            GlobalPosition approach = pad.GetApproachPoint(aircraft);
            if (!padRegistered && FastMath.InRange(approach, aircraft.GlobalPosition(), 200f))
            {
                if (aircraft.gearState == LandingGear.GearState.LockedRetracted) aircraft.SetGear(true);
                pad.RegisterLanding(aircraft);
                padRegistered = true;
                phase = Phase.PadDescent;
                return;
            }
            Vector3 toApproach = approach - aircraft.GlobalPosition();
            toApproach.y = 0f;
            aircraft.autopilot.AutoAim(approach, hoverTargetHeight, approach - aircraft.GlobalPosition(),
                pad.GetVelocity(), toApproach.magnitude > 200f);
        }

        private void DescendToPad()
        {
            if (pad == null || !pad.IsAvailable())
            {
                Disengage("Autopilot: landing pad unavailable");
                return;
            }
            GlobalPosition destination = pad.point.position.ToGlobalPosition();
            if (aircraft.radarAlt < 0.2f)
            {
                ControlInputs inputs = aircraft.GetInputs();
                inputs.brake = 1f;
                inputs.throttle = 0f;
                inputs.pitch = 0f;
                inputs.yaw = 0f;
                inputs.roll = 0f;
                aircraft.FilterInputs();
                if (aircraft.speed < 1f) Disengage("Autopilot: landed, brakes set");
                return;
            }

            Vector3 toPad = destination - aircraft.GlobalPosition();
            toPad.y = 0f;
            hoverTargetHeight = toPad.magnitude < 15f
                ? hoverTargetHeight - 3f * Time.fixedDeltaTime
                : 30f;

            ControlsFilter filter = aircraft.GetControlsFilter();
            if (filter != null && !filter.IsAutoHoverEnabled() &&
                FastMath.InRange(destination, aircraft.GlobalPosition(), 200f))
            {
                // Mirror AIHeloLandingState: hand the final descent to the native hover
                // controller instead of letting the native tiltwing AutoAim keep rewriting
                // flight assist (and its wing-tilt automation) every fixed step.
                filter.SetAutoHover(true);
                aircraft.SetFlightAssist(false);
            }
            if (filter != null && filter.IsAutoHoverEnabled())
            {
                Unit attached;
                Vector3 direction = airbase != null && airbase.TryGetAttachedUnit(out attached)
                    ? attached.transform.forward
                    : -pad.point.forward;
                aircraft.autopilot.Hover(destination, hoverTargetHeight, direction);
            }
            else
            {
                aircraft.autopilot.AutoAim(destination, hoverTargetHeight,
                    pad.point.position - aircraft.transform.position, pad.GetVelocity(),
                    toPad.magnitude > 200f);
            }
        }

        private bool RunwayStillUsable()
        {
            if (airbase == null || Time.timeSinceLevelLoad - lastRunwayCheck < RunwayRecheckSeconds) return true;
            lastRunwayCheck = Time.timeSinceLevelLoad;
            var query = new RunwayQuery
            {
                RunwayType = RunwayQueryType.Landing,
                MinSize = parameters.verticalLanding ? 0f : parameters.takeoffDistance,
                LandingSpeed = parameters.verticalLanding ? 0f : adjustedLandingSpeed,
                TailHook = aircraft.weaponManager != null && aircraft.weaponManager.HasTailHook()
            };
            if (runwayUsage.Runway.IsSuitable(query)) return true;
            Disengage("Autopilot: runway unavailable");
            return false;
        }

        public void ResetForScene()
        {
            ResetState();
            BoscaliRadialMenu.Reset();
        }

        private void ResetState()
        {
            aircraft = null;
            parameters = null;
            airbase = null;
            pad = null;
            runwayUsage = default;
            phase = Phase.None;
            adjustedLandingSpeed = 0f;
            alignedTime = 0f;
            touchdownTime = 0f;
            hoverTargetHeight = 0f;
            timeOnGround = 0f;
            lastRunwayCheck = 0f;
            reachedFinal = false;
            padRegistered = false;
            inputSettled = false;
            storedFlightAssist = false;
            storedAutoHover = false;
            padLanding = false;
            failureReported = false;
            glideslopeCorrection = Vector3.zero;
        }
    }
}
