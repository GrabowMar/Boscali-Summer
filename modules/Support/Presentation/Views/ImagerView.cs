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
    /// SPACE › IMAGER — the sensor operator. No panels: the station's live imager feed (a camera on
    /// the true line of sight, processed to grayscale SAR by <see cref="SatelliteImager"/>) fills
    /// the room edge to edge and the symbology is drawn straight on it in targeting-pod style (reticle, corner brackets,
    /// north arrow, scale bar, slant range, off-nadir, azimuth and elevation, a timestamp and the pass
    /// clock in the corners, a thin OSD line instead of a header). The radar product is a
    /// picture-in-picture; the five tasks and DELIVER ARMED are pod softkeys along the bottom, fed by
    /// <see cref="AbilityStatus"/>. Drag or WASD slews with gimbal lag, the wheel or Q/E zooms,
    /// double-click centres, C looks down the ground track, G tags contacts on demand.
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
        private const float SoftkeyHeight = 58f;
        private const float TagDwellSeconds = 12f;
        private const float TagFadeSeconds = 3f;
        private const int MaxContacts = 16;
        private const int FeedPixelsWide = 960;
        private const float FramesPerSecond = 8f;
        private static readonly double FieldOfRegard = 62.0 * OrbitMath.Deg;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly float[] Footprints = { 32000f, 16000f, 8000f, 4000f, 2000f, 1000f, 500f };
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

        private sealed class Contact
        {
            public RectTransform Root;
            public Image[] Box;
            public Image Leader;
            public TMP_Text Label;
            public Unit Unit;
            public Color Ink;
            public bool Active;
        }

        private readonly Softkey[] softkeys = new Softkey[Tasks.Length + 2];
        private readonly bool[] taskReady = new bool[Tasks.Length];
        private readonly Contact[] contacts = new Contact[MaxContacts];
        private readonly Rect[] sections = new Rect[4];

        private Rect area;
        private RectTransform room;
        private RawImage feed, scanlines, pip;
        private Texture2D scanlineTexture;
        private Image veil, reticle, sweep, liveDot;
        private TMP_Text aimStatus;
        private Rect cameraControls;
        private TMP_Text osd, cornerTL, cornerTR, cornerBL, cornerBR, slate, scaleLabel, pipCaption;
        private Image passFill;
        private RectTransform north, contactLayer, scaleGroup, signalPlate;
        private Rect feedCrop = new Rect(0f, 0f, 1f, 1f);
        private Rect softkeyStrip, pipRect;

        private double aimX, aimZ, commandX, commandZ;
        private float aimHeight, nextHeight, lastTime = -1f;
        private int footprintIndex = 4;
        private float tagUntil = -1f;
        private RoomControl tagButton;
        private TMP_Text tagLabel;
        private bool gsdLimited, live, dragging, shown, deliverReady;
        private Vector3 lastMouse, lastClickPosition;
        private float lastClickTime = -10f;
        private float entrance = 1f;
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
            Image surface = AvKit.Panel(host, new Rect(0f, 0f, w, h), ImagerStyle.Pod.WithAlpha(1f));
            surface.raycastTarget = true;

            // The live imager renders at the room's own aspect, so the feed fills it uncropped.
            feedPixelsHigh = Mathf.Clamp(Mathf.RoundToInt(FeedPixelsWide * h / Mathf.Max(1f, w)), 64, 2048);
            DestroyImager();
            feedCrop = new Rect(0f, 0f, 1f, 1f);
            feed = Raw(host, "Feed", new Rect(0f, 0f, w, h));
            feed.uvRect = feedCrop;
            feed.enabled = false;

            if (scanlineTexture != null) UnityEngine.Object.DestroyImmediate(scanlineTexture);
            scanlineTexture = new Texture2D(1, 4, TextureFormat.RGBA32, false)
            {
                name = "BoscaliImagerScanlines",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            scanlineTexture.SetPixels32(new[]
            {
                new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 255)
            });
            scanlineTexture.Apply(false);
            scanlines = Raw(host, "Scanlines", new Rect(0f, 0f, w, h));
            scanlines.texture = scanlineTexture;
            scanlines.uvRect = new Rect(0f, 0f, 1f, h / 4f);
            scanlines.color = new Color(1f, 1f, 1f, 0.045f);
            veil = AvKit.Panel(host, new Rect(0f, 0f, w, h), ImagerStyle.Pod.WithAlpha(0.72f));

            var layerObject = new GameObject("Contacts", typeof(RectTransform));
            contactLayer = (RectTransform)layerObject.transform;
            contactLayer.SetParent(host, false);
            AvKit.Place(contactLayer, new Rect(0f, 0f, w, h));
            for (int i = 0; i < MaxContacts; i++) contacts[i] = BuildContact();

            BuildSymbology(host, w, h);
            BuildSoftkeys(host, w, h);
            BuildPip(host, w, h);
            BuildCameraControls(host, w);
            sweep = AvKit.Panel(host, new Rect(0f, 0f, w, 70f), ImagerStyle.Ink.WithAlpha(0.35f), OpsSprites.Scan);
            sweep.type = Image.Type.Simple;
            sweep.enabled = false;

            sections[0] = new Rect(0f, 0f, w, h);
            sections[1] = new Rect(0f, 0f, w, 34f);
            sections[2] = new Rect(softkeyStrip.x, -softkeyStrip.y, softkeyStrip.width, softkeyStrip.height);
            sections[3] = new Rect(pipRect.x, -pipRect.y, pipRect.width, pipRect.height);
        }

        private static RawImage Raw(RectTransform parent, string name, Rect at)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            RawImage raw = go.GetComponent<RawImage>();
            raw.rectTransform.SetParent(parent, false);
            AvKit.Place(raw.rectTransform, at);
            raw.raycastTarget = false;
            return raw;
        }

        private void BuildSymbology(RectTransform host, float w, float h)
        {
            Color ink = ImagerStyle.Ink;
            // A thin OSD line instead of a header.
            AvKit.Panel(host, new Rect(0f, 0f, w, 30f), ImagerStyle.Halo.WithAlpha(0.90f));
            osd = ImagerStyle.Symbol(host, "", new Rect(18f, -6f, w - 36f - 220f, 18f), ImagerStyle.Osd);
            liveDot = AvKit.Panel(host, new Rect(w - 236f, -10f, 10f, 10f), AvTheme.RailDanger, OpsSprites.Dot);
            liveDot.type = Image.Type.Simple;

            AvKit.Panel(host, new Rect(28f, -42f, 650f, 36f), ImagerStyle.Halo.WithAlpha(0.72f));
            AvKit.Panel(host, new Rect(w - 678f, -42f, 650f, 44f), ImagerStyle.Halo.WithAlpha(0.72f));
            cornerTL = ImagerStyle.Symbol(host, "", new Rect(40f, -50f, 620f, 20f), ImagerStyle.Corner);
            cornerTR = ImagerStyle.Symbol(host, "", new Rect(w - 40f - 620f, -50f, 620f, 20f), ImagerStyle.Corner,
                TextAlignmentOptions.MidlineRight);
            AvKit.Panel(host, new Rect(w - 40f - 300f, -76f, 300f, 4f), ink.WithAlpha(0.25f));
            passFill = AvKit.Panel(host, new Rect(w - 40f - 300f, -76f, 0f, 4f), ink);
            float bottom = h - SoftkeyHeight - 30f;
            cornerBL = ImagerStyle.Symbol(host, "", new Rect(52f, -bottom + 30f, 760f, 20f), ImagerStyle.Corner);
            cornerBR = ImagerStyle.Symbol(host, "", new Rect(w - 52f - 760f, -bottom + 30f, 760f, 20f), ImagerStyle.Corner,
                TextAlignmentOptions.MidlineRight);

            // Corner brackets around the field and the reticle at its centre.
            AvKit.Panel(host, new Rect(28f, -40f, w - 56f, bottom - 32f), ink.WithAlpha(0.9f), OpsSprites.Brackets);
            reticle = AvKit.Panel(host, new Rect(w * 0.5f - 90f, -h * 0.5f + 90f, 180f, 180f), ink, OpsSprites.Reticle);
            reticle.type = Image.Type.Simple;

            var northObject = new GameObject("North", typeof(RectTransform));
            north = (RectTransform)northObject.transform;
            north.SetParent(host, false);
            north.anchorMin = north.anchorMax = new Vector2(0f, 1f);
            north.pivot = new Vector2(0.5f, 0.5f);
            north.sizeDelta = new Vector2(44f, 44f);
            north.anchoredPosition = new Vector2(w - 70f, -130f);
            AvKit.Rule(north, new Rect(21f, -6f, 2f, 26f), ink);
            Image head = AvKit.Panel(north, new Rect(14f, 6f, 16f, 14f), ink, OpsSprites.Triangle);
            Lines.Centre(head.rectTransform, 22f, -1f, 16f, 14f);
            head.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);
            ImagerStyle.Symbol(north, "N", new Rect(0f, 22f, 44f, 16f), ImagerStyle.Small, TextAlignmentOptions.Center);

            var scaleObject = new GameObject("Scale", typeof(RectTransform));
            scaleGroup = (RectTransform)scaleObject.transform;
            scaleGroup.SetParent(host, false);
            AvKit.Place(scaleGroup, new Rect(0f, 0f, w, h));
            float scaleY = -bottom + 56f;
            AvKit.Rule(scaleGroup, new Rect(40f, scaleY, ScaleBarPixels, 2f), ink);
            AvKit.Rule(scaleGroup, new Rect(40f, scaleY + 6f, 2f, 8f), ink);
            AvKit.Rule(scaleGroup, new Rect(38f + ScaleBarPixels, scaleY + 6f, 2f, 8f), ink);
            scaleLabel = ImagerStyle.Symbol(scaleGroup, "", new Rect(50f + ScaleBarPixels, scaleY + 8f, 200f, 16f), ImagerStyle.Small);

            float plateW = Mathf.Min(860f, w - 80f);
            const float plateH = 148f;
            Image plate = AvKit.Panel(host, new Rect((w - plateW) * 0.5f, -h * 0.5f + plateH * 0.5f,
                plateW, plateH), ImagerStyle.Halo.WithAlpha(0.94f));
            signalPlate = plate.rectTransform;
            plate.raycastTarget = false;
            AvKit.Outline(signalPlate, new Rect(0f, 0f, plateW, plateH), ink.WithAlpha(0.75f));
            AvKit.Rule(signalPlate, new Rect(20f, -38f, plateW - 40f, 1f), ink.WithAlpha(0.5f));
            ImagerStyle.Symbol(signalPlate, "SENSOR VIDEO / SIGNAL STATE", new Rect(24f, -10f, plateW - 48f, 20f),
                ImagerStyle.Small, TextAlignmentOptions.Center);
            slate = ImagerStyle.Symbol(signalPlate, "", new Rect(24f, -49f, plateW - 48f, 76f), 23f, TextAlignmentOptions.Center);
            slate.enableWordWrapping = true;
        }

        private Contact BuildContact()
        {
            var go = new GameObject("Contact", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(contactLayer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(32f, 32f);
            var contact = new Contact
            {
                Root = rect,
                Box = AvKit.Outline(rect, new Rect(0f, 0f, 32f, 32f), ImagerStyle.Ink),
                Leader = Lines.Make(rect, ImagerStyle.Ink),
                Label = ImagerStyle.Symbol(rect, "", new Rect(-54f, -36f, 140f, 14f), 10f, TextAlignmentOptions.Center)
            };
            go.SetActive(false);
            return contact;
        }

        private void BuildSoftkeys(RectTransform host, float w, float h)
        {
            float gap = 10f, count = softkeys.Length;
            float keyW = (w - 48f - gap * (count - 1f)) / count;
            softkeyStrip = new Rect(24f, -(h - SoftkeyHeight - 14f), w - 48f, SoftkeyHeight);
            for (int i = 0; i < softkeys.Length; i++)
            {
                int index = i;
                var at = new Rect(24f + i * (keyW + gap), softkeyStrip.y, keyW, SoftkeyHeight);
                Action click = i < Tasks.Length ? (Action)(() => Task(index)) : i == Tasks.Length ? DeliverArmed : (Action)(() => openStation?.Invoke());
                var key = new Softkey();
                key.Control = RoomControl.Create(host, at, click, "Softkey");
                RectTransform rect = key.Control.Rect;
                key.Fill = AvKit.Panel(rect, new Rect(0f, 0f, at.width, at.height), ImagerStyle.Halo.WithAlpha(0.7f));
                key.Edge = AvKit.Outline(rect, new Rect(0f, 0f, at.width, at.height), ImagerStyle.Ink);
                key.Title = ImagerStyle.Symbol(rect, "", new Rect(10f, -6f, at.width - 20f, 20f), 13f);
                key.Facts = ImagerStyle.Symbol(rect, "", new Rect(10f, -28f, at.width - 20f, 26f), 10f, TextAlignmentOptions.TopLeft, true);
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
            const float pw = 320f, ph = 200f;
            pipRect = new Rect(w - 24f - pw, -(h - SoftkeyHeight - 30f - ph - 40f), pw, ph);
            AvKit.Panel(host, pipRect, ImagerStyle.Halo.WithAlpha(0.85f));
            AvKit.Outline(host, pipRect, ImagerStyle.Ink);
            pip = Raw(host, "Product", new Rect(pipRect.x + 6f, pipRect.y - 6f, pw - 12f, ph - 34f));
            // Formed images keep range on the horizontal axis; flip so the product is a rotation, not a mirror.
            pip.uvRect = new Rect(1f, 0f, -1f, 1f);
            pip.enabled = false;
            pipCaption = ImagerStyle.Symbol(host, "", new Rect(pipRect.x + 8f, pipRect.y - ph + 26f, pw - 16f, 20f), 10f);
        }

        private void BuildCameraControls(RectTransform host, float w)
        {
            cameraControls = new Rect(40f, -94f, 600f, 76f);
            AvKit.Panel(host, cameraControls, ImagerStyle.Halo.WithAlpha(0.88f));
            CameraButton(host, new Rect(48f, -102f, 130f, 30f), "− ZOOM [Q]", () => Zoom(-1), out _);
            CameraButton(host, new Rect(184f, -102f, 130f, 30f), "+ ZOOM [E]", () => Zoom(1), out _);
            CameraButton(host, new Rect(320f, -102f, 160f, 30f), "RECENTER [C]", Recenter, out _);
            tagButton = CameraButton(host, new Rect(486f, -102f, 130f, 30f), "TAG [G]", TagSweep, out tagLabel);
            tagButton.WithTooltip("Tag what the sensor sees right now: up to 16 contacts, tracked for 12 s. Local display only — task a scan to exploit them.");
            aimStatus = ImagerStyle.Symbol(host, "DRAG / WASD · SLEW   WHEEL · ZOOM", new Rect(50f, -141f, 580f, 18f), 11f);
        }

        private static RoomControl CameraButton(RectTransform host, Rect at, string title, Action action, out TMP_Text label)
        {
            RoomControl control = RoomControl.Create(host, at, action, "CameraControl");
            Image fill = AvKit.Panel(control.Rect, new Rect(0f, 0f, at.width, at.height), ImagerStyle.Pod.WithAlpha(1f));
            AvKit.Outline(control.Rect, new Rect(0f, 0f, at.width, at.height), ImagerStyle.Dim);
            label = ImagerStyle.Symbol(control.Rect, title, new Rect(6f, 0f, at.width - 12f, at.height), 11f, TextAlignmentOptions.Center);
            control.Changed = c => fill.color = !c.Enabled ? ImagerStyle.Pod.WithAlpha(0.6f)
                : c.Hovered ? ImagerStyle.Ink.WithAlpha(0.25f) : ImagerStyle.Pod.WithAlpha(1f);
            return control;
        }

        private void Recenter()
        {
            OrbitalPlatform platform = support != null ? support.LocalPlatform : null;
            if (platform == null || !platform.Exists) return;
            OrbitState state = platform.State(support.OrbitNow, support.OrbitClock);
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
            tagUntil = -1f;
            dragging = false;
            lastTime = -1f;
            Array.Clear(taskReady, 0, taskReady.Length);
            deliverReady = false;
            shown = true;
        }

        public void Hide()
        {
            if (!shown) return;
            shown = false;
            dragging = false;
            live = false;
            HideContacts();
            DestroyImager();
            support?.SetUplinkAim(AimPoint());
            AvButton.ClearTooltip();
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
                r.anchoredPosition = new Vector2(r.anchoredPosition.x, -(area.height - 70f) * entrance);
            }
            feed.color = new Color(1f, 1f, 1f, Mathf.Clamp01(0.3f + entrance));
        }

        public void Refresh(double now, float time, bool textTick)
        {
            if (support == null) return;
            float dt = lastTime < 0f ? 0f : Mathf.Min(time - lastTime, 0.1f);
            lastTime = time;
            OrbitalPlatform platform = support.LocalPlatform;
            OrbitClock clock = support.OrbitClock;
            bool station = platform != null && platform.Exists;
            OrbitState state = station ? platform.State(support.OrbitNow, clock) : default;
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
            Paint(platform, state, support.OrbitNow, clock, slewing, textTick);
        }

        /// <summary>Paint the feed and the symbology. The harness calls it with a fixture station.</summary>
        internal void Paint(OrbitalPlatform platform, in OrbitState state, double now, in OrbitClock clock, bool slewing, bool textTick)
        {
            bool station = platform != null && platform.Exists;
            LookAngles look = station ? TheaterTrack.Look(state, aimX, aimZ) : LookAngles.Hidden;
            RenderFeed(platform, state, look, now);
            liveDot.enabled = live;
            if (fixtureFeed != null)
            {
                pip.texture = fixtureFeed;
                pip.enabled = station;
            }
            else if (products != null)
            {
                pip.texture = products.Scan.Image;
                pip.enabled = station && products.HasProduct;
            }
            if (textTick) WriteText(platform, state, look, now, clock, slewing);
        }

        public bool HandleKeys()
        {
            if (support == null) return false;
            OrbitalPlatform platform = support.LocalPlatform;
            bool station = platform != null && platform.Exists;
            OrbitState state = station ? platform.State(support.OrbitNow, support.OrbitClock) : default;
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
            if (Input.GetKeyDown(KeyCode.G)) TagSweep();
            if (Input.GetKeyDown(KeyCode.C)) Recenter();
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
            if (platform == null || !platform.Exists) message = "NO STATION ON ORBIT\nLAUNCH THE CORE FROM THE STATION WALL [TAB]";
            else if (!platform.Fitted(ModuleKind.Imager)) message = "NO SPY IMAGER FITTED\nDOCK AN IMG MODULE FROM THE STATION WALL";
            else if (!platform.FittedOnline(ModuleKind.Imager, now)) message = "IMAGER OFFLINE\nWAIT FOR MODULE RECOVERY · [TAB] STATION";
            else if (platform.Brownout) message = "POWER RESERVE DEPLETED\nFIT SOLAR / REACTOR / BATTERY · [TAB] STATION";
            else if (platform.HoldAt(now) != PlatformHold.None)
                message = PlatformWords.Hold(platform.HoldAt(now)) + " · T-" + PlatformWords.Clock(platform.CycleStart - now);
            else if (!state.InPass) message = "STATION REPOSITIONING\nFEED RESUMES ON ARRIVAL";
            else if (!look.Visible || look.OffNadir > FieldOfRegard) message = "AIM BEYOND FIELD OF REGARD\nPRESS C FOR NADIR";

            if (message != null)
            {
                live = false;
                HideContacts();
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
                HideContacts();
                return;
            }
            // The live feed is a camera on the station's line of sight; the formed SAR product
            // from a RADAR SCAN stays in the picture-in-picture.
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
            Set(slate, live ? "" : "ACQUIRING SAR FEED…");
            SetSignalState(live);
            UpdateTagged();
        }

        private void SetSignalState(bool feedReady)
        {
            if (signalPlate.gameObject.activeSelf == feedReady) signalPlate.gameObject.SetActive(!feedReady);
            if (north.gameObject.activeSelf != feedReady) north.gameObject.SetActive(feedReady);
            if (scaleGroup.gameObject.activeSelf != feedReady) scaleGroup.gameObject.SetActive(feedReady);
        }

        private float Footprint(in OrbitRegime orbit, in LookAngles look)
        {
            float footprint = Footprints[footprintIndex];
            float minimum = (float)(TheaterTrack.GroundSample(orbit, look, false) * FeedPixelsWide * 0.75);
            gsdLimited = footprint < minimum;
            return footprint;
        }

        private void WriteText(OrbitalPlatform platform, in OrbitState state, in LookAngles look, double now, in OrbitClock clock, bool slewing)
        {
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
            Set(osd, OrbitalPlatform.Callsign + " SAR-X · " + (station ? orbit.Code + " " + TheaterGrid.Km(orbit.Altitude) + " KM" : "NO STATION") +
                     " · ZOOM " + (footprintIndex + 1) + "/" + Footprints.Length + (gsdLimited ? " DIGITAL" : "") + " · FOV " +
                     TheaterGrid.Km(footprint) + " KM · " + frameWord +
                     (station ? " · PWR " + PlatformWords.Whole(platform.Energy) + " KJ" + (platform.Brownout ? " BROWNOUT" : "") : "") +
                     (live ? " · LIVE" : " · NO FEED") + "   |   [TAB] STATION WALL");
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            string gmt = level != null ? TheaterGrid.Clock(((level.timeOfDay % 24f) + 24f) % 24f * 3600.0) : "--:--";
            Set(cornerTL, "SAR / MONO   FRAME " + (imager != null ? imager.FramesRendered.ToString("D6", Invariant) : "------") +
                "   GMT " + gmt + "   GET " + (station ? TheaterGrid.Elapsed(platform.Elapsed(now)) : "--:--:--"));
            if (station && state.InPass)
            {
                Set(cornerTR, "FIXED STATION · " + StationKeeping.Name(platform.PositionIndex));
                passFill.rectTransform.sizeDelta = new Vector2(300f, 4f);
            }
            else
            {
                Set(cornerTR, station ? PlatformWords.Phase(platform, now, clock) : "NO STATION");
                passFill.rectTransform.sizeDelta = new Vector2(0f, 4f);
            }
            Set(cornerBL, look.Visible
                ? "EL " + Deg(look.Elevation) + "   AZ " + Bearing(look.AzimuthDeg) + "   OFF-NDR " + Deg(look.OffNadir) +
                  (slewing ? " SLEW" : "") + "   SLANT " + TheaterGrid.Km(look.SlantRange) + " KM"
                : station ? PlatformWords.Phase(platform, now, clock) : "NO TRACK");
            Set(cornerBR, "GSD " + (look.Visible ? gsd.ToString("0.00", Invariant) + " M" : "—") + "   AIM " + TheaterGrid.Kilometres(aimX, aimZ));
            Set(scaleLabel, live ? Distance(footprint * ScaleBarPixels / Mathf.Max(1f, area.width)) : "");
            float tagLeft = tagUntil - Time.unscaledTime;
            Set(aimStatus, (tagLeft > 0f ? "TAG " + Mathf.CeilToInt(tagLeft) + "s" : "") +
                (slewing ? "SLEWING · RELEASE TO SETTLE" : "DRAG / WASD · SLEW   WHEEL · ZOOM   DOUBLE-CLICK · AIM   G · TAG"));
            if (tagButton != null)
            {
                tagButton.SetEnabled(live);
                Set(tagLabel, tagLeft > 0f ? "TAG " + Mathf.CeilToInt(tagLeft) + "s" : "TAG [G]");
                tagLabel.color = live ? ImagerStyle.Ink : ImagerStyle.Dim;
            }
            WritePip(scan, station);
            WriteSoftkeys();
        }

        private void WritePip(SarCollector scan, bool station)
        {
            if (!station) { Set(pipCaption, "POD OFFLINE · STATION REQUIRED"); return; }
            if (scan == null) { Set(pipCaption, "NO PRODUCT"); return; }
            switch (scan.Phase)
            {
                case SarPhase.Collecting: Set(pipCaption, "PRODUCT · COLLECTING " + Mathf.RoundToInt(scan.Progress * 100f) + "%"); break;
                case SarPhase.Processing: Set(pipCaption, "PRODUCT · PROCESSING " + Mathf.RoundToInt(scan.ProcessingProgress * 100f) + "%"); break;
                case SarPhase.Complete: Set(pipCaption, "PRODUCT READY · " + scan.Contacts + " CONTACT(S)"); break;
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
                bool ready = live && facts.Enabled && facts.Tone == AbilityTone.Ready;
                taskReady[i] = ready;
                Set(key.Title, "[" + (i + 1) + "] " + name + (ability == PlatformAbility.EmpBurst ? " · FF" : ""));
                Set(key.Facts, facts.CostText + " · " + PlatformWords.Whole(PlatformAbilities.Info(ability).EnergyKj) + " KJ · " +
                               (ready ? "READY AT CROSSHAIR" : !live && facts.Enabled ? "NO LIVE FEED" : facts.Readiness));
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
            Set(back.Title, "[TAB] STATION WALL");
            Set(back.Facts, "BUILD AND LAUNCH · THE FEED KEEPS ITS AIM");
            back.Control.SetEnabled(true);
            PaintKey(back);
        }

        // ---- Controls ----------------------------------------------------------------------------

        private bool OverFeed(Vector3 screen)
        {
            if (room == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(room, screen, null, out Vector2 local)) return false;
            // The room host is stretched; its local origin is the centre.
            float x = local.x + area.width * 0.5f, y = area.height * 0.5f - local.y;
            if (x < 0f || y < 34f || x > area.width || y > area.height) return false;
            if (y > -softkeyStrip.y - 6f) return false;
            if (x >= cameraControls.x && x <= cameraControls.xMax && y >= -cameraControls.y && y <= -cameraControls.y + cameraControls.height) return false;
            return !(x >= pipRect.x && x <= pipRect.x + pipRect.width && y >= -pipRect.y && y <= -pipRect.y + pipRect.height);
        }

        private float FeedScreenWidth()
        {
            Canvas canvas = room != null ? room.GetComponentInParent<Canvas>() : null;
            return Mathf.Max(1f, area.width * (canvas != null ? canvas.rootCanvas.scaleFactor : 1f));
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
            // A click right of centre should become the centre: slew the picture the other way.
            Slew(-local.x * scale, -local.y * scale, state, station);
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
            if (!live || index < 0 || index >= Tasks.Length || !taskReady[index] || support == null || support.RequestPending) return;
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

        // ---- Contacts ----------------------------------------------------------------------------

        private void HideContacts()
        {
            tagUntil = -1f;
            for (int i = 0; i < MaxContacts; i++)
            {
                Contact contact = contacts[i];
                if (contact == null) continue;
                contact.Unit = null;
                if (!contact.Active) continue;
                contact.Active = false;
                contact.Root.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// On-demand contact tag: one sweep of the live frame, tracking what is in view until
        /// the dwell ends. Local display only — a tag reveals nothing to the faction.
        /// </summary>
        private void TagSweep()
        {
            if (!live || imager == null || imager.Camera == null || support == null) return;
            Vector3 aimLocal = new GlobalPosition((float)aimX, aimHeight, (float)aimZ).ToLocalPosition();
            OrbitalPlatform platform = support.LocalPlatform;
            float footprint = platform != null && platform.Exists
                ? CurrentFootprint(platform.State(support.OrbitNow, support.OrbitClock))
                : Footprints[footprintIndex];
            Camera cam = imager.Camera;
            var units = UnitRegistry.allUnits;
            if (units == null || units.Count == 0)
            {
                HideContacts();
                return;
            }
            FactionHQ localHq = GameManager.GetLocalPlayer<Player>(out Player localPlayer) && localPlayer != null ? localPlayer.HQ : null;
            float maxDistSq = footprint * 0.75f * (footprint * 0.75f);
            int index = 0;
            for (int i = 0; i < units.Count && index < MaxContacts; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled) continue;
                Vector3 position = unit.transform.position;
                float dx = position.x - aimLocal.x, dz = position.z - aimLocal.z;
                if (dx * dx + dz * dz > maxDistSq) continue;
                Vector3 vp = cam.WorldToViewportPoint(position);
                if (vp.z <= 0f || vp.x < 0.03f || vp.x > 0.97f || vp.y < 0.05f || vp.y > 0.95f) continue;
                Contact contact = contacts[index++];
                contact.Unit = unit;
                bool hostile = localHq != null && unit.NetworkHQ != null && unit.NetworkHQ != localHq;
                bool friendly = localHq != null && unit.NetworkHQ == localHq;
                // Pod symbology is monochrome; the status is the word.
                string name = !string.IsNullOrEmpty(unit.unitName) ? unit.unitName : unit is Aircraft ? "AIR" : "VEH";
                Set(contact.Label, (hostile ? "TGT " : friendly ? "FRD " : "UNK ") + name.ToUpperInvariant());
                contact.Ink = hostile ? AvTheme.RailDanger : ImagerStyle.Ink;
            }
            for (int i = index; i < MaxContacts; i++)
            {
                Contact contact = contacts[i];
                contact.Unit = null;
                if (!contact.Active) continue;
                contact.Active = false;
                contact.Root.gameObject.SetActive(false);
            }
            tagUntil = Time.unscaledTime + TagDwellSeconds;
            UpdateTagged();
        }

        /// <summary>Re-project the tagged set each frame until the dwell ends, fading the last seconds.</summary>
        private void UpdateTagged()
        {
            if (tagUntil < 0f) return;
            if (tagUntil - Time.unscaledTime <= 0f || !live || imager == null || imager.Camera == null)
            {
                HideContacts();
                return;
            }
            Camera cam = imager.Camera;
            float w = area.width, h = area.height;
            float alpha = Mathf.Clamp01((tagUntil - Time.unscaledTime) / TagFadeSeconds);
            for (int i = 0; i < MaxContacts; i++)
            {
                Contact contact = contacts[i];
                Unit unit = contact != null ? contact.Unit : null;
                if (unit == null)
                {
                    if (contact != null && contact.Active)
                    {
                        contact.Active = false;
                        contact.Root.gameObject.SetActive(false);
                    }
                    continue;
                }
                Vector3 position = unit.transform.position;
                Vector3 vp = cam.WorldToViewportPoint(position);
                if (vp.z <= 0f || vp.x < 0.03f || vp.x > 0.97f || vp.y < 0.05f || vp.y > 0.95f)
                {
                    if (contact.Active)
                    {
                        contact.Active = false;
                        contact.Root.gameObject.SetActive(false);
                    }
                    continue;
                }
                contact.Root.anchoredPosition = new Vector2((vp.x - 0.5f) * w, (vp.y - 0.5f) * h);
                Color faded = contact.Ink;
                faded.a *= alpha;
                for (int b = 0; b < contact.Box.Length; b++) contact.Box[b].color = faded;
                contact.Label.color = faded;
                Vector3 velocity = unit.rb != null ? unit.rb.velocity : unit.transform.forward * unit.speed;
                float speed = velocity.magnitude;
                Vector3 ahead = speed > 2f ? cam.WorldToViewportPoint(position + velocity.normalized * 50f) : Vector3.zero;
                if (speed > 2f && ahead.z > 0f)
                {
                    var direction = new Vector2((ahead.x - vp.x) * w, (ahead.y - vp.y) * h);
                    float length = Mathf.Clamp(speed * 0.35f, 10f, 36f);
                    Vector2 end = direction.sqrMagnitude > 0.0001f ? direction.normalized * length : Vector2.zero;
                    Lines.Set(contact.Leader, 16f, -16f, 16f + end.x, -16f + end.y, 1.5f);
                    contact.Leader.color = faded;
                    contact.Leader.enabled = true;
                }
                else contact.Leader.enabled = false;
                if (!contact.Active)
                {
                    contact.Active = true;
                    contact.Root.gameObject.SetActive(true);
                }
            }
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
