using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// Delivers the tactical theater map overlay for Boscali Summer fire support abilities:
    /// 1. Armed Cursor Reticle: Real-time mouse hover tracking on DynamicMap displaying exact
    ///    physical radius of effect (radiusMeters * mapDisplayFactor), cardinal ticks,
    ///    ability tactical vector icon, slant range distance, and out-of-range caution alert.
    /// 2. Active Strike Waypoints: Tactical on-map beacons for all in-flight or detonating abilities,
    ///    featuring animated pulsing danger/effect-zone rings, NATO ability icons, and live
    ///    ETA countdown badges ("ROD T-06s", "EMP T-11s", "SPLASH").
    /// </summary>
    internal sealed class SupportMapOverlay : MonoBehaviour, ISceneService
    {
        private const int MaxActiveMarkers = 17; // sixteen effects plus one faction scan reservation

        private SupportSettings settings;
        private SupportManager supportManager;
        private ManualLogSource logger;

        private DynamicMap dynamicMap;
        private GameObject overlayRoot;
        private RectTransform overlayRect;
        private TMP_FontAsset uiFont;

        // Armed Reticle UI Components
        private GameObject armedGroup;
        private RectTransform armedGroupRect;
        private Image armedRingImage;
        private Image armedFillImage;
        private Image armedCore;
        private GameObject armedCenterObj;
        private Image armedCenterIcon;
        private GameObject armedCardObj;
        private RectTransform armedCardRect;
        private Image armedCardBg;
        private TextMeshProUGUI armedCardText;

        // Active Strike Waypoint Marker Pool
        private sealed class StrikeMarker
        {
            public GameObject Root;
            public Image DangerRing;
            public Image DangerFill;
            public Image Core;
            public GameObject CenterObj;
            public Image CenterIcon;
            public GameObject BadgeObj;
            public Image BadgeBg;
            public TextMeshProUGUI BadgeText;
            public bool IsInUse;
        }

        private readonly List<StrikeMarker> markerPool = new List<StrikeMarker>(MaxActiveMarkers);
        private bool initialized;

        // Orbital overlay: the sub-station point, for our station and (as unknown red tracks)
        // foreign ones, plus the last uplink aim. Everything is pooled and bounded.
        private const int OrbitMarkers = 1 + SpaceOperations.MaximumForeign;
        private static readonly Color StationColour = new Color(0.35f, 0.9f, 1f, 1f);
        private static readonly Color HostileStationColour = new Color(1f, 0.32f, 0.26f, 1f);

        private sealed class OrbitMarker
        {
            public GameObject Icon;
            public Image IconImage;
            public TextMeshProUGUI BadgeText;
        }

        private readonly List<OrbitMarker> orbitPool = new List<OrbitMarker>(OrbitMarkers);
        private GameObject aimMarker;
        private CyberMapLayer cyberLayer;
        private SpecOpsMapLayer fieldLayer;

        public void Configure(SupportSettings config, SupportManager manager, ManualLogSource log)
        {
            settings = config;
            supportManager = manager;
            logger = log;
            SupportTacticalIcons.EnsureInitialized();
        }

        public void ResetForScene()
        {
            if (overlayRoot != null)
            {
                Destroy(overlayRoot);
                overlayRoot = null;
            }

            markerPool.Clear();
            orbitPool.Clear();
            aimMarker = null;
            cyberLayer = null;
            fieldLayer = null;
            dynamicMap = null;
            initialized = false;
        }

        private void OnDestroy()
        {
            ResetForScene();
        }

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value || !settings.ShowOnTacticalMap.Value)
            {
                if (overlayRoot != null && overlayRoot.activeSelf) overlayRoot.SetActive(false);
                return;
            }

            if (!initialized)
            {
                TryInitialize();
                return;
            }

            if (dynamicMap == null || dynamicMap.mapImage == null) return;

            bool isMaximized = DynamicMap.mapMaximized;
            if (overlayRoot.activeSelf != isMaximized)
            {
                overlayRoot.SetActive(isMaximized);
            }

            if (!isMaximized) return;

            float mapZoom = Mathf.Max(0.001f, dynamicMap.mapImage.transform.localScale.x);
            float invZoom = 1f / mapZoom;
            float mapFactor = dynamicMap.mapDisplayFactor;

            // 1. Update Armed Reticle
            UpdateArmedReticle(mapFactor, invZoom);

            // 3. Update Active Strike Waypoint Beacons
            UpdateActiveStrikes(mapFactor, invZoom);

            // 3. Station ground tracks, sub-station points and the uplink aim
            UpdateOrbits(mapFactor, invZoom);

            // 4. The CYBER network: sites, links, covers, incidents and the placement preview
            cyberLayer?.Update(supportManager, dynamicMap, mapFactor, invZoom);

            // 5. SPEC OPS: deployed teams and the reach of every held post
            fieldLayer?.Update(supportManager, mapFactor, invZoom);
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;

            SupportTacticalIcons.EnsureInitialized();
            ResolveFont();

            overlayRoot = new GameObject("BoscaliSummer.SupportMapOverlay", typeof(RectTransform));
            overlayRoot.transform.SetParent(dynamicMap.mapImage.transform, false);

            overlayRect = overlayRoot.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.localScale = Vector3.one;
            overlayRect.localPosition = Vector3.zero;

            // Ensure our overlay draws above base terrain map
            overlayRoot.transform.SetAsLastSibling();

            BuildArmedReticle();
            BuildMarkerPool();
            BuildOrbitOverlay();
            cyberLayer = new CyberMapLayer(overlayRoot.transform, uiFont);
            fieldLayer = new SpecOpsMapLayer(overlayRoot.transform, uiFont);

            initialized = true;
            logger?.LogInfo("[Support] Tactical theater map overlay initialized.");
        }

        private void ResolveFont()
        {
            if (uiFont != null) return;
            try
            {
                TMP_Text sample = dynamicMap.GetComponentInChildren<TMP_Text>(true);
                if (sample != null && sample.font != null)
                {
                    uiFont = sample.font;
                }
                else
                {
                    uiFont = TMP_Settings.defaultFontAsset;
                }
            }
            catch (Exception)
            {
                uiFont = TMP_Settings.defaultFontAsset;
            }
        }

        private void BuildArmedReticle()
        {
            armedGroup = new GameObject("ArmedReticleGroup", typeof(RectTransform));
            armedGroup.transform.SetParent(overlayRoot.transform, false);
            armedGroupRect = armedGroup.GetComponent<RectTransform>();
            armedGroupRect.pivot = new Vector2(0.5f, 0.5f);
            armedGroupRect.localScale = Vector3.one;

            // Shaded effect zone radial fill
            var fillObj = new GameObject("RangeFill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(armedGroup.transform, false);
            armedFillImage = fillObj.GetComponent<Image>();
            armedFillImage.sprite = SupportTacticalIcons.RadialFillSprite;
            armedFillImage.type = Image.Type.Simple;
            armedFillImage.raycastTarget = false;

            // Outer range ring with cardinal tick marks
            var ringObj = new GameObject("RangeRing", typeof(RectTransform), typeof(Image));
            ringObj.transform.SetParent(armedGroup.transform, false);
            armedRingImage = ringObj.GetComponent<Image>();
            armedRingImage.sprite = SupportTacticalIcons.RingSprite;
            armedRingImage.type = Image.Type.Simple;
            armedRingImage.raycastTarget = false;

            armedCore = CreateCore(armedGroup.transform);

            // Center cursor ability icon & crosshair (counter-scaled so it stays crisp on screen)
            armedCenterObj = new GameObject("CenterMarker", typeof(RectTransform), typeof(Image));
            armedCenterObj.transform.SetParent(armedGroup.transform, false);
            RectTransform centerRect = armedCenterObj.GetComponent<RectTransform>();
            centerRect.sizeDelta = new Vector2(36f, 36f);
            centerRect.pivot = new Vector2(0.5f, 0.5f);
            armedCenterIcon = armedCenterObj.GetComponent<Image>();
            armedCenterIcon.sprite = SupportTacticalIcons.CrosshairSprite;
            armedCenterIcon.raycastTarget = false;

            // Telemetry HUD card (counter-scaled)
            armedCardObj = new GameObject("TelemetryCard", typeof(RectTransform), typeof(Image));
            armedCardObj.transform.SetParent(armedGroup.transform, false);
            armedCardRect = armedCardObj.GetComponent<RectTransform>();
            armedCardRect.sizeDelta = new Vector2(224f, 74f);
            armedCardRect.pivot = new Vector2(0f, 1f); // Top-left anchor for offset placement
            armedCardBg = armedCardObj.GetComponent<Image>();
            armedCardBg.sprite = SupportTacticalIcons.BadgeBgSprite;
            armedCardBg.type = Image.Type.Sliced;
            armedCardBg.raycastTarget = false;

            var textObj = new GameObject("CardText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(armedCardObj.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-6f, -4f);

            armedCardText = textObj.GetComponent<TextMeshProUGUI>();
            if (uiFont != null) armedCardText.font = uiFont;
            armedCardText.fontSize = 9.5f;
            armedCardText.lineSpacing = 1.1f;
            armedCardText.alignment = TextAlignmentOptions.TopLeft;
            armedCardText.color = Color.white;
            armedCardText.raycastTarget = false;

            armedGroup.SetActive(false);
        }

        private void BuildMarkerPool()
        {
            for (int i = 0; i < MaxActiveMarkers; i++)
            {
                var markerObj = new GameObject("ActiveStrikeMarker_" + i, typeof(RectTransform));
                markerObj.transform.SetParent(overlayRoot.transform, false);
                var rootRect = markerObj.GetComponent<RectTransform>();
                rootRect.pivot = new Vector2(0.5f, 0.5f);

                // Danger zone radial fill
                var fillObj = new GameObject("DangerFill", typeof(RectTransform), typeof(Image));
                fillObj.transform.SetParent(markerObj.transform, false);
                var fillImg = fillObj.GetComponent<Image>();
                fillImg.sprite = SupportTacticalIcons.RadialFillSprite;
                fillImg.raycastTarget = false;

                // Danger zone perimeter ring (dotted)
                var ringObj = new GameObject("DangerRing", typeof(RectTransform), typeof(Image));
                ringObj.transform.SetParent(markerObj.transform, false);
                var ringImg = ringObj.GetComponent<Image>();
                ringImg.sprite = SupportTacticalIcons.DottedRingSprite;
                ringImg.raycastTarget = false;

                // Ability center vector icon
                var centerObj = new GameObject("CenterIcon", typeof(RectTransform), typeof(Image));
                centerObj.transform.SetParent(markerObj.transform, false);
                var centerRect = centerObj.GetComponent<RectTransform>();
                centerRect.sizeDelta = new Vector2(30f, 30f);
                centerRect.pivot = new Vector2(0.5f, 0.5f);
                var centerImg = centerObj.GetComponent<Image>();
                centerImg.raycastTarget = false;

                // Countdown badge
                var badgeObj = new GameObject("CountdownBadge", typeof(RectTransform), typeof(Image));
                badgeObj.transform.SetParent(markerObj.transform, false);
                var badgeRect = badgeObj.GetComponent<RectTransform>();
                badgeRect.sizeDelta = new Vector2(180f, 40f);
                badgeRect.pivot = new Vector2(0.5f, 1f); // sits directly below center icon
                var badgeBg = badgeObj.GetComponent<Image>();
                badgeBg.sprite = SupportTacticalIcons.BadgeBgSprite;
                badgeBg.type = Image.Type.Sliced;
                badgeBg.raycastTarget = false;

                var textObj = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObj.transform.SetParent(badgeObj.transform, false);
                var textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(4f, 2f);
                textRect.offsetMax = new Vector2(-4f, -2f);

                var badgeText = textObj.GetComponent<TextMeshProUGUI>();
                if (uiFont != null) badgeText.font = uiFont;
                badgeText.fontSize = 9.0f;
                badgeText.alignment = TextAlignmentOptions.Center;
                badgeText.color = Color.white;
                badgeText.raycastTarget = false;

                markerObj.SetActive(false);
                markerPool.Add(new StrikeMarker
                {
                    Root = markerObj,
                    Core = CreateCore(markerObj.transform),
                    DangerRing = ringImg,
                    DangerFill = fillImg,
                    CenterObj = centerObj,
                    CenterIcon = centerImg,
                    BadgeObj = badgeObj,
                    BadgeBg = badgeBg,
                    BadgeText = badgeText,
                    IsInUse = false
                });
            }
        }

        private void BuildOrbitOverlay()
        {
            for (int i = 0; i < OrbitMarkers; i++)
            {
                var marker = new OrbitMarker();
                marker.Icon = new GameObject("StationPoint", typeof(RectTransform), typeof(Image));
                marker.Icon.transform.SetParent(overlayRoot.transform, false);
                var iconRect = (RectTransform)marker.Icon.transform;
                iconRect.sizeDelta = new Vector2(22f, 22f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                marker.IconImage = marker.Icon.GetComponent<Image>();
                marker.IconImage.sprite = SupportTacticalIcons.SatIcon;
                marker.IconImage.raycastTarget = false;

                var badgeObj = new GameObject("StationBadge", typeof(RectTransform), typeof(Image));
                badgeObj.transform.SetParent(marker.Icon.transform, false);
                var badgeRect = (RectTransform)badgeObj.transform;
                badgeRect.sizeDelta = new Vector2(150f, 20f);
                badgeRect.pivot = new Vector2(0.5f, 1f);
                badgeRect.anchoredPosition = new Vector2(0f, -14f);
                var badgeBg = badgeObj.GetComponent<Image>();
                badgeBg.sprite = SupportTacticalIcons.BadgeBgSprite;
                badgeBg.type = Image.Type.Sliced;
                badgeBg.color = new Color(0.025f, 0.05f, 0.06f, 0.92f);
                badgeBg.raycastTarget = false;

                var textObj = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObj.transform.SetParent(badgeObj.transform, false);
                var textRect = (RectTransform)textObj.transform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(4f, 2f);
                textRect.offsetMax = new Vector2(-4f, -2f);
                marker.BadgeText = textObj.GetComponent<TextMeshProUGUI>();
                if (uiFont != null) marker.BadgeText.font = uiFont;
                marker.BadgeText.fontSize = 8.5f;
                marker.BadgeText.alignment = TextAlignmentOptions.Center;
                marker.BadgeText.color = Color.white;
                marker.BadgeText.raycastTarget = false;

                marker.Icon.SetActive(false);
                orbitPool.Add(marker);
            }

            aimMarker = new GameObject("UplinkAim", typeof(RectTransform), typeof(Image));
            aimMarker.transform.SetParent(overlayRoot.transform, false);
            ((RectTransform)aimMarker.transform).sizeDelta = new Vector2(26f, 26f);
            Image aim = aimMarker.GetComponent<Image>();
            aim.sprite = SupportTacticalIcons.CrosshairSprite;
            aim.color = new Color(1f, 0.72f, 0.22f, 0.95f);
            aim.raycastTarget = false;
            aimMarker.SetActive(false);
        }

        private void UpdateArmedReticle(float mapFactor, float invZoom)
        {
            if (supportManager == null || !supportManager.ArmedAction.HasValue)
            {
                if (armedGroup != null && armedGroup.activeSelf) armedGroup.SetActive(false);
                return;
            }

            if (!dynamicMap.TryGetCursorCoordinates(out GlobalPosition cursorCoord))
            {
                if (armedGroup != null && armedGroup.activeSelf) armedGroup.SetActive(false);
                return;
            }

            if (!armedGroup.activeSelf) armedGroup.SetActive(true);

            Runtime.SupportActionId action = supportManager.ArmedAction.Value;
            float radius = supportManager.GetEffectRadius(action);

            GlobalPosition areaCenter = cursorCoord;
            supportManager.ResolveMapArea(action, ref areaCenter, ref radius);

            // Position reticle exactly at terrain cursor coordinate in map local space
            Vector3 localPos = new Vector3(areaCenter.x * mapFactor, areaCenter.z * mapFactor, 0f);
            armedGroup.transform.localPosition = localPos;

            // Physical diameter of effect on the map
            float diameterMapUnits = radius * 2f * mapFactor;
            Vector2 diameterVector = new Vector2(diameterMapUnits, diameterMapUnits);

            armedRingImage.rectTransform.sizeDelta = diameterVector;
            armedFillImage.rectTransform.sizeDelta = diameterVector;
            SetCore(armedCore, action, mapFactor);

            // Counter-scale center icon and telemetry card so they stay constant pixel size on screen
            armedCenterObj.transform.localScale = Vector3.one * invZoom;
            armedCenterIcon.sprite = SupportTacticalIcons.GetIcon(action);

            armedCardObj.transform.localScale = Vector3.one * invZoom;
            // Position card offset from cursor (30px right, 25px down in screen pixels)
            armedCardObj.transform.localPosition = new Vector3(28f * invZoom, -22f * invZoom, 0f);

            // Compute aircraft slant range & out-of-range check
            float distMeters = 0f;
            bool hasPlayerAircraft = false;
            if (GameManager.GetLocalPlayer<Player>(out Player localPlayer) && localPlayer?.Aircraft != null)
            {
                distMeters = Vector3.Distance(localPlayer.Aircraft.transform.position, cursorCoord.ToLocalPosition());
                hasPlayerAircraft = true;
            }

            float maxRange = settings != null ? settings.MaximumRange.Value : 30000f;
            bool ranged = action == Runtime.SupportActionId.Artillery ||
                          action == Runtime.SupportActionId.Emp ||
                          action == Runtime.SupportActionId.FlareMissile;

            bool outOfRange = ranged && hasPlayerAircraft && distMeters > maxRange;

            // Theme colors
            Color themeColor = GetActionColor(action);
            Color hudAccent = outOfRange ? new Color(1f, 0.28f, 0.22f, 1f) : themeColor;

            bool hasArea = radius > 0.5f;
            if (armedRingImage.enabled != hasArea) armedRingImage.enabled = hasArea;
            if (armedFillImage.enabled != hasArea) armedFillImage.enabled = hasArea;

            armedRingImage.color = new Color(hudAccent.r, hudAccent.g, hudAccent.b, 0.85f);
            armedFillImage.color = new Color(hudAccent.r, hudAccent.g, hudAccent.b, outOfRange ? 0.08f : 0.16f);
            armedCenterIcon.color = hudAccent;
            armedCardBg.color = new Color(0.025f, 0.05f, 0.06f, 0.95f);

            string actionCode = GetActionCode(action);
            string actionName = GetActionName(action);
            string rangeStr = !hasPlayerAircraft ? "—" : distMeters >= 1000f ? $"{distMeters / 1000f:F1} km" : $"{distMeters:F0} m";
            string radiusStr = radius <= 0f ? "—" : radius >= 1000f ? $"{radius / 1000f:F1} km" : $"{radius:F0} m";

            string areaLabel = action == Runtime.SupportActionId.Fortify ? "ZONE" : "AREA";
            if (action == Runtime.SupportActionId.Artillery) radiusStr += "\nCORE: 150 m";
            if (action == Runtime.SupportActionId.Fortify && radius <= 0f)
            {
                armedCardText.text = "<b>FTF · SELECT AN OWNED ZONE</b>\nFortification reinforces a base garrison.";
                return;
            }
            if (outOfRange)
            {
                armedCardText.text = $"<color=#FF4433><b>[{actionCode}] OUT OF RANGE</b></color>\n" +
                                     $"DIST: <color=#FFAA44>{rangeStr}</color> (MAX {maxRange / 1000f:F0}km)\n" +
                                     $"EFFECT {areaLabel}: {radiusStr}";
            }
            else
            {
                PlatformAbility? needed = SupportManager.OrbitalAbility(action);
                string coverage = string.Empty;
                bool noAccess = false;
                if (needed.HasValue)
                {
                    PlatformDenial denial = supportManager.PlatformCheck(needed.Value);
                    noAccess = denial != PlatformDenial.None;
                    coverage = noAccess
                        ? "<color=#FFAA44>" + PlatformWords.Denial(denial, supportManager.LocalPlatform, needed.Value,
                            supportManager.OrbitNow, supportManager.OrbitClock) + "</color>\n"
                        : "<color=#66FF99>" + OrbitalPlatform.Callsign + " OVERHEAD · READY</color>\n";
                }
                string area = radius <= 0f ? "FLEET-WIDE"
                    : areaLabel + ": " + radiusStr;
                armedCardText.text = coverage + $"<b>[{actionCode}] {actionName}</b>\n" +
                                     $"DIST: {rangeStr} · {area}\n" +
                                     $"<color=#88DDFF>RIGHT-CLICK TO CONFIRM</color>";
                if (noAccess)
                {
                    armedRingImage.color = new Color(1f, 0.68f, 0.2f, 0.85f);
                    armedFillImage.color = new Color(1f, 0.68f, 0.2f, 0.06f);
                }
            }
        }

        private void UpdateOrbits(float mapFactor, float invZoom)
        {
            int used = 0;
            float reach = OrbitalBounds.Radius() * 1.42f;
            if (supportManager != null && reach > 0f)
            {
                double now = supportManager.OrbitNow;
                OrbitClock clock = supportManager.OrbitClock;

                OrbitalPlatform platform = supportManager.LocalPlatform;
                if (platform != null && platform.Exists)
                {
                    OrbitState state = platform.State(now, clock);
                    if (state.InPass)
                        DrawOrbit(orbitPool[used++], state, reach, mapFactor, invZoom, StationColour,
                            "<b>" + OrbitalPlatform.Callsign + "</b> · " + StationKeeping.Name(platform.PositionIndex));
                }

                IReadOnlyList<ForeignPlatform> others = supportManager.Space.Foreign;
                for (int i = 0; i < others.Count && used < orbitPool.Count; i++)
                {
                    OrbitState state = others[i].State(now, clock);
                    if (!state.InPass) continue;
                    DrawOrbit(orbitPool[used++], state, reach, mapFactor, invZoom, HostileStationColour,
                        "<b>UNKNOWN STATION</b> " + OrbitRegimes.Get(others[i].Regime).Code);
                }
            }

            for (int i = used; i < orbitPool.Count; i++)
            {
                OrbitMarker marker = orbitPool[i];
                if (marker.Icon.activeSelf) marker.Icon.SetActive(false);
            }

            if (aimMarker != null)
            {
                bool show = supportManager != null && supportManager.UplinkAimSet;
                if (aimMarker.activeSelf != show) aimMarker.SetActive(show);
                if (show)
                {
                    GlobalPosition aim = supportManager.UplinkAim;
                    aimMarker.transform.localPosition = new Vector3(aim.x * mapFactor, aim.z * mapFactor, 0f);
                    aimMarker.transform.localScale = Vector3.one * invZoom;
                }
            }
        }

        private static void DrawOrbit(OrbitMarker marker, in OrbitState state, float reach, float mapFactor, float invZoom,
                                      Color colour, string label)
        {
            bool onMap = state.SubX * state.SubX + state.SubZ * state.SubZ <= reach * (double)reach;
            if (marker.Icon.activeSelf != onMap) marker.Icon.SetActive(onMap);
            if (!onMap) return;
            marker.Icon.transform.localPosition = new Vector3((float)state.SubX * mapFactor, (float)state.SubZ * mapFactor, 0f);
            marker.Icon.transform.localScale = Vector3.one * invZoom;
            marker.IconImage.color = colour;
            marker.BadgeText.text = label;
        }

        private void UpdateActiveStrikes(float mapFactor, float invZoom)
        {
            var activeStrikes = supportManager?.ActiveStrikes;
            int count = activeStrikes != null ? activeStrikes.Count : 0;
            float now = Time.timeSinceLevelLoad;

            for (int i = 0; i < markerPool.Count; i++)
            {
                StrikeMarker marker = markerPool[i];
                if (i < count)
                {
                    ActiveStrikeInfo strike = activeStrikes[i];
                    if (!strike.IsActive(now))
                    {
                        if (marker.IsInUse)
                        {
                            marker.Root.SetActive(false);
                            marker.IsInUse = false;
                        }
                        continue;
                    }

                    if (!marker.IsInUse)
                    {
                        marker.Root.SetActive(true);
                        marker.IsInUse = true;
                    }

                    // Position at target coordinate
                    Vector3 strikePos = new Vector3(strike.Target.x * mapFactor, strike.Target.z * mapFactor, 0f);
                    marker.Root.transform.localPosition = strikePos;

                    // Physical danger zone diameter
                    float diameter = strike.Radius * 2f * mapFactor;
                    marker.DangerRing.rectTransform.sizeDelta = new Vector2(diameter, diameter);
                    marker.DangerFill.rectTransform.sizeDelta = new Vector2(diameter, diameter);

                    SetCore(marker.Core, strike.ActionId, mapFactor);

                    // Counter-scale icon and badge
                    marker.CenterObj.transform.localScale = Vector3.one * invZoom;
                    marker.BadgeObj.transform.localScale = Vector3.one * invZoom;
                    marker.BadgeObj.transform.localPosition = new Vector3(0f, -22f * invZoom, 0f);

                    Color strikeColor = GetActionColor(strike.ActionId);
                    marker.CenterIcon.sprite = SupportTacticalIcons.GetIcon(strike.ActionId);
                    marker.CenterIcon.color = strikeColor;
                    marker.BadgeBg.color = new Color(0.025f, 0.05f, 0.06f, 0.95f);

                    // Animated breathing/pulsing danger ring
                    float pulse = 0.55f + Mathf.Sin(now * 6.5f) * 0.25f;
                    marker.DangerRing.color = new Color(strikeColor.r, strikeColor.g, strikeColor.b, pulse);
                    marker.DangerFill.color = new Color(strikeColor.r, strikeColor.g, strikeColor.b, 0.12f * pulse);

                    // Live countdown / splash text
                    float remaining = strike.ImpactTime - now;
                    string code = GetActionCode(strike.ActionId);
                    if (remaining > 0f)
                    {
                        int sec = Mathf.CeilToInt(remaining);
                        marker.BadgeText.text = $"<b>{code}</b>  ETA ~{sec:D2}s\nRADIUS {strike.Radius / 1000f:0.00} km";
                        marker.BadgeText.color = Color.white;
                    }
                    else
                    {
                        // Impact confirmed / active effect phase
                        string phase = strike.ActionId == SupportActionId.Recon ? "SAR SCENE" :
                            strike.ActionId == SupportActionId.MtiSweep ? "MTI TRACK" :
                            strike.ActionId == SupportActionId.Fortify ? "REINFORCED" : "EST. ACTIVE";
                        marker.BadgeText.text = $"<b>{code}</b> {phase}\nRADIUS {strike.Radius / 1000f:0.00} km";
                        marker.BadgeText.color = Color.white;
                    }
                }
                else if (marker.IsInUse)
                {
                    marker.Root.SetActive(false);
                    marker.IsInUse = false;
                }
            }
        }

        private static Image CreateCore(Transform parent)
        {
            var go = new GameObject("Lethal core", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            var image = go.GetComponent<Image>();
            image.sprite = SupportTacticalIcons.DottedRingSprite;
            image.raycastTarget = false;
            image.color = new Color(1f, 0.8f, 0.4f, 0.9f);
            return image;
        }

        private static void SetCore(Image image, SupportActionId action, float factor)
        {
            image.gameObject.SetActive(action == SupportActionId.Artillery);
            image.rectTransform.sizeDelta = Vector2.one * (SupportEffectPolicy.RodCoreRadius * 2f * factor);
        }

        private static Color GetActionColor(Runtime.SupportActionId action)
        {
            switch (action)
            {
                case Runtime.SupportActionId.Artillery:
                    return new Color(1f, 0.45f, 0.12f, 1f); // High-heat kinetic orange
                case Runtime.SupportActionId.Emp:
                    return new Color(0.2f, 0.85f, 1f, 1f);   // Electric cyan
                case Runtime.SupportActionId.Recon:
                    return new Color(0.3f, 0.95f, 0.5f, 1f);  // Tactical reconnaissance green
                case Runtime.SupportActionId.ElintSweep:
                    return new Color(0.55f, 1f, 0.85f, 1f);  // Signals teal
                case Runtime.SupportActionId.MtiSweep:
                    return new Color(0.45f, 1f, 0.55f, 1f);  // Moving-target green
                case Runtime.SupportActionId.FlareMissile:
                    return new Color(1f, 0.82f, 0.2f, 1f);   // Pyrotechnic flare yellow
                case Runtime.SupportActionId.Fortify:
                    return new Color(0.35f, 0.65f, 1f, 1f);  // Garrison fortification blue
                case Runtime.SupportActionId.HackPing:
                case Runtime.SupportActionId.HackTrack:
                case Runtime.SupportActionId.HackBlackout:
                case Runtime.SupportActionId.HackGhost:
                case Runtime.SupportActionId.HackSpoof:
                    return new Color(0.75f, 0.55f, 1f, 1f);  // Cyber operation violet
                case Runtime.SupportActionId.SpecSpot:
                case Runtime.SupportActionId.SpecSkywatch:
                case Runtime.SupportActionId.SpecEavesdrop:
                    return new Color(0.3f, 0.95f, 0.5f, 1f);  // Observation post green
                case Runtime.SupportActionId.SpecSuppress:
                case Runtime.SupportActionId.SpecHunt:
                    return new Color(1f, 0.72f, 0.22f, 1f);  // Saboteur cell amber
                default:
                    return new Color(0.25f, 0.85f, 1f, 1f);
            }
        }

        private static string GetActionCode(Runtime.SupportActionId action)
        {
            switch (action)
            {
                case Runtime.SupportActionId.Artillery: return "ROD";
                case Runtime.SupportActionId.Emp: return "EMP";
                case Runtime.SupportActionId.Recon: return "SAR";
                case Runtime.SupportActionId.MtiSweep: return "MTI";
                case Runtime.SupportActionId.ElintSweep: return "ELT";
                case Runtime.SupportActionId.FlareMissile: return "FLR";
                case Runtime.SupportActionId.Fortify: return "FTF";
                case Runtime.SupportActionId.HackPing: return "PNG";
                case Runtime.SupportActionId.HackTrack: return "TRK";
                case Runtime.SupportActionId.HackBlackout: return "C2B";
                case Runtime.SupportActionId.HackGhost: return "GST";
                case Runtime.SupportActionId.HackSpoof: return "SPF";
                case Runtime.SupportActionId.SpecSpot: return "SPT";
                case Runtime.SupportActionId.SpecSuppress: return "SUP";
                case Runtime.SupportActionId.SpecSkywatch: return "SKY";
                case Runtime.SupportActionId.SpecEavesdrop: return "EAV";
                case Runtime.SupportActionId.SpecHunt: return "HNT";
                default: return "OPS";
            }
        }

        private static string GetActionName(Runtime.SupportActionId action)
        {
            switch (action)
            {
                case Runtime.SupportActionId.Artillery: return "ROD FROM GOD";
                case Runtime.SupportActionId.Emp: return "EMP SHOCK";
                case Runtime.SupportActionId.Recon: return "RADAR SCAN";
                case Runtime.SupportActionId.MtiSweep: return "MTI SWEEP";
                case Runtime.SupportActionId.ElintSweep: return "ELINT SWEEP";
                case Runtime.SupportActionId.FlareMissile: return "FLARE BARRAGE";
                case Runtime.SupportActionId.Fortify: return "FORTIFICATION";
                case Runtime.SupportActionId.HackPing: return "PING SWEEP";
                case Runtime.SupportActionId.HackTrack: return "TRACK UPLINK";
                case Runtime.SupportActionId.HackBlackout: return "RADAR BLACKOUT";
                case Runtime.SupportActionId.HackGhost: return "GHOST SHIELD";
                case Runtime.SupportActionId.HackSpoof: return "SPOOF CONTACTS";
                case Runtime.SupportActionId.SpecSpot: return "SPOT";
                case Runtime.SupportActionId.SpecSuppress: return "SUPPRESS";
                case Runtime.SupportActionId.SpecSkywatch: return "SKYWATCH";
                case Runtime.SupportActionId.SpecEavesdrop: return "EAVESDROP";
                case Runtime.SupportActionId.SpecHunt: return "HUNT";
                default: return "SUPPORT CALL-IN";
            }
        }
    }
}
