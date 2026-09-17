using System.Text;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>
    /// The always-on cockpit weather banner, three things and nothing else: the SIGMET tier,
    /// the wind and its gust at the aircraft, and the nearest cell worth naming with its range
    /// and bearing. The small rose on its right is the warning ring: where that cell is relative
    /// to the aircraft's nose, so "which way" is a picture and not only a sentence.
    ///
    /// <para>A screen-space overlay, so it claims no bezel and patches nothing, and it hides
    /// itself the moment the module, the setting or the camera view says it should. It ticks at
    /// ten hertz and allocates nothing after the build.</para>
    /// </summary>
    internal sealed class WeatherHud : MonoBehaviour, ISceneService
    {
        /// <summary>Ten hertz: fast enough for a distance readout, cheap enough to ignore.</summary>
        private const float TickSeconds = 0.1f;

        private const float PanelWidth = 348f;
        private const float PanelHeight = 74f;

        /// <summary>Below the vanilla top bar, clear of the cockpit HUD's own rows.</summary>
        private const float TopInset = 104f;

        private const float EdgeInset = 10f;
        private const float ChipWidth = 78f;
        private const float ChipHeight = 18f;
        private const float ChipGap = 8f;
        private const float LineHeight = 14f;
        private const float Line1Y = 8f;
        private const float Line2Y = 30f;
        private const float Line3Y = 48f;

        private const float RoseSize = 44f;
        private const float RoseBlip = 5f;
        private const float RoseMinRadius = 3f;
        private const float RoseMaxRadius = RoseSize * 0.5f - RoseBlip - 1f;

        /// <summary>Beyond this the blip stays pinned to the rim: the ring is a bearing, not a chart.</summary>
        private const float RoseFullRangeMetres = 40000f;

        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        private const int TierCount = 4;

        /// <summary>A slow breathe, never a strobe: the frame alpha dips by this much.</summary>
        private const float PulseDepth = 0.25f;
        private const float PulsePeriod = 2f;

        private WeatherSettings settings;
        private WeatherManager manager;
        private ManualLogSource log;

        /// <summary>One buffer for every compound line, reused by every tick.</summary>
        private readonly StringBuilder text = new StringBuilder(128);

        // Style colours resolved once at build; the tick only indexes them.
        private readonly Color[] chipInk = new Color[TierCount];
        private readonly Color[] chipFill = new Color[TierCount];
        private readonly Color[] rail = new Color[TierCount];

        private GameObject root;
        private Image chipBox;
        private TMP_Text chipLabel;
        private TMP_Text subjectLabel;
        private TMP_Text windLabel;
        private TMP_Text cellLabel;
        private Image[] frame;
        private Image[] roseFrame;
        private Image roseBlip;
        private Image roseNose;

        private float nextTick;

        public bool Visible => root != null && root.activeSelf;

        public void Configure(WeatherSettings weatherSettings, WeatherManager weatherManager, ManualLogSource logger)
        {
            settings = weatherSettings;
            manager = weatherManager;
            log = logger;
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            chipBox = null;
            chipLabel = null;
            subjectLabel = null;
            windLabel = null;
            cellLabel = null;
            frame = null;
            roseFrame = null;
            roseBlip = null;
            roseNose = null;
            nextTick = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickSeconds;
            Refresh();
        }

        private void Refresh()
        {
            WeatherSnapshot snapshot = manager != null ? manager.Snapshot : WeatherSnapshot.Unavailable;
            if (!CanShow(snapshot) || !TryPlayerPosition(out float playerX, out float playerZ))
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }

            if (root == null) Build();
            if (!root.activeSelf) root.SetActive(true);
            Apply(snapshot, playerX, playerZ);
        }

        private bool CanShow(WeatherSnapshot snapshot)
        {
            if (Application.isBatchMode || settings == null || manager == null) return false;
            if (!settings.Enabled.Value || !settings.Hud.Value) return false;
            return snapshot.Available && InMissionView();
        }

        /// <summary>
        /// Cockpit and the two external mission cameras. The cinematic TV camera, the free
        /// camera and the editor views are not a pilot's view, so the banner stays down.
        /// </summary>
        private static bool InMissionView()
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null || cameras.currentState == null) return false;
            return cameras.currentState == cameras.cockpitState ||
                   cameras.currentState == cameras.orbitState ||
                   cameras.currentState == cameras.chaseState;
        }

        /// <summary>Same reading the WEA panel passes to its radar page.</summary>
        private static bool TryPlayerPosition(out float x, out float z)
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null)
            {
                x = 0f;
                z = 0f;
                return false;
            }

            Vector3 position = cameras.transform.position;
            x = position.x;
            z = position.z;
            return !float.IsNaN(x) && !float.IsNaN(z);
        }

        /// <summary>
        /// Where the nose points, so the rose can read relative. The ownship is the honest
        /// source; the camera it is followed by is the fallback when no aircraft is being flown.
        /// </summary>
        private static bool TryOwnshipHeading(out float heading)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            Aircraft aircraft = hud != null ? hud.aircraft : null;
            if (aircraft != null && TryHeading(aircraft.transform.forward, out heading)) return true;

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras != null && TryHeading(cameras.transform.forward, out heading)) return true;

            heading = 0f;
            return false;
        }

        private static bool TryHeading(Vector3 forward, out float heading)
        {
            heading = 0f;
            if (float.IsNaN(forward.x) || float.IsNaN(forward.z)) return false;
            if (forward.x * forward.x + forward.z * forward.z < 1e-6f) return false;
            heading = WeatherState.WrapHeading((float)(System.Math.Atan2(forward.x, forward.z) * 180.0 / System.Math.PI));
            return true;
        }

        // ---- Build -----------------------------------------------------------------------

        private void Build()
        {
            root = new GameObject("Boscali Weather HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = ResolveSortingOrder();
            canvas.pixelPerfect = true;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            RectTransform panel = AvKit.Panel((RectTransform)root.transform,
                new Rect(0f, -TopInset, PanelWidth, PanelHeight), AvTheme.Unity(AvTokens.HudPanel)).rectTransform;
            panel.name = "Weather Banner";
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            frame = AvKit.Outline(panel, new Rect(0f, 0f, PanelWidth, PanelHeight), AvTheme.RailInert);

            Rect chipRect = new Rect(EdgeInset, -Line1Y, ChipWidth, ChipHeight);
            chipBox = AvStyled.Box(panel, chipRect, "chip inert");
            chipLabel = AvStyled.Label(panel, chipRect, "", "chip inert", align: TextAlignmentOptions.Center);

            float textWidth = PanelWidth - EdgeInset * 2f - RoseSize - EdgeInset;
            subjectLabel = AvStyled.Label(panel,
                new Rect(EdgeInset + ChipWidth + ChipGap, -Line1Y, textWidth - ChipWidth - ChipGap, ChipHeight),
                "", "row-name", align: TextAlignmentOptions.MidlineLeft);

            windLabel = AvStyled.Label(panel, new Rect(EdgeInset, -Line2Y, textWidth, LineHeight),
                "", "row-sub", align: TextAlignmentOptions.MidlineLeft);
            windLabel.enableWordWrapping = false;
            windLabel.overflowMode = TextOverflowModes.Overflow;
            windLabel.color = AvTheme.TextPrimary;

            cellLabel = AvStyled.Label(panel, new Rect(EdgeInset, -Line3Y, textWidth, LineHeight),
                "", "row-sub", align: TextAlignmentOptions.MidlineLeft);
            cellLabel.enableWordWrapping = false;
            cellLabel.overflowMode = TextOverflowModes.Overflow;

            BuildRose(panel);

            for (int tier = 0; tier < TierCount; tier++)
            {
                StormWarning warning = (StormWarning)tier;
                AvStyle chip = AvStyleHost.Style("chip " + StormReadout.ChipClass(warning));
                chipInk[tier] = AvStyleHost.Resolve(chip.Color, AvTheme.Dim);
                chipFill[tier] = AvStyleHost.Resolve(chip.Background, AvTheme.SurfaceInert);

                AvStyle railStyle = AvStyleHost.Style(StormReadout.RailClass(warning));
                rail[tier] = AvStyleHost.Resolve(railStyle.Background, AvTheme.RailInert);
            }

            // A banner must never eat the mouse: it sits under the pointer the whole flight.
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

            log?.LogInfo("Weather HUD installed.");
        }

        /// <summary>
        /// The warning ring. It is a bearing ring, not a picture of the map: the nose sits at
        /// the top, the blip is the cell's relative bearing, and its radius is range until the
        /// cell is beyond the ring, where it pins to the rim.
        /// </summary>
        private void BuildRose(RectTransform panel)
        {
            float x = PanelWidth - EdgeInset - RoseSize;
            var roseObject = new GameObject("WarningRose", typeof(RectTransform));
            var rose = roseObject.GetComponent<RectTransform>();
            rose.SetParent(panel, false);
            AvKit.Place(rose, new Rect(x, -Line1Y, RoseSize, RoseSize));

            roseFrame = AvKit.Outline(rose, new Rect(0f, 0f, RoseSize, RoseSize), AvTheme.RailInert);

            float centre = RoseSize * 0.5f;
            roseNose = AvKit.Rule(rose, new Rect(centre - 1f, 0f, 2f, 5f), AvTheme.Dim);

            roseBlip = AvKit.Panel(rose, new Rect(0f, 0f, RoseBlip, RoseBlip), AvTheme.RailInert);
            RectTransform blip = roseBlip.rectTransform;
            blip.anchorMin = blip.anchorMax = new Vector2(0.5f, 0.5f);
            blip.pivot = new Vector2(0.5f, 0.5f);
            blip.anchoredPosition = Vector2.zero;
            roseBlip.gameObject.SetActive(false);
        }

        /// <summary>
        /// The cockpit HUD canvas, so the banner sits just under it. The game names no
        /// sorting order in code, so the FlightHud hierarchy is the only honest way to
        /// tell that canvas apart from the map or menu canvases.
        /// </summary>
        private static int ResolveSortingOrder()
        {
            FlightHud hud = SceneSingleton<FlightHud>.i;
            if (hud != null)
            {
                Canvas canvas = hud.GetComponent<Canvas>();
                Transform cursor = hud.transform.parent;
                while (canvas == null && cursor != null)
                {
                    canvas = cursor.GetComponent<Canvas>();
                    cursor = cursor.parent;
                }
                if (canvas != null) return canvas.sortingOrder - 1;
            }

            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
                if (candidate.GetComponentInChildren<FlightHud>(true) != null) return candidate.sortingOrder - 1;
            }

            return 1;
        }

        // ---- Tick ------------------------------------------------------------------------

        private void Apply(WeatherSnapshot snapshot, float playerX, float playerZ)
        {
            int tier = Mathf.Clamp((int)snapshot.Warning, 0, TierCount - 1);

            chipLabel.text = StormReadout.Warning(snapshot.Warning);
            chipLabel.color = chipInk[tier];
            if (chipBox != null) chipBox.color = chipFill[tier];

            // The one cell worth naming: the tier's own source when there is a warning, else the
            // nearest supercell. Never invented, and never a zero when there is nothing.
            StormCell source = snapshot.WarningSource;
            bool hasCell = snapshot.Warning != StormWarning.None;
            if (!hasCell)
            {
                hasCell = StormReadout.Nearest(snapshot.Cells, snapshot.CellCount, playerX, playerZ,
                    out source, out _);
            }

            subjectLabel.text = hasCell
                ? StormReadout.Kind(source.Kind)
                : WeatherReadout.Regime(snapshot.Live.Regime);

            text.Length = 0;
            text.Append("WIND ");
            text.Append(WeatherReadout.Wind(snapshot.LocalWindSpeed, snapshot.LocalWindHeading));
            if (snapshot.Atmosphere.Available)
            {
                text.Append("   GUST ");
                text.Append(WeatherReadout.Speed(snapshot.Atmosphere.GustSpeed));
            }
            windLabel.SetText(text);

            Color roseColor = rail[tier];
            if (roseFrame != null)
            {
                for (int i = 0; i < roseFrame.Length; i++)
                {
                    if (roseFrame[i] != null) roseFrame[i].color = roseColor;
                }
            }

            if (hasCell)
            {
                float distance = source.DistanceTo(playerX, playerZ);
                float bearing = StormReadout.BearingDegrees(playerX, playerZ, source.X, source.Z);
                text.Length = 0;
                text.Append(StormReadout.NauticalMiles(distance));
                text.Append("  ");
                text.Append(StormReadout.BearingTo(playerX, playerZ, source.X, source.Z));
                cellLabel.SetText(text);
                PlaceBlip(bearing, distance, roseColor);
            }
            else
            {
                cellLabel.text = "NO STORM IN RANGE";
                if (roseNose != null) roseNose.color = AvTheme.Dim;
                if (roseBlip != null) roseBlip.gameObject.SetActive(false);
            }

            Color frameColor = rail[tier];
            if (snapshot.Warning == StormWarning.Warning)
            {
                // A sine breath, not a blink: a strobe in the cockpit is a hazard.
                float pulse = Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / PulsePeriod));
                frameColor.a *= 1f - PulseDepth * (0.5f + 0.5f * pulse);
            }
            if (frame != null)
            {
                for (int i = 0; i < frame.Length; i++)
                {
                    if (frame[i] != null) frame[i].color = frameColor;
                }
            }
        }

        /// <summary>
        /// Put the blip where the cell is relative to the nose: the ring is read like the
        /// attitude of the traffic, not like north-up terrain.
        /// </summary>
        private void PlaceBlip(float bearing, float distance, Color tint)
        {
            if (roseBlip == null) return;

            bool hasHeading = TryOwnshipHeading(out float heading);
            if (roseNose != null) roseNose.color = hasHeading ? tint : AvTheme.Dim;
            if (!hasHeading)
            {
                if (roseBlip.gameObject.activeSelf) roseBlip.gameObject.SetActive(false);
                return;
            }

            float radians = (bearing - heading) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(RoseMinRadius, RoseMaxRadius, Mathf.Clamp01(distance / RoseFullRangeMetres));
            roseBlip.rectTransform.anchoredPosition =
                new Vector2(Mathf.Sin(radians) * radius, Mathf.Cos(radians) * radius);
            roseBlip.color = tint;
            if (!roseBlip.gameObject.activeSelf) roseBlip.gameObject.SetActive(true);
        }
    }
}
