using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Patches;
using BoscaliSummer.Modules.Hud.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using NOAvionics;
using UnityEngine;
namespace BoscaliSummer.Modules.Hud.Presentation
{
    // Service and lifecycle only. Feed handles contain data; the panel owns a fixed widget pool
    // parented directly under the native weapons panel (see StatusPanel).
    // Native CameraStateManager runs at order 2 and owns camera-state entry/visibility.
    // Since 2026-10-07 nothing is configurable: every feed shows, notices are on, the vanilla HUD
    // is forced open in orbit/chase, Wingview and its look-ahead are on, the target card shows.
    // Own missiles get preview lines (ownship → missile → target) through the same store, and
    // the C-menu can ride one with the orbit camera (IMissileView).
    [DefaultExecutionOrder(1000)]
    internal sealed class HudBoard : MonoBehaviour, ISceneService, IHudBoard, IMissileView
    {
        /// <summary>The one HudBoard alive this scene, reached statically from
        /// <c>Patches/WingviewCameraPatch.cs</c> the same way other Boscali patches reach their
        /// owning controller (see <c>AutopilotLandController.Instance</c>).</summary>
        public static HudBoard Instance { get; private set; }

        private readonly HudFeedStore store = new HudFeedStore(() => Time.unscaledTime);
        private readonly HudMessage[] snapshot = new HudMessage[HudLayout.MaxRows];
        private readonly BoardFrame frame = new BoardFrame();
        private readonly ExternalHudEnabler externalHud = new ExternalHudEnabler();
        private readonly WingviewCameraState wingview = new WingviewCameraState();
        private readonly ThirdPersonHudCenter hudCenterProjection = new ThirdPersonHudCenter();
        private readonly NativeFlightNumberHider flightNumberHider = new NativeFlightNumberHider();
        private readonly ThreatWatch threats = new ThreatWatch();
        private readonly CountermeasureWatch countermeasures = new CountermeasureWatch();
        private readonly FuelWatch fuelWatch = new FuelWatch();
        private readonly DamageWatch damageWatch = new DamageWatch();
        private readonly MissileTracker missileTracker = new MissileTracker();
        private readonly MissileView missileView = new MissileView();
        private readonly List<MissileTrack> missileTracks = new List<MissileTrack>(MissileTracks.MaxShown);
        // One held line per displayed slot, keyed by slot and never by missile: a key per missile piled up dead lines in the
        // 16-line store until it started evicting other features' lines.
        private static readonly string[] MissileKeys = { "msl-0", "msl-1", "msl-2" };
        private readonly IHudLine[] missileSlots = new IHudLine[MissileTracks.MaxShown];
        // Lamp test clock: restarts whenever the native HUD shows a different aircraft (spawn, swap).
        private Aircraft bootAircraft;
        private float bootStart;
        private StatusPanel panel;
        private ThirdPersonHudCluster cluster;
        private MissileCamPanel missileCamPanel;
        private MissileCamData camData;
        private float nextRead;
        private System.Func<int, bool> missileFlying;

        // Target closure: range delta between 10 Hz reads of the same target.
        private Unit closureTarget;
        private float closureRange, closureTime;

        private void Awake() => Instance = this;

        /// <summary>The live own-missile set (the sim probe in <c>HudAutomation</c> reads it).</summary>
        internal MissileTracker Tracker => missileTracker;

        private void OnEnable()
        {
            missileTracker.Launched += OnOwnLaunch;
            hudCenterProjection.Subscribe(
                () => isActiveAndEnabled && ExternalHudEnabler.CanShowExternalHud(OwnExternalView(out _)),
                () => OwnExternalView(out Aircraft aircraft) ? aircraft : null);
        }

        /// <summary>The camera shows the local pilot's own aircraft from outside, or rides one of its missiles: the native HUD is forced open.</summary>
        private static bool OwnExternalView(out Aircraft aircraft) =>
            ExternalHudEnabler.IsOwnAircraftExternalView(out aircraft) || MissileCam.IsOwnMissileView(out aircraft, out _);

        /// <summary>Called from <c>Patches/WingviewCameraPatch.cs</c>'s postfix every
        /// <c>CameraOrbitState.UpdateState</c> while installed.</summary>
        public void TickWingview(CameraStateManager cam, ref float panView, ref float tiltView,
            float viewDistAdjust, float lookAtTargetLerp)
        {
            wingview.Tick(cam, ref panView, ref tiltView, viewDistAdjust, lookAtTargetLerp);
        }

        public IHudLine Acquire(string owner, string channel, string key) => store.Acquire(owner, channel, key);
        public void Notice(string channel, HudTone tone, string text, string detail = null) =>
            store.Notice(channel, tone, text, detail);

        private void LateUpdate()
        {
            if (Application.isBatchMode) return;

            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            // Orbit state objects are reused. A trip through cockpit/chase must clear old pose
            // smoothing even though the orbit postfix does not run while that view is inactive.
            if (cam == null || cam.currentState != cam.orbitState) wingview.Reset();

            // Re-checked every tick: vanilla re-disables FlightHud's canvas on every orbit/chase
            // state entry, and a forced canvas must be released the instant the condition ends.
            bool viewingOwnExternally = ExternalHudEnabler.IsOwnAircraftExternalView(out Aircraft ownAircraft);
            Missile ridden = null;
            Aircraft missileOwner = null;
            bool viewingMissile = !viewingOwnExternally && MissileCam.IsOwnMissileView(out missileOwner, out ridden);
            externalHud.Tick(viewingOwnExternally || viewingMissile);

            // Same gate as the forced-canvas condition above: the third-person HUD (hidden
            // native numbers, screen-fixed cluster, releveled HUDCenter) only ever shows where
            // the native HUDCanvas is actually up. Riding a missile gets the same canvas and its own readout.
            bool canShow = ExternalHudEnabler.CanShowExternalHud(viewingOwnExternally || viewingMissile);
            bool thirdPerson = canShow && viewingOwnExternally;
            bool missileCam = canShow && viewingMissile;
            flightNumberHider.Tick(thirdPerson || missileCam, thirdPerson ? ownAircraft : missileOwner);
            if (!thirdPerson) cluster?.Hide();
            if (!missileCam) missileCamPanel?.Hide();

            if (panel == null) panel = new StatusPanel();
            if (!panel.Ensure()) return;
            if (cluster == null) cluster = new ThirdPersonHudCluster();
            cluster.Ensure();
            if (missileCamPanel == null) missileCamPanel = new MissileCamPanel();
            missileCamPanel.Ensure();

            if (Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + .1f;

            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            Aircraft hudAircraft = combat != null ? combat.aircraft : null;
            float now = Time.unscaledTime;
            if (hudAircraft != bootAircraft)
            {
                bootAircraft = hudAircraft;
                bootStart = now;
                ShotFeed.Ledger.Clear();
            }
            PublishMissiles(hudAircraft);
            ShotFeed.Ledger.Expire(now, missileFlying ??= missileTracker.IsLive);
            missileView.Tick(missileTracker.Missiles);
            frame.Systems = ReadSystems(combat);
            frame.Threat = threats.Read(hudAircraft, frame);
            countermeasures.Read(hudAircraft, frame);
            fuelWatch.Read(hudAircraft, frame);
            damageWatch.Read(hudAircraft, frame);
            frame.Now = now;
            frame.Units = AvUnitsOf((int)PlayerSettings.unitSystem);
            frame.BootAge = hudAircraft != null ? now - bootStart : -1f;
            frame.HasTicker = store.Ticker(out frame.Ticker);
            // The pilot's situation, read from which feeds are up and what is inbound, ranks the cells and the cue strip.
            CueContext context = CuePriority.Detect(store.HasLive("ils"), store.HasLive("skywell"),
                frame.Threat.Missiles > 0 || frame.Threat.SinceLock < HudThreats.LockHold || missileTracks.Count > 0 || store.HasLive("ace-hunt"));
            int count = store.Snapshot(snapshot, HudLayout.MaxRows, context);
            panel.Present(snapshot, count, frame, ShotFeed.Ledger);

            // The cockpit already has its own native tac screen for this feed; the card only
            // earns its space in the external views this module can force the HUD into.
            Texture cameraTexture = null;
            string cameraCode = null;
            float range = float.NaN, closure = float.NaN;
            if (viewingOwnExternally && ownAircraft != null && ownAircraft.targetCam != null &&
                BoscaliSummer.Core.Game.NativeCamera.ReadMode(ownAircraft.targetCam) != TargetCam.CamMode.landingMode &&
                BoscaliSummer.Core.Game.NativeCamera.TryGet(ownAircraft, out Camera camera, out string mode) &&
                camera.targetTexture != null && camera.targetTexture.IsCreated())
            {
                cameraTexture = camera.targetTexture;
                cameraCode = mode;
                ReadTarget(ownAircraft, out range, out closure);
            }

            if (missileCam)
            {
                IReadOnlyList<Missile> list = missileTracker.Missiles;
                int at = -1;
                for (int i = 0; i < list.Count && at < 0; i++) if (ReferenceEquals(list[i], ridden)) at = i;
                MissileCam.Read(ridden, at, list.Count, ref camData);
                missileCamPanel.Present(true, camData, AvUnitsOf((int)PlayerSettings.unitSystem), ThirdPersonHudLayout.TextScale(PlayerSettings.hudTextSize));
            }
            else missileCamPanel?.Present(false, default, NOAvionics.AvUnits.Metric, 1f);

            if (thirdPerson && ownAircraft != null)
            {
                Rigidbody rb = ownAircraft.rb;
                float speed = ownAircraft.speed;
                // Global (floating-origin independent) height; transform.position.y is origin-local.
                float altitude = ownAircraft.transform.position.GlobalY();
                float climb = rb != null ? rb.velocity.y : 0f;
                float heading = ownAircraft.transform.eulerAngles.y;
                float soundSpeed = LevelInfo.GetSpeedOfSound(altitude);
                float mach = soundSpeed > 0.01f ? speed / soundSpeed : 0f;
                float fuel = BoscaliSummer.Core.Game.AircraftReadout.Fuel(ownAircraft);
                float throttle = Mathf.Clamp01(ownAircraft.GetInputs()?.throttle ?? 0f);
                Transform t = ownAircraft.transform;
                Vector3 localVelocity = rb != null ? t.InverseTransformDirection(rb.velocity) : Vector3.zero;

                cluster.Present(true, AvUnitsOf((int)PlayerSettings.unitSystem),
                    new ThirdPersonHudCluster.Flight
                    {
                        SpeedMps = speed, AltitudeM = altitude, RadarAltM = ownAircraft.radarAlt, ClimbMps = climb,
                        HeadingDeg = heading, Mach = mach, G = BoscaliSummer.Core.Game.AircraftReadout.LoadFactor(ownAircraft),
                        GLimit = BoscaliSummer.Core.Game.AircraftReadout.GLimit(ownAircraft), Fuel = fuel, Throttle = throttle,
                        BankDeg = Attitude.Bank(t.right.y, t.up.y), SlipDeg = Attitude.Slip(localVelocity.x, localVelocity.z),
                        AoaDeg = BoscaliSummer.Core.Game.AircraftReadout.AngleOfAttack(ownAircraft),
                        FuelBingo = frame.Fuel.Bingo, FuelJoker = frame.Fuel.Joker,
                    },
                    cameraTexture, cameraCode, range, closure, ThirdPersonHudLayout.TextScale(PlayerSettings.hudTextSize));
            }
            else
            {
                cluster.Present(false, NOAvionics.AvUnits.Metric, default, null, null, float.NaN, float.NaN, 1f);
            }
        }

        /// <summary>The annunciator row's switch states, read off the aircraft the native HUD is showing.
        /// Systems the airframe lacks stay null so their lamp is left out.</summary>
        private static SystemStates ReadSystems(CombatHUD hud)
        {
            var s = new SystemStates();
            Aircraft a = hud != null ? hud.aircraft : null;
            if (a == null) return s;
            switch (a.gearState)
            {
                case LandingGear.GearState.LockedExtended: s.Gear = GearLamp.Down; break;
                case LandingGear.GearState.LockedRetracted: s.Gear = GearLamp.Up; break;
                case LandingGear.GearState.Extending:
                case LandingGear.GearState.Retracting: s.Gear = GearLamp.Moving; break;
            }
            var filter = a.GetControlsFilter();
            if (filter != null && filter.HasFlightAssist()) s.FlightAssist = a.flightAssist;
            if (filter != null && filter.HasAutoHover()) s.AutoHover = a.IsAutoHoverEnabled();
            if (a.radar != null) s.RadarEmitting = a.radar.activated;
            if (BoscaliSummer.Core.Game.GameAccess.TryGetNightVisionState(out bool nvg, out _)) s.NightVision = nvg;
            s.EngineRunning = a.Ignition;
            return s;
        }

        /// <summary>Range to the first selected target and its closure rate (positive = closing).</summary>
        private void ReadTarget(Aircraft aircraft, out float range, out float closure)
        {
            range = closure = float.NaN;
            if (aircraft.weaponManager == null) return;
            var targets = aircraft.weaponManager.GetTargetList();
            if (targets == null || targets.Count == 0 || targets[0] == null) { closureTarget = null; return; }
            Unit target = targets[0];
            range = Vector3.Distance(aircraft.transform.position, target.transform.position);
            float now = Time.unscaledTime;
            if (ReferenceEquals(target, closureTarget) && now > closureTime + 0.05f)
                closure = (closureRange - range) / (now - closureTime);
            closureTarget = target;
            closureRange = range;
            closureTime = now;
        }

        private static NOAvionics.AvUnits AvUnitsOf(int unitSystem) =>
            unitSystem == (int)NOAvionics.AvUnits.Imperial ? NOAvionics.AvUnits.Imperial : NOAvionics.AvUnits.Metric;

        /// <summary>The shot tracker's launch hook: every own delivery the game registers earns one pip (decoys do not).</summary>
        private void OnOwnLaunch(Missile missile)
        {
            WeaponInfo info = missile != null ? missile.GetWeaponInfo() : null;
            if (info == null || !ShotRules.IsDelivery(info.blastDamage, info.jammer)) return;
            ShotFeed.Ledger.Launch(missile.GetInstanceID(), info.bomb, Time.unscaledTime);
        }

        /// <summary>Own-missile preview lines: one held line per displayed slot, released when the slot empties.</summary>
        private void PublishMissiles(Aircraft hudAircraft)
        {
            AvUnits units = AvUnitsOf((int)PlayerSettings.unitSystem);
            missileTracker.Read(hudAircraft, missileTracks);
            for (int i = 0; i < missileSlots.Length; i++)
            {
                if (i >= missileTracks.Count) { missileSlots[i]?.Release(); continue; }
                MissileTrack track = missileTracks[i];
                // Acquire is idempotent and re-creates an evicted line, so a handle kept across ticks cannot go dead.
                IHudLine line = missileSlots[i] = store.Acquire("missile", "missile", MissileKeys[i]);
                line?.Set(MissileTracks.ToneFor(track), MissileTracks.LineText(track, units),
                    MissileTracks.DetailText(track), MissileTracks.BarFor(track));
            }
        }

        // IMissileView: the C-menu rides the same tracked list the preview lines show.
        bool IMissileView.Available => bootAircraft != null && !bootAircraft.disabled;
        bool IMissileView.Active => missileView.Active;
        int IMissileView.Count => missileTracker.Missiles.Count;
        bool IMissileView.CanEnter => missileTracker.Missiles.Count > 0 && !missileView.Active;

        string IMissileView.Status
        {
            get
            {
                var missiles = missileTracker.Missiles;
                if (missiles.Count == 0 || bootAircraft == null) return string.Empty;
                float closest = float.MaxValue;
                Vector3 own = bootAircraft.transform.position;
                for (int i = 0; i < missiles.Count; i++)
                {
                    if (missiles[i] == null) continue;
                    float d = Vector3.Distance(own, missiles[i].transform.position);
                    if (d < closest) closest = d;
                }
                if (closest == float.MaxValue) return string.Empty;
                return MissileTracks.Status(missiles.Count, closest, AvUnitsOf((int)PlayerSettings.unitSystem));
            }
        }

        string IMissileView.CurrentLabel
        {
            get
            {
                if (!missileView.Active || missileView.Current == null) return string.Empty;
                var missiles = missileTracker.Missiles;
                int at = missileView.Index + 1;
                return "MSL " + at + "/" + missiles.Count;
            }
        }

        void IMissileView.Enter() => missileView.Enter(missileTracker.Missiles);
        void IMissileView.Next() => missileView.Next(missileTracker.Missiles);
        void IMissileView.Prev() => missileView.Prev(missileTracker.Missiles);
        void IMissileView.Exit() => missileView.Exit();

        public void ResetForScene()
        {
            missileView.Exit();
            missileTracker.Release();
            System.Array.Clear(missileSlots, 0, missileSlots.Length);
            missileTracks.Clear();
            store.Reset();
            panel?.Destroy();
            panel = null;
            cluster?.Destroy();
            cluster = null;
            missileCamPanel?.Destroy();
            missileCamPanel = null;
            nextRead = 0;
            closureTarget = null;
            bootAircraft = null;
            threats.Release();
            fuelWatch.Release();
            damageWatch.Release();
            ShotFeed.Ledger.Clear();
            externalHud.Release();
            wingview.Reset();
            flightNumberHider.Dispose();
        }
        private void OnDisable()
        {
            missileTracker.Launched -= OnOwnLaunch;
            missileTracker.Release();
            missileView.Exit();
            panel?.Hide();
            cluster?.Hide();
            missileCamPanel?.Hide();
            externalHud.Release();
            wingview.Reset();
            flightNumberHider.Tick(false, null);
            hudCenterProjection.Unsubscribe();
            threats.Release();
            fuelWatch.Release();
            damageWatch.Release();
        }
        private void OnDestroy()
        {
            ResetForScene();
            hudCenterProjection.Unsubscribe();
            if (Instance == this) Instance = null;
        }
    }
}
