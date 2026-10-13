using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Hud.Configuration;
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
    // The TAC BOARD's options (HudSettings, section "TacBoard", 2026-10-11) are read once per 10 Hz tick into `options`. The rest is not configurable (since 2026-10-07): the
    // vanilla HUD is forced open in orbit/chase, Wingview and its look-ahead are on, the target card shows.
    // The C-menu can ride an own missile with the orbit camera (IMissileView). The board is the TAC BOARD (HUD v8, see StatusPanel):
    // this service reads the watchers at 10 Hz into the BoardFrame, serves Hud's own scope feeds (SYSTEMS, THREAT, SHOT, MISSILE CAM) and carries the board's motion.
    [DefaultExecutionOrder(1000)]
    internal sealed class HudBoard : MonoBehaviour, ISceneService, IHudBoard, IMissileView
    {
        /// <summary>The one HudBoard alive this scene, reached statically from
        /// <c>Patches/WingviewCameraPatch.cs</c> the same way other Boscali patches reach their
        /// owning controller (see <c>AutopilotLandController.Instance</c>).</summary>
        public static HudBoard Instance { get; private set; }

        private readonly HudFeedStore store = new HudFeedStore(() => Time.unscaledTime);
        private readonly BoardFrame frame = new BoardFrame();
        private readonly ExternalHudEnabler externalHud = new ExternalHudEnabler();
        private readonly WingviewCameraState wingview = new WingviewCameraState();
        private readonly ThirdPersonHudCenter hudCenterProjection = new ThirdPersonHudCenter();
        private readonly NativeFlightNumberHider flightNumberHider = new NativeFlightNumberHider();
        private readonly VisorSwayDriver visorSway = new VisorSwayDriver();
        private readonly ThreatWatch threats = new ThreatWatch();
        private readonly CountermeasureWatch countermeasures = new CountermeasureWatch();
        private readonly FuelWatch fuelWatch = new FuelWatch();
        private readonly DamageWatch damageWatch = new DamageWatch();
        private readonly SystemsFeed systemsFeed = new SystemsFeed();
        private ScopeProvider<SystemsScope> systemsProvider;
        private readonly StoresFeed storesFeed = new StoresFeed();
        private ScopeProvider<StoresScope> storesProvider;
        private readonly MissileTracker missileTracker = new MissileTracker();
        private readonly ShotBook shotBook = new ShotBook(ShotFeed.Ledger);
        private ThreatScopeFeed threatFeed;
        private ShotScopeFeed shotFeed;
        private MissileCamScopeFeed camFeed;
        private ScopeProvider<ThreatScope> threatProvider;
        private ScopeProvider<ShotScope> shotProvider;
        private ScopeProvider<MissileCamScope> camProvider;
        private readonly MissileView missileView = new MissileView();
        private readonly MissileFeedCamera missileFeed = new MissileFeedCamera();
        private readonly List<MissileTrack> missileTracks = new List<MissileTrack>(MissileTracks.MaxShown);
        // Lamp test clock: restarts whenever the native HUD shows a different aircraft (spawn, swap).
        private Aircraft bootAircraft;
        private float bootStart;
        private StatusPanel panel;
        private ThirdPersonHudCluster cluster;
        private float nextRead;
        private System.Func<int, bool> missileFlying;
        private HudSettings settings;
        /// <summary>The pilot's options as of the last 10 Hz tick (the defaults until the module configures the board); what later features of the board read.</summary>
        private BoardOptions options = BoardOptions.Default;

        // The selected target's range and closure (10 Hz reads of the same target): the third-person card's and the cockpit's NAV DATA block's.
        private readonly TargetReadout targetReadout = new TargetReadout();

        private void Awake()
        {
            Instance = this;
            threatFeed = new ThreatScopeFeed(threats);
            shotFeed = new ShotScopeFeed(shotBook, ShotFeed.Ledger);
            camFeed = new MissileCamScopeFeed(missileTracker, shotBook, ShotFeed.Ledger);
        }

        /// <summary>Hand the board the pilot's options; the next 10 Hz tick takes the first snapshot.</summary>
        public void Configure(HudSettings hudSettings) => settings = hudSettings;

        /// <summary>How many CUE stack cues are on the screen and which game boxes they stand in for (the sim probe in <c>HudAutomation</c> reads them; -1: no board).</summary>
        internal int CueDrawn => panel != null ? panel.CueDrawn : -1;
        internal int CueInCharge => panel != null ? panel.CueInCharge : -1;

        /// <summary>The live own-missile set (the sim probe in <c>HudAutomation</c> reads it).</summary>
        internal MissileTracker Tracker => missileTracker;

        private void OnEnable()
        {
            ScopeFeeds.OnError = BoscaliSummer.Core.Diagnostics.PatchGuard.Report;
            ScopeFeeds.Register(systemsProvider ??= systemsFeed.Read);
            ScopeFeeds.Register(storesProvider ??= storesFeed.Read);
            ScopeFeeds.Register(threatProvider ??= threatFeed.Read);
            ScopeFeeds.Register(shotProvider ??= shotFeed.Read);
            ScopeFeeds.Register(camProvider ??= camFeed.Read);
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
        public void Notice(string channel, HudTone tone, string text, string detail = null)
        {
            // The radio module sends the game's centre notices ("Weapon safety lock Disabled") to the ticker's "sys" channel and keeps them off the game's own box: while the CUE stack is drawing that box they are its cues instead, shown once.
            if (channel == "sys" && CueFeed.TakeNotice(text, tone)) return;
            store.Notice(channel, tone, text, detail);
        }

        public bool TryGetScreenBox(out float left, out float top, out float right, out float bottom)
        {
            left = top = right = bottom = 0f;
            return panel != null && panel.TryGetScreenBox(out left, out top, out right, out bottom);
        }

        /// <summary>The board's 10 Hz readers, in order (the stores and threat feeds read the frame the watchers before them filled): the sites a reader that throws is reported under.</summary>
        private static readonly string[] ReadSites =
        {
            "Hud.Read.Missiles", "Hud.Read.Shots", "Hud.Read.Systems", "Hud.Read.Threats", "Hud.Read.Countermeasures", "Hud.Read.Fuel", "Hud.Read.Damage",
            "Hud.Read.SystemsFeed", "Hud.Read.Stores", "Hud.Read.Nav", "Hud.Read.CombatFeeds",
        };

        private void LateUpdate()
        {
            if (Application.isBatchMode) return;
            try { Tick(); }
            catch (Exception e) { PatchGuard.Report("Hud.Tick", e); }
            // Every frame, whatever happened above (fail open): a board that is not up and fresh gives the native boxes it replaces back.
            panel?.TickNatives();
            if (panel != null)
            {
                // The visor's lag (the option Sway): zero in a pause, the map and a cinematic, and while the board is off (its corners then stay still too). The driver itself answers only in the cockpit view.
                visorSway.Tick(Time.unscaledDeltaTime, options.Sway && options.Enabled && BoscaliSummer.Core.Game.CockpitOverlayGate.NotSuppressed());
                panel.ApplySway(visorSway.X, visorSway.Y);
            }
        }

        /// <summary>The pilot's options as of this tick, handed to everything that reads them: the board, and the radar-warning watch (how long it keeps a contact after its last warning).</summary>
        private void TakeOptions()
        {
            settings?.Snapshot(ref options);
            if (panel != null) panel.Options = options;
            threats.Hold = options.ThreatHoldSeconds;
        }

        private void Tick()
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            // Orbit state objects are reused. A trip through cockpit/chase must clear old pose
            // smoothing even though the orbit postfix does not run while that view is inactive.
            if (cam == null || cam.currentState != cam.orbitState) wingview.Reset();

            // Re-checked every tick: vanilla re-disables FlightHud's canvas on every orbit/chase
            // state entry, and a forced canvas must be released the instant the condition ends.
            bool viewingOwnExternally = ExternalHudEnabler.IsOwnAircraftExternalView(out Aircraft ownAircraft);
            Aircraft missileOwner = null;
            bool viewingMissile = !viewingOwnExternally && MissileCam.IsOwnMissileView(out missileOwner, out _);
            externalHud.Tick(viewingOwnExternally || viewingMissile);

            // Same gate as the forced-canvas condition above: the third-person HUD (hidden
            // native numbers, screen-fixed cluster, releveled HUDCenter) only ever shows where
            // the native HUDCanvas is actually up. Riding a missile gets the same canvas (the board's MISSILE CAM page is its readout).
            bool canShow = ExternalHudEnabler.CanShowExternalHud(viewingOwnExternally || viewingMissile);
            bool thirdPerson = canShow && viewingOwnExternally;
            bool missileCam = canShow && viewingMissile;
            flightNumberHider.Tick(thirdPerson || missileCam, thirdPerson ? ownAircraft : missileOwner);

            // The missile feed: a camera on the last own weapon in the air, every frame (its pose must follow the weapon), wherever the HUD is up; riding a missile the screen is the feed already.
            CombatHUD nativeHud = SceneSingleton<CombatHUD>.i;
            bool hudUp = nativeHud != null && nativeHud.iconLayer != null && nativeHud.iconLayer.gameObject.activeInHierarchy;
            missileFeed.Tick(missileTracker.Missiles, hudUp && !viewingMissile, Time.unscaledTime);
            if (!thirdPerson)
            {
                // The feed's card stays in the cockpit; the flight boxes around it go the same frame third person ends (the read below runs now).
                if (missileFeed.Texture == null) cluster?.Hide();
                else if (cluster != null && cluster.FlightShown) nextRead = 0f;
            }

            if (panel == null) panel = new StatusPanel { Notice = Notice };
            if (!panel.Ensure()) return;
            if (cluster == null) cluster = new ThirdPersonHudCluster();
            cluster.Ensure();

            float clock = Time.unscaledTime;
            if (clock < nextRead)
            {
                panel.Animate(clock);
                return;
            }
            nextRead = clock + .1f;
            TakeOptions();

            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            Aircraft hudAircraft = combat != null ? combat.aircraft : null;
            float now = Time.unscaledTime;
            if (hudAircraft != bootAircraft)
            {
                bootAircraft = hudAircraft;
                bootStart = now;
                ShotFeed.Ledger.Clear();
                shotBook.Clear();
            }
            frame.Now = now;
            frame.Units = AvUnitsOf((int)PlayerSettings.unitSystem);
            frame.BootAge = hudAircraft != null ? now - bootStart : -1f;
            frame.HasTicker = store.Ticker(out frame.Ticker);
            frame.Systems = default;
            frame.Threat = ThreatPicture.Clear;
            // Each reader on its own: one that throws is reported once and leaves its own part of the board empty (every reader clears what it owns first), the others still run.
            for (int step = 0; step < ReadSites.Length; step++)
            {
                try
                {
                    switch (step)
                    {
                        case 0: missileTracker.Read(hudAircraft, missileTracks); break;
                        case 1:
                            shotBook.Track(now);
                            ShotFeed.Ledger.Expire(now, missileFlying ??= missileTracker.IsLive);
                            missileView.Tick(missileTracker.Missiles);
                            break;
                        case 2: frame.Systems = ReadSystems(combat); break;
                        case 3: frame.Threat = threats.Read(hudAircraft, frame); break;
                        case 4: countermeasures.Read(hudAircraft, frame); break;
                        case 5: fuelWatch.Read(hudAircraft, frame); break;
                        case 6: damageWatch.Read(hudAircraft, frame); break;
                        case 7: systemsFeed.Update(hudAircraft, frame, damageWatch.Rotary); break;
                        case 8: storesFeed.Update(hudAircraft, frame, combat); break;
                        case 9: NavReader.Read(hudAircraft, frame, !viewingOwnExternally && !viewingMissile, targetReadout); break;      // after the fuel watch: HOME and FUEL come from it
                        default:
                            threatFeed.Update(hudAircraft, frame);
                            shotFeed.Update(hudAircraft);
                            break;
                    }
                }
                catch (Exception e) { PatchGuard.Report(ReadSites[step], e); }
            }
            // A board that cannot be presented must not take the third-person cluster below down with it (and TickNatives takes a board that stopped presenting off the screen).
            try { panel.Present(frame); }
            catch (Exception e) { PatchGuard.Report("Hud.Present", e); }
            cluster.Board = panel.KeepClearBox;      // the third-person cluster keeps clear of the board's box

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

            // In the cockpit NAV DATA has the bottom-right corner: the card stays one slot up there.
            cluster.SetMissileFeed(missileFeed.Texture, missileFeed.Name, missileFeed.RangeM, missileFeed.Lost, raised: !thirdPerson);
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
                cluster.Present(false, AvUnitsOf((int)PlayerSettings.unitSystem), default, null, null, float.NaN, float.NaN, ThirdPersonHudLayout.TextScale(PlayerSettings.hudTextSize));
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
            if (BoscaliSummer.Core.Game.GameAccess.TryGetNightVisionState(out bool nvg, out _)) { s.NightVision = nvg; s.NvgLegend = BoscaliSummer.Core.Game.NightVisionLink.Legend; }
            s.EngineRunning = a.Ignition;
            return s;
        }

        /// <summary>Range to the first selected target and its closure rate (positive = closing).</summary>
        private void ReadTarget(Aircraft aircraft, out float range, out float closure) => targetReadout.Read(aircraft, out _, out range, out closure);

        private static NOAvionics.AvUnits AvUnitsOf(int unitSystem) =>
            unitSystem == (int)NOAvionics.AvUnits.Imperial ? NOAvionics.AvUnits.Imperial : NOAvionics.AvUnits.Metric;

        /// <summary>The shot tracker's launch hook: every own delivery the game registers earns one entry in the ledger and a record in the book (decoys do not).</summary>
        private void OnOwnLaunch(Missile missile)
        {
            WeaponInfo info = missile != null ? missile.GetWeaponInfo() : null;
            if (info == null || !ShotRules.IsDelivery(info.blastDamage, info.jammer)) return;
            float now = Time.unscaledTime;
            ShotFeed.Ledger.Launch(missile.GetInstanceID(), info.bomb, ShotBook.Short(info), now);
            shotBook.Launched(missile, info, now);
        }

        // IMissileView: the C-menu rides the live own-missile list (MissileTracker), launch order.
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
            missileFeed.Release();
            missileTracker.Release();
            missileTracks.Clear();
            store.Reset();
            visorSway.Reset();
            panel?.Destroy();
            panel = null;
            cluster?.Destroy();
            cluster = null;
            nextRead = 0;
            targetReadout.Release();
            bootAircraft = null;
            threats.Release();
            fuelWatch.Release();
            damageWatch.Release();
            ShotFeed.Ledger.Clear();
            shotBook.Clear();
            threatFeed?.Clear();
            shotFeed?.Clear();
            camFeed?.Clear();
            storesFeed.Clear();
            externalHud.Release();
            wingview.Reset();
            flightNumberHider.Dispose();
        }
        private void OnDisable()
        {
            ScopeFeeds.Unregister(systemsProvider);
            ScopeFeeds.Unregister(storesProvider);
            ScopeFeeds.Unregister(threatProvider);
            ScopeFeeds.Unregister(shotProvider);
            ScopeFeeds.Unregister(camProvider);
            systemsFeed.Clear();
            storesFeed.Clear();
            threatFeed.Clear();
            shotFeed.Clear();
            camFeed.Clear();
            missileTracker.Launched -= OnOwnLaunch;
            missileTracker.Release();
            missileView.Exit();
            missileFeed.Release();
            panel?.Hide();
            cluster?.Hide();
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
