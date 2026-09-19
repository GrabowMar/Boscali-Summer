using System;
using System.Collections.Generic;
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
using static BoscaliSummer.Features.Support.Presentation.SupportOverlayUi;

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
        private const float VideoTop = -88f;
        private const float VideoWidth = 1280f;
        private const float VideoHeight = 800f;
        private const float LeftX = 24f;
        private const float RightX = 1616f;
        private const float RailWidth = 280f;
        // One grid for every readout: key column, then the value column. 30 px rows, and the
        // list's pitch stretches so the rail fills the video's height instead of leaving a
        // dead band under the last row.
        private const float KeyWidth = RailWidth * 0.34f;
        private const float TaskBlock = 82f;
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

        private static readonly float[] Footprints = { 32000f, 16000f, 8000f, 4000f, 2000f, 1000f, 500f, 250f, 120f, 60f };
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

        private sealed class ContactMarker
        {
            public GameObject Root;
            public RectTransform Transform;
            public Image[] Brackets;
            public Image Leader;
            public TMP_Text Label;
            public bool Active;
        }

        private const int MaxContactMarkers = 16;
        private readonly ContactMarker[] contactMarkers = new ContactMarker[MaxContactMarkers];

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
        // The same readiness the buttons paint with, kept for the key handler: a hotkey
        // must not fire what the button would have refused.
        private readonly bool[] taskReady = new bool[Tasks.Length];
        private bool deliverReady;
        private AvButton deliverButton;
        private TMP_Text status;
        private Image statusRail;

        private SatelliteImager imager;
        private double aimX, aimZ, commandX, commandZ;
        private float aimHeight;
        private float nextHeight;
        private float nextText;
        private int footprintIndex = 4;
        private bool gsdLimited;
        private bool live;
        private bool dragging;
        private Vector3 lastMouse;
        private Vector3 rightPress;
        private Vector3 lastClickPosition;
        private float lastClickTime = -10f;

        private readonly FullscreenInput input = new FullscreenInput();

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
            // Keys are gated on the button readiness; the first frame repaints it.
            Array.Clear(taskReady, 0, taskReady.Length);
            deliverReady = false;
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
            HideContacts();
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

        private void HoldInput() => input.Hold();

        private void ReleaseInput() => input.Release();

        // ---- Build -------------------------------------------------------------------------------

        private void Build()
        {
            var root = (RectTransform)transform;
            var contentObject = new GameObject("Content", typeof(RectTransform));
            content = contentObject;
            var contentRect = (RectTransform)contentObject.transform;
            contentRect.SetParent(root, false);
            AvKit.Stretch(contentRect);

            Image blocker = AvKit.Panel(contentRect, new Rect(0f, 0f, 10f, 10f), AvTheme.Surface.WithAlpha(0.98f));
            AvKit.Stretch(blocker.rectTransform);
            blocker.raycastTarget = true;

            var frameObject = new GameObject("Frame", typeof(RectTransform));
            frame = (RectTransform)frameObject.transform;
            frame.SetParent(contentRect, false);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(FrameWidth, FrameHeight);

            bar = AvStyled.TopBar(frame, new Rect(LeftX, -16f, FrameWidth - LeftX * 2f, AvTokens.ScreenHeaderHeight), "UPLINK", 4);
            BuildVideo();

            // The feed keeps its 16:10 rect; the pass bar, legend and status strip are stacked
            // under it and pinned to the frame, so the bottom of the overlay is never a dead
            // band. The rails flank the feed at exactly its height.
            float frameHeight = frame.rect.height;
            float videoBottom = VideoTop - VideoHeight;
            float statusTop = -frameHeight + AvTokens.Space4 + AvTokens.StatusStripHeight;
            float margin = ((videoBottom - statusTop) - (10f + 16f + 18f)) / 4f;
            float passTop = videoBottom - margin;
            float passLabelTop = passTop - 10f - margin;
            float legendTop = passLabelTop - 16f - margin;

            BuildLeftRail(VideoTop, videoBottom);
            BuildRightRail(VideoTop, videoBottom);

            passFill = AvKit.ProgressBar(frame, new Rect(VideoX, passTop, VideoWidth, 10f), 0f, AvTheme.RailReady);
            passLeft = AvKit.Label(frame, "", new Rect(VideoX, passLabelTop, 400f, 16f), AvTheme.Dim, AvTokens.FontSmall,
                FontStyles.Bold);
            passRight = AvKit.Label(frame, "", new Rect(VideoX + VideoWidth - 400f, passLabelTop, 400f, 16f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Right);

            AvKit.Label(frame,
                "DRAG / WASD  SLEW   ·   WHEEL / Q E  ZOOM   ·   DOUBLE-CLICK  CENTRE   ·   C  NADIR   ·   " +
                "1-4  TASK AT CROSSHAIR   ·   F  DELIVER ARMED   ·   ESC / RIGHT-CLICK  CLOSE",
                new Rect(VideoX, legendTop, VideoWidth, 18f), AvTheme.Dim, AvTokens.FontSmall,
                FontStyles.Normal, TextAlignmentOptions.Center);
            status = AvStyled.StatusStrip(
                frame, new Rect(VideoX, statusTop, VideoWidth, AvTokens.StatusStripHeight), out statusRail);
        }

        private void BuildVideo()
        {
            var area = new Rect(VideoX, VideoTop, VideoWidth, VideoHeight);
            AvKit.Panel(frame, area, AvTheme.SurfaceInert);

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

            var contactsObject = new GameObject("Contacts", typeof(RectTransform));
            var contactsRect = (RectTransform)contactsObject.transform;
            contactsRect.SetParent(frame, false);
            AvKit.Place(contactsRect, area);

            for (int i = 0; i < MaxContactMarkers; i++)
            {
                var markerObj = new GameObject("Marker_" + i, typeof(RectTransform));
                var rt = (RectTransform)markerObj.transform;
                rt.SetParent(contactsRect, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(32f, 32f);

                Image[] brackets = AvKit.Outline(rt, new Rect(-16f, 16f, 32f, 32f), hud);

                var leaderObj = new GameObject("Leader", typeof(RectTransform), typeof(Image));
                var leaderRt = (RectTransform)leaderObj.transform;
                leaderRt.SetParent(rt, false);
                leaderRt.anchorMin = leaderRt.anchorMax = new Vector2(0.5f, 0.5f);
                leaderRt.pivot = new Vector2(0f, 0.5f);
                leaderRt.anchoredPosition = Vector2.zero;
                leaderRt.sizeDelta = new Vector2(16f, 1.5f);
                Image leaderImg = leaderObj.GetComponent<Image>();
                leaderImg.color = hud;
                leaderImg.raycastTarget = false;

                TMP_Text lbl = AvKit.Label(rt, "", new Rect(-60f, -22f, 120f, 14f), hud, AvTokens.FontMicro,
                    FontStyles.Bold, TextAlignmentOptions.Center);

                contactMarkers[i] = new ContactMarker
                {
                    Root = markerObj,
                    Transform = rt,
                    Brackets = brackets,
                    Leader = leaderImg,
                    Label = lbl,
                    Active = false
                };
                markerObj.SetActive(false);
            }
        }

        private void BuildLeftRail(float railTop, float railBottom)
        {
            float y = railTop;
            Section(frame, LeftX, ref y, RailWidth, "TRACK · TELEMETRY");
            float rowsTop = y;
            // The readout list is the rail's primary list: it takes the slack between the
            // section head and the power block, so the rail is flush top and bottom.
            float block = AvTokens.Space3 + 30f + 42f + 42f + 16f;
            float pitch = FillPitch(rowsTop - railBottom - block, Keys.Length, AvTokens.RowHeight);
            for (int i = 0; i < Keys.Length; i++)
            {
                float rowY = rowsTop - i * pitch;
                AvKit.Label(frame, Keys[i], new Rect(LeftX, rowY, KeyWidth, AvTokens.RowHeight), AvTheme.Dim, 12f);
                values[i] = Value(frame, new Rect(LeftX + KeyWidth + AvTokens.Gap, rowY,
                    RailWidth - KeyWidth - AvTokens.Gap, AvTokens.RowHeight));
            }
            y = rowsTop - (Keys.Length - 1) * pitch - AvTokens.RowHeight - AvTokens.Space3;
            Section(frame, LeftX, ref y, RailWidth, "POWER · PROPELLANT");
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

        private void BuildRightRail(float railTop, float railBottom)
        {
            float y = railTop;
            Section(frame, RightX, ref y, RailWidth, "RADAR PRODUCT");
            const float productSize = 220f;
            var box = new Rect(RightX, y, RailWidth, productSize);
            AvKit.Panel(frame, box, AvTheme.SurfaceInert);
            AvKit.Outline(frame, box, AvTheme.Hairline);
            var productObject = new GameObject("Product", typeof(RectTransform), typeof(RawImage));
            product = productObject.GetComponent<RawImage>();
            product.rectTransform.SetParent(frame, false);
            AvKit.Place(product.rectTransform, new Rect(RightX + (RailWidth - productSize) * 0.5f + 8f, y - 8f, productSize - 16f, productSize - 16f));
            // Formed images keep range on the horizontal axis; flip so the product is a rotation, not a mirror.
            product.uvRect = new Rect(1f, 0f, -1f, 1f);
            product.raycastTarget = false;
            product.enabled = false;
            productCaption = AvKit.Label(frame, "", new Rect(RightX, y - productSize - 8f, RailWidth, 16f), AvTheme.RailReady,
                AvTokens.FontSmall, FontStyles.Bold);
            productDetail = AvKit.Label(frame, "", new Rect(RightX, y - productSize - 26f, RailWidth, 34f), AvTheme.Dim,
                AvTokens.FontSmall, FontStyles.Normal, TextAlignmentOptions.TopLeft, wrap: true);
            y -= productSize + 70f;

            Section(frame, RightX, ref y, RailWidth, "TASKING · AT CROSSHAIR");
            // Deliver and close are anchored to the rail's bottom; the tasking rows are the
            // primary list and absorb the rest, so nothing hangs in a dead band.
            float closeTop = railBottom + 34f;
            float deliverTop = closeTop + AvTokens.Gap + AvTokens.RowHeight;
            float tasksBottom = deliverTop + AvTokens.Space3;
            float pitch = FillPitch(y - tasksBottom, Tasks.Length, TaskBlock);
            for (int i = 0; i < Tasks.Length; i++)
            {
                int index = i;
                float rowY = y - i * pitch;
                taskButtons[i] = AvStyled.Button(frame, new Rect(RightX, rowY, RailWidth, AvTokens.RowHeight), "", "btn",
                    () => Task(index), AvButtonStyle.Default);
                taskStatus[i] = Detail(frame, new Rect(RightX, rowY - 34f, RailWidth, 46f));
                taskStatus[i].maxVisibleLines = 3;
            }
            deliverButton = AvStyled.Button(frame, new Rect(RightX, deliverTop, RailWidth, AvTokens.RowHeight),
                "[F] DELIVER ARMED", "btn", DeliverArmed, AvButtonStyle.Quiet);
            AvStyled.Button(frame, new Rect(RightX, closeTop, RailWidth, 34f), "CLOSE UPLINK  [ESC]", "btn", Close,
                AvButtonStyle.Quiet).WithTooltip("Close the feed. The station keeps flying; the aim is remembered.");
        }

        // ---- Frame -------------------------------------------------------------------------------

        private void Update()
        {
            if (!visible)
            {
                if (input.Held && Time.frameCount > closedFrame + 1) ReleaseInput();
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

            liveLight.enabled = live;
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
                HideContacts();
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
            scanlines.color = new Color(1f, 1f, 1f, 0.15f);
            SetCrosshair(true);

            // Microwave Synthetic Aperture Radar penetrates clouds seamlessly 24/7
            video.color = Color.white;
            veil.enabled = !live;
            slate.text = live ? "" : "ACQUIRING SAR FEED…";
            slate.color = AvTheme.RailReady;

            UpdateContacts(aimLocal, footprint, state);
        }

        private float Footprint(in OrbitRegime orbit, in LookAngles look, bool night)
        {
            float footprint = Footprints[footprintIndex];
            float minimum = (float)(TheaterTrack.GroundSample(orbit, look, false) * FeedWidth * 0.75);
            gsdLimited = footprint < minimum;
            return footprint;
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

            bar.State.text = OrbitalPlatform.Callsign + " · SAR RADAR · " + (station ? orbit.Name : "NO STATION");
            bar.SetChip(0, "SAR-X", "info");
            bar.SetChip(1, contact ? "AOS" : "LOS", contact ? "live" : "warn");
            bar.SetChip(2, live ? "LIVE" : "NO FEED", live ? "live" : "inert");
            bar.SetChip(3, gsdLimited ? "DIGITAL MAG" : "ZOOM " + (footprintIndex + 1) + "/" + Footprints.Length,
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
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            float clouds = level != null ? Mathf.Clamp01(level.conditions) : 0f;
            values[12].text = "SAR-X · " + Mathf.RoundToInt(clouds * 100f) + "% CLOUD PEN";
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

            string gmt = level != null ? TheaterGrid.Clock(((level.timeOfDay % 24f) + 24f) % 24f * 3600.0) : "--:--";
            hudTopLeft.text = "GMT " + gmt + "   FRM " + (imager != null ? imager.FramesRendered : 0).ToString("00000", Invariant);
            hudTopRight.text = OrbitalPlatform.Callsign + " SAR-X   GET " +
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
            bool help = !string.IsNullOrEmpty(hovered);
            status.text = (help ? "HELP" : "STATUS") + "  ·  " + (help ? hovered : support.Status);
            status.color = help ? AvTheme.TextPrimary : AvTheme.Dim;
            if (statusRail != null) statusRail.color = help ? AvTheme.RailInfo : AvTheme.RailInert;
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
                        line = "READY · FIRE AT CROSSHAIR";
                        enabled = true;
                    }
                }
                taskButtons[i].SetText("[" + (i + 1) + "]  " + name +
                    (ability == PlatformAbility.EmpBurst ? " · FRIENDLY FIRE" : ""));
                taskButtons[i].SetEnabled(enabled);
                taskReady[i] = enabled;
                taskButtons[i].WithTooltip(name + " at the crosshair — " + PlatformAbilities.Info(ability).Summary + " " + line + ".");
                taskStatus[i].text = (cost > 0f ? PlatformWords.Whole(cost) + " ALLOC · " +
                    PlatformWords.Whole(PlatformAbilities.Info(ability).EnergyKj) + " KJ\n" : "") + line;
                taskStatus[i].color = enabled ? AvTheme.RailReady : AvTheme.Dim;
            }

            SupportActionDefinition armed = support.ArmedAction.HasValue ? Find(support.ArmedAction.Value) : null;
            deliverButton.SetText(armed != null ? "[F] DELIVER " + armed.Name : "[F] DELIVER ARMED");
            deliverReady = armed != null && !support.RequestPending;
            deliverButton.SetEnabled(deliverReady);
            deliverButton.WithTooltip(armed == null
                ? "Arm any support action in OPS, then deliver it here at the crosshair."
                : support.RequestPending
                    ? "REQUEST PENDING · the host has not answered the last one yet."
                    : "Deliver the armed " + armed.Name + " at the crosshair instead of a map click.");
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
            // The key handler and the button share one gate: keys must not fire a task the
            // button would have refused. The host still re-validates the request.
            if (!taskReady[index]) return;
            SupportActionDefinition definition = Find(Tasks[index]);
            if (definition == null) return;
            support.RequestAt(definition.Id, AimPoint());
            nextText = 0f;
        }

        private void DeliverArmed()
        {
            if (!deliverReady || !support.ArmedAction.HasValue) return;
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

        private void HideContacts()
        {
            for (int i = 0; i < MaxContactMarkers; i++)
            {
                ContactMarker marker = contactMarkers[i];
                if (marker != null && marker.Active)
                {
                    marker.Active = false;
                    marker.Root.SetActive(false);
                }
            }
        }

        private void UpdateContacts(Vector3 aimLocal, float footprint, in OrbitState state)
        {
            if (imager == null || imager.Camera == null || !live)
            {
                HideContacts();
                return;
            }

            List<Unit> units = UnitRegistry.allUnits;
            if (units == null || units.Count == 0)
            {
                HideContacts();
                return;
            }

            FactionHQ localHq = null;
            if (GameManager.GetLocalPlayer<Player>(out Player localPlayer) && localPlayer != null)
            {
                localHq = localPlayer.HQ;
            }

            Camera cam = imager.Camera;
            float maxDistSq = (footprint * 0.75f) * (footprint * 0.75f);
            int markerIndex = 0;

            for (int i = 0; i < units.Count && markerIndex < MaxContactMarkers; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled) continue;

                Vector3 unitPos = unit.transform.position;
                float dx = unitPos.x - aimLocal.x;
                float dz = unitPos.z - aimLocal.z;
                if (dx * dx + dz * dz > maxDistSq) continue;

                Vector3 vp = cam.WorldToViewportPoint(unitPos);
                if (vp.z <= 0f || vp.x < 0.03f || vp.x > 0.97f || vp.y < 0.03f || vp.y > 0.97f) continue;

                ContactMarker marker = contactMarkers[markerIndex];
                if (marker == null) continue;

                float px = vp.x * VideoWidth;
                float py = -(1f - vp.y) * VideoHeight;
                marker.Transform.anchoredPosition = new Vector2(px, py);

                bool hostile = localHq != null && unit.NetworkHQ != null && unit.NetworkHQ != localHq;
                bool friendly = localHq != null && unit.NetworkHQ == localHq;
                Color col = hostile ? AvTheme.RailDanger : (friendly ? AvTheme.RailReady : AvTheme.RailCaution);

                if (marker.Brackets != null)
                {
                    for (int b = 0; b < marker.Brackets.Length; b++)
                    {
                        if (marker.Brackets[b] != null) marker.Brackets[b].color = col;
                    }
                }
                if (marker.Leader != null) marker.Leader.color = col;
                if (marker.Label != null) marker.Label.color = col;

                string rawName = !string.IsNullOrEmpty(unit.unitName) ? unit.unitName : (unit is Aircraft ? "AIR" : "VEH");
                string tag = hostile ? "TGT: " : (friendly ? "FRD: " : "");
                marker.Label.text = tag + rawName.ToUpperInvariant();

                Vector3 vel = (unit.rb != null) ? unit.rb.velocity : unit.transform.forward * unit.speed;
                float speed = vel.magnitude;
                if (speed > 2f)
                {
                    Vector3 leadWorld = unitPos + vel.normalized * 50f;
                    Vector3 leadVp = cam.WorldToViewportPoint(leadWorld);
                    Vector2 screenDir = new Vector2((leadVp.x - vp.x) * VideoWidth, (leadVp.y - vp.y) * VideoHeight);
                    if (screenDir.sqrMagnitude > 0.001f)
                    {
                        marker.Leader.enabled = true;
                        float angle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg;
                        marker.Leader.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
                        float len = Mathf.Clamp(speed * 0.35f, 10f, 36f);
                        marker.Leader.rectTransform.sizeDelta = new Vector2(len, 1.5f);
                    }
                    else
                    {
                        marker.Leader.enabled = false;
                    }
                }
                else
                {
                    marker.Leader.enabled = false;
                }

                marker.Active = true;
                marker.Root.SetActive(true);
                markerIndex++;
            }

            for (int i = markerIndex; i < MaxContactMarkers; i++)
            {
                ContactMarker marker = contactMarkers[i];
                if (marker != null && marker.Active)
                {
                    marker.Active = false;
                    marker.Root.SetActive(false);
                }
            }
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
