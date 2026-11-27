using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Configuration;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Networking;
using BoscaliSummer.Modules.Vanguard.Presentation;
using HarmonyLib;
using UnityEngine;
using Player = NuclearOption.Networking.Player;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Runs the local drone station and applies the host's leased commands to each airframe.</summary>
    internal sealed class DronePilot : MonoBehaviour, ISceneService
    {
        private const float SendInterval = 0.05f, InputFreshness = 0.35f, CruiseThrottle = 0.7f;
        // Positive roll is a right bank, which is negative rotation about Unity's local Z.
        private static readonly Vector3 BodyRates = new Vector3(0.8f, 0.7f, -1.7f);

        private struct Sample
        {
            public uint Lease;
            public Vector3 Axes;
            public float Throttle, ReceivedAt;
            public DroneWeapon FireWeapon;
        }

        public static DronePilot Active { get; private set; }
        internal static bool Supported { get; } =
            AccessTools.Method(typeof(PilotPlayerState), nameof(PilotPlayerState.FixedUpdateState), new[] { typeof(Pilot) }) != null
            && AccessTools.Method(typeof(PilotPlayerState), nameof(PilotPlayerState.UpdateState), new[] { typeof(Pilot) }) != null
            && AccessTools.Method(typeof(Missile), "Steering", Type.EmptyTypes) != null
            && AccessTools.Field(typeof(Missile), "inputs")?.FieldType == typeof(Vector3)
            && AccessTools.Field(typeof(Missile), "localAngularVel")?.FieldType == typeof(Vector3);
        public Missile Current { get; private set; }
        public Vector3 LastAxes { get; private set; }
        internal bool ThirdPerson { get; private set; }
        internal bool GunView { get; private set; }
        internal bool ControlsEnabled { get; private set; }
        internal DroneWeapon Weapon { get; private set; }
        internal float Throttle { get; private set; }
        internal Unit Lock { get; private set; }
        internal GlobalPosition LockPoint { get; private set; }
        internal Transform WeaponMount => weaponMount;
        internal Vector3 GunDirection => gunship != null || sentry != null ? weaponMount.forward : Current.transform.forward;
        internal bool HasPortGun => gunship != null;
        internal bool CanCycleWeapon => hasGun && hasGuided;
        private Transform weaponMount;
        private GlaiveDrone gunship;
        private SentinelDrone sentry;
        private bool hasGun, hasGuided;
        private DroneWeapon queuedTrigger;
        private float nextLock;
        private readonly List<Unit> lockCandidates = new List<Unit>();

        private readonly Dictionary<Missile, Sample> samples = new Dictionary<Missile, Sample>();
        private readonly List<Missile> stale = new List<Missile>();
        private VanguardNet net;
        private DronePilotInput input;
        private Aircraft home;
        private Missile releasePending;
        private uint currentLease, releasePendingLease;
        private float nextSend, nextPrune, homeThrottle, homeBrake, homeAltitude;
        private Vector3 homeHeading;
#if !PUBLIC_RELEASE
        private Vector3? testAxes;
        private float testThrottle;
        private bool testFire;

        internal void SetTestInput(Vector3? axes, float throttle, bool fire = false)
        {
            testAxes = axes;
            testThrottle = throttle;
            testFire = fire;
        }
#endif

        public void Configure(VanguardNet network, DroneControlSettings settings)
        {
            net = network;
            input = new DronePilotInput(settings);
        }

        private void Awake() => Active = this;

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

        internal static void Receive(Missile missile, Player player, Vector3 axes, float throttle, DroneWeapon fireWeapon)
        {
            if (Active == null || player == null || !DroneRoster.TryGet(missile, out DroneRoster.Entry entry)
                || entry.Pilot != player || !entry.ValidPilot) return;
            Active.samples[missile] = new Sample
            {
                Lease = entry.Lease, Axes = axes, Throttle = throttle, ReceivedAt = Time.unscaledTime, FireWeapon = fireWeapon
            };
        }

        internal static DroneWeapon Trigger(Missile missile) =>
            TrySample(missile, out Sample sample) && Time.unscaledTime - sample.ReceivedAt <= 0.2f ? sample.FireWeapon : DroneWeapon.None;

        internal void SwitchView()
        {
            ThirdPerson = !ThirdPerson;
            DroneFlightView view = DroneFlightView.Active;
            if (view != null) view.ResetOrbit();
        }

        internal void CycleWeapon()
        {
            if (hasGun && hasGuided) Weapon = Weapon == DroneWeapon.Gun ? DroneWeapon.Guided : DroneWeapon.Gun;
            queuedTrigger = DroneWeapon.None;
            Lock = null;
            nextLock = 0f;
        }

        internal int Ammunition()
        {
            if (Weapon == DroneWeapon.None || Current == null) return 0;
            if (gunship != null) return gunship.RoundsLeft;
            if (sentry != null) return sentry.Rounds;
            bool hasSnapshot = RemoraBoard.Views.TryGetValue(Current.persistentID.Id, out RemoraView view);
            if (Weapon == DroneWeapon.Guided) return hasSnapshot ? RemoraBoard.Left(view) : RemoraBoard.Capacity(Current.persistentID.Id);
            return RemoraArcher.GunRounds - view.Shots;
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
            if (!Supported || missile == null || !missile.LocalSim || missile.rb == null || !IsPiloting(missile)) return false;
            localAngularVelocity = missile.transform.InverseTransformVector(missile.rb.angularVelocity);
            bool hasSample = TrySample(missile, out Sample sample);
            Vector3 demand = hasSample ? Vector3.Scale(sample.Axes, BodyRates) : LevelRates(missile.transform);
            Vector3 error = (demand - localAngularVelocity) * 2.4f;
            deflections = new Vector3(Mathf.Clamp(error.x, -1f, 1f), Mathf.Clamp(error.y, -1f, 1f), Mathf.Clamp(error.z, -1f, 1f));
            missile.SetThrottle(hasSample ? sample.Throttle : CruiseThrottle);
            return true;
        }

        private static Vector3 LevelRates(Transform airframe)
        {
            Vector3 up = airframe.InverseTransformDirection(Vector3.up);
            float pitch = Mathf.Clamp(up.z, -0.35f, 0.35f);
            float roll = Mathf.Abs(up.z) < 0.9f ? Mathf.Clamp(-Mathf.Atan2(up.x, up.y), -0.8f, 0.8f) : 0f;
            return new Vector3(pitch, 0f, roll);
        }

        internal static bool SupportsLinkAircraft(Aircraft aircraft) => Supported && aircraft != null && !aircraft.disabled
            && !aircraft.HasEjected() && aircraft.autopilot != null && aircraft.pilots.Length > 0
            && aircraft.pilots[0] != null && !aircraft.pilots[0].dead;

        internal static bool SupportsAircraft(Aircraft aircraft) => SupportsLinkAircraft(aircraft) && aircraft.LocalSim
            && aircraft.pilots[0].currentState == aircraft.pilots[0].playerState;

        internal bool Holds(Pilot pilot) => Current != null && home != null && pilot != null && pilot == home.pilots[0];

        internal void HoldAircraft()
        {
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
            if (input == null || !GameManager.GetLocalPlayer<Player>(out Player local) || local == null || !SupportsAircraft(local.Aircraft))
            {
                Release();
                return;
            }
            DroneRoster.Entry assignment = DroneRoster.TryGet(Current, out DroneRoster.Entry active) &&
                active.ControlledBy(local) && active.VisibleTo(local) ? active : DroneRoster.ControlledBy(local);
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
                StartControl(local.Aircraft, assignment);
            }
            if (home != local.Aircraft || (now >= nextSend && !DroneRoster.InRange(local, assignment)))
            {
                Release();
                return;
            }

            if (input.ReturnPressed && RequestReturn()) return;
            DronePilotInput.Frame controls = input.Tick(ThirdPerson);
            if (controls.ToggleView) SwitchView();
            if (controls.CycleWeapon) CycleWeapon();
            ControlsEnabled = controls.Enabled;
            Throttle = controls.Throttle;
            GunView = weaponMount != null && gunship != null && !ThirdPerson && controls.GunView;
            DroneFlightView view = DroneFlightView.Active;
            if (view != null && ThirdPerson)
            {
                if (controls.Center) view.ResetOrbit();
                if (controls.LookDelta != Vector2.zero || controls.Zoom != 0f) view.Orbit(controls.LookDelta, controls.Zoom);
            }
            Vector3 axes = controls.Axes;
            bool fire = controls.Fire;
#if !PUBLIC_RELEASE
            if (testAxes.HasValue && DronePilotInput.GameplayOpen && !DronePilotInput.PointerHeld)
            {
                ControlsEnabled = true;
                axes = testAxes.Value;
                Throttle = testThrottle;
                fire = testFire;
            }
#endif
            if (!ControlsEnabled) queuedTrigger = DroneWeapon.None;
            else if (fire) queuedTrigger = Weapon;
            if (now < nextSend) return;
            nextSend = now + SendInterval;
            if (Weapon == DroneWeapon.Guided && now >= nextLock)
            {
                nextLock = now + 0.2f;
                Lock = VanguardFlight.FindPilotLock(Current, lockCandidates, out GlobalPosition point);
                LockPoint = point;
            }
            else if (Weapon != DroneWeapon.Guided) Lock = null;
            if (VanguardNet.SendDroneControls(Current, axes, Throttle, queuedTrigger))
            {
                LastAxes = axes;
                queuedTrigger = DroneWeapon.None;
            }
        }

        private void StartControl(Aircraft aircraft, DroneRoster.Entry assignment)
        {
            home = aircraft;
            ControlInputs inputs = home.GetInputs();
            homeThrottle = inputs.throttle;
            homeBrake = inputs.brake;
            if (home.countermeasureTrigger && home.countermeasureManager != null)
                home.Countermeasures(false, home.countermeasureManager.activeIndex);
            homeAltitude = home.GlobalPosition().y + Mathf.Max(0f, 150f - home.radarAlt);
            homeHeading = Vector3.ProjectOnPlane(home.transform.forward, Vector3.up).normalized;
            if (homeHeading == Vector3.zero) homeHeading = Vector3.forward;
            input.Reset(CruiseThrottle);
            Current = assignment.Missile;
            currentLease = assignment.Lease;
            ThirdPerson = false;
            DroneFlightView view = DroneFlightView.Active;
            if (view != null) view.ResetOrbit();
            gunship = Current.GetComponent<GlaiveDrone>();
            sentry = Current.GetComponent<SentinelDrone>();
            VanguardRole role = assignment.Role;
            Gun gun = role == VanguardRole.Drone && !RemoraBoard.IsManta(Current) ? Current.GetComponentInChildren<Gun>(true) : null;
            weaponMount = gunship != null ? gunship.PilotMuzzle : sentry != null ? sentry.PilotMuzzle : gun != null ? gun.transform : null;
            hasGun = weaponMount != null;
            hasGuided = role == VanguardRole.Drone;
            Weapon = hasGun ? DroneWeapon.Gun : hasGuided ? DroneWeapon.Guided : DroneWeapon.None;
            nextSend = nextLock = 0f;
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

        internal bool RequestReturn()
        {
            if (Current == null || !DronePilotInput.CanReturn) return false;
            Release();
            return true;
        }

        private void Restore()
        {
            if (ReferenceEquals(home, null)) return;
            Current = null;
            Lock = null;
            lockCandidates.Clear();
            weaponMount = null;
            gunship = null;
            sentry = null;
            Weapon = DroneWeapon.None;
            queuedTrigger = DroneWeapon.None;
            GunView = ControlsEnabled = ThirdPerson = false;
            LastAxes = Vector3.zero;
#if !PUBLIC_RELEASE
            testAxes = null;
            DronePilotInput.TestPointer = false;
#endif
            Aircraft aircraft = home;
            home = null;
            if (aircraft != null && !aircraft.disabled)
            {
                ControlInputs inputs = aircraft.GetInputs();
                inputs.throttle = homeThrottle;
                inputs.brake = homeBrake;
                inputs.pitch = inputs.roll = inputs.yaw = 0f;
            }
            DroneFlightView view = DroneFlightView.Active;
            if (view != null && !ReferenceEquals(aircraft, null)) view.HandBack(aircraft);
        }
    }
}
