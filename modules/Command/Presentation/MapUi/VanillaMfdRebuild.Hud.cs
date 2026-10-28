using NOAvionics;
using System;
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

            // The HUD picks its mode from the active weapon station (HUDOptions.AutomaticToggle), on every weapon or safety change.
            private static readonly string[] ModeRules =
            {
                "Chosen while weapon safety is on, or when no store is active.",
                "Chosen when the active store is a gun.",
                "Chosen when the active store rates higher against air than against surface.",
                "Chosen when the active store rates higher against surface than against air.",
                "Chosen for an anti-radar store.",
                "Chosen for a store with no combat rating: cargo, troops or supply.",
            };

            private static readonly string[] ModeNotes =
            {
                "NAV: the priority set used with weapon safety on. Tap to switch the HUD to it.",
                "GUN: the priority set used when the active store is a gun. Tap to switch the HUD to it.",
                "A2A: the priority set used for stores rated best against air. Tap to switch the HUD to it.",
                "A2G: the priority set used for stores rated best against surface. Tap to switch the HUD to it.",
                "EW: the priority set used for anti-radar stores. Tap to switch the HUD to it.",
                "LOG: the priority set used for stores with no combat rating. Tap to switch the HUD to it.",
            };

            private static readonly AvIcon[] ModeGlyphs =
            {
                AvIcon.MapPin, AvIcon.Target, AvIcon.Plane, AvIcon.Flame, AvIcon.Antenna, AvIcon.Stack2
            };

            private static readonly string[] CategoryNotes =
            {
                "Gate: friendly contacts. Open gives full-size HUD markers, closed shrinks them to a mark.",
                "Gate: hostile contacts. Open gives full-size HUD markers, closed shrinks them to a mark.",
                "Gate: airborne tracks. Open gives full-size HUD markers, closed shrinks them to a mark.",
                "Gate: incoming missiles. Open gives full-size HUD markers, closed shrinks them to a mark.",
                "Gate: ground vehicles. Open gives full-size HUD markers, closed shrinks them to a mark.",
                "Gate: bases and structures. Open gives full-size HUD markers, closed shrinks them to a mark."
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
                "Civilian sites. Listed gives them full-size HUD markers; unlisted keeps them minimised.",
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

            private readonly AvRow[] modeBrief = new AvRow[4];
            private AvRow[] vehicleBrief, buildingBrief;

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

            protected override string Title => PageTitles[0];

            private static readonly string[] PageTitles =
                { "HUD SYMBOLOGY / MODE & GATES", "HUD SYMBOLOGY / GROUND PRIORITY", "HUD SYMBOLOGY / STRUCTURE PRIORITY" };

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

                // A page that ends above the footer prints these on its bay cover, most important first.
                Console.Orders(0,
                    "The HUD picks its mode itself whenever you change weapon or flip the safety.",
                    "Pick a mode by hand to edit its priority set. The next weapon change re-selects.",
                    "Each mode keeps its own gates and platform lists.");
                Console.Orders(1,
                    "Tap a tile to list or unlist that vehicle type.",
                    "AIR DEF and ARMOR keep one family and switch every other type off.",
                    "ALL lists every type; CLEAR minimises every vehicle on the HUD.");
                Console.Orders(2,
                    "Tap a tile to list or unlist that structure type.",
                    "STRIKE keeps radar, depots, hangars and munitions; MILITARY switches civilian sites off.",
                    "ALL lists every type, civilian sites included; CLEAR minimises every structure.");
            }

            protected override void OnPageChanged(int index)
            {
                Console.SetTitle(PageTitles[Mathf.Clamp(index, 0, PageTitles.Length - 1)]);
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

                // Why the HUD is in this mode and what the gates do to its markers.
                page.Section(AvIcon.InfoCircle, "MODE BRIEF", "WHAT THE HUD DRAWS");
                string[] names = { "ACTIVE STORE", "AUTO-SELECT", "PRIORITY SET", "MARKERS" };
                for (int i = 0; i < modeBrief.Length; i++)
                {
                    modeBrief[i] = page.Add(new AvRow(page.Content));
                    modeBrief[i].Set(names[i], "Waiting for the HUD.", "—", AvState.Inert);
                }
            }

            /// <summary>The MODE page brief for one mode. Takes plain values, so a layout preview can fill it without a HUD.</summary>
            private void SetModeBrief(int mode, string store, string safety, AvState safetyState, int gatesOpen, int gates)
            {
                string code = ((HUDOptions.HUDMode)mode).ToString(), rule = ModeRules[Mathf.Clamp(mode, 0, ModeRules.Length - 1)];
                modeBrief[0].Set("ACTIVE STORE", store, safety, safetyState);
                modeBrief[1].Set("AUTO-SELECT", rule, code, AvState.Info);
                modeBrief[2].Set("PRIORITY SET", code + " keeps its own gates and platform lists. Edits on these pages are saved to " +
                    code + " only.", "SAVED", AvState.Inert);
                modeBrief[3].Set("MARKERS", "Open gate and listed type: full-size marker within reach. Otherwise a minimised mark.",
                    gatesOpen + "/" + gates + " OPEN", gatesOpen == 0 ? AvState.Caution : AvState.Info);
            }

            /// <summary>The store the HUD follows: its name and whether its safety is on (a dash when it cannot be read).</summary>
            private static void ReadStore(out string store, out string safety, out AvState state)
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                WeaponStation station = hud == null ? null : hud.GetWeaponStation();
                if (hud == null || hud.aircraft == null || hud.aircraft.disabled)
                {
                    store = "No aircraft."; safety = "—"; state = AvState.Inert; return;
                }
                if (station == null || station.WeaponInfo == null)
                {
                    store = "No store selected."; safety = "—"; state = AvState.Inert; return;
                }
                string name = string.IsNullOrEmpty(station.WeaponInfo.weaponName) ? station.WeaponInfo.shortName : station.WeaponInfo.weaponName;
                store = string.IsNullOrEmpty(name) ? "UNNAMED STORE" : name.ToUpperInvariant();
                bool safe = station.SafetyIsOn(hud.aircraft);
                safety = safe ? "SAFE" : "ARMED";
                state = safe ? AvState.Inert : AvState.Caution;
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
                page.Section(AvIcon.Filter, "GROUND TYPES", "LISTED = FULL MARKER");
                vehicles =new MfdPagingGrid(page.Content, 3, 4, pager: true, rowHeight: 56f, tile: true);
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

                vehicleBrief = BuildBrief(page, "GROUND");
            }

            // ------------------------------------------------------------ buildings page

            private void BuildBuildingsPage(AvFlow page)
            {
                page.Section(AvIcon.BuildingBank, "STRUCTURE TYPES", "LISTED = FULL MARKER");
                buildings =new MfdPagingGrid(page.Content, 3, 3, pager: true, rowHeight: 56f, tile: true);
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
                civilianRing.Help = "Civilian sites: OFF keeps civilian buildings minimised on the HUD; ON gives them full-size markers.";
                page.Row(strikeRing, militaryRing, civilianRing);

                buildingBrief = BuildBrief(page, "STRUCTURE");
            }

            // ------------------------------------------------------------ priority brief

            private static AvRow[] BuildBrief(AvFlow page, string kind)
            {
                page.Section(AvIcon.InfoCircle, kind + " PRIORITY BRIEF", "FULL MARKER · MINIMISED");
                var rows = new AvRow[3];
                string[] names = { "FULL MARKER", "MINIMISED", "RULE" };
                for (int i = 0; i < rows.Length; i++)
                {
                    rows[i] = page.Add(new AvRow(page.Content));
                    rows[i].Set(names[i], "Waiting for the HUD.", "—", AvState.Inert);
                }
                return rows;
            }

            /// <summary>The listed types in words (the shorter of the listed and the unlisted names) and the standing rule.</summary>
            private void SetBrief(AvRow[] rows, List<HUDOptions_ToggleButton> types, string what)
            {
                int total = types.Count, listed = CountEnabled(types);
                FillBrief(rows, listed == 0 || listed == total ? "" : TypeWords(types, true),
                    listed == 0 || listed == total ? "" : TypeWords(types, false), listed, total, what, options.currentMode.ToString());
            }

            private void FillBrief(AvRow[] rows, string listedWords, string unlistedWords, int listed, int total, string what, string code)
            {
                rows[0].Set("FULL MARKER", listed == 0 ? "NONE LISTED. Every " + what + " is minimised."
                    : listed == total ? "EVERY " + what.ToUpperInvariant() + " TYPE" : listedWords, listed + "/" + total,
                    listed == 0 ? AvState.Caution : AvState.Ready);
                rows[1].Set("MINIMISED", listed == total ? "NONE. Every " + what + " type is listed."
                    : listed == 0 ? "EVERY " + what.ToUpperInvariant() + " TYPE" : unlistedWords,
                    AvNum.Fixed(total - listed, 0), listed == total ? AvState.Inert : AvState.Info);
                rows[2].Set("RULE", "Listed types get a full-size HUD marker within reach; unlisted types shrink to a minimised mark. Saved with the " +
                    code + " set.", code, AvState.Inert);
            }

            private string TypeWords(List<HUDOptions_ToggleButton> types, bool listed)
            {
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < types.Count; i++)
                {
                    if (types[i] == null || types[i].status != listed) continue;
                    if (text.Length > 0) text.Append(" · ");
                    text.Append(LabelFor(types[i], "TYPE"));
                }
                return text.ToString();
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

                    string store, safety;
                    AvState safetyState;
                    ReadStore(out store, out safety, out safetyState);
                    SetModeBrief((int)options.currentMode, store, safety, safetyState, activeGates, options.listCategories.Count);
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
                    SetBrief(vehicleBrief, options.listVehicleTypes, "vehicle");
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
                    SetBrief(buildingBrief, options.listBuildingTypes, "structure");
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
                        ? "HUD MODE " + options.currentMode
                        : selectedPage == 1 ? "GROUND TYPES · TAP TO TOGGLE" : "STRUCTURES · TAP TO TOGGLE";

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

            private void ToggleVehicle(int index) => ToggleType(false, index);
            private void ToggleBuilding(int index) => ToggleType(true, index);

            private List<HUDOptions_ToggleButton> TypeList(bool buildings) =>
                buildings ? options.listBuildingTypes : options.listVehicleTypes;

            private void ToggleType(bool buildings, int index)
            {
                if (!Ready) return;
                List<HUDOptions_ToggleButton> list = TypeList(buildings);
                if (index < 0 || index >= list.Count) return;
                HUDOptions_ToggleButton source = list[index];
                if (source == null) return;
                source.Set(!source.status);
                Persist();
            }

            private void SetAllVehicles(bool enabled) => SetTypes(false, i => enabled);
            private void SetAllBuildings(bool enabled) => SetTypes(true, i => enabled);

            private void SetVehicleFilter(params int[] activeIndices) => SetTypes(false, new HashSet<int>(activeIndices).Contains);
            private void SetBuildingFilter(params int[] activeIndices) => SetTypes(true, new HashSet<int>(activeIndices).Contains);

            private void SetTypes(bool buildings, Func<int, bool> on)
            {
                if (!Ready) return;
                List<HUDOptions_ToggleButton> list = TypeList(buildings);
                for (int i = 0; i < list.Count; i++)
                    list[i]?.Set(on(i));
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
