using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Networking;
using HarmonyLib;
using NuclearOption.UI;
using UnityEngine;
using Player = NuclearOption.Networking.Player;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Uses the aircraft's stock input reader while leaving its pilot and network ownership intact.</summary>
    internal sealed class DronePilot : MonoBehaviour, ISceneService
    {
        private const float SendInterval = 0.05f, InputFreshness = 0.75f, CruiseThrottle = 0.7f;
        // Positive stock roll is a right bank, which is negative rotation about Unity's local Z.
        private static readonly Vector3 BodyRates = new Vector3(0.8f, 0.7f, -1.7f);
        private static readonly MethodInfo AxisMethod = AccessTools.Method(typeof(PilotPlayerState), "PlayerAxisControls", Type.EmptyTypes);
        private static readonly MethodInfo ThrottleMethod = AccessTools.Method(typeof(PilotPlayerState), "PlayerThrottleAxis1Controls", Type.EmptyTypes);
        private static readonly FieldInfo InputField = AccessTools.Field(typeof(PilotBaseState), "controlInputs");
        private static readonly FieldInfo StockThrottleField = AccessTools.Field(typeof(PilotPlayerState), "simulatedThrottle");
        private static readonly Action<PilotPlayerState> ReadAxes = AxisMethod?.ReturnType == typeof(void) ? AccessTools.MethodDelegate<Action<PilotPlayerState>>(AxisMethod) : null;
        private static readonly Action<PilotPlayerState> ReadThrottle = ThrottleMethod?.ReturnType == typeof(void) ? AccessTools.MethodDelegate<Action<PilotPlayerState>>(ThrottleMethod) : null;
        private static readonly AccessTools.FieldRef<PilotBaseState, ControlInputs> StateInputs = InputField?.FieldType == typeof(ControlInputs) ? AccessTools.FieldRefAccess<PilotBaseState, ControlInputs>(InputField) : null;
        private static readonly AccessTools.FieldRef<PilotPlayerState, float> StockThrottle = StockThrottleField?.FieldType == typeof(float) ? AccessTools.FieldRefAccess<PilotPlayerState, float>(StockThrottleField) : null;

        private struct Sample
        {
            public uint Lease;
            public Vector3 Axes;
            public float Throttle, ReceivedAt;
        }

        public static DronePilot Active { get; private set; }
        internal static bool Supported { get; } = ReadAxes != null && ReadThrottle != null && StateInputs != null && StockThrottle != null
            && AccessTools.Method(typeof(PilotPlayerState), nameof(PilotPlayerState.FixedUpdateState), new[] { typeof(Pilot) }) != null
            && AccessTools.Method(typeof(PilotPlayerState), "PlayerControls", Type.EmptyTypes) != null
            && AccessTools.Method(typeof(Missile), "Steering", Type.EmptyTypes) != null
            && AccessTools.Field(typeof(Missile), "inputs")?.FieldType == typeof(Vector3)
            && AccessTools.Field(typeof(Missile), "localAngularVel")?.FieldType == typeof(Vector3);
        public Missile Current { get; private set; }
        public Vector3 LastAxes { get; private set; }

        private readonly Dictionary<Missile, Sample> samples = new Dictionary<Missile, Sample>();
        private readonly List<Missile> stale = new List<Missile>();
        private readonly ControlInputs droneInputs = new ControlInputs();
        private VanguardNet net;
        private ForwardCamera droneCamera;
        private CameraBaseState returnState;
        private Unit returnUnit;
        private Aircraft home;
        private Missile releasePending;
        private uint currentLease, releasePendingLease;
        private float nextSend, nextPrune, homeThrottle, homeBrake, homeCustomAxis, homeAltitude, droneThrottleAxis;
        private Vector3 homeHeading, homeJoystick, sampledAxes;
        private bool homeAssist;
#if !PUBLIC_RELEASE
        private Vector3? testAxes;
        private float testThrottle;

        internal void SetTestInput(Vector3? axes, float throttle)
        {
            testAxes = axes;
            testThrottle = throttle;
        }
#endif

        public void Configure(VanguardNet network) => net = network;

        private void Awake()
        {
            Active = this;
            droneCamera = new ForwardCamera(this);
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (Active == this) Active = null;
        }

        public void ResetForScene()
        {
            Restore();
            releasePending = null;
            samples.Clear();
            stale.Clear();
            nextSend = nextPrune = 0f;
        }

        internal static bool IsPiloting(Missile missile) => DroneRoster.ActivePilot(missile) != null;

        internal static void Receive(Missile missile, Player player, Vector3 axes, float throttle)
        {
            if (Active == null || player == null || !DroneRoster.TryGet(missile, out DroneRoster.Entry entry)
                || entry.Pilot != player || !entry.ValidPilot) return;
            Active.samples[missile] = new Sample { Lease = entry.Lease, Axes = axes, Throttle = throttle, ReceivedAt = Time.unscaledTime };
        }

        private static bool TrySample(Missile missile, out Sample sample)
        {
            sample = default;
            return Active != null && DroneRoster.TryGet(missile, out DroneRoster.Entry entry)
                && Active.samples.TryGetValue(missile, out sample) && sample.Lease == entry.Lease
                && Time.unscaledTime - sample.ReceivedAt <= InputFreshness;
        }

        internal static bool ApplySteering(Missile missile, ref Vector3 deflections, ref Vector3 localAngularVelocity)
        {
            if (!Supported || missile == null || missile.rb == null || !IsPiloting(missile)) return false;
            localAngularVelocity = missile.transform.InverseTransformVector(missile.rb.angularVelocity);
            bool hasSample = TrySample(missile, out Sample sample);
            Vector3 demand = hasSample ? Vector3.Scale(sample.Axes, BodyRates) : Vector3.zero;
            Vector3 error = (demand - localAngularVelocity) * 2.4f;
            deflections = new Vector3(Mathf.Clamp(error.x, -1f, 1f), Mathf.Clamp(error.y, -1f, 1f), Mathf.Clamp(error.z, -1f, 1f));
            missile.SetThrottle(hasSample ? sample.Throttle : CruiseThrottle);
            return true;
        }

        internal static bool SupportsLinkAircraft(Aircraft aircraft) => Supported && aircraft != null && !aircraft.disabled
            && !aircraft.HasEjected() && aircraft.autopilot != null && aircraft.pilots.Length > 0
            && aircraft.pilots[0] != null && !aircraft.pilots[0].dead;

        internal static bool SupportsAircraft(Aircraft aircraft) => SupportsLinkAircraft(aircraft) && aircraft.LocalSim
            && aircraft.pilots[0].currentState == aircraft.pilots[0].playerState;

        private static bool CanReadInput => GameManager.flightControlsEnabled && !DynamicMap.mapMaximized
            && !RadialMenuMain.IsInUse() && !LeaderboardMenu.IsOpen() && GameManager.playerInput != null;

        internal bool Controls(PilotPlayerState state) => Current != null && home != null && home.pilots[0].playerState == state;

        internal void ReadPlayerActions(PilotPlayerState state)
        {
            if (!CanReadInput) return;
            if (GameManager.playerInput.GetButtonDown("Eject")) { Release(); return; }
            if (!home.IsAutoHoverEnabled()) ReadStockControls(state, ReadThrottle);
        }

        internal void ReadPlayerAxes(PilotPlayerState state)
        {
            if (CanReadInput)
            {
                ReadStockControls(state, ReadAxes);
                sampledAxes = new Vector3(droneInputs.pitch, droneInputs.yaw, droneInputs.roll);
            }
            else sampledAxes = Vector3.zero;

            ControlInputs homeInputs = home.GetInputs();
            GlobalPosition position = home.GlobalPosition();
            if (home.IsLanded())
            {
                homeInputs.throttle = 0f;
                homeInputs.brake = 1f;
                homeInputs.pitch = homeInputs.roll = homeInputs.yaw = 0f;
                home.FilterInputs();
                return;
            }
            homeInputs.throttle = Mathf.Max(homeThrottle, home.GetAircraftParameters().cruiseThrottle);
            homeInputs.brake = 0f;
            if (home.autopilot is AutopilotPlane plane)
            {
                GlobalPosition aim = position + homeHeading * 3000f;
                aim.y = homeAltitude;
                plane.AutoAim(aim, true, false, false, 0.6f, 40f, false, 0f, Vector3.zero);
            }
            else
            {
                GlobalPosition aim = position + homeHeading * 600f;
                aim.y = homeAltitude;
                home.autopilot.AutoAim(aim, 0f, homeHeading, Vector3.zero, false);
            }
            home.FilterInputs();
        }

        private void ReadStockControls(PilotPlayerState state, Action<PilotPlayerState> reader)
        {
            ControlInputs original = StateInputs(state);
            float originalThrottle = StockThrottle(state);
            Vector3 originalAxes = new Vector3(state.pitchInput, state.yawInput, state.rollInput);
            CameraMode mode = CameraStateManager.cameraMode;
            try
            {
                StateInputs(state) = droneInputs;
                StockThrottle(state) = droneThrottleAxis;
                // The stock reader evaluates mouse flight only in a forward pilot view.
                CameraStateManager.cameraMode = CameraMode.cockpit;
                reader(state);
            }
            finally
            {
                CameraStateManager.cameraMode = mode;
                StateInputs(state) = original;
                droneThrottleAxis = StockThrottle(state);
                StockThrottle(state) = originalThrottle;
                state.pitchInput = originalAxes.x;
                state.yawInput = originalAxes.y;
                state.rollInput = originalAxes.z;
            }
        }

        private void Update()
        {
            try { UpdateControl(); }
            catch (Exception error) { PatchGuard.Report("DronePilot.Update", error); Release(); }
        }

        private void UpdateControl()
        {
            float now = Time.unscaledTime;
            if (now >= nextPrune)
            {
                nextPrune = now + 1f;
                foreach (var entry in samples)
                    if (!DroneRoster.TryGet(entry.Key, out DroneRoster.Entry link) || link.Lease != entry.Value.Lease) stale.Add(entry.Key);
                foreach (Missile missile in stale) samples.Remove(missile);
                stale.Clear();
            }
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (!GameManager.GetLocalPlayer<Player>(out Player local) || local == null || !SupportsAircraft(local.Aircraft) || camera == null || camera.currentState == null)
            {
                Release();
                return;
            }
            DroneRoster.Entry assignment = DroneRoster.ControlledBy(local);
            Missile assigned = assignment?.Missile;
            if (assigned != releasePending || (assignment != null && assignment.Lease != releasePendingLease)) releasePending = null;
            if (assigned == null || assigned == releasePending)
            {
                Restore();
                return;
            }
            if (Current != assigned || currentLease != assignment.Lease)
            {
                Restore();
                StartControl(camera, local.Aircraft, assignment);
            }
            if (Current == null || Current.disabled || home != local.Aircraft || camera.currentState != droneCamera || camera.followingUnit != Current)
            {
                Release();
                return;
            }
            if (now < nextSend) return;
            nextSend = now + SendInterval;
            Vector3 axes = CanReadInput ? sampledAxes : Vector3.zero;
            float throttle = droneInputs.throttle;
#if !PUBLIC_RELEASE
            if (testAxes.HasValue) { axes = testAxes.Value; throttle = testThrottle; }
#endif
            if (VanguardNet.SendDroneControls(Current, axes, throttle)) LastAxes = axes;
        }

        private void StartControl(CameraStateManager camera, Aircraft aircraft, DroneRoster.Entry assignment)
        {
            home = aircraft;
            ControlInputs inputs = home.GetInputs();
            homeThrottle = inputs.throttle;
            homeBrake = inputs.brake;
            homeCustomAxis = inputs.customAxis1;
            homeAssist = home.flightAssist;
            if (home.countermeasureTrigger && home.countermeasureManager != null)
                home.Countermeasures(false, home.countermeasureManager.activeIndex);
            homeAltitude = home.GlobalPosition().y + Mathf.Max(0f, 150f - home.radarAlt);
            homeHeading = Vector3.ProjectOnPlane(home.transform.forward, Vector3.up).normalized;
            if (homeHeading == Vector3.zero) homeHeading = Vector3.forward;
            FlightHud hud = SceneSingleton<FlightHud>.i;
            if (hud != null && hud.virtualJoystickPos != null)
            {
                homeJoystick = hud.virtualJoystickPos.transform.localPosition;
                hud.SetVirtualJoystick(Vector3.zero);
            }
            else homeJoystick = Vector3.zero;
            returnUnit = camera.followingUnit;
            returnState = camera.currentState;
            droneInputs.pitch = droneInputs.roll = droneInputs.yaw = 0f;
            droneInputs.throttle = CruiseThrottle;
            droneThrottleAxis = PlayerSettings.throttleUseNegative ? CruiseThrottle * 2f - 1f : CruiseThrottle;
            Current = assignment.Missile;
            currentLease = assignment.Lease;
            camera.SetFollowingUnit(Current);
            camera.SwitchState(droneCamera);
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null) map.Minimize();
        }

        internal void Release()
        {
            try
            {
                if (Current != null)
                {
                    releasePending = Current;
                    releasePendingLease = currentLease;
                    if (net != null) net.SendDroneAction(Current, DroneAction.Release);
                }
            }
            finally { Restore(); }
        }

        private void Restore()
        {
            Missile previous = Current;
            Current = null;
            sampledAxes = LastAxes = Vector3.zero;
#if !PUBLIC_RELEASE
            testAxes = null;
#endif
            Aircraft aircraft = home;
            Unit oldFocus = returnUnit;
            CameraBaseState oldState = returnState;
            home = null;
            returnUnit = null;
            returnState = null;
            if (ReferenceEquals(aircraft, null)) return;
            if (aircraft != null && !aircraft.disabled)
            {
                ControlInputs inputs = aircraft.GetInputs();
                inputs.throttle = homeThrottle;
                inputs.brake = homeBrake;
                inputs.customAxis1 = homeCustomAxis;
                aircraft.flightAssist = homeAssist;
                inputs.pitch = inputs.roll = inputs.yaw = 0f;
                FlightHud hud = SceneSingleton<FlightHud>.i;
                if (hud != null && hud.virtualJoystickPos != null) hud.SetVirtualJoystick(homeJoystick);
            }
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera != null && camera.currentState != null && NetworkSceneSingleton<LevelInfo>.i != null
                && (camera.currentState == droneCamera || camera.followingUnit == previous || camera.followingUnit == null))
            {
                bool mapWasOpen = DynamicMap.mapMaximized;
                Unit target = oldFocus != null && !oldFocus.disabled ? oldFocus : aircraft != null && !aircraft.disabled ? aircraft : null;
                camera.SetFollowingUnit(target);
                if (target == oldFocus && target != null && oldState != null) camera.SwitchState(oldState);
                else if (target != null && target == aircraft) camera.SwitchState(camera.cockpitState);
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (mapWasOpen && map != null && !DynamicMap.mapMaximized) map.Maximize();
            }
        }

        private sealed class ForwardCamera : CameraBaseState
        {
            private readonly DronePilot pilot;

            internal ForwardCamera(DronePilot pilot) => this.pilot = pilot;

            public override void EnterState(CameraStateManager camera)
            {
                camera.cameraPivot.SetParent(Datum.origin);
                camera.transform.SetParent(camera.cameraPivot);
                camera.transform.localPosition = Vector3.zero;
                camera.transform.localRotation = Quaternion.identity;
                camera.mainCamera.nearClipPlane = 0.3f;
                CameraStateManager.cameraMode = CameraMode.orbit;
                UpdateState(camera);
            }

            public override void LeaveState(CameraStateManager camera) { }
            public override void FixedUpdateState(CameraStateManager camera) { }

            public override void UpdateState(CameraStateManager camera)
            {
                Missile drone = pilot.Current;
                if (drone == null || drone.disabled) return;
                Transform model = drone.transform;
                camera.cameraPivot.SetPositionAndRotation(model.position + model.forward * (drone.maxRadius + 0.5f), model.rotation);
                camera.cameraVelocity = drone.rb != null ? drone.rb.velocity : Vector3.zero;
            }
        }
    }
}
