using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Support.Configuration;
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
        private const int MaxActiveMarkers = 16;

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
            public RectTransform RootRect;
            public Image DangerRing;
            public Image DangerFill;
            public GameObject CenterObj;
            public Image CenterIcon;
            public GameObject BadgeObj;
            public RectTransform BadgeRect;
            public Image BadgeBg;
            public TextMeshProUGUI BadgeText;
            public bool IsInUse;
        }

        private readonly List<StrikeMarker> markerPool = new List<StrikeMarker>(MaxActiveMarkers);
        private bool initialized;

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

            // 2. Update Active Strike Waypoint Beacons
            UpdateActiveStrikes(mapFactor, invZoom);
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
            armedCardRect.sizeDelta = new Vector2(170f, 54f);
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
                badgeRect.sizeDelta = new Vector2(100f, 22f);
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
                    RootRect = rootRect,
                    DangerRing = ringImg,
                    DangerFill = fillImg,
                    CenterObj = centerObj,
                    CenterIcon = centerImg,
                    BadgeObj = badgeObj,
                    BadgeRect = badgeRect,
                    BadgeBg = badgeBg,
                    BadgeText = badgeText,
                    IsInUse = false
                });
            }
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

            // Position reticle exactly at terrain cursor coordinate in map local space
            Vector3 localPos = new Vector3(cursorCoord.x * mapFactor, cursorCoord.z * mapFactor, 0f);
            armedGroup.transform.localPosition = localPos;

            // Physical diameter of effect on the map
            float diameterMapUnits = radius * 2f * mapFactor;
            Vector2 diameterVector = new Vector2(diameterMapUnits, diameterMapUnits);

            armedRingImage.rectTransform.sizeDelta = diameterVector;
            armedFillImage.rectTransform.sizeDelta = diameterVector;

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
                distMeters = Vector3.Distance(localPlayer.Aircraft.transform.position, cursorCoord.AsVector3());
                hasPlayerAircraft = true;
            }

            float maxRange = action == Runtime.SupportActionId.Recon
                ? (settings != null ? settings.ReconRange.Value : 120000f)
                : (settings != null ? settings.MaximumRange.Value : 30000f);

            bool outOfRange = hasPlayerAircraft && action != Runtime.SupportActionId.Recon && distMeters > maxRange;

            // Theme colors
            Color themeColor = GetActionColor(action);
            Color hudAccent = outOfRange ? new Color(1f, 0.28f, 0.22f, 1f) : themeColor;

            armedRingImage.color = new Color(hudAccent.r, hudAccent.g, hudAccent.b, 0.85f);
            armedFillImage.color = new Color(hudAccent.r, hudAccent.g, hudAccent.b, outOfRange ? 0.08f : 0.16f);
            armedCenterIcon.color = hudAccent;
            armedCardBg.color = hudAccent;

            string actionCode = GetActionCode(action);
            string actionName = GetActionName(action);
            string rangeStr = distMeters >= 1000f ? $"{distMeters / 1000f:F1} km" : $"{distMeters:F0} m";
            string radiusStr = radius >= 1000f ? $"{radius / 1000f:F1} km" : $"{radius:F0} m";

            if (outOfRange)
            {
                armedCardText.text = $"<color=#FF4433><b>[{actionCode}] OUT OF RANGE</b></color>\n" +
                                     $"DIST: <color=#FFAA44>{rangeStr}</color> (MAX {maxRange / 1000f:F0}km)\n" +
                                     $"EFFECT RADIUS: {radiusStr}";
            }
            else
            {
                armedCardText.text = $"<b>[{actionCode}] {actionName}</b>\n" +
                                     $"DIST: {rangeStr} · RADIUS: {radiusStr}\n" +
                                     $"<color=#88DDFF>RIGHT-CLICK TO CONFIRM</color>";
            }
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

                    // Counter-scale icon and badge
                    marker.CenterObj.transform.localScale = Vector3.one * invZoom;
                    marker.BadgeObj.transform.localScale = Vector3.one * invZoom;
                    marker.BadgeObj.transform.localPosition = new Vector3(0f, -22f * invZoom, 0f);

                    Color strikeColor = GetActionColor(strike.ActionId);
                    marker.CenterIcon.sprite = SupportTacticalIcons.GetIcon(strike.ActionId);
                    marker.CenterIcon.color = strikeColor;
                    marker.BadgeBg.color = strikeColor;

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
                        marker.BadgeText.text = $"<b>{code}</b>  T-{sec:D2}s";
                        marker.BadgeText.color = Color.white;
                    }
                    else
                    {
                        // Impact confirmed / active effect phase
                        float flash = Mathf.Sin(now * 14f) > 0f ? 1f : 0.4f;
                        marker.BadgeText.text = $"<color=#FF5533><b>{code} SPLASH</b></color>";
                        marker.BadgeText.color = new Color(1f, 0.35f, 0.2f, flash);
                    }
                }
                else if (marker.IsInUse)
                {
                    marker.Root.SetActive(false);
                    marker.IsInUse = false;
                }
            }
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
                case Runtime.SupportActionId.FlareMissile:
                    return new Color(1f, 0.82f, 0.2f, 1f);   // Pyrotechnic flare yellow
                case Runtime.SupportActionId.Fortify:
                    return new Color(0.35f, 0.65f, 1f, 1f);  // Garrison fortification blue
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
                case Runtime.SupportActionId.Recon: return "SAT";
                case Runtime.SupportActionId.FlareMissile: return "FLR";
                case Runtime.SupportActionId.Fortify: return "FTF";
                default: return "OPS";
            }
        }

        private static string GetActionName(Runtime.SupportActionId action)
        {
            switch (action)
            {
                case Runtime.SupportActionId.Artillery: return "ROD FROM GOD";
                case Runtime.SupportActionId.Emp: return "EMP SHOCK";
                case Runtime.SupportActionId.Recon: return "SATELLITE SCAN";
                case Runtime.SupportActionId.FlareMissile: return "FLARE BARRAGE";
                case Runtime.SupportActionId.Fortify: return "FORTIFICATION";
                default: return "SUPPORT CALL-IN";
            }
        }
    }
}
