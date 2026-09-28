using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Features.Support.Visuals;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// SPACE › SENSOR TASKING. A SAR-first product viewport occupies the left of a persistent
    /// faction task rail. Actions show host-approved availability and costs next to the image they
    /// use; EO is an optional local aiming view. No local scene unit is classified or labelled.
    ///
    /// <para>Everything shown is client-local; a task is an ordinary support request at the aim point
    /// (<c>RequestAt</c>, <c>CallArmedAt</c>, <c>SetUplinkAim</c>). The imager camera exists only while
    /// this room is on screen.</para>
    /// </summary>
    internal sealed class ImagerView : IOpsView
    {
        private const float SlewTimeConstant = 0.35f;
        private const float GyroSlewTimeConstant = 0.15f;
        private const float KeySlewFraction = 0.55f;
        private const float ClickSlop = 8f;
        private const float DoubleClickSeconds = 0.3f;
        private const float ScaleBarPixels = 200f;
        private const float HeaderHeight = 76f;
        private const float FooterHeight = 72f;
        private const int FeedPixelsWide = 640;
        private const float FramesPerSecond = 6f;
        private static readonly double FieldOfRegard = 62.0 * OrbitMath.Deg;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly float[] Footprints = { 32000f, 16000f, 8000f, 4000f, 2000f, 1000f, 500f };
        private static Texture2D sharedScanlines;
        private static readonly SupportActionId[] Tasks =
            { SupportActionId.Recon, SupportActionId.ElintSweep, SupportActionId.Artillery, SupportActionId.Emp, SupportActionId.MtiSweep };

        private readonly SupportManager support;
        private readonly PlatformProducts products;
        private readonly Action openStation;

        private sealed class Softkey
        {
            public RoomControl Control;
            public Image Fill;
            public Image[] Edge;
            public TMP_Text Title, Facts;
        }

        private readonly Softkey[] softkeys = new Softkey[Tasks.Length + 2];
        private readonly bool[] taskReady = new bool[Tasks.Length];
        private readonly Rect[] sections = new Rect[4];

        private Rect area, sensorRect, railRect, footerRect;
        private RectTransform room;
        private RawImage feed, scanlines, pip;
        private Image veil, reticle, sweep, liveDot;
        private TMP_Text aimStatus, modeLabel, linkStatus;
        private Rect cameraControls;
        private RoomControl zoomOutButton, zoomInButton, recenterButton;
        private TMP_Text osd, cornerTL, cornerTR, cornerBL, cornerBR, slate, scaleLabel, pipCaption;
        private Image passFill, pipFrame;
        private Image[] pipEdges;
        private RectTransform north, scaleGroup, signalPlate;
        private Rect feedCrop = new Rect(0f, 0f, 1f, 1f);
        private Rect pipRect;

        private double aimX, aimZ, commandX, commandZ;
        private float aimHeight, nextHeight, lastTime = -1f;
        private int footprintIndex = 4;
        private bool live, dragging, shown, deliverReady, showOptical;
        private Vector3 lastMouse, lastClickPosition;
        private float lastClickTime = -10f;
        private float entrance = 1f;
        private int productSerial = -1;
        private Texture fixtureFeed;
        private SatelliteImager imager;
        private int feedPixelsHigh = 600;

        public ImagerView(SupportManager support, PlatformProducts products, Action openStation)
        {
            this.support = support;
            this.products = products;
            this.openStation = openStation;
        }

        public OpsDomain Domain => OpsDomain.Space;
        public float EntranceSeconds => ImagerStyle.EntranceSeconds;
        public Rect Hero => sections[0];
        public IReadOnlyList<Rect> Sections => sections;

        /// <summary>True while the room is on screen; the imager renders only then.</summary>
        public bool Active => shown;

        // ---- Build -------------------------------------------------------------------------------

        public void Build(RectTransform host, Rect at)
        {
            ImagerStyle.Resolve();
            OpsSprites.Ensure();
            room = host;
            area = at;
            float w = at.width, h = at.height;
            float railW = Mathf.Clamp(w * 0.32f, 372f, 612f);
            float sensorW = w - railW - 48f;
            float workH = h - HeaderHeight - FooterHeight - 20f;
            sensorRect = new Rect(16f, -HeaderHeight - 8f, sensorW, workH);
            railRect = new Rect(sensorRect.xMax + 12f, sensorRect.y, railW, workH);
            footerRect = new Rect(16f, -(h - FooterHeight + 8f), w - 32f, FooterHeight - 16f);
            Image surface = Chrome.Panel(host, new Rect(0f, 0f, w, h), ImagerStyle.Pod.WithAlpha(1f));
            surface.raycastTarget = true;

            feedPixelsHigh = Mathf.Clamp(Mathf.RoundToInt(FeedPixelsWide * workH / Mathf.Max(1f, sensorW)), 64, 2048);
            DestroyImager();
            feedCrop = new Rect(0f, 0f, 1f, 1f);
            feed = Raw(host, "Feed", sensorRect);
            feed.uvRect = feedCrop;
            feed.enabled = false;

            if (sharedScanlines == null)
            {
                sharedScanlines = new Texture2D(1, 4, TextureFormat.RGBA32, false)
                {
                    name = "BoscaliImagerScanlines",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave
                };
                sharedScanlines.SetPixel(0, 0, Color.clear);
                sharedScanlines.SetPixel(0, 1, Color.clear);
                sharedScanlines.SetPixel(0, 2, Color.clear);
                sharedScanlines.SetPixel(0, 3, Color.black);
                sharedScanlines.Apply(false);
            }
            scanlines = Raw(host, "Scanlines", sensorRect);
            scanlines.texture = sharedScanlines;
            scanlines.uvRect = new Rect(0f, 0f, 1f, workH / 4f);
            scanlines.color = Color.white.WithAlpha(0.045f);
            veil = Chrome.Panel(host, sensorRect, ImagerStyle.Pod.WithAlpha(1f));

            BuildSymbology(host, w, h);
            BuildSoftkeys(host, w, h);
            BuildPip(host, w, h);
            BuildCameraControls(host, w);
            sweep = Chrome.Panel(host, new Rect(sensorRect.x, sensorRect.y, sensorRect.width, 54f),
                ImagerStyle.Ink.WithAlpha(0.35f), OpsSprites.Scan);
            sweep.type = Image.Type.Simple;
            sweep.enabled = false;

            sections[0] = new Rect(sensorRect.x, -sensorRect.y, sensorRect.width, sensorRect.height);
            sections[1] = new Rect(0f, 0f, w, HeaderHeight);
            sections[2] = new Rect(railRect.x, -railRect.y, railRect.width, railRect.height);
            sections[3] = new Rect(footerRect.x, -footerRect.y, footerRect.width, footerRect.height);
        }

        private static RawImage Raw(RectTransform parent, string name, Rect at)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            RawImage raw = go.GetComponent<RawImage>();
            raw.rectTransform.SetParent(parent, false);
            Chrome.Place(raw.rectTransform, at);
            raw.raycastTarget = false;
            return raw;
        }

        private void BuildSymbology(RectTransform host, float w, float h)
        {
            Color ink = ImagerStyle.Ink;
            Chrome.Panel(host, new Rect(0f, 0f, w, HeaderHeight), ImagerStyle.Halo.WithAlpha(0.96f));
            Chrome.Rule(host, new Rect(0f, -HeaderHeight + 2f, w, 2f), ink.WithAlpha(0.5f));
            ImagerStyle.Symbol(host, "BASTION / SENSOR TASKING", new Rect(22f, -12f, w * 0.45f, 28f), 22f);
            osd = ImagerStyle.Symbol(host, "", new Rect(22f, -45f, w - 390f, 20f), 14f);
            liveDot = Chrome.Panel(host, new Rect(w - 357f, -22f, 11f, 11f), ink, OpsSprites.Dot);
            liveDot.type = Image.Type.Simple;
            linkStatus = ImagerStyle.Symbol(host, "", new Rect(w - 335f, -14f, 312f, 28f), 14f,
                TextAlignmentOptions.MidlineRight);
            Chrome.Panel(host, sensorRect, ImagerStyle.Halo.WithAlpha(0.2f), OpsSprites.Brackets);
            float sx = sensorRect.x, sy = sensorRect.y, sw = sensorRect.width, sh = sensorRect.height;
            Chrome.Panel(host, new Rect(sx + 12f, sy - 12f, sw - 24f, 37f), ImagerStyle.Halo.WithAlpha(0.78f));
            cornerTL = ImagerStyle.Symbol(host, "", new Rect(sx + 24f, sy - 18f, sw * 0.55f, 24f), 16f);
            cornerTR = ImagerStyle.Symbol(host, "", new Rect(sx + sw * 0.56f, sy - 18f, sw * 0.40f, 24f), 16f,
                TextAlignmentOptions.MidlineRight);
            Chrome.Panel(host, new Rect(sx + 12f, sy - sh + 41f, sw - 24f, 31f), ImagerStyle.Halo.WithAlpha(0.82f));
            cornerBL = ImagerStyle.Symbol(host, "", new Rect(sx + 24f, sy - sh + 33f, sw * 0.56f, 22f), 14f);
            cornerBR = ImagerStyle.Symbol(host, "", new Rect(sx + sw * 0.56f, sy - sh + 33f, sw * 0.40f, 22f), 14f,
                TextAlignmentOptions.MidlineRight);
            passFill = Chrome.Panel(host, new Rect(sx + 12f, sy - 49f, 0f, 3f), ink);
            reticle = Chrome.Panel(host, new Rect(sx + sw * 0.5f - 72f, sy - sh * 0.5f + 72f,
                144f, 144f), ink, OpsSprites.Reticle);
            reticle.type = Image.Type.Simple;

            var northObject = new GameObject("North", typeof(RectTransform));
            north = (RectTransform)northObject.transform;
            north.SetParent(host, false);
            north.anchorMin = north.anchorMax = new Vector2(0f, 1f);
            north.pivot = new Vector2(0.5f, 0.5f);
            north.sizeDelta = new Vector2(44f, 44f);
            north.anchoredPosition = new Vector2(sx + sw - 64f, sy - 112f);
            Chrome.Rule(north, new Rect(21f, -6f, 2f, 26f), ink);
            Image head = Chrome.Panel(north, new Rect(14f, 6f, 16f, 14f), ink, OpsSprites.Triangle);
            Lines.Centre(head.rectTransform, 22f, -1f, 16f, 14f);
            head.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
            ImagerStyle.Symbol(north, "N", new Rect(0f, 22f, 44f, 16f), ImagerStyle.Small, TextAlignmentOptions.Center);

            var scaleObject = new GameObject("Scale", typeof(RectTransform));
            scaleGroup = (RectTransform)scaleObject.transform;
            scaleGroup.SetParent(host, false);
            Chrome.Place(scaleGroup, sensorRect);
            float scaleY = -sh + 88f;
            Chrome.Rule(scaleGroup, new Rect(25f, scaleY, ScaleBarPixels, 2f), ink);
            Chrome.Rule(scaleGroup, new Rect(25f, scaleY + 6f, 2f, 8f), ink);
            Chrome.Rule(scaleGroup, new Rect(23f + ScaleBarPixels, scaleY + 6f, 2f, 8f), ink);
            scaleLabel = ImagerStyle.Symbol(scaleGroup, "", new Rect(38f + ScaleBarPixels, scaleY + 8f, 160f, 18f), 13f);

            float plateW = Mathf.Min(650f, sw - 66f);
            const float plateH = 156f;
            Image plate = Chrome.Panel(host, new Rect(sx + (sw - plateW) * 0.5f,
                sy - sh * 0.5f + plateH * 0.5f, plateW, plateH), ImagerStyle.Halo.WithAlpha(0.96f));
            signalPlate = plate.rectTransform;
            plate.raycastTarget = false;
            Chrome.Outline(signalPlate, new Rect(0f, 0f, plateW, plateH), ink.WithAlpha(0.75f));
            Chrome.Rule(signalPlate, new Rect(20f, -38f, plateW - 40f, 1f), ink.WithAlpha(0.5f));
            ImagerStyle.Symbol(signalPlate, "SENSOR PRODUCT / SIGNAL STATE", new Rect(24f, -10f, plateW - 48f, 20f),
                13f, TextAlignmentOptions.Center);
            slate = ImagerStyle.Symbol(signalPlate, "", new Rect(24f, -49f, plateW - 48f, 86f), 22f, TextAlignmentOptions.Center);
            slate.enableWordWrapping = true;

            Chrome.Panel(host, footerRect, ImagerStyle.Halo.WithAlpha(0.96f));
            Chrome.Outline(host, footerRect, ink.WithAlpha(0.38f));
            aimStatus = ImagerStyle.Symbol(host, "", new Rect(footerRect.x + 16f, footerRect.y - 8f,
                footerRect.width - 360f, 22f), 15f);
            ImagerStyle.Symbol(host, "SAR SCAN TO FORM · EO FOR MANUAL AIM", new Rect(footerRect.x + 16f,
                footerRect.y - 30f, footerRect.width - 360f, 18f), 12f, dim: true);
        }

        private void BuildSoftkeys(RectTransform host, float w, float h)
        {
            Chrome.Panel(host, railRect, ImagerStyle.Halo.WithAlpha(0.94f));
            Chrome.Outline(host, railRect, ImagerStyle.Ink.WithAlpha(0.5f));
            ImagerStyle.Symbol(host, "TASKING / HOST-VALIDATED", new Rect(railRect.x + 16f,
                railRect.y - 12f, railRect.width - 32f, 24f), 17f);
            ImagerStyle.Symbol(host, "SELECT A SENSOR OR EFFECT AT THE AIM POINT", new Rect(railRect.x + 16f,
                railRect.y - 39f, railRect.width - 32f, 18f), 12f, dim: true);
            float gap = 6f;
            float keyW = railRect.width - 24f;
            float keyH = (railRect.height - 72f - gap * 5f) / 6f;
            for (int i = 0; i < softkeys.Length; i++)
            {
                int index = i;
                Rect at = i == Tasks.Length + 1
                    ? new Rect(footerRect.xMax - 316f, footerRect.y - 8f, 300f, footerRect.height - 16f)
                    : new Rect(railRect.x + 12f, railRect.y - 65f - i * (keyH + gap), keyW, keyH);
                Action click = i < Tasks.Length ? (Action)(() => Task(index)) : i == Tasks.Length ? DeliverArmed : (Action)(() => openStation?.Invoke());
                var key = new Softkey();
                key.Control = RoomControl.Create(host, at, click, "Softkey");
                RectTransform rect = key.Control.Rect;
                key.Fill = Chrome.Panel(rect, new Rect(0f, 0f, at.width, at.height), ImagerStyle.Halo.WithAlpha(0.7f));
                key.Edge = Chrome.Outline(rect, new Rect(0f, 0f, at.width, at.height), ImagerStyle.Ink);
                key.Title = ImagerStyle.Symbol(rect, "", new Rect(14f, -8f, at.width - 28f, 24f), 16f);
                key.Facts = ImagerStyle.Symbol(rect, "", new Rect(14f, -35f, at.width - 28f,
                    Mathf.Max(24f, at.height - 39f)), 13f, TextAlignmentOptions.TopLeft, true);
                key.Facts.enableWordWrapping = true;
                key.Control.Changed = _ => PaintKey(key);
                softkeys[i] = key;
            }
        }

        private static void PaintKey(Softkey key)
        {
            RoomControl c = key.Control;
            key.Fill.color = !c.Enabled ? ImagerStyle.Halo.WithAlpha(0.45f)
                : c.Pressed ? ImagerStyle.Ink.WithAlpha(0.5f) : c.Hovered ? ImagerStyle.Ink.WithAlpha(0.22f) : ImagerStyle.Halo.WithAlpha(0.72f);
            Color ink = c.Enabled ? ImagerStyle.Ink : ImagerStyle.Dim;
            for (int e = 0; e < key.Edge.Length; e++) key.Edge[e].color = c.Enabled ? ImagerStyle.Ink : ImagerStyle.Dim.WithAlpha(0.5f);
            key.Title.color = ink;
            key.Facts.color = ink;
        }

        private void BuildPip(RectTransform host, float w, float h)
        {
            float pw = Mathf.Min(260f, sensorRect.width * 0.28f), ph = Mathf.Min(174f, sensorRect.height * 0.28f);
            pipRect = new Rect(sensorRect.xMax - 15f - pw, sensorRect.y - sensorRect.height + ph + 64f, pw, ph);
            pipFrame = Chrome.Panel(host, pipRect, ImagerStyle.Halo.WithAlpha(0.85f));
            pipEdges = Chrome.Outline(host, pipRect, ImagerStyle.Ink);
            pip = Raw(host, "Product", new Rect(pipRect.x + 6f, pipRect.y - 6f, pw - 12f, ph - 34f));
            // Formed images keep range on the horizontal axis; flip so the product is a rotation, not a mirror.
            pip.uvRect = new Rect(1f, 0f, -1f, 1f);
            pip.enabled = false;
            pipCaption = ImagerStyle.Symbol(host, "", new Rect(pipRect.x + 8f, pipRect.y - ph + 26f, pw - 16f, 20f), 12f);
            pipFrame.enabled = false;
            pipCaption.enabled = false;
            foreach (Image edge in pipEdges) edge.enabled = false;
        }

        private void BuildCameraControls(RectTransform host, float w)
        {
            float buttonW = Mathf.Min(142f, (sensorRect.width - 54f) / 4f);
            cameraControls = new Rect(sensorRect.x + 12f, sensorRect.y - 61f,
                buttonW * 4f + 18f, 44f);
            Chrome.Panel(host, cameraControls, ImagerStyle.Halo.WithAlpha(0.88f));
            float x = cameraControls.x + 5f, y = cameraControls.y - 5f;
            zoomOutButton = CameraButton(host, new Rect(x, y, buttonW, 34f), "− ZOOM [Q]", () => Zoom(-1), out _);
            zoomInButton = CameraButton(host, new Rect(x + buttonW + 3f, y, buttonW, 34f), "+ ZOOM [E]", () => Zoom(1), out _);
            recenterButton = CameraButton(host, new Rect(x + (buttonW + 3f) * 2f, y, buttonW, 34f), "NADIR [C]", Recenter, out _);
            CameraButton(host, new Rect(x + (buttonW + 3f) * 3f, y, buttonW, 34f), "EO VIEW [V]", ToggleFeed, out modeLabel);
        }

        private void ToggleFeed()
        {
            showOptical = !showOptical;
            if (!showOptical) DestroyImager();
            lastTime = -1f;
        }

        private static RoomControl CameraButton(RectTransform host, Rect at, string title, Action action, out TMP_Text label)
        {
            RoomControl control = RoomControl.Create(host, at, action, "CameraControl");
            Image fill = Chrome.Panel(control.Rect, new Rect(0f, 0f, at.width, at.height), ImagerStyle.Pod.WithAlpha(1f));
            Chrome.Outline(control.Rect, new Rect(0f, 0f, at.width, at.height), ImagerStyle.Dim);
            label = ImagerStyle.Symbol(control.Rect, title, new Rect(6f, 0f, at.width - 12f, at.height), 12f, TextAlignmentOptions.Center);
            control.Changed = c => fill.color = !c.Enabled ? ImagerStyle.Pod.WithAlpha(0.6f)
                : c.Hovered ? ImagerStyle.Ink.WithAlpha(0.25f) : ImagerStyle.Pod.WithAlpha(1f);
            return control;
        }

        private void Recenter()
        {
            OrbitalPlatform platform = support != null ? support.LocalPlatform : null;
            if (platform == null || !platform.Exists) return;
            OrbitState state = platform.State(support.OrbitNow);
            commandX = state.SubX;
            commandZ = state.SubZ;
        }

        // ---- Lifecycle ---------------------------------------------------------------------------

        public void Show(object context)
        {
            GlobalPosition start = context is GlobalPosition aim ? aim : support != null && support.UplinkAimSet ? support.UplinkAim : default;
            commandX = aimX = start.x;
            commandZ = aimZ = start.z;
            nextHeight = 0f;
            dragging = false;
            lastTime = -1f;
            Array.Clear(taskReady, 0, taskReady.Length);
            deliverReady = false;
            shown = true;
            showOptical = false;
        }

        public void Hide()
        {
            if (!shown) return;
            shown = false;
            dragging = false;
            live = false;
            DestroyImager();
            support?.SetUplinkAim(AimPoint());
            Chrome.ClearTooltip();
        }

        private void DestroyImager()
        {
            if (imager != null) UnityEngine.Object.Destroy(imager.gameObject);
            imager = null;
        }

        /// <summary>The feed acquires: one scan line sweeps down, then the picture holds steady.</summary>
        public void Entrance(float progress)
        {
            entrance = Mathf.Clamp01(progress);
            if (sweep == null) return;
            bool on = entrance < 1f;
            sweep.enabled = on;
            if (on)
            {
                RectTransform r = sweep.rectTransform;
                r.anchoredPosition = new Vector2(sensorRect.x,
                    sensorRect.y - (sensorRect.height - 54f) * entrance);
            }
            feed.color = Color.white.WithAlpha(Mathf.Clamp01(0.3f + entrance));
        }

        public void Refresh(double now, float time, bool textTick)
        {
            if (support == null) return;
            // A formed SAR product is static between text ticks. Keep the optional EO camera
            // responsive without making every cockpit frame query orbit and terrain for SAR.
            if (!showOptical && !textTick) return;
            float dt = lastTime < 0f ? 0f : Mathf.Min(time - lastTime, 0.1f);
            lastTime = time;
            OrbitalPlatform platform = support.LocalPlatform;
            bool station = platform != null && platform.Exists;
            OrbitState state = station ? platform.State(support.OrbitNow) : default;
            if (!showOptical && products != null && products.HasProduct && productSerial != support.RadarScanSerial)
            {
                productSerial = support.RadarScanSerial;
                GlobalPosition target = support.RadarScanTarget;
                commandX = aimX = target.x;
                commandZ = aimZ = target.z;
            }
            float constant = station && platform.Stats(support.OrbitNow).Stabilised ? GyroSlewTimeConstant : SlewTimeConstant;
            double k = dt > 0f ? 1.0 - Math.Exp(-dt / constant) : 0.0;
            aimX += (commandX - aimX) * k;
            aimZ += (commandZ - aimZ) * k;
            bool slewing = Math.Abs(commandX - aimX) + Math.Abs(commandZ - aimZ) > 10.0;
            if (time >= nextHeight)
            {
                nextHeight = time + 0.5f;
                var probe = new GlobalPosition((float)aimX, 0f, (float)aimZ);
                aimHeight = SupportTargeting.TryMapPoint(probe, out Vector3 ground) ? ground.ToGlobalPosition().y : 0f;
            }
            Paint(platform, state, support.OrbitNow, slewing, textTick);
        }

        /// <summary>Paint the feed and the symbology. The harness calls it with a fixture station.</summary>
        internal void Paint(OrbitalPlatform platform, in OrbitState state, double now, bool slewing, bool textTick)
        {
            bool station = platform != null && platform.Exists;
            LookAngles look = station ? TheaterTrack.Look(state, aimX, aimZ) : LookAngles.Hidden;
            RenderFeed(platform, state, look, now);
            liveDot.enabled = live;
            if (fixtureFeed != null)
            {
                pip.texture = fixtureFeed;
                SetPipVisible(showOptical && station);
            }
            else if (products != null)
            {
                pip.texture = products.Scan.Image;
                bool showProduct = showOptical && station && products.HasProduct;
                SetPipVisible(showProduct);
            }
            else SetPipVisible(false);
            if (textTick) WriteText(platform, state, look, now, slewing);
        }

        private void SetPipVisible(bool visible)
        {
            pip.enabled = visible;
            pipFrame.enabled = visible;
            pipCaption.enabled = visible;
            foreach (Image edge in pipEdges) edge.enabled = visible;
        }

        public bool HandleKeys()
        {
            if (support == null) return false;
            if (!showOptical)
            {
                if (Input.GetKeyDown(KeyCode.V)) ToggleFeed();
                for (int i = 0; i < Tasks.Length; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) Task(i);
                if (Input.GetKeyDown(KeyCode.F)) DeliverArmed();
                if (Input.GetKeyDown(KeyCode.Tab)) openStation?.Invoke();
                return true;
            }
            OrbitalPlatform platform = support.LocalPlatform;
            bool station = platform != null && platform.Exists;
            OrbitState state = station ? platform.State(support.OrbitNow) : default;
            Vector3 mouse = Input.mousePosition;
            bool overFeed = OverFeed(mouse);
            if (Input.GetMouseButtonDown(0) && overFeed)
            {
                dragging = true;
                lastMouse = mouse;
                if (Time.unscaledTime - lastClickTime < DoubleClickSeconds && (mouse - lastClickPosition).sqrMagnitude <= ClickSlop * ClickSlop)
                {
                    CentreOn(mouse, state, station);
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
                if (delta.sqrMagnitude > 0f) Slew(delta.x, delta.y, state, station);
            }
            if (Input.GetMouseButtonUp(0)) dragging = false;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && overFeed) Zoom(wheel > 0f ? 1 : -1);
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.KeypadPlus)) Zoom(1);
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.KeypadMinus)) Zoom(-1);
            float right = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) -
                          (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float up = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) -
                       (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            if (right != 0f || up != 0f)
            {
                float pixels = FeedScreenWidth() * KeySlewFraction * Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                // Keys move the sensor; dragging moves the picture. Opposite signs.
                Slew(-right * pixels, -up * pixels, state, station);
            }
            if (Input.GetKeyDown(KeyCode.C)) Recenter();
            if (Input.GetKeyDown(KeyCode.V)) ToggleFeed();
            for (int i = 0; i < Tasks.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) Task(i);
            if (Input.GetKeyDown(KeyCode.F)) DeliverArmed();
            if (Input.GetKeyDown(KeyCode.Tab)) openStation?.Invoke();
            return true;
        }

        /// <summary>Right-click inside is "back": return to the station wall.</summary>
        public void RightClickInside() => openStation?.Invoke();

        // ---- Feed --------------------------------------------------------------------------------

        private void RenderFeed(OrbitalPlatform platform, in OrbitState state, in LookAngles look, double now)
        {
            string message = null;
            if (platform == null || !platform.Exists) message = "NO STATION ON ORBIT\nTASK MAP [TAB] → ENGINEERING → LAUNCH CORE";
            else if (!platform.Fitted(ModuleKind.Imager)) message = "NO SPY IMAGER FITTED\nDOCK AN IMG MODULE IN ENGINEERING";
            else if (!platform.FittedOnline(ModuleKind.Imager, now)) message = "IMAGER OFFLINE\nWAIT FOR MODULE RECOVERY · [TAB] STATION";
            else if (platform.Brownout) message = "POWER RESERVE DEPLETED\nFIT SOLAR / REACTOR / BATTERY · [TAB] STATION";
            else if (platform.HoldAt(now) != PlatformHold.None)
                message = PlatformWords.Hold(platform.HoldAt(now)) + " · T-" + PlatformWords.Clock(platform.CycleStart - now);
            else if (!state.InPass) message = "STATION REPOSITIONING\nFEED RESUMES ON ARRIVAL";
            else if (!look.Visible || look.OffNadir > FieldOfRegard) message = "AIM BEYOND FIELD OF REGARD\nPRESS C FOR NADIR";

            if (message != null)
            {
                live = false;
                Set(slate, message);
                SetSignalState(false);
                veil.enabled = true;
                // Hold the last frame, dimmed, under the slate.
                feed.enabled = imager != null && imager.FramesRendered > 0;
                if (feed.enabled) feed.texture = imager.Output;
                reticle.enabled = false;
                return;
            }
            OrbitRegime orbit = platform.Orbit;
            float footprint = Footprint(orbit, look);
            if (fixtureFeed != null)
            {
                // Offline renders: a supplied scene instead of the client-local collect.
                live = true;
                feed.texture = fixtureFeed;
                feed.enabled = true;
                veil.enabled = false;
                reticle.enabled = true;
                Set(slate, "");
                SetSignalState(true);
                return;
            }
            if (!showOptical)
            {
                SarCollector scan = products != null ? products.Scan : null;
                live = platform != null && platform.Exists && scan != null && products.HasProduct;
                feed.texture = live ? scan.Image : null;
                feed.uvRect = new Rect(1f, 0f, -1f, 1f);
                feed.enabled = live;
                veil.enabled = !live;
                veil.color = ImagerStyle.Pod.WithAlpha(1f);
                reticle.enabled = true;
                Set(slate, live ? "" : "SAR PRODUCT NOT FORMED\nUSE [1] RECON / SAR SWEEP");
                SetSignalState(live);
                north.gameObject.SetActive(false);
                scaleGroup.gameObject.SetActive(false);
                return;
            }
            // EO is an optional local camera view. SAR remains the default main product.
            feed.uvRect = feedCrop;
            if (imager == null) imager = SatelliteImager.Create(FeedPixelsWide, feedPixelsHigh);
            Vector3 aimLocal = new GlobalPosition((float)aimX, aimHeight, (float)aimZ).ToLocalPosition();
            float cosEl = (float)Math.Cos(look.Elevation);
            var los = new Vector3((float)look.AzimuthX * cosEl, (float)Math.Sin(look.Elevation), (float)look.AzimuthZ * cosEl);
            var along = new Vector3((float)state.Pass.DirX, 0f, (float)state.Pass.DirZ);
            imager.Aim(aimLocal, los, along, footprint, IsNight(), FramesPerSecond);
            north.localEulerAngles = new Vector3(0f, 0f, (float)(state.Pass.Heading / OrbitMath.Deg));
            live = imager.FramesRendered > 0;
            feed.texture = imager.Output;
            feed.enabled = live;
            veil.enabled = !live;
            reticle.enabled = true;
            Set(slate, live ? "" : "ACQUIRING OPTICAL FEED…");
            SetSignalState(live);
        }

        private void SetSignalState(bool feedReady)
        {
            if (signalPlate.gameObject.activeSelf == feedReady) signalPlate.gameObject.SetActive(!feedReady);
            if (north.gameObject.activeSelf != feedReady) north.gameObject.SetActive(feedReady);
            if (scaleGroup.gameObject.activeSelf != feedReady) scaleGroup.gameObject.SetActive(feedReady);
        }

        private float Footprint(in OrbitRegime orbit, in LookAngles look)
        {
            return Footprints[footprintIndex];
        }

        private void WriteText(OrbitalPlatform platform, in OrbitState state, in LookAngles look, double now, bool slewing)
        {
            Set(linkStatus, "OPS " + (support != null ? PlatformWords.Whole(support.LocalOpsReserve) : "—") +
                (support != null && support.RequestPending ? " · HOST PENDING" : " · HOST CONFIRMED"));
            if (modeLabel != null) Set(modeLabel, showOptical ? "SAR VIEW [V]" : "EO VIEW [V]");
            zoomOutButton.SetEnabled(showOptical);
            zoomInButton.SetEnabled(showOptical);
            recenterButton.SetEnabled(showOptical);
            zoomOutButton.WithTooltip(showOptical ? "Decrease EO camera zoom." : "Switch to EO view to change camera zoom.");
            zoomInButton.WithTooltip(showOptical ? "Increase EO camera zoom." : "Switch to EO view to change camera zoom.");
            recenterButton.WithTooltip(showOptical ? "Look down the station ground track." : "Switch to EO view to recenter the camera.");
            if (!showOptical)
            {
                SarCollector product = products != null ? products.Scan : null;
                string phase = fixtureFeed != null ? "PRODUCT READY" : product != null && product.Phase == SarPhase.Collecting
                    ? "COLLECTING " + Mathf.RoundToInt(product.Progress * 100f) + "%"
                    : product != null && product.Phase == SarPhase.Processing
                        ? "PROCESSING " + Mathf.RoundToInt(product.ProcessingProgress * 100f) + "%"
                        : product != null && products.HasProduct ? "PRODUCT READY" : "NO PRODUCT";
                Set(osd, "SAR / FORMED RASTER  ·  " + phase + "  ·  AIM " + TheaterGrid.Kilometres(aimX, aimZ));
                Set(cornerTL, "SAR / RANGE × AZIMUTH");
                Set(cornerTR, phase);
                Set(cornerBL, "MONOCHROME SENSOR PRODUCT");
                Set(cornerBR, "AIM " + TheaterGrid.Kilometres(aimX, aimZ));
                Set(scaleLabel, "");
                Set(aimStatus, slewing ? "SLEWING · RELEASE TO SETTLE" : "SAR PRODUCT · RECON TO FORM · V · EO VIEW");
                WritePip(product, platform != null && platform.Exists);
                WriteSoftkeys();
                return;
            }
            bool station = platform != null && platform.Exists;
            OrbitRegime orbit = station ? platform.Orbit : OrbitRegimes.Get(OrbitRegimes.Mid);
            float footprint = station && look.Visible ? Footprint(orbit, look) : Footprints[footprintIndex];
            bool night = IsNight();
            double gsd = station ? TheaterTrack.GroundSample(orbit, look, night) : 0.0;
            SarCollector scan = products != null ? products.Scan : null;
            string frameWord = scan == null ? "IMG ---" : scan.Phase == SarPhase.Complete ? "IMG HOLD"
                : scan.Phase == SarPhase.Collecting ? "IMG " + Mathf.RoundToInt(scan.Progress * 100f) + "%"
                : scan.Phase == SarPhase.Processing ? "PRC " + Mathf.RoundToInt(scan.ProcessingProgress * 100f) + "%" : "IMG ---";
            PlatformStats stats = station ? platform.Stats(now) : default;
            Set(osd, "EO / " + (station ? orbit.Code + " " + TheaterGrid.Km(orbit.Altitude) + " KM" : "NO STATION") +
                     "  ·  FOV " + TheaterGrid.Km(footprint) + " KM  ·  " + frameWord +
                     (live ? "  ·  LIVE" : "  ·  ACQUIRING"));
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            string gmt = level != null ? TheaterGrid.Clock(((level.timeOfDay % 24f) + 24f) % 24f * 3600.0) : "--:--";
            Set(cornerTL, "EO / LIVE   FRAME " + (imager != null ? imager.FramesRendered.ToString("D6", Invariant) : "------") +
                "   GMT " + gmt + "   GET " + (station ? TheaterGrid.Elapsed(platform.Elapsed(now)) : "--:--:--"));
            if (station && state.InPass)
            {
                Set(cornerTR, "FIXED STATION · " + StationKeeping.Name(platform.PositionIndex));
                passFill.rectTransform.sizeDelta = new Vector2(sensorRect.width - 24f, 3f);
            }
            else
            {
                Set(cornerTR, station ? PlatformWords.Phase(platform, now) : "NO STATION");
                passFill.rectTransform.sizeDelta = new Vector2(0f, 3f);
            }
            Set(cornerBL, look.Visible
                ? "EL " + Deg(look.Elevation) + "  AZ " + Bearing(look.AzimuthDeg) + "  SLANT " + TheaterGrid.Km(look.SlantRange) + " KM"
                : station ? PlatformWords.Phase(platform, now) : "NO TRACK");
            Set(cornerBR, "GSD " + (look.Visible ? gsd.ToString("0.00", Invariant) + " M" : "—") + "   AIM " + TheaterGrid.Kilometres(aimX, aimZ));
            Set(scaleLabel, live ? Distance(footprint * ScaleBarPixels / Mathf.Max(1f, sensorRect.width)) : "");
            Set(aimStatus, slewing ? "SLEWING · RELEASE TO SETTLE"
                : "DRAG / WASD · SLEW   WHEEL · ZOOM   DOUBLE-CLICK · AIM");
            WritePip(scan, station);
            WriteSoftkeys();
        }

        private void WritePip(SarCollector scan, bool station)
        {
            if (!station) { Set(pipCaption, "POD OFFLINE · STATION REQUIRED"); return; }
            if (fixtureFeed != null) { Set(pipCaption, "SAR PRODUCT READY · GRAYSCALE"); return; }
            if (scan == null) { Set(pipCaption, "NO PRODUCT"); return; }
            switch (scan.Phase)
            {
                case SarPhase.Collecting: Set(pipCaption, "PRODUCT · COLLECTING " + Mathf.RoundToInt(scan.Progress * 100f) + "%"); break;
                case SarPhase.Processing: Set(pipCaption, "PRODUCT · PROCESSING " + Mathf.RoundToInt(scan.ProcessingProgress * 100f) + "%"); break;
                case SarPhase.Complete: Set(pipCaption, "SAR PRODUCT READY · GRAYSCALE"); break;
                default: Set(pipCaption, "NO PRODUCT · TASK A RADAR SCAN [1]"); break;
            }
        }

        private void WriteSoftkeys()
        {
            bool bypass = support != null && support.BypassRequirements;
            for (int i = 0; i < Tasks.Length; i++)
            {
                Softkey key = softkeys[i];
                SupportActionDefinition definition = Find(Tasks[i]);
                PlatformAbility ability = SupportManager.OrbitalAbility(Tasks[i]).GetValueOrDefault();
                string name = definition != null ? definition.Name : PlatformAbilities.Info(ability).Name;
                // The same facts as the MFD's ACTIONS rows: one presenter.
                AbilityFacts facts = definition != null && support != null
                    ? AbilityStatus.For(support, definition, bypass)
                    : new AbilityFacts(AbilityTone.Locked, "UNAVAILABLE ON THIS SERVER", "—", false, false);
                bool sensorReady = live || (!showOptical && i == 0);
                bool pending = support != null && support.RequestPending;
                bool ready = !pending && sensorReady && facts.Enabled && facts.Tone == AbilityTone.Ready;
                taskReady[i] = ready;
                Set(key.Title, "[" + (i + 1) + "] " + name + (ability == PlatformAbility.EmpBurst ? " · FF" : ""));
                Set(key.Facts, facts.CostText + " · " + PlatformWords.Whole(PlatformAbilities.Info(ability).EnergyKj) + " KJ\n" +
                               (pending ? "HOST REPLY PENDING" : ready ? "READY AT CROSSHAIR" : !live && facts.Enabled ? "NO LIVE FEED" : facts.Readiness));
                key.Control.SetEnabled(ready);
                key.Control.WithTooltip(name + " at the crosshair — " + PlatformAbilities.Info(ability).Summary + " " + facts.Readiness + ".");
                PaintKey(key);
            }
            Softkey deliver = softkeys[Tasks.Length];
            SupportActionDefinition armed = support != null && support.ArmedAction.HasValue ? Find(support.ArmedAction.Value) : null;
            deliverReady = live && armed != null && !support.RequestPending;
            Set(deliver.Title, armed != null ? "[F] DELIVER " + armed.Name : "[F] DELIVER ARMED");
            Set(deliver.Facts, armed == null ? "ARM ANY OPS ACTION, THEN DELIVER IT HERE" : support.RequestPending ? "REQUEST PENDING" : "AT THE CROSSHAIR");
            deliver.Control.SetEnabled(deliverReady);
            PaintKey(deliver);
            Softkey back = softkeys[Tasks.Length + 1];
            Set(back.Title, "[TAB] TASK MAP");
            Set(back.Facts, "");
            back.Control.SetEnabled(true);
            PaintKey(back);
        }

        // ---- Controls ----------------------------------------------------------------------------

        private bool OverFeed(Vector3 screen)
        {
            if (room == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(room, screen, null, out Vector2 local)) return false;
            // The room host is stretched; its local origin is the centre.
            float x = local.x + area.width * 0.5f, y = area.height * 0.5f - local.y;
            if (x < sensorRect.x || y < -sensorRect.y ||
                x > sensorRect.xMax || y > -sensorRect.y + sensorRect.height) return false;
            if (x >= cameraControls.x && x <= cameraControls.xMax && y >= -cameraControls.y && y <= -cameraControls.y + cameraControls.height) return false;
            return !(x >= pipRect.x && x <= pipRect.x + pipRect.width && y >= -pipRect.y && y <= -pipRect.y + pipRect.height);
        }

        private float FeedScreenWidth()
        {
            Canvas canvas = room != null ? room.GetComponentInParent<Canvas>() : null;
            return Mathf.Max(1f, sensorRect.width * (canvas != null ? canvas.rootCanvas.scaleFactor : 1f));
        }

        private float CurrentFootprint(in OrbitState state)
        {
            OrbitalPlatform platform = support.LocalPlatform;
            if (platform == null || !platform.Exists || !state.InPass) return Footprints[footprintIndex];
            LookAngles look = TheaterTrack.Look(state, aimX, aimZ);
            return look.Visible ? Footprint(platform.Orbit, look) : Footprints[footprintIndex];
        }

        /// <summary>Move the aim so the picture follows a screen drag of (dx, dy) pixels.</summary>
        private void Slew(float dx, float dy, in OrbitState state, bool station)
        {
            if (!station) return;
            double metresPerPixel = CurrentFootprint(state) / FeedScreenWidth();
            // Screen up is along-track, screen right is to the right of track.
            double upX = state.Pass.DirX, upZ = state.Pass.DirZ;
            double rightX = upZ, rightZ = -upX;
            double nextX = commandX - (rightX * dx + upX * dy) * metresPerPixel;
            double nextZ = commandZ - (rightZ * dx + upZ * dy) * metresPerPixel;
            Accept(nextX, nextZ, state);
        }

        private void CentreOn(Vector3 screen, in OrbitState state, bool station)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(room, screen, null, out Vector2 local)) return;
            Canvas canvas = room.GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            float x = local.x + area.width * 0.5f;
            float y = area.height * 0.5f - local.y;
            float dx = x - (sensorRect.x + sensorRect.width * 0.5f);
            float dy = -sensorRect.y + sensorRect.height * 0.5f - y;
            // A click right of the sensor centre moves that point to the centre.
            Slew(-dx * scale, -dy * scale, state, station);
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

        private void Zoom(int direction) => footprintIndex = Mathf.Clamp(footprintIndex + direction, 0, Footprints.Length - 1);

        private GlobalPosition AimPoint() => new GlobalPosition((float)aimX, aimHeight, (float)aimZ);

        private void Task(int index)
        {
            // Keys and softkeys share one gate: a key must not fire what the softkey would refuse.
            if ((!live && (showOptical || index != 0)) || index < 0 || index >= Tasks.Length || !taskReady[index] || support == null || support.RequestPending) return;
            SupportActionDefinition definition = Find(Tasks[index]);
            if (definition == null) return;
            support.RequestAt(definition.Id, AimPoint());
        }

        private void DeliverArmed()
        {
            if (!live || !deliverReady || support == null || support.RequestPending || !support.ArmedAction.HasValue) return;
            support.CallArmedAt(AimPoint());
        }

        private SupportActionDefinition Find(SupportActionId id)
        {
            if (support == null) return null;
            var actions = support.Actions;
            for (int i = 0; i < actions.Count; i++)
                if (actions[i].Id == id) return actions[i];
            return null;
        }

        // ---- Formatting --------------------------------------------------------------------------

        /// <summary>Offline fixtures: show this scene instead of forming one.</summary>
        internal void Fixture(Texture scene) => fixtureFeed = scene;

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

        private static void Set(TMP_Text label, string text)
        {
            if (text == null) text = "";
            if (label.text != text) label.text = text;
        }
    }
}
