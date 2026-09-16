using System;
using System.Globalization;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Features.Support.Visuals;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The station uplink: a full-screen mission-control feed from the spy imager.
    ///
    /// <para>A 16:10 EO/IR video fills the centre with corner brackets, crosshair, north arrow,
    /// scale bar, frame counter and a LIVE light; telemetry runs down the left rail; the radar
    /// product and four taskings (radar scan, ELINT, rod, EMP) that fire at the crosshair run
    /// down the right; a pass bar sits under the feed. Drag or WASD slews the sensor with gimbal
    /// lag (quicker with gyros), the wheel or Q/E zooms down to the imager's GSD, a double-click
    /// centres, C looks straight down the ground track, Esc or a right-click closes.</para>
    ///
    /// <para>The uplink owns the mouse and keyboard while it is up: its own overlay canvas blocks
    /// uGUI, a Harmony guard stops the map from panning or taking orders, and Rewired keyboard
    /// input and the pause keybind are held and released a frame after closing, so the key that
    /// closed it reaches nothing else. It closes itself when the map closes or the game pauses.
    /// Everything shown is client-local; a tasking is an ordinary support request.</para>
    /// </summary>
    internal sealed class PlatformUplink : MonoBehaviour
    {
        private const float FrameWidth = 1920f;
        private const float FrameHeight = 1080f;
        private const float VideoX = 320f;
        private const float VideoTop = -84f;
        private const float VideoWidth = 1280f;
        private const float VideoHeight = 800f;
        private const float LeftX = 24f;
        private const float RightX = 1616f;
        private const float RailWidth = 280f;
        private const int FeedWidth = 768;
        private const int FeedHeight = 480;
        private const float FramesPerSecond = 8f;
        private const float SlewTimeConstant = 0.35f;
        private const float GyroSlewTimeConstant = 0.15f;
        private const float KeySlewFraction = 0.55f;
        private const float TextInterval = 0.1f;
        private const float ClickSlop = 8f;
        private const float DoubleClickSeconds = 0.3f;
        private const float ScaleBarPixels = 200f;
        private const int SortingOrder = 30000;
        private static readonly double FieldOfRegard = 62.0 * OrbitMath.Deg;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private static readonly float[] Footprints = { 24000f, 12000f, 6000f, 3000f, 1500f, 750f, 400f };
        private static readonly string[] Keys =
        {
            "STATION", "ORBIT", "PHASE", "EL / AZ", "OFF-NADIR", "SLANT", "GSD", "FOV", "AIM",
            "SUB-POINT", "HEADING", "ENERGY", "MODE", "LINK"
        };
        private static readonly SupportActionId[] Tasks =
            { SupportActionId.Recon, SupportActionId.ElintSweep, SupportActionId.Artillery, SupportActionId.Emp };
        private static readonly KeyCode[] TaskKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };

        private static bool visible;
        private static int closedFrame = -10;

        /// <summary>True while the uplink is on screen and for one frame after it closes, so the key
        /// or click that closed it does not also reach the map or the pause menu.</summary>
        public static bool IsOpen => visible || Time.frameCount <= closedFrame + 1;

        private SupportManager support;
        private PlatformProducts products;
        private Canvas canvas;
        private GameObject content;
        private RectTransform frame;
        private AvStyled.DataBar bar;
        private RawImage video;
        private RawImage scanlines;
        private Texture2D scanlineTexture;
        private Image veil;
        private Image liveLight;
        private TMP_Text liveLabel;
        private TMP_Text slate;
        private TMP_Text hudTopLeft, hudTopRight, hudBottomLeft, hudBottomRight;
        private RectTransform north;
        private TMP_Text scaleLabel;
        private Image[] crosshair;
        private Image passFill;
        private TMP_Text passLeft, passRight;
        private readonly TMP_Text[] values = new TMP_Text[Keys.Length];
        private Image energyFill, fuelFill;
        private TMP_Text energyText, fuelText, rodsText;
        private RawImage product;
        private TMP_Text productCaption, productDetail;
        private readonly AvButton[] taskButtons = new AvButton[Tasks.Length];
        private readonly TMP_Text[] taskStatus = new TMP_Text[Tasks.Length];
        private AvButton deliverButton;
        private TMP_Text status;

        private SatelliteImager imager;
        private double aimX, aimZ, commandX, commandZ;
        private float aimHeight;
        private float nextHeight;
        private float nextText;
        private int footprintIndex = 2;
        private bool gsdLimited;
        private bool live;
        private bool dragging;
        private Vector3 lastMouse;
        private Vector3 rightPress;
        private Vector3 lastClickPosition;
        private float lastClickTime = -10f;

        private bool inputHeld;
        private bool keyboardTouched;
        private bool keyboardWas;
        private bool pauseWas;

        public bool Visible => visible && content != null && content.activeSelf;

        public static PlatformUplink Create(SupportManager manager, PlatformProducts products)
        {
            var go = new GameObject("BoscaliStationUplink", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.enabled = false;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(FrameWidth, FrameHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            PlatformUplink uplink = go.AddComponent<PlatformUplink>();
            uplink.support = manager;
            uplink.products = products;
            uplink.canvas = canvas;
            uplink.Build();
            uplink.content.SetActive(false);
            return uplink;
        }

        /// <summary>Open the feed, aimed at <paramref name="aim"/> or the last remembered aim.</summary>
        public void Show(GlobalPosition? aim)
        {
            GlobalPosition start = aim ?? (support.UplinkAimSet ? support.UplinkAim : default);
            commandX = aimX = start.x;
            commandZ = aimZ = start.z;
            nextHeight = 0f;
            nextText = 0f;
            dragging = false;
            visible = true;
            canvas.enabled = true;
            content.SetActive(true);
            AvButton.ClearTooltip();
            HoldInput();
        }

        public void Close()
        {
            if (!visible) return;
            visible = false;
            closedFrame = Time.frameCount;
            dragging = false;
            support?.SetUplinkAim(AimPoint());
            if (content != null) content.SetActive(false);
            if (canvas != null) canvas.enabled = false;
            if (imager != null) Destroy(imager.gameObject);
            imager = null;
            live = false;
            AvButton.ClearTooltip();
        }

        private void OnDestroy()
        {
            Close();
            ReleaseInput();
            closedFrame = -10;
            if (imager != null) Destroy(imager.gameObject);
            if (scanlineTexture != null) Destroy(scanlineTexture);
        }

        // ---- Input ownership -------------------------------------------------------------------

        private void HoldInput()
        {
            if (inputHeld) return;
            inputHeld = true;
            pauseWas = GameplayUI.AllowPauseKeybind;
            GameplayUI.AllowPauseKeybind = false;
            keyboardTouched = false;
            if (Rewired.ReInput.isReady && Rewired.ReInput.controllers != null && Rewired.ReInput.controllers.Keyboard != null)
            {
                keyboardWas = Rewired.ReInput.controllers.Keyboard.enabled;
                Rewired.ReInput.controllers.Keyboard.enabled = false;
                keyboardTouched = true;
            }
        }

        private void ReleaseInput()
        {
            if (!inputHeld) return;
            inputHeld = false;
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                Rewired.ReInput.controllers.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
        }

        // ---- Build -------------------------------------------------------------------------------

        private void Build()
        {
            var root = (RectTransform)transform;
            var contentObject = new GameObject("Content", typeof(RectTransform));
            content = contentObject;
            var contentRect = (RectTransform)contentObject.transform;
            contentRect.SetParent(root, false);
            AvKit.Stretch(contentRect);

            Image blocker = AvKit.Panel(contentRect, new Rect(0f, 0f, 10f, 10f), new Color(0.006f, 0.012f, 0.01f, 0.97f));
            AvKit.Stretch(blocker.rectTransform);
            blocker.raycastTarget = true;

            var frameObject = new GameObject("Frame", typeof(RectTransform));
            frame = (RectTransform)frameObject.transform;
            frame.SetParent(contentRect, false);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(FrameWidth, FrameHeight);

            bar = AvStyled.TopBar(frame, new Rect(LeftX, -16f, FrameWidth - LeftX * 2f, 40f), "UPLINK", 4);
            BuildVideo();
            BuildLeftRail();
            BuildRightRail();

            var passArea = new Rect(VideoX, VideoTop - VideoHeight - 14f, VideoWidth, 10f);
            passFill = AvKit.ProgressBar(frame, passArea, 0f, AvTheme.RailReady);
            passLeft = AvKit.Label(frame, "", new Rect(VideoX, passArea.y - 14f, 400f, 16f), AvTheme.Dim, AvTokens.FontSmall,
                FontStyles.Bold);
            passRight = AvKit.Label(frame, "", new Rect(VideoX + VideoWidth - 400f, passArea.y - 14f, 400f, 16f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Right);

            AvKit.Label(frame,
                "DRAG / WASD  SLEW   ·   WHEEL / Q E  ZOOM   ·   DOUBLE-CLICK  CENTRE   ·   C  NADIR   ·   " +
                "1-4  TASK AT CROSSHAIR   ·   F  DELIVER ARMED   ·   ESC / RIGHT-CLICK  CLOSE",
                new Rect(VideoX, VideoTop - VideoHeight - 52f, VideoWidth, 18f), AvTheme.Dim, AvTokens.FontSmall,
                FontStyles.Normal, TextAlignmentOptions.Center);
            status = AvStyled.StatusStrip(frame, new Rect(VideoX, VideoTop - VideoHeight - 80f, VideoWidth, 44f));
        }

        private void BuildVideo()
        {
            var area = new Rect(VideoX, VideoTop, VideoWidth, VideoHeight);
            AvKit.Panel(frame, area, new Color(0.01f, 0.018f, 0.015f, 1f));

            var videoObject = new GameObject("Feed", typeof(RectTransform), typeof(RawImage));
            video = videoObject.GetComponent<RawImage>();
            video.rectTransform.SetParent(frame, false);
            AvKit.Place(video.rectTransform, area);
            video.raycastTarget = false;
            video.enabled = false;

            scanlineTexture = new Texture2D(1, 4, TextureFormat.RGBA32, false)
            {
                name = "BoscaliUplinkScanlines",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            scanlineTexture.SetPixels32(new[]
            {
                new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 255)
            });
            scanlineTexture.Apply(false);
            var linesObject = new GameObject("Scanlines", typeof(RectTransform), typeof(RawImage));
            scanlines = linesObject.GetComponent<RawImage>();
            scanlines.rectTransform.SetParent(frame, false);
            AvKit.Place(scanlines.rectTransform, area);
            scanlines.texture = scanlineTexture;
            scanlines.uvRect = new Rect(0f, 0f, 1f, VideoHeight / 4f);
            scanlines.color = new Color(1f, 1f, 1f, 0.14f);
            scanlines.raycastTarget = false;

            veil = AvKit.Panel(frame, area, new Color(0.02f, 0.03f, 0.03f, 0.55f));

            AvKit.Outline(frame, area, AvTheme.Hairline);
            Color hud = AvTheme.RailReady.WithAlpha(0.9f);
            AvKit.CornerTicks(frame, new Rect(VideoX + 14f, VideoTop - 14f, VideoWidth - 28f, VideoHeight - 28f), hud, 40f);

            float cx = VideoX + VideoWidth * 0.5f;
            float cy = VideoTop - VideoHeight * 0.5f;
            Color cross = AvTheme.RailReady.WithAlpha(0.8f);
            crosshair = new[]
            {
                AvKit.Rule(frame, new Rect(cx - 70f, cy, 50f, 1f), cross),
                AvKit.Rule(frame, new Rect(cx + 20f, cy, 50f, 1f), cross),
                AvKit.Rule(frame, new Rect(cx, cy + 70f, 1f, 50f), cross),
                AvKit.Rule(frame, new Rect(cx, cy - 20f, 1f, 50f), cross),
                AvKit.Rule(frame, new Rect(cx - 2f, cy + 2f, 5f, 5f), cross),
                AvKit.Rule(frame, new Rect(cx - 120f, cy + 8f, 1f, 17f), cross.WithAlpha(0.5f)),
                AvKit.Rule(frame, new Rect(cx + 120f, cy + 8f, 1f, 17f), cross.WithAlpha(0.5f)),
                AvKit.Rule(frame, new Rect(cx - 8f, cy + 120f, 17f, 1f), cross.WithAlpha(0.5f)),
                AvKit.Rule(frame, new Rect(cx - 8f, cy - 120f, 17f, 1f), cross.WithAlpha(0.5f))
            };

            hudTopLeft = AvKit.Label(frame, "", new Rect(VideoX + 26f, VideoTop - 22f, 520f, 18f), hud, AvTokens.FontLead,
                FontStyles.Bold);
            hudTopRight = AvKit.Label(frame, "", new Rect(VideoX + VideoWidth - 546f, VideoTop - 22f, 440f, 18f), hud,
                AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.Right);
            hudBottomLeft = AvKit.Label(frame, "", new Rect(VideoX + 26f, VideoTop - VideoHeight + 40f, 620f, 18f), hud,
                AvTokens.FontLead, FontStyles.Bold);
            hudBottomRight = AvKit.Label(frame, "", new Rect(VideoX + VideoWidth - 546f, VideoTop - VideoHeight + 40f, 520f, 18f),
                hud, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.Right);

            liveLight = AvKit.Panel(frame, new Rect(VideoX + VideoWidth - 94f, VideoTop - 26f, 10f, 10f), AvTheme.RailDanger);
            liveLabel = AvKit.Label(frame, "LIVE", new Rect(VideoX + VideoWidth - 80f, VideoTop - 22f, 56f, 18f),
                AvTheme.RailDanger, AvTokens.FontLead, FontStyles.Bold);

            var northObject = new GameObject("North", typeof(RectTransform));
            north = (RectTransform)northObject.transform;
            north.SetParent(frame, false);
            north.anchorMin = north.anchorMax = new Vector2(0f, 1f);
            north.pivot = new Vector2(0.5f, 0.5f);
            north.sizeDelta = new Vector2(44f, 44f);
            north.anchoredPosition = new Vector2(VideoX + VideoWidth - 50f, VideoTop - 90f);
            AvKit.Rule(north, new Rect(21f, -6f, 2f, 24f), hud);
            AvKit.Rule(north, new Rect(17f, -6f, 10f, 2f), hud);
            AvKit.Label(north, "N", new Rect(0f, 12f, 44f, 14f), hud, AvTokens.FontSmall, FontStyles.Bold,
                TextAlignmentOptions.Center);

            float scaleY = VideoTop - VideoHeight + 70f;
            AvKit.Rule(frame, new Rect(VideoX + 26f, scaleY, ScaleBarPixels, 2f), hud);
            AvKit.Rule(frame, new Rect(VideoX + 26f, scaleY + 6f, 2f, 8f), hud);
            AvKit.Rule(frame, new Rect(VideoX + 24f + ScaleBarPixels, scaleY + 6f, 2f, 8f), hud);
            scaleLabel = AvKit.Label(frame, "", new Rect(VideoX + 26f + ScaleBarPixels + 10f, scaleY + 8f, 160f, 16f), hud,
                AvTokens.FontSmall, FontStyles.Bold);

            slate = AvKit.Label(frame, "", new Rect(VideoX + 80f, VideoTop - VideoHeight * 0.5f + 40f, VideoWidth - 160f, 80f),
                AvTheme.RailCaution, 26f, FontStyles.Bold, TextAlignmentOptions.Center, wrap: true);
        }

        private void BuildLeftRail()
        {
            float y = VideoTop;
            Section(LeftX, ref y, "TRACK · TELEMETRY");
            for (int i = 0; i < Keys.Length; i++)
            {
                AvStyled.Label(frame, new Rect(LeftX, y, RailWidth * 0.4f, 18f), Keys[i], "kv-key");
                values[i] = AvKit.Label(frame, "—", new Rect(LeftX + RailWidth * 0.34f, y, RailWidth * 0.66f, 18f),
                    AvTheme.TextPrimary, AvTokens.FontBody, FontStyles.Bold, TextAlignmentOptions.Right);
                y -= 26f;
            }
            y -= 12f;
            Section(LeftX, ref y, "POWER · PROPELLANT");
            energyText = AvKit.Label(frame, "", new Rect(LeftX, y, RailWidth, 16f), AvTheme.TextPrimary, AvTokens.FontSmall,
                FontStyles.Bold);
            energyFill = AvKit.ProgressBar(frame, new Rect(LeftX, y - 20f, RailWidth, 10f), 0f, AvTheme.RailReady);
            y -= 42f;
            fuelText = AvKit.Label(frame, "", new Rect(LeftX, y, RailWidth, 16f), AvTheme.TextPrimary, AvTokens.FontSmall,
                FontStyles.Bold);
            fuelFill = AvKit.ProgressBar(frame, new Rect(LeftX, y - 20f, RailWidth, 10f), 0f, AvTheme.RailCaution);
            y -= 42f;
            rodsText = AvKit.Label(frame, "", new Rect(LeftX, y, RailWidth, 16f), AvTheme.TextPrimary, AvTokens.FontSmall,
                FontStyles.Bold);
        }

        private void BuildRightRail()
        {
            float y = VideoTop;
            Section(RightX, ref y, "RADAR PRODUCT");
            var box = new Rect(RightX, y, RailWidth, RailWidth);
            AvKit.Panel(frame, box, new Color(0.01f, 0.018f, 0.015f, 1f));
            AvKit.Outline(frame, box, AvTheme.Hairline);
            var productObject = new GameObject("Product", typeof(RectTransform), typeof(RawImage));
            product = productObject.GetComponent<RawImage>();
            product.rectTransform.SetParent(frame, false);
            AvKit.Place(product.rectTransform, new Rect(RightX + 8f, y - 8f, RailWidth - 16f, RailWidth - 16f));
            // Formed images keep range on the horizontal axis; flip so the product is a rotation, not a mirror.
            product.uvRect = new Rect(1f, 0f, -1f, 1f);
            product.raycastTarget = false;
            product.enabled = false;
            productCaption = AvKit.Label(frame, "", new Rect(RightX, y - RailWidth - 8f, RailWidth, 16f), AvTheme.RailReady,
                AvTokens.FontSmall, FontStyles.Bold);
            productDetail = AvKit.Label(frame, "", new Rect(RightX, y - RailWidth - 26f, RailWidth, 34f), AvTheme.Dim,
                AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.TopLeft, wrap: true);
            y -= RailWidth + 70f;

            Section(RightX, ref y, "TASKING · AT CROSSHAIR");
            for (int i = 0; i < Tasks.Length; i++)
            {
                int index = i;
                taskButtons[i] = AvStyled.Button(frame, new Rect(RightX, y, RailWidth, 30f), "", "btn",
                    () => Task(index), AvButtonStyle.Primary);
                taskStatus[i] = AvKit.Label(frame, "", new Rect(RightX, y - 33f, RailWidth, 16f), AvTheme.Dim,
                    AvTokens.FontSmall, FontStyles.Normal);
                y -= 58f;
            }
            deliverButton = AvStyled.Button(frame, new Rect(RightX, y, RailWidth, 30f), "[F] DELIVER ARMED", "btn",
                DeliverArmed, AvButtonStyle.Quiet);
            y -= 48f;
            AvStyled.Button(frame, new Rect(RightX, y, RailWidth, 34f), "CLOSE UPLINK  [ESC]", "btn", Close,
                AvButtonStyle.Danger).WithTooltip("Close the feed. The station keeps flying; the aim is remembered.");
        }

        private void Section(float x, ref float y, string title)
        {
            AvStyled.Label(frame, new Rect(x, y, RailWidth, 16f), title, "section-title");
            AvKit.Rule(frame, new Rect(x, y - 20f, RailWidth, 1f), AvTheme.Hairline);
            y -= 30f;
        }

        // ---- Frame -------------------------------------------------------------------------------

        private void Update()
        {
            if (!visible)
            {
                if (inputHeld && Time.frameCount > closedFrame + 1) ReleaseInput();
                return;
            }
            if (support == null || !DynamicMap.mapMaximized || GameplayUI.GameIsPaused)
            {
                Close();
                return;
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            OrbitalPlatform platform = support.LocalPlatform;
            double now = support.OrbitNow;
            OrbitClock clock = support.OrbitClock;
            bool station = platform != null && platform.Exists;
            OrbitState state = station ? platform.State(now, clock) : default;

            HandleInput(dt, station ? platform : null, state);
            if (!visible) return;

            float constant = station && platform.Stats(now).Stabilised ? GyroSlewTimeConstant : SlewTimeConstant;
            double k = 1.0 - Math.Exp(-dt / constant);
            aimX += (commandX - aimX) * k;
            aimZ += (commandZ - aimZ) * k;
            bool slewing = Math.Abs(commandX - aimX) + Math.Abs(commandZ - aimZ) > 10.0;

            if (Time.unscaledTime >= nextHeight)
            {
                nextHeight = Time.unscaledTime + 0.5f;
                var probe = new GlobalPosition((float)aimX, 0f, (float)aimZ);
                aimHeight = SupportTargeting.TryMapPoint(probe, out Vector3 ground) ? ground.ToGlobalPosition().y : 0f;
            }

            LookAngles look = station ? TheaterTrack.Look(state, aimX, aimZ) : LookAngles.Hidden;
            RenderFeed(platform, state, look, now);

            bool blink = Time.unscaledTime % 1f < 0.6f;
            liveLight.enabled = live && blink;
            liveLabel.text = live ? "LIVE" : "NO FEED";
            liveLabel.color = live ? AvTheme.RailDanger : AvTheme.Dim;
            product.texture = products.Scan.Image;
            product.enabled = products.HasProduct;

            if (Time.unscaledTime >= nextText)
            {
                nextText = Time.unscaledTime + TextInterval;
                WriteText(platform, state, look, now, clock, slewing);
            }
        }

        private void RenderFeed(OrbitalPlatform platform, in OrbitState state, in LookAngles look, double now)
        {
            string message = null;
            if (platform == null || !platform.Exists) message = "NO STATION ON ORBIT\nLAUNCH A CORE IN MISSION PLANNER";
            else if (!platform.Fitted(ModuleKind.Imager)) message = "NO SPY IMAGER FITTED";
            else if (!platform.FittedOnline(ModuleKind.Imager, now)) message = "IMAGER OFFLINE · DEBRIS STRIKE";
            else if (platform.Brownout) message = "STATION BROWNOUT · FEED LOST";
            else if (platform.HoldAt(now) != PlatformHold.None)
                message = PlatformWords.Hold(platform.HoldAt(now)) + " · T-" + PlatformWords.Clock(platform.CycleStart - now);
            else if (!state.InPass) message = "LOSS OF SIGNAL\nAOS " + PlatformWords.Clock(state.TimeToPass);
            else if (!look.Visible || look.OffNadir > FieldOfRegard) message = "AIM BEYOND FIELD OF REGARD\nPRESS C FOR NADIR";

            if (message != null)
            {
                live = false;
                slate.text = message;
                slate.color = AvTheme.RailCaution;
                veil.enabled = true;
                video.color = new Color(0.4f, 0.43f, 0.42f, 1f);
                video.enabled = imager != null && imager.FramesRendered > 0;
                scanlines.color = new Color(1f, 1f, 1f, 0.3f);
                SetCrosshair(false);
                return;
            }

            bool night = IsNight();
            OrbitRegime orbit = platform.Orbit;
            float footprint = Footprint(orbit, look, night);

            if (imager == null) imager = SatelliteImager.Create(FeedWidth, FeedHeight);
            Vector3 aimLocal = new GlobalPosition((float)aimX, aimHeight, (float)aimZ).ToLocalPosition();
            float cosEl = (float)Math.Cos(look.Elevation);
            var los = new Vector3((float)look.AzimuthX * cosEl, (float)Math.Sin(look.Elevation), (float)look.AzimuthZ * cosEl);
            var along = new Vector3((float)state.Pass.DirX, 0f, (float)state.Pass.DirZ);
            imager.Aim(aimLocal, los, along, footprint, night, FramesPerSecond);

            north.localEulerAngles = new Vector3(0f, 0f, (float)(state.Pass.Heading / OrbitMath.Deg));
            live = imager.FramesRendered > 0;
            video.texture = imager.Output;
            video.enabled = live;
            scanlines.color = new Color(1f, 1f, 1f, night ? 0.2f : 0.12f);
            SetCrosshair(true);

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float cloud = level != null ? level.GetCloudOcclusion(aimLocal) : 0f;
            if (cloud > 0.55f)
            {
                video.color = new Color(0.55f, 0.58f, 0.6f, 1f);
                veil.enabled = true;
                slate.text = "CLOUD DECK · NO GROUND CONTACT\nRADAR SCAN [1] SEES THROUGH";
                slate.color = AvTheme.RailCaution;
            }
            else
            {
                video.color = Color.white;
                veil.enabled = !live;
                slate.text = live ? "" : "ACQUIRING…";
                slate.color = AvTheme.RailReady;
            }
        }

        private float Footprint(in OrbitRegime orbit, in LookAngles look, bool night)
        {
            float footprint = Footprints[footprintIndex];
            float minimum = (float)(TheaterTrack.GroundSample(orbit, look, night) * FeedWidth * 0.75);
            gsdLimited = footprint < minimum;
            return gsdLimited ? minimum : footprint;
        }

        private void SetCrosshair(bool on)
        {
            for (int i = 0; i < crosshair.Length; i++) crosshair[i].enabled = on;
        }

        private void WriteText(OrbitalPlatform platform, in OrbitState state, in LookAngles look, double now,
                               in OrbitClock clock, bool slewing)
        {
            bool station = platform != null && platform.Exists;
            bool night = IsNight();
            OrbitRegime orbit = station ? platform.Orbit : OrbitRegimes.Get(OrbitRegimes.Mid);
            PlatformStats stats = station ? platform.Stats(now) : default;
            float footprint = station && look.Visible ? Footprint(orbit, look, night) : Footprints[footprintIndex];
            double gsd = station ? TheaterTrack.GroundSample(orbit, look, night) : 0.0;
            bool contact = station && state.InPass && look.Visible;

            bar.State.text = OrbitalPlatform.Callsign + " · SPY IMAGER · " + (station ? orbit.Name : "NO STATION");
            bar.SetChip(0, night ? "MWIR" : "VNIR", night ? "warn" : "info");
            bar.SetChip(1, contact ? "AOS" : "LOS", contact ? "live" : "warn");
            bar.SetChip(2, live ? "LIVE" : "NO FEED", live ? "live" : "inert");
            bar.SetChip(3, gsdLimited ? "GSD LIMIT" : "ZOOM " + (footprintIndex + 1) + "/" + Footprints.Length,
                gsdLimited ? "warn" : "info");

            values[0].text = OrbitalPlatform.Callsign;
            values[1].text = station ? orbit.Code + " · " + TheaterGrid.Km(orbit.Altitude) + " KM" : "—";
            values[2].text = PlatformWords.Phase(platform, now, clock);
            values[3].text = look.Visible ? Deg(look.Elevation) + " / " + Bearing(look.AzimuthDeg) : "—";
            values[4].text = look.Visible ? Deg(look.OffNadir) + (slewing ? " SLEW" : "") : "—";
            values[5].text = look.Visible ? TheaterGrid.Km(look.SlantRange) + " KM" : "—";
            values[6].text = look.Visible ? gsd.ToString("0.00", Invariant) + " M" : "—";
            values[7].text = TheaterGrid.Km(footprint) + " KM" + (gsdLimited ? " GSD" : "");
            values[8].text = TheaterGrid.Kilometres(aimX, aimZ);
            values[9].text = contact ? TheaterGrid.Kilometres(state.SubX, state.SubZ) : "—";
            values[10].text = station ? Bearing(state.Pass.Heading / OrbitMath.Deg) + (state.Pass.Ascending ? " ASC" : " DSC") : "—";
            values[11].text = station ? PlatformWords.Whole(platform.Energy) + " KJ" + (platform.Brownout ? " BRN" : "") : "—";
            values[12].text = night ? "MWIR · WHITE HOT" : "VNIR · TRUE COLOUR";
            if (station)
            {
                LookAngles centre = TheaterTrack.Look(state, 0.0, 0.0);
                TelemetryFrame telemetry = PlatformTelemetry.Compute(platform, stats, centre, !state.InPass || !night, slewing, now);
                values[13].text = telemetry.HasLink
                    ? (telemetry.LinkMarginDb >= 0 ? "+" : "") + telemetry.LinkMarginDb.ToString("0.0", Invariant) + " DB"
                    : "NO LINK";
            }
            else
            {
                values[13].text = "NO LINK";
            }

            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            string gmt = level != null ? TheaterGrid.Clock(((level.timeOfDay % 24f) + 24f) % 24f * 3600.0) : "--:--";
            hudTopLeft.text = "GMT " + gmt + "   FRM " + (imager != null ? imager.FramesRendered : 0).ToString("00000", Invariant);
            hudTopRight.text = OrbitalPlatform.Callsign + (night ? " MWIR" : " VNIR") + "   GET " +
                               TheaterGrid.Elapsed(station ? platform.Elapsed(now) : -1.0);
            hudBottomLeft.text = look.Visible
                ? "EL " + Deg(look.Elevation) + "   OFF-NDR " + Deg(look.OffNadir) + "   SLANT " + TheaterGrid.Km(look.SlantRange) + " KM"
                : PlatformWords.Phase(platform, now, clock);
            hudBottomRight.text = "FOV " + TheaterGrid.Km(footprint) + " KM   GSD " +
                                  (look.Visible ? gsd.ToString("0.00", Invariant) + " M" : "—");
            scaleLabel.text = Distance(footprint * ScaleBarPixels / VideoWidth);

            if (station && state.InPass)
            {
                passFill.fillAmount = (float)(state.TimeInPass / Math.Max(1.0, state.Window));
                passFill.color = AvTheme.RailReady;
                passLeft.text = "AOS +" + PlatformWords.Clock(state.TimeInPass);
                passRight.text = "LOS " + PlatformWords.Clock(state.TimeToPassEnd);
            }
            else
            {
                passFill.fillAmount = 0f;
                passLeft.text = station ? PlatformWords.Phase(platform, now, clock) : "NO STATION";
                passRight.text = station && platform.HoldAt(now) == PlatformHold.None
                    ? "NEXT PASS " + PlatformWords.Clock(state.Window)
                    : "";
            }

            if (station)
            {
                energyText.text = "ENERGY " + PlatformWords.Whole(platform.Energy) + " / " + PlatformWords.Whole(stats.StorageKj) + " KJ";
                energyFill.fillAmount = stats.StorageKj > 0f ? platform.Energy / stats.StorageKj : 0f;
                energyFill.color = platform.Brownout ? AvTheme.RailDanger : AvTheme.RailReady;
                fuelText.text = stats.FuelCapacity > 0f
                    ? "FUEL " + PlatformWords.Whole(platform.Fuel) + " / " + PlatformWords.Whole(stats.FuelCapacity)
                    : "FUEL — NO PROPULSION";
                fuelFill.fillAmount = stats.FuelCapacity > 0f ? platform.Fuel / stats.FuelCapacity : 0f;
                rodsText.text = stats.RodCapacity > 0 ? "RODS " + platform.Rods + " / " + stats.RodCapacity : "RODS — NO MAGAZINE";
            }
            else
            {
                energyText.text = fuelText.text = rodsText.text = "—";
                energyFill.fillAmount = fuelFill.fillAmount = 0f;
            }

            WriteProduct();
            WriteTasks(platform, now, clock);

            string hovered = AvButton.HoveredTooltip;
            status.text = "> " + (!string.IsNullOrEmpty(hovered) ? hovered : support.Status);
            status.color = !string.IsNullOrEmpty(hovered) ? AvTheme.Friendly : AvTheme.Dim;
        }

        private void WriteProduct()
        {
            SarCollector scan = products.Scan;
            switch (scan.Phase)
            {
                case SarPhase.Collecting:
                    productCaption.text = "COLLECTING " + Mathf.RoundToInt(scan.Progress * 100f) + "%";
                    productCaption.color = AvTheme.RailCaution;
                    break;
                case SarPhase.Processing:
                    productCaption.text = "RANGE-DOPPLER PROCESSING " + Mathf.RoundToInt(scan.ProcessingProgress * 100f) + "%";
                    productCaption.color = AvTheme.RailCaution;
                    break;
                case SarPhase.Complete:
                    productCaption.text = "PRODUCT READY · " + scan.Contacts + " CONTACT(S)";
                    productCaption.color = AvTheme.RailReady;
                    break;
                default:
                    productCaption.text = "NO PRODUCT";
                    productCaption.color = AvTheme.Dim;
                    productDetail.text = "Task a RADAR SCAN [1]: stationary ground contacts are revealed; clouds and night do not matter.";
                    return;
            }
            productDetail.text = "GRD · LOOK " + (scan.LookRight ? "RIGHT" : "LEFT") + " · INC " +
                                 scan.IncidenceDeg.ToString("0.0", Invariant) + "° · SCENE " +
                                 TheaterGrid.Km(scan.SceneHalfSize * 2.0) + " KM · " +
                                 TheaterGrid.Kilometres(scan.Centre.x, scan.Centre.z);
        }

        private void WriteTasks(OrbitalPlatform platform, double now, in OrbitClock clock)
        {
            bool bypass = support.BypassRequirements;
            float cooldown = support.LocalCooldownRemaining;
            float allocation = support.LocalAllocation;
            for (int i = 0; i < Tasks.Length; i++)
            {
                SupportActionDefinition definition = Find(Tasks[i]);
                PlatformAbility ability = SupportManager.OrbitalAbility(Tasks[i]).GetValueOrDefault();
                string name = PlatformAbilities.Info(ability).Name;
                string line;
                bool enabled = false;
                float cost = definition != null ? support.Cost(definition) : 0f;
                if (definition == null || !definition.Enabled || cost <= 0f)
                {
                    line = "UNAVAILABLE ON THIS SERVER";
                }
                else if (!support.IsAuthorised(definition))
                {
                    line = "LOCKED · UNLOCK IN SQD ABILITIES";
                }
                else
                {
                    PlatformDenial denial = support.PlatformCheck(ability);
                    if (denial != PlatformDenial.None) line = PlatformWords.Denial(denial, platform, ability, now, clock);
                    else if (support.RequestPending) line = "REQUEST PENDING · AWAITING HOST";
                    else if (cooldown > 0.5f) line = "NET COOLING · T-" + Mathf.CeilToInt(cooldown) + "S";
                    else if (!bypass && allocation + 0.001f < cost) line = "INSUFFICIENT ALLOCATION";
                    else
                    {
                        line = "READY · " + PlatformWords.Whole(cost) + " ALLOC · " +
                               PlatformWords.Whole(PlatformAbilities.Info(ability).EnergyKj) + " KJ";
                        enabled = true;
                    }
                }
                taskButtons[i].SetText("[" + (i + 1) + "]  " + name);
                taskButtons[i].SetEnabled(enabled);
                taskButtons[i].WithTooltip(name + " at the crosshair — " + PlatformAbilities.Info(ability).Summary + " " + line + ".");
                taskStatus[i].text = line;
                taskStatus[i].color = enabled ? AvTheme.RailReady : AvTheme.Dim;
            }

            SupportActionDefinition armed = support.ArmedAction.HasValue ? Find(support.ArmedAction.Value) : null;
            deliverButton.SetText(armed != null ? "[F] DELIVER " + armed.Name : "[F] DELIVER ARMED");
            deliverButton.SetEnabled(armed != null && !support.RequestPending);
            deliverButton.WithTooltip(armed != null
                ? "Deliver the armed " + armed.Name + " at the crosshair instead of a map click."
                : "Arm any support action in OPS, then deliver it here at the crosshair.");
        }

        // ---- Controls ----------------------------------------------------------------------------

        private void HandleInput(float dt, OrbitalPlatform platform, in OrbitState state)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace))
            {
                Close();
                return;
            }

            Vector3 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(1)) rightPress = mouse;
            if (Input.GetMouseButtonUp(1) && (mouse - rightPress).sqrMagnitude <= ClickSlop * ClickSlop)
            {
                Close();
                return;
            }

            bool overVideo = RectTransformUtility.RectangleContainsScreenPoint(video.rectTransform, mouse, null);
            if (Input.GetMouseButtonDown(0) && overVideo)
            {
                dragging = true;
                lastMouse = mouse;
                if (Time.unscaledTime - lastClickTime < DoubleClickSeconds &&
                    (mouse - lastClickPosition).sqrMagnitude <= ClickSlop * ClickSlop)
                {
                    CentreOn(mouse, state);
                    lastClickTime = -10f;
                }
                else
                {
                    lastClickTime = Time.unscaledTime;
                    lastClickPosition = mouse;
                }
            }
            if (dragging && Input.GetMouseButton(0))
            {
                Vector3 delta = mouse - lastMouse;
                lastMouse = mouse;
                if (delta.sqrMagnitude > 0f) Slew(delta.x, delta.y, state, platform != null);
            }
            if (Input.GetMouseButtonUp(0)) dragging = false;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && overVideo) Zoom(wheel > 0f ? 1 : -1);
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.KeypadPlus)) Zoom(1);
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.KeypadMinus)) Zoom(-1);

            float right = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) -
                          (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float up = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) -
                       (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            if (right != 0f || up != 0f)
            {
                float pixels = VideoScreenWidth() * KeySlewFraction * dt;
                // Keys move the sensor; dragging moves the picture. Opposite signs.
                Slew(-right * pixels, -up * pixels, state, platform != null);
            }

            if (Input.GetKeyDown(KeyCode.C) && state.InPass)
            {
                commandX = state.SubX;
                commandZ = state.SubZ;
            }
            for (int i = 0; i < TaskKeys.Length; i++)
                if (Input.GetKeyDown(TaskKeys[i]) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    Task(i);
            if (Input.GetKeyDown(KeyCode.F)) DeliverArmed();
        }

        private float VideoScreenWidth() =>
            Mathf.Max(1f, video.rectTransform.rect.width * (canvas != null ? canvas.scaleFactor : 1f));

        private float CurrentFootprint(in OrbitState state)
        {
            OrbitalPlatform platform = support.LocalPlatform;
            if (platform == null || !platform.Exists || !state.InPass) return Footprints[footprintIndex];
            LookAngles look = TheaterTrack.Look(state, aimX, aimZ);
            return look.Visible ? Footprint(platform.Orbit, look, IsNight()) : Footprints[footprintIndex];
        }

        /// <summary>Move the aim so the picture follows a screen drag of (dx, dy) pixels.</summary>
        private void Slew(float dx, float dy, in OrbitState state, bool station)
        {
            if (!station) return;
            double metresPerPixel = CurrentFootprint(state) / VideoScreenWidth();
            // Screen up is along-track, screen right is to the right of track.
            double upX = state.Pass.DirX, upZ = state.Pass.DirZ;
            double rightX = upZ, rightZ = -upX;
            double nextX = commandX - (rightX * dx + upX * dy) * metresPerPixel;
            double nextZ = commandZ - (rightZ * dx + upZ * dy) * metresPerPixel;
            Accept(nextX, nextZ, state);
        }

        private void CentreOn(Vector3 screenPoint, in OrbitState state)
        {
            RectTransform rect = video.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, null, out Vector2 local)) return;
            Vector2 centre = rect.rect.center;
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            Vector2 offset = (local - centre) * scale;
            // A click right of centre should become the centre: slew the picture the other way.
            Slew(-offset.x, -offset.y, state, support.LocalPlatform != null);
        }

        private void Accept(double x, double z, in OrbitState state)
        {
            OrbitalBounds.Extents(out float halfWidth, out float halfHeight);
            if (halfWidth > 0f)
            {
                x = OrbitMath.Clamp(x, -halfWidth * 1.1, halfWidth * 1.1);
                z = OrbitMath.Clamp(z, -halfHeight * 1.1, halfHeight * 1.1);
            }
            if (state.InPass)
            {
                LookAngles look = TheaterTrack.Look(state, x, z);
                if (!look.Visible || look.OffNadir > FieldOfRegard) return;
            }
            commandX = x;
            commandZ = z;
        }

        private void Zoom(int direction)
        {
            footprintIndex = Mathf.Clamp(footprintIndex + direction, 0, Footprints.Length - 1);
            nextText = 0f;
        }

        private GlobalPosition AimPoint() => new GlobalPosition((float)aimX, aimHeight, (float)aimZ);

        private void Task(int index)
        {
            if (index < 0 || index >= Tasks.Length || taskButtons[index] == null) return;
            SupportActionDefinition definition = Find(Tasks[index]);
            if (definition == null) return;
            support.RequestAt(definition.Id, AimPoint());
            nextText = 0f;
        }

        private void DeliverArmed()
        {
            if (!support.ArmedAction.HasValue) return;
            support.CallArmedAt(AimPoint());
            nextText = 0f;
        }

        private SupportActionDefinition Find(SupportActionId id)
        {
            var actions = support.Actions;
            for (int i = 0; i < actions.Count; i++)
                if (actions[i].Id == id) return actions[i];
            return null;
        }

        // ---- Formatting --------------------------------------------------------------------------

        private static bool IsNight()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            return level != null && (level.timeOfDay < 6f || level.timeOfDay > 18f || level.GetAmbientLight() < 0.06f);
        }

        private static string Deg(double radians) => (radians / OrbitMath.Deg).ToString("0.0", Invariant) + "°";

        private static string Bearing(double degrees) =>
            ((Math.Round(degrees) % 360.0 + 360.0) % 360.0).ToString("000", Invariant) + "°";

        private static string Distance(double metres) =>
            metres >= 1000.0 ? (metres / 1000.0).ToString("0.0", Invariant) + " KM" : Math.Round(metres).ToString("0", Invariant) + " M";
    }
}
