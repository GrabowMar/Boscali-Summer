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

            // Range gates: contacts per band, the same steps the range filter cycles through.
            private static readonly float[] BandFloorKm = { 0f, 10f, 25f, 50f, 100f };
            private static readonly string[] BandNames = { "0-10 KM", "10-25 KM", "25-50 KM", "50-100 KM", "OVER 100 KM" };
            private AvHazardBar[] rangeBands;
            private AvSection rangeSection;
            private AvNote rangeNote;
            private AvRow focusRow;
            private GateBoard filterGate, presetGate;
            private AvSection gateSection;
            private int gateStamp = int.MinValue;
            private string presetGateKey = "";
            private AvEqualizer trackedProfile;
            private AvNote trackedNote;
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
            private const int SelectedVisible = 10;

            // Camera surface mark: state lives in Support through a narrow contract.
            private ICameraTargetService cameraService;
            private AvSlab cameraSlab;
            private AvFlow cameraPage;
            private AvGauge[] cameraRings;
            private AvHazardBar cameraAge;
            // Mark chain, in flow order: [2] camera view, [0] grid, [1] armed call-in, [3] delivery.
            private readonly AvRow[] cameraRows = new AvRow[4];
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

            protected override string Title => PageTitles[0];
            protected override (AvIcon Icon, string Label)[] TabItems => Pages;

            private static readonly string[] PageTitles =
            {
                "TARGETING / ENGAGEMENT FILTERS", "TARGETING / TRACK ACQUISITION", "TARGETING / FILTER PROFILES",
                "TARGETING / TRACK FILE", "TARGETING / SENSOR MARK",
            };

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

                // A page that ends above the footer prints these on its bay cover, most important first.
                Console.Orders(0,
                    "Left-click a tile to open or close it; right-click keeps only that one.",
                    "A contact reaches the list only when its side, class and platform are all open.",
                    "HUD LINK lets the HUD mode set the filters; unlinking resets them to ALL.",
                    "RESET returns every gate to the ALL profile.");
                Console.Orders(1,
                    "Preview a contact to light it on the map; DESIGNATE adds it to the target list.",
                    "Contacts are listed nearest first, from positions your side actually knows.",
                    "FIT ON keeps only contacts the selected weapon can engage right now.",
                    "The range button cycles the reach through ALL, 10, 25, 50 and 100 km.");
                Console.Orders(2,
                    "Left-click a profile to apply it; right-click assigns it to a quick slot.",
                    "Quick slots answer to their keys and to the radial wheel.",
                    "UPDATE overwrites the selected saved profile with the gates as they stand.",
                    "Built-in profiles are fixed. Save your own with SAVE AS.");
                Console.Orders(3,
                    "Left-click a group to recall it; right-click stores the tracked contacts in it.",
                    "Right-click a tracked contact to drop it from the file.",
                    "A group keeps up to 32 contacts and forgets any that are destroyed.",
                    "CLEAR on the FILTERS page drops every tracked contact at once.");
                Console.Orders(4,
                    "MARK records the surface point under the targeting camera cross-hair.",
                    "A mark lapses 120 seconds after it is taken. MARK again to refresh it.",
                    "Arm an operation on OPS / SUPPORT, then CALL AT MARK to deliver it.",
                    "CALL AT MARK stays dark until a mark is held and an operation is armed.");
            }

            protected override void OnPageChanged(int index)
            {
                Console.SetTitle(PageTitles[Mathf.Clamp(index, 0, PageTitles.Length - 1)]);
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
                    filterGate.Wait();
                    presetGate.Wait();
                    gateStamp = int.MinValue;
                    presetGateKey = "";
                    gateSection.SetCaption("FILTERS OFFLINE");
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
                RefreshGate(tracked);
                RefreshPresetGate();

                bool onProfile = !selector.toggleFollowHUD.status && selectedPreset == activePreset;
                presetReadout.Set(selector.toggleFollowHUD.status ? "HUD LINK" : selectedPreset,
                    selector.toggleFollowHUD.status ? "FILTERS FOLLOW THE HUD"
                    : onProfile ? "ACTIVE PROFILE" : "SELECTED · NOT ACTIVE", DescribeSelected());
            }

            protected override string AmbientStatus()
            {
                if (!string.IsNullOrEmpty(echo) && Time.unscaledTime < echoUntil) return echo;
                int page = Console.CurrentPage;
                if (page == 1) return "PICK · DESIGNATE";
                if (page == 2) return "L APPLY · R QUICK SLOT";
                if (page == 3) return "L RECALL · R STORE / DROP";
                if (page == 4) return "MARK · CALL AT MARK";
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
                    new AvGauge(page.Content, "FACTION"),
                    new AvGauge(page.Content, "CLASS"),
                    new AvGauge(page.Content, "PLATFORM"),
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

                page.Section(AvIcon.Shield, "FACTION GATE", "R-CLICK: ONLY THIS");
                factionGrid = AddGrid(page, new MfdPagingGrid(page.Content, 2, 1, pager: false, rowHeight: 56f, tile: true));
                factionGrid.SetGlyphs(i => FilterGlyph(selector == null ? null : selector.toggleFactionItems, i,
                    i == 0 ? "FRIENDLY" : i == 1 ? "ENEMY" : ""));
                AddRightClickActions(factionGrid, 2, OnlyFaction);

                page.Section(AvIcon.LayersSubtract, "CLASS GATE", "R-CLICK: ONLY THIS");
                unitGrid = AddGrid(page, new MfdPagingGrid(page.Content, 3, 2, pager: false, rowHeight: 56f, tile: true));
                unitGrid.SetGlyphs(i => FilterGlyph(selector == null ? null : selector.toggleUnitTypesItems, i,
                    i >= 0 && i < UnitFallback.Length ? UnitFallback[i] : ""));
                AddRightClickActions(unitGrid, 6, OnlyUnitType);

                page.Section(AvIcon.Stack2, "PLATFORM GATE", "R-CLICK: ONLY THIS");
                vehicleGrid = AddGrid(page, new MfdPagingGrid(page.Content, 5, 2, pager: false, rowHeight: 56f, tile: true));
                vehicleGrid.SetGlyphs(i => VehicleGlyph(
                    selector != null && selector.toggleVehicleTypesItems != null && i >= 0 && i < selector.toggleVehicleTypesItems.Count
                        ? NativeTargetLabel(selector.toggleVehicleTypesItems[i]) : "", i));
                AddRightClickActions(vehicleGrid, 10, OnlyVehicleType);

                gateSection = page.Section(AvIcon.Eye, "TRACK FILE GATE", "LIST EMPTY");
                filterGate = new GateBoard(page);
            }

            // ----------------------------------------------------- gate summary

            /// <summary>Four list rows that put a set of target gates into words: sides, classes, platforms and
            /// the two overrides. The FILTERS page reads the live toggles into one, the PRESETS page a saved profile.</summary>
            private sealed class GateBoard
            {
                private static readonly string[] Names = { "SIDES", "CLASSES", "PLATFORMS", "LINKS" };
                private readonly AvRow[] rows = new AvRow[4];

                public GateBoard(AvFlow page)
                {
                    for (int i = 0; i < rows.Length; i++) rows[i] = page.Add(new AvRow(page.Content));
                    Wait();
                }

                public void Wait()
                {
                    for (int i = 0; i < rows.Length; i++) rows[i].Set(Names[i], "Waiting for the target filters.", "—", AvState.Inert);
                }

                public void Set(string sides, int sideOpen, int sideTotal, string classes, int classOpen, int classTotal,
                    string platforms, int platformOpen, int platformTotal, string linkName, string links, string linkValue,
                    AvState linkState)
                {
                    rows[0].Set(Names[0], sides, Fraction(sideOpen, sideTotal), State(sideOpen, sideTotal));
                    rows[1].Set(Names[1], classes, Fraction(classOpen, classTotal), State(classOpen, classTotal));
                    rows[2].Set(Names[2], platforms, Fraction(platformOpen, platformTotal), State(platformOpen, platformTotal));
                    rows[3].Set(linkName, links, linkValue, linkState);
                }

                private static string Fraction(int open, int total) => AvNum.Fixed(open, 0) + "/" + AvNum.Fixed(total, 0);

                private static AvState State(int open, int total) =>
                    total == 0 ? AvState.Inert : open == total ? AvState.Ready : open == 0 ? AvState.Caution : AvState.Info;
            }

            /// <summary>
            /// A gate list in words, shortest form first: the open names when few are open, "ALL BUT" the closed ones
            /// when most are. <paramref name="allOpen"/> replaces the list when nothing is closed.
            /// </summary>
            private static string GateWords(List<TargetListSelector_ToggleButton> entries, bool[] on, out int open, string allOpen = null)
            {
                int total = entries == null ? 0 : entries.Count;
                open = 0;
                for (int i = 0; i < total; i++) if (on[i]) open++;
                if (total == 0) return "—";
                if (open == total && allOpen != null) return allOpen;
                if (open == 0) return "NONE OPEN";
                bool listOpen = open * 2 <= total;
                var text = new System.Text.StringBuilder(listOpen ? "" : "ALL BUT ");
                bool first = true;
                for (int i = 0; i < total; i++)
                {
                    if (on[i] != listOpen) continue;
                    if (!first) text.Append(" · ");
                    text.Append(NativeTargetLabel(entries[i]));
                    first = false;
                }
                return text.ToString();
            }

            private static string LaserWords(bool laser) =>
                laser ? "LASER ONLY ON — UNLASED CONTACTS STAY OFF THE LIST" : "LASER ONLY OFF — LASED OR NOT";

            private void RefreshGate(int tracked)
            {
                gateSection.SetCaption(tracked > 0 ? tracked + " ON LIST" : "LIST EMPTY");
                if (gateStamp == activeStamp) return;   // the stamp covers every toggle, the laser and the HUD link
                gateStamp = activeStamp;
                FillLive(filterGate);
            }

            /// <summary>The filters as they stand right now.</summary>
            private void FillLive(GateBoard board)
            {
                bool[] sides = Opened(selector.toggleFactionItems), classes = Opened(selector.toggleUnitTypesItems),
                    platforms = Opened(selector.toggleVehicleTypesItems);
                int sideOpen, classOpen, platformOpen;
                string sideWords = GateWords(selector.toggleFactionItems, sides, out sideOpen);
                string classWords = GateWords(selector.toggleUnitTypesItems, classes, out classOpen, "EVERY CLASS");
                string platformWords = GateWords(selector.toggleVehicleTypesItems, platforms, out platformOpen, "EVERY VEHICLE TYPE");
                bool hud = selector.toggleFollowHUD.status, laserOnly = selector.toggleLaser.status;
                board.Set(WithConsequence(sideWords, sideOpen, "LIST STAYS EMPTY"), sideOpen, sides.Length,
                    WithConsequence(classWords, classOpen, "LIST STAYS EMPTY"), classOpen, classes.Length,
                    WithConsequence(platformWords, platformOpen, "NO VEHICLES LISTED"), platformOpen, platforms.Length,
                    "LINKS", (hud ? "HUD LINK ON — THE HUD MODE SETS THE GATES" : "HUD LINK OFF") + "  ·  " + LaserWords(laserOnly),
                    hud ? "LINKED" : laserOnly ? "LASED" : "FREE", hud || laserOnly ? AvState.Caution : AvState.Inert);
            }

            private static string WithConsequence(string words, int open, string consequence) =>
                open == 0 && words != "—" ? words + " — " + consequence : words;

            private static bool[] Opened(List<TargetListSelector_ToggleButton> entries)
            {
                var on = new bool[entries == null ? 0 : entries.Count];
                for (int i = 0; i < on.Length; i++) on[i] = entries[i] != null && entries[i].status;
                return on;
            }

            /// <summary>The same four lines for the profile selected in the library, read from its stored gates.</summary>
            private void FillPreset(GateBoard board, TargetPresetSnapshot preset)
            {
                TargetPresetState state = TargetPresetRuntime.CaptureState(selector);
                var sides = new bool[selector.toggleFactionItems.Count];
                for (int i = 0; i < sides.Length; i++)
                    sides[i] = selector.toggleFactionItems[i] != null &&
                               TargetPresetRules.FactionExpected(preset, selector.toggleFactionItems[i].sameFaction);
                var classes = new bool[state.ClassTokens.Length];
                for (int i = 0; i < classes.Length; i++) classes[i] = TargetPresetRules.Includes(state.ClassTokens[i], preset.Classes);
                var platforms = new bool[state.VehicleTokens.Length];
                for (int i = 0; i < platforms.Length; i++) platforms[i] = TargetPresetRules.Includes(state.VehicleTokens[i], preset.Vehicles);
                int sideOpen, classOpen, platformOpen;
                string sideWords = GateWords(selector.toggleFactionItems, sides, out sideOpen);
                string classWords = GateWords(selector.toggleUnitTypesItems, classes, out classOpen, "EVERY CLASS");
                string platformWords = GateWords(selector.toggleVehicleTypesItems, platforms, out platformOpen, "EVERY VEHICLE TYPE");
                board.Set(WithConsequence(sideWords, sideOpen, "LIST STAYS EMPTY"), sideOpen, sides.Length,
                    WithConsequence(classWords, classOpen, "LIST STAYS EMPTY"), classOpen, classes.Length,
                    WithConsequence(platformWords, platformOpen, "NO VEHICLES LISTED"), platformOpen, platforms.Length,
                    "LASER", LaserWords(preset.Laser), preset.Laser ? "LASED" : "ANY", preset.Laser ? AvState.Caution : AvState.Inert);
            }

            private void RefreshPresetGate()
            {
                if (Console.CurrentPage != 2) return;
                int index = TargetPresetRuntime.IndexOfCatalogName(selectedPreset);
                TargetPresetSnapshot preset = index >= 0 && index < catalog.Count ? catalog[index] : null;
                // A hand-set profile has no stored form: it is read from the live gates, so it follows their stamp.
                string key = selectedPreset + "|" + catalogVersion + "|" + catalogStamp + "|" + (preset == null ? activeStamp : 0);
                if (key == presetGateKey) return;
                presetGateKey = key;
                if (preset == null) FillLive(presetGate);
                else FillPreset(presetGate, preset);
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
                public int BearingDeg;
                public string Side;
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

                contactsSection = page.Section(AvIcon.Eye, "KNOWN CONTACTS", "0 MATCH");
                focusRow = page.Add(new AvRow(page.Content));
                focusRow.Set("NO TRACK IN FOCUS", "Pick a contact below to preview it on the map.", "—", AvState.Inert);
                focusRow.Help = "Focus track: the contact previewed on the map, with its side, class, bearing and range from your aircraft. Known position only.";
                candidateGrid = AddGrid(page, new MfdPagingGrid(page.Content, 1, 8, rowHeight: 40f));
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

                rangeSection = page.Section(AvIcon.Ruler2, "RANGE GATES", "NONE HELD");
                rangeNote = page.Add(new AvNote(page.Content, "NO CONTACTS HELD. The gates draw once a contact is known."));
                rangeBands = new AvHazardBar[BandNames.Length];
                for (int i = 0; i < rangeBands.Length; i++)
                {
                    rangeBands[i] = new AvHazardBar(page.Content, BandNames[i]);
                    rangeBands[i].Set(0f, "—", AvState.Inert);
                    rangeBands[i].Help = "Range gate " + BandNames[i].ToLowerInvariant() +
                                         ": contacts on the list above that lie in this distance band from your aircraft, scaled to the busiest band.";
                    page.Add(rangeBands[i]).SetShown(false);
                }
            }

            /// <summary>Contacts per range gate. With none held the five bars collapse to a one-line note.</summary>
            private void SetRangeBands(int[] counts, int limitKm, bool flying)
            {
                int total = 0, peak = 0;
                for (int i = 0; i < counts.Length; i++) { total += counts[i]; peak = Mathf.Max(peak, counts[i]); }
                rangeSection.SetCaption(total > 0 ? total + " BY BAND" : "NONE HELD");
                rangeNote.Set(flying ? "NO CONTACTS HELD. The gates draw once a contact is known."
                                     : "NO AIRCRAFT. The gates measure from your own position.");
                rangeNote.SetShown(total == 0);
                for (int i = 0; i < rangeBands.Length; i++)
                {
                    rangeBands[i].SetShown(total > 0);
                    bool beyond = limitKm > 0 && BandFloorKm[i] >= limitKm;   // the range filter already excludes this band
                    rangeBands[i].Set(peak > 0 ? counts[i] / (float)peak : 0f, beyond ? "BEYOND LIMIT" : AvNum.Fixed(counts[i], 0),
                        beyond || counts[i] == 0 ? AvState.Inert : AvState.Info);
                }
            }

            private readonly int[] bandCounts = new int[BandNames.Length];
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
                    subs: i => AvNum.Fixed(candidates[i].DistanceKm, 1) + " KM · BRG " +
                               candidates[i].BearingDeg.ToString("000") + " · " + candidates[i].Side);
                RefreshFocus(flying);
                for (int i = 0; i < bandCounts.Length; i++) bandCounts[i] = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    int band = 0;
                    while (band + 1 < BandFloorKm.Length && candidates[i].DistanceKm >= BandFloorKm[band + 1]) band++;
                    bandCounts[band]++;
                }
                SetRangeBands(bandCounts, settings.TargetRangeKm.Value, flying);
                if (contactsSection != null) contactsSection.SetCaption(flying ? candidates.Count + " MATCH · NEAREST FIRST" : "NO AIRCRAFT");
                nextCandidate.Interactable = candidates.Count > 0;
                designateCandidate.Interactable = candidateFocus != null && candidates.Exists(c => c.Unit == candidateFocus);
            }

            /// <summary>The previewed contact as one track line: callsign, side, class, bearing and range (known position only).</summary>
            private void RefreshFocus(bool flying)
            {
                int index = candidates.FindIndex(c => c.Unit == candidateFocus);
                if (index < 0)
                {
                    focusRow.Set("NO TRACK IN FOCUS", flying ? "Pick a contact below to preview it on the map."
                        : "No aircraft: there is nothing to measure from.", "—", AvState.Inert);
                    return;
                }
                TargetCandidate c = candidates[index];
                AvState state;
                SideWord(c.Unit, out state);
                focusRow.Set(TargetUnitLabel(c.Unit), c.Side + " · " + ClassWord(c.Unit) + " · BRG " + c.BearingDeg.ToString("000"),
                    AvNum.Fixed(c.DistanceKm, 1) + " KM", state);
            }

            private static string SideWord(Unit unit, out AvState state)
            {
                switch (DynamicMap.GetFactionMode(unit.NetworkHQ, true))
                {
                    case FactionMode.Friendly: state = AvState.Info; return "FRIENDLY";
                    case FactionMode.Enemy: state = AvState.Danger; return "HOSTILE";
                    default: state = AvState.Caution; return "UNKNOWN";
                }
            }

            private static string ClassWord(Unit unit)
            {
                switch (unit.definition == null ? "" : unit.definition.GetType().Name)
                {
                    case "AircraftDefinition": return "AIRCRAFT";
                    case "MissileDefinition": return "MISSILE";
                    case "VehicleDefinition": return "VEHICLE";
                    case "BuildingDefinition": return "STRUCTURE";
                    case "ShipDefinition": return "SHIP";
                    default: return "UNIT";
                }
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
                    GlobalPosition own = hud.aircraft.GlobalPosition();
                    float distance = FastMath.Distance(own, known) / 1000f;
                    if (float.IsNaN(distance) || float.IsInfinity(distance) ||
                        (settings.TargetRangeKm.Value > 0 && distance > settings.TargetRangeKm.Value)) continue;
                    if (settings.TargetWeaponOnly.Value &&
                        (station == null || station.CalcOpportunityThreat(unit.definition, hud.aircraft).opportunity <= 0f))
                        continue;
                    int insert = candidates.FindIndex(c => c.DistanceKm > distance);
                    if (insert < 0) insert = candidates.Count;
                    if (insert >= 128) continue;
                    Vector3 offset = known - own;
                    AvState side;
                    candidates.Insert(insert, new TargetCandidate
                    {
                        Unit = unit,
                        DistanceKm = distance,
                        BearingDeg = ((Mathf.RoundToInt(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg) % 360) + 360) % 360,
                        Side = SideWord(unit, out side),
                    });
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

                // The selected profile, in words: what UPDATE, RENAME and DELETE would act on.
                page.Section(AvIcon.Bookmark, "PROFILE CARD", "SELECTED PROFILE");
                presetReadout = page.Add(new AvReadout(page.Content));
                presetGate = new GateBoard(page);
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
                if (index < 0) return selectedPreset == TargetPresetLibrary.CustomProfile
                    ? "Hand-set filters. SAVE AS keeps them as a profile." : "—";
                if (index < TargetPresetRuntime.BuiltInCount)
                    return "Built-in. " + MfdTargetPresets.Descriptions[index];
                TargetPresetSnapshot preset = TargetPresetRuntime.Library.At(
                    index - TargetPresetRuntime.BuiltInCount);
                return preset == null ? "" : TargetPresetRules.Summary(preset);
            }

            // ------------------------------------------------------------ selected

            private void BuildSelectedPage(AvFlow page)
            {
                selectedSection = page.Section(AvIcon.Target, "TRACK FILE", "NONE HELD");

                groupGrid = AddGrid(page, new MfdPagingGrid(page.Content, 3, 1, pager: false, rowHeight: 56f, tile: true));
                groupGrid.SetGlyphs(i => AvIcon.UsersGroup);
                AddGroupActions();

                selectedGrid = AddGrid(page, new MfdPagingGrid(page.Content, 1, SelectedVisible, readOnly: true, rowHeight: 44f));
                selectedGrid.SetEmptyMessage("NO TARGETS HELD");
                AddRightClickActions(selectedGrid, SelectedVisible, DeselectSelected);

                // An empty chart is one line of text until a tracked target has a position to plot.
                trackedNote = page.Add(new AvNote(page.Content, "NO RANGE PROFILE. It draws once a tracked target has a known position."));
                trackedProfile = new AvEqualizer(page.Content, "RANGE PROFILE", 36f);
                trackedProfile.Set(trackedBars, "—", AvState.Inert);
                trackedProfile.Help = "Range profile: one bar per tracked target in list order, tall for far and short for near, scaled to the farthest. " +
                                      "Right-click a target in the list to drop it.";
                page.Add(trackedProfile).SetShown(false);
            }

            /// <summary>Distance of each tracked target from the player, as bars scaled to the farthest.</summary>
            private void RefreshTrackedProfile()
            {
                for (int i = 0; i < trackedBars.Length; i++) trackedBars[i] = 0f;
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                float far = 0f, near = float.MaxValue;
                int known = 0;
                if (map != null && map.HQ != null && hud != null && hud.aircraft != null)
                {
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
                }
                trackedProfile.SetShown(known > 0);
                trackedNote.SetShown(known == 0);
                if (known == 0)
                {
                    trackedNote.Set(selectedUnits.Count == 0
                        ? "NO TARGETS TRACKED. The range profile draws once one is."
                        : "NO KNOWN POSITION. The range profile needs a position your side holds.");
                    trackedProfile.Set(trackedBars, "—", AvState.Inert);
                    return;
                }
                for (int i = 0; i < trackedBars.Length; i++)
                    if (trackedBars[i] > 0f) trackedBars[i] = Mathf.Max(0.08f, trackedBars[i] / Mathf.Max(0.01f, far));
                trackedProfile.Set(trackedBars, "NEAREST " + AvNum.Fixed(near, 1) + " KM", AvState.Info);
            }

            /// <summary>One line under a tracked target: class, side and range from the player (known position only).</summary>
            private static string TrackSub(Unit unit)
            {
                if (unit == null) return "";
                AvState state;
                string head = ClassWord(unit) + " · " + SideWord(unit, out state);
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                GlobalPosition at;
                if (map == null || map.HQ == null || hud == null || hud.aircraft == null ||
                    !map.HQ.TryGetKnownPosition(unit, out at)) return head + " · POSITION UNKNOWN";
                float km = FastMath.Distance(hud.aircraft.GlobalPosition(), at) / 1000f;
                return float.IsNaN(km) || float.IsInfinity(km) ? head + " · POSITION UNKNOWN" : head + " · " + AvNum.Fixed(km, 1) + " KM";
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
                cameraPage = page;
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
                    new AvGauge(page.Content, "RANGE KM"),
                    new AvGauge(page.Content, "ELEV M"),
                    new AvGauge(page.Content, "BRG DEG"),
                };
                cameraRings[0].Help = "Range: distance from your aircraft to the mark, full ring at 20 km.";
                cameraRings[1].Help = "Elevation: height of the marked surface point above sea level, full ring at 3000 m.";
                cameraRings[2].Help = "Bearing: true bearing from your aircraft to the mark, one full turn around the ring.";
                page.Row(cameraRings);

                // The chain from camera to delivery, one link per row, in the order the pilot works through it.
                page.Section(AvIcon.Camera, "MARK CHAIN", "CAMERA TO DELIVERY");
                string[] links = { "GRID (X / Z)", "ARMED CALL-IN", "CAMERA VIEW", "DELIVERY" };
                int[] order = { 2, 0, 1, 3 };
                foreach (int slot in order)
                {
                    cameraRows[slot] = page.Add(new AvRow(page.Content));
                    cameraRows[slot].Set(links[slot], "Waiting for the camera service.", "—", AvState.Inert);
                }

                cameraLife = new AvEqualizer(page.Content, "MARK LIFE", 36f);
                cameraLife.Set(cameraLifeBars, "—", AvState.Inert);
                cameraLife.Help = "Mark life: twelve steps of ten seconds. Each lit step is time the mark has left before it expires.";
                page.Add(cameraLife).SetShown(false);   // nothing to count down until a mark is held
            }

            /// <summary>A slab is placed to its text width; a new word needs a new placement, which the flow only gives on a height change.</summary>
            private void SetCameraSlab(string word, AvState state)
            {
                cameraSlab.Set(word, state);
                if (word != cameraSlabWord) cameraPage.RequestRelayout();
                cameraSlabWord = word;
            }

            private string cameraSlabWord;

            private void RefreshCamera()
            {
                if (cameraSlab == null) return;
                ICameraTargetService service = Camera;
                if (service == null || !service.Available)
                {
                    SetCameraSlab("OFFLINE", AvState.Inert);
                    if (cameraCapture != null) cameraCapture.Interactable = false;
                    if (cameraCall != null) cameraCall.Interactable = false;
                    if (cameraClear != null) cameraClear.Interactable = false;
                    const string cameraOffline =
                        "Camera marking is unavailable: the support module or its observation source is not installed.";
                    if (cameraCapture != null) cameraCapture.Help = cameraOffline;
                    if (cameraCall != null) cameraCall.Help = cameraOffline;
                    if (cameraClear != null) cameraClear.Help = cameraOffline;
                    SetCameraTelemetry("—", "", false, 0f, 0f, 0f, -1);
                    cameraRows[2].Set("CAMERA VIEW", "Marking is offline: the support module or its observation source is not installed.",
                        "OFFLINE", AvState.Inert);
                    cameraRows[3].Set("DELIVERY", "Needs the camera marking service.", "OFFLINE", AvState.Inert);
                    return;
                }

                bool marked = service.HasMark;
                string armedName = service.ArmedActionName;
                bool armed = !string.IsNullOrEmpty(armedName);
                if (marked)
                {
                    ObservationPoint point = service.Mark;
                    SetCameraSlab((point.Source ?? "SENSOR").ToUpperInvariant() + " MARK", AvState.Ready);
                    int bearing = -1;
                    CombatHUD hud = SceneSingleton<CombatHUD>.i;
                    if (hud != null && hud.aircraft != null && !hud.aircraft.disabled)
                    {
                        GlobalPosition own = hud.aircraft.GlobalPosition();
                        float turn = Mathf.Atan2(point.X - own.x, point.Z - own.z) * Mathf.Rad2Deg;
                        if (!float.IsNaN(turn) && !float.IsInfinity(turn)) bearing = ((Mathf.RoundToInt(turn) % 360) + 360) % 360;
                    }
                    SetCameraTelemetry(
                        "X " + AvNum.Fixed(point.X, 0) + " · Z " + AvNum.Fixed(point.Z, 0),
                        armedName, true, Mathf.Max(0f, point.Range / 1000f), point.Y, Mathf.Max(0f, service.AgeSeconds), bearing);
                }
                else
                {
                    SetCameraSlab("NO MARK", AvState.Inert);
                    SetCameraTelemetry("", armedName, false, 0f, 0f, 0f, -1);
                }

                bool view = service.CanCapture;
                cameraRows[2].Set("CAMERA VIEW",
                    view ? "Targeting camera is live. MARK records the point under its cross-hair."
                         : "No live camera view on your aircraft. MARK needs one.",
                    view ? "LIVE" : "NO VIEW", view ? AvState.Ready : AvState.Inert);
                bool deliver = service.CanCallAtMark;
                cameraRows[3].Set("DELIVERY",
                    deliver ? "CALL AT MARK sends the armed operation onto the mark."
                        : !marked ? "Needs a mark first." : !armed ? "Needs an armed call-in. Arm one on OPS / SUPPORT."
                        : "Not available right now.",
                    deliver ? "READY" : "WAIT", deliver ? AvState.Ready : AvState.Inert);

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

            /// <summary>Range, elevation and bearing ride rings, the 120 s expiry a hazard bar; grid and armed call-in
            /// stay as rows so a long action name can wrap. <paramref name="armed"/> is empty when nothing is armed,
            /// <paramref name="bearing"/> negative when it could not be established.</summary>
            private void SetCameraTelemetry(string position, string armed, bool marked, float rangeKm,
                float elevationM, float ageSeconds, int bearing)
            {
                cameraRings[0].Set(marked ? Mathf.Clamp01(rangeKm / 20f) : 0f,
                    marked ? AvNum.Fixed(rangeKm, 1) : "\u2014", marked ? AvState.Info : AvState.Inert);
                cameraRings[1].Set(marked ? Mathf.Clamp01(elevationM / 3000f) : 0f,
                    marked ? AvNum.Fixed(elevationM, 0) : "\u2014", marked ? AvState.Info : AvState.Inert);
                cameraRings[2].Set(marked && bearing >= 0 ? bearing / 360f : 0f,
                    marked && bearing >= 0 ? bearing.ToString("000") : "\u2014", marked && bearing >= 0 ? AvState.Info : AvState.Inert);
                cameraAge.Set(marked ? Mathf.Clamp01(1f - ageSeconds / 120f) : 0f,
                    marked ? AvNum.Fixed(ageSeconds, 0) + " S" : "—",
                    !marked ? AvState.Inert : ageSeconds > 90f ? AvState.Caution : AvState.Info);
                for (int i = 0; i < cameraLifeBars.Length; i++)
                    cameraLifeBars[i] = marked ? ((120f - ageSeconds) > i * 10f ? 1f : 0.12f) : 0.12f;
                cameraLife.Set(cameraLifeBars, marked ? AvNum.Fixed(Mathf.Max(0f, 120f - ageSeconds), 0) + " S LEFT" : "—",
                    !marked ? AvState.Inert : ageSeconds > 90f ? AvState.Caution : AvState.Info);
                cameraLife.SetShown(marked);
                cameraRows[0]?.Set("GRID (X / Z)", marked ? position : "No point recorded.", marked ? "HELD" : "NONE",
                    marked ? AvState.Info : AvState.Inert);
                bool isArmed = !string.IsNullOrEmpty(armed);
                cameraRows[1]?.Set("ARMED CALL-IN", isArmed ? armed : "None armed. Arm one on OPS / SUPPORT.",
                    isArmed ? "ARMED" : "NONE", isArmed ? AvState.Caution : AvState.Inert);
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

                bool onPage = Console.CurrentPage == 3;   // the sub-lines only matter while this page shows
                selectedGrid.SetData(selectedUnits.Count,
                    i => TargetUnitLabel(selectedUnits[i]), i => false, null,
                    icons: i => selectedUnits[i].definition == null ? null : selectedUnits[i].definition.mapIcon,
                    subs: onPage ? (Func<int, string>)(i => TrackSub(selectedUnits[i])) : null);
                if (selectedSection != null)
                    selectedSection.SetCaption(selectedUnits.Count > 0 ? selectedUnits.Count + " HELD" : "NONE HELD");
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
