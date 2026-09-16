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
    /// The always-on cockpit weather banner: the warning tier, the one line a pilot needs
    /// about the storm that owns it, and rain or cloud occlusion only when either is real.
    /// A screen-space overlay, so it claims no bezel and patches nothing, and it hides
    /// itself the moment the module, the setting or the camera view says it should.
    /// </summary>
    internal sealed class WeatherHud : MonoBehaviour, ISceneService
    {
        /// <summary>Ten hertz: fast enough for a distance readout, cheap enough to ignore.</summary>
        private const float TickSeconds = 0.1f;

        private const float PanelWidth = 320f;
        private const float PanelHeight = 74f;

        /// <summary>Below the vanilla top bar, clear of the cockpit HUD's own rows.</summary>
        private const float TopInset = 104f;

        private const float EdgeInset = 10f;
        private const float ChipWidth = 76f;
        private const float ChipHeight = 16f;
        private const float ChipGap = 8f;
        private const float LineHeight = 14f;
        private const float Line1Y = 8f;
        private const float Line2Y = 28f;
        private const float Line3Y = 48f;

        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        private const int TierCount = 4;

        private const float RainVisible = 0.02f;
        private const float OcclusionVisible = 0.3f;

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
        private TMP_Text regimeLabel;
        private TMP_Text hazardLabel;
        private TMP_Text detailLabel;
        private GameObject detailRoot;
        private Image[] frame;

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
            regimeLabel = null;
            hazardLabel = null;
            detailLabel = null;
            detailRoot = null;
            frame = null;
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

            float regimeX = EdgeInset + ChipWidth + ChipGap;
            regimeLabel = AvStyled.Label(panel,
                new Rect(regimeX, -Line1Y, PanelWidth - regimeX - EdgeInset, ChipHeight),
                "", "row-name", align: TextAlignmentOptions.MidlineLeft);

            hazardLabel = AvStyled.Label(panel,
                new Rect(EdgeInset, -Line2Y, PanelWidth - EdgeInset * 2f, LineHeight),
                "", "row-sub", align: TextAlignmentOptions.MidlineLeft);
            hazardLabel.enableWordWrapping = false;
            hazardLabel.overflowMode = TextOverflowModes.Overflow;
            hazardLabel.color = AvTheme.TextPrimary;

            detailLabel = AvStyled.Label(panel,
                new Rect(EdgeInset, -Line3Y, PanelWidth - EdgeInset * 2f, LineHeight),
                "", "row-sub", align: TextAlignmentOptions.MidlineLeft);
            detailLabel.enableWordWrapping = false;
            detailLabel.overflowMode = TextOverflowModes.Overflow;
            detailRoot = detailLabel.gameObject;
            detailRoot.SetActive(false);

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

            regimeLabel.text = WeatherReadout.Regime(snapshot.Live.Regime);

            text.Length = 0;
            if (snapshot.Warning == StormWarning.None)
            {
                text.Append(WeatherReadout.Wind(snapshot.LocalWindSpeed, snapshot.LocalWindHeading));
                text.Append("  ");
                text.Append(WeatherReadout.Meters(snapshot.Live.CloudBase));
            }
            else
            {
                StormCell source = snapshot.WarningSource;
                text.Append(StormReadout.HazardLine(
                    snapshot.Warning,
                    StormReadout.Kind(source.Kind),
                    source.DistanceTo(playerX, playerZ),
                    StormReadout.BearingTo(playerX, playerZ, source.X, source.Z)));
            }
            hazardLabel.SetText(text);

            bool rain = snapshot.RainIntensity > RainVisible;
            bool occlusion = snapshot.CloudOcclusion > OcclusionVisible;
            if (detailRoot.activeSelf != (rain || occlusion)) detailRoot.SetActive(rain || occlusion);
            if (rain || occlusion)
            {
                text.Length = 0;
                if (rain)
                {
                    text.Append("RAIN ");
                    text.Append(WeatherReadout.Percent01(snapshot.RainIntensity));
                }
                if (occlusion)
                {
                    if (rain) text.Append("  ");
                    text.Append("OCCLUSION ");
                    text.Append(WeatherReadout.Percent01(snapshot.CloudOcclusion));
                }
                detailLabel.SetText(text);
            }

            Color frameColor = rail[tier];
            if (snapshot.Warning == StormWarning.Warning)
            {
                // A sine breath, not a blink: a strobe in the cockpit is a hazard.
                float pulse = Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / PulsePeriod));
                frameColor.a *= 1f - PulseDepth * (0.5f + 0.5f * pulse);
            }
            if (frame == null) return;
            for (int i = 0; i < frame.Length; i++)
            {
                if (frame[i] != null) frame[i].color = frameColor;
            }
        }
    }
}
