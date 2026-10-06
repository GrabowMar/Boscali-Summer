using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ------------------------------------------------------------ target panel

        private sealed class TargetPresenter : Presenter
        {
            private enum EditMode { None, SaveAs, Rename }

            private readonly TargetListSelector selector;
            private readonly List<Unit> selectedUnits = new List<Unit>();
            private readonly List<TargetPresetSnapshot> catalog = new List<TargetPresetSnapshot>();

            private AvEqualizer rangeProfile;
            private readonly float[] rangeBins = new float[20];
            private AvEqualizer trackedProfile;
            private readonly float[] trackedBars = new float[16];
            private AvEqualizer cameraLife;
            private readonly float[] cameraLifeBars = new float[12];
            private MfdPagingGrid factionGrid;
            private MfdPagingGrid unitGrid;
            private MfdPagingGrid vehicleGrid;
            private AvGauge[] filterMetrics;
            private MfdPagingGrid selectedGrid;
            private MfdPagingGrid candidateGrid;
            private readonly List<TargetCandidate> candidates = new List<TargetCandidate>(128);
            private Unit candidateFocus;
            private float nextCandidateScan;
            private AvControl missilePreference;
            private AvControl weaponPreference;
            private AvControl airPreference;
            private AvControl rangePreference;
            private AvControl designateCandidate;
            private AvSlab focusSlab;
            private AvHazardBar focusRange;
            private AvControl nextCandidate;
            private AvControl incomingCandidate;
            private AvSection contactsSection;
            private readonly List<Unit>[] targetGroups =
                { new List<Unit>(16), new List<Unit>(16), new List<Unit>(16) };
            private MfdPagingGrid groupGrid;
            private MfdPagingGrid quickGrid;
            private MfdPagingGrid presetGrid;
            private AvControl resetFilters;
            private AvControl clearTargets;
            private AvControl followHud;
            private AvControl laser;
            private AvControl saveAs;
            private AvControl updatePreset;
            private AvControl renamePreset;
            private AvControl deletePreset;
            private AvReadout presetReadout;
            private AvSection presetSection;
            private AvSection selectedSection;
            private PresetEditor editorPart;

            private string activePreset = MfdTargetPresets.Names[0];
            private string selectedPreset = "";
            private string echo;
            private float echoUntil;
            private string confirmDelete;
            private float confirmDeleteUntil;
            private int catalogVersion = -1;
            private int catalogStamp = -1;
            private int activeStamp = -1;
            private string activeCached = TargetPresetLibrary.CustomProfile;
            private EditMode editorMode = EditMode.None;

            private const int PresetVisible = 12;
            private const int SelectedVisible = 8;

            // Camera surface mark: state lives in Support through a narrow contract.
            private ICameraTargetService cameraService;
            private AvSlab cameraSlab;
            private AvGauge[] cameraRings;
            private AvHazardBar cameraAge;
            private readonly AvRow[] cameraRows = new AvRow[2];
            private AvControl cameraCapture;
            private AvControl cameraCall;
            private AvControl cameraClear;

            private static readonly (AvIcon Icon, string Label)[] Pages =
            {
                (AvIcon.Filter, "FILTERS"), (AvIcon.Radar2, "ACQUIRE"), (AvIcon.Star, "PRESETS"),
                (AvIcon.Target, "TARGETS"), (AvIcon.Camera, "CAMERA"),
            };

            public TargetPresenter(MFDScreen screen, TargetListSelector selector)
                : base(screen, VanillaMfdPanelId.Tgt)
            {
                this.selector = selector;
            }

            private ICameraTargetService Camera
            {
                get
                {
                    if (cameraService == null) ModuleServices.TryGet(out cameraService);
                    return cameraService;
                }
            }

            protected override string Title => "TARGETING";
            protected override (AvIcon Icon, string Label)[] TabItems => Pages;

            protected override string[] TabTips { get; } = new[]
            {
                "Filters: gate the target list by faction, class and platform, and link it to the HUD or the laser.",
                "Acquire: browse the nearest known contacts, preview one on the map and designate it onto the target list.",
                "Presets: three quick-switch slots and the saved preset library. Save, update, rename or delete your own.",
                "Targets: the tracked targets with three recall groups. Right-click a target to drop it.",
                "Camera: mark the surface point under the native camera and deliver an armed operation onto it.",
            };

            protected override void BuildContent()
            {
                // The filter-count / preset / HUD-link chips repeated the rings, the preset readout and the two
                // toggles below them, so the console header carries only the tab bar.
                BuildFiltersPage(CreatePage());
                BuildAcquirePage(CreatePage());
                BuildPresetsPage(CreatePage());
                BuildSelectedPage(CreatePage());
                BuildCameraPage(CreatePage());
            }

            protected override void OnPageChanged(int index)
            {
                CancelDeleteConfirm();
                CloseEditor();
                RequestRefresh();
            }

            protected override void RefreshContent()
            {
                RefreshCamera();

                if (!Ready)
                {
                    SetFilterInput(false);
                    foreach (AvGauge ring in filterMetrics) ring.Set(0f, "—", AvState.Inert);
                    return;
                }

                if (confirmDelete != null && Time.unscaledTime > confirmDeleteUntil)
                {
                    confirmDelete = null;
                    Echo("DELETE CANCELLED");
                }

                activePreset = CachedActive();
                EnsureCatalog();
                TrackSelectedPreset();
                SetFilterInput(true);

                int filters = CountEnabled(selector.toggleFactionItems) +
                              CountEnabled(selector.toggleUnitTypesItems) +
                              CountEnabled(selector.toggleVehicleTypesItems);
                SetFilterRing(filterMetrics[0], selector.toggleFactionItems);
                SetFilterRing(filterMetrics[1], selector.toggleUnitTypesItems);
                SetFilterRing(filterMetrics[2], selector.toggleVehicleTypesItems);


                int tracked = SelectedCount();
                clearTargets.Interactable = tracked > 0;
                clearTargets.Help = tracked > 0
                    ? "Drop every tracked contact from the target list."
                    : "No tracked contacts to clear.";

                followHud.Label = selector.toggleFollowHUD.status ? "HUD ON" : "HUD OFF";
                followHud.Latched = selector.toggleFollowHUD.status;
                laser.Label = selector.toggleLaser.status ? "LASER ON" : "LASER OFF";
                laser.Latched = selector.toggleLaser.status;

                SetGrid(factionGrid, selector.toggleFactionItems, ToggleFaction);
                SetGrid(unitGrid, selector.toggleUnitTypesItems, ToggleUnitType);
                SetGrid(vehicleGrid, selector.toggleVehicleTypesItems, ToggleVehicleType);
                RefreshSelectedGrid();
                RefreshAcquire();
                RefreshQuickSlots();
                RefreshLibrary();

                presetReadout.Set(selector.toggleFollowHUD.status ? "HUD LINK" : activePreset,
                    "ACTIVE PROFILE", DescribeSelected());
            }

            protected override string AmbientStatus()
            {
                if (!string.IsNullOrEmpty(echo) && Time.unscaledTime < echoUntil) return echo;
                int page = Console.CurrentPage;
                if (page == 1) return "PICK · DESIGNATE";
                if (page == 2) return "L APPLY · R QUICK SLOT";
                if (page == 3) return "L RECALL · R STORE / DROP";
                return "L TOGGLE · R SOLO";
            }

            private void Echo(string text)
            {
                echo = text;
                echoUntil = Time.unscaledTime + 2.4f;
            }

            // ------------------------------------------------------------- filters

            private void BuildFiltersPage(AvFlow page)
            {
                // Three rings (faction / class / platform): each shows how much of its mask is open.
                filterMetrics = new[]
                {
                    new AvGauge(page.Content, "FACTION", AvGaugeShape.Ring, 56f),
                    new AvGauge(page.Content, "CLASS", AvGaugeShape.Ring, 56f),
                    new AvGauge(page.Content, "PLATFORM", AvGaugeShape.Ring, 56f),
                };
                filterMetrics[0].Help = "Faction gate: how many sides are open. Every ring full means the target list follows everything.";
                filterMetrics[1].Help = "Class gate: aircraft, missiles, ground, buildings and ships that stay in the target list.";
                filterMetrics[2].Help = "Platform gate: which ground vehicle types stay in the target list.";
                page.Row(filterMetrics);

                AvButtons actionRow = page.Buttons(
                    new AvControl.Spec("RESET", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.ResetFilters();
                        selector.NeedUpdateIcons();
                        Echo("FILTERS RESET");
                        RequestRefresh();
                    }, AvButtonStyle.Default, AvIcon.Refresh),
                    new AvControl.Spec("CLEAR", () =>
                    {
                        if (!Ready) return;
                        selector.DeselectAll();
                        RequestRefresh();
                    }, AvButtonStyle.Default, AvIcon.Eraser),
                    new AvControl.Spec("HUD LINK", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleFollowHUD.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle, AvIcon.Link),
                    new AvControl.Spec("LASER", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleLaser.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle, AvIcon.Bolt));
                resetFilters = actionRow.Controls[0];
                clearTargets = actionRow.Controls[1];
                followHud = actionRow.Controls[2];
                laser = actionRow.Controls[3];
                resetFilters.Help = "Reset: restore the ALL profile. Every faction and unit class is open and the laser filter is off.";
                followHud.Help = "HUD link: the target list tracks whatever the HUD is following, so the two never disagree.";
                laser.Help = "Laser only: the target list keeps only targets that are being lased.";

                page.Section(AvIcon.Shield, "FACTION", "R-CLICK: ONLY THIS");
                factionGrid = AddGrid(page, new MfdPagingGrid(page.Content, 2, 1, pager: false, rowHeight: 56f, tile: true));
                factionGrid.SetGlyphs(i => FilterGlyph(selector == null ? null : selector.toggleFactionItems, i,
                    i == 0 ? "FRIENDLY" : i == 1 ? "ENEMY" : ""));
                AddRightClickActions(factionGrid, 2, OnlyFaction);

                page.Section(AvIcon.LayersSubtract, "CLASS", "R-CLICK: ONLY THIS");
                unitGrid = AddGrid(page, new MfdPagingGrid(page.Content, 3, 2, pager: false, rowHeight: 56f, tile: true));
                unitGrid.SetGlyphs(i => FilterGlyph(selector == null ? null : selector.toggleUnitTypesItems, i,
                    i >= 0 && i < UnitFallback.Length ? UnitFallback[i] : ""));
                AddRightClickActions(unitGrid, 6, OnlyUnitType);

                page.Section(AvIcon.Stack2, "PLATFORM", "R-CLICK: ONLY THIS");
                vehicleGrid = AddGrid(page, new MfdPagingGrid(page.Content, 5, 2, pager: false, rowHeight: 56f, tile: true));
                vehicleGrid.SetGlyphs(i => VehicleGlyph(
                    selector != null && selector.toggleVehicleTypesItems != null && i >= 0 && i < selector.toggleVehicleTypesItems.Count
                        ? NativeTargetLabel(selector.toggleVehicleTypesItems[i]) : "", i));
                AddRightClickActions(vehicleGrid, 10, OnlyVehicleType);

            }

            private static readonly string[] UnitFallback =
                { "AIRCRAFT", "MISSILES", "GROUND", "BUILDINGS", "SHIPS" };

            private static AvIcon FilterGlyph(List<TargetListSelector_ToggleButton> entries, int index, string fallback = "")
            {
                switch (entries != null && index >= 0 && index < entries.Count ? NativeTargetLabel(entries[index]) : fallback)
                {
                    case "FRIENDLY": return AvIcon.Shield;
                    case "ENEMY": return AvIcon.Skull;
                    case "AIRCRAFT": return AvIcon.Plane;
                    case "MISSILES": return AvIcon.ArrowUpRight;
                    case "GROUND": return AvIcon.ChartArrows;
                    case "BUILDINGS": return AvIcon.BuildingBank;
                    case "SHIPS": return AvIcon.Flag;
                    default: return AvIcon.Circle;
                }
            }

            // ------------------------------------------------------------- acquire

            private struct TargetCandidate
            {
                public Unit Unit;
                public float DistanceKm;
            }

            private void BuildAcquirePage(AvFlow page)
            {
                AvButtons prefs = page.Buttons(
                    new AvControl.Spec("MSL OFF", () => ToggleAcquirePreference(0), AvButtonStyle.Toggle, AvIcon.ArrowUpRight),
                    new AvControl.Spec("FIT OFF", () => ToggleAcquirePreference(1), AvButtonStyle.Toggle, AvIcon.Target),
                    new AvControl.Spec("ALL CLASS", () => ToggleAcquirePreference(2), AvButtonStyle.Toggle, AvIcon.Plane),
                    new AvControl.Spec("ALL RNG", () => ToggleAcquirePreference(3), AvButtonStyle.Toggle, AvIcon.Ruler2));
                missilePreference = prefs.Controls[0];
                weaponPreference = prefs.Controls[1];
                airPreference = prefs.Controls[2];
                rangePreference = prefs.Controls[3];
                missilePreference.Help = "Missiles: include tracked missiles in this browser. The native target filters stay independent.";
                weaponPreference.Help = "Weapon fit: only list contacts the selected weapon can engage right now.";
                airPreference.Help = "Air only: list aircraft only. Press again to show every class the filters allow.";
                rangePreference.Help = "Range: cycle the maximum distance through all, 10, 25, 50 and 100 km.";

                contactsSection = page.Section(AvIcon.Eye, "NEAREST", "0 KNOWN");
                focusSlab = new AvSlab(page.Content, "NO CONTACT", AvState.Inert);
                focusRange = new AvHazardBar(page.Content, "RANGE");
                focusRange.Set(0f, "—", AvState.Inert);
                focusRange.Help = "Range to the previewed contact, full at your aircraft and empty at the range limit. Known position only.";
                page.Row(focusSlab, focusRange);
                candidateGrid = AddGrid(page, new MfdPagingGrid(page.Content, 1, 7, rowHeight: 40f));
                candidateGrid.SetEmptyMessage("NO CONTACTS");
                AvButtons candidateRow = page.Buttons(
                    new AvControl.Spec("NEXT", NextCandidate, AvButtonStyle.Default, AvIcon.ChevronRight, true),
                    new AvControl.Spec("INCOMING", PreviewIncoming, AvButtonStyle.Default, AvIcon.AlertTriangle),
                    new AvControl.Spec("DESIGNATE", DesignateCandidate, AvButtonStyle.Primary, AvIcon.Target));
                nextCandidate = candidateRow.Controls[0];
                incomingCandidate = candidateRow.Controls[1];
                designateCandidate = candidateRow.Controls[2];
                nextCandidate.Help = "Next: preview the next known contact on the map without selecting it.";
                incomingCandidate.Help = "Incoming: highlight the nearest tracked missile aimed at your aircraft. It does not select it.";
                designateCandidate.Help = "Designate: add the previewed contact to the native target list.";

                rangeProfile = new AvEqualizer(page.Content, "RANGE PROFILE", 36f);
                rangeProfile.Set(rangeBins, "—", AvState.Inert);
                rangeProfile.Help = "Range profile: known contacts per distance band from your aircraft out to the range limit, nearest on the left. " +
                                    "Tall bars are where the contacts are.";
                page.Add(rangeProfile, 1f);
            }

            /// <summary>Histogram of the listed contacts by distance (nearest left), out to the range limit.</summary>
            private void RefreshRangeProfile(int limitKm)
            {
                for (int i = 0; i < rangeBins.Length; i++) rangeBins[i] = 0f;
                float span = limitKm > 0 ? limitKm : 100f;
                float peak = 0f;
                for (int i = 0; i < candidates.Count; i++)
                {
                    int bin = Mathf.Clamp((int)(candidates[i].DistanceKm / span * rangeBins.Length), 0, rangeBins.Length - 1);
                    rangeBins[bin] += 1f;
                    peak = Mathf.Max(peak, rangeBins[bin]);
                }
                if (peak > 0f) for (int i = 0; i < rangeBins.Length; i++) rangeBins[i] /= peak;
                rangeProfile.Set(rangeBins, candidates.Count == 0 ? "—" : candidates.Count + " · 0-" + AvNum.Fixed(span, 0) + " KM",
                    candidates.Count > 0 ? AvState.Info : AvState.Inert);
            }
            private void ToggleAcquirePreference(int which)
            {
                var settings = TargetPresetRuntime.Settings;
                if (settings == null) return;
                switch (which)
                {
                    case 0: settings.TargetShowMissiles.Value = !settings.TargetShowMissiles.Value; break;
                    case 1: settings.TargetWeaponOnly.Value = !settings.TargetWeaponOnly.Value; break;
                    case 2: settings.TargetAirOnly.Value = !settings.TargetAirOnly.Value; break;
                    case 3:
                        int[] ranges = { 0, 10, 25, 50, 100 };
                        int index = Array.IndexOf(ranges, settings.TargetRangeKm.Value);
                        settings.TargetRangeKm.Value = ranges[(index + 1) % ranges.Length];
                        break;
                }
                candidateFocus = null;
                nextCandidateScan = 0f;
                RequestRefresh();
            }

            private void RefreshAcquire()
            {
                var settings = TargetPresetRuntime.Settings;
                if (settings == null || candidateGrid == null) return;
                missilePreference.Label = settings.TargetShowMissiles.Value ? "MSL ON" : "MSL OFF";
                missilePreference.Latched = settings.TargetShowMissiles.Value;
                weaponPreference.Label = settings.TargetWeaponOnly.Value ? "FIT ON" : "FIT OFF";
                weaponPreference.Latched = settings.TargetWeaponOnly.Value;
                airPreference.Label = settings.TargetAirOnly.Value ? "AIR ONLY" : "ALL CLASS";
                airPreference.Latched = settings.TargetAirOnly.Value;
                int range = settings.TargetRangeKm.Value;
                rangePreference.Label = range <= 0 ? "ALL RNG" : range + " KM";
                rangePreference.Latched = range > 0;
                if (Console.CurrentPage != 1) return;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;
                candidateGrid.SetEmptyMessage(flying ? "NO CONTACTS" : "NO AIRCRAFT");
                incomingCandidate.Interactable = flying;
                if (Time.unscaledTime >= nextCandidateScan)
                {
                    ScanCandidates();
                    nextCandidateScan = Time.unscaledTime + 0.5f;
                }
                candidateGrid.SetData(candidates.Count,
                    i => TargetUnitLabel(candidates[i].Unit),
                    i => candidates[i].Unit == candidateFocus,
                    PreviewCandidate,
                    icons: i => candidates[i].Unit.definition == null ? null : candidates[i].Unit.definition.mapIcon,
                    subs: i => AvNum.Fixed(candidates[i].DistanceKm, 1) + " KM");
                RefreshFocus(settings.TargetRangeKm.Value);
                RefreshRangeProfile(settings.TargetRangeKm.Value);
                if (contactsSection != null) contactsSection.SetCaption(flying ? candidates.Count + " MATCH" : "NO AIRCRAFT");
                nextCandidate.Interactable = candidates.Count > 0;
                designateCandidate.Interactable = candidateFocus != null && candidates.Exists(c => c.Unit == candidateFocus);
            }

            /// <summary>IFF slab and range bar for the previewed contact (known position only).</summary>
            private void RefreshFocus(int rangeLimitKm)
            {
                int index = candidates.FindIndex(c => c.Unit == candidateFocus);
                if (index < 0)
                {
                    focusSlab.Set("NO CONTACT", AvState.Inert);
                    focusRange.Set(0f, "—", AvState.Inert);
                    return;
                }
                string word;
                AvState state;
                switch (DynamicMap.GetFactionMode(candidates[index].Unit.NetworkHQ, true))
                {
                    case FactionMode.Friendly: word = "FRIENDLY"; state = AvState.Info; break;
                    case FactionMode.Enemy: word = "HOSTILE"; state = AvState.Danger; break;
                    default: word = "UNKNOWN"; state = AvState.Caution; break;
                }
                focusSlab.Set(word, state);
                float km = candidates[index].DistanceKm;
                float span = rangeLimitKm > 0 ? rangeLimitKm : 100f;
                focusRange.Set(Mathf.Clamp01(1f - km / span), AvNum.Fixed(km, 1) + " KM", AvState.Info);
            }

            private void ScanCandidates()
            {
                candidates.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                var settings = TargetPresetRuntime.Settings;
                if (map == null || map.HQ == null || map.mapIcons == null || hud == null ||
                    hud.aircraft == null || hud.aircraft.disabled || settings == null) return;
                WeaponStation station = hud.GetWeaponStation();
                for (int i = 0; i < map.mapIcons.Count; i++)
                {
                    UnitMapIcon icon = map.mapIcons[i] as UnitMapIcon;
                    Unit unit = icon == null ? null : icon.unit;
                    if (unit == null || unit == hud.aircraft || unit.definition == null ||
                        !icon.gameObject.activeInHierarchy || selector.CheckExclusions(unit)) continue;
                    if (!settings.TargetShowMissiles.Value && unit.definition is MissileDefinition) continue;
                    if (settings.TargetAirOnly.Value && !(unit is Aircraft)) continue;
                    GlobalPosition known;
                    if (!map.HQ.TryGetKnownPosition(unit, out known)) continue;
                    float distance = FastMath.Distance(hud.aircraft.GlobalPosition(), known) / 1000f;
                    if (float.IsNaN(distance) || float.IsInfinity(distance) ||
                        (settings.TargetRangeKm.Value > 0 && distance > settings.TargetRangeKm.Value)) continue;
                    if (settings.TargetWeaponOnly.Value &&
                        (station == null || station.CalcOpportunityThreat(unit.definition, hud.aircraft).opportunity <= 0f))
                        continue;
                    int insert = candidates.FindIndex(c => c.DistanceKm > distance);
                    if (insert < 0) insert = candidates.Count;
                    if (insert >= 128) continue;
                    candidates.Insert(insert, new TargetCandidate { Unit = unit, DistanceKm = distance });
                    if (candidates.Count > 128) candidates.RemoveAt(128);
                }
                if (candidateFocus != null && !candidates.Exists(c => c.Unit == candidateFocus)) candidateFocus = null;
            }

            private void PreviewCandidate(int index)
            {
                if (index < 0 || index >= candidates.Count) return;
                candidateFocus = candidates[index].Unit;
                SceneSingleton<DynamicMap>.i?.HighlightIcon(candidateFocus);
                Echo("PREVIEW " + TargetUnitLabel(candidateFocus));
                RequestRefresh();
            }

            private void NextCandidate()
            {
                if (candidates.Count == 0) return;
                int index = candidates.FindIndex(c => c.Unit == candidateFocus);
                PreviewCandidate((index + 1) % candidates.Count);
            }

            private void PreviewIncoming()
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (map == null || map.HQ == null || map.mapIcons == null || hud == null || hud.aircraft == null) return;
                Missile nearest = null;
                float best = float.MaxValue;
                for (int i = 0; i < map.mapIcons.Count; i++)
                {
                    UnitMapIcon icon = map.mapIcons[i] as UnitMapIcon;
                    Missile missile = icon == null ? null : icon.unit as Missile;
                    if (missile == null || missile.targetID != hud.aircraft.persistentID) continue;
                    GlobalPosition known;
                    if (!map.HQ.TryGetKnownPosition(missile, out known)) continue;
                    float distance = FastMath.Distance(hud.aircraft.GlobalPosition(), known);
                    if (distance >= best) continue;
                    best = distance;
                    nearest = missile;
                }
                if (nearest == null) { Echo("NO TRACKED INCOMING MISSILE"); return; }
                map.HighlightIcon(nearest);
                Echo("INCOMING MISSILE HIGHLIGHTED · NO TARGET ADDED");
                RequestRefresh();
            }

            private void DesignateCandidate()
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                GlobalPosition known;
                if (candidateFocus == null || hud == null || hud.aircraft == null || hud.aircraft.disabled ||
                    map == null || map.HQ == null || !map.HQ.TryGetKnownPosition(candidateFocus, out known) ||
                    !candidates.Exists(c => c.Unit == candidateFocus) || selector.CheckExclusions(candidateFocus)) return;
                hud.SelectUnit(candidateFocus);
                Echo(hud.GetTargetList().Contains(candidateFocus)
                    ? "DESIGNATED " + TargetUnitLabel(candidateFocus)
                    : "CONTACT NOT AVAILABLE TO HUD TARGET LIST");
                RequestRefresh();
            }

            // ------------------------------------------------------------- presets

            private void BuildPresetsPage(AvFlow page)
            {
                string[] keys =
                {
                    KeyLabel(TargetPresetRuntime.Key(0)),
                    KeyLabel(TargetPresetRuntime.Key(1)),
                    KeyLabel(TargetPresetRuntime.Key(2)),
                };
                page.Section(AvIcon.Star, "QUICK SWITCH", "RADIAL · " + string.Join(" ", keys));
                quickGrid = AddGrid(page, new MfdPagingGrid(page.Content, 3, 1, pager: false, rowHeight: 56f, tile: true));
                quickGrid.SetGlyphs(i => AvIcon.Star);
                AddSlotActions();

                presetSection = page.Section(AvIcon.Bookmark, "PRESET LIBRARY", SavedNote());
                presetGrid = AddGrid(page, new MfdPagingGrid(page.Content, 2, 6, rowHeight: 40f));
                AddPresetActions();

                presetReadout = page.Add(new AvReadout(page.Content));

                AvButtons presetActions = page.Buttons(
                    new AvControl.Spec("SAVE AS", BeginSaveAs, AvButtonStyle.Primary, AvIcon.Plus),
                    new AvControl.Spec("UPDATE", UpdateSelected, AvButtonStyle.Toggle, AvIcon.Refresh),
                    new AvControl.Spec("RENAME", BeginRename, AvButtonStyle.Toggle, AvIcon.Pencil),
                    new AvControl.Spec("DELETE", PressDelete, AvButtonStyle.Danger, AvIcon.X));
                saveAs = presetActions.Controls[0];
                updatePreset = presetActions.Controls[1];
                renamePreset = presetActions.Controls[2];
                deletePreset = presetActions.Controls[3];
                saveAs.Help = "Save as: store the current filters as a new named preset. Names are A-Z, 0-9, up to 14 characters.";

                editorPart = page.Add(new PresetEditor(page, CommitEdit, CancelEdit));
            }
            private string SavedNote() =>
                TargetPresetRuntime.Library.Count + " / " + TargetPresetLibrary.MaxCustomPresets + " SAVED";

            private static string KeyLabel(KeyCode key) => key == KeyCode.None ? "NONE" : key.ToString();

            /// <summary>Local kit v2 part: a name field plus CONFIRM/CANCEL, hidden (zero height) until
            /// <see cref="Show"/>. AvField has no public focus/select seam (kit gap): the field no longer
            /// auto-activates the keyboard on open, the way <c>TMP_InputField.ActivateInputField</c> did.</summary>
            private sealed class PresetEditor : AvPart
            {
                private readonly AvFlow page;
                private readonly TMPro.TMP_Text label;
                private readonly AvField field;
                private readonly AvControl confirm, cancel;
                private bool visible;

                public PresetEditor(AvFlow flow, Action commit, Action cancelAction)
                {
                    page = flow;
                    Rect = AvLay.Child(flow.Content, "Editor");
                    label = AvText.Make(Rect, "Label", AvTextRole.Micro, "");
                    field = new AvField(Rect, "A-Z 0-9, 14 characters", TargetPresetLibrary.MaxNameLength, _ => commit());
                    confirm = AvControl.Make(Rect, new AvControl.Spec("CONFIRM", commit, AvButtonStyle.Primary));
                    cancel = AvControl.Make(Rect, new AvControl.Spec("CANCEL", cancelAction, AvButtonStyle.Toggle));
                    Rect.gameObject.SetActive(false);
                }

                public string Text { get => field.Text; set => field.Text = value; }

                public void Show(string labelText)
                {
                    label.text = labelText ?? "";
                    visible = true;
                    Rect.gameObject.SetActive(true);
                    page.RequestRelayout();
                }

                public void Hide()
                {
                    if (!visible) return;
                    visible = false;
                    Rect.gameObject.SetActive(false);
                    page.RequestRelayout();
                }

                public override float Measure(float width) => visible ? 18f + AvGridTokens.Row : 0f;

                public override void Place(AvSlot s)
                {
                    base.Place(s);
                    if (!visible) return;
                    AvLay.Place(label.rectTransform, 0f, 0f, s.W, 16f);
                    float okW = 90f, cancelW = 90f, gap = AvGridTokens.Gap;
                    float fieldW = Mathf.Max(80f, s.W - okW - cancelW - gap * 2f);
                    field.Place(new AvSlot(0f, 18f, fieldW, AvGridTokens.Row));
                    AvLay.Place(confirm.Rect, fieldW + gap, 18f, okW, AvGridTokens.Row);
                    AvLay.Place(cancel.Rect, fieldW + gap * 2f + okW, 18f, cancelW, AvGridTokens.Row);
                }

                public override void Restyle()
                {
                    field.Restyle();
                    confirm.Restyle();
                    cancel.Restyle();
                }
            }

            private void AddSlotActions()
            {
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                {
                    int index = slot;
                    MfdIconCell cell = quickGrid.CellAt(slot);
                    if (cell == null) continue;
                    cell.OnRightClick = () => ClearQuickSlot(index);
                }
            }

            private void AddPresetActions()
            {
                for (int slot = 0; slot < PresetVisible; slot++)
                {
                    int index = slot;
                    MfdIconCell cell = presetGrid.CellAt(slot);
                    if (cell == null) continue;
                    cell.OnRightClick = () => AssignQuickSlot(presetGrid.CurrentIndex(index));
                }
            }

            private void AddGroupActions()
            {
                for (int slot = 0; slot < targetGroups.Length; slot++)
                {
                    int index = slot;
                    MfdIconCell cell = groupGrid.CellAt(slot);
                    if (cell == null) continue;
                    cell.OnRightClick = () => StoreGroup(index);
                }
            }

            private void AssignQuickSlot(int catalogIndex)
            {
                if (!Ready || catalogIndex < 0 || catalogIndex >= catalog.Count) return;
                CancelDeleteConfirm();
                string name = catalog[catalogIndex].Name;
                if (TargetPresetRuntime.AssignToFirstFreeSlot(name))
                {
                    for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                        if (TargetPresetRuntime.QuickSlotName(slot) == name)
                        {
                            Echo(name + " → QUICK SLOT " + (slot + 1));
                            break;
                        }
                }
                else
                {
                    Echo("QUICK SLOTS FULL — RIGHT-CLICK A SLOT TO CLEAR");
                }
                RequestRefresh();
            }

            private void ApplyQuickSlot(int slot)
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                string name = TargetPresetRuntime.QuickSlotName(slot);
                if (name.Length == 0)
                {
                    Echo("SLOT " + (slot + 1) + " EMPTY — RIGHT-CLICK A PRESET TO ASSIGN");
                    RequestRefresh();
                    return;
                }
                TargetPresetRuntime.TryApplyByName(selector, name);
                selectedPreset = name;
                Echo("APPLIED " + name);
                RequestRefresh();
            }

            private void ClearQuickSlot(int slot)
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                Echo(TargetPresetRuntime.ClearQuickSlot(slot)
                    ? "QUICK SLOT " + (slot + 1) + " CLEARED"
                    : "QUICK SLOT " + (slot + 1) + " ALREADY EMPTY");
                RequestRefresh();
            }

            private void RefreshQuickSlots()
            {
                quickGrid.SetData(TargetPresetLibrary.SlotCount,
                    slot => SlotLabel(slot),
                    slot => TargetPresetRuntime.QuickSlotName(slot).Length > 0 &&
                            TargetPresetRuntime.QuickSlotName(slot) == activePreset,
                    ApplyQuickSlot,
                    details: slot =>
                    {
                        string name = TargetPresetRuntime.QuickSlotName(slot);
                        return name.Length == 0
                            ? "Quick slot " + (slot + 1) + " is empty. Right-click a preset below to assign it."
                            : "Apply " + name + " (quick slot " + (slot + 1) + ", key " +
                              KeyLabel(TargetPresetRuntime.Key(slot)) + "). Right-click to clear.";
                    },
                    subs: slot => "SLOT " + (slot + 1) + " · " + KeyLabel(TargetPresetRuntime.Key(slot)));
            }

            private static string SlotLabel(int slot)
            {
                string name = TargetPresetRuntime.QuickSlotName(slot);
                return name.Length == 0 ? "EMPTY" : name;
            }

            private void RefreshLibrary()
            {
                EnsureCatalog();
                if (presetSection != null) presetSection.SetCaption(SavedNote());
                presetGrid.SetData(catalog.Count,
                    LibraryLabel,
                    index => catalog[index] != null && catalog[index].Name == activePreset,
                    ApplyCatalog,
                    icons: index => null,
                    subs: index => TargetPresetRuntime.IsBuiltIn(index) ? "BUILT-IN" : "SAVED",
                    details: index =>
                    {
                        TargetPresetSnapshot preset = catalog[index];
                        if (preset == null) return null;
                        return preset.Name + PresetSlotBadge(preset.Name) + " — " +
                            (TargetPresetRuntime.IsBuiltIn(index)
                                ? MfdTargetPresets.Descriptions[index]
                                : TargetPresetRules.Summary(preset)) +
                            "  ·  Left click applies, right click assigns a quick slot.";
                    });
            }

            private string LibraryLabel(int index)
            {
                TargetPresetSnapshot preset = catalog[index];
                if (preset == null) return "";
                string label = preset.Name;
                if (selectedPreset == preset.Name) label = "> " + label;
                string badge = PresetSlotBadge(preset.Name);
                return badge.Length == 0 ? label : label + "  " + badge;
            }

            private static string PresetSlotBadge(string name)
            {
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                    if (TargetPresetRuntime.QuickSlotName(slot) == name) return "[" + (slot + 1) + "]";
                return "";
            }

            private void ApplyCatalog(int index)
            {
                if (!Ready || index < 0 || index >= catalog.Count) return;
                CancelDeleteConfirm();
                TargetPresetSnapshot preset = catalog[index];
                if (preset == null) return;
                TargetPresetRuntime.Apply(selector, preset);
                selectedPreset = preset.Name;
                Echo("APPLIED " + preset.Name);
                RequestRefresh();
            }

            private void EnsureCatalog()
            {
                int stamp = selector.toggleFactionItems.Count * 31 +
                            selector.toggleUnitTypesItems.Count * 7 +
                            selector.toggleVehicleTypesItems.Count;
                if (catalogVersion == TargetPresetRuntime.Version && catalogStamp == stamp) return;

                catalog.Clear();
                int count = TargetPresetRuntime.CatalogCount;
                for (int i = 0; i < count; i++) catalog.Add(TargetPresetRuntime.CatalogAt(selector, i));
                catalogVersion = TargetPresetRuntime.Version;
                catalogStamp = stamp;
            }

            /// <summary>Profile matching walks every preset; only redo it when a toggle moved.</summary>
            private string CachedActive()
            {
                int stamp = TargetPresetRuntime.Version;
                stamp = stamp * 31 + (selector.toggleFollowHUD.status ? 1 : 0);
                stamp = stamp * 31 + (selector.toggleLaser.status ? 1 : 0);
                stamp = stamp * 31 + selector.toggleFactionItems.Count;
                for (int i = 0; i < selector.toggleFactionItems.Count; i++)
                    stamp = stamp * 31 + EntryHash(selector.toggleFactionItems[i]);
                stamp = stamp * 31 + selector.toggleUnitTypesItems.Count;
                for (int i = 0; i < selector.toggleUnitTypesItems.Count; i++)
                    stamp = stamp * 31 + EntryHash(selector.toggleUnitTypesItems[i]);
                stamp = stamp * 31 + selector.toggleVehicleTypesItems.Count;
                for (int i = 0; i < selector.toggleVehicleTypesItems.Count; i++)
                    stamp = stamp * 31 + EntryHash(selector.toggleVehicleTypesItems[i]);

                if (stamp == activeStamp) return activeCached;
                activeStamp = stamp;
                activeCached = TargetPresetRuntime.ActiveName(selector);
                return activeCached;
            }

            private static int EntryHash(TargetListSelector_ToggleButton entry) =>
                entry == null ? 0 : (entry.status ? 1 : 2);

            private void TrackSelectedPreset()
            {
                if (string.IsNullOrEmpty(selectedPreset) ||
                    TargetPresetRuntime.IndexOfCatalogName(selectedPreset) < 0)
                    selectedPreset = activePreset;
            }

            private TargetPresetSnapshot SelectedCustom()
            {
                int index = TargetPresetRuntime.IndexOfCatalogName(selectedPreset);
                return index >= TargetPresetRuntime.BuiltInCount ? TargetPresetRuntime.Library.At(
                    index - TargetPresetRuntime.BuiltInCount) : null;
            }

            private void BeginSaveAs()
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                editorPart.Text = TargetPresetRuntime.SuggestName();
                OpenEditor(EditMode.SaveAs, "SAVE CURRENT FILTERS AS");
            }

            private void BeginRename()
            {
                if (!Ready) return;
                TargetPresetSnapshot preset = SelectedCustom();
                if (preset == null)
                {
                    Echo("RENAME WORKS ON SAVED PRESETS — SELECT ONE FIRST");
                    RequestRefresh();
                    return;
                }
                CancelDeleteConfirm();
                editorPart.Text = preset.Name;
                OpenEditor(EditMode.Rename, "RENAME " + preset.Name);
            }

            private void OpenEditor(EditMode mode, string label)
            {
                editorMode = mode;
                editorPart.Show(label);
                RequestRefresh();
            }

            private void CloseEditor()
            {
                editorMode = EditMode.None;
                editorPart?.Hide();
            }

            private void CancelEdit()
            {
                CloseEditor();
                Echo("EDIT CANCELLED");
                RequestRefresh();
            }

            private void CommitEdit()
            {
                if (editorMode == EditMode.None) return;
                string name = TargetPresetRules.SanitiseName(editorPart.Text);
                if (name.Length == 0)
                {
                    Echo("NAME REQUIRED — A-Z, 0-9, 14 CHARACTERS");
                    RequestRefresh();
                    return;
                }

                if (editorMode == EditMode.SaveAs)
                {
                    TargetPresetSaveResult result = TargetPresetRuntime.SaveCurrent(selector, name, false);
                    if (result == TargetPresetSaveResult.Ok)
                    {
                        selectedPreset = name;
                        Echo("SAVED " + name);
                    }
                    else
                    {
                        Echo(SaveMessage(result, name));
                        RequestRefresh();
                        return;
                    }
                }
                else
                {
                    if (name == selectedPreset)
                    {
                        CloseEditor();
                        Echo("NO CHANGE");
                        RequestRefresh();
                        return;
                    }
                    if (!TargetPresetRuntime.Rename(selectedPreset, name))
                    {
                        Echo("RENAME REJECTED — NAME IN USE OR RESERVED");
                        RequestRefresh();
                        return;
                    }
                    Echo("RENAMED TO " + name);
                    selectedPreset = name;
                }

                CloseEditor();
                RequestRefresh();
            }

            private static string SaveMessage(TargetPresetSaveResult result, string name)
            {
                switch (result)
                {
                    case TargetPresetSaveResult.NameInUse: return name + " EXISTS — USE UPDATE TO REPLACE IT";
                    case TargetPresetSaveResult.LibraryFull:
                        return "LIBRARY FULL (" + TargetPresetLibrary.MaxCustomPresets + " MAX) — DELETE ONE FIRST";
                    case TargetPresetSaveResult.ReservedName: return name + " IS A BUILT-IN PROFILE NAME";
                    case TargetPresetSaveResult.NotReady: return "WAITING FOR TARGET FILTERS";
                    default: return "NAME REQUIRED — A-Z, 0-9, 14 CHARACTERS";
                }
            }

            private void UpdateSelected()
            {
                if (!Ready) return;
                CancelDeleteConfirm();
                TargetPresetSnapshot preset = SelectedCustom();
                if (preset == null)
                {
                    Echo("UPDATE WORKS ON SAVED PRESETS — SELECT ONE FIRST");
                    RequestRefresh();
                    return;
                }
                TargetPresetSaveResult result = TargetPresetRuntime.SaveCurrent(selector, preset.Name, true);
                Echo(result == TargetPresetSaveResult.Ok ? "UPDATED " + preset.Name : SaveMessage(result, preset.Name));
                RequestRefresh();
            }

            private void PressDelete()
            {
                if (!Ready) return;
                TargetPresetSnapshot preset = SelectedCustom();
                if (preset == null)
                {
                    Echo("DELETE WORKS ON SAVED PRESETS — SELECT ONE FIRST");
                    RequestRefresh();
                    return;
                }

                if (confirmDelete != preset.Name || Time.unscaledTime > confirmDeleteUntil)
                {
                    confirmDelete = preset.Name;
                    confirmDeleteUntil = Time.unscaledTime + 4f;
                    Echo("DELETE " + preset.Name + "? PRESS DELETE AGAIN");
                    RequestRefresh();
                    return;
                }

                TargetPresetRuntime.Delete(preset.Name);
                confirmDelete = null;
                selectedPreset = "";
                Echo("DELETED " + preset.Name);
                RequestRefresh();
            }

            private void CancelDeleteConfirm()
            {
                if (confirmDelete == null) return;
                confirmDelete = null;
                Echo("DELETE CANCELLED");
            }

            private string DescribeSelected()
            {
                int index = TargetPresetRuntime.IndexOfCatalogName(selectedPreset);
                if (index < 0) return "—";
                if (index < TargetPresetRuntime.BuiltInCount)
                    return "Built-in. " + MfdTargetPresets.Descriptions[index];
                TargetPresetSnapshot preset = TargetPresetRuntime.Library.At(
                    index - TargetPresetRuntime.BuiltInCount);
                return preset == null ? "" : TargetPresetRules.Summary(preset);
            }

            // ------------------------------------------------------------ selected

            private void BuildSelectedPage(AvFlow page)
            {
                selectedSection = page.Section(AvIcon.Target, "SELECTED TARGETS", "0 TRACKED");

                groupGrid = AddGrid(page, new MfdPagingGrid(page.Content, 3, 1, pager: false, rowHeight: 56f, tile: true));
                groupGrid.SetGlyphs(i => AvIcon.UsersGroup);
                AddGroupActions();

                selectedGrid = AddGrid(page, new MfdPagingGrid(page.Content, 1, SelectedVisible, readOnly: true, rowHeight: 44f));
                selectedGrid.SetEmptyMessage("NO TARGETS");
                AddRightClickActions(selectedGrid, SelectedVisible, DeselectSelected);

                trackedProfile = new AvEqualizer(page.Content, "RANGE PROFILE", 36f);
                trackedProfile.Set(trackedBars, "—", AvState.Inert);
                trackedProfile.Help = "Range profile: one bar per tracked target in list order, tall for far and short for near, scaled to the farthest. " +
                                      "Right-click a target in the list to drop it.";
                page.Add(trackedProfile, 1f);
            }

            /// <summary>Distance of each tracked target from the player, as bars scaled to the farthest.</summary>
            private void RefreshTrackedProfile()
            {
                for (int i = 0; i < trackedBars.Length; i++) trackedBars[i] = 0f;
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (map == null || map.HQ == null || hud == null || hud.aircraft == null || selectedUnits.Count == 0)
                {
                    trackedProfile.Set(trackedBars, "—", AvState.Inert);
                    return;
                }
                float far = 0f, near = float.MaxValue;
                int known = 0;
                for (int i = 0; i < selectedUnits.Count && i < trackedBars.Length; i++)
                {
                    GlobalPosition at;
                    if (selectedUnits[i] == null || !map.HQ.TryGetKnownPosition(selectedUnits[i], out at)) continue;
                    float km = FastMath.Distance(hud.aircraft.GlobalPosition(), at) / 1000f;
                    if (float.IsNaN(km) || float.IsInfinity(km)) continue;
                    trackedBars[i] = km;
                    far = Mathf.Max(far, km);
                    near = Mathf.Min(near, km);
                    known++;
                }
                if (known == 0) { trackedProfile.Set(trackedBars, "—", AvState.Inert); return; }
                for (int i = 0; i < trackedBars.Length; i++)
                    if (trackedBars[i] > 0f) trackedBars[i] = Mathf.Max(0.08f, trackedBars[i] / Mathf.Max(0.01f, far));
                trackedProfile.Set(trackedBars, "NEAREST " + AvNum.Fixed(near, 1) + " KM", AvState.Info);
            }
            private void DeselectSelected(int index)
            {
                if (index < 0 || index >= selectedUnits.Count) return;
                Unit unit = selectedUnits[index];
                if (unit == null) return;
                selector.ForceDeselect(unit);
                Echo("DROPPED " + TargetUnitLabel(unit));
                RequestRefresh();
            }

            private void StoreGroup(int slot)
            {
                if (slot < 0 || slot >= targetGroups.Length || selectedUnits.Count == 0) return;
                List<Unit> group = targetGroups[slot];
                group.Clear();
                for (int i = 0; i < selectedUnits.Count && group.Count < 32; i++)
                    if (selectedUnits[i] != null && !group.Contains(selectedUnits[i])) group.Add(selectedUnits[i]);
                Echo("STORED " + group.Count + " TARGETS IN GROUP " + (slot + 1));
                RequestRefresh();
            }

            private void RecallGroup(int slot)
            {
                if (!Ready || slot < 0 || slot >= targetGroups.Length) return;
                List<Unit> group = targetGroups[slot];
                if (group.Count == 0) { Echo("GROUP " + (slot + 1) + " EMPTY · RIGHT CLICK TO STORE"); return; }
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (map == null || map.HQ == null || hud == null || hud.aircraft == null || hud.aircraft.disabled) return;
                var eligible = new List<Unit>(group.Count);
                for (int i = 0; i < group.Count; i++)
                {
                    Unit unit = group[i];
                    GlobalPosition known;
                    if (unit != null && !unit.disabled && !selector.CheckExclusions(unit) &&
                        map.HQ.TryGetKnownPosition(unit, out known)) eligible.Add(unit);
                }
                if (eligible.Count == 0) { Echo("GROUP HAS NO ELIGIBLE TRACKED TARGETS"); return; }
                selector.DeselectAll();
                for (int i = 0; i < eligible.Count; i++) hud.SelectUnit(eligible[i]);
                int recalled = 0;
                for (int i = 0; i < eligible.Count; i++)
                    if (hud.GetTargetList().Contains(eligible[i])) recalled++;
                Echo("RECALLED " + recalled + " TARGETS FROM GROUP " + (slot + 1));
                RequestRefresh();
            }

            // ------------------------------------------------------------ camera

            private void BuildCameraPage(AvFlow page)
            {
                cameraSlab = new AvSlab(page.Content, "NO MARK", AvState.Inert);
                cameraAge = new AvHazardBar(page.Content, "MARK AGE");
                cameraAge.Help = "Mark age: the mark expires 120 seconds after it was taken. The bar empties as it ages.";
                page.Row(cameraSlab, cameraAge);
                AvButtons cameraActions = page.Buttons(
                    new AvControl.Spec("MARK", () => { Camera?.Capture(); RequestRefresh(); }, AvButtonStyle.Default, AvIcon.Camera),
                    new AvControl.Spec("CALL AT MARK", () => { Camera?.CallAtMark(); RequestRefresh(); }, AvButtonStyle.Default, AvIcon.CurrentLocation),
                    new AvControl.Spec("CLEAR", () => { Camera?.Clear(); RequestRefresh(); }, AvButtonStyle.Default, AvIcon.Eraser));
                cameraCapture = cameraActions.Controls[0];
                cameraCall = cameraActions.Controls[1];
                cameraClear = cameraActions.Controls[2];

                cameraRings = new[]
                {
                    new AvGauge(page.Content, "RANGE KM", AvGaugeShape.Ring, 76f),
                    new AvGauge(page.Content, "ELEV M", AvGaugeShape.Ring, 76f),
                };
                cameraRings[0].Help = "Range: distance from your aircraft to the mark, full ring at 20 km.";
                cameraRings[1].Help = "Elevation: height of the marked surface point above sea level, full ring at 3000 m.";
                page.Row(cameraRings);
                for (int i = 0; i < cameraRows.Length; i++) cameraRows[i] = page.Add(new AvRow(page.Content));

                cameraLife = new AvEqualizer(page.Content, "MARK LIFE", 36f);
                cameraLife.Set(cameraLifeBars, "—", AvState.Inert);
                cameraLife.Help = "Mark life: twelve steps of ten seconds. Each lit step is time the mark has left before it expires.";
                page.Add(cameraLife, 1f);
            }
            private void RefreshCamera()
            {
                if (cameraSlab == null) return;
                ICameraTargetService service = Camera;
                if (service == null || !service.Available)
                {
                    cameraSlab.Set("OFFLINE", AvState.Inert);
                    if (cameraCapture != null) cameraCapture.Interactable = false;
                    if (cameraCall != null) cameraCall.Interactable = false;
                    if (cameraClear != null) cameraClear.Interactable = false;
                    const string cameraOffline =
                        "Camera marking is unavailable: the support module or its observation source is not installed.";
                    if (cameraCapture != null) cameraCapture.Help = cameraOffline;
                    if (cameraCall != null) cameraCall.Help = cameraOffline;
                    if (cameraClear != null) cameraClear.Help = cameraOffline;
                    SetCameraTelemetry("—", "—", false, 0f, 0f, 0f);
                    return;
                }

                bool marked = service.HasMark;
                bool armed = !string.IsNullOrEmpty(service.ArmedActionName);
                if (marked)
                {
                    ObservationPoint point = service.Mark;
                    cameraSlab.Set((point.Source ?? "SENSOR").ToUpperInvariant() + " MARK", AvState.Ready);
                    SetCameraTelemetry(
                        "X " + AvNum.Fixed(point.X, 0) + " · Z " + AvNum.Fixed(point.Z, 0),
                        armed ? service.ArmedActionName : "NONE",
                        true, Mathf.Max(0f, point.Range / 1000f), point.Y, Mathf.Max(0f, service.AgeSeconds));
                }
                else
                {
                    cameraSlab.Set("NO MARK", AvState.Inert);
                    SetCameraTelemetry("—", armed ? service.ArmedActionName : "NONE", false, 0f, 0f, 0f);
                }

                if (cameraCapture != null) cameraCapture.Interactable = service.CanCapture;
                if (cameraCall != null)
                {
                    cameraCall.Interactable = service.CanCallAtMark;
                    cameraCall.Label = armed ? "CALL AT MARK" : "SELECT IN OPS";
                }
                if (cameraClear != null) cameraClear.Interactable = marked;
                if (cameraCapture != null)
                    cameraCapture.Help = service.CanCapture
                        ? "Record the surface point under the native camera."
                        : "Requires an active native camera view on your aircraft.";
                if (cameraCall != null)
                    cameraCall.Help = !marked ? "Capture a mark first."
                        : !armed ? "Arm an operation on OPS / SUPPORT first."
                        : "Deliver the armed operation onto this mark.";
                if (cameraClear != null) cameraClear.Help = marked ? "Clear the active mark." : "No mark to clear.";
            }

            /// <summary>Range and elevation ride rings, the 120 s expiry a hazard bar; grid and armed call-in stay
            /// as rows so a long action name can wrap.</summary>
            private void SetCameraTelemetry(string position, string armed, bool marked, float rangeKm,
                float elevationM, float ageSeconds)
            {
                cameraRings[0].Set(marked ? Mathf.Clamp01(rangeKm / 20f) : 0f,
                    marked ? AvNum.Fixed(rangeKm, 1) : "\u2014", marked ? AvState.Info : AvState.Inert);
                cameraRings[1].Set(marked ? Mathf.Clamp01(elevationM / 3000f) : 0f,
                    marked ? AvNum.Fixed(elevationM, 0) : "\u2014", marked ? AvState.Info : AvState.Inert);
                cameraAge.Set(marked ? Mathf.Clamp01(1f - ageSeconds / 120f) : 0f,
                    marked ? AvNum.Fixed(ageSeconds, 0) + " S" : "—",
                    !marked ? AvState.Inert : ageSeconds > 90f ? AvState.Caution : AvState.Info);
                for (int i = 0; i < cameraLifeBars.Length; i++)
                    cameraLifeBars[i] = marked ? ((120f - ageSeconds) > i * 10f ? 1f : 0.12f) : 0.12f;
                cameraLife.Set(cameraLifeBars, marked ? AvNum.Fixed(Mathf.Max(0f, 120f - ageSeconds), 0) + " S LEFT" : "—",
                    !marked ? AvState.Inert : ageSeconds > 90f ? AvState.Caution : AvState.Info);
                cameraRows[0]?.Set("GRID", position, "", marked ? AvState.Info : AvState.Inert);
                cameraRows[1]?.Set("ARMED", armed, "", armed.StartsWith("NONE") ? AvState.Inert : AvState.Caution);
            }

            // ------------------------------------------------------------ plumbing

            private void SetGrid(MfdPagingGrid grid, List<TargetListSelector_ToggleButton> entries,
                                 Action<int> onClick)
            {
                grid.SetData(entries == null ? 0 : entries.Count,
                    i => NativeTargetLabel(entries[i]),
                    i => entries[i] != null && entries[i].status,
                    onClick, icons: i => entries[i] == null || entries[i].image == null ? null : entries[i].image.sprite,
                    details: i => (FilterNote(NativeTargetLabel(entries[i])) ?? NativeTargetLabel(entries[i])) +
                                  ". Left click toggles it in the target list; right click keeps only this one.");
            }

            /// <summary>One line under each filter name, MAP-style, so every switch reads the same height and weight.</summary>
            private static string FilterNote(string label)
            {
                switch (label)
                {
                    case "FRIENDLY": return "Friendly contacts";
                    case "ENEMY": return "Hostile contacts";
                    case "AIRCRAFT": return "Airborne tracks";
                    case "MISSILES": return "Missiles in flight";
                    case "GROUND": return "Vehicles & troops";
                    case "BUILDINGS": return "Bases & structures";
                    case "SHIPS": return "Naval contacts";
                    case "TRUCK": return "Supply trucks";
                    case "UGV": return "Unmanned ground";
                    case "LCV": return "Light combat";
                    case "AFV": return "Armored vehicles";
                    case "MBT": return "Main battle tanks";
                    case "ART": return "Field artillery";
                    case "AAA": return "Anti-air guns";
                    case "IR SAM": return "Heat-seeking SAM";
                    case "R SAM": return "Radar-guided SAM";
                    case "RDR":
                    case "RADAR": return "Search radars";
                    default: return null;
                }
            }

            private static void SetFilterRing(AvGauge ring, List<TargetListSelector_ToggleButton> entries)
            {
                int total = entries == null ? 0 : entries.Count;
                int open = CountEnabled(entries);
                ring.Set(total > 0 ? open / (float)total : 0f, AvNum.Fixed(open, 0) + "/" + AvNum.Fixed(total, 0),
                    total == 0 ? AvState.Inert : open == total ? AvState.Ready : open == 0 ? AvState.Caution : AvState.Info);
            }

            private static void AddRightClickActions(MfdPagingGrid grid, int slots, Action<int> onOnly)
            {
                for (int i = 0; i < slots; i++)
                {
                    int slot = i;
                    MfdIconCell cell = grid.CellAt(i);
                    if (cell == null) continue;
                    cell.OnRightClick = () => onOnly(grid.CurrentIndex(slot));
                }
            }

            private void ToggleFaction(int index) => Toggle(selector.toggleFactionItems, index);
            private void ToggleUnitType(int index) => Toggle(selector.toggleUnitTypesItems, index);
            private void ToggleVehicleType(int index) => Toggle(selector.toggleVehicleTypesItems, index);

            private void OnlyFaction(int index) => SetOnly(selector.toggleFactionItems, index);
            private void OnlyUnitType(int index) => SetOnly(selector.toggleUnitTypesItems, index);
            private void OnlyVehicleType(int index) => SetOnly(selector.toggleVehicleTypesItems, index);

            private void Toggle(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                CancelDeleteConfirm();
                entries[index].Toggle();
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void SetOnly(List<TargetListSelector_ToggleButton> entries, int index)
            {
                if (!Ready) return;
                if (entries == null || index < 0 || index >= entries.Count || entries[index] == null) return;
                CancelDeleteConfirm();
                selector.SetOnlyItem(entries[index]);
                selector.NeedUpdateIcons();
                RequestRefresh();
            }

            private void RefreshSelectedGrid()
            {
                selectedUnits.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.selectedIcons != null)
                {
                    for (int i = 0; i < map.selectedIcons.Count; i++)
                    {
                        UnitMapIcon icon = map.selectedIcons[i] as UnitMapIcon;
                        if (icon != null && icon.unit != null) selectedUnits.Add(icon.unit);
                    }
                }

                selectedGrid.SetData(selectedUnits.Count,
                    i => TargetUnitLabel(selectedUnits[i]), i => false, null,
                    icons: i => selectedUnits[i].definition == null ? null : selectedUnits[i].definition.mapIcon);
                if (selectedSection != null) selectedSection.SetCaption(selectedUnits.Count + " TRACKED");
                RefreshTrackedProfile();
                for (int i = 0; i < targetGroups.Length; i++)
                    targetGroups[i].RemoveAll(unit => unit == null || unit.disabled);
                groupGrid.SetData(targetGroups.Length,
                    i => "GROUP " + (i + 1) + " · " + targetGroups[i].Count,
                    i => targetGroups[i].Count > 0,
                    RecallGroup,
                    details: i => "Group " + (i + 1) + " holds " + targetGroups[i].Count + " targets. Left click recalls the group onto the " +
                                  "target list; right click stores up to 32 of the currently selected targets in it.",
                    subs: i => targetGroups[i].Count + " STORED");
            }

            private int SelectedCount()
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                return map == null || map.selectedIcons == null ? 0 : map.selectedIcons.Count;
            }

            private bool Ready => selector != null && selector.toggleFollowHUD != null &&
                                  selector.toggleLaser != null && selector.toggleFactionItems != null &&
                                  selector.toggleUnitTypesItems != null &&
                                  selector.toggleVehicleTypesItems != null && selector.toggleFactionItems.Count > 0 &&
                                  selector.toggleUnitTypesItems.Count > 0 && selector.toggleVehicleTypesItems.Count > 0;

            private void SetFilterInput(bool enabled)
            {
                if (resetFilters != null) resetFilters.Interactable = enabled;
                if (clearTargets != null) clearTargets.Interactable = enabled;
                if (followHud != null) followHud.Interactable = enabled;
                if (laser != null) laser.Interactable = enabled;
                factionGrid?.SetInteractable(enabled);
                unitGrid?.SetInteractable(enabled);
                vehicleGrid?.SetInteractable(enabled);
                selectedGrid?.SetInteractable(enabled);
                candidateGrid?.SetInteractable(enabled);
                if (missilePreference != null) missilePreference.Interactable = enabled;
                if (weaponPreference != null) weaponPreference.Interactable = enabled;
                if (airPreference != null) airPreference.Interactable = enabled;
                if (rangePreference != null) rangePreference.Interactable = enabled;
                if (nextCandidate != null) nextCandidate.Interactable = enabled && candidates.Count > 0;
                if (incomingCandidate != null) incomingCandidate.Interactable = enabled;
                if (designateCandidate != null) designateCandidate.Interactable = enabled && candidateFocus != null;
                groupGrid?.SetInteractable(enabled);
                quickGrid?.SetInteractable(enabled);
                presetGrid?.SetInteractable(enabled);
                if (saveAs != null) saveAs.Interactable = enabled;
                if (updatePreset != null) updatePreset.Interactable = enabled;
                if (renamePreset != null) renamePreset.Interactable = enabled;
                if (deletePreset != null) deletePreset.Interactable = enabled;
                if (!enabled)
                {
                    editorMode = EditMode.None;
                    editorPart?.Hide();
                    confirmDelete = null;
                    const string waiting = "Waiting for target filters.";
                    if (clearTargets != null) clearTargets.Help = waiting;
                    if (updatePreset != null) updatePreset.Help = waiting;
                    if (renamePreset != null) renamePreset.Help = waiting;
                    if (deletePreset != null) deletePreset.Help = waiting;
                }
                else
                {
                    // A disabled preset action states the one thing it is waiting for,
                    // the way the camera and map preset buttons do.
                    bool custom = SelectedCustom() != null;
                    if (updatePreset != null) updatePreset.Interactable = custom;
                    if (renamePreset != null) renamePreset.Interactable = custom;
                    if (deletePreset != null) deletePreset.Interactable = custom;
                    const string selectFirst = "Select a saved preset first.";
                    if (updatePreset != null)
                        updatePreset.Help = custom ? "Overwrite the selected saved preset with the current filters." : selectFirst;
                    if (renamePreset != null)
                        renamePreset.Help = custom ? "Rename the selected saved preset." : selectFirst;
                    if (deletePreset != null)
                        deletePreset.Help = custom
                            ? "Delete the selected saved preset. Deletion asks for a second press." : selectFirst;
                }
            }

            private static int CountEnabled(List<TargetListSelector_ToggleButton> entries)
            {
                if (entries == null) return 0;
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i] != null && entries[i].status) count++;
                return count;
            }

            /// <summary>
            /// The native filter's full name. It is not cut here: the cell wraps it over
            /// two lines, so "GROUND VEHICLES" never prints as "GND".
            /// </summary>
            private static string NativeTargetLabel(TargetListSelector_ToggleButton entry)
            {
                if (entry != null && entry.label != null && !string.IsNullOrEmpty(entry.label.text))
                {
                    string label = entry.label.text.Replace("\n", " ").ToUpperInvariant();
                    switch (label)
                    {
                        case "AIR": return "AIRCRAFT";
                        case "MSL": return "MISSILES";
                        case "GND": return "GROUND";
                        case "BLD": return "BUILDINGS";
                        case "SHP": return "SHIPS";
                        default: return label;
                    }
                }
                return NativeLabel(entry, "FILTER");
            }

            private static string TargetUnitLabel(Unit unit)
            {
                // The selected grid wraps or shrinks the whole call sign; a unit's own
                // name is never cut to a fixed column.
                if (unit == null) return "UNKNOWN TARGET";
                string code = unit.definition == null ? "UNIT" : unit.definition.code;
                string name = string.IsNullOrEmpty(unit.unitName) ? code : unit.unitName;
                return (string.IsNullOrEmpty(name) ? "UNIT" : name).ToUpperInvariant();
            }
        }
    }
}
