using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Hud.Configuration;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Patches;
using BoscaliSummer.Modules.Hud.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
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
        private readonly CargoFeed cargoFeed = new CargoFeed();
        private ScopeProvider<CargoScope> cargoProvider;
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
        // The centre combat layer (WARNING banner, THREAT RING, shot callouts) and the aircraft it is up for (null: down).
        private CombatAlerts alerts;
        private Aircraft alertAircraft;
        // The visor's brow rail and frame furniture (cockpit view only).
        private BrowHud brow;
        // The segmented fuel and throttle arcs (cockpit view only); arcsAt / arcsExternal say when and what the last complete Tick saw, so a Tick that stops running gives the game's gauges back (fail open).
        private ArcsHud arcs;
        private float arcsAt = float.NegativeInfinity;
        private bool arcsExternal;
        // The visor's rim (corner lines, side ticks, the etched line) and its power-on (2026-11-16, "HELMET ON"): the boot runs once per aircraft the pilot takes the seat of (full the first time of the session, a re-sync after),
        // is cut short at once by a real warning or by anything that is not the cockpit view, and while it runs sets how far the zones below have come alive (see StepBoot).
        private VisorRim rim;
        private readonly VisorBootHud boot = new VisorBootHud();
        private Aircraft bootedFor, identityFor;
        private bool everBooted, bootApplied, bootWarning;
        private string identityCallsign = "", identityAirframe = "";
        private float nextIdentity;
        private float nextRead;
        private System.Func<int, bool> missileFlying;
        private HudSettings settings;
        /// <summary>The pilot's options as of the last 10 Hz tick (the defaults until the module configures the board); what later features of the board read.</summary>
        private BoardOptions options = BoardOptions.Default;

        // The selected target's range and closure (10 Hz reads of the same target): the third-person TGT card's.
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

        /// <summary>The centre combat layer (the sim probe in <c>HudAutomation</c> reads it; null until first shown).</summary>
        internal CombatAlerts Alerts => alerts;

        /// <summary>The visor's brow rail (the sim probe in <c>HudAutomation</c> reads it; null until first shown).</summary>
        internal BrowHud Brow => brow;

        /// <summary>The segmented fuel and throttle arcs (the sim probe in <c>HudAutomation</c> reads it; null until first wanted).</summary>
        internal ArcsHud Arcs => arcs;

        /// <summary>The visor's rim and power-on (the sim probe in <c>HudAutomation</c> reads them).</summary>
        internal VisorRim Rim => rim;
        internal VisorBootHud BootHud => boot;
        /// <summary>Why the last boot ended: "finished", "warning" or "not allowed" (the sim probe reads it).</summary>
        internal string BootEnd { get; private set; } = "";

        /// <summary>The sim's hook: start the power-on now in <paramref name="mode"/> on the HUD's aircraft, with the real state of this moment. False: no aircraft, or not the cockpit view.</summary>
        internal bool RestartBoot(BootMode mode)
        {
            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            Aircraft aircraft = combat != null ? combat.aircraft : null;
            if (aircraft == null || aircraft.disabled || arcsExternal) return false;
            StartBoot(aircraft, mode);
            return true;
        }

        private void OnEnable()
        {
            ScopeFeeds.OnError = BoscaliSummer.Core.Diagnostics.PatchGuard.Report;
            ScopeFeeds.Register(systemsProvider ??= systemsFeed.Read);
            ScopeFeeds.Register(storesProvider ??= storesFeed.Read);
            ScopeFeeds.Register(cargoProvider ??= cargoFeed.Read);
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
            // A mission's hints ("task") are task lines on the CUE stack while it is drawn.
            if (channel == "task" && CueFeed.TakeTask(text)) return;
            // The THREAT page's "ARH MISSILE INBOUND" and "LOCK - ..." warnings are the centre banner's words already (its advice too): said once, there.
            if (alerts != null && alerts.Shown && tone == HudTone.Warning && (channel == "missile" || channel == "rwr")) return;
            store.Notice(channel, tone, text, detail);
        }

        public bool TryGetScreenBox(out float left, out float top, out float right, out float bottom)
        {
            left = top = right = bottom = 0f;
            return panel != null && panel.TryGetScreenBox(out left, out top, out right, out bottom);
        }

        public bool ShowsWing => panel != null && panel.ShowsWing;

        /// <summary>The board's 10 Hz readers, in order (the stores and threat feeds read the frame the watchers before them filled): the sites a reader that throws is reported under.</summary>
        private static readonly string[] ReadSites =
        {
            "Hud.Read.Missiles", "Hud.Read.Shots", "Hud.Read.Systems", "Hud.Read.Threats", "Hud.Read.Countermeasures", "Hud.Read.Fuel", "Hud.Read.Damage",
            "Hud.Read.SystemsFeed", "Hud.Read.Stores", "Hud.Read.CombatFeeds", "Hud.Read.Cargo",
        };

        private void LateUpdate()
        {
            if (Application.isBatchMode) return;
            try { Tick(); }
            catch (Exception e) { PatchGuard.Report("Hud.Tick", e); }
            // The centre combat layer's blink, ring and pop, every frame (its data comes with the 10 Hz read).
            try { alerts?.Frame(alertAircraft, threats, this, Time.unscaledTime); }
            catch (Exception e) { alerts?.Hide(); PatchGuard.Report("Hud.Alerts.Frame", e); }      // fail open: the game's own missile list comes back
            // The power-on's clock first: how far each zone has come alive is set before they draw this frame. A throw ends the boot and snaps everything to flying state.
            try { StepBoot(); }
            catch (Exception e) { boot.Abort(); ReleaseBoot(); PatchGuard.Report("Hud.Boot", e); }
            try { brow?.Frame(); }
            catch (Exception e) { brow?.Hide(); PatchGuard.Report("Hud.Brow.Frame", e); }
            try { rim?.Frame(); }
            catch (Exception e) { rim?.Hide(); PatchGuard.Report("Hud.Rim.Frame", e); }
            // The arcs, every frame and BEFORE anything below can fail: they hide the game's own two gauges only in the frame they are drawn, and give them back in the frame they are not wanted.
            try { arcs?.Frame(ArcsWanted(out Aircraft arcsAircraft), arcsAircraft); }
            catch (Exception e) { arcs?.Fail(); PatchGuard.Report("Hud.Arcs.Frame", e); }
            // Every frame, whatever happened above (fail open): a board that is not up and fresh gives the native boxes it replaces back.
            panel?.TickNatives();
            if (panel != null)
            {
                // The visor's lag (the option Sway): zero in a pause, the map and a cinematic, and while the board is off (its corners then stay still too). The driver itself answers only in the cockpit view.
                visorSway.Tick(Time.unscaledDeltaTime, options.Sway && options.Enabled && BoscaliSummer.Core.Game.CockpitOverlayGate.NotSuppressed());
                panel.ApplySway(visorSway.X, visorSway.Y);
            }
        }

        /// <summary>
        /// The arcs may draw this frame: the switch (read live, so the game's gauges are back the frame it goes off), a live HUD aircraft, the cockpit view (the external views and a missile ride have the
        /// third-person cluster and its own hiding of these gauges), an overlay may show (no pause, map or cinematic, the native HUD up), and the last complete Tick is under half a second old.
        /// </summary>
        private bool ArcsWanted(out Aircraft aircraft)
        {
            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            aircraft = combat != null ? combat.aircraft : null;
            if (aircraft == null || aircraft.disabled || arcsExternal || NativeTakeover.Stale(Time.unscaledTime, arcsAt)) return false;
            if (!(settings != null ? settings.Arcs.Value : options.Arcs)) return false;
            // The power-on hands the arcs over when they start to fill: until then the game's own gauges are the ones on the screen (never a frame with neither).
            if (boot.Active && boot.Progress(BootGroup.Arcs) <= 0f) return false;
            return BoscaliSummer.Core.Game.CockpitOverlayGate.Open();
        }

        /// <summary>The power-on may run this frame: the option (read live), a live HUD aircraft, the cockpit view, an overlay may show (no pause, map or cinematic) and the pilot has not asked for reduced motion.</summary>
        private bool BootAllowed(Aircraft aircraft, bool external) =>
            (settings != null ? settings.Boot.Value : options.Boot) && aircraft != null && !aircraft.disabled && !external && !NOAvionics.AvFxDriver.ReducedMotion &&
            BoscaliSummer.Core.Game.CockpitOverlayGate.Open();

        /// <summary>Every frame: advance the boot, cut it when it is no longer allowed or a real warning is up, and hand each zone how far it has come alive; when it ends give every zone its flying state back, once.</summary>
        private void StepBoot()
        {
            if (!boot.Active) { if (bootApplied) ReleaseBoot(); return; }
            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            Aircraft aircraft = combat != null ? combat.aircraft : null;
            bool warning = bootWarning || (alerts != null && alerts.WarningUp);
            bool allowed = BootAllowed(aircraft, arcsExternal) && aircraft == bootedFor;
            if (!boot.Step(Time.unscaledTime, allowed, warning))
            {
                BootEnd = warning ? "warning" : !allowed ? "not allowed" : "finished";      // the sim probe reads why the last boot ended
                ReleaseBoot();
                return;
            }
            if (brow != null) brow.Boot = boot.Progress(BootGroup.Brow);
            if (rim != null) rim.Boot = boot.Progress(BootGroup.Rim);
            if (arcs != null) arcs.Boot = boot.Progress(BootGroup.Arcs);
            float left = boot.Alpha(BootGroup.Left);
            panel?.ApplyBoot(boot.Alpha(BootGroup.Board), left);
            LeftHudStack.BootAlpha = left;      // the COMMS block (the Chatter module's) follows the left column
            bootApplied = true;
        }

        /// <summary>The boot is over (done, cut or failed): every zone as in flight.</summary>
        private void ReleaseBoot()
        {
            if (brow != null) brow.Boot = 1f;
            if (rim != null) rim.Boot = 1f;
            if (arcs != null) arcs.Boot = 1f;
            panel?.ApplyBoot(1f, 1f);
            LeftHudStack.BootAlpha = 1f;
            bootApplied = false;
        }

        /// <summary>The power-on, with the real state of this moment: what each self-test line will say is read here, once.</summary>
        private void StartBoot(Aircraft aircraft, BootMode mode)
        {
            var inputs = new BootInputs
            {
                Hmd = aircraft != null,
                HasNet = GameManager.gameState == GameState.SinglePlayer || GameManager.gameState == GameState.Multiplayer,
                Online = GameManager.gameState == GameState.Multiplayer,
                HasRadar = frame.Systems.RadarEmitting.HasValue, RadarOn = frame.Systems.RadarEmitting == true,
                HasFuel = frame.Fuel.Valid, Fuel = frame.Fuel.Level, Bingo = frame.Fuel.Bingo,
                Field = frame.Fuel.Field, FieldM = frame.Fuel.FieldM, Metric = frame.Units == NOAvionics.AvUnits.Metric,
                Cm = frame.Cm, CmCount = frame.CmCount,
            };
            if (ScopeFeeds.TryRead(out StoresScope stores)) { inputs.Stations = stores.StationCount; inputs.HasSafety = stores.Selected >= 0; inputs.Safe = stores.Safe; }
            RefreshIdentity(aircraft, true);
            boot.Begin(mode, Time.unscaledTime, inputs, identityCallsign);
            bootedFor = aircraft;
            everBooted = true;
        }

        /// <summary>The pilot's callsign and the airframe's type for the rim and the closing line: read when the aircraft changes, and every couple of seconds while the callsign is still unknown.</summary>
        private void RefreshIdentity(Aircraft aircraft, bool force)
        {
            float now = Time.unscaledTime;
            if (!force && aircraft == identityFor && (identityCallsign != "PILOT" || now < nextIdentity)) return;
            identityFor = aircraft;
            identityCallsign = VisorIdentity.Callsign();
            identityAirframe = VisorIdentity.Airframe(aircraft);
            nextIdentity = now + 2f;
        }

        /// <summary>The pilot's options as of this tick, handed to everything that reads them: the board, and every backdrop of the mod's HUD (<see cref="HudSprites.Backdrop"/>).</summary>
        private void TakeOptions()
        {
            settings?.Snapshot(ref options);
            if (panel != null) panel.Options = options;
            HudSprites.Backdrop = ScopeLayout.CloudAlpha * options.Shade;      // the one backdrop every HUD block of the mod stands on
            BoscaliSummer.Core.Game.VanillaHudStyle.SetEdge(options.TextEdge);      // the thin dark edge on every glow material
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
            arcsExternal = viewingOwnExternally || viewingMissile;      // any external view or missile ride: the arcs are not drawn, the game's gauges are the hider's
            arcsAt = Time.unscaledTime;

            // The missile feed: a camera on the last own weapon in the air, every frame (its pose must follow the weapon), wherever the HUD is up; riding a missile the screen is the feed already.
            CombatHUD nativeHud = SceneSingleton<CombatHUD>.i;
            bool hudUp = nativeHud != null && nativeHud.iconLayer != null && nativeHud.iconLayer.gameObject.activeInHierarchy;
            missileFeed.Tick(missileTracker.Missiles, (settings?.PageMissileCam.Value ?? options.PageMissileCam) && hudUp && !viewingMissile, Time.unscaledTime);
            if (!thirdPerson)
            {
                // The feed and target cards stay in the cockpit; the flight boxes around them go the same frame third person ends (the read below runs now).
                if (missileFeed.Texture == null && !(cluster != null && cluster.TargetCardShown)) cluster?.Hide();      // the cockpit target card (a lock) keeps the root up; the next read retires it with the lock
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
                        case 10: cargoFeed.Update(hudAircraft); break;
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

            // The centre combat layer: its switch, a live aircraft, and only where an overlay may show (no pause, map or cinematic). Independent of the board's own switch.
            try
            {
                bool showAlerts = options.CentreAlerts && hudAircraft != null && !hudAircraft.disabled && BoscaliSummer.Core.Game.CockpitOverlayGate.NotSuppressed();
                alerts ??= new CombatAlerts();
                alerts.Read(showAlerts && alerts.Ensure(), hudAircraft, frame, threats, now);
                alertAircraft = showAlerts ? hudAircraft : null;
            }
            catch (Exception e) { PatchGuard.Report("Hud.Alerts", e); }

            // The visor's brow rail and furniture: cockpit view only (the external views have the third-person boxes, a missile ride has no nose to take bearings from). The lane under it rises where it is not drawn.
            try
            {
                bool showBrow = options.Brow && hudAircraft != null && !hudAircraft.disabled && !thirdPerson && !missileCam && BoscaliSummer.Core.Game.CockpitOverlayGate.NotSuppressed();
                brow ??= new BrowHud();
                int scope = options.ScopeMode == BoardScopeMode.Auto ? 0 : options.ScopeMode == BoardScopeMode.Manual ? 1 : 2;
                brow.Read(showBrow && brow.Ensure(), hudAircraft, frame, scope, this);
                TopCentre.Lift = thirdPerson || missileCam ? TopCentre.ExternalLift : 0f;
                // The rim shares the brow's switch and its view: the frame of the same glass.
                rim ??= new VisorRim();
                RefreshIdentity(hudAircraft, false);
                rim.Read(showBrow && rim.Ensure(), identityCallsign, identityAirframe, hudAircraft != null ? now - bootStart : -1f, this);
            }
            catch (Exception e) { PatchGuard.Report("Hud.Brow", e); }

            // The visor's power-on: the first time the cockpit HUD is up for an aircraft (full the first time of the session, a re-sync after a respawn or an aircraft change). A real warning on the screen (a missile, a fire)
            // is what cuts a running boot; it is seen here at 10 Hz and again by the combat layer's own board every frame (StepBoot).
            try
            {
                bootWarning = frame.Threat.Missiles > 0 || (frame.Damage.Flags & DamageFlags.Fire) != 0;
                if (hudAircraft == null) bootedFor = null;
                else if (hudAircraft != bootedFor)
                {
                    if (boot.Active) { boot.Abort(); ReleaseBoot(); }
                    if (BootAllowed(hudAircraft, thirdPerson || missileCam))
                    {
                        if (bootWarning || (alerts != null && alerts.WarningUp)) bootedFor = hudAircraft;      // a warning is up as the pilot sits down: no boot for this aircraft
                        else StartBoot(hudAircraft, VisorBoot.ModeFor(everBooted));
                    }
                }
            }
            catch (Exception e) { PatchGuard.Report("Hud.BootStart", e); }

            // The arcs' data (fuel level, the BINGO need); the arcs themselves run every frame in LateUpdate.
            try
            {
                arcs ??= new ArcsHud();
                arcs.Read(hudAircraft, frame);
            }
            catch (Exception e) { PatchGuard.Report("Hud.Arcs", e); }

            // The card stands in the left column in the external views, and in the cockpit while a target is locked (the picture is the cockpit tac screen's own camera, so nothing native is touched).
            if (hudAircraft != null) TargetCamLook.Apply(hudAircraft.targetCam);      // the cockpit tac screen and the card share vanilla's camera
            Texture cameraTexture = null;
            string cameraCode = null;
            float range = float.NaN, closure = float.NaN, aspect = float.NaN;
            Aircraft camOwner = viewingOwnExternally ? ownAircraft : hudAircraft;
            if (camOwner != null && camOwner.targetCam != null &&
                BoscaliSummer.Core.Game.NativeCamera.ReadMode(camOwner.targetCam) != TargetCam.CamMode.landingMode &&
                BoscaliSummer.Core.Game.NativeCamera.TryGet(camOwner, out Camera camera, out string mode) &&
                camera.targetTexture != null && camera.targetTexture.IsCreated())
            {
                bool seenOne = targetReadout.Read(camOwner, out Unit seen, out range, out closure) && seen != null;
                // External: the card is up whenever the pod has a picture (its header names the camera mode with no target). Cockpit: only while a target is locked.
                // The cockpit card has no canvas gate of its own: off while paused, on the big map, in cinematic mode and on a missile ride.
                if (viewingOwnExternally || (seenOne && !viewingMissile && BoscaliSummer.Core.Game.CockpitOverlayGate.Open()))
                {
                    cameraTexture = camera.targetTexture;
                    cameraCode = seenOne ? targetReadout.NameOf(seen) : mode;
                    if (seenOne) aspect = TargetReadout.Aspect(camOwner, seen);
                }
            }
            // hudAircraft, not ownAircraft: the latter is null in the cockpit, which left the overlay off after any trip outside.
            if (hudAircraft != null) TargetCamLook.SetOverlay(hudAircraft.targetCam, cameraTexture == null || !viewingOwnExternally);      // the cockpit's own tac screen keeps its text even while the card shows

            // The feed column holds the bottom-right corner in every view (NAV DATA is gone, 2026-10-21).
            missileFeed.Guidance(out float feedRange, out float feedClosure, out float feedTti);
            cluster.SetMissileFeed(missileFeed.Texture, missileFeed.Name, feedRange, missileFeed.Lost, feedClosure, feedTti);
            cluster.CardNativeText = !viewingOwnExternally;      // the cockpit's tac screen prints its own text on the picture
            cluster.CardAspectDeg = aspect;
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
                cluster.Present(false, AvUnitsOf((int)PlayerSettings.unitSystem), default, cameraTexture, cameraCode, range, closure, ThirdPersonHudLayout.TextScale(PlayerSettings.hudTextSize));      // the cockpit: the target card only (a lock), no flight boxes
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
        private static NOAvionics.AvUnits AvUnitsOf(int unitSystem) =>
            unitSystem == (int)NOAvionics.AvUnits.Imperial ? NOAvionics.AvUnits.Imperial : NOAvionics.AvUnits.Metric;

        /// <summary>The shot tracker's launch hook: every own delivery the game registers earns one entry in the ledger and a record in the book (decoys do not).</summary>
        private void OnOwnLaunch(Missile missile)
        {
            WeaponInfo info = missile != null ? missile.GetWeaponInfo() : null;
            if (info == null || !ShotRules.IsDelivery(Mathf.Max(info.blastDamage, missile.GetYield()), info.jammer)) return;      // WeaponInfo.blastDamage reads 0 for every missile on the 0.34.6 build: the warhead's own yield is the number
            float now = Time.unscaledTime;
            ShotFeed.Ledger.Launch(missile.GetInstanceID(), info.bomb, ShotBook.Short(info), now);
            shotBook.Launched(missile, info, now);
        }
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
            alerts?.Destroy();
            alerts = null;
            alertAircraft = null;
            brow?.Destroy();
            brow = null;
            arcs?.Destroy();
            arcs = null;
            rim?.Destroy();
            rim = null;
            boot.Destroy();
            bootedFor = identityFor = null;
            everBooted = bootApplied = bootWarning = false;
            identityCallsign = identityAirframe = "";
            TopCentre.Lift = 0f;
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
            cargoFeed.Clear();
            externalHud.Release();
            wingview.Reset();
            flightNumberHider.Dispose();
        }
        private void OnDisable()
        {
            ScopeFeeds.Unregister(systemsProvider);
            ScopeFeeds.Unregister(storesProvider);
            ScopeFeeds.Unregister(cargoProvider);
            ScopeFeeds.Unregister(threatProvider);
            ScopeFeeds.Unregister(shotProvider);
            ScopeFeeds.Unregister(camProvider);
            systemsFeed.Clear();
            storesFeed.Clear();
            cargoFeed.Clear();
            threatFeed.Clear();
            shotFeed.Clear();
            camFeed.Clear();
            missileTracker.Launched -= OnOwnLaunch;
            missileTracker.Release();
            missileView.Exit();
            missileFeed.Release();
            panel?.Hide();
            cluster?.Hide();
            alerts?.Hide();
            alertAircraft = null;
            brow?.Hide();
            rim?.Hide();
            boot.Abort();
            ReleaseBoot();
            arcs?.Down();
            TopCentre.Lift = 0f;
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
