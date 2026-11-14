using NOAvionics;
using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // --------------------------------------------------------- HUD priority

        /// <summary>
        /// The HUD's own priority lists (the game's HUDOptions, once the HUD rail key) as TGT FILTER's last sections: the mode
        /// whose set you edit, the six contact gates, and the ground and structure types that get a full-size HUD marker. Every
        /// press goes to HUDOptions and is saved with that mode's set. The HUD screen itself stays: MfdBezel lays mod screens on it.
        /// </summary>
        private sealed class HudPriority
        {
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

            private readonly Action changed;
            private readonly Dictionary<HUDOptions_ToggleButton, string> labels = new Dictionary<HUDOptions_ToggleButton, string>();
            private readonly AvSection modeSection, vehicleSection, buildingSection;
            private readonly MfdPagingGrid modes, gates, vehicles, buildings;
            private readonly AvControl vehAll, vehClear, bldAll, bldClear;

            /// <param name="changed">Called after every press, so the host refreshes at once.</param>
            public HudPriority(AvFlow page, Action changed)
            {
                this.changed = changed;
                modeSection = page.Section(AvIcon.Focus2, "HUD MODE", "THE SET YOU EDIT");
                // Six to a row leaves no room for an ON word beside the glyph: lit is the state, as on the filter's own grids.
                modes = AddGrid(page, new MfdPagingGrid(page.Content, 6, 1, pager: false, rowHeight: 56f, tile: true, words: false));
                modes.SetGlyphs(i => i >= 0 && i < ModeGlyphs.Length ? ModeGlyphs[i] : AvIcon.Focus2);

                page.Section(AvIcon.Filter, "HUD GATES", "LIT = FULL MARKER");
                gates = AddGrid(page, new MfdPagingGrid(page.Content, 6, 1, pager: false, rowHeight: 56f, tile: true, words: false));
                gates.SetGlyphs(i =>
                {
                    HUDOptions o = Options;
                    return GateGlyph(o != null && o.listCategories != null && i < o.listCategories.Count
                        ? NativeCategoryLabel(o.listCategories[i], i) : "");
                });

                vehicleSection = page.Section(AvIcon.ChartArrows, "HUD GROUND TYPES", "LISTED = FULL MARKER");
                vehicles = AddGrid(page, new MfdPagingGrid(page.Content, 5, 2, pager: true, rowHeight: 56f, tile: true));
                vehicles.SetGlyphs(i => VehicleGlyph(TypeLabel(false, i), i));
                AvControl[] vrow = page.Buttons(
                    new AvControl.Spec("ALL", () => SetTypes(false, i => true), AvButtonStyle.Default, AvIcon.CircleCheck),
                    new AvControl.Spec("AIR DEF", () => SetTypes(false, Only(6, 7, 8, 9)), AvButtonStyle.Default, AvIcon.Radar2),
                    new AvControl.Spec("ARMOR", () => SetTypes(false, Only(2, 3, 4)), AvButtonStyle.Default, AvIcon.Shield),
                    new AvControl.Spec("CLEAR", () => SetTypes(false, i => false), AvButtonStyle.Default, AvIcon.Eraser)).Controls;
                vehAll = vrow[0];
                vehClear = vrow[3];
                vehAll.Help = "All: draw every vehicle type on the HUD priority list.";
                vrow[1].Help = "Air defence: keep only AAA, IR SAM, R SAM and RADAR, and switch every other vehicle type off.";
                vrow[2].Help = "Armor: keep only LCV, AFV and MBT, and switch every other vehicle type off.";
                vehClear.Help = "Clear: switch off every vehicle type. The HUD draws no vehicle priority marks.";

                buildingSection = page.Section(AvIcon.BuildingBank, "HUD STRUCTURE TYPES", "LISTED = FULL MARKER");
                buildings = AddGrid(page, new MfdPagingGrid(page.Content, 4, 2, pager: true, rowHeight: 56f, tile: true));
                buildings.SetGlyphs(i => BuildingGlyph(TypeLabel(true, i), i));
                AvControl[] brow = page.Buttons(
                    new AvControl.Spec("ALL", () => SetTypes(true, i => true), AvButtonStyle.Default, AvIcon.CircleCheck),
                    new AvControl.Spec("STRIKE", () => SetTypes(true, Only(2, 3, 4, 6)), AvButtonStyle.Default, AvIcon.Target),
                    new AvControl.Spec("MILITARY", () => SetTypes(true, Only(1, 2, 3, 4, 5, 6)), AvButtonStyle.Default, AvIcon.Shield),
                    new AvControl.Spec("CLEAR", () => SetTypes(true, i => false), AvButtonStyle.Default, AvIcon.Eraser)).Controls;
                bldAll = brow[0];
                bldClear = brow[3];
                bldAll.Help = "All: draw every building type on the HUD priority list, civilians included.";
                brow[1].Help = "Strike: keep only RDR, DEP, HGR and AMMO, the strategic strike targets, and switch the rest off.";
                brow[2].Help = "Military: keep every military installation and switch civilian sites off.";
                bldClear.Help = "Clear: switch off every building type. The HUD draws no building priority marks.";
            }

            private static HUDOptions Options => SceneSingleton<HUDOptions>.i;

            private static bool Ready(HUDOptions o) => o != null && o.listModes != null && o.listCategories != null &&
                                                        o.listVehicleTypes != null && o.listBuildingTypes != null;

            private static Func<int, bool> Only(params int[] indices) => new HashSet<int>(indices).Contains;

            public void Refresh()
            {
                HUDOptions o = Options;
                bool ready = Ready(o);
                modes.SetInteractable(ready);
                gates.SetInteractable(ready);
                vehicles.SetInteractable(ready);
                buildings.SetInteractable(ready);
                if (!ready)
                {
                    modeSection.SetCaption("WAITING FOR THE HUD");
                    return;
                }

                modeSection.SetCaption(o.currentMode + " · PICKED BY YOUR WEAPON · TAP TO EDIT ANOTHER");
                modes.SetData(o.listModes.Count,
                    i => i < 6 ? ((HUDOptions.HUDMode)i).ToString() : LabelFor(o.listModes[i], "MODE"),
                    i => o.listModes[i] != null && o.listModes[i].status, SelectMode,
                    details: i => i < ModeNotes.Length ? ModeNotes[i] : null);
                gates.SetData(o.listCategories.Count,
                    i => NativeCategoryLabel(o.listCategories[i], i),
                    i => o.listCategories[i] != null && o.listCategories[i].maximized, ToggleGate,
                    details: i => i < CategoryNotes.Length ? CategoryNotes[i] : null);
                vehicles.SetData(o.listVehicleTypes.Count,
                    i => LabelFor(o.listVehicleTypes[i], "VEHICLE"),
                    i => o.listVehicleTypes[i] != null && o.listVehicleTypes[i].status, i => ToggleType(false, i),
                    icons: i => DefinitionIcon(o.listVehicleTypes[i]),
                    details: i => i < VehicleNotes.Length ? VehicleNotes[i] : null);
                buildings.SetData(o.listBuildingTypes.Count,
                    i => LabelFor(o.listBuildingTypes[i], "BUILDING"),
                    i => o.listBuildingTypes[i] != null && o.listBuildingTypes[i].status, i => ToggleType(true, i),
                    icons: i => DefinitionIcon(o.listBuildingTypes[i]),
                    details: i => i < BuildingNotes.Length ? BuildingNotes[i] : null);

                int veh = CountEnabled(o.listVehicleTypes), bld = CountEnabled(o.listBuildingTypes);
                vehicleSection.SetCaption(veh + "/" + o.listVehicleTypes.Count + " LISTED IN " + o.currentMode);
                buildingSection.SetCaption(bld + "/" + o.listBuildingTypes.Count + " LISTED IN " + o.currentMode);
                vehAll.Interactable = veh < o.listVehicleTypes.Count;
                vehClear.Interactable = veh > 0;
                bldAll.Interactable = bld < o.listBuildingTypes.Count;
                bldClear.Interactable = bld > 0;
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

            private string TypeLabel(bool buildings, int i)
            {
                HUDOptions o = Options;
                List<HUDOptions_ToggleButton> list = o == null ? null : buildings ? o.listBuildingTypes : o.listVehicleTypes;
                return list != null && i >= 0 && i < list.Count ? LabelFor(list[i], "") : "";
            }

            private void SelectMode(int index)
            {
                HUDOptions o = Options;
                if (!Ready(o) || index < 0 || index >= o.listModes.Count || o.listModes[index] == null) return;
                o.ToggleButtons(o.listModes[index]);
                if (index < 6) o.currentMode = (HUDOptions.HUDMode)index;
                Persist(o);
            }

            private void ToggleGate(int index)
            {
                HUDOptions o = Options;
                if (!Ready(o) || index < 0 || index >= o.listCategories.Count || o.listCategories[index] == null) return;
                o.listCategories[index].Set(!o.listCategories[index].maximized);
                Persist(o);
            }

            private void ToggleType(bool buildings, int index)
            {
                HUDOptions o = Options;
                if (!Ready(o)) return;
                List<HUDOptions_ToggleButton> list = buildings ? o.listBuildingTypes : o.listVehicleTypes;
                if (index < 0 || index >= list.Count || list[index] == null) return;
                list[index].Set(!list[index].status);
                Persist(o);
            }

            private void SetTypes(bool buildings, Func<int, bool> on)
            {
                HUDOptions o = Options;
                if (!Ready(o)) return;
                List<HUDOptions_ToggleButton> list = buildings ? o.listBuildingTypes : o.listVehicleTypes;
                for (int i = 0; i < list.Count; i++) list[i]?.Set(on(i));
                Persist(o);
            }

            private void Persist(HUDOptions o)
            {
                o.SaveSettings();
                o.NeedUpdateIcons();
                changed();
            }

            private static UnityEngine.Sprite DefinitionIcon(HUDOptions_ToggleButton button) =>
                button == null || button.listDefinitions == null || button.listDefinitions.Count == 0 ||
                button.listDefinitions[0] == null ? null : button.listDefinitions[0].mapIcon;

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
        }
    }
}
