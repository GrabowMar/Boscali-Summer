using System;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    internal sealed partial class SupportPanel
    {
        private sealed class SpaceRow
        {
            public GameObject Root;
            public Image Rail;
            public TMP_Text Name;
            public TMP_Text Status;
            public AvButton Select;
        }

        private sealed class CoverageRow
        {
            public Image Rail;
            public TMP_Text Asset;
            public TMP_Text Status;
        }

        private OrbitPlot plot;
        private Rect schematicRect;
        private readonly TMP_Text[] schematicNames = new TMP_Text[SpaceOperations.MaximumSatellites];
        private readonly TMP_Text[] schematicDetails = new TMP_Text[SpaceOperations.MaximumSatellites];
        private readonly SpaceRow[] spaceRows = new SpaceRow[SpaceOperations.MaximumSatellites];
        private readonly CoverageRow[] coverageRows = new CoverageRow[3];

        private TMP_Text onlineChip;
        private Image onlineChipBg;

        private TMP_Text platformTitle, platformStation, platformSwath, platformDist, platformFuel;
        private Image platformFuelBar;
        private AvButton moveSatelliteButton, recallSatelliteButton;
        private TMP_Text deployStatus;
        private AvButton[] launchRoleButtons;
        private AvButton[] altitudeButtons;
        private AvButton launchButton;

        private int selectedSatellite;
        private SatelliteRole launchRole = SatelliteRole.Recon;
        private byte launchAltitude = 1;
        private float nextSpacePoll;
        private float nextPlotAt;
        private float recallConfirmUntil;

        private void ResetSpacePage()
        {
            plot?.Destroy();
            plot = null;
            for (int i = 0; i < schematicNames.Length; i++)
            {
                schematicNames[i] = null;
                schematicDetails[i] = null;
                spaceRows[i] = null;
            }
            for (int i = 0; i < coverageRows.Length; i++) coverageRows[i] = null;
            onlineChip = null;
            onlineChipBg = null;
            platformTitle = null;
            platformStation = platformSwath = platformDist = platformFuel = null;
            platformFuelBar = null;
            moveSatelliteButton = null;
            recallSatelliteButton = null;
            deployStatus = null;
            launchRoleButtons = null;
            altitudeButtons = null;
            launchButton = null;
            selectedSatellite = 0;
            launchRole = SatelliteRole.Recon;
            launchAltitude = 1;
            nextSpacePoll = 0f;
            nextPlotAt = 0f;
            recallConfirmUntil = 0f;
        }

        private void BuildSpacePage(RectTransform parent, Rect body)
        {
            AvNode page = AvBox.Column("space").Gaps(0f)
                .Add(AvBox.Cell("hero").Height(48f))
                .Add(AvBox.Cell("schematic").Height(186f))
                .Add(AvBox.Cell("legend").Height(15f))
                .Add(AvBox.Cell("coverageTitle").Height(16f))
                .Add(AvBox.Cell("coverage").Height(52f))
                .Add(AvBox.Cell("constellationTitle").Height(18f));
            for (int i = 0; i < spaceRows.Length; i++)
                page.Add(AvBox.Cell("sat" + i).Height(38f));
            page.Add(AvBox.Cell("platformTitle").Height(18f))
                .Add(AvBox.Cell("platform").Height(112f))
                .Add(AvBox.Cell("deployTitle").Height(18f))
                .Add(AvBox.Cell("roles").Height(32f))
                .Add(AvBox.Cell("altitude").Height(32f))
                .Add(AvBox.Cell("launch").Height(30f))
                .Add(AvBox.Filler());

            page.Arrange(body);
            parent = AvScreen.Scroll(parent, body, 750f, out body);
            page.Arrange(body);

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            BuildHero(parent, page.At("hero"));
            BuildSchematic(parent, page.At("schematic"));
            BuildLegend(parent, page.At("legend"));
            BuildCoverage(parent, page.At("coverageTitle"), page.At("coverage"));
            DrawBandTitle(parent, page.At("constellationTitle"), "01 / CONSTELLATION", "SELECT TO COMMAND");
            for (int i = 0; i < spaceRows.Length; i++)
                BuildSpaceRow(parent, page.At("sat" + i), i);
            BuildPlatform(parent, page.At("platformTitle"), page.At("platform"));
            BuildDeploy(parent, page.At("deployTitle"), page.At("roles"), page.At("altitude"), page.At("launch"));
        }

        private void BuildHero(RectTransform parent, Rect area)
        {
            float x = area.x + SpineInset;
            float width = area.width - SpineInset;
            AvKit.Label(parent, "SPACE WARFARE", new Rect(x, area.y, width * 0.6f, 24f),
                AvTheme.TextPrimary, AvTokens.FontTitle, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            AvStyled.Label(parent, new Rect(x, area.y - 24f, width * 0.7f, 15f),
                "STATION-KEEPING COMMAND · ANY GRID, ONE BURN AWAY", "row-sub");

            onlineChipBg = AvKit.Panel(parent, new Rect(x + width - 88f, area.y - 6f, 88f, 18f),
                AvTheme.SurfaceInert, AvSprites.Control);
            onlineChip = AvStyled.Label(parent, new Rect(x + width - 88f, area.y - 6f, 88f, 18f),
                "0/4 ONLINE", "row-value", align: TextAlignmentOptions.Center);
        }

        private void BuildSchematic(RectTransform parent, Rect area)
        {
            float x = area.x + SpineInset;
            float width = area.width - SpineInset;
            schematicRect = new Rect(x, area.y, width, area.height - 6f);

            var plotHost = new GameObject("PlotHost", typeof(RectTransform));
            var plotRect = (RectTransform)plotHost.transform;
            plotRect.SetParent(parent, false);
            plot = new OrbitPlot();
            plot.Build(plotRect, new Rect(0f, 0f, schematicRect.width, schematicRect.height));
            AvKit.Place(plotRect, schematicRect);
            AvKit.Outline(parent, schematicRect, AvTheme.Frame);

            for (int i = 0; i < schematicNames.Length; i++)
            {
                schematicNames[i] = AvStyled.Label(parent, new Rect(0f, 0f, 124f, 13f), "", "row-name");
                schematicDetails[i] = AvStyled.Label(parent, new Rect(0f, 0f, 124f, 12f), "", "row-sub");
                schematicNames[i].gameObject.SetActive(false);
                schematicDetails[i].gameObject.SetActive(false);
            }
        }

        private void BuildLegend(RectTransform parent, Rect area)
        {
            float x = area.x + SpineInset;
            for (int i = 0; i < 3; i++)
            {
                SatelliteRole role = (SatelliteRole)i;
                AvKit.Panel(parent, new Rect(x, area.y + 3f, 7f, 7f), RoleColor(role));
                AvStyled.Label(parent, new Rect(x + 11f, area.y, 52f, 14f), RoleName(role), "row-sub");
                x += 62f;
            }
            AvStyled.Label(parent, new Rect(area.x + area.width * 0.42f, area.y,
                area.width * 0.58f - SpineInset, 14f),
                "DOME = SWATH · DASH = TRANSFER", "section-title-note",
                align: TextAlignmentOptions.MidlineRight);
        }

        private void BuildCoverage(RectTransform parent, Rect titleArea, Rect area)
        {
            float x = titleArea.x + SpineInset;
            float width = titleArea.width - SpineInset;
            AvStyled.Label(parent, new Rect(x, titleArea.y, width * 0.5f, 15f),
                "COVERAGE AT CURSOR", "section-title");
            AvStyled.Label(parent, new Rect(x + width * 0.5f, titleArea.y, width * 0.5f, 15f),
                "WHO CAN SUPPORT THIS GRID", "section-title-note",
                align: TextAlignmentOptions.MidlineRight);

            float left = area.x + SpineInset;
            float row = area.height / 3f;
            for (int i = 0; i < 3; i++)
            {
                SatelliteRole role = (SatelliteRole)i;
                float rowY = area.y - i * row;
                var coverage = new CoverageRow();
                coverage.Rail = AvStyled.Rail(parent, new Rect(left, rowY - 1f, 3f, 14f), "locked");
                AvStyled.Label(parent, new Rect(left + 10f, rowY, 44f, 15f), RoleName(role), "kv-key",
                    align: TextAlignmentOptions.MidlineLeft);
                coverage.Asset = AvStyled.Label(parent, new Rect(left + 58f, rowY, 130f, 15f),
                    "—", "row-name");
                coverage.Status = AvStyled.Label(parent, new Rect(left + 190f, rowY,
                    area.width - SpineInset - 190f, 15f), "—", "kv-value",
                    align: TextAlignmentOptions.MidlineRight);
                coverageRows[i] = coverage;
            }
        }

        private void BuildSpaceRow(RectTransform parent, Rect area, int index)
        {
            var row = new SpaceRow();
            var root = new GameObject("SatelliteRow" + index, typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            float width = area.width - SpineInset;
            AvKit.Place(rootRect, new Rect(area.x + SpineInset, area.y, width, 36f));
            row.Root = root;

            AvKit.Rule(rootRect, new Rect(0f, 0f, width, 1f),
                       AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));
            row.Rail = AvStyled.Rail(rootRect, new Rect(0f, 8f, 3f, 24f), "ready");
            row.Name = AvStyled.Label(rootRect, new Rect(12f, 2f, width - 90f, 16f), "—", "row-name");
            row.Status = AvStyled.Label(rootRect, new Rect(12f, 19f, width - 90f, 14f), "", "row-sub");
            row.Select = AvStyled.Button(rootRect, new Rect(width - 68f, 6f, 62f, 24f),
                "TRACK", "btn", () => { selectedSatellite = SelectedIdFromRow(index); nextRefresh = 0f; },
                AvButtonStyle.Toggle);
            spaceRows[index] = row;
        }

        private void BuildPlatform(RectTransform parent, Rect titleArea, Rect area)
        {
            platformTitle = DrawBandTitle(parent, titleArea, "02 / PLATFORM CONTROL", "");
            float x = area.x + SpineInset;
            float width = area.width - SpineInset;
            float column = width * 0.5f;
            platformStation = Stat(parent, x, area.y, column, "STATION");
            platformSwath = Stat(parent, x + column, area.y, column, "SWATH");
            platformDist = Stat(parent, x, area.y - 18f, column, "DIST TO CURSOR");
            platformFuel = Stat(parent, x + column, area.y - 18f, column, "FUEL");
            platformFuelBar = AvKit.ProgressBar(parent, new Rect(x, area.y - 40f, width * 0.55f, 8f),
                0f, AvTheme.RailReady);

            float buttonWidth = (width - 6f) * 0.62f;
            moveSatelliteButton = AvStyled.Button(parent,
                new Rect(x, area.y - 58f, buttonWidth, 28f),
                "DESIGNATE STATION", "btn", () =>
                {
                    int id = selectedSatellite;
                    if (id != 0) support.ArmCommand(OpsCommand.Move, (byte)id, 0, "STATION TRANSFER");
                    nextRefresh = 0f;
                }, AvButtonStyle.Primary)
                .WithTooltip("Click a map point: the satellite transfers to a station over that grid.");
            recallSatelliteButton = AvStyled.Button(parent,
                new Rect(x + buttonWidth + 6f, area.y - 58f, width - buttonWidth - 6f, 28f),
                "RECALL", "btn", () =>
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
                }, AvButtonStyle.Quiet)
                .WithTooltip("De-orbit this satellite and refund part of its launch cost.");
        }

        private void BuildDeploy(RectTransform parent, Rect titleArea, Rect roles, Rect altitude, Rect launch)
        {
            deployStatus = DrawBandTitle(parent, titleArea, "03 / DEPLOY SATELLITE", "");

            float x = roles.x + SpineInset;
            float width = roles.width - SpineInset;
            float buttonWidth = (width - 8f) / 3f;
            launchRoleButtons = new AvButton[3];
            for (int i = 0; i < 3; i++)
            {
                SatelliteRole role = (SatelliteRole)i;
                launchRoleButtons[i] = AvStyled.Button(parent,
                    new Rect(x + i * (buttonWidth + 4f), roles.y, buttonWidth, 28f),
                    RoleName(role), "toggle",
                    () => { launchRole = role; nextRefresh = 0f; },
                    AvButtonStyle.Toggle);
            }

            altitudeButtons = new AvButton[Constellation.AltitudeCount];
            for (int i = 0; i < altitudeButtons.Length; i++)
            {
                SatelliteAltitude option = Constellation.Altitude(i);
                byte index = (byte)i;
                altitudeButtons[i] = AvStyled.Button(parent,
                    new Rect(x + i * (buttonWidth + 4f), altitude.y, buttonWidth, 28f),
                    option.Name, "toggle",
                    () => { launchAltitude = index; nextRefresh = 0f; },
                    AvButtonStyle.Toggle)
                    .WithTooltip(option.Name + ": swath " + (option.Swath / 1000f).ToString("0") +
                                 " km · transfer " + (option.TransitSpeed / 1000f).ToString("0.0") + " km/s · " +
                                 option.FuelPerKm.ToString("0.00") + "% fuel/km.");
            }

            launchButton = AvStyled.Button(parent, new Rect(x, launch.y, width, 28f),
                "SELECT STATION ON MAP", "btn", () =>
                {
                    support.ArmCommand(OpsCommand.Launch, launchAltitude, (byte)launchRole,
                        "DEPLOY " + RoleName(launchRole) + " SATELLITE");
                    nextRefresh = 0f;
                }, AvButtonStyle.Primary)
                .WithTooltip("Arm the map, then right-click the grid this satellite should hold.");
        }

        private static TMP_Text Stat(RectTransform parent, float x, float y, float width, string key)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.55f, 15f), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, 15f),
                "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
        }

        private static TMP_Text DrawBandTitle(RectTransform parent, Rect area, string title, string note)
        {
            AvStyled.Box(parent, new Rect(area.x + SpineInset, area.y + 3f,
                area.width - SpineInset, 17f), "section band");
            AvStyled.SpineTick(parent, area.x + SpineInset, area.y + 3f);
            AvStyled.Label(parent, new Rect(area.x + SpineInset + 8f, area.y,
                area.width - SpineInset - 8f, 16f), title, "section-title");
            return AvStyled.Label(parent, new Rect(area.x + area.width * 0.42f, area.y,
                area.width * 0.58f - SpineInset, 16f), note, "section-title-note",
                align: TextAlignmentOptions.MidlineRight);
        }

        private int SelectedIdFromRow(int index)
        {
            Constellation constellation = support.LocalConstellation;
            if (constellation == null || index >= constellation.Satellites.Count) return 0;
            return constellation.Satellites[index].Id;
        }

        private void RefreshSpace(bool bypass)
        {
            if (plot == null) return;
            if (Time.unscaledTime >= nextSpacePoll)
            {
                nextSpacePoll = Time.unscaledTime + 1f;
                support.PollOps();
            }

            Constellation constellation = support.LocalConstellation;
            int maximum = support.Settings != null ? support.Settings.MaximumSatellites.Value : 4;
            int count = constellation != null ? constellation.Satellites.Count : 0;
            bool hasCursor = TryCursor(out float cursorX, out float cursorZ);

            if (Time.unscaledTime >= nextPlotAt)
            {
                nextPlotAt = Time.unscaledTime + 0.25f;
                plot.Render(constellation, selectedSatellite, hasCursor, cursorX, cursorZ, AvTheme.RailReady);
            }

            if (onlineChip != null)
            {
                onlineChip.text = count + "/" + maximum + " ONLINE";
                onlineChip.color = count > 0 ? AvTheme.RailReady : AvTheme.Dim;
                onlineChipBg.color = count > 0
                    ? AvTheme.Unity(AvTokens.RailReady.WithAlpha(0.12f))
                    : AvTheme.SurfaceInert;
            }

            RefreshSchematicLabels(constellation, count);
            RefreshCoverageRows(constellation, hasCursor, cursorX, cursorZ);

            for (int i = 0; i < spaceRows.Length; i++)
            {
                SpaceRow row = spaceRows[i];
                if (row == null) continue;
                Satellite satellite = constellation != null && i < count ? constellation.Satellites[i] : null;
                row.Root.SetActive(satellite != null);
                if (satellite == null) continue;

                Color role = RoleColor(satellite.Role);
                row.Rail.color = role;
                row.Name.text = SatelliteNaming.Callsign(satellite.Role, satellite.Id);
                row.Name.color = role;
                row.Status.text = RoleName(satellite.Role) + " · " + satellite.Orbit.Name + " · " +
                    SatelliteStatus(constellation, satellite, hasCursor, cursorX, cursorZ);
                row.Select.SetEnabled(true);
                row.Select.SetLatched(satellite.Id == selectedSatellite);
            }

            Satellite selected = constellation != null ? constellation.Find((byte)selectedSatellite) : null;
            if (selected == null && constellation != null && count > 0)
            {
                selected = constellation.Satellites[0];
                selectedSatellite = selected.Id;
            }
            support.SelectedSatelliteId = selectedSatellite;

            if (platformTitle != null)
                platformTitle.text = selected == null ? "—"
                    : SatelliteNaming.Callsign(selected.Role, selected.Id) + " · " + RoleName(selected.Role);

            if (selected == null)
            {
                platformStation.text = platformSwath.text = platformDist.text = platformFuel.text = "—";
                platformFuelBar.fillAmount = 0f;
            }
            else if (selected.State == SatelliteState.Transit)
            {
                platformStation.text = "TRANSFER " + Clock(selected.TransitLeft);
                platformStation.color = AvTheme.RailCaution;
                platformSwath.text = selected.Orbit.Name + " · " +
                    (selected.Orbit.Swath / 1000f).ToString("0") + " km";
                platformSwath.color = AvTheme.TextPrimary;
                platformDist.text = hasCursor ? CursorDistance(selected, cursorX, cursorZ) : "—";
                platformDist.color = AvTheme.TextPrimary;
                platformFuel.text = selected.Fuel.ToString("0") + "%";
                platformFuel.color = selected.Fuel < 25f ? AvTheme.RailCaution : AvTheme.TextPrimary;
                platformFuelBar.fillAmount = selected.Fuel / Constellation.MaximumFuel;
                platformFuelBar.color = selected.Fuel < 25f ? AvTheme.RailCaution : RoleColor(selected.Role);
            }
            else
            {
                platformStation.text = "X " + selected.StationX.ToString("0") + " · Z " + selected.StationZ.ToString("0");
                platformStation.color = AvTheme.TextPrimary;
                platformSwath.text = selected.Orbit.Name + " · " +
                    (selected.Orbit.Swath / 1000f).ToString("0") + " km";
                platformSwath.color = AvTheme.TextPrimary;
                platformDist.text = hasCursor ? CursorDistance(selected, cursorX, cursorZ) : "—";
                platformDist.color = hasCursor && constellation.SatelliteCovers(selected, cursorX, cursorZ)
                    ? AvTheme.RailReady : AvTheme.TextPrimary;
                platformFuel.text = selected.Fuel.ToString("0") + "%";
                platformFuel.color = selected.Fuel < 25f ? AvTheme.RailCaution : AvTheme.TextPrimary;
                platformFuelBar.fillAmount = selected.Fuel / Constellation.MaximumFuel;
                platformFuelBar.color = selected.Fuel < 25f ? AvTheme.RailCaution : RoleColor(selected.Role);
            }

            bool moveArmed = support.CommandArmed && support.ArmedCommand == OpsCommand.Move;
            if (moveSatelliteButton != null)
            {
                moveSatelliteButton.SetEnabled(selected != null || moveArmed);
                moveSatelliteButton.SetLatched(moveArmed);
                moveSatelliteButton.SetText(moveArmed ? "CLICK MAP TO MOVE" : "DESIGNATE STATION");
            }
            if (recallSatelliteButton != null)
            {
                bool confirming = Time.unscaledTime < recallConfirmUntil;
                recallSatelliteButton.SetEnabled(selected != null);
                recallSatelliteButton.SetText(confirming ? "CONFIRM" : "RECALL");
                recallSatelliteButton.SetLatched(confirming);
            }

            if (launchRoleButtons != null)
            {
                for (int i = 0; i < launchRoleButtons.Length; i++)
                {
                    SatelliteRole role = (SatelliteRole)i;
                    launchRoleButtons[i].SetLatched(role == launchRole);
                    launchRoleButtons[i].SetText(RoleName(role) + " " + support.SatelliteCost(role).ToString("N0"));
                    launchRoleButtons[i].WithTooltip(RoleDescription(role));
                }
            }
            if (altitudeButtons != null)
            {
                for (int i = 0; i < altitudeButtons.Length; i++)
                {
                    SatelliteAltitude option = Constellation.Altitude(i);
                    altitudeButtons[i].SetLatched(i == launchAltitude);
                    altitudeButtons[i].SetText(option.Name + " " + (option.Swath / 1000f).ToString("0") + "km");
                }
            }

            float cost = support.SatelliteCost(launchRole);
            bool room = count < maximum;
            bool affordable = bypass || support.LocalAllocation + 0.001f >= cost;
            bool launchArmed = support.CommandArmed && support.ArmedCommand == OpsCommand.Launch;
            if (deployStatus != null)
            {
                deployStatus.text = launchArmed ? "STATION PREVIEW ACTIVE"
                    : !room ? "NO FREE SLOT · RECALL ONE FIRST"
                    : !affordable ? "INSUFFICIENT ALLOCATION · " + cost.ToString("N0") + " REQUIRED"
                    : (maximum - count) + " SLOT" + (maximum - count == 1 ? "" : "S") + " · " +
                      cost.ToString("N0") + " ALLOC · TRANSFER " +
                      (Constellation.Altitude(launchAltitude).TransitSpeed / 1000f).ToString("0.0") + " KM/S";
                deployStatus.color = launchArmed ? AvTheme.RailCaution
                    : room && affordable ? AvTheme.RailReady : AvTheme.Warning;
            }
            if (launchButton != null)
            {
                launchButton.SetLatched(launchArmed);
                launchButton.SetText(launchArmed ? "CLICK MAP TO PLACE" : "SELECT STATION ON MAP");
                launchButton.SetEnabled(launchArmed ||
                    (room && affordable && !support.RequestPending && !support.CommandPending));
            }
        }

        private void RefreshSchematicLabels(Constellation constellation, int count)
        {
            for (int i = 0; i < schematicNames.Length; i++)
            {
                Satellite satellite = constellation != null && i < count ? constellation.Satellites[i] : null;
                if (satellite == null)
                {
                    if (schematicNames[i].gameObject.activeSelf) schematicNames[i].gameObject.SetActive(false);
                    if (schematicDetails[i].gameObject.activeSelf) schematicDetails[i].gameObject.SetActive(false);
                    continue;
                }

                constellation.Position(satellite, out float px, out float pz);
                OrbitPlot.Project(constellation, px, pz, schematicRect, 1f, out float lx, out float ly);
                float labelX = lx + 9f;
                if (labelX + 122f > schematicRect.xMax - 2f) labelX = lx - 131f;
                float nameY = ly - 2f;
                float detailY = ly - 16f;
                if (detailY - 10f < schematicRect.y - schematicRect.height + 2f)
                {
                    nameY = ly + 24f;
                    detailY = ly + 10f;
                }

                AvKit.Place(schematicNames[i].rectTransform, new Rect(labelX, nameY, 122f, 13f));
                AvKit.Place(schematicDetails[i].rectTransform, new Rect(labelX, detailY, 122f, 12f));
                schematicNames[i].text = SatelliteNaming.Callsign(satellite.Role, satellite.Id);
                schematicNames[i].color = RoleColor(satellite.Role);
                schematicDetails[i].text = satellite.State == SatelliteState.Transit
                    ? "TRANSFER " + Clock(satellite.TransitLeft)
                    : satellite.Orbit.Name + " STATION";
                schematicDetails[i].color = AvTheme.Dim;
                if (!schematicNames[i].gameObject.activeSelf) schematicNames[i].gameObject.SetActive(true);
                if (!schematicDetails[i].gameObject.activeSelf) schematicDetails[i].gameObject.SetActive(true);
            }
        }

        private void RefreshCoverageRows(Constellation constellation, bool hasCursor, float cursorX, float cursorZ)
        {
            for (int i = 0; i < coverageRows.Length; i++)
            {
                CoverageRow row = coverageRows[i];
                if (row == null) continue;
                SatelliteRole role = (SatelliteRole)i;

                if (constellation == null || !hasCursor)
                {
                    row.Rail.color = AvTheme.Dim;
                    row.Asset.text = "—";
                    row.Asset.color = AvTheme.Dim;
                    row.Status.text = constellation == null ? "THEATER DATA UNAVAILABLE" : "MOVE CURSOR OVER A GRID";
                    row.Status.color = AvTheme.Dim;
                    continue;
                }

                StationCoverage coverage = constellation.Query(role, cursorX, cursorZ);
                if (coverage.Covered)
                {
                    row.Rail.color = AvTheme.RailReady;
                    row.Asset.text = SatelliteName(constellation, coverage.SatelliteId);
                    row.Asset.color = AvTheme.RailReady;
                    row.Status.text = "IN COVERAGE";
                    row.Status.color = AvTheme.RailReady;
                }
                else if (coverage.HasSatellite)
                {
                    row.Rail.color = AvTheme.RailCaution;
                    row.Asset.text = SatelliteName(constellation, coverage.SatelliteId);
                    row.Asset.color = RoleColor(role);
                    row.Status.text = (coverage.NearestGap / 1000f).ToString("0.0") + " KM OUT";
                    row.Status.color = AvTheme.RailCaution;
                }
                else
                {
                    row.Rail.color = AvTheme.RailDanger;
                    row.Asset.text = "NO ASSET";
                    row.Asset.color = AvTheme.Dim;
                    row.Status.text = "DEPLOY ONE IN THIS ROLE";
                    row.Status.color = AvTheme.RailDanger;
                }
            }
        }

        private static string SatelliteStatus(Constellation constellation, Satellite satellite,
                                              bool hasCursor, float cursorX, float cursorZ)
        {
            if (satellite.State == SatelliteState.Transit)
                return "TRANSFER " + Clock(satellite.TransitLeft);
            if (!hasCursor) return "ON STATION";
            return constellation.SatelliteCovers(satellite, cursorX, cursorZ)
                ? "COVERS CURSOR"
                : "ON STATION";
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
            role == SatelliteRole.Recon ? AvTheme.Friendly :
            role == SatelliteRole.Strike ? AvTheme.Alert : AvTheme.Warning;

        private static string RoleName(SatelliteRole role) => SatelliteNaming.RoleTag(role);

        private static string RoleDescription(SatelliteRole role)
        {
            switch (role)
            {
                case SatelliteRole.Recon: return "Reconnaissance: satellite scan coverage.";
                case SatelliteRole.Strike: return "Kinetic strike: Rod from God coverage.";
                default: return "Electronic warfare: EMP shock coverage.";
            }
        }
    }
}
