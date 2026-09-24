using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
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

            private RectTransform[] pages;
            private MfdPagingGrid factionGrid;
            private MfdPagingGrid unitGrid;
            private MfdPagingGrid vehicleGrid;
            private TMPro.TMP_Text filterCountReadout;
            private TMPro.TMP_Text filterProfileReadout;
            private Image filterGauge;
            private MfdPagingGrid selectedGrid;
            private MfdPagingGrid candidateGrid;
            private readonly List<TargetCandidate> candidates = new List<TargetCandidate>(128);
            private Unit candidateFocus;
            private float nextCandidateScan;
            private AvButton missilePreference;
            private AvButton weaponPreference;
            private AvButton airPreference;
            private AvButton rangePreference;
            private AvButton designateCandidate;
            private AvButton nextCandidate;
            private AvButton incomingCandidate;
            private TMPro.TMP_Text candidateNote;
            private readonly List<Unit>[] targetGroups =
                { new List<Unit>(16), new List<Unit>(16), new List<Unit>(16) };
            private readonly AvButton[] groupButtons = new AvButton[3];
            private MfdPagingGrid quickGrid;
            private MfdPagingGrid presetGrid;
            private AvButton resetFilters;
            private AvButton clearTargets;
            private AvButton followHud;
            private AvButton laser;
            private AvButton saveAs;
            private AvButton updatePreset;
            private AvButton renamePreset;
            private AvButton deletePreset;
            private TMPro.TMP_Text presetStatus;
            private TMPro.TMP_Text presetSummary;
            private TMPro.TMP_Text editorLabel;
            private TMPro.TMP_InputField nameField;
            private RectTransform editor;

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
            private TMPro.TMP_Text selectedNote;
            private TMPro.TMP_Text presetNote;
            private int selectedVisible = 9;
            private int presetVisible = 6;

            // Camera surface mark: state lives in Support through a narrow contract.
            private ICameraTargetService cameraService;
            private Image cameraRail;
            private TMPro.TMP_Text cameraState;
            private TMPro.TMP_Text cameraDetails;
            private AvButton cameraCapture;
            private AvButton cameraCall;
            private AvButton cameraClear;
            private TMPro.TMP_Text cameraPos;
            private TMPro.TMP_Text cameraElev;
            private TMPro.TMP_Text cameraRange;
            private TMPro.TMP_Text cameraAge;
            private TMPro.TMP_Text cameraArmed;
            private TMPro.TMP_Text cameraReticleStatus;
            private Image[] cameraReticleBorder;

            public TargetPresenter(MFDScreen screen, TargetListSelector selector)
                : base(screen, VanillaMfdPanelId.Tgt)
            {
                this.selector = selector;
            }

            private ICameraTargetService Camera
            {
                get
                {
                    if (cameraService == null) ModServices.TryGet(out cameraService);
                    return cameraService;
                }
            }

            protected override int TabCount => 5;
            protected override bool PageHasTitle => false;

            private static readonly string[] PageStates =
            {
                "FILTERS / ACQUISITION", "ACQUIRE / CONTACTS", "PRESETS / LIBRARY",
                "TARGETS / TRACKED", "CAMERA / SENSOR MARK"
            };

            private static float TargetHeading(RectTransform page, float y, float width,
                                               string title, string note = null)
            {
                SectionHead head = Head(page, y, width, title, note);
                string kind = title.Contains("CAMERA") ? "eye"
                    : title.Contains("TELEMETRY") ? "chart"
                    : title.Contains("SENSOR") || title.Contains("CONTACT") ? "radar"
                    : title.Contains("FACTION") ? "faction"
                    : title.Contains("PLATFORM") ? "ground"
                    : title.Contains("PRESET") || title.Contains("FILTER") ? "filter"
                    : title.Contains("QUICK") ? "nav" : "target";
                var iconObject = new GameObject("SectionIcon", typeof(RectTransform), typeof(MfdGlyph));
                var icon = iconObject.GetComponent<MfdGlyph>();
                iconObject.transform.SetParent(page, false);
                AvKit.Place(icon.rectTransform, new Rect(AvTokens.Space3, y - 1f, 13f, 13f));
                icon.raycastTarget = false;
                icon.SetKind(kind, AvTheme.RailInfo);
                AvKit.Place(head.Title.rectTransform,
                    new Rect(AvTokens.Space3 + 19f, y,
                        width * .55f - AvTokens.Space3 - 19f, 16f));
                return y - HeadingPitch;
            }

            protected override void BuildContent()
            {
                ConfigureTabs(new[] { "FILTERS", "ACQUIRE", "PRESETS", "TARGETS", "CAMERA" }, SelectPage);
                // The active-profile chip carries the whole saved name: tracking off and
                // the micro floor keep it inside the fixed chip instead of cutting it.
                TMPro.TMP_Text profileChip = Shell.DataBar.Chips[1];
                profileChip.characterSpacing = 0f;
                profileChip.enableAutoSizing = true;
                profileChip.fontSizeMin = AvTokens.FontMicro;
                profileChip.fontSizeMax = profileChip.fontSize;
                profileChip.overflowMode = TMPro.TextOverflowModes.Overflow;
                pages = new[]
                {
                    CreatePage("Filters", 112f), CreatePage("Acquire"), CreatePage("Presets"),
                    CreatePage("Target Deck"), CreatePage("Sensor Mark")
                };
                BuildFiltersPage(pages[0]);
                BuildAcquirePage(pages[1]);
                BuildPresetsPage(pages[2]);
                BuildSelectedPage(pages[3]);
                BuildCameraPage(pages[4]);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                RefreshCamera();

                if (!Ready)
                {
                    SetFilterInput(false);
                    filterCountReadout.text = "—";
                    filterProfileReadout.text = "NO TARGET LINK";
                    if (filterGauge != null) filterGauge.gameObject.SetActive(false);
                    Shell.DataBar.State.text = "FILTER LINK WAIT";
                    Shell.DataBar.SetChip(0, "LINK", false);
                    Shell.DataBar.SetChip(1, "DATA", false);
                    Shell.DataBar.SetChip(2, "—", false);
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
                filterCountReadout.text = filters.ToString("00");
                filterProfileReadout.text = "PROFILE / " + activePreset.ToUpperInvariant();
                if (filterGauge != null)
                {
                    int total = selector.toggleFactionItems.Count + selector.toggleUnitTypesItems.Count +
                                selector.toggleVehicleTypesItems.Count;
                    filterGauge.gameObject.SetActive(total > 0);
                    filterGauge.fillAmount = total > 0 ? filters / (float)total : 0f;
                }
                Shell.DataBar.State.text = PageStates[Mathf.Clamp(Shell.Page, 0, PageStates.Length - 1)];
                Shell.DataBar.SetChip(0, filters + " FILTERS", filters > 0);
                Shell.DataBar.SetChip(1, activePreset,
                                      activePreset != TargetPresetLibrary.CustomProfile);
                Shell.DataBar.SetChip(2, selector.toggleFollowHUD.status ? "HUD LINK" :
                                      selector.toggleLaser.status ? "LASER" : "MANUAL",
                                      selector.toggleFollowHUD.status || selector.toggleLaser.status);

                int tracked = SelectedCount();
                clearTargets.SetEnabled(tracked > 0);
                clearTargets.WithTooltip(tracked > 0
                    ? "Drop every tracked contact from the target list."
                    : "No tracked contacts to clear.");
                PaintButton(followHud, selector.toggleFollowHUD.status ? "HUD ON" : "HUD OFF",
                            selector.toggleFollowHUD.status);
                PaintButton(laser, selector.toggleLaser.status ? "LASER ON" : "LASER OFF",
                            selector.toggleLaser.status);

                SetGrid(factionGrid, selector.toggleFactionItems, ToggleFaction);
                SetGrid(unitGrid, selector.toggleUnitTypesItems, ToggleUnitType);
                SetGrid(vehicleGrid, selector.toggleVehicleTypesItems, ToggleVehicleType);
                RefreshSelectedGrid();
                RefreshAcquire();
                RefreshQuickSlots();
                RefreshLibrary();

                presetStatus.text = selector.toggleFollowHUD.status
                    ? "ACTIVE PROFILE  HUD LINK"
                    : "ACTIVE PROFILE  " + activePreset;
                presetSummary.text = DescribeSelected();
            }

            protected override string AmbientStatus()
            {
                if (!string.IsNullOrEmpty(echo) && Time.unscaledTime < echoUntil) return echo;
                if (Shell == null) return "LEFT CLICK TO TOGGLE — RIGHT CLICK TO SHOW ONLY ONE FILTER";
                if (Shell.Page == 1)
                    return "CHOOSE A CONTACT, THEN DESIGNATE; PREFS AFFECT THIS BROWSER";
                if (Shell.Page == 2)
                    return "LEFT CLICK APPLIES — RIGHT CLICK ASSIGNS A QUICK SLOT";
                if (Shell.Page == 3)
                    return "RIGHT-CLICK GROUP TO SAVE · LEFT-CLICK TO RECALL · RIGHT-CLICK CONTACT TO DROP";
                return "LEFT CLICK TO TOGGLE — RIGHT CLICK TO SHOW ONLY ONE FILTER";
            }

            private void Echo(string text)
            {
                echo = text;
                echoUntil = Time.unscaledTime + 2.4f;
            }

            // ------------------------------------------------------------- filters

            private void BuildFiltersPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float gap = AvTokens.Gap;

                // Keep every filter visible, and let tall bays give each target a larger hit area.
                float cell = Mathf.Clamp((PageHeight - 170f) / 9f, 40f, 60f);

                float y = TargetHeading(page, -AvTokens.Space1, width,
                                  "ACQUISITION GATE", "SENSOR LOGIC");
                Rect gate = new Rect(AvTokens.Space3, y, width - AvTokens.Space3, 84f);
                AvKit.Panel(page, gate, AvTheme.SurfaceInert);
                AvKit.Outline(page, gate, AvTheme.Hairline.WithAlpha(0.65f));
                AvKit.Rule(page, new Rect(gate.x, y, 58f, 2f), AvTheme.RailInfo);
                AvStyled.Label(page, new Rect(gate.x + 15f, y - 9f, 180f, 14f),
                    "ENABLED FILTERS", "metric-key");
                filterCountReadout = AvStyled.Label(page, new Rect(gate.x + 14f, y - 24f, 90f, 35f),
                    "—", "readout");
                filterProfileReadout = AvStyled.Label(page,
                    new Rect(gate.x + 104f, y - 37f, gate.width - 188f, 24f),
                    "NO TARGET LINK", "row-sub");
                filterProfileReadout.enableAutoSizing = true;
                filterProfileReadout.fontSizeMin = AvTokens.FontMicro;
                float reticleX = gate.x + gate.width - 70f;
                float reticleY = y - 11f;
                Rect reticle = new Rect(reticleX, reticleY, 56f, 56f);
                AvKit.CornerTicks(page, reticle, AvTheme.RailInfo, 9f);
                AvKit.Rule(page, new Rect(reticleX + 27f, reticleY - 8f, 1f, 40f),
                    AvTheme.RailInfo.WithAlpha(0.55f));
                AvKit.Rule(page, new Rect(reticleX + 8f, reticleY - 27f, 40f, 1f),
                    AvTheme.RailInfo.WithAlpha(0.55f));
                AvKit.Panel(page, new Rect(reticleX + 25f, reticleY - 25f, 5f, 5f), AvTheme.Accent);
                Rect gauge = new Rect(gate.x + 14f, y - gate.height + 10f,
                                      gate.width - 28f, 3f);
                AvKit.Panel(page, gauge, AvTheme.Surface);
                filterGauge = AvKit.Panel(page, gauge, AvTheme.RailInfo);
                filterGauge.sprite = AvSprites.White;
                filterGauge.type = Image.Type.Filled;
                filterGauge.fillMethod = Image.FillMethod.Horizontal;
                filterGauge.fillOrigin = 0;
                filterGauge.fillAmount = 0f;
                y -= gate.height + AvTokens.Space2;

                y = TargetHeading(page, y, width, "FILTER ACTIONS", "L TOGGLE / R SOLO");
                float chipWidth = (width - AvTokens.Space3 - gap * 3f) / 4f;
                resetFilters = PanelButton(page, new Rect(AvTokens.Space3, y, chipWidth, AvTokens.RowHeight),
                    "RESET", "btn", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.ResetFilters();
                        selector.NeedUpdateIcons();
                        Echo("FILTERS RESET");
                        RequestRefresh();
                    }, AvButtonStyle.Default);
                clearTargets = PanelButton(page,
                    new Rect(AvTokens.Space3 + (chipWidth + gap), y, chipWidth, AvTokens.RowHeight),
                    "CLEAR", "btn", () =>
                    {
                        if (!Ready) return;
                        selector.DeselectAll();
                        RequestRefresh();
                    }, AvButtonStyle.Default);
                followHud = PanelButton(page,
                    new Rect(AvTokens.Space3 + 2f * (chipWidth + gap), y, chipWidth, AvTokens.RowHeight),
                    "HUD LINK", "toggle", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleFollowHUD.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    },
                    AvButtonStyle.Toggle);
                laser = PanelButton(page,
                    new Rect(AvTokens.Space3 + 3f * (chipWidth + gap), y, chipWidth, AvTokens.RowHeight),
                    "LASER", "toggle", () =>
                    {
                        if (!Ready) return;
                        CancelDeleteConfirm();
                        selector.toggleLaser.Toggle();
                        selector.NeedUpdateIcons();
                        RequestRefresh();
                    }, AvButtonStyle.Toggle);
                resetFilters.WithTooltip("Restore the ALL profile: every faction and unit class, laser off.");
                clearTargets.WithTooltip("Drop every tracked contact from the target list.");
                followHud.WithTooltip("Follow the HUD: the target list tracks whatever the HUD is following.");
                laser.WithTooltip("Laser only: the target list keeps lased targets.");
                y -= AvTokens.RowHeight + AvTokens.Space2;

                y = TargetHeading(page, y, width, "FACTION", "FRIEND / FOE");
                factionGrid = new MfdPagingGrid(page, y, width, 2, 1, pager: false, rowHeight: cell);
                AddRightClickActions(factionGrid, OnlyFaction);
                y -= cell + AvTokens.Space2;

                y = TargetHeading(page, y, width, "UNIT CLASS", "AIR / LAND / SEA");
                unitGrid = new MfdPagingGrid(page, y, width, 2, 3, pager: false, rowHeight: cell);
                AddRightClickActions(unitGrid, OnlyUnitType);
                y -= cell * 3f + AvTokens.Space2;

                y = TargetHeading(page, y, width, "PLATFORM TYPE", "TYPE MASK");
                vehicleGrid = new MfdPagingGrid(page, y, width, 2, 5, pager: false, rowHeight: cell);
                AddRightClickActions(vehicleGrid, OnlyVehicleType);
            }

            // ------------------------------------------------------------- acquire

            private struct TargetCandidate
            {
                public Unit Unit;
                public float DistanceKm;
            }

            private void BuildAcquirePage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float y = TargetHeading(page, -AvTokens.Space1, width, "CONTACT BROWSER", "NEAREST KNOWN FIRST");
                float half = (width - AvTokens.Space3 - AvTokens.Gap) * 0.5f;
                missilePreference = PanelButton(page, new Rect(AvTokens.Space3, y, half, 40f),
                    "MISSILES", "toggle", () => ToggleAcquirePreference(0), AvButtonStyle.Toggle);
                weaponPreference = PanelButton(page, new Rect(AvTokens.Space3 + half + AvTokens.Gap, y, half, 40f),
                    "WEAPON FIT", "toggle", () => ToggleAcquirePreference(1), AvButtonStyle.Toggle);
                y -= 40f + AvTokens.Gap;
                airPreference = PanelButton(page, new Rect(AvTokens.Space3, y, half, 40f),
                    "AIR ONLY", "toggle", () => ToggleAcquirePreference(2), AvButtonStyle.Toggle);
                rangePreference = PanelButton(page, new Rect(AvTokens.Space3 + half + AvTokens.Gap, y, half, 40f),
                    "RANGE ALL", "toggle", () => ToggleAcquirePreference(3), AvButtonStyle.Toggle);
                missilePreference.WithTooltip("Include tracked missiles in this browser. Native TGT filters remain independent.");
                weaponPreference.WithTooltip("Only list contacts the selected weapon can engage.");
                airPreference.WithTooltip("Only list aircraft. Press again to show all allowed classes.");
                rangePreference.WithTooltip("Cycle maximum distance: all, 10, 25, 50 and 100 km.");
                y -= 40f + AvTokens.Space3;

                float noteY = y;
                y = TargetHeading(page, y, width, "CONTACTS", null);
                candidateNote = AvStyled.Label(page,
                    new Rect(width * 0.65f, noteY, width * 0.35f, 14f), "0 KNOWN",
                    "section-title-note", align: TMPro.TextAlignmentOptions.MidlineRight);
                int rows = Mathf.Clamp(Mathf.FloorToInt((PageHeight - 380f) / 42f), 3, 7);
                candidateGrid = new MfdPagingGrid(page, y, width, 1, rows, rowHeight: 42f, exclusive: true);
                candidateGrid.SetEmptyMessage("NO CONTACTS MATCH THESE PREFERENCES");
                y -= rows * 42f + AvTokens.RowHeight + AvTokens.Space2;
                float third = (width - AvTokens.Space3 - AvTokens.Gap * 2f) / 3f;
                nextCandidate = PanelButton(page, new Rect(AvTokens.Space3, y, third, 42f),
                    "NEXT", "btn", NextCandidate, AvButtonStyle.Default);
                incomingCandidate = PanelButton(page,
                    new Rect(AvTokens.Space3 + third + AvTokens.Gap, y, third, 42f),
                    "INCOMING", "btn", PreviewIncoming, AvButtonStyle.Default);
                designateCandidate = PanelButton(page,
                    new Rect(AvTokens.Space3 + 2f * (third + AvTokens.Gap), y, third, 42f),
                    "DESIGNATE", "btn", DesignateCandidate, AvButtonStyle.Primary);
                nextCandidate.WithTooltip("Preview the next known contact without selecting it.");
                incomingCandidate.WithTooltip("Highlight the nearest tracked missile targeting your aircraft. Does not select it.");
                designateCandidate.WithTooltip("Add the previewed contact to the native target list.");
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
                PaintButton(missilePreference, settings.TargetShowMissiles.Value ? "MISSILES ON" : "MISSILES OFF",
                            settings.TargetShowMissiles.Value);
                PaintButton(weaponPreference, settings.TargetWeaponOnly.Value ? "WEAPON FIT ON" : "WEAPON FIT OFF",
                            settings.TargetWeaponOnly.Value);
                PaintButton(airPreference, settings.TargetAirOnly.Value ? "AIR ONLY" : "ALL CLASSES",
                            settings.TargetAirOnly.Value);
                int range = settings.TargetRangeKm.Value;
                PaintButton(rangePreference, range <= 0 ? "RANGE ALL" : "RANGE " + range + " KM", range > 0);
                if (Shell.Page != 1) return;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                bool flying = hud != null && hud.aircraft != null && !hud.aircraft.disabled;
                candidateGrid.SetEmptyMessage(flying ? "NO CONTACTS MATCH THESE PREFERENCES" :
                    "ENTER AN AIRCRAFT TO BROWSE CONTACTS");
                incomingCandidate.SetEnabled(flying);
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
                    subs: i => candidates[i].DistanceKm.ToString("0.0") + " KM · KNOWN POSITION");
                candidateNote.text = flying ? candidates.Count + " MATCH" : "NO AIRCRAFT";
                nextCandidate.SetEnabled(candidates.Count > 0);
                designateCandidate.SetEnabled(candidateFocus != null && candidates.Exists(c => c.Unit == candidateFocus));
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
                Echo("PREVIEW " + TargetUnitLabel(candidateFocus) + " · PRESS DESIGNATE TO SELECT");
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

            private void BuildPresetsPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                string[] keys =
                {
                    KeyLabel(TargetPresetRuntime.Key(0)),
                    KeyLabel(TargetPresetRuntime.Key(1)),
                    KeyLabel(TargetPresetRuntime.Key(2)),
                };
                float y = TargetHeading(page, -AvTokens.Space1, width, "QUICK SWITCH",
                                  "RADIAL · " + string.Join(" ", keys));

                // Three columns: quick slots 1-3 fill a single balanced row, eliminating
                // the dead 4th slot and reclaiming 46px vertically.
                quickGrid = new MfdPagingGrid(page, y, width, 3, 1, pager: false, rowHeight: 44f, exclusive: true);
                AddSlotActions();
                y -= 44f + AvTokens.Space3;

                float libraryY = y;
                y = TargetHeading(page, y, width, "PRESET LIBRARY", null);
                presetNote = AvStyled.Label(page,
                    new Rect(width * 0.52f, libraryY, width * 0.48f, 14f), SavedNote(),
                    "section-title-note", align: TMPro.TextAlignmentOptions.MidlineRight);

                // Exactly 4 rows x 2 columns = 8 slots, framing the 8 built-in presets
                // without any empty ghost boxes at the bottom. Custom presets page cleanly.
                const int libraryRows = 4;
                const float libraryCell = 42f;
                presetVisible = 2 * libraryRows;
                presetGrid = new MfdPagingGrid(page, y, width, 2, libraryRows, rowHeight: libraryCell, exclusive: true);
                AddPresetActions();
                y -= libraryRows * libraryCell + AvTokens.Space1 + AvTokens.RowHeight;

                presetStatus = AvStyled.Label(page, new Rect(AvTokens.Space3, y, width - AvTokens.Space3, 16f),
                    "ACTIVE PROFILE", "row-main");
                y -= 20f;

                presetSummary = AvStyled.Label(page,
                    new Rect(AvTokens.Space3, y, width - AvTokens.Space3 * 2f, 30f),
                    "", "row-sub", state: null);
                y -= 36f;

                float gap = AvTokens.Gap;
                float buttonWidth = (width - AvTokens.Space3 - gap * 3f) / 4f;
                saveAs = PanelButton(page, new Rect(AvTokens.Space3, y, buttonWidth, AvTokens.RowHeight),
                    "SAVE AS", "toggle", BeginSaveAs, AvButtonStyle.Primary);
                updatePreset = PanelButton(page,
                    new Rect(AvTokens.Space3 + (buttonWidth + gap), y, buttonWidth, AvTokens.RowHeight),
                    "UPDATE", "toggle", UpdateSelected, AvButtonStyle.Toggle);
                renamePreset = PanelButton(page,
                    new Rect(AvTokens.Space3 + 2f * (buttonWidth + gap), y, buttonWidth, AvTokens.RowHeight),
                    "RENAME", "toggle", BeginRename, AvButtonStyle.Toggle);
                deletePreset = PanelButton(page,
                    new Rect(AvTokens.Space3 + 3f * (buttonWidth + gap), y, buttonWidth, AvTokens.RowHeight),
                    "DELETE", "toggle", PressDelete, AvButtonStyle.Danger);
                y -= AvTokens.RowHeight + AvTokens.Space2;

                // Belt and braces: the measured layout already lands here; the clamp keeps
                // the editor's top from ever crossing the body bottom if a token changes.
                BuildNameEditor(page, Mathf.Max(y, -PageHeight + AvTokens.Space2 + 52f), width);
            }

            private string SavedNote() =>
                TargetPresetRuntime.Library.Count + " / " + TargetPresetLibrary.MaxCustomPresets + " SAVED";

            private static string KeyLabel(KeyCode key) => key == KeyCode.None ? "NONE" : key.ToString();

            private void BuildNameEditor(RectTransform page, float y, float width)
            {
                var go = new GameObject("NameEditor", typeof(RectTransform));
                editor = go.GetComponent<RectTransform>();
                editor.SetParent(page, worldPositionStays: false);
                float editorWidth = width - AvTokens.Space3 * 2f;
                AvKit.Place(editor, new Rect(AvTokens.Space3, y, editorWidth, 52f));

                editorLabel = AvStyled.Label(editor, new Rect(0f, 0f, editorWidth, 14f),
                    "PRESET NAME", "section-title-note");
                float gap = AvTokens.Space2;
                float okWidth = 96f;
                float cancelWidth = 96f;
                float fieldWidth = Mathf.Max(80f, editorWidth - (okWidth + cancelWidth + gap * 2f));
                nameField = AvKit.InputField(editor, new Rect(0f, -18f, fieldWidth, 26f),
                    TargetPresetLibrary.MaxNameLength, null, null, null,
                    "A-Z 0-9, 14 characters", "PRESET NAME");
                PanelButton(editor, new Rect(fieldWidth + gap, -18f, okWidth, 26f),
                    "CONFIRM", "toggle", CommitEdit, AvButtonStyle.Primary);
                PanelButton(editor,
                    new Rect(fieldWidth + gap * 2f + okWidth, -18f, cancelWidth, 26f),
                    "CANCEL", "toggle", CancelEdit, AvButtonStyle.Toggle);
                editor.gameObject.SetActive(false);
            }

            private void AddSlotActions()
            {
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                {
                    int index = slot;
                    AvButton button = quickGrid.ButtonAt(slot);
                    if (button == null) continue;
                    button.SetAction(() => ApplyQuickSlot(index));
                    MfdRightClickAction right = button.gameObject.AddComponent<MfdRightClickAction>();
                    right.Configure(() => ClearQuickSlot(index));
                }
            }

            private void AddPresetActions()
            {
                for (int slot = 0; slot < presetVisible; slot++)
                {
                    int index = slot;
                    AvButton button = presetGrid.ButtonAt(slot);
                    if (button == null) continue;
                    MfdRightClickAction right = button.gameObject.AddComponent<MfdRightClickAction>();
                    right.Configure(() => AssignQuickSlot(presetGrid.CurrentIndex(index)));
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
                    null,
                    subs: slot => "SLOT " + (slot + 1) + " · " + KeyLabel(TargetPresetRuntime.Key(slot)));
                for (int slot = 0; slot < TargetPresetLibrary.SlotCount; slot++)
                {
                    AvButton button = quickGrid.ButtonAt(slot);
                    if (button == null) continue;
                    string name = TargetPresetRuntime.QuickSlotName(slot);
                    button.SetEnabled(true);
                    button.WithTooltip(name.Length == 0
                        ? "Quick slot " + (slot + 1) + " is empty. Right-click a preset below to assign it."
                        : "Apply " + name + " (quick slot " + (slot + 1) + ", key " +
                          KeyLabel(TargetPresetRuntime.Key(slot)) + "). Right-click to clear.");
                }
            }

            private static string SlotLabel(int slot)
            {
                string name = TargetPresetRuntime.QuickSlotName(slot);
                return name.Length == 0 ? "EMPTY" : name;
            }

            private void RefreshLibrary()
            {
                EnsureCatalog();
                if (presetNote != null) presetNote.text = SavedNote();
                presetGrid.SetData(catalog.Count,
                    LibraryLabel,
                    index => catalog[index] != null && catalog[index].Name == activePreset,
                    ApplyCatalog,
                    icons: index => null);
                for (int slot = 0; slot < presetVisible; slot++)
                {
                    AvButton button = presetGrid.ButtonAt(slot);
                    if (button == null) continue;
                    int index = presetGrid.CurrentIndex(slot);
                    if (index < 0 || index >= catalog.Count)
                    {
                        button.WithTooltip(null);
                        continue;
                    }
                    TargetPresetSnapshot preset = catalog[index];
                    string badge = PresetSlotBadge(preset.Name);
                    button.WithTooltip(preset.Name + badge + " — " +
                        (TargetPresetRuntime.IsBuiltIn(index)
                            ? MfdTargetPresets.Descriptions[index]
                            : TargetPresetRules.Summary(preset)) +
                        "  ·  Left click applies, right click assigns a quick slot.");
                }
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
                nameField.text = TargetPresetRuntime.SuggestName();
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
                nameField.text = preset.Name;
                OpenEditor(EditMode.Rename, "RENAME " + preset.Name);
            }

            private void OpenEditor(EditMode mode, string label)
            {
                editorMode = mode;
                editorLabel.text = label;
                editor.gameObject.SetActive(true);
                nameField.ActivateInputField();
                nameField.Select();
                RequestRefresh();
            }

            private void CloseEditor()
            {
                editorMode = EditMode.None;
                if (editor != null) editor.gameObject.SetActive(false);
                if (nameField != null) nameField.DeactivateInputField();
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
                string name = TargetPresetRules.SanitiseName(nameField.text);
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
                if (index < 0) return "Select a preset to apply. SAVE AS stores the current filters.";
                if (index < TargetPresetRuntime.BuiltInCount)
                    return "Built-in. " + MfdTargetPresets.Descriptions[index];
                TargetPresetSnapshot preset = TargetPresetRuntime.Library.At(
                    index - TargetPresetRuntime.BuiltInCount);
                return preset == null ? "" : TargetPresetRules.Summary(preset);
            }

            // ------------------------------------------------------------ selected

            private void BuildSelectedPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float y = TargetHeading(page, -AvTokens.Space1, width, "SELECTED TARGETS", null);
                selectedNote = AvStyled.Label(page,
                    new Rect(width * 0.52f, -AvTokens.Space1, width * 0.48f, 14f),
                    "0 TRACKED", "section-title-note", align: TMPro.TextAlignmentOptions.MidlineRight);
                float groupWidth = (width - AvTokens.Space3 - AvTokens.Gap * 2f) / 3f;
                for (int group = 0; group < groupButtons.Length; group++)
                {
                    int slot = group;
                    groupButtons[group] = PanelButton(page,
                        new Rect(AvTokens.Space3 + group * (groupWidth + AvTokens.Gap), y, groupWidth, 42f),
                        "GROUP " + (group + 1), "btn", () => RecallGroup(slot), AvButtonStyle.Default);
                    groupButtons[group].WithTooltip("Left click recalls this mission group. Right click stores up to 32 selected targets.");
                    MfdRightClickAction right = groupButtons[group].gameObject.AddComponent<MfdRightClickAction>();
                    right.Configure(() => StoreGroup(slot));
                }
                y -= 42f + AvTokens.Space2;
                // The same 70px chrome as the HUD list pages: heading, pager and the two
                // small gaps outside them. The pitch takes the body's slack so the pager
                // sits on the footer instead of a dead band.
                const float chrome = 70f + 42f + AvTokens.Space2;
                selectedVisible = Mathf.Clamp(
                    Mathf.FloorToInt((PageHeight - chrome) / AvTokens.RowHeight), 3, 9);
                float cell = Mathf.Clamp((PageHeight - chrome) / selectedVisible,
                                         AvTokens.RowHeight, 72f);
                selectedGrid = new MfdPagingGrid(page, y, width, 1, selectedVisible,
                                                 readOnly: true, rowHeight: cell);
                selectedGrid.SetEmptyMessage("NO TARGETS TRACKED\nDESIGNATE CONTACTS ON MAP OR ENGAGE HUD LINK");
                for (int slot = 0; slot < selectedVisible; slot++)
                {
                    int index = slot;
                    AvButton button = selectedGrid.ButtonAt(slot);
                    if (button == null) continue;
                    button.SetEnabled(true);
                    MfdRightClickAction right = button.gameObject.AddComponent<MfdRightClickAction>();
                    right.Configure(() => DeselectSelected(selectedGrid.CurrentIndex(index)));
                }
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

            private void BuildCameraPage(RectTransform page)
            {
                DrawSpine(page);
                float width = PageWidth;
                float y = TargetHeading(page, -AvTokens.Space1, width, "CAMERA MARK", "SURFACE SENSOR TARGET");

                // Keep the mark, actions and readout together at every bezel height.
                const float plate = 112f;
                const float keyPitch = 28f;

                Rect markCard = new Rect(AvTokens.Space3, y, width - AvTokens.Space3 * 2f, plate);
                AvKit.Panel(page, markCard, AvTheme.Surface, AvSprites.Card);
                AvKit.Outline(page, markCard, AvTheme.Hairline);
                cameraRail = AvStyled.Rail(page,
                    new Rect(AvTokens.Space3 + 6f, y - 6f, 3f, Mathf.Max(10f, plate - 12f)), "locked");
                Rect sensorBadge = new Rect(markCard.x + markCard.width - 56f, y - 28f, 40f, 40f);
                AvKit.CornerTicks(page, sensorBadge, AvTheme.RailInfo, 7f);
                var sensorObject = new GameObject("CameraSensorIcon", typeof(RectTransform), typeof(MfdGlyph));
                var sensorIcon = sensorObject.GetComponent<MfdGlyph>();
                sensorObject.transform.SetParent(page, false);
                AvKit.Place(sensorIcon.rectTransform,
                    new Rect(sensorBadge.x + 12f, sensorBadge.y - 12f, 16f, 16f));
                sensorIcon.raycastTarget = false;
                sensorIcon.SetKind("eye", AvTheme.RailInfo);
                float groupTop = y - Mathf.Max(6f, (plate - 54f) * 0.5f);
                cameraState = AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + 16f, groupTop, markCard.width - 88f, 16f),
                    "NO ACTIVE MARK", "section-title");
                cameraDetails = AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + 16f, groupTop - 18f, markCard.width - 88f, 36f),
                    "Aim the cockpit camera at a surface point and press MARK CAMERA.", "row-sub");
                y -= plate + AvTokens.Space2;

                float gap = AvTokens.Gap;
                float buttonWidth = (width - AvTokens.Space3 * 2f - gap * 2f) / 3f;
                cameraCapture = PanelButton(page,
                    new Rect(AvTokens.Space3, y, buttonWidth, AvTokens.RowHeight),
                    "MARK CAMERA", "btn",
                    () => { Camera?.Capture(); RequestRefresh(); }, AvButtonStyle.Default);
                cameraCall = PanelButton(page,
                    new Rect(AvTokens.Space3 + buttonWidth + gap, y, buttonWidth, AvTokens.RowHeight),
                    "CALL AT MARK", "btn",
                    () => { Camera?.CallAtMark(); RequestRefresh(); }, AvButtonStyle.Default);
                cameraClear = PanelButton(page,
                    new Rect(AvTokens.Space3 + (buttonWidth + gap) * 2f, y, buttonWidth, AvTokens.RowHeight),
                    "CLEAR MARK", "btn",
                    () => { Camera?.Clear(); RequestRefresh(); }, AvButtonStyle.Default);
                y -= AvTokens.RowHeight + AvTokens.Space2;

                y = TargetHeading(page, y, width, "TARGET TELEMETRY", "COORDINATES & RANGE");
                const float telemHeight = 152f;
                Rect telemCard = new Rect(AvTokens.Space3, y, width - AvTokens.Space3 * 2f, telemHeight);
                AvKit.Panel(page, telemCard, AvTheme.Unity(AvTokens.Surface), AvSprites.Card);
                AvKit.Outline(page, telemCard, AvTheme.Hairline);

                float ty = y - 8f;
                cameraPos = CameraKey(page, ty, width, "GRID (X / Z)");
                ty -= keyPitch;
                cameraElev = CameraKey(page, ty, width, "ELEVATION (Y)");
                ty -= keyPitch;
                cameraRange = CameraKey(page, ty, width, "SLANT RANGE");
                ty -= keyPitch;
                cameraAge = CameraKey(page, ty, width, "MARK AGE");
                ty -= keyPitch;
                cameraArmed = CameraKey(page, ty, width, "ARMED CALL-IN");
                y -= telemHeight + AvTokens.Space2;

                y = TargetHeading(page, y, width, "SENSOR ALIGNMENT", "LINE-OF-SIGHT DATUM");
                // A taller bezel gives the sensor its own scope rather than leaving a dead
                // strip below a fixed 136px card. Compact bays retain the original floor.
                float reticleHeight = Mathf.Clamp(PageHeight + y - AvTokens.Space2, 136f, 320f);
                Rect reticleCard = new Rect(AvTokens.Space3, y, width - AvTokens.Space3 * 2f, reticleHeight);
                AvKit.Panel(page, reticleCard, AvTheme.Unity(AvTokens.Surface), AvSprites.Card);
                cameraReticleBorder = AvKit.Outline(page, reticleCard, AvTheme.Hairline);

                float cardW = width - AvTokens.Space3 * 2f;
                float midY = y - reticleHeight * 0.38f;
                float centerX = AvTokens.Space3 + cardW * .5f;
                float scopeSize = reticleHeight > 200f ? 60f : 40f;
                AvKit.CornerTicks(page, new Rect(centerX - scopeSize * .5f,
                    midY + scopeSize * .2f, scopeSize, scopeSize * .65f),
                                  AvTheme.RailInfo, 6f);
                AvKit.Rule(page, new Rect(centerX - 12f, midY - 5f, 24f, 1f), AvTheme.RailInfo);
                AvKit.Rule(page, new Rect(centerX, midY + 6f, 1f, 22f), AvTheme.RailInfo);
                AvKit.Panel(page, new Rect(centerX - 1f, midY - 4f, 3f, 3f), AvTheme.Accent);
                cameraReticleStatus = AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + 10f,
                        midY - (scopeSize > 40f ? 48f : 26f), cardW - 20f, 16f),
                    "BORESIGHT STANDBY · SLEW CAMERA TO DESIGNATE", "row-sub",
                    align: TMPro.TextAlignmentOptions.Center);
                TMPro.TMP_Text markHelp = AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + 10f, y - reticleHeight + 38f, cardW - 20f, 28f),
                    "A surface reference for OPS. Arm support, then CALL AT MARK.",
                    "row-sub", align: TMPro.TextAlignmentOptions.Center);
                markHelp.enableWordWrapping = true;
                markHelp.overflowMode = TMPro.TextOverflowModes.Truncate;
            }

            private static TMPro.TMP_Text CameraKey(RectTransform page, float y, float width, string key)
            {
                AvStyled.Label(page, new Rect(AvTokens.Space3 + 12f, y, width * 0.55f - 12f, 16f), key, "kv-key");
                return AvStyled.Label(page,
                    new Rect(AvTokens.Space3 + width * 0.55f, y, width * 0.45f - AvTokens.Space3 - 12f, 16f),
                    "—", "kv-value", align: TMPro.TextAlignmentOptions.MidlineRight);
            }

            private void RefreshCamera()
            {
                if (cameraState == null) return;
                ICameraTargetService service = Camera;
                if (service == null || !service.Available)
                {
                    cameraRail.color = AvTheme.RailInert;
                    cameraState.text = "CAMERA MARKING UNAVAILABLE";
                    cameraState.color = AvTheme.Dim;
                    cameraDetails.text = "The support module or its observation source is not installed.";
                    cameraCapture?.SetEnabled(false);
                    cameraCall?.SetEnabled(false);
                    cameraClear?.SetEnabled(false);
                    cameraCapture?.WithTooltip("Camera marking is unavailable: the support module or its observation source is not installed.");
                    cameraCall?.WithTooltip("Camera marking is unavailable: the support module or its observation source is not installed.");
                    cameraClear?.WithTooltip("Camera marking is unavailable: the support module or its observation source is not installed.");
                    SetCameraTelemetry("—", "—", "—", "—", "—");
                    if (cameraReticleStatus != null)
                    {
                        cameraReticleStatus.text = "SENSOR INTERFACE OFFLINE";
                        cameraReticleStatus.color = AvTheme.Disabled;
                    }
                    SetCameraReticle(AvTheme.Hairline);
                    return;
                }

                bool marked = service.HasMark;
                bool armed = !string.IsNullOrEmpty(service.ArmedActionName);
                if (marked)
                {
                    ObservationPoint point = service.Mark;
                    cameraRail.color = AvTheme.RailReady;
                    cameraState.text = (point.Source ?? "SENSOR").ToUpperInvariant() + " SURFACE MARK";
                    cameraState.color = AvTheme.RailReady;
                    cameraDetails.text = "Surface reference recorded; expires 120 seconds after capture.";
                    SetCameraTelemetry(
                        "X " + point.X.ToString("0") + " · Z " + point.Z.ToString("0"),
                        point.Y.ToString("0") + " m ASL",
                        (point.Range / 1000f).ToString("0.0") + " km",
                        service.AgeSeconds.ToString("0") + "s",
                        armed ? service.ArmedActionName : "NONE (ARM IN OPS)");
                    if (cameraReticleStatus != null)
                    {
                        cameraReticleStatus.text = "SURFACE MARK LOCKED · REFERENCE RECORDED";
                        cameraReticleStatus.color = AvTheme.Accent;
                    }
                    SetCameraReticle(AvTheme.Accent);
                }
                else
                {
                    cameraRail.color = AvTheme.RailInert;
                    cameraState.text = "NO ACTIVE MARK";
                    cameraState.color = AvTheme.Dim;
                    cameraDetails.text = service.Status.ToUpperInvariant() + " · AIM AND PRESS MARK CAMERA.";
                    SetCameraTelemetry("—", "—", "—", "—", armed ? service.ArmedActionName : "NONE (ARM IN OPS)");
                    if (cameraReticleStatus != null)
                    {
                        cameraReticleStatus.text = "BORESIGHT STANDBY · SLEW CAMERA TO DESIGNATE";
                        cameraReticleStatus.color = AvTheme.Dim;
                    }
                    SetCameraReticle(AvTheme.Hairline);
                }

                cameraCapture.SetEnabled(service.CanCapture);
                cameraCall.SetEnabled(service.CanCallAtMark);
                cameraCall.SetText(armed ? "CALL AT MARK" : "SELECT IN OPS");
                cameraClear.SetEnabled(marked);
                cameraCapture.WithTooltip(service.CanCapture
                    ? "Record the surface point under the native camera."
                    : "Requires an active native camera view on your aircraft.");
                cameraCall.WithTooltip(!marked ? "Capture a mark first."
                    : !armed ? "Arm an operation on OPS / SUPPORT first."
                    : "Deliver the armed operation onto this mark.");
                cameraClear.WithTooltip(marked ? "Clear the active mark." : "No mark to clear.");
            }

            private void SetCameraReticle(Color colour)
            {
                if (cameraReticleBorder == null) return;
                for (int i = 0; i < cameraReticleBorder.Length; i++) cameraReticleBorder[i].color = colour;
            }

            private void SetCameraTelemetry(string position, string elevation, string range, string age, string armed)
            {
                cameraPos.text = position;
                if (cameraElev != null) cameraElev.text = elevation;
                cameraRange.text = range;
                cameraAge.text = age;
                cameraAge.color = AvTheme.TextPrimary;
                cameraArmed.text = armed;
                cameraArmed.color = armed.StartsWith("NONE") ? AvTheme.Dim : AvTheme.RailCaution;
            }

            // ------------------------------------------------------------ plumbing

            private void SelectPage(int selected)
            {
                CancelDeleteConfirm();
                CloseEditor();
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                SetSelectedTab(selected);
                RequestRefresh();
            }

            private void SetGrid(MfdPagingGrid grid, List<TargetListSelector_ToggleButton> entries,
                                 Action<int> onClick)
            {
                grid.SetData(entries == null ? 0 : entries.Count,
                    i => NativeTargetLabel(entries[i]),
                    i => entries[i] != null && entries[i].status,
                    onClick, icons: i => entries[i] == null || entries[i].image == null ? null : entries[i].image.sprite);
            }

            private void AddRightClickActions(MfdPagingGrid grid, Action<int> onOnly)
            {
                for (int i = 0; i < 12; i++)
                {
                    int slot = i;
                    AvButton button = grid.ButtonAt(i);
                    if (button == null) continue;
                    MfdRightClickAction action = button.gameObject.AddComponent<MfdRightClickAction>();
                    action.Configure(() => onOnly(grid.CurrentIndex(slot)));
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
                if (selectedNote != null) selectedNote.text = selectedUnits.Count + " TRACKED";
                for (int i = 0; i < groupButtons.Length; i++)
                {
                    targetGroups[i].RemoveAll(unit => unit == null || unit.disabled);
                    PaintButton(groupButtons[i], "GROUP " + (i + 1) + " · " + targetGroups[i].Count,
                                targetGroups[i].Count > 0);
                }
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
                resetFilters?.SetEnabled(enabled);
                clearTargets?.SetEnabled(enabled);
                followHud?.SetEnabled(enabled);
                laser?.SetEnabled(enabled);
                factionGrid?.SetInteractable(enabled);
                unitGrid?.SetInteractable(enabled);
                vehicleGrid?.SetInteractable(enabled);
                selectedGrid?.SetInteractable(enabled);
                candidateGrid?.SetInteractable(enabled);
                missilePreference?.SetEnabled(enabled);
                weaponPreference?.SetEnabled(enabled);
                airPreference?.SetEnabled(enabled);
                rangePreference?.SetEnabled(enabled);
                nextCandidate?.SetEnabled(enabled && candidates.Count > 0);
                incomingCandidate?.SetEnabled(enabled);
                designateCandidate?.SetEnabled(enabled && candidateFocus != null);
                for (int i = 0; i < groupButtons.Length; i++) groupButtons[i]?.SetEnabled(enabled);
                quickGrid?.SetInteractable(enabled);
                presetGrid?.SetInteractable(enabled);
                saveAs?.SetEnabled(enabled);
                updatePreset?.SetEnabled(enabled);
                renamePreset?.SetEnabled(enabled);
                deletePreset?.SetEnabled(enabled);
                if (!enabled)
                {
                    editorMode = EditMode.None;
                    if (editor != null) editor.gameObject.SetActive(false);
                    confirmDelete = null;
                    clearTargets?.WithTooltip("Waiting for target filters.");
                    updatePreset?.WithTooltip("Waiting for target filters.");
                    renamePreset?.WithTooltip("Waiting for target filters.");
                    deletePreset?.WithTooltip("Waiting for target filters.");
                }
                else
                {
                    // A disabled preset action states the one thing it is waiting for,
                    // the way the camera and map preset buttons do.
                    bool custom = SelectedCustom() != null;
                    updatePreset?.SetEnabled(custom);
                    updatePreset?.WithTooltip(custom
                        ? "Overwrite the selected saved preset with the current filters."
                        : "Select a saved preset first.");
                    renamePreset?.SetEnabled(custom);
                    renamePreset?.WithTooltip(custom
                        ? "Rename the selected saved preset."
                        : "Select a saved preset first.");
                    deletePreset?.SetEnabled(custom);
                    deletePreset?.WithTooltip(custom
                        ? "Delete the selected saved preset. Deletion asks for a second press."
                        : "Select a saved preset first.");
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
            /// two lines at the micro floor, so "GROUND VEHICLES" never prints as "GND".
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
