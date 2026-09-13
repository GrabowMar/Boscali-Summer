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
            public RectTransform RootRect;
            public Image DangerRing;
            public Image DangerFill;
            public Image Core;
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

        // Constellation overlay: satellite icons, selected swath and transfer preview.
        private sealed class SatelliteMarker
        {
            public GameObject Root;
            public RectTransform RootRect;
            public Image Swath;
            public Image Ring;
            public GameObject IconObj;
            public Image Icon;
            public GameObject BadgeObj;
            public Image BadgeBg;
            public TextMeshProUGUI BadgeText;
            public GameObject PathObj;
            public Image Path;
            public GameObject DestObj;
            public bool IsInUse;
        }

        private readonly List<SatelliteMarker> satellitePool = new List<SatelliteMarker>(4);

        // Armed fleet-command preview: projected orbit and destination footprint.
        private GameObject commandGroup;
        private RectTransform commandGroupRect;
        private Image commandTrack;
        private RectTransform commandDestRect;
        private Image commandDestFill;
        private Image commandDestRing;
        private GameObject commandCenterObj;
        private GameObject commandCardObj;
        private Image commandCardBg;
        private TextMeshProUGUI commandCardText;
        private GameObject commandSatObj;
        private Image commandSatIcon;

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
            satellitePool.Clear();
            commandGroup = null;
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

            // 2. Armed fleet command preview (orbit burn / launch)
            UpdateCommandReticle(mapFactor, invZoom);

            // 3. Update Active Strike Waypoint Beacons
            UpdateActiveStrikes(mapFactor, invZoom);

            // 4. Update the faction constellation: shell tracks, footprints, satellites
            UpdateConstellation(mapFactor, invZoom);
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
            BuildCommandReticle();
            BuildMarkerPool();
            BuildConstellationOverlay();

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

        private void BuildCommandReticle()
        {
            commandGroup = new GameObject("CommandReticleGroup", typeof(RectTransform));
            commandGroup.transform.SetParent(overlayRoot.transform, false);
            commandGroupRect = commandGroup.GetComponent<RectTransform>();
            commandGroupRect.pivot = new Vector2(0.5f, 0.5f);
            commandGroupRect.localPosition = Vector3.zero;
            commandGroupRect.localScale = Vector3.one;

            var pathObj = new GameObject("TransferPath", typeof(RectTransform), typeof(Image));
            pathObj.transform.SetParent(commandGroup.transform, false);
            var pathRect = pathObj.GetComponent<RectTransform>();
            pathRect.pivot = new Vector2(0f, 0.5f);
            pathRect.localPosition = Vector3.zero;
            commandTrack = pathObj.GetComponent<Image>();
            commandTrack.sprite = SupportTacticalIcons.DashedLineSprite;
            commandTrack.raycastTarget = false;

            var satObj = new GameObject("Satellite", typeof(RectTransform), typeof(Image));
            satObj.transform.SetParent(commandGroup.transform, false);
            var satRect = satObj.GetComponent<RectTransform>();
            satRect.sizeDelta = new Vector2(24f, 24f);
            satRect.pivot = new Vector2(0.5f, 0.5f);
            commandSatObj = satObj;
            commandSatIcon = satObj.GetComponent<Image>();
            commandSatIcon.sprite = SupportTacticalIcons.SatIcon;
            commandSatIcon.raycastTarget = false;

            var destObj = new GameObject("Destination", typeof(RectTransform));
            destObj.transform.SetParent(commandGroup.transform, false);
            commandDestRect = destObj.GetComponent<RectTransform>();
            commandDestRect.pivot = new Vector2(0.5f, 0.5f);
            commandDestRect.localScale = Vector3.one;

            var fillObj = new GameObject("FootprintFill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(destObj.transform, false);
            commandDestFill = fillObj.GetComponent<Image>();
            commandDestFill.sprite = SupportTacticalIcons.CoverageDiscSprite;
            commandDestFill.raycastTarget = false;

            var ringObj = new GameObject("FootprintRing", typeof(RectTransform), typeof(Image));
            ringObj.transform.SetParent(destObj.transform, false);
            commandDestRing = ringObj.GetComponent<Image>();
            commandDestRing.sprite = SupportTacticalIcons.RingSprite;
            commandDestRing.raycastTarget = false;

            commandCenterObj = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
            commandCenterObj.transform.SetParent(destObj.transform, false);
            var centerRect = commandCenterObj.GetComponent<RectTransform>();
            centerRect.sizeDelta = new Vector2(30f, 30f);
            centerRect.pivot = new Vector2(0.5f, 0.5f);
            var crosshair = commandCenterObj.GetComponent<Image>();
            crosshair.sprite = SupportTacticalIcons.CrosshairSprite;
            crosshair.raycastTarget = false;

            commandCardObj = new GameObject("CommandCard", typeof(RectTransform), typeof(Image));
            commandCardObj.transform.SetParent(destObj.transform, false);
            var cardRect = commandCardObj.GetComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(208f, 46f);
            cardRect.pivot = new Vector2(0f, 1f);
            commandCardBg = commandCardObj.GetComponent<Image>();
            commandCardBg.sprite = SupportTacticalIcons.BadgeBgSprite;
            commandCardBg.type = Image.Type.Sliced;
            commandCardBg.raycastTarget = false;

            var textObj = new GameObject("CommandText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(commandCardObj.transform, false);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-6f, -3f);
            commandCardText = textObj.GetComponent<TextMeshProUGUI>();
            if (uiFont != null) commandCardText.font = uiFont;
            commandCardText.fontSize = 9.5f;
            commandCardText.alignment = TextAlignmentOptions.TopLeft;
            commandCardText.color = Color.white;
            commandCardText.raycastTarget = false;

            commandGroup.SetActive(false);
        }

        private void UpdateCommandReticle(float mapFactor, float invZoom)
        {
            if (commandGroup == null) return;
            bool armed = supportManager != null && supportManager.CommandArmed &&
                         (supportManager.ArmedCommand == OpsCommand.Move ||
                          supportManager.ArmedCommand == OpsCommand.Launch);
            if (!armed || !dynamicMap.TryGetCursorCoordinates(out GlobalPosition cursor))
            {
                if (commandGroup.activeSelf) commandGroup.SetActive(false);
                return;
            }

            Constellation constellation = supportManager.LocalConstellation;
            if (constellation == null)
            {
                if (commandGroup.activeSelf) commandGroup.SetActive(false);
                return;
            }

            Satellite satellite = null;
            SatelliteRole role;
            float swath;
            if (supportManager.ArmedCommand == OpsCommand.Move)
            {
                satellite = constellation.Find(supportManager.ArmedCommandArg);
                if (satellite == null)
                {
                    if (commandGroup.activeSelf) commandGroup.SetActive(false);
                    return;
                }
                role = satellite.Role;
                swath = satellite.Orbit.Swath;
            }
            else
            {
                byte altitude = supportManager.ArmedCommandArg;
                if (altitude >= Constellation.AltitudeCount)
                {
                    if (commandGroup.activeSelf) commandGroup.SetActive(false);
                    return;
                }
                role = (SatelliteRole)Mathf.Clamp(supportManager.ArmedCommandArg2, 0, 2);
                swath = Constellation.Altitude(altitude).Swath;
            }

            if (!commandGroup.activeSelf) commandGroup.SetActive(true);

            Color roleColor = RoleColorFor(role);
            float destX = cursor.x * mapFactor;
            float destZ = cursor.z * mapFactor;

            if (satellite != null)
            {
                constellation.Position(satellite, out float px, out float pz);
                float fromX = px * mapFactor;
                float fromZ = pz * mapFactor;
                float length = Mathf.Sqrt((destX - fromX) * (destX - fromX) + (destZ - fromZ) * (destZ - fromZ));
                commandTrack.enabled = true;
                commandTrack.rectTransform.sizeDelta = new Vector2(length, 3f);
                commandTrack.rectTransform.localPosition = new Vector3(fromX, fromZ, 0f);
                commandTrack.rectTransform.localRotation =
                    Quaternion.Euler(0f, 0f, Mathf.Atan2(destZ - fromZ, destX - fromX) * Mathf.Rad2Deg);
                commandTrack.color = new Color(roleColor.r, roleColor.g, roleColor.b, 0.85f);
                commandSatObj.SetActive(true);
                commandSatObj.transform.localPosition = new Vector3(fromX, fromZ, 0f);
                commandSatObj.transform.localScale = Vector3.one * invZoom;
                commandSatIcon.color = RoleColorFor(satellite.Role);

                float cost = Constellation.TransferCost(px, pz, cursor.x, cursor.z, satellite.Altitude);
                bool enough = satellite.Fuel + 0.01f >= cost;
                commandCardText.text = "<b>STATION TRANSFER · " +
                    SatelliteNaming.Callsign(satellite.Role, satellite.Id) + "</b>\n" +
                    (length / mapFactor / 1000f).ToString("0.0") + " km · " + cost.ToString("0") + "% FUEL" +
                    (enough ? "" : "  ·  <color=#FFAA44>INSUFFICIENT</color>");
            }
            else
            {
                commandTrack.enabled = false;
                commandSatObj.SetActive(false);
                commandCardText.text = "<b>DEPLOY " + RoleNameFor(role) + "</b>\n" +
                    "SWATH " + (swath / 1000f).ToString("0.0") + " km · HOLDS THIS STATION";
            }

            commandDestRect.localPosition = new Vector3(destX, destZ, 0f);
            float swathDiameter = swath * 2f * mapFactor;
            Vector2 diameter = new Vector2(swathDiameter, swathDiameter);
            commandDestFill.rectTransform.sizeDelta = diameter;
            commandDestRing.rectTransform.sizeDelta = diameter;
            commandDestFill.color = new Color(roleColor.r, roleColor.g, roleColor.b, 0.22f);
            commandDestRing.color = roleColor;

            commandCenterObj.transform.localScale = Vector3.one * invZoom;
            commandCardObj.transform.localScale = Vector3.one * invZoom;
            commandCardObj.transform.localPosition = new Vector3(26f * invZoom, -20f * invZoom, 0f);
            commandCardBg.color = new Color(0.025f, 0.05f, 0.06f, 0.95f);
            commandCardText.color = Color.white;
        }

        private static Color RoleColorFor(SatelliteRole role) =>
            role == SatelliteRole.Recon ? new Color(0.35f, 0.95f, 0.6f)
            : role == SatelliteRole.Strike ? new Color(1f, 0.4f, 0.3f)
            : new Color(1f, 0.78f, 0.25f);

        private static string RoleNameFor(SatelliteRole role) =>
            role == SatelliteRole.Recon ? "RECON" : role == SatelliteRole.Strike ? "STRIKE" : "EW";

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
                    RootRect = rootRect,
                    Core = CreateCore(markerObj.transform),
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

        private void BuildConstellationOverlay()
        {
            for (int i = 0; i < 4; i++)
            {
                var markerObj = new GameObject("SatelliteMarker_" + i, typeof(RectTransform));
                markerObj.transform.SetParent(overlayRoot.transform, false);
                var rootRect = markerObj.GetComponent<RectTransform>();
                rootRect.pivot = new Vector2(0.5f, 0.5f);

                var pathObj = new GameObject("TransferPath", typeof(RectTransform), typeof(Image));
                pathObj.transform.SetParent(markerObj.transform, false);
                var pathRect = pathObj.GetComponent<RectTransform>();
                pathRect.pivot = new Vector2(0f, 0.5f);
                var path = pathObj.GetComponent<Image>();
                path.sprite = SupportTacticalIcons.DashedLineSprite;
                path.type = Image.Type.Simple;
                path.raycastTarget = false;

                var swathObj = new GameObject("Swath", typeof(RectTransform), typeof(Image));
                swathObj.transform.SetParent(markerObj.transform, false);
                var swath = swathObj.GetComponent<Image>();
                swath.sprite = SupportTacticalIcons.CoverageDiscSprite;
                swath.raycastTarget = false;

                var ringObj = new GameObject("SwathRing", typeof(RectTransform), typeof(Image));
                ringObj.transform.SetParent(markerObj.transform, false);
                var ring = ringObj.GetComponent<Image>();
                ring.sprite = SupportTacticalIcons.RingSprite;
                ring.raycastTarget = false;

                var destObj = new GameObject("Destination", typeof(RectTransform));
                destObj.transform.SetParent(markerObj.transform, false);
                var destRect = destObj.GetComponent<RectTransform>();
                destRect.pivot = new Vector2(0.5f, 0.5f);
                var destImage = destObj.AddComponent<Image>();
                destImage.sprite = SupportTacticalIcons.CrosshairSprite;
                destImage.raycastTarget = false;

                var iconObj = new GameObject("SatelliteIcon", typeof(RectTransform), typeof(Image));
                iconObj.transform.SetParent(markerObj.transform, false);
                var iconRect = iconObj.GetComponent<RectTransform>();
                iconRect.sizeDelta = new Vector2(22f, 22f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                var icon = iconObj.GetComponent<Image>();
                icon.sprite = SupportTacticalIcons.SatIcon;
                icon.raycastTarget = false;

                var badgeObj = new GameObject("SatelliteBadge", typeof(RectTransform), typeof(Image));
                badgeObj.transform.SetParent(markerObj.transform, false);
                var badgeRect = badgeObj.GetComponent<RectTransform>();
                badgeRect.sizeDelta = new Vector2(126f, 20f);
                badgeRect.pivot = new Vector2(0.5f, 1f);
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
                badgeText.fontSize = 8.5f;
                badgeText.alignment = TextAlignmentOptions.Center;
                badgeText.color = Color.white;
                badgeText.raycastTarget = false;

                markerObj.SetActive(false);
                satellitePool.Add(new SatelliteMarker
                {
                    Root = markerObj,
                    RootRect = rootRect,
                    Swath = swath,
                    Ring = ring,
                    IconObj = iconObj,
                    Icon = icon,
                    BadgeObj = badgeObj,
                    BadgeBg = badgeBg,
                    BadgeText = badgeText,
                    PathObj = pathObj,
                    Path = path,
                    DestObj = destObj,
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

            float maxRange = action == Runtime.SupportActionId.Recon
                ? (settings != null ? settings.ReconRange.Value : 120000f)
                : (settings != null ? settings.MaximumRange.Value : 30000f);
            bool ranged = action == Runtime.SupportActionId.Recon ||
                          action == Runtime.SupportActionId.Artillery ||
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
                SatelliteRole? needed = SupportManager.CoverageRole(action);
                string coverage = !needed.HasValue ? string.Empty
                    : supportManager.CoverageNow(action, cursorCoord.x, cursorCoord.z)
                        ? "<color=#66FF99>SATELLITE COVERAGE CONFIRMED</color>\n"
                        : "<color=#FFAA44>NO COVERAGE — MOVE A SATELLITE IN OPS/SPACE</color>\n";
                string area = radius <= 0f ? "FLEET-WIDE"
                    : areaLabel + ": " + radiusStr;
                armedCardText.text = coverage + $"<b>[{actionCode}] {actionName}</b>\n" +
                                     $"DIST: {rangeStr} · {area}\n" +
                                     $"<color=#88DDFF>RIGHT-CLICK TO CONFIRM</color>";
                if (needed.HasValue && !supportManager.CoverageNow(action, cursorCoord.x, cursorCoord.z))
                {
                    armedRingImage.color = new Color(1f, 0.68f, 0.2f, 0.85f);
                    armedFillImage.color = new Color(1f, 0.68f, 0.2f, 0.06f);
                }
            }
        }

        private void UpdateConstellation(float mapFactor, float invZoom)
        {
            Constellation constellation = supportManager != null ? supportManager.LocalConstellation : null;
            int selectedId = supportManager != null ? supportManager.SelectedSatelliteId : 0;
            int count = constellation != null ? constellation.Satellites.Count : 0;

            for (int i = 0; i < satellitePool.Count; i++)
            {
                SatelliteMarker marker = satellitePool[i];
                Satellite satellite = i < count ? constellation.Satellites[i] : null;
                if (satellite == null)
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

                bool isSelected = satellite.Id == selectedId;
                Color role = RoleColorFor(satellite.Role);
                constellation.Position(satellite, out float px, out float pz);
                marker.RootRect.localPosition = new Vector3(px * mapFactor, pz * mapFactor, 0f);

                // Only the focused satellite shows its coverage; the map stays readable.
                float swath = satellite.Orbit.Swath;
                float diameter = swath * 2f * mapFactor;
                Vector2 swathSize = new Vector2(diameter, diameter);
                marker.Swath.rectTransform.sizeDelta = swathSize;
                marker.Ring.rectTransform.sizeDelta = swathSize;
                marker.Swath.color = new Color(role.r, role.g, role.b, isSelected ? 0.55f : 0f);
                marker.Ring.color = new Color(role.r, role.g, role.b, isSelected ? 0.75f : 0f);
                marker.Swath.enabled = isSelected;
                marker.Ring.enabled = isSelected;

                marker.Icon.color = role;
                marker.IconObj.transform.localScale = Vector3.one * invZoom;
                marker.BadgeObj.SetActive(isSelected);
                if (isSelected)
                {
                    marker.BadgeObj.transform.localScale = Vector3.one * invZoom;
                    marker.BadgeObj.transform.localPosition = new Vector3(0f, -20f * invZoom, 0f);
                    marker.BadgeBg.color = new Color(0.025f, 0.05f, 0.06f, 0.95f);
                    marker.BadgeText.text = "<b>" + SatelliteNaming.Callsign(satellite.Role, satellite.Id) + "</b> " +
                        satellite.Orbit.Name + " · " + Mathf.RoundToInt(satellite.Fuel) + "%";
                }

                if (isSelected && satellite.State == SatelliteState.Transit)
                {
                    float ox = satellite.OriginX * mapFactor;
                    float oz = satellite.OriginZ * mapFactor;
                    float dx = satellite.StationX * mapFactor;
                    float dz = satellite.StationZ * mapFactor;
                    float length = Mathf.Sqrt((dx - ox) * (dx - ox) + (dz - oz) * (dz - oz));
                    marker.PathObj.SetActive(true);
                    marker.Path.rectTransform.sizeDelta = new Vector2(length, 3f);
                    marker.Path.rectTransform.localPosition = new Vector3(ox, oz, 0f);
                    marker.Path.rectTransform.localRotation =
                        Quaternion.Euler(0f, 0f, Mathf.Atan2(dz - oz, dx - ox) * Mathf.Rad2Deg);
                    marker.Path.color = new Color(role.r, role.g, role.b, 0.85f);

                    marker.DestObj.SetActive(true);
                    marker.DestObj.transform.localPosition = new Vector3(dx, dz, 0f);
                    marker.DestObj.transform.localScale = Vector3.one * invZoom;
                    marker.DestObj.GetComponent<Image>().color = role;
                }
                else
                {
                    marker.PathObj.SetActive(false);
                    marker.DestObj.SetActive(false);
                }
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
                        string phase = strike.ActionId == SupportActionId.Recon ? "SCAN COMPLETE" :
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
                case Runtime.SupportActionId.HackPing: return "PNG";
                case Runtime.SupportActionId.HackTrack: return "TRK";
                case Runtime.SupportActionId.HackBlackout: return "C2B";
                case Runtime.SupportActionId.HackGhost: return "GST";
                case Runtime.SupportActionId.HackSpoof: return "SPF";
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
                case Runtime.SupportActionId.HackPing: return "PING SWEEP";
                case Runtime.SupportActionId.HackTrack: return "TRACK UPLINK";
                case Runtime.SupportActionId.HackBlackout: return "RADAR BLACKOUT";
                case Runtime.SupportActionId.HackGhost: return "GHOST SHIELD";
                case Runtime.SupportActionId.HackSpoof: return "SPOOF CONTACTS";
                default: return "SUPPORT CALL-IN";
            }
        }
    }
}
