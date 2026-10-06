using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Hud.Configuration;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;
namespace BoscaliSummer.Modules.Hud.Presentation
{
    // Service and lifecycle only. Feed handles contain data; the panel owns a fixed widget pool
    // parented directly under the native weapons panel (see StatusPanel).
    // Native CameraStateManager runs at order 2 and owns camera-state entry/visibility.
    [DefaultExecutionOrder(1000)]
    internal sealed class HudBoard : MonoBehaviour, ISceneService, IHudBoard
    {
        /// <summary>The one HudBoard alive this scene, reached statically from
        /// <c>Patches/WingviewCameraPatch.cs</c> the same way other Boscali patches reach their
        /// owning controller (see <c>AutopilotLandController.Instance</c>).</summary>
        public static HudBoard Instance { get; private set; }

        private readonly HudFeedStore store = new HudFeedStore(() => Time.unscaledTime);
        private readonly List<Channel> channels = new List<Channel>(HudLayout.MaxChannels);
        private readonly HudMessage[] snapshot = new HudMessage[HudLayout.MaxRows];
        private readonly ExternalHudEnabler externalHud = new ExternalHudEnabler();
        private readonly WingviewCameraState wingview = new WingviewCameraState();
        private readonly ThirdPersonHudCenter hudCenterProjection = new ThirdPersonHudCenter();
        private readonly NativeFlightNumberHider flightNumberHider = new NativeFlightNumberHider();
        private HudSettings settings;
        private StatusPanel panel;
        private ThirdPersonHudCluster cluster;
        private float nextRead;

        private void Awake() => Instance = this;

        public void Configure(HudSettings config, ManualLogSource logger)
        {
            settings = config;
            SubscribeProjection();
        }

        private void OnEnable() => SubscribeProjection();

        private void SubscribeProjection()
        {
            if (settings == null || !isActiveAndEnabled) return;
            hudCenterProjection.Subscribe(
                () => isActiveAndEnabled && ExternalHudEnabler.CanShowExternalHud(
                    Enabled && settings.ExternalHud.Value, ExternalHudEnabler.IsOwnAircraftExternalView(out _)),
                () => ExternalHudEnabler.IsOwnAircraftExternalView(out Aircraft aircraft) ? aircraft : null);
        }

        /// <summary>Called from <c>Patches/WingviewCameraPatch.cs</c>'s postfix every
        /// <c>CameraOrbitState.UpdateState</c> while installed.</summary>
        public void TickWingview(CameraStateManager cam, ref float panView, ref float tiltView,
            float viewDistAdjust, float lookAtTargetLerp)
        {
            bool enabled = settings != null && !Application.isBatchMode && settings.Wingview.Value;
            bool lookAhead = settings != null && settings.WingviewLookAhead.Value;
            wingview.Tick(enabled, lookAhead, cam, ref panView, ref tiltView, viewDistAdjust, lookAtTargetLerp);
        }
        public IReadOnlyList<IHudChannel> Channels => channels;
        public void DeclareChannel(string key, string label)
        {
            if (string.IsNullOrEmpty(key) || channels.Count >= HudLayout.MaxChannels) return;
            foreach (Channel channel in channels) if (channel.Key == key) return;
            channels.Add(new Channel(this, key, string.IsNullOrEmpty(label) ? key : label));
        }
        public IHudLine Acquire(string owner, string channel, string key) => store.Acquire(owner, channel, key);
        public void Notice(string channel, HudTone tone, string text, string detail = null)
        {
            if (NoticesEnabled && ChannelEnabled(channel)) store.Notice(channel, tone, text, detail, NoticeSeconds);
        }
        private bool ChannelEnabled(string channel) => settings == null || settings.ChannelEnabled(channel);
        public bool Enabled
        {
            get => settings == null || settings.Enabled.Value;
            set
            {
                if (settings != null) settings.Enabled.Value = value;
            }
        }

        public int ScaleStep
        {
            get => settings != null ? HudLayout.ClampScale(settings.ScaleStep.Value) : 1;
            set
            {
                if (settings == null) return;
                settings.ScaleStep.Value = HudLayout.ClampScale(value);
            }
        }

        public int OpacityStep
        {
            get => settings != null ? HudLayout.ClampOpacity(settings.OpacityStep.Value) : 1;
            set
            {
                if (settings == null) return;
                settings.OpacityStep.Value = HudLayout.ClampOpacity(value);
            }
        }

        public int MaxRows
        {
            get => settings != null ? HudLayout.ClampRows(settings.MaxRows.Value) : 4;
            set
            {
                if (settings == null) return;
                settings.MaxRows.Value = HudLayout.ClampRows(value);
            }
        }

        public bool NoticesEnabled
        {
            get => settings == null || settings.Notices.Value;
            set
            {
                if (settings == null || settings.Notices.Value == value) return;
                settings.Notices.Value = value;
                if (!value) store.ClearNotices();
            }
        }

        public float NoticeSeconds
        {
            get => settings != null
                ? HudLayout.ClampNoticeSeconds(settings.NoticeSeconds.Value)
                : HudLayout.DefaultNoticeSeconds;
            set
            {
                if (settings == null) return;
                settings.NoticeSeconds.Value = HudLayout.ClampNoticeSeconds(value);
            }
        }

        public int Contrast { get => settings?.Contrast.Value ?? 1; set { if (settings != null) settings.Contrast.Value = Mathf.Clamp(value, 0, 2); } }
        public bool ShowDetails { get => settings == null || settings.ShowDetails.Value; set { if (settings != null) settings.ShowDetails.Value = value; } }
        public int OffsetX { get => settings?.OffsetX.Value ?? 0; set { if (settings != null) settings.OffsetX.Value = Mathf.Clamp(value, -600, 600); } }
        public int OffsetY { get => settings?.OffsetY.Value ?? 0; set { if (settings != null) settings.OffsetY.Value = Mathf.Clamp(value, -600, 600); } }

        public bool CameraFeedEnabled
        {
            get => settings == null || settings.ThirdPersonCameraEnabled.Value;
            set { if (settings != null) settings.ThirdPersonCameraEnabled.Value = value; }
        }

        public void ResetLayout()
        {
            Enabled = ShowDetails = NoticesEnabled = true;
            ScaleStep = Contrast = 1;
            OpacityStep = 1;
            MaxRows = 4;
            OffsetX = OffsetY = 0;
            NoticeSeconds = HudLayout.DefaultNoticeSeconds;
        }

        private void LateUpdate()
        {
            if (settings == null || Application.isBatchMode) return;

            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            // Orbit state objects are reused. A trip through cockpit/chase must clear old pose
            // smoothing even though the orbit postfix does not run while that view is inactive.
            if (cam == null || cam.currentState != cam.orbitState) wingview.Reset();

            // Re-checked every tick regardless of Enabled/below: vanilla re-disables FlightHud's
            // canvas on every orbit/chase state entry, and a forced canvas must be released the
            // instant the condition ends even if the board itself just got switched off.
            bool viewingOwnExternally = ExternalHudEnabler.IsOwnAircraftExternalView(out Aircraft ownAircraft);
            externalHud.Tick(Enabled && settings.ExternalHud.Value, viewingOwnExternally);

            // Same gate as the forced-canvas condition above: the third-person HUD (hidden
            // native numbers, screen-fixed cluster, releveled HUDCenter) only ever shows where
            // the native HUDCanvas is actually up.
            bool thirdPerson = ExternalHudEnabler.CanShowExternalHud(Enabled && settings.ExternalHud.Value, viewingOwnExternally);
            flightNumberHider.Tick(thirdPerson, ownAircraft);
            if (!thirdPerson) cluster?.Hide();

            if (!Enabled)
            {
                panel?.Hide();
                cluster?.Hide();
                return;
            }

            if (panel == null) panel = new StatusPanel();
            if (!panel.Ensure()) return;
            if (cluster == null) cluster = new ThirdPersonHudCluster();
            cluster.Ensure();

            if (Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + .1f;

            foreach (Channel channel in channels) if (!channel.Enabled) store.Mute(channel.Key);
            int count = store.Snapshot(snapshot, MaxRows, ChannelEnabled, NoticesEnabled);
            panel.Present(snapshot, count, ShowDetails, Contrast, OpacityStep, HudLayout.Scale(ScaleStep), OffsetX, OffsetY);

            // The cockpit already has its own native tac screen for this feed; the card only
            // earns its space in the external views this module can force the HUD into.
            Texture cameraTexture = null;
            string cameraCode = null;
            string cameraRange = null;
            if (CameraFeedEnabled && viewingOwnExternally && ownAircraft != null && ownAircraft.targetCam != null &&
                BoscaliSummer.Core.Game.NativeCamera.ReadMode(ownAircraft.targetCam) != TargetCam.CamMode.landingMode &&
                BoscaliSummer.Core.Game.NativeCamera.TryGet(ownAircraft, out Camera camera, out string mode) &&
                camera.targetTexture != null && camera.targetTexture.IsCreated())
            {
                cameraTexture = camera.targetTexture;
                cameraCode = mode;
                cameraRange = RangeToSelectedTarget(ownAircraft);
            }

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
                float fuel = Mathf.Clamp01(ownAircraft.fuelLevel);
                float throttle = Mathf.Clamp01(ownAircraft.GetInputs()?.throttle ?? 0f);

                cluster.Present(true, AvUnitsOf((int)PlayerSettings.unitSystem),
                    speed, altitude, climb, heading, mach, ownAircraft.gForce, fuel, throttle,
                    cameraTexture != null, cameraTexture, cameraCode, cameraRange);
            }
            else
            {
                cluster.Present(false, NOAvionics.AvUnits.Metric, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, false, null, null, null);
            }
        }

        private readonly char[] rangeBuf = new char[24];

        private string RangeToSelectedTarget(Aircraft aircraft)
        {
            if (aircraft.weaponManager == null) return null;
            var targets = aircraft.weaponManager.GetTargetList();
            if (targets == null || targets.Count == 0 || targets[0] == null) return null;
            float distance = Vector3.Distance(aircraft.transform.position, targets[0].transform.position);
            NOAvionics.AvUnits units = AvUnitsOf((int)PlayerSettings.unitSystem);
            int len = NOAvionics.AvUnitTable.DistanceReading(rangeBuf, 0, distance, units);
            return new string(rangeBuf, 0, len);
        }

        private static NOAvionics.AvUnits AvUnitsOf(int unitSystem) =>
            unitSystem == (int)NOAvionics.AvUnits.Imperial ? NOAvionics.AvUnits.Imperial : NOAvionics.AvUnits.Metric;

        public void ResetForScene()
        {
            store.Reset();
            panel?.Destroy();
            panel = null;
            cluster?.Destroy();
            cluster = null;
            nextRead = 0;
            externalHud.Release();
            wingview.Reset();
            flightNumberHider.Dispose();
        }
        private void OnDisable()
        {
            panel?.Hide();
            cluster?.Hide();
            externalHud.Release();
            wingview.Reset();
            flightNumberHider.Tick(false, null);
            hudCenterProjection.Unsubscribe();
        }
        private void OnDestroy()
        {
            ResetForScene();
            hudCenterProjection.Unsubscribe();
            if (Instance == this) Instance = null;
        }
        private sealed class Channel : IHudChannel
        {
            private readonly HudBoard board;

            internal Channel(HudBoard owner, string key, string label)
            {
                board = owner;
                Key = key;
                Label = label;
            }

            public string Key { get; }
            public string Label { get; }
            public bool Enabled => board.settings == null || board.settings.ChannelEnabled(Key);

            public void Toggle()
            {
                if (board.settings == null) return;
                board.settings.SetChannel(Key, !Enabled);
            }
        }
    }
}
