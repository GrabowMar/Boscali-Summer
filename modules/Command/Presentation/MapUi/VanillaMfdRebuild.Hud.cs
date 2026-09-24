using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // --------------------------------------------------------------- HUD panel

        private sealed class HudPresenter : Presenter
        {
            private static readonly string[] ModeNotes =
            {
                "Routes & waypoints",
                "Guns & lead cue",
                "Air intercepts",
                "Ground attack cues",
                "Emitters & jamming",
                "Transport routing"
            };

            private static readonly string[] CategoryNotes =
            {
                "Friendly contacts",
                "Hostile contacts",
                "Airborne tracks",
                "Incoming missiles",
                "Ground vehicles",
                "Bases & structures"
            };

            private static readonly string[] VehicleNotes =
            {
                "Supply trucks",
                "Unmanned vehicles",
                "Light combat armor",
                "Armored vehicles",
                "Heavy tanks",
                "Field artillery",
                "Anti-air guns",
                "Heat-seeking SAMs",
                "Radar-guided SAMs",
                "Search radars"
            };

            private static readonly string[] BuildingNotes =
            {
                "Civilian sites",
                "Industrial plants",
                "Early warning radar",
                "Fuel & supply depots",
                "Hangars & shelters",
                "Fortifications",
                "Munitions stores"
            };

            private readonly HUDOptions options;
            private readonly Dictionary<HUDOptions_ToggleButton, string> labels =
                new Dictionary<HUDOptions_ToggleButton, string>();
            private RectTransform[] pages;
            private int selectedPage;

            private MfdPagingGrid modes;
            private MfdPagingGrid categories;
            private readonly TMP_Text[] modeReadoutValues = new TMP_Text[3];

            private MfdPagingGrid vehicles;
            private AvButton vehAll, vehClear;
            private readonly TMP_Text[] vehReadoutValues = new TMP_Text[2];

            private MfdPagingGrid buildings;
            private AvButton bldAll, bldClear;
            private readonly TMP_Text[] bldReadoutValues = new TMP_Text[2];

            public HudPresenter(MFDScreen screen, HUDOptions options)
                : base(screen, VanillaMfdPanelId.Hud)
            {
                this.options = options;
            }

            protected override int TabCount => 3;

            protected override void BuildContent()
            {
                ConfigureTabs(new[] { "MODE", "VEHICLES", "BUILDINGS" }, SelectPage);
                pages = new[]
                {
                    CreatePage("Engagement"),
                    CreatePage("Vehicles"),
                    CreatePage("Buildings"),
                };

                BuildModePage(pages[0]);
                BuildVehiclesPage(pages[1]);
                BuildBuildingsPage(pages[2]);
                SelectPage(0);
            }

            // ----------------------------------------------------------------- mode page

            private void BuildModePage(RectTransform root)
            {
                DrawSpine(root);
                float width = PageWidth;
                float cell = Mathf.Clamp((PageHeight - 198f) / 6f, 48f, 70f);
                const float brief = 98f;

                float y = Heading(root, -AvTokens.Space1, width,
                                  "ACTIVE HUD PROFILE", "PILOT DISPLAY");
                Rect profile = new Rect(AvTokens.Space3, y, width - AvTokens.Space3, brief);
                AvKit.Panel(root, profile, AvTheme.SurfaceInert);
                AvKit.Outline(root, profile, AvTheme.Hairline.WithAlpha(0.7f));
                AvKit.Rule(root, new Rect(profile.x, y, 54f, 2f), AvTheme.Accent);
                AvStyled.Label(root, new Rect(profile.x + 14f, y - 10f, profile.width - 28f, 14f),
                    "AUTO SELECT / CURRENT MODE", "metric-key");
                modeReadoutValues[0] = AvStyled.Label(root,
                    new Rect(profile.x + 14f, y - 27f, profile.width - 28f, 30f), "—", "metric-value");
                modeReadoutValues[0].enableAutoSizing = true;
                modeReadoutValues[0].fontSizeMin = AvTokens.FontBody;
                AvKit.Rule(root, new Rect(profile.x + 12f, y - 62f, profile.width - 24f, 1f),
                    AvTheme.Hairline.WithAlpha(0.5f));
                AvStyled.Label(root, new Rect(profile.x + 14f, y - 67f, 90f, 14f), "GATES", "metric-key");
                modeReadoutValues[1] = AvStyled.Label(root,
                    new Rect(profile.x + 94f, y - 67f, 120f, 16f), "—", "kv-value");
                AvStyled.Label(root, new Rect(profile.x + profile.width * 0.52f, y - 67f, 74f, 14f),
                    "SURFACE", "metric-key");
                modeReadoutValues[2] = AvStyled.Label(root,
                    new Rect(profile.x + profile.width * 0.7f, y - 67f,
                        profile.width * 0.3f - 12f, 16f), "—", "kv-value",
                    align: TextAlignmentOptions.MidlineRight);
                modeReadoutValues[2].enableAutoSizing = true;
                modeReadoutValues[2].fontSizeMin = AvTokens.FontMicro;
                y -= brief + AvTokens.Space2;

                y = Heading(root, y, width, "ENGAGEMENT MODE", "SELECT ONE");
                modes = new MfdPagingGrid(root, y, width, 2, 3, pager: false, rowHeight: cell, exclusive: true);
                y -= cell * 3f + AvTokens.Space2;

                y = Heading(root, y, width, "PRIORITY GATES", "MAXIMISE TRACKS");
                categories = new MfdPagingGrid(root, y, width, 2, 3, pager: false, rowHeight: cell);

            }

            // ------------------------------------------------------------- vehicles page

            private void BuildVehiclesPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float gap = AvTokens.Gap;
                float cell = Mathf.Clamp((PageHeight - 160f) / 5f, 46f, 96f);
                const float cardHeight = 68f;

                float y = Heading(page, -AvTokens.Space1, width,
                                  "VEHICLE PRIORITY", "FILTER MATRIX · MULTI-SELECT");
                vehicles = new MfdPagingGrid(page, y, width, 2, 5, pager: false, rowHeight: cell);
                y -= cell * 5f + AvTokens.Space2;

                float btnWidth = (width - AvTokens.Space3 - gap * 3f) / 4f;
                float btnHeight = AvTokens.RowHeight;
                vehAll = AvStyled.Button(page, new Rect(AvTokens.Space3, y, btnWidth, btnHeight),
                    "ALL ON", "btn", () => SetAllVehicles(true))
                    .WithTooltip("Highlight all 10 vehicle types on HUD.");
                AvStyled.Button(page, new Rect(AvTokens.Space3 + btnWidth + gap, y, btnWidth, btnHeight),
                    "AIR DEF", "btn", () => SetVehicleFilter(6, 7, 8, 9))
                    .WithTooltip("Filter for air defense threats only: AAA, IR SAM, R SAM, RDR.");
                AvStyled.Button(page, new Rect(AvTokens.Space3 + (btnWidth + gap) * 2f, y, btnWidth, btnHeight),
                    "ARMOR", "btn", () => SetVehicleFilter(2, 3, 4))
                    .WithTooltip("Filter for armored targets: MBT, AFV, LCV.");
                vehClear = AvStyled.Button(page, new Rect(AvTokens.Space3 + (btnWidth + gap) * 3f, y, btnWidth, btnHeight),
                    "CLEAR", "btn", () => SetAllVehicles(false))
                    .WithTooltip("Deselect all vehicle priority filters.");
                y -= btnHeight + AvTokens.Space2;

                AvKit.TacticalCard(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, cardHeight), AvTheme.RailInfo);
                AvStyled.Label(page, new Rect(AvTokens.Space5, y - 10f, width - AvTokens.Space5 * 2f, 14f),
                    "SURFACE THREAT SUMMARY", "section-title");

                string[] keys = { "AIR DEFENSE", "ARMORED TARGETS" };
                for (int i = 0; i < keys.Length; i++)
                {
                    float rowY = y - 28f - i * 18f;
                    AvStyled.Label(page, new Rect(AvTokens.Space5, rowY, 140f, 15f), keys[i], "kv-key");
                    vehReadoutValues[i] = AvStyled.Label(page,
                        new Rect(AvTokens.Space5 + 140f, rowY, width - AvTokens.Space5 * 2f - 140f, 15f),
                        "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
                }
            }

            // ------------------------------------------------------------ buildings page

            private void BuildBuildingsPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float gap = AvTokens.Gap;
                float cell = Mathf.Clamp((PageHeight - 160f) / 4f, 46f, 110f);
                const float cardHeight = 68f;

                float y = Heading(page, -AvTokens.Space1, width,
                                  "BUILDING PRIORITY", "FILTER MATRIX · MULTI-SELECT");
                buildings = new MfdPagingGrid(page, y, width, 2, 4, pager: false, rowHeight: cell);
                y -= cell * 4f + AvTokens.Space2;

                float btnWidth = (width - AvTokens.Space3 - gap * 3f) / 4f;
                float btnHeight = AvTokens.RowHeight;
                bldAll = AvStyled.Button(page, new Rect(AvTokens.Space3, y, btnWidth, btnHeight),
                    "ALL ON", "btn", () => SetAllBuildings(true))
                    .WithTooltip("Highlight all 7 building types on HUD.");
                AvStyled.Button(page, new Rect(AvTokens.Space3 + btnWidth + gap, y, btnWidth, btnHeight),
                    "STRIKE", "btn", () => SetBuildingFilter(2, 3, 4, 6))
                    .WithTooltip("Filter for strategic strike targets: RDR, DEP, HGR, AMMO.");
                AvStyled.Button(page, new Rect(AvTokens.Space3 + (btnWidth + gap) * 2f, y, btnWidth, btnHeight),
                    "MILITARY", "btn", () => SetBuildingFilter(1, 2, 3, 4, 5, 6))
                    .WithTooltip("Prioritise all military installations; exclude civilian.");
                bldClear = AvStyled.Button(page, new Rect(AvTokens.Space3 + (btnWidth + gap) * 3f, y, btnWidth, btnHeight),
                    "CLEAR", "btn", () => SetAllBuildings(false))
                    .WithTooltip("Deselect all building priority filters.");
                y -= btnHeight + AvTokens.Space2;

                AvKit.TacticalCard(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, cardHeight), AvTheme.RailInfo);
                AvStyled.Label(page, new Rect(AvTokens.Space5, y - 10f, width - AvTokens.Space5 * 2f, 14f),
                    "STRUCTURE TARGET SUMMARY", "section-title");

                string[] keys = { "STRIKE TARGETS", "CIVILIAN ASSETS" };
                for (int i = 0; i < keys.Length; i++)
                {
                    float rowY = y - 28f - i * 18f;
                    AvStyled.Label(page, new Rect(AvTokens.Space5, rowY, 140f, 15f), keys[i], "kv-key");
                    bldReadoutValues[i] = AvStyled.Label(page,
                        new Rect(AvTokens.Space5 + 140f, rowY, width - AvTokens.Space5 * 2f - 140f, 15f),
                        "—", "kv-value", align: TextAlignmentOptions.MidlineRight);
                }
            }

            // ------------------------------------------------------------- refresh

            protected override void RefreshContent()
            {
                if (options == null || options.listModes == null || options.listCategories == null ||
                    options.listVehicleTypes == null || options.listBuildingTypes == null)
                {
                    SetGridInput(false);
                    Shell.DataBar.State.text = "WAITING FOR HUD OPTIONS";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
                    return;
                }

                Shell.DataBar.State.text = "HUD PRIORITY MATRIX";
                SetGridInput(true);
                int activeVeh = CountEnabled(options.listVehicleTypes);
                int activeBld = CountEnabled(options.listBuildingTypes);
                int activeGates = CountCategories(options.listCategories);

                Shell.DataBar.SetChip(0, options.currentMode.ToString(), true);
                Shell.DataBar.SetChip(1, activeVeh + "/" + options.listVehicleTypes.Count + " VEH", true);
                Shell.DataBar.SetChip(2, activeBld + "/" + options.listBuildingTypes.Count + " BLD", true);

                string brief;
                switch (options.currentMode.ToString())
                {
                    case "NAV": brief = "NAVIGATION"; break;
                    case "GUN": brief = "GUNNERY"; break;
                    case "A2A": brief = "AIR COMBAT"; break;
                    case "A2G": brief = "GROUND STRIKE"; break;
                    case "EW": brief = "ELECTRONIC WARFARE"; break;
                    default: brief = "LOGISTICS"; break;
                }

                modeReadoutValues[0].text = brief + " (" + options.currentMode + ")";
                modeReadoutValues[1].text = activeGates + " OF " + options.listCategories.Count + " GATES";
                modeReadoutValues[2].text = activeVeh + " VEH · " + activeBld + " BLD";

                modes.SetData(options.listModes.Count,
                    i => i < 6 ? ((HUDOptions.HUDMode)i).ToString() : LabelFor(options.listModes[i], "MODE"),
                    i => options.listModes[i] != null && options.listModes[i].status,
                    SelectMode,
                    subs: i => i < ModeNotes.Length ? ModeNotes[i] : null);

                categories.SetData(options.listCategories.Count,
                    i => NativeCategoryLabel(options.listCategories[i], i),
                    i => options.listCategories[i] != null && options.listCategories[i].maximized,
                    ToggleCategory,
                    subs: i => i < CategoryNotes.Length ? CategoryNotes[i] : null);

                vehicles.SetData(options.listVehicleTypes.Count,
                    i => LabelFor(options.listVehicleTypes[i], "VEHICLE"),
                    i => options.listVehicleTypes[i] != null && options.listVehicleTypes[i].status,
                    ToggleVehicle,
                    icons: i => DefinitionIcon(options.listVehicleTypes[i]),
                    subs: i => i < VehicleNotes.Length ? VehicleNotes[i] : null);

                buildings.SetData(options.listBuildingTypes.Count,
                    i => LabelFor(options.listBuildingTypes[i], "BUILDING"),
                    i => options.listBuildingTypes[i] != null && options.listBuildingTypes[i].status,
                    ToggleBuilding,
                    icons: i => DefinitionIcon(options.listBuildingTypes[i]),
                    subs: i => i < BuildingNotes.Length ? BuildingNotes[i] : null);

                // Update vehicle presets & telemetry
                int airDefCount = 0;
                int[] airDefIndices = { 6, 7, 8, 9 };
                foreach (int idx in airDefIndices)
                    if (idx < options.listVehicleTypes.Count && options.listVehicleTypes[idx] != null && options.listVehicleTypes[idx].status)
                        airDefCount++;

                int armorCount = 0;
                int[] armorIndices = { 2, 3, 4 };
                foreach (int idx in armorIndices)
                    if (idx < options.listVehicleTypes.Count && options.listVehicleTypes[idx] != null && options.listVehicleTypes[idx].status)
                        armorCount++;

                vehAll.SetEnabled(activeVeh < options.listVehicleTypes.Count);
                vehClear.SetEnabled(activeVeh > 0);
                vehReadoutValues[0].text = airDefCount + " OF 4 ACTIVE" + (airDefCount == 4 ? " (FULL)" : "");
                vehReadoutValues[0].color = airDefCount > 0 ? AvTheme.Accent : AvTheme.Dim;
                vehReadoutValues[1].text = armorCount + " OF 3 ACTIVE";
                vehReadoutValues[1].color = armorCount > 0 ? AvTheme.Accent : AvTheme.Dim;

                // Update building presets & telemetry
                int strikeCount = 0;
                int[] strikeIndices = { 2, 3, 4, 6 };
                foreach (int idx in strikeIndices)
                    if (idx < options.listBuildingTypes.Count && options.listBuildingTypes[idx] != null && options.listBuildingTypes[idx].status)
                        strikeCount++;

                bool civActive = options.listBuildingTypes.Count > 0 && options.listBuildingTypes[0] != null && options.listBuildingTypes[0].status;
                bldAll.SetEnabled(activeBld < options.listBuildingTypes.Count);
                bldClear.SetEnabled(activeBld > 0);
                bldReadoutValues[0].text = strikeCount + " OF 4 ACTIVE";
                bldReadoutValues[0].color = strikeCount > 0 ? AvTheme.Accent : AvTheme.Dim;
                bldReadoutValues[1].text = civActive ? "ACTIVE (COLLATERAL RISK)" : "OFF (PROTECTED)";
                bldReadoutValues[1].color = civActive ? AvTheme.Warning : AvTheme.Dim;
            }

            protected override string AmbientStatus() =>
                options == null
                    ? "HUD MATRIX UNAVAILABLE"
                    : selectedPage == 0
                        ? "HUD " + options.currentMode + " — SELECT ENGAGEMENT PROFILE & PRIORITY GATES"
                        : selectedPage == 1
                            ? "VEHICLES — TAP TO TOGGLE • USE PRESETS FOR QUICK MISSION LOADOUTS"
                            : "BUILDINGS — TAP TO TOGGLE • USE PRESETS FOR TARGET SELECTION";

            private void SelectPage(int next)
            {
                selectedPage = next;
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == next);
                SetSelectedTab(next);
                RequestRefresh();
            }

            private void SelectMode(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listModes.Count) return;
                HUDOptions_ToggleButton source = options.listModes[index];
                if (source == null) return;
                options.ToggleButtons(source);
                if (index < 6) options.currentMode = (HUDOptions.HUDMode)index;
                Persist();
            }

            private void ToggleCategory(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listCategories.Count) return;
                HUDOptions_Category category = options.listCategories[index];
                if (category == null) return;
                category.Set(!category.maximized);
                Persist();
            }

            private void ToggleVehicle(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listVehicleTypes.Count) return;
                HUDOptions_ToggleButton source = options.listVehicleTypes[index];
                if (source == null) return;
                source.Set(!source.status);
                Persist();
            }

            private void ToggleBuilding(int index)
            {
                if (!Ready) return;
                if (index < 0 || index >= options.listBuildingTypes.Count) return;
                HUDOptions_ToggleButton source = options.listBuildingTypes[index];
                if (source == null) return;
                source.Set(!source.status);
                Persist();
            }

            private void SetAllVehicles(bool enabled)
            {
                if (!Ready) return;
                for (int i = 0; i < options.listVehicleTypes.Count; i++)
                    options.listVehicleTypes[i]?.Set(enabled);
                Persist();
            }

            private void SetVehicleFilter(params int[] activeIndices)
            {
                if (!Ready) return;
                HashSet<int> active = new HashSet<int>(activeIndices);
                for (int i = 0; i < options.listVehicleTypes.Count; i++)
                    options.listVehicleTypes[i]?.Set(active.Contains(i));
                Persist();
            }

            private void SetAllBuildings(bool enabled)
            {
                if (!Ready) return;
                for (int i = 0; i < options.listBuildingTypes.Count; i++)
                    options.listBuildingTypes[i]?.Set(enabled);
                Persist();
            }

            private void SetBuildingFilter(params int[] activeIndices)
            {
                if (!Ready) return;
                HashSet<int> active = new HashSet<int>(activeIndices);
                for (int i = 0; i < options.listBuildingTypes.Count; i++)
                    options.listBuildingTypes[i]?.Set(active.Contains(i));
                Persist();
            }

            private static int CountCategories(List<HUDOptions_Category> categories)
            {
                if (categories == null) return 0;
                int count = 0;
                for (int i = 0; i < categories.Count; i++)
                    if (categories[i] != null && categories[i].maximized) count++;
                return count;
            }

            private void Persist()
            {
                options.SaveSettings();
                options.NeedUpdateIcons();
                RequestRefresh();
            }

            private static Sprite DefinitionIcon(HUDOptions_ToggleButton button)
            {
                return button == null || button.listDefinitions == null || button.listDefinitions.Count == 0 ||
                    button.listDefinitions[0] == null ? null : button.listDefinitions[0].mapIcon;
            }

            private string LabelFor(HUDOptions_ToggleButton source, string fallback)
            {
                if (source == null) return fallback;
                if (!labels.TryGetValue(source, out string label))
                {
                    label = NativeLabel(source, fallback);
                    labels.Add(source, label);
                }
                return label;
            }

            private bool Ready => options != null && options.listModes != null &&
                                  options.listCategories != null &&
                                  options.listVehicleTypes != null &&
                                  options.listBuildingTypes != null;

            private void SetGridInput(bool enabled)
            {
                modes?.SetInteractable(enabled);
                categories?.SetInteractable(enabled);
                vehicles?.SetInteractable(enabled);
                buildings?.SetInteractable(enabled);
            }
        }
    }
}
