using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE: an orbital tasking console. A fleet-strength readout and role-composition bar
    /// sit above a live orbital display (station-keeping footprints, transfer tracks, a
    /// rotating sweep and a data-pulse riding each transfer), then a 2x2 grid of compact fleet
    /// cards, spacecraft control, and the launch pad. Skeleton is <see cref="AvBox"/>-arranged,
    /// matching the idiom <see cref="SupportPanel.Cyber"/> already uses; the orbital plot's own
    /// 2D coordinate math is unchanged manual arithmetic, since it is a radar display, not a
    /// flex layout.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float SpaceDisplayMin = 160f;
        private const float SpaceDisplayMax = 280f;
        private const float SpaceDisplayGap = 4f;
        private const float SpaceRoomThreshold = 520f;

        private const float SpaceHeaderHeight = 24f;
        private const float SpaceLogHeight = 38f;
        private const float SpaceRosterTitleHeight = 18f;
        private const float FleetCardHeight = 60f;
        private const float FleetCardGap = 6f;
        private const float SpaceRosterHeight = FleetCardHeight * 2f + FleetCardGap;
        private const float SpaceControlTitleHeight = 18f;
        private const float SpaceControlHeight = 60f;
        private const float SpaceLaunchTitleHeight = 18f;
        private const float SpaceLaunchHeight = 104f;

        /// <summary>Everything but the orbital display: header, log, roster, control, launch.</summary>
        private const float SpaceFixedHeight = SpaceHeaderHeight + SpaceLogHeight + SpaceRosterTitleHeight +
            SpaceRosterHeight + SpaceControlTitleHeight + SpaceControlHeight +
            SpaceLaunchTitleHeight + SpaceLaunchHeight;

        private const float SweepDegPerSec = 45f;

        private sealed class FleetCard
        {
            public Image Background;
            public Image Rail;
            public TMP_Text Callsign;
            public TMP_Text Role;
            public TMP_Text Shell;
            public TMP_Text State;
            public Image FuelFill;
            public AvButton Hit;
        }

        private sealed class PlotPlatform
        {
            public GameObject Root;
            public Image Footprint;
            public Image Ring;
            public Image Track;
            public Image Destination;
            public Image Pulse;
            public Image Glyph;
            public Image Selection;
            public TMP_Text Tag;
        }

        private readonly FleetCard[] fleetCards = new FleetCard[SpaceOperations.MaximumSatellites];
        private readonly PlotPlatform[] plotPlatforms = new PlotPlatform[SpaceOperations.MaximumSatellites];
        private readonly List<string> fleetEvents = new List<string>(2);
        private readonly Dictionary<byte, SatelliteState> lastStates = new Dictionary<byte, SatelliteState>(4);
        private readonly Dictionary<byte, string> lastCallsigns = new Dictionary<byte, string>(4);
        private readonly List<byte> absentSatellites = new List<byte>(4);

        private Rect plotArea;
        private Rect plotBoundsRect;
        private Image plotCursor;
        private Image plotRange;
        private Image sweepImage;
        private bool plotBuilt;

        private TMP_Text onlineChip;
        private Image onlineChipBg;
        private Image[] fleetBarSegments;
        private float fleetBarX, fleetBarTrackWidth;

        private TMP_Text telemetryCursor;
        private TMP_Text telemetryEvent;
        private TMP_Text telemetryEvent2;

        private TMP_Text platformTitle;
        private TMP_Text platformStation, platformShell, platformRange, platformFuel;
        private Image platformFuelBar;
        private AvButton transferButton, deorbitButton;
        private TMP_Text launchStatus;
        private TMP_Text launchPreview;
        private AvButton[] payloadButtons;
        private AvButton[] shellButtons;
        private AvButton launchButton;

        private int selectedSatellite;
        private SatelliteRole launchRole = SatelliteRole.Recon;
        private byte launchAltitude = 1;
        private float nextSpacePoll;
        private float recallConfirmUntil;

        private void ResetSpacePage()
        {
            for (int i = 0; i < fleetCards.Length; i++)
            {
                fleetCards[i] = null;
                plotPlatforms[i] = null;
            }
            fleetEvents.Clear();
            lastStates.Clear();
            lastCallsigns.Clear();
            absentSatellites.Clear();

            plotArea = default;
            plotBoundsRect = default;
            plotCursor = null;
            plotRange = null;
            sweepImage = null;
            plotBuilt = false;

            onlineChip = null;
            onlineChipBg = null;
            fleetBarSegments = null;
            telemetryCursor = null;
            telemetryEvent = null;
            telemetryEvent2 = null;
            platformTitle = null;
            platformStation = platformShell = platformRange = platformFuel = null;
            platformFuelBar = null;
            transferButton = null;
            deorbitButton = null;
            launchStatus = null;
            launchPreview = null;
            payloadButtons = null;
            shellButtons = null;
            launchButton = null;
            selectedSatellite = 0;
            launchRole = SatelliteRole.Recon;
            launchAltitude = 1;
            nextSpacePoll = 0f;
            recallConfirmUntil = 0f;
        }

        private void BuildSpacePage(RectTransform parent, Rect body)
        {
            bool roomy = body.height >= SpaceRoomThreshold;
            float display = roomy
                ? Mathf.Clamp(body.height - SpaceFixedHeight - SpaceDisplayGap, SpaceDisplayMin, SpaceDisplayMax)
                : 0f;

            AvNode roster = AvBox.Grid("roster", 2).Height(SpaceRosterHeight).Gaps(FleetCardGap);
            for (int i = 0; i < fleetCards.Length; i++)
                roster.Add(AvBox.Cell("card" + i).Height(FleetCardHeight));

            AvNode page = AvBox.Column("space").Gaps(0f)
                .Add(AvBox.Cell("header").Height(SpaceHeaderHeight));
            if (roomy) page.Add(AvBox.Cell("display").Height(display));
            page.Add(AvBox.Cell("log").Height(SpaceLogHeight))
                .Add(AvBox.Cell("rosterTitle").Height(SpaceRosterTitleHeight))
                .Add(roster)
                .Add(AvBox.Cell("controlTitle").Height(SpaceControlTitleHeight))
                .Add(AvBox.Cell("control").Height(SpaceControlHeight))
                .Add(AvBox.Cell("launchTitle").Height(SpaceLaunchTitleHeight))
                .Add(AvBox.Cell("launch").Height(SpaceLaunchHeight))
                .Add(AvBox.Filler());

            float contentHeight = SpaceFixedHeight + (roomy ? display + SpaceDisplayGap : 0f);
            page.Arrange(body);
            parent = AvScreen.Scroll(parent, body, contentHeight, out body);
            page.Arrange(body);

            AddScanlineOverlay(parent, body);
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            BuildSpaceHeader(parent, page.At("header"));

            if (roomy)
            {
                AddCardGlow(parent, page.At("display"), AvTheme.RailInfo);
                BuildOrbitalDisplay(parent, page.At("display"));
            }
            else
                plotBuilt = false;

            BuildTelemetryLog(parent, page.At("log"));

            DrawBandTitle(parent, page.At("rosterTitle"), "01 / CONSTELLATION", "SELECT A PLATFORM TO COMMAND");
            for (int i = 0; i < fleetCards.Length; i++)
                BuildFleetCard(parent, page.At("roster.card" + i), i);

            platformTitle = DrawBandTitle(parent, page.At("controlTitle"), "02 / SPACECRAFT CONTROL", "");
            Rect control = page.At("control");
            BuildPlatformStats(parent, control.x + SpineInset, control.y, control.width - SpineInset);
            BuildPlatformButtons(parent, control.x + SpineInset, control.y - 32f, control.width - SpineInset);

            launchStatus = DrawBandTitle(parent, page.At("launchTitle"), "03 / LAUNCH PAD", "");
            Rect launch = page.At("launch");
            BuildLaunchPad(parent, launch.x + SpineInset, launch.y, launch.width - SpineInset);
        }

        private void BuildSpaceHeader(RectTransform parent, Rect area)
        {
            AvKit.Label(parent, "SPACE WARFARE", new Rect(area.x, area.y, area.width * 0.55f, 16f),
                AvTheme.TextPrimary, AvTokens.FontTitle, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

            onlineChipBg = AvKit.Panel(parent, new Rect(area.x + area.width - 96f, area.y + 1f, 96f, 15f),
                AvTheme.SurfaceInert, AvSprites.Control);
            onlineChip = AvStyled.Label(parent, new Rect(area.x + area.width - 96f, area.y + 1f, 96f, 15f),
                "0/4 ONLINE", "row-sub", align: TextAlignmentOptions.Center);

            float barY = area.y - 18f;
            fleetBarX = area.x;
            fleetBarTrackWidth = area.width;
            AvKit.Panel(parent, new Rect(area.x, barY, area.width, 6f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.18f)));
            fleetBarSegments = new Image[3];
            for (int i = 0; i < fleetBarSegments.Length; i++)
                fleetBarSegments[i] = AvKit.Panel(parent, new Rect(area.x, barY, 0f, 6f), Color.clear);
        }

        private void BuildTelemetryLog(RectTransform parent, Rect area)
        {
            telemetryCursor = AvKit.Label(parent, "", new Rect(area.x, area.y - 1f, area.width, 12f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            telemetryEvent = AvKit.Label(parent, "", new Rect(area.x, area.y - 13f, area.width, 12f),
                AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            telemetryEvent2 = AvKit.Label(parent, "", new Rect(area.x, area.y - 25f, area.width, 12f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        }

        // ---- Orbital display -------------------------------------------------------------

        private void BuildOrbitalDisplay(RectTransform parent, Rect area)
        {
            SupportTacticalIcons.EnsureInitialized();

            AvKit.TacticalCard(parent, area, AvTheme.RailInfo);
            AvKit.Label(parent, "ORBITAL DISPLAY", new Rect(area.x + 8f, area.y - 3f, 160f, 12f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            AvKit.Label(parent, "STATION-KEEPING · FOOTPRINTS · TRANSFER",
                new Rect(area.x + 168f, area.y - 3f, area.width - 176f, 12f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            plotArea = new Rect(area.x + 8f, area.y - 19f, area.width - 16f, area.height - 27f);
            plotBuilt = true;

            BuildPlotBounds(parent);

            if (plotBoundsRect.width > 0f && plotBoundsRect.height > 0f)
            {
                // Diameter from the SMALLER bound dimension: since PlotScale already fits
                // plotBoundsRect entirely inside plotArea on both axes, a circle no wider
                // than the tighter dimension can never bleed past the display's own card,
                // even when the theater is a long, thin strip rather than square.
                sweepImage = PlotSprite(parent, "Sweep", SupportTacticalIcons.SweepWedgeSprite);
                PlaceAt(sweepImage,
                    new Vector2(plotBoundsRect.x + plotBoundsRect.width * 0.5f,
                                plotBoundsRect.y - plotBoundsRect.height * 0.5f),
                    Mathf.Min(plotBoundsRect.width, plotBoundsRect.height));
                sweepImage.color = AvTheme.RailInfo;
            }

            for (int i = 0; i < plotPlatforms.Length; i++)
                plotPlatforms[i] = BuildPlotPlatform(parent, i);

            plotRange = PlotSprite(parent, "CursorRange", SupportTacticalIcons.DashedLineSprite);
            plotCursor = PlotSprite(parent, "Cursor", SupportTacticalIcons.CrosshairSprite);
        }

        /// <summary>Rotates the orbital display's sweep wedge. Pure transform mutation, called
        /// every frame from <see cref="Update"/> only while the SPACE tab is on-screen and the
        /// display exists — zero cost on any other tab or on a bay too short for the display.</summary>
        private void UpdateSpaceMotion()
        {
            if (sweepImage == null || shell == null || shell.Page != TabSpace) return;
            sweepImage.rectTransform.Rotate(0f, 0f, -SweepDegPerSec * Time.unscaledDeltaTime);
        }

        private void BuildPlotBounds(RectTransform parent)
        {
            OrbitalBounds.Extents(out float halfWidth, out float halfHeight);
            if (halfWidth <= 0f || halfHeight <= 0f) return;

            float scale = PlotScale(halfWidth, halfHeight);
            float halfW = halfWidth * scale;
            float halfH = halfHeight * scale;
            float centerX = plotArea.x + plotArea.width * 0.5f;
            float centerY = plotArea.y - plotArea.height * 0.5f;
            Rect bounds = new Rect(centerX - halfW, centerY - halfH, halfW * 2f, halfH * 2f);
            plotBoundsRect = bounds;

            AvKit.Panel(parent, bounds, new Color(0.020f, 0.048f, 0.044f, 0.85f));
            for (int i = 1; i < 6; i++)
            {
                float gx = bounds.x + bounds.width * i / 6f;
                float gy = bounds.y + bounds.height * i / 4f;
                AvKit.Rule(parent, new Rect(gx, bounds.y, 1f, bounds.height),
                           AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.22f)));
                AvKit.Rule(parent, new Rect(bounds.x, gy, bounds.width, 1f),
                           AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.16f)));
            }
            AvKit.Outline(parent, bounds, AvTheme.Frame);
            AvKit.CornerTicks(parent, bounds, AvTheme.Hairline, 8f);
        }

        private PlotPlatform BuildPlotPlatform(RectTransform parent, int index)
        {
            var platform = new PlotPlatform();
            var root = new GameObject("PlotPlatform" + index, typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            AvKit.Stretch(rootRect);
            platform.Root = root;

            platform.Footprint = PlotSprite(rootRect, "Footprint", SupportTacticalIcons.CoverageDiscSprite);
            platform.Ring = PlotSprite(rootRect, "FootprintRing", SupportTacticalIcons.RingSprite);
            platform.Track = PlotSprite(rootRect, "TransferTrack", SupportTacticalIcons.DashedLineSprite);
            platform.Destination = PlotSprite(rootRect, "TransferTarget", SupportTacticalIcons.CrosshairSprite);
            platform.Pulse = PlotSprite(rootRect, "DataPulse", SupportTacticalIcons.DataPulseSprite);
            platform.Glyph = PlotSprite(rootRect, "Satellite", SupportTacticalIcons.SatIcon);
            platform.Selection = PlotSprite(rootRect, "Selection", SupportTacticalIcons.DottedRingSprite);
            platform.Tag = AvKit.Label(rootRect, "", new Rect(0f, 0f, 96f, 12f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            root.SetActive(false);
            return platform;
        }

        private static Image PlotSprite(RectTransform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        private float PlotScale(float halfWidth, float halfHeight) =>
            Mathf.Min(plotArea.width / (2f * Mathf.Max(1f, halfWidth)),
                      plotArea.height / (2f * Mathf.Max(1f, halfHeight))) * 0.90f;

        private Vector2 PlotPoint(float worldX, float worldZ, float scale) =>
            new Vector2(plotArea.x + plotArea.width * 0.5f + worldX * scale,
                        plotArea.y - plotArea.height * 0.5f - worldZ * scale);

        private static void PlaceAt(Image image, Vector2 point, float size)
        {
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = point;
            rect.sizeDelta = new Vector2(size, size);
        }

        private static void PlaceLine(Image image, Vector2 from, Vector2 to, Color color)
        {
            Vector2 delta = to - from;
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = (from + to) * 0.5f;
            rect.sizeDelta = new Vector2(Mathf.Max(1f, delta.magnitude), 2f);
            rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg);
            image.color = color;
        }

        private void RefreshPlot(Constellation constellation, int count, bool hasCursor,
                                 float cursorX, float cursorZ)
        {
            if (!plotBuilt) return;

            OrbitalBounds.Extents(out float halfWidth, out float halfHeight);
            if (halfWidth <= 0f || halfHeight <= 0f)
            {
                halfWidth = halfHeight = Mathf.Max(1f, constellation != null ? constellation.MapRadius : 1f);
            }
            float scale = PlotScale(halfWidth, halfHeight);

            for (int i = 0; i < plotPlatforms.Length; i++)
            {
                PlotPlatform platform = plotPlatforms[i];
                if (platform == null) continue;

                Satellite satellite = constellation != null && i < count
                    ? constellation.Satellites[i] : null;
                platform.Root.SetActive(satellite != null);
                if (satellite == null) continue;

                Color role = RoleColor(satellite.Role);
                bool selected = satellite.Id == selectedSatellite;
                constellation.Position(satellite, out float px, out float pz);
                Vector2 point = PlotPoint(px, pz, scale);
                bool transit = satellite.State == SatelliteState.Transit;

                if (transit)
                {
                    Vector2 origin = PlotPoint(satellite.OriginX, satellite.OriginZ, scale);
                    Vector2 destination = PlotPoint(satellite.StationX, satellite.StationZ, scale);
                    PlaceLine(platform.Track, origin, destination, role.WithAlpha(0.55f));
                    platform.Track.gameObject.SetActive(true);
                    PlaceAt(platform.Destination, destination, 14f);
                    platform.Destination.color = role;
                    platform.Destination.gameObject.SetActive(true);
                    PlaceAt(platform.Footprint, destination, satellite.Orbit.Swath * 2f * scale);
                    platform.Footprint.color = role.WithAlpha(0.10f);
                    PlaceAt(platform.Ring, destination, satellite.Orbit.Swath * 2f * scale);
                    platform.Ring.color = role.WithAlpha(selected ? 0.6f : 0.35f);

                    float progress = satellite.TransitTotal > 0f
                        ? Mathf.Clamp01(1f - satellite.TransitLeft / satellite.TransitTotal) : 0f;
                    PlaceAt(platform.Pulse, Vector2.Lerp(origin, destination, progress), 10f);
                    platform.Pulse.color = role;
                    platform.Pulse.gameObject.SetActive(true);
                }
                else
                {
                    platform.Track.gameObject.SetActive(false);
                    platform.Destination.gameObject.SetActive(false);
                    platform.Pulse.gameObject.SetActive(false);
                    PlaceAt(platform.Footprint, point, satellite.Orbit.Swath * 2f * scale);
                    platform.Footprint.color = role.WithAlpha(selected ? 0.16f : 0.10f);
                    PlaceAt(platform.Ring, point, satellite.Orbit.Swath * 2f * scale);
                    platform.Ring.color = role.WithAlpha(selected ? 0.6f : 0.35f);
                }

                PlaceAt(platform.Glyph, point, 16f);
                platform.Glyph.color = selected ? AvTheme.TextPrimary : role;
                PlaceAt(platform.Selection, point, 26f);
                platform.Selection.color =
                    AvTheme.RailReady.WithAlpha(0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f));
                platform.Selection.gameObject.SetActive(selected);
                float tagX = point.x + 10f;
                if (tagX + 96f > plotArea.x + plotArea.width)
                    tagX = Mathf.Max(plotArea.x, point.x - 106f);
                AvKit.Place(platform.Tag.rectTransform,
                    new Rect(tagX, point.y - 7f, 96f, 12f));
                platform.Tag.text = SatelliteNaming.Callsign(satellite.Role, satellite.Id);
                platform.Tag.color = role;
            }

            if (plotCursor != null)
            {
                plotCursor.gameObject.SetActive(hasCursor);
                if (hasCursor)
                {
                    PlaceAt(plotCursor, PlotPoint(cursorX, cursorZ, scale), 16f);
                    plotCursor.color = AvTheme.TextPrimary;
                }
            }

            Satellite active = selectedSatellite != 0 && constellation != null
                ? constellation.Find((byte)selectedSatellite) : null;
            bool rangeVisible = plotRange != null && active != null && hasCursor;
            if (plotRange != null)
            {
                plotRange.gameObject.SetActive(rangeVisible);
                if (rangeVisible)
                {
                    constellation.Position(active, out float ax, out float az);
                    PlaceLine(plotRange, PlotPoint(ax, az, scale),
                              PlotPoint(cursorX, cursorZ, scale), AvTheme.RailInfo.WithAlpha(0.45f));
                }
            }
        }

        // ---- Constellation roster ----------------------------------------------------------

        private void BuildFleetCard(RectTransform parent, Rect area, int index)
        {
            var card = new FleetCard();
            (Image cardFill, Image rail) = AvKit.TacticalCard(parent, area, AvTheme.RailInert);
            card.Background = cardFill;
            card.Rail = rail;

            float x = area.x + 8f;
            float width = area.width - 16f;
            float y = area.y - 6f;

            card.Callsign = AvStyled.Label(parent, new Rect(x, y, width * 0.6f, 15f), "—", "row-name");
            card.Role = AvStyled.Label(parent, new Rect(x + width * 0.6f, y, width * 0.4f, 15f),
                "", "row-sub", align: TextAlignmentOptions.MidlineRight);
            y -= 16f;

            card.Shell = AvStyled.Label(parent, new Rect(x, y, width, 13f), "", "row-sub");
            y -= 15f;

            card.State = AvStyled.Label(parent, new Rect(x, y, width - 30f, 13f), "", "row-sub");
            card.FuelFill = AvKit.ProgressBar(parent, new Rect(x + width - 26f, y + 1f, 26f, 4f),
                0f, AvTheme.RailReady);

            card.Hit = AvKit.HitButton(parent, area,
                () => { selectedSatellite = SelectedIdFromRow(index); nextRefresh = 0f; });

            fleetCards[index] = card;
        }

        private int SelectedIdFromRow(int index)
        {
            Constellation constellation = support.LocalConstellation;
            if (constellation == null || index >= constellation.Satellites.Count) return 0;
            return constellation.Satellites[index].Id;
        }

        private void RefreshFleetCards(Constellation constellation, int count, int maximum,
                                       bool hasCursor, float cursorX, float cursorZ)
        {
            Color hover = AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(),
                AvTokens.RowHoverScale, AvTokens.RowHoverAlpha));

            for (int i = 0; i < fleetCards.Length; i++)
            {
                FleetCard card = fleetCards[i];
                if (card == null) continue;

                Satellite satellite = constellation != null && i < count
                    ? constellation.Satellites[i] : null;
                bool selected = satellite != null && satellite.Id == selectedSatellite;

                if (satellite == null)
                {
                    card.Rail.color = AvTheme.RailInert;
                    card.Callsign.text = "—";
                    card.Callsign.color = AvTheme.Disabled;
                    card.Role.text = "";
                    card.Shell.text = i < maximum ? "BAY OPEN" : "NOT ENABLED";
                    card.Shell.color = AvTheme.Disabled;
                    card.State.text = "VACANT";
                    card.State.color = AvTheme.Disabled;
                    card.FuelFill.fillAmount = 0f;
                    card.Hit.SetEnabled(false);
                    card.Hit.SetRowHighlight(card.Background, Color.clear, Color.clear);
                    card.Hit.WithTooltip(null);
                    continue;
                }

                Color role = RoleColor(satellite.Role);
                bool transit = satellite.State == SatelliteState.Transit;
                card.Rail.color = transit ? AvTheme.RailCaution : role;
                card.Callsign.text = SatelliteNaming.Callsign(satellite.Role, satellite.Id);
                card.Callsign.color = selected ? AvTheme.TextPrimary : role;
                card.Role.text = RoleName(satellite.Role);
                card.Role.color = role;
                card.Shell.text = "SHELL " + satellite.Orbit.Name.Substring(0, 1);
                card.Shell.color = AvTheme.Dim;

                string state;
                Color stateColor;
                if (transit)
                {
                    state = "T-" + Clock(satellite.TransitLeft);
                    stateColor = AvTheme.RailCaution;
                }
                else if (hasCursor && constellation.SatelliteCovers(satellite, cursorX, cursorZ))
                {
                    state = "COVERS CURSOR";
                    stateColor = AvTheme.RailReady;
                }
                else if (hasCursor)
                {
                    state = CursorDistance(satellite, cursorX, cursorZ).ToUpperInvariant();
                    stateColor = AvTheme.Dim;
                }
                else
                {
                    state = "ON STATION";
                    stateColor = AvTheme.Dim;
                }
                card.State.text = state;
                card.State.color = stateColor;
                card.FuelFill.fillAmount = satellite.Fuel / Constellation.MaximumFuel;
                card.FuelFill.color = satellite.Fuel < 25f ? AvTheme.RailCaution : role;

                card.Hit.SetEnabled(true);
                card.Hit.SetRowHighlight(card.Background, selected
                    ? AvTheme.Unity(AvTokens.Wash(role.ToRgba(), AvTokens.SelectedScale, AvTokens.SelectedAlpha))
                    : Color.clear, hover);
                card.Hit.WithTooltip(card.Callsign.text + " · " + card.Role.text + " · " +
                    satellite.Orbit.Name + " — " + state);
            }
        }

        private void RefreshFleetBar(Constellation constellation, int count, int maximum)
        {
            onlineChip.text = count + "/" + maximum + " ONLINE";
            onlineChip.color = count > 0 ? AvTheme.RailReady : AvTheme.Dim;
            onlineChipBg.color = count > 0
                ? AvTheme.Unity(AvTokens.RailReady.WithAlpha(0.12f))
                : AvTheme.SurfaceInert;

            if (fleetBarSegments == null || maximum <= 0) return;

            int recon = 0, strike = 0, ew = 0;
            if (constellation != null)
            {
                for (int i = 0; i < constellation.Satellites.Count; i++)
                {
                    switch (constellation.Satellites[i].Role)
                    {
                        case SatelliteRole.Recon: recon++; break;
                        case SatelliteRole.Strike: strike++; break;
                        default: ew++; break;
                    }
                }
            }

            float segmentWidth = fleetBarTrackWidth / maximum;
            float x = fleetBarX;
            PlaceFleetSegment(fleetBarSegments[0], ref x, segmentWidth * recon, RoleColor(SatelliteRole.Recon));
            PlaceFleetSegment(fleetBarSegments[1], ref x, segmentWidth * strike, RoleColor(SatelliteRole.Strike));
            PlaceFleetSegment(fleetBarSegments[2], ref x, segmentWidth * ew, RoleColor(SatelliteRole.Ew));
        }

        private static void PlaceFleetSegment(Image image, ref float x, float width, Color color)
        {
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), rect.sizeDelta.y);
            image.color = color;
            x += width;
        }

        // ---- Spacecraft control ------------------------------------------------------------

        private void BuildPlatformStats(RectTransform parent, float x, float y, float width)
        {
            float column = width * 0.55f;
            platformStation = Stat(parent, x, y - 1f, column, "GROUND STATION");
            platformShell = Stat(parent, x + column, y - 1f, width - column, "SHELL · SWATH");
            platformRange = Stat(parent, x, y - 16f, column, "RANGE TO CURSOR");
            platformFuel = Stat(parent, x + column, y - 16f, width - column, "Δv RESERVE");
            platformFuelBar = AvKit.ProgressBar(parent,
                new Rect(x + column, y - 28f, width - column, 3f), 0f, AvTheme.RailReady);
        }

        private void BuildPlatformButtons(RectTransform parent, float x, float y, float width)
        {
            transferButton = AvStyled.Button(parent,
                new Rect(x, y - 1f, width - 118f, 26f),
                "ORBITAL TRANSFER", "btn", () =>
                {
                    int id = selectedSatellite;
                    if (id != 0) support.ArmCommand(OpsCommand.Move, (byte)id, 0, "STATION TRANSFER");
                    nextRefresh = 0f;
                }, AvButtonStyle.Primary)
                .WithTooltip("Commit an orbital burn, then right-click the ground station this platform should hold. Fuel is spent for the distance.");
            deorbitButton = AvStyled.Button(parent,
                new Rect(x + width - 112f, y - 1f, 112f, 26f),
                "DEORBIT", "btn", () =>
                {
                    int id = selectedSatellite;
                    if (id == 0) return;
                    if (Time.unscaledTime < recallConfirmUntil)
                    {
                        recallConfirmUntil = 0f;
                        support.RequestRecall((byte)id);
                    }
                    else
                    {
                        recallConfirmUntil = Time.unscaledTime + 3f;
                    }
                    nextRefresh = 0f;
                }, AvButtonStyle.Danger)
                .WithTooltip("De-orbit this platform and recover the allocation it cost. Click twice to confirm.");
        }

        private void BuildLaunchPad(RectTransform parent, float x, float y, float width)
        {
            const float labelWidth = 46f;
            const float gap = 5f;
            float buttonWidth = (width - labelWidth - gap * 2f) / 3f;

            AvStyled.Label(parent, new Rect(x, y - 1f, labelWidth, 26f), "PAYLOAD", "kv-key",
                align: TextAlignmentOptions.MidlineLeft);
            payloadButtons = new AvButton[3];
            for (int i = 0; i < 3; i++)
            {
                SatelliteRole role = (SatelliteRole)i;
                payloadButtons[i] = AvStyled.Button(parent,
                    new Rect(x + labelWidth + i * (buttonWidth + gap), y - 1f, buttonWidth, 26f),
                    RoleName(role), "toggle",
                    () => { launchRole = role; nextRefresh = 0f; },
                    AvButtonStyle.Toggle);
            }

            float shellY = y - 30f;
            AvStyled.Label(parent, new Rect(x, shellY - 1f, labelWidth, 26f), "SHELL", "kv-key",
                align: TextAlignmentOptions.MidlineLeft);
            shellButtons = new AvButton[Constellation.AltitudeCount];
            for (int i = 0; i < shellButtons.Length; i++)
            {
                SatelliteAltitude option = Constellation.Altitude(i);
                byte index = (byte)i;
                shellButtons[i] = AvStyled.Button(parent,
                    new Rect(x + labelWidth + i * (buttonWidth + gap), shellY - 1f, buttonWidth, 26f),
                    option.Name, "toggle",
                    () => { launchAltitude = index; nextRefresh = 0f; },
                    AvButtonStyle.Toggle);
            }

            float previewY = shellY - 26f;
            launchPreview = AvStyled.Label(parent, new Rect(x, previewY, width, 13f), "", "row-sub");

            launchButton = AvStyled.Button(parent, new Rect(x, previewY - 16f, width, 28f),
                "COMMIT LAUNCH · RIGHT-CLICK GROUND STATION", "btn", () =>
                {
                    support.ArmCommand(OpsCommand.Launch, launchAltitude, (byte)launchRole,
                        "DEPLOY " + RoleName(launchRole) + " PLATFORM");
                    nextRefresh = 0f;
                }, AvButtonStyle.Primary)
                .WithTooltip("Open the map and right-click the ground station this platform should hold. It reaches station after the transfer burn.");
        }

        // ---- Refresh -----------------------------------------------------------------------

        private void RefreshSpace(bool bypass)
        {
            if (onlineChip == null) return;
            if (Time.unscaledTime >= nextSpacePoll)
            {
                nextSpacePoll = Time.unscaledTime + 1f;
                support.PollOps();
            }

            Constellation constellation = support.LocalConstellation;
            int maximum = support.Settings != null ? support.Settings.MaximumSatellites.Value : 4;
            int count = constellation != null ? constellation.Satellites.Count : 0;
            bool hasCursor = TryCursor(out float cursorX, out float cursorZ);

            RefreshFleetBar(constellation, count, maximum);

            Satellite selected = constellation != null ? constellation.Find((byte)selectedSatellite) : null;
            if (selected == null && constellation != null && count > 0)
            {
                selected = constellation.Satellites[0];
                selectedSatellite = selected.Id;
            }
            support.SelectedSatelliteId = selectedSatellite;

            TrackFleetEvents(constellation);
            RefreshFleetCards(constellation, count, maximum, hasCursor, cursorX, cursorZ);
            RefreshTelemetry(constellation, hasCursor, cursorX, cursorZ);

            if (platformTitle != null)
                platformTitle.text = selected == null ? "NO SPACECRAFT SELECTED"
                    : SatelliteNaming.Callsign(selected.Role, selected.Id) +
                      " · " + RoleName(selected.Role);

            RefreshPlatform(selected, constellation, hasCursor, cursorX, cursorZ);
            RefreshPlot(constellation, count, hasCursor, cursorX, cursorZ);
            RefreshLaunchPad(bypass, count, maximum);
        }

        private void TrackFleetEvents(Constellation constellation)
        {
            if (constellation != null)
            {
                for (int i = 0; i < constellation.Satellites.Count; i++)
                {
                    Satellite satellite = constellation.Satellites[i];
                    string callsign = SatelliteNaming.Callsign(satellite.Role, satellite.Id);
                    if (!lastStates.TryGetValue(satellite.Id, out SatelliteState previous))
                    {
                        bool launching = satellite.State == SatelliteState.Transit;
                        AddFleetEvent(callsign + (launching
                            ? " LAUNCH CONFIRMED · TRANSFER BURN"
                            : " ON STATION · TELEMETRY NOMINAL"));
                        if (launching && satellite.TransitTotal > 0f &&
                            SupportTargeting.TryMapPoint(
                                new GlobalPosition(satellite.StationX, 0f, satellite.StationZ), out Vector3 launchPoint))
                        {
                            Visuals.SatelliteLaunchVisuals.Play(launchPoint, satellite.TransitTotal);
                        }
                    }
                    else if (previous != satellite.State)
                    {
                        AddFleetEvent(callsign + (satellite.State == SatelliteState.Transit
                            ? " ORBITAL TRANSFER · BURN COMMITTED"
                            : " TRANSFER COMPLETE · ON STATION"));
                    }
                    lastStates[satellite.Id] = satellite.State;
                    lastCallsigns[satellite.Id] = callsign;
                }

                absentSatellites.Clear();
                foreach (KeyValuePair<byte, string> entry in lastCallsigns)
                    if (constellation.Find(entry.Key) == null) absentSatellites.Add(entry.Key);
                for (int i = 0; i < absentSatellites.Count; i++)
                {
                    AddFleetEvent(lastCallsigns[absentSatellites[i]] + " DEORBIT · ASSETS RECOVERED");
                    lastCallsigns.Remove(absentSatellites[i]);
                    lastStates.Remove(absentSatellites[i]);
                }
            }
            else if (lastCallsigns.Count > 0)
            {
                lastCallsigns.Clear();
                lastStates.Clear();
                fleetEvents.Clear();
            }
        }

        private void AddFleetEvent(string text)
        {
            fleetEvents.Insert(0, text);
            while (fleetEvents.Count > 2) fleetEvents.RemoveAt(fleetEvents.Count - 1);
        }

        private void RefreshTelemetry(Constellation constellation, bool hasCursor,
                                      float cursorX, float cursorZ)
        {
            if (telemetryCursor == null) return;

            if (!hasCursor)
            {
                telemetryCursor.text = "CURSOR OFF-MAP · TARGETS ARE CHOSEN ON THE TACTICAL MAP";
                telemetryCursor.color = AvTheme.Dim;
            }
            else
            {
                telemetryCursor.text =
                    "CURSOR " + (cursorX / 1000f).ToString("0.0") + " / " +
                    (cursorZ / 1000f).ToString("0.0") + " KM · " +
                    "R " + CoverageTag(constellation, SatelliteRole.Recon, cursorX, cursorZ) + " · " +
                    "S " + CoverageTag(constellation, SatelliteRole.Strike, cursorX, cursorZ) + " · " +
                    "E " + CoverageTag(constellation, SatelliteRole.Ew, cursorX, cursorZ);
                telemetryCursor.color = AvTheme.TextPrimary;
            }

            telemetryEvent.text = "» " + (fleetEvents.Count > 0
                ? fleetEvents[0]
                : "STATION-KEEPING COMMAND · ANY GRID, ONE BURN AWAY");
            telemetryEvent.color = fleetEvents.Count > 0 ? AvTheme.RailInfo : AvTheme.Dim;

            telemetryEvent2.text = fleetEvents.Count > 1 ? "» " + fleetEvents[1] : "";
            telemetryEvent2.color = AvTheme.Dim;
        }

        private static string CoverageTag(Constellation constellation, SatelliteRole role,
                                          float cursorX, float cursorZ)
        {
            if (constellation == null) return "—";
            StationCoverage coverage = constellation.Query(role, cursorX, cursorZ);
            if (coverage.Covered)
            {
                Satellite satellite = constellation.Find(coverage.SatelliteId);
                return satellite == null ? "—" : SatelliteNaming.Callsign(satellite.Role, satellite.Id);
            }
            if (!coverage.HasSatellite) return "—";
            return (coverage.NearestGap / 1000f).ToString("0.0") + " KM OUT";
        }

        private void RefreshPlatform(Satellite selected, Constellation constellation,
                                     bool hasCursor, float cursorX, float cursorZ)
        {
            if (platformStation == null) return;

            bool transferArmed = support.CommandArmed && support.ArmedCommand == OpsCommand.Move;
            if (transferButton != null)
            {
                transferButton.SetEnabled(selected != null || transferArmed);
                transferButton.SetLatched(transferArmed);
                transferButton.SetText(transferArmed ? "AWAITING GROUND STATION" : "ORBITAL TRANSFER");
            }
            if (deorbitButton != null)
            {
                bool confirming = Time.unscaledTime < recallConfirmUntil;
                deorbitButton.SetEnabled(selected != null);
                deorbitButton.SetText(confirming ? "CONFIRM DEORBIT" : "DEORBIT");
                deorbitButton.SetLatched(confirming);
            }

            if (selected == null)
            {
                platformStation.text = platformShell.text = platformRange.text = platformFuel.text = "—";
                platformStation.color = platformShell.color = platformRange.color = platformFuel.color = AvTheme.Dim;
                platformFuelBar.fillAmount = 0f;
                return;
            }

            bool transit = selected.State == SatelliteState.Transit;
            platformStation.text = transit
                ? "TRANSFER · T-" + Clock(selected.TransitLeft)
                : "X " + selected.StationX.ToString("0") + " · Z " + selected.StationZ.ToString("0");
            platformStation.color = transit ? AvTheme.RailCaution : AvTheme.TextPrimary;

            platformShell.text = selected.Orbit.Name + " · " +
                (selected.Orbit.Swath / 1000f).ToString("0") + " KM";
            platformShell.color = AvTheme.TextPrimary;

            platformRange.text = hasCursor ? CursorDistance(selected, cursorX, cursorZ).ToUpperInvariant() : "—";
            platformRange.color = hasCursor && constellation.SatelliteCovers(selected, cursorX, cursorZ)
                ? AvTheme.RailReady : AvTheme.TextPrimary;

            platformFuel.text = selected.Fuel.ToString("0") + "%";
            platformFuel.color = selected.Fuel < 25f ? AvTheme.RailCaution : AvTheme.TextPrimary;
            platformFuelBar.fillAmount = selected.Fuel / Constellation.MaximumFuel;
            platformFuelBar.color = selected.Fuel < 25f ? AvTheme.RailCaution : RoleColor(selected.Role);
        }

        private void RefreshLaunchPad(bool bypass, int count, int maximum)
        {
            float cost = support.SatelliteCost(launchRole);
            bool room = count < maximum;
            bool affordable = bypass || support.LocalAllocation + 0.001f >= cost;
            bool launchArmed = support.CommandArmed && support.ArmedCommand == OpsCommand.Launch;

            if (payloadButtons != null)
            {
                for (int i = 0; i < payloadButtons.Length; i++)
                {
                    SatelliteRole role = (SatelliteRole)i;
                    payloadButtons[i].SetLatched(role == launchRole);
                    payloadButtons[i].SetText(RoleName(role) + " " + support.SatelliteCost(role).ToString("N0"));
                    payloadButtons[i].WithTooltip(PayloadDescription(role));
                }
            }
            if (shellButtons != null)
            {
                for (int i = 0; i < shellButtons.Length; i++)
                {
                    SatelliteAltitude option = Constellation.Altitude(i);
                    shellButtons[i].SetLatched(i == launchAltitude);
                    shellButtons[i].SetText(option.Name + " " + (option.Swath / 1000f).ToString("0") + " KM");
                    shellButtons[i].WithTooltip("SHELL " + option.Name + " · swath " +
                        (option.Swath / 1000f).ToString("0") + " km · transfer " +
                        (option.TransitSpeed / 1000f).ToString("0.0") + " km/s · " +
                        option.FuelPerKm.ToString("0.00") + " %Δv/km.");
                }
            }

            if (launchPreview != null)
            {
                SatelliteAltitude option = Constellation.Altitude(launchAltitude);
                launchPreview.text = RoleName(launchRole) + " · " + option.Name + " SHELL · " +
                    (option.Swath / 1000f).ToString("0") + " KM SWATH · " +
                    (option.TransitSpeed / 1000f).ToString("0.0") + " KM/S · " +
                    option.FuelPerKm.ToString("0.00") + " %Δv/KM";
                launchPreview.color = AvTheme.Dim;
            }

            if (launchStatus != null)
            {
                int open = Math.Max(0, maximum - count);
                launchStatus.text = launchArmed ? "AWAITING GROUND STATION · RIGHT-CLICK MAP"
                    : !room ? "NO FREE BAY · DEORBIT ONE FIRST"
                    : !affordable ? "INSUFFICIENT ALLOCATION · " + cost.ToString("N0") + " REQUIRED"
                    : open + (open == 1 ? " BAY OPEN · " : " BAYS OPEN · ") + cost.ToString("N0") +
                      " ALLOC · TRANSIT " +
                      (Constellation.Altitude(launchAltitude).TransitSpeed / 1000f).ToString("0.0") + " KM/S";
                launchStatus.color = launchArmed ? AvTheme.RailCaution
                    : room && affordable ? AvTheme.RailReady : AvTheme.Warning;
            }
            if (launchButton != null)
            {
                launchButton.SetLatched(launchArmed);
                launchButton.SetText(launchArmed
                    ? "AWAITING GROUND STATION · CLICK MAP"
                    : "COMMIT LAUNCH · RIGHT-CLICK GROUND STATION");
                launchButton.SetEnabled(launchArmed ||
                    (room && affordable && !support.RequestPending && !support.CommandPending));
            }
        }

        private static string CursorDistance(Satellite satellite, float cursorX, float cursorZ)
        {
            float dx = cursorX - satellite.StationX, dz = cursorZ - satellite.StationZ;
            return ((float)Math.Sqrt(dx * dx + dz * dz) / 1000f).ToString("0.0") + " km";
        }

        private static string Clock(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        private static Color RoleColor(SatelliteRole role) =>
            role == SatelliteRole.Recon ? new Color(0.35f, 0.95f, 0.60f)
            : role == SatelliteRole.Strike ? new Color(1.00f, 0.40f, 0.30f)
            : new Color(1.00f, 0.78f, 0.25f);

        private static string RoleName(SatelliteRole role) => SatelliteNaming.RoleTag(role);

        private static string PayloadDescription(SatelliteRole role)
        {
            switch (role)
            {
                case SatelliteRole.Recon:
                    return "ISR payload · satellite scan coverage. Needs a RECON platform overhead at task time.";
                case SatelliteRole.Strike:
                    return "Kinetic payload · Rod from God coverage. Needs a STRIKE platform overhead at task time.";
                default:
                    return "Electronic-warfare payload · high-altitude EMP airburst. Needs an EW platform overhead at task time; the geomagnetic shock is indiscriminate.";
            }
        }
    }
}
