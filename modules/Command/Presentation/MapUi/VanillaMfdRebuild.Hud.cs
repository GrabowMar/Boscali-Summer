using NOAvionics;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // --------------------------------------------------------------- HUD panel

        /// <summary>
        /// The MODE page head on ONE line: the active mode's name on the left, then the gates ring and the
        /// types ring (replaces the old readout, two hazard bars and the header chips).
        /// </summary>
        private sealed class HudHeader : AvPart
        {
            private const float RingSlot = 84f;
            private readonly TMP_Text name, sub;

            public HudHeader(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "HudHeader");
                name = AvText.Make(Rect, "Mode", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
                sub = AvText.Make(Rect, "Sub", AvTextRole.Micro, "// HUD MODE", TextAlignmentOptions.TopLeft, true);
                Gates = new AvGauge(Rect, "GATES");
                Types = new AvGauge(Rect, "TYPES");
                Restyle();
            }

            public AvGauge Gates { get; }
            public AvGauge Types { get; }

            public void SetMode(string mode)
            {
                if (name.text != (mode ?? "")) name.text = mode ?? "";
            }

            public override float Measure(float width) => Gates.Measure(RingSlot);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float tw = Mathf.Max(40f, s.W - 2f * RingSlot - 6f);
                float nh = AvText.Height(name, tw), sh = AvText.Height(sub, tw);
                float y0 = Mathf.Max(0f, (s.H - nh - 2f - sh) * 0.5f);
                AvLay.Place(name.rectTransform, 0f, y0, tw, nh);
                AvLay.Place(sub.rectTransform, 0f, y0 + nh + 2f, tw, sh);
                Gates.Place(new AvSlot(s.W - 2f * RingSlot, 0f, RingSlot, s.H));
                Types.Place(new AvSlot(s.W - RingSlot, 0f, RingSlot, s.H));
            }

            public override void Restyle()
            {
                name.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
                sub.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                Gates.Restyle();
                Types.Restyle();
            }
        }

        private sealed class HudPresenter : Presenter
        {

            private static readonly string[] ModeNotes =
            {
                "NAV: routes and waypoints on the HUD. Tap to make it the active mode.",
                "GUN: gun pipper and lead cue. Tap to make it the active mode.",
                "A2A: air intercept cues and tracks. Tap to make it the active mode.",
                "A2G: ground attack cues and targets. Tap to make it the active mode.",
                "EW: emitters and jamming. Tap to make it the active mode.",
                "LOG: transport and logistics routing. Tap to make it the active mode."
            };

            private static readonly AvIcon[] ModeGlyphs =
            {
                AvIcon.MapPin, AvIcon.Target, AvIcon.Plane, AvIcon.Flame, AvIcon.Antenna, AvIcon.Stack2
            };

            private static readonly string[] CategoryNotes =
            {
                "Gate: friendly contacts. Tap to draw or hide them on the HUD.",
                "Gate: hostile contacts. Tap to draw or hide them on the HUD.",
                "Gate: airborne tracks. Tap to draw or hide them on the HUD.",
                "Gate: incoming missiles. Tap to draw or hide them on the HUD.",
                "Gate: ground vehicles. Tap to draw or hide them on the HUD.",
                "Gate: bases and structures. Tap to draw or hide them on the HUD."
            };

            private static readonly string[] VehicleNotes =
            {
                "Supply trucks. Tap to toggle them in the HUD priority list.",
                "Unmanned vehicles. Tap to toggle them in the HUD priority list.",
                "Light combat armor. Tap to toggle it in the HUD priority list.",
                "Armored vehicles. Tap to toggle them in the HUD priority list.",
                "Heavy tanks. Tap to toggle them in the HUD priority list.",
                "Field artillery. Tap to toggle it in the HUD priority list.",
                "Anti-air guns. Tap to toggle them in the HUD priority list.",
                "Heat-seeking SAMs. Tap to toggle them in the HUD priority list.",
                "Radar-guided SAMs. Tap to toggle them in the HUD priority list.",
                "Search radars. Tap to toggle them in the HUD priority list."
            };

            private static readonly string[] BuildingNotes =
            {
                "Civilian sites. Turning them on lets the HUD mark civilians as targets.",
                "Industrial plants. Tap to toggle them in the HUD priority list.",
                "Early warning radar. Tap to toggle it in the HUD priority list.",
                "Fuel and supply depots. Tap to toggle them in the HUD priority list.",
                "Hangars and shelters. Tap to toggle them in the HUD priority list.",
                "Fortifications. Tap to toggle them in the HUD priority list.",
                "Munitions stores. Tap to toggle them in the HUD priority list."
            };

            private readonly HUDOptions options;
            private readonly Dictionary<HUDOptions_ToggleButton, string> labels =
                new Dictionary<HUDOptions_ToggleButton, string>();
            private int selectedPage;

            private MfdPagingGrid modes;
            private MfdPagingGrid categories;
            private HudHeader modeHeader;
            private AvGauge gatesRing;
            private AvGauge typesRing;

            private MfdPagingGrid vehicles;
            private AvControl vehAll, vehClear;
            private AvGauge airDefRing, armorRing, shownVehRing;

            private MfdPagingGrid buildings;
            private AvControl bldAll, bldClear;
            private AvGauge strikeRing, militaryRing, civilianRing;

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

            protected override string[] TabTips { get; } = new[]
            {
                "Mode: pick the HUD mode and choose which contact gates the HUD draws. The counters summarise both.",
                "Vehicles: choose which ground vehicle types get HUD priority marks, singly or with the air-defence and armor presets.",
                "Buildings: choose which building types get HUD priority marks, singly or with the strike and military presets.",
            };

            protected override void BuildContent()
            {
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
                modeHeader = page.Add(new HudHeader(page.Content));
                gatesRing = modeHeader.Gates;
                typesRing = modeHeader.Types;
                gatesRing.Help = "Gates open: how many of the six contact categories the HUD draws. Toggle them in the GATES cells below.";
                typesRing.Help = "Types shown: vehicle and building types currently on the HUD priority list. Set them on the VEHICLES and BUILDINGS pages.";

                page.Section(AvIcon.Target, "MODE", "PICK ONE");
                modes = new MfdPagingGrid(page.Content, 3, 2, pager: false, rowHeight: 56f, tile: true);
                modes.SetGlyphs(i => i >= 0 && i < ModeGlyphs.Length ? ModeGlyphs[i] : AvIcon.Focus2);
                AddGrid(page, modes);

                page.Section(AvIcon.Filter, "GATES", "TOGGLE ANY");
                categories = new MfdPagingGrid(page.Content, 3, 2, pager: false, rowHeight: 56f, tile: true);
                categories.SetGlyphs(i => GateGlyph(options != null && options.listCategories != null && i < options.listCategories.Count
                    ? NativeCategoryLabel(options.listCategories[i], i) : ""));
                AddGrid(page, categories);

            }

            private static AvIcon GateGlyph(string label)
            {
                switch (label)
                {
                    case "FRIENDLY": return AvIcon.Shield;
                    case "ENEMY": return AvIcon.Skull;
                    case "AIRCRAFT": return AvIcon.Plane;
                    case "MISSILES": return AvIcon.ArrowUpRight;
                    case "VEHICLES": return AvIcon.ChartArrows;
                    case "BUILDINGS": return AvIcon.BuildingBank;
                    case "SHIPS": return AvIcon.Flag;
                    default: return AvIcon.Filter;
                }
            }

            // ------------------------------------------------------------- vehicles page

            private void BuildVehiclesPage(AvFlow page)
            {
                vehicles = new MfdPagingGrid(page.Content, 3, 4, pager: true, rowHeight: 56f, tile: true);
                vehicles.SetGlyphs(i => VehicleGlyph(
                    options != null && options.listVehicleTypes != null && i >= 0 && i < options.listVehicleTypes.Count
                        ? LabelFor(options.listVehicleTypes[i], "") : "", i));
                AddGrid(page, vehicles);

                AvButtons row = page.Buttons(
                    new AvControl.Spec("ALL", () => SetAllVehicles(true), AvButtonStyle.Default, AvIcon.CircleCheck),
                    new AvControl.Spec("AIR DEF", () => SetVehicleFilter(6, 7, 8, 9), AvButtonStyle.Default, AvIcon.Radar2),
                    new AvControl.Spec("ARMOR", () => SetVehicleFilter(2, 3, 4), AvButtonStyle.Default, AvIcon.Shield),
                    new AvControl.Spec("CLEAR", () => SetAllVehicles(false), AvButtonStyle.Default, AvIcon.Eraser));
                vehAll = row.Controls[0];
                vehClear = row.Controls[3];
                vehAll.Help = "All: draw every one of the 10 vehicle types on the HUD priority list.";
                row.Controls[1].Help = "Air defence: keep only AAA, IR SAM, R SAM and RADAR, and switch every other vehicle type off.";
                row.Controls[2].Help = "Armor: keep only LCV, AFV and MBT, and switch every other vehicle type off.";
                vehClear.Help = "Clear: switch off every vehicle type. The HUD draws no vehicle priority marks.";

                airDefRing = new AvGauge(page.Content, "AIR DEF");
                armorRing = new AvGauge(page.Content, "ARMOR");
                shownVehRing = new AvGauge(page.Content, "SHOWN");
                airDefRing.Help = "Air defence coverage: how many of AAA, IR SAM, R SAM and RADAR are on (4 = full set).";
                armorRing.Help = "Armor coverage: how many of LCV, AFV and MBT are on (3 = full set).";
                shownVehRing.Help = "Vehicle types on the HUD priority list out of all ten.";
                page.Row(airDefRing, armorRing, shownVehRing);

            }

            // ------------------------------------------------------------ buildings page

            private void BuildBuildingsPage(AvFlow page)
            {
                buildings = new MfdPagingGrid(page.Content, 3, 3, pager: true, rowHeight: 56f, tile: true);
                buildings.SetGlyphs(i => BuildingGlyph(
                    options != null && options.listBuildingTypes != null && i >= 0 && i < options.listBuildingTypes.Count
                        ? LabelFor(options.listBuildingTypes[i], "") : "", i));
                AddGrid(page, buildings);

                AvButtons row = page.Buttons(
                    new AvControl.Spec("ALL", () => SetAllBuildings(true), AvButtonStyle.Default, AvIcon.CircleCheck),
                    new AvControl.Spec("STRIKE", () => SetBuildingFilter(2, 3, 4, 6), AvButtonStyle.Default, AvIcon.Target),
                    new AvControl.Spec("MILITARY", () => SetBuildingFilter(1, 2, 3, 4, 5, 6), AvButtonStyle.Default, AvIcon.Shield),
                    new AvControl.Spec("CLEAR", () => SetAllBuildings(false), AvButtonStyle.Default, AvIcon.Eraser));
                bldAll = row.Controls[0];
                bldClear = row.Controls[3];
                bldAll.Help = "All: draw every one of the 7 building types on the HUD priority list, civilians included.";
                row.Controls[1].Help = "Strike: keep only RDR, DEP, HGR and AMMO, the strategic strike targets, and switch the rest off.";
                row.Controls[2].Help = "Military: keep every military installation and switch civilian sites off.";
                bldClear.Help = "Clear: switch off every building type. The HUD draws no building priority marks.";

                strikeRing = new AvGauge(page.Content, "STRIKE");
                militaryRing = new AvGauge(page.Content, "MILITARY");
                civilianRing = new AvGauge(page.Content, "CIVILIANS");
                strikeRing.Help = "Strike coverage: how many of RDR, DEP, HGR and AMMO are on (4 = full set).";
                militaryRing.Help = "Military coverage: how many of the six military building types are on.";
                civilianRing.Help = "Civilian sites: OFF keeps civilians protected from HUD target marks; ON lets the HUD mark them.";
                page.Row(strikeRing, militaryRing, civilianRing);

            }

            // ------------------------------------------------------------- refresh

            protected override void RefreshContent()
            {
                if (options == null || options.listModes == null || options.listCategories == null ||
                    options.listVehicleTypes == null || options.listBuildingTypes == null)
                {
                    SetGridInput(false);
                    modeHeader.SetMode("HUD LINK LOST");
                    gatesRing.Set(0f, "—", AvState.Inert);
                    typesRing.Set(0f, "—", AvState.Inert);
                    return;
                }

                SetGridInput(true);
                int activeVeh = CountEnabled(options.listVehicleTypes);
                int activeBld = CountEnabled(options.listBuildingTypes);
                int activeGates = CountCategories(options.listCategories);

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

                modeHeader.SetMode(brief);
                int gateTotal = Mathf.Max(1, options.listCategories.Count);
                gatesRing.Set(activeGates / (float)gateTotal, activeGates + "/" + options.listCategories.Count, AvState.Info);
                int typeTotal = Mathf.Max(1, options.listVehicleTypes.Count + options.listBuildingTypes.Count);
                typesRing.Set((activeVeh + activeBld) / (float)typeTotal,
                    (activeVeh + activeBld) + "/" + typeTotal, AvState.Info);

                if (selectedPage == 0)
                {
                    modes.SetData(options.listModes.Count,
                        i => i < 6 ? ((HUDOptions.HUDMode)i).ToString() : LabelFor(options.listModes[i], "MODE"),
                        i => options.listModes[i] != null && options.listModes[i].status,
                        SelectMode,
                        details: i => i < ModeNotes.Length ? ModeNotes[i] : null);

                    categories.SetData(options.listCategories.Count,
                        i => NativeCategoryLabel(options.listCategories[i], i),
                        i => options.listCategories[i] != null && options.listCategories[i].maximized,
                        ToggleCategory,
                        details: i => i < CategoryNotes.Length ? CategoryNotes[i] : null);
                }

                if (selectedPage == 1)
                {
                    vehicles.SetData(options.listVehicleTypes.Count,
                        i => LabelFor(options.listVehicleTypes[i], "VEHICLE"),
                        i => options.listVehicleTypes[i] != null && options.listVehicleTypes[i].status,
                        ToggleVehicle,
                        icons: i => DefinitionIcon(options.listVehicleTypes[i]),
                        details: i => i < VehicleNotes.Length ? VehicleNotes[i] : null);

                    int airDefCount = CountAt(options.listVehicleTypes, 6, 7, 8, 9);
                    int armorCount = CountAt(options.listVehicleTypes, 2, 3, 4);

                    vehAll.Interactable = activeVeh < options.listVehicleTypes.Count;
                    vehClear.Interactable = activeVeh > 0;
                    airDefRing.Set(airDefCount / 4f, airDefCount + "/4", airDefCount > 0 ? AvState.Ready : AvState.Inert);
                    armorRing.Set(armorCount / 3f, armorCount + "/3", armorCount > 0 ? AvState.Ready : AvState.Inert);
                    int vehTotal = Mathf.Max(1, options.listVehicleTypes.Count);
                    shownVehRing.Set(activeVeh / (float)vehTotal, activeVeh + "/" + options.listVehicleTypes.Count,
                        activeVeh > 0 ? AvState.Ready : AvState.Inert);
                }

                if (selectedPage == 2)
                {
                    buildings.SetData(options.listBuildingTypes.Count,
                        i => LabelFor(options.listBuildingTypes[i], "BUILDING"),
                        i => options.listBuildingTypes[i] != null && options.listBuildingTypes[i].status,
                        ToggleBuilding,
                        icons: i => DefinitionIcon(options.listBuildingTypes[i]),
                        details: i => i < BuildingNotes.Length ? BuildingNotes[i] : null);

                    int strikeCount = CountAt(options.listBuildingTypes, 2, 3, 4, 6);
                    int militaryCount = CountAt(options.listBuildingTypes, 1, 2, 3, 4, 5, 6);

                    bool civActive = options.listBuildingTypes.Count > 0 && options.listBuildingTypes[0] != null && options.listBuildingTypes[0].status;
                    bldAll.Interactable = activeBld < options.listBuildingTypes.Count;
                    bldClear.Interactable = activeBld > 0;
                    strikeRing.Set(strikeCount / 4f, strikeCount + "/4", strikeCount > 0 ? AvState.Ready : AvState.Inert);
                    militaryRing.Set(militaryCount / 6f, militaryCount + "/6", militaryCount > 0 ? AvState.Ready : AvState.Inert);
                    civilianRing.Set(civActive ? 1f : 0f, civActive ? "ON" : "OFF", civActive ? AvState.Caution : AvState.Inert);
                }
            }

            private static int CountAt(List<HUDOptions_ToggleButton> list, params int[] indices)
            {
                int count = 0;
                foreach (int idx in indices)
                    if (idx < list.Count && list[idx] != null && list[idx].status) count++;
                return count;
            }

            protected override string AmbientStatus() =>
                options == null
                    ? "HUD MATRIX UNAVAILABLE"
                    : selectedPage == 0
                        ? "HUD " + options.currentMode
                        : selectedPage == 1 ? "VEHICLES • TAP TO TOGGLE" : "BUILDINGS • TAP TO TOGGLE";

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
