using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The big map's own gestures, rule B (2026-10-29). The left button selects and pans, armed tool or not: a click adds or drops
    /// a contact, empty ground does nothing (issue #34), Shift-drag box-adds and Ctrl-drag box-drops. The right button goes to the
    /// first claim: an armed tool places, a WMC wingman selection moves, a friendly commandable selection takes the game's own
    /// waypoint, and only then the context card opens. While any tool but WMC's is armed this class holds the pause key (Esc
    /// cancels the tool) and shows the one armed banner. Native DynamicMap still owns contacts and selection; this surface never
    /// builds a unit order of its own or reads an unobserved unit position.
    /// </summary>
    internal static class MfdMapInteractions
    {
        private const float ContactRadius = 18f;
        private const int MaximumScannedIcons = 2048;
        private const int MaximumSelected = 128;
        private const float BannerWidth = 470f, BannerHeight = 40f;

        private static readonly List<Unit> candidates = new List<Unit>(MaximumSelected);
        private static readonly List<MapMenuAction> actions = new List<MapMenuAction>(16);
        private static readonly List<MapMenuRow> rows = new List<MapMenuRow>(12), wingRows = new List<MapMenuRow>(4);

        private static RectTransform box;
        /// <summary>The dropped range mark: a map close keeps it, a scene reset forgets it.</summary>
        private static GlobalPosition? mark;
        private static RectTransform markGlyph;
        private static RectTransform banner;
        private static TMP_Text bannerWhat;
        private static string bannerPrompt;
        private static bool boxPress;
        private static bool boxDragged;
        private static MapBoxMode boxMode;
        private static Vector2 boxStart;
        private static int boxReleaseFrame = -1;
        private static bool rightPress;
        private static Vector2 rightStart;
        private const string PauseOwner = "command.map";
        private static bool pauseHeld;
        private static string feedback;
        private static float feedbackUntil;
        /// <summary>The card group the next <see cref="Add"/> files its action under.</summary>
        private static string group;

        internal static string Feedback => Time.unscaledTime < feedbackUntil ? feedback : null;

        internal static bool ContainsMenu(Vector2 screen) => MapContextCard.Contains(screen);

        /// <summary>Another map control (an STR tag or card) took this click: the context menu goes.</summary>
        internal static void ConsumeClick() => HideMenu();

        /// <summary>Who this frame's right click belongs to (rule B).</summary>
        internal static MapRightClick RightRule(DynamicMap map) => MapClickRules.RightClick(MapPicker.IsBusy,
            ModuleServices.TryGet(out IMapWingMenu wing) && wing.TakesRightClick, NativeCommands(map));

        /// <summary>The game's own right-click waypoint condition (DynamicMap.MapControls): the first selected icon is a friendly unit
        /// that takes orders, outside the editor.</summary>
        private static bool NativeCommands(DynamicMap map)
        {
            if (map == null || map.selectedIcons == null || map.selectedIcons.Count == 0 || GameManager.gameState == GameState.Editor)
                return false;
            return map.selectedIcons[0] is UnitMapIcon icon && icon.unit != null && !icon.unit.disabled && icon.unit is ICommandable &&
                DynamicMap.GetFactionMode(icon.unit.NetworkHQ, false) == FactionMode.Friendly;
        }

        /// <summary>Lets the native right-click waypoint through only for a commandable selection (rule B); suppresses the native
        /// Ctrl-drag pan under a selection box.</summary>
        internal static bool BlockNativeControls(DynamicMap map)
        {
            if (map == null || !DynamicMap.mapMaximized || map.mapBackground == null) return false;
            if (Input.GetMouseButtonDown(1)) return RightRule(map) != MapRightClick.Native;
            if (boxPress && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0))) return true;
            return (Input.GetMouseButton(0) || Input.GetMouseButtonDown(0)) && BoxDrag() &&
                !BoxBlocked() && !MapUiPointer.OverControls() &&
                map.TryGetCursorCoordinates(out _);
        }

        internal static bool BlockIconClick() => DynamicMap.mapMaximized &&
            ((boxPress && (boxDragged || !MapTool.IsClick(boxStart, Input.mousePosition))) || boxReleaseFrame == Time.frameCount);

        /// <summary>Use the native map's additive selection without its single-contact camera follow.</summary>
        internal static bool HandleUnitClick(UnitMapIcon icon, bool pointerEvent = false)
        {
            if (!DynamicMap.mapMaximized || (!pointerEvent && !Input.GetMouseButtonUp(0)))
                return false;
            if (BlockIconClick() || ReliefNavigator.BlockIconClick()) return true;
            if (BoxBlocked()) return true;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || icon == null || icon.unit == null || icon.unit.disabled ||
                Excluded(icon.unit) || !MapUiPointer.Contains(map.mapBackground.rectTransform,
                    Input.mousePosition)) return true;
            if (GameManager.GetLocalAircraft(out Aircraft localAircraft) && icon.unit == localAircraft)
                return true;
            ConsumeClick();
            // CLICK ADDS builds the selection: a click adds the contact, or drops it if already in.
            // CLICK REPLACES makes it the only one; Shift or Ctrl still adds. Empty ground does nothing, as in vanilla (issue #34).
            bool selected = map.selectedIcons.Contains(icon);
            if (Plugin.Settings?.Command?.MapClickAdds?.Value != false || ShiftHeld() || ControlHeld())
            {
                if (selected) Deselect(map, icon.unit);
                else Select(map, icon.unit);
            }
            else
            {
                bool only = selected && map.selectedIcons.Count == 1;
                ClearSelection(map);
                if (!only) Select(map, icon.unit);
            }
            Echo((map.selectedIcons?.Count ?? 0) + " TRACKS SELECTED");
            return true;
        }

        internal static void HandleAirbaseClick(AirbaseMapIcon icon)
        {
            if (!MfdTerrainRelief.IsDrawing || BoxBlocked() || BlockIconClick() ||
                ReliefNavigator.BlockIconClick() || icon == null || icon.airbase == null)
                return;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;
            ConsumeClick();
            if (ShiftHeld() || ControlHeld())
            {
                if (!map.selectedIcons.Contains(icon)) map.SelectIcon(icon.airbase);
                Echo((map.selectedIcons?.Count ?? 0) + " MAP MARKERS SELECTED");
            }
            else icon.ClickIcon(MapIcon.ClickSource.Mouse);
        }

        internal static void Tick(DynamicMap map)
        {
            if (map == null || !DynamicMap.mapMaximized || map.maximizedMapCanvas == null ||
                map.mapBackground == null)
            {
                Restore();
                return;
            }
            HoldPause();
            DrawMark(map);
            DrawBanner(map);
            MapSelectionDeck.Tick(map);
            // An armed tool owns the right button: the card goes and no right press is latched for it.
            if (MapPicker.IsBusy)
            {
                HideMenu();
                rightPress = false;
            }
            if (BoxBlocked()) CancelBox();
            if (MapTool.CancelPressed())
            {
                CancelBox();
                HideMenu();
                rightPress = false;
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                bool onMenu = ContainsMenu(Input.mousePosition);
                if (!onMenu) HideMenu();
                rightStart = Input.mousePosition;
                // Latch the claim on press: an armed tool can disarm during its own release Update.
                rightPress = !onMenu && RightRule(map) == MapRightClick.Card &&
                    !MapUiPointer.OverControls() &&
                    MapUiPointer.Contains(map.mapBackground.rectTransform, rightStart);
            }
            if (Input.GetMouseButtonUp(1))
            {
                GlobalPosition point = default;
                bool open = rightPress && !MapPicker.IsBusy && !ReliefNavigator.BlockContextClick() &&
                    MapTool.IsClick(rightStart, Input.mousePosition) &&
                    !MapUiPointer.OverControls() && map.TryGetCursorCoordinates(out point);
                rightPress = false;
                if (open) ShowMenu(map, point, Input.mousePosition);
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (!ContainsMenu(Input.mousePosition)) HideMenu();
                boxMode = BoxModeNow();
                boxPress = boxMode != MapBoxMode.None && !BoxBlocked() && !MapUiPointer.OverControls() &&
                    !MapTool.Typing() && map.TryGetCursorCoordinates(out _);
                boxDragged = false;
                if (boxPress) boxStart = Input.mousePosition;
            }
            if (!boxPress) return;
            if (MapUiPointer.OverControls() || !map.TryGetCursorCoordinates(out _))
            {
                CancelBox();
                return;
            }
            Vector2 current = Input.mousePosition;
            if (!MapTool.IsClick(boxStart, current)) boxDragged = true;
            if (Input.GetMouseButtonUp(0))
            {
                if (boxDragged)
                {
                    SelectBox(map, new Rect(Mathf.Min(boxStart.x, current.x),
                        Mathf.Min(boxStart.y, current.y), Mathf.Abs(current.x - boxStart.x),
                        Mathf.Abs(current.y - boxStart.y)), boxMode);
                    boxReleaseFrame = Time.frameCount;
                }
                CancelBox();
            }
            else if (boxDragged) DrawBox(map.maximizedMapCanvas, boxStart, current, boxMode);
        }

        private static void SelectBox(DynamicMap map, Rect region, MapBoxMode mode)
        {
            candidates.Clear();
            if (map.mapIcons == null) return;
            GameManager.GetLocalAircraft(out Aircraft localAircraft);
            MapBoxSide side = Plugin.Settings?.Command?.MapBoxSide?.Value ?? MapBoxSide.All;
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit; i++)
            {
                if (!Pickable(map.mapIcons[i], localAircraft, out UnitMapIcon icon)) continue;
                if (!MapClickRules.BoxTakes(side, icon.unit.NetworkHQ != null, icon.unit.NetworkHQ == map.HQ)) continue;
                Vector2 screen = IconScreen(icon);
                if (!region.Contains(screen) ||
                    !MapUiPointer.Contains(map.mapBackground.rectTransform, screen)) continue;
                if (candidates.Count < MaximumSelected) candidates.Add(icon.unit);
            }
            int changed = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                bool held = IsSelected(map, candidates[i]);
                if (mode == MapBoxMode.Drop && held) { Deselect(map, candidates[i]); changed++; }
                else if (mode == MapBoxMode.Add && !held) { Select(map, candidates[i]); changed++; }
            }
            Echo(mode == MapBoxMode.Drop ? changed + " TRACKS DROPPED" : changed + " TRACKS ADDED");
        }

        // ---- the context card (rule B: a right click nobody claims) ----

        /// <summary>Test seam (MapInputAutomation): the card a right click at this screen point over this ground would open.</summary>
        internal static void OpenCardForTest(DynamicMap map, Vector2 screen, GlobalPosition ground) => ShowMenu(map, ground, screen);

        /// <summary>Read-back for the automation hook: what the map's input surface shows now.</summary>
        internal static bool BannerShown => banner != null && banner.gameObject.activeInHierarchy;
        internal static bool PauseHeld => pauseHeld;

        /// <summary>The selection box being dragged now, if any (the cursor follows it).</summary>
        internal static MapBoxMode BoxDragging => boxPress && boxDragged ? boxMode : MapBoxMode.None;

        /// <summary>The pickable contact under this screen point, as a click would take it.</summary>
        internal static UnitMapIcon HoverContact(DynamicMap map, Vector2 screen) => ContactAt(map, screen);

        private static void ShowMenu(DynamicMap map, GlobalPosition ground, Vector2 click)
        {
            HideMenu();
            RectTransform parent = map.maximizedMapCanvas.transform as RectTransform;
            if (parent == null) return;
            UnitMapIcon contact = ContactAt(map, click);
            AirbaseMapIcon airbase = contact == null ? AirbaseAt(map, click) : null;
            int nearby = CountNearby(map, click);
            int selected = map.selectedIcons?.Count ?? 0;
            bool hasWing = airbase == null && ModuleServices.TryGet(out IMapWingMenu _);
            actions.Clear();
            wingRows.Clear();

            if (contact != null)
            {
                Unit unit = contact.unit;
                group = "SELECT";
                Add("SELECT ONLY THIS", AvIcon.Target, () => SelectTrack(map, unit, false));
                // The line says what the click will do, not both things it might.
                bool held = selected > 0 && IsSelected(map, unit);
                Add(held ? "DROP FROM SELECTION" : "ADD TO SELECTION", held ? AvIcon.Minus : AvIcon.Plus,
                    () => SelectTrack(map, unit, true));
                int same = CountVisibleType(map, unit);
                if (same > 1) Add("ALL OF THIS TYPE  ·  " + same, AvIcon.Filter, () => SelectVisibleType(map, unit));
                if (nearby > 1) Add("THE STACK HERE  ·  " + nearby, AvIcon.Stack2, () => SelectNearby(map, click));
                AddClear(map, selected);
                if (hasWing) FillWing(unit, ground);
            }
            else if (airbase != null)
            {
                AirbaseMapIcon target = airbase;
                group = "SELECT";
                Add("SELECT BASE", AvIcon.Runway, () =>
                {
                    ClearSelection(map);
                    if (target != null && target.airbase != null) map.SelectIcon(target.airbase);
                });
                AddClear(map, selected);
            }
            else
            {
                // Bare ground: where the wing should go is the likeliest question, so it leads.
                if (hasWing) FillWing(null, ground);
                group = "SELECT";
                if (nearby > 0) Add("NEARBY TRACKS  ·  " + nearby, AvIcon.Stack2, () => SelectNearby(map, click));
                AddClear(map, selected);
            }

            group = "MAP";
            if (contact != null || airbase != null) Add("FRAME", AvIcon.Focus2, () => Focus(ground, true), true);
            else
            {
                Add("CENTER", AvIcon.CurrentLocation, () => Focus(ground, false), true);
                Add("ZOOM IN", AvIcon.Maximize, () => Focus(ground, true), true);
            }
            // The world camera is the player's only while they fly nothing (the vanilla icon-click rule).
            if (!GameManager.GetLocalAircraft(out Aircraft flown) || flown == null || flown.disabled)
            {
                Unit goTo = contact != null ? contact.unit : null;
                Add(goTo != null ? "GO TO UNIT" : "GO TO PLACE", AvIcon.Camera, () => GoTo(goTo, ground), true);
            }
            bool marked = mark.HasValue;
            Add(marked ? "MOVE MARK" : "RANGE MARK", AvIcon.Ruler2, () => SetMark(ground), true);
            if (marked) Add("CLEAR MARK", AvIcon.Eraser, () => SetMark(null), true);
            // The landing zone stands until moved or cleared: every HALO stick and glider this
            // pilot releases aims at it (Urban Combat owns the zone and its host sync).
            if (ModuleServices.TryGet(out ILandingZone _) && GameManager.GetLocalAircraft(out Aircraft _))
            {
                bool zoned = TryZone(out _);
                Add(zoned ? "MOVE LZ" : "LANDING ZONE", AvIcon.Parachute, () => SetLandingZone(ground), true);
                if (zoned) Add("CLEAR LZ", AvIcon.Eraser, () => SetLandingZone(null), true);
            }
            Add("COPY FIX", AvIcon.Copy, () =>
            {
                GUIUtility.systemCopyBuffer = "X " + AvNum.Fixed(ground.x, 0) + "  Z " + AvNum.Fixed(ground.z, 0);
                Echo("GROUND FIX COPIED");
            }, true);

            Color tone = MapContextRead.Fill(map, ground, mark, rows);
            string title, kind;
            if (contact != null)
            {
                title = FirstLine(contact.GetInfoText());
                FactionMode side = DynamicMap.GetFactionMode(contact.unit.NetworkHQ, false);
                kind = side == FactionMode.Enemy ? "HOSTILE TRACK" : side == FactionMode.Friendly ? "OWN TRACK" : "TRACK";
                tone = side == FactionMode.Enemy ? AvStyleHost.FuiColor("danger", AvTheme.Alert)
                    : side == FactionMode.Friendly ? AvStyleHost.FuiColor("friendly", AvTheme.Friendly) : AvStyleHost.FuiColor("muted", AvTheme.Dim);
            }
            else if (airbase != null)
            {
                title = FirstLine(airbase.GetInfoText());
                kind = "AIRBASE";
            }
            else
            {
                title = "GROUND FIX";
                kind = TakeRow("FIX") ?? "";
            }
            // A wingman's own rows lead the facts: that is what the click was about.
            rows.InsertRange(0, wingRows);
            MapContextCard.Show(parent, click, title, kind, tone, rows, actions);
        }

        /// <summary>Files an action under the current <see cref="group"/>; pressing it closes the card first.</summary>
        private static void Add(string label, AvIcon icon, Action invoke, bool half = false) =>
            actions.Add(new MapMenuAction { Label = label, Group = group, Icon = icon, Invoke = Closing(invoke), Half = half });

        private static Action Closing(Action invoke) => () =>
        {
            HideMenu();
            invoke?.Invoke();
        };

        private static void AddClear(DynamicMap map, int selected)
        {
            if (selected <= 0) return;
            Add("CLEAR SELECTION  ·  " + selected, AvIcon.Trash, () =>
            {
                ClearSelection(map);
                Echo("SELECTION CLEARED");
            });
        }

        /// <summary>The wing's orders for this click: Wing owns and runs them, the card only presses.</summary>
        private static void FillWing(Unit clicked, GlobalPosition ground)
        {
            if (!ModuleServices.TryGet(out IMapWingMenu wing)) return;
            int first = actions.Count;
            wing.Fill(clicked, ground, wingRows, actions);
            for (int i = first; i < actions.Count; i++)
            {
                MapMenuAction a = actions[i];
                a.Invoke = Closing(a.Invoke);
                if (string.IsNullOrEmpty(a.Group)) a.Group = "WING";
                actions[i] = a;
            }
        }

        /// <summary>Removes the row with this key and returns its value (the ground fix goes in the card's head instead).</summary>
        private static string TakeRow(string key)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Key != key) continue;
                string value = rows[i].Value;
                rows.RemoveAt(i);
                return value;
            }
            return null;
        }

        /// <summary>"274° / 38.2 KM": compass bearing and flat range from one map point to another.</summary>
        internal static string LegText(GlobalPosition from, GlobalPosition to) =>
            AvNum.Fixed(MapFix.Bearing(from.x, from.z, to.x, to.z), 0).PadLeft(3, '0') + "° / " +
            AvNum.Fixed(MapFix.Range(from.x, from.z, to.x, to.z) / 1000f, 1) + " KM";

        /// <summary>Bearing and range from the dropped range mark, or null while none is set.</summary>
        internal static string FromMark(GlobalPosition to) => mark.HasValue ? LegText(mark.Value, to) : null;

        private static bool TryZone(out GlobalPosition point)
        {
            point = default;
            return ModuleServices.TryGet(out ILandingZone zone) && zone.TryGetLocal(out point);
        }

        private static void SetLandingZone(GlobalPosition? point)
        {
            if (ModuleServices.TryGet(out ILandingZone zone))
            {
                if (point.HasValue) zone.SetLocal(point.Value);
                else zone.ClearLocal();
            }
            Echo(point.HasValue ? "LANDING ZONE SET  /  PARATROOPERS AND GLIDERS AIM THERE" : "LANDING ZONE CLEARED");
        }

        private static void SetMark(GlobalPosition? point)
        {
            mark = point;
            Echo(point.HasValue ? "RANGE MARK SET  /  CURSOR LINE READS FROM IT" : "RANGE MARK CLEARED");
        }

        private static void DrawMark(DynamicMap map)
        {
            RectTransform viewport = map.mapBackground.rectTransform;
            RectTransform sheet = mark.HasValue ? MfdTerrainRelief.MapImageRect(map.mapImage) : null;
            Vector3 local = default;
            // ponytail: the mark is drawn on the relief only; the flat sheet still gets every FROM MARK
            // readout. Project through the native sheet too if flat-map users ask for the glyph.
            bool shown = sheet != null &&
                MfdTerrainRelief.TryProject(mark.Value.x, mark.Value.z, sheet.rect, out Vector2 point) &&
                viewport.rect.Contains(local = viewport.InverseTransformPoint(sheet.TransformPoint(point)));
            if (!shown)
            {
                if (markGlyph != null) markGlyph.gameObject.SetActive(false);
                return;
            }
            if (markGlyph == null || markGlyph.parent != viewport)
            {
                if (markGlyph != null) Object.Destroy(markGlyph.gameObject);
                TMP_Text glyph = AvIcons.Make(viewport, AvIcon.Crosshair, 22f,
                    AvStyleHost.FuiColor("select", AvTheme.Accent));
                glyph.name = "NOAvionics.MapMark";
                glyph.raycastTarget = false;
                markGlyph = glyph.rectTransform;
                markGlyph.sizeDelta = new Vector2(26f, 26f);
            }
            markGlyph.gameObject.SetActive(true);
            markGlyph.localPosition = new Vector3(local.x, local.y, 0f);
        }

        // ---- armed tools: the one banner and the pause key ----

        /// <summary>Esc cancels an armed tool or closes the card and must not also pause the game: the key is held while either is
        /// up (WMC holds it for its own tools) through Core's shared <see cref="PauseKey"/>, which gives it back as it was found.</summary>
        private static void HoldPause()
        {
            pauseHeld = !Application.isBatchMode &&
                ((MapPicker.IsBusy && !MapPicker.IsOwner(MapPicker.WingPoint)) || MapContextCard.Visible);
            PauseKey.Hold(PauseOwner, pauseHeld);
        }

        private static void ReleasePause()
        {
            pauseHeld = false;
            PauseKey.Hold(PauseOwner, false);
        }

        /// <summary>Whatever panel armed it, an armed tool reads the same at the top of the map: what it places, then how.</summary>
        private static void DrawBanner(DynamicMap map)
        {
            string prompt = MapPicker.IsBusy ? MapPicker.Prompt : null;
            RectTransform viewport = map.mapBackground.rectTransform;
            if (string.IsNullOrEmpty(prompt))
            {
                if (banner != null && banner.gameObject.activeSelf) banner.gameObject.SetActive(false);
                return;
            }
            if (banner == null || banner.parent != viewport) BuildBanner(viewport);
            // On top when it appears, not every frame: the deck and the view bar keep their own places beside it.
            if (!banner.gameObject.activeSelf)
            {
                banner.gameObject.SetActive(true);
                banner.SetAsLastSibling();
            }
            // Bottom centre: the top edge belongs to the STR plate and the selection deck (2026-10-29 sim capture).
            banner.anchoredPosition = new Vector2(Mathf.Max(8f, (viewport.rect.width - BannerWidth) * .5f), 14f + BannerHeight);
            if (prompt == bannerPrompt) return;
            bannerPrompt = prompt;
            bannerWhat.text = prompt.ToUpperInvariant();
        }

        private static void BuildBanner(RectTransform viewport)
        {
            if (banner != null) Object.Destroy(banner.gameObject);
            var go = new GameObject("NOAvionics.MapToolBanner", typeof(RectTransform));
            banner = go.GetComponent<RectTransform>();
            banner.SetParent(viewport, false);
            banner.anchorMin = banner.anchorMax = new Vector2(0f, 0f);
            banner.pivot = new Vector2(0f, 1f);
            banner.sizeDelta = new Vector2(BannerWidth, BannerHeight);
            Color caution = AvStyleHost.FuiColor("caution", AvTheme.Warning);
            AvFrame back = MfdChromeLay.Panel(banner, "Back", new Rect(0f, 0f, BannerWidth, BannerHeight),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.94f), caution, AvChamfer.Diagonal(5f));
            back.raycastTarget = false;
            MfdChromeLay.Rule(banner, "Rail", new Rect(1f, -5f, 3f, BannerHeight - 10f), caution).raycastTarget = false;
            bannerWhat = AvText.Make(banner, "What", AvTextRole.Head, "", TextAlignmentOptions.Center);
            MfdChromeLay.Place(bannerWhat.rectTransform, new Rect(10f, -3f, BannerWidth - 20f, 19f));
            bannerWhat.color = caution;
            bannerWhat.raycastTarget = false;
            AvText.Fit(bannerWhat, false);
            TMP_Text how = AvText.Make(banner, "How", AvTextRole.Micro,
                "RIGHT CLICK PLACES  ·  ESC CANCELS  ·  LEFT BUTTON STILL PANS AND SELECTS", TextAlignmentOptions.Center);
            MfdChromeLay.Place(how.rectTransform, new Rect(10f, -23f, BannerWidth - 20f, 13f));
            how.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            how.raycastTarget = false;
            AvText.Fit(how, false);
            bannerPrompt = null;
        }

        // ---- the actions ----

        private static void Focus(GlobalPosition ground, bool zoom)
        {
            ReliefNavigator.Focus(ground, zoom);
            Echo(zoom ? "AREA FRAMED" : "VIEW CENTERED");
        }

        /// <summary>Sends the world camera to follow the unit, or to look down on the clicked ground. An enemy
        /// whose fix is not accurate gets the ground point: the camera must not reveal where it really is.</summary>
        private static void GoTo(Unit unit, GlobalPosition ground)
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return;
            if (unit != null && !unit.disabled && (!GameManager.GetLocalHQ(out FactionHQ hq) ||
                unit.NetworkHQ == null || hq == unit.NetworkHQ || hq.IsTargetPositionAccurate(unit, 100f)))
            {
                cameras.SetFollowingUnit(unit);
                Echo("CAMERA ON UNIT");
                return;
            }
            Vector3 point = ground.ToLocalPosition();
            if (Physics.Raycast(new Vector3(point.x, 12000f, point.z), Vector3.down, out RaycastHit hit, 24000f))
                point.y = hit.point.y;
            // Keep the heading, look 30 degrees down from 400 m back.
            Quaternion look = Quaternion.Euler(30f, cameras.transform.eulerAngles.y, 0f);
            cameras.FocusPosition(point, look, 400f);
            Echo("CAMERA MOVED");
        }

        private static void SelectTrack(DynamicMap map, Unit unit, bool add)
        {
            if (map == null || unit == null || unit.disabled || Excluded(unit)) return;
            if (GameManager.GetLocalAircraft(out Aircraft localAircraft) && unit == localAircraft) return;
            if (add && IsSelected(map, unit)) Deselect(map, unit);
            else
            {
                if (!add) ClearSelection(map);
                Select(map, unit);
            }
            Echo($"{map.selectedIcons?.Count ?? 0} TRACKS SELECTED");
        }

        /// <summary>The selection deck's FRAME: centres the view on what is selected, at the positions the local faction knows.</summary>
        internal static void FrameSelection()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.selectedIcons == null) return;
            GameManager.GetLocalHQ(out FactionHQ hq);
            float x = 0f, z = 0f;
            int n = 0;
            for (int i = 0; i < map.selectedIcons.Count; i++)
            {
                if (!(map.selectedIcons[i] is UnitMapIcon icon) || icon.unit == null || icon.unit.disabled) continue;
                GlobalPosition at;
                if (hq == null || icon.unit.NetworkHQ == hq) at = icon.unit.GlobalPosition();
                else if (!hq.TryGetKnownPosition(icon.unit, out at)) continue;
                x += at.x;
                z += at.z;
                n++;
            }
            if (n == 0 || !MapFocus.To(x / n, z / n, n == 1)) return;
            Echo(n == 1 ? "VIEW ON THE TRACK" : "VIEW ON " + n + " TRACKS");
        }

        /// <summary>The selection deck's SAME TYPE: every visible contact of the first selected one's type and side.</summary>
        internal static void SelectSameType()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.selectedIcons == null) return;
            for (int i = 0; i < map.selectedIcons.Count; i++)
            {
                if (!(map.selectedIcons[i] is UnitMapIcon icon) || icon.unit == null) continue;
                SelectVisibleType(map, icon.unit);
                return;
            }
        }

        private static void SelectVisibleType(DynamicMap map, Unit exemplar)
        {
            if (map == null || exemplar == null || exemplar.definition == null || map.mapIcons == null) return;
            candidates.Clear();
            CollectVisibleType(map, exemplar);
            ClearSelection(map);
            for (int i = 0; i < candidates.Count; i++) Select(map, candidates[i]);
            Echo($"{candidates.Count} TRACKS SELECTED");
        }

        private static int CountVisibleType(DynamicMap map, Unit exemplar)
        {
            if (map == null || exemplar == null || exemplar.definition == null || map.mapIcons == null) return 0;
            candidates.Clear();
            CollectVisibleType(map, exemplar);
            int count = candidates.Count;
            candidates.Clear();
            return count;
        }

        private static void CollectVisibleType(DynamicMap map, Unit exemplar)
        {
            GameManager.GetLocalAircraft(out Aircraft localAircraft);
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit && candidates.Count < MaximumSelected; i++)
            {
                if (!Pickable(map.mapIcons[i], localAircraft, out UnitMapIcon icon) ||
                    icon.unit.definition != exemplar.definition ||
                    icon.unit.NetworkHQ != exemplar.NetworkHQ) continue;
                if (MapUiPointer.Contains(map.mapBackground.rectTransform, IconScreen(icon))) candidates.Add(icon.unit);
            }
        }

        private static int CountNearby(DynamicMap map, Vector2 screen)
        {
            if (map.mapIcons == null) return 0;
            int count = 0;
            GameManager.GetLocalAircraft(out Aircraft localAircraft);
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit; i++)
            {
                if (!Pickable(map.mapIcons[i], localAircraft, out UnitMapIcon icon)) continue;
                if ((IconScreen(icon) - screen).sqrMagnitude <= 28f * 28f) count++;
            }
            return count;
        }

        private static void SelectNearby(DynamicMap map, Vector2 screen)
        {
            candidates.Clear();
            if (map.mapIcons != null)
            {
                GameManager.GetLocalAircraft(out Aircraft localAircraft);
                int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
                for (int i = 0; i < limit && candidates.Count < MaximumSelected; i++)
                {
                    if (!Pickable(map.mapIcons[i], localAircraft, out UnitMapIcon icon)) continue;
                    if ((IconScreen(icon) - screen).sqrMagnitude <= 28f * 28f)
                        candidates.Add(icon.unit);
                }
            }
            ClearSelection(map);
            for (int i = 0; i < candidates.Count; i++) Select(map, candidates[i]);
            Echo(candidates.Count + " NEARBY TRACKS SELECTED");
        }

        private static AirbaseMapIcon AirbaseAt(DynamicMap map, Vector2 screen)
        {
            AirbaseMapIcon nearest = null;
            float best = 22f * 22f;
            if (map.mapIcons == null) return null;
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit; i++)
            {
                if (!(map.mapIcons[i] is AirbaseMapIcon icon) || icon.airbase == null ||
                    icon.iconImage == null || !icon.iconImage.enabled ||
                    !icon.gameObject.activeInHierarchy || !icon.iconImage.gameObject.activeInHierarchy) continue;
                Vector2 point = IconScreen(icon);
                float distance = (point - screen).sqrMagnitude;
                if (distance >= best) continue;
                nearest = icon;
                best = distance;
            }
            return nearest;
        }

        private static UnitMapIcon ContactAt(DynamicMap map, Vector2 screen)
        {
            UnitMapIcon nearest = null;
            float best = ContactRadius * ContactRadius;
            if (map.mapIcons == null) return null;
            GameManager.GetLocalAircraft(out Aircraft localAircraft);
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit; i++)
            {
                if (!Pickable(map.mapIcons[i], localAircraft, out UnitMapIcon icon)) continue;
                float distance = (IconScreen(icon) - screen).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = icon;
            }
            return nearest;
        }

        /// <summary>A contact the pointer may act on: a live unit other than the player's aircraft whose icon is drawn (or hidden only by a stack) and not filtered out of the target list.</summary>
        private static bool Pickable(MapIcon mapIcon, Aircraft localAircraft, out UnitMapIcon icon)
        {
            icon = mapIcon as UnitMapIcon;
            return icon != null && icon.unit != null && icon.unit != localAircraft && !icon.unit.disabled &&
                   icon.iconImage != null && icon.gameObject.activeInHierarchy && icon.iconImage.gameObject.activeInHierarchy &&
                   (icon.iconImage.enabled || MfdTerrainRelief.ClusterHidden(icon.iconImage)) && !Excluded(icon.unit);
        }

        private static Vector2 IconScreen(MapIcon icon)
        {
            Canvas canvas = icon.GetComponentInParent<Canvas>();
            Camera camera = MapUiPointer.UiCamera(canvas);
            return RectTransformUtility.WorldToScreenPoint(camera, icon.iconImage.transform.position);
        }

        private static bool Excluded(Unit unit)
        {
            TargetListSelector selector = SceneSingleton<TargetListSelector>.i;
            return selector != null && selector.CheckExclusions(unit);
        }

        private static void DrawBox(Canvas canvas, Vector2 start, Vector2 end, MapBoxMode mode)
        {
            RectTransform parent = canvas.transform as RectTransform;
            if (parent == null || !ToCanvas(parent, start, out Vector2 a) ||
                !ToCanvas(parent, end, out Vector2 b)) return;
            if (box == null)
            {
                var go = new GameObject("NOAvionics.SelectionBox", typeof(RectTransform), typeof(CanvasRenderer));
                box = go.GetComponent<RectTransform>();
                box.SetParent(parent, false);
                box.anchorMin = box.anchorMax = box.pivot = Vector2.zero;
                AvFrame frame = go.AddComponent<AvFrame>();
                frame.Chamfer = AvChamfer.All(0f);
                frame.Stroke = 1.5f;
                frame.raycastTarget = false;
                // A dropping box wears the caution ink, so the drag says which way it works before the release.
                if (mode == MapBoxMode.Drop)
                    frame.Paint(AvStyleHost.FuiColor("caution", AvTheme.Warning).WithAlpha(.1f), AvStyleHost.FuiColor("caution", AvTheme.Warning));
                else
                    frame.Paint(AvStyleHost.FuiColor("info", AvTheme.RailInfo).WithAlpha(.13f), AvStyleHost.FuiColor("select", AvTheme.Accent));
            }
            box.anchoredPosition = Vector2.Min(a, b);
            box.sizeDelta = new Vector2(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
            box.SetAsLastSibling();
        }

        private static bool ToCanvas(RectTransform parent, Vector2 screen, out Vector2 point)
        {
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera camera = MapUiPointer.UiCamera(canvas);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen,
                    camera, out Vector2 local))
            {
                point = default;
                return false;
            }
            point = local + Vector2.Scale(parent.rect.size, parent.pivot);
            return true;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "TRACK";
            int end = text.IndexOf('\n');
            if (end < 0) end = text.Length;
            return text.Substring(0, Mathf.Min(end, 31));
        }

        /// <summary>What this left drag does: the one answer for the box, the relief grab and the native pan guard.</summary>
        internal static MapBoxMode BoxModeNow() => MapClickRules.BoxMode(ControlHeld(), ShiftHeld(),
            Plugin.Settings?.Command?.MapLeftDragBox?.Value == true);

        /// <summary>A left drag draws a selection box instead of panning.</summary>
        internal static bool BoxDrag() => BoxModeNow() != MapBoxMode.None;

        // ---- selection: the one door every map gesture goes through ----

        private static bool Flying(out CombatHUD hud)
        {
            hud = SceneSingleton<CombatHUD>.i;
            return hud != null && hud.aircraft != null && !hud.aircraft.disabled;
        }

        private static bool IsSelected(DynamicMap map, Unit unit)
        {
            for (int i = 0; i < map.selectedIcons.Count; i++)
                if (map.selectedIcons[i] is UnitMapIcon icon && icon.unit == unit) return true;
            return false;
        }

        /// <summary>
        /// Selects a contact the way the game does. While the player flies, a selected contact IS a
        /// weapon target: it has to go through the HUD, which adds it to the aircraft's target list and
        /// lights the icon. Writing the icon alone made contacts look selected that were never targets.
        /// </summary>
        private static void Select(DynamicMap map, Unit unit)
        {
            if (unit == null || Excluded(unit) || IsSelected(map, unit)) return;
            if (Flying(out CombatHUD hud)) hud.SelectUnit(unit);
            else map.SelectIcon(unit);
        }

        private static void Deselect(DynamicMap map, Unit unit)
        {
            if (unit == null) return;
            if (Flying(out CombatHUD hud) && hud.GetTargetList().Contains(unit)) hud.DeSelectUnit(unit);
            else map.DeselectIcon(unit);
        }

        /// <summary>Empties the selection, and with it the target list while flying.</summary>
        internal static void ClearSelection(DynamicMap map)
        {
            if (map == null) return;
            if (Flying(out CombatHUD hud)) hud.DeselectAll(false);
            map.UnselectAll();
        }

        /// <summary>The deck's CLEAR.</summary>
        internal static void ClearSelectionTool()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;
            ClearSelection(map);
            Echo("SELECTION CLEARED");
        }

        internal static bool ControlHeld() => Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);
        internal static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift);

        /// <summary>A panel's own left-drag (COM hold-to-draw) has the left button: no box, no icon click.</summary>
        private static bool BoxBlocked() => ModuleServices.TryGet(out IMapBoxInput input) && input.BlocksBoxSelection;

        internal static void Echo(string value)
        {
            feedback = value;
            feedbackUntil = Time.unscaledTime + 2f;
        }

        internal static bool Typing() => MapTool.Typing();

        private static void CancelBox()
        {
            boxPress = false;
            boxDragged = false;
            if (box != null) Object.Destroy(box.gameObject);
            box = null;
        }

        private static void HideMenu() => MapContextCard.Hide();

        /// <summary>Scene reset: everything <see cref="Restore"/> drops, the pooled card and the banner, and the range mark.</summary>
        internal static void Reset()
        {
            Restore();
            MapContextCard.Destroy();
            if (banner != null) Object.Destroy(banner.gameObject);
            banner = null;
            bannerWhat = null;
            bannerPrompt = null;
            MapSelectionDeck.Reset();
            mark = null;
        }

        /// <summary>Map closed: gestures end, the card hides (kept for the next open), the pause key goes back.</summary>
        internal static void Restore()
        {
            CancelBox();
            HideMenu();
            ReleasePause();
            MapSelectionDeck.Hide();
            if (banner != null) banner.gameObject.SetActive(false);
            if (markGlyph != null) Object.Destroy(markGlyph.gameObject);
            markGlyph = null;
            rows.Clear();
            wingRows.Clear();
            rightPress = false;
            boxReleaseFrame = -1;
            candidates.Clear();
            actions.Clear();
            feedback = null;
            feedbackUntil = 0f;
        }
    }

    /// <summary>The runway marker owns its larger click target without changing the native icon.</summary>
    internal sealed class AirbaseHitTarget : MonoBehaviour, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        internal AirbaseMapIcon Icon;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                MfdMapInteractions.HandleAirbaseClick(Icon);
        }
        public void OnPointerEnter(PointerEventData eventData) =>
            SceneSingleton<DynamicMap>.i?.DisplayTooltip(Icon);
        public void OnPointerExit(PointerEventData eventData) =>
            SceneSingleton<DynamicMap>.i?.HideTooltip();
    }

    /// <summary>The symbol plate widens the native glyph's click target.</summary>
    internal sealed class UnitHitTarget : MonoBehaviour, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        internal UnitMapIcon Icon;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                MfdMapInteractions.HandleUnitClick(Icon, true);
        }
        public void OnPointerEnter(PointerEventData eventData) =>
            SceneSingleton<DynamicMap>.i?.DisplayTooltip(Icon);
        public void OnPointerExit(PointerEventData eventData) =>
            SceneSingleton<DynamicMap>.i?.HideTooltip();
    }
}
