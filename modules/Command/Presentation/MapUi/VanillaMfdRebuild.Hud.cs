using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
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
            private int selectedPage;
            private AvChip[] chips;

            private MfdPagingGrid modes;
            private MfdPagingGrid categories;
            private AvReadout modeReadout;
            private AvRow gatesRow;
            private AvRow surfaceRow;

            private MfdPagingGrid vehicles;
            private AvControl vehAll, vehClear;
            private AvRow airDefRow, armorRow;

            private MfdPagingGrid buildings;
            private AvControl bldAll, bldClear;
            private AvRow strikeRow, civilianRow;

            public HudPresenter(MFDScreen screen, HUDOptions options)
                : base(screen, VanillaMfdPanelId.Hud)
            {
                this.options = options;
            }

            protected override string Title => "HUD PRIORITY MATRIX";

            protected override (AvIcon Icon, string Label)[] TabItems => new[]
            {
                (AvIcon.Focus2, "MODE"),
                (AvIcon.Filter, "VEHICLES"),
                (AvIcon.BuildingBank, "BUILDINGS"),
            };

            protected override void BuildContent()
            {
                chips = Console.Chips(3);
                BuildModePage(CreatePage());
                BuildVehiclesPage(CreatePage());
                BuildBuildingsPage(CreatePage());
            }

            protected override void OnPageChanged(int index)
            {
                selectedPage = index;
                RequestRefresh();
            }

            // ----------------------------------------------------------------- mode page

            private void BuildModePage(AvFlow page)
            {
                page.Section(AvIcon.Focus2, "ACTIVE HUD PROFILE", "PILOT DISPLAY");
                modeReadout = page.Add(new AvReadout(page.Content));
                gatesRow = page.Add(new AvRow(page.Content));
                surfaceRow = page.Add(new AvRow(page.Content));

                page.Section(AvIcon.Target, "ENGAGEMENT MODE", "SELECT ONE");
                modes = new MfdPagingGrid(page.Content, 2, 3, pager: false);
                AddGrid(page, modes);

                page.Section(AvIcon.Filter, "PRIORITY GATES", "MAXIMISE TRACKS");
                categories = new MfdPagingGrid(page.Content, 2, 3, pager: false);
                AddGrid(page, categories);
            }

            // ------------------------------------------------------------- vehicles page

            private void BuildVehiclesPage(AvFlow page)
            {
                page.Section(AvIcon.Filter, "VEHICLE PRIORITY", "FILTER MATRIX · MULTI-SELECT");
                vehicles = new MfdPagingGrid(page.Content, 2, 5, pager: false);
                AddGrid(page, vehicles);

                AvButtons row = page.Buttons(
                    new AvControl.Spec("ALL ON", () => SetAllVehicles(true)),
                    new AvControl.Spec("AIR DEF", () => SetVehicleFilter(6, 7, 8, 9)),
                    new AvControl.Spec("ARMOR", () => SetVehicleFilter(2, 3, 4)),
                    new AvControl.Spec("CLEAR", () => SetAllVehicles(false)));
                vehAll = row.Controls[0];
                vehClear = row.Controls[3];
                vehAll.Help = "Highlight all 10 vehicle types on HUD.";
                row.Controls[1].Help = "Filter for air defense threats only: AAA, IR SAM, R SAM, RDR.";
                row.Controls[2].Help = "Filter for armored targets: MBT, AFV, LCV.";
                vehClear.Help = "Deselect all vehicle priority filters.";

                page.Section(AvIcon.Target, "SURFACE THREAT SUMMARY", null);
                airDefRow = page.Add(new AvRow(page.Content));
                armorRow = page.Add(new AvRow(page.Content));
            }

            // ------------------------------------------------------------ buildings page

            private void BuildBuildingsPage(AvFlow page)
            {
                page.Section(AvIcon.BuildingBank, "BUILDING PRIORITY", "FILTER MATRIX · MULTI-SELECT");
                buildings = new MfdPagingGrid(page.Content, 2, 4, pager: false);
                AddGrid(page, buildings);

                AvButtons row = page.Buttons(
                    new AvControl.Spec("ALL ON", () => SetAllBuildings(true)),
                    new AvControl.Spec("STRIKE", () => SetBuildingFilter(2, 3, 4, 6)),
                    new AvControl.Spec("MILITARY", () => SetBuildingFilter(1, 2, 3, 4, 5, 6)),
                    new AvControl.Spec("CLEAR", () => SetAllBuildings(false)));
                bldAll = row.Controls[0];
                bldClear = row.Controls[3];
                bldAll.Help = "Highlight all 7 building types on HUD.";
                row.Controls[1].Help = "Filter for strategic strike targets: RDR, DEP, HGR, AMMO.";
                row.Controls[2].Help = "Prioritise all military installations; exclude civilian.";
                bldClear.Help = "Deselect all building priority filters.";

                page.Section(AvIcon.Target, "STRUCTURE TARGET SUMMARY", null);
                strikeRow = page.Add(new AvRow(page.Content));
                civilianRow = page.Add(new AvRow(page.Content));
            }

            // ------------------------------------------------------------- refresh

            protected override void RefreshContent()
            {
                if (options == null || options.listModes == null || options.listCategories == null ||
                    options.listVehicleTypes == null || options.listBuildingTypes == null)
                {
                    SetGridInput(false);
                    chips[0].Set("LINK", AvState.Inert);
                    chips[1].Set("DATA", AvState.Inert);
                    chips[2].Set("—", AvState.Inert);
                    return;
                }

                SetGridInput(true);
                int activeVeh = CountEnabled(options.listVehicleTypes);
                int activeBld = CountEnabled(options.listBuildingTypes);
                int activeGates = CountCategories(options.listCategories);

                chips[0].Set(options.currentMode.ToString(), AvState.Ready);
                chips[1].Set(activeVeh + "/" + options.listVehicleTypes.Count + " VEH", AvState.Ready);
                chips[2].Set(activeBld + "/" + options.listBuildingTypes.Count + " BLD", AvState.Ready);

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

                modeReadout.Set(brief, "", "AUTO SELECT / CURRENT MODE");
                gatesRow.Set("GATES", "MAXIMISE TRACKS", activeGates + " OF " + options.listCategories.Count, AvState.Info);
                surfaceRow.Set("SURFACE", null, activeVeh + " VEH · " + activeBld + " BLD", AvState.Info);

                if (selectedPage == 0)
                {
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
                }

                if (selectedPage == 1)
                {
                    vehicles.SetData(options.listVehicleTypes.Count,
                        i => LabelFor(options.listVehicleTypes[i], "VEHICLE"),
                        i => options.listVehicleTypes[i] != null && options.listVehicleTypes[i].status,
                        ToggleVehicle,
                        icons: i => DefinitionIcon(options.listVehicleTypes[i]),
                        subs: i => i < VehicleNotes.Length ? VehicleNotes[i] : null);

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

                    vehAll.Interactable = activeVeh < options.listVehicleTypes.Count;
                    vehClear.Interactable = activeVeh > 0;
                    airDefRow.Set("AIR DEFENSE", null, airDefCount + " OF 4 ACTIVE" + (airDefCount == 4 ? " (FULL)" : ""),
                        airDefCount > 0 ? AvState.Ready : AvState.Inert);
                    armorRow.Set("ARMORED TARGETS", null, armorCount + " OF 3 ACTIVE",
                        armorCount > 0 ? AvState.Ready : AvState.Inert);
                }

                if (selectedPage == 2)
                {
                    buildings.SetData(options.listBuildingTypes.Count,
                        i => LabelFor(options.listBuildingTypes[i], "BUILDING"),
                        i => options.listBuildingTypes[i] != null && options.listBuildingTypes[i].status,
                        ToggleBuilding,
                        icons: i => DefinitionIcon(options.listBuildingTypes[i]),
                        subs: i => i < BuildingNotes.Length ? BuildingNotes[i] : null);

                    int strikeCount = 0;
                    int[] strikeIndices = { 2, 3, 4, 6 };
                    foreach (int idx in strikeIndices)
                        if (idx < options.listBuildingTypes.Count && options.listBuildingTypes[idx] != null && options.listBuildingTypes[idx].status)
                            strikeCount++;

                    bool civActive = options.listBuildingTypes.Count > 0 && options.listBuildingTypes[0] != null && options.listBuildingTypes[0].status;
                    bldAll.Interactable = activeBld < options.listBuildingTypes.Count;
                    bldClear.Interactable = activeBld > 0;
                    strikeRow.Set("STRIKE TARGETS", null, strikeCount + " OF 4 ACTIVE",
                        strikeCount > 0 ? AvState.Ready : AvState.Inert);
                    civilianRow.Set("CIVILIAN ASSETS", null, civActive ? "ACTIVE (COLLATERAL RISK)" : "OFF (PROTECTED)",
                        civActive ? AvState.Caution : AvState.Inert);
                }
            }

            protected override string AmbientStatus() =>
                options == null
                    ? "HUD MATRIX UNAVAILABLE"
                    : selectedPage == 0
                        ? "HUD " + options.currentMode + " — SELECT ENGAGEMENT PROFILE & PRIORITY GATES"
                        : selectedPage == 1
                            ? "VEHICLES — TAP TO TOGGLE • USE PRESETS FOR QUICK MISSION LOADOUTS"
                            : "BUILDINGS — TAP TO TOGGLE • USE PRESETS FOR TARGET SELECTION";

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
