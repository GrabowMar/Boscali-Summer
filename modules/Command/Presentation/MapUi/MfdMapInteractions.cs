using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Map-only RTS affordances. Native DynamicMap still owns contacts and selection; this
    /// surface never creates a unit order or reads an unobserved unit position.
    /// </summary>
    internal static class MfdMapInteractions
    {
        private const float DragSlop = 6f;
        private const float ContactRadius = 18f;
        private const int MaximumScannedIcons = 2048;
        private const int MaximumSelected = 128;
        private const float MenuWidth = 284f;
        private const float HeadHeight = 26f;
        private const float RowHeight = 17f;
        private const float KeyWidth = 76f;
        private const float ActionHeight = 28f;
        private const float ActionGap = 3f;

        private static readonly List<Unit> candidates = new List<Unit>(MaximumSelected);
        private static readonly List<MapMenuAction> actions = new List<MapMenuAction>(8);
        private static readonly List<MapMenuRow> rows = new List<MapMenuRow>(10), wingRows = new List<MapMenuRow>(4);

        private static RectTransform box;
        private static RectTransform menu;
        /// <summary>The dropped range mark: a map close keeps it, a scene reset forgets it.</summary>
        private static GlobalPosition? mark;
        private static RectTransform markGlyph;
        private static RectTransform lzGlyph;
        private static bool boxPress;
        private static bool boxDragged;
        private static Vector2 boxStart;
        private static int boxReleaseFrame = -1;
        private static int iconClickFrame = -1;
        /// <summary>Frame of a bare-map release still waiting to learn whether an icon took it; -1 for none.</summary>
        private static int clearFrame = -1;
        private static bool rightPress;
        private static Vector2 rightStart;
        private static bool leftPress;
        private static Vector2 leftStart;
        private static string feedback;
        private static float feedbackUntil;

        internal static string Feedback => Time.unscaledTime < feedbackUntil ? feedback : null;

        internal static bool ContainsMenu(Vector2 screen) => MapUiPointer.Contains(menu, screen);

        /// <summary>Another map control (an STR tag or card) took this click: stamp the frame and drop the press so the release does not clear the selection.</summary>
        internal static void ConsumeClick()
        {
            iconClickFrame = Time.frameCount;
            leftPress = false;
            HideMenu();
        }

        internal static bool ClickedThisFrame => iconClickFrame == Time.frameCount;

        /// <summary>Suppress the native immediate RMB waypoint and the native Ctrl-drag pan.</summary>
        internal static bool BlockNativeControls(DynamicMap map)
        {
            if (map == null || !DynamicMap.mapMaximized || map.mapBackground == null) return false;
            if (Input.GetMouseButtonDown(1)) return true;
            if (boxPress && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0))) return true;
            return (Input.GetMouseButton(0) || Input.GetMouseButtonDown(0)) && BoxDrag() &&
                !BoxBlocked() && !MapUiPointer.OverControls() &&
                map.TryGetCursorCoordinates(out _);
        }

        internal static bool BlockIconClick() => DynamicMap.mapMaximized &&
            ((boxPress && (boxDragged ||
                ((Vector2)Input.mousePosition - boxStart).sqrMagnitude >= DragSlop * DragSlop)) ||
             boxReleaseFrame == Time.frameCount);

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
            // CLICK ONE replaces it; Shift or Ctrl still adds. Empty ground clears.
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
            Echo((map.selectedIcons?.Count ?? 0) + " TRACKS SELECTED  /  CLICK EMPTY MAP TO CLEAR");
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
            DrawMark(map);
            DrawLandingZone(map);
            // The input module may hand the icon its click on the release frame or the one after, so a
            // bare-map release waits out both before it is allowed to clear.
            if (clearFrame >= 0 && Time.frameCount > clearFrame + 1)
            {
                if (iconClickFrame < clearFrame)
                {
                    ClearSelection(map);
                    Echo("SELECTION CLEARED  /  RIGHT CLICK FOR MAP ACTIONS");
                }
                clearFrame = -1;
            }
            if (BoxBlocked())
            {
                CancelBox();
                HideMenu();
                if (MapPicker.IsBusy) rightPress = false;
                leftPress = false;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelBox();
                HideMenu();
                rightPress = false;
                leftPress = false;
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                bool onMenu = ContainsMenu(Input.mousePosition);
                if (!onMenu) HideMenu();
                rightStart = Input.mousePosition;
                // Latch ownership on press: Support can disarm during its release Update.
                rightPress = !onMenu && !MapPicker.IsBusy &&
                    !MapUiPointer.OverControls() &&
                    MapUiPointer.Contains(map.mapBackground.rectTransform, rightStart);
            }
            if (Input.GetMouseButtonUp(1))
            {
                GlobalPosition point = default;
                bool open = rightPress && !MapPicker.IsBusy && !ReliefNavigator.BlockContextClick() &&
                    ((Vector2)Input.mousePosition - rightStart).sqrMagnitude < DragSlop * DragSlop &&
                    !MapUiPointer.OverControls() && map.TryGetCursorCoordinates(out point);
                rightPress = false;
                if (open) ShowMenu(map, point);
            }

            if (Input.GetMouseButtonDown(0))
            {
                bool onMenu = ContainsMenu(Input.mousePosition);
                if (menu != null && !onMenu) HideMenu();
                boxPress = BoxDrag() && !BoxBlocked() && !MapUiPointer.OverControls() &&
                    !Typing() && map.TryGetCursorCoordinates(out _);
                boxDragged = false;
                if (boxPress) boxStart = Input.mousePosition;
                leftStart = Input.mousePosition;
                leftPress = !boxPress && !onMenu && !BoxDrag() && !BoxBlocked() &&
                    !Typing() && !MapUiPointer.OverControls() &&
                    MapUiPointer.Contains(map.mapBackground.rectTransform, leftStart);
            }
            if (Input.GetMouseButtonUp(0) && !boxPress)
            {
                GlobalPosition point = default;
                bool open = leftPress && !BoxBlocked() && !ReliefNavigator.BlockContextClick() &&
                    ((Vector2)Input.mousePosition - leftStart).sqrMagnitude < DragSlop * DragSlop &&
                    !MapUiPointer.OverControls() && map.TryGetCursorCoordinates(out point);
                leftPress = false;
                // Decide later: the UI may deliver this release's icon click after this tick, and
                // clearing now would empty the selection just before that click adds to it.
                clearFrame = open && !ShiftHeld() ? Time.frameCount : -1;
            }
            if (!boxPress) return;
            if (!BoxDrag() || BoxBlocked() || MapUiPointer.OverControls() ||
                !map.TryGetCursorCoordinates(out _))
            {
                CancelBox();
                return;
            }
            Vector2 current = Input.mousePosition;
            if ((current - boxStart).sqrMagnitude >= DragSlop * DragSlop) boxDragged = true;
            if (Input.GetMouseButtonUp(0))
            {
                if (boxDragged && map.TryGetCursorCoordinates(out _))
                {
                    SelectBox(map, new Rect(Mathf.Min(boxStart.x, current.x),
                        Mathf.Min(boxStart.y, current.y), Mathf.Abs(current.x - boxStart.x),
                        Mathf.Abs(current.y - boxStart.y)), true);
                    boxReleaseFrame = Time.frameCount;
                }
                CancelBox();
            }
            else if (boxDragged) DrawBox(map.maximizedMapCanvas, boxStart, current);
        }

        private static void SelectBox(DynamicMap map, Rect region, bool add)
        {
            candidates.Clear();
            if (map.mapIcons == null) return;
            GameManager.GetLocalAircraft(out Aircraft localAircraft);
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit; i++)
            {
                if (!(map.mapIcons[i] is UnitMapIcon icon) || icon.unit == null ||
                    icon.unit == localAircraft || icon.unit.disabled ||
                    !icon.gameObject.activeInHierarchy || icon.iconImage == null ||
                    !icon.iconImage.gameObject.activeInHierarchy ||
                    (!icon.iconImage.enabled && !MfdTerrainRelief.ClusterHidden(icon.iconImage)) ||
                    Excluded(icon.unit)) continue;
                if (!MapClickRules.BoxTakes(Plugin.Settings?.Command?.MapBoxSide?.Value ?? MapBoxSide.All, icon.unit.NetworkHQ != null, icon.unit.NetworkHQ == map.HQ)) continue;
                Vector2 screen = IconScreen(icon);
                if (!region.Contains(screen) ||
                    !MapUiPointer.Contains(map.mapBackground.rectTransform, screen)) continue;
                if (candidates.Count < MaximumSelected) candidates.Add(icon.unit);
            }
            if (!add) ClearSelection(map);
            for (int i = 0; i < candidates.Count; i++) Select(map, candidates[i]);
            Echo(candidates.Count > 0 ? $"{candidates.Count} TRACKS SELECTED" : "SELECTION CLEARED");
        }

        private static void ShowMenu(DynamicMap map, GlobalPosition ground)
        {
            HideMenu();
            Canvas canvas = map.maximizedMapCanvas;
            RectTransform parent = canvas.transform as RectTransform;
            if (parent == null) return;
            UnitMapIcon contact = ContactAt(map, Input.mousePosition);
            AirbaseMapIcon airbase = contact == null ? AirbaseAt(map, Input.mousePosition) : null;
            Vector2 click = Input.mousePosition;
            int nearby = CountNearby(map, click);
            int selected = map.selectedIcons?.Count ?? 0;
            actions.Clear();
            if (contact != null)
            {
                Unit unit = contact.unit;
                Add("FRAME", AvIcon.Focus2, () => Focus(ground, true), true);
                Add("SELECT ONLY", AvIcon.Target, () => SelectTrack(map, unit, false), true);
                // The line says what the click will do, not both things it might.
                bool held = selected > 0 && TrySelectedUnit(map, unit);
                Add(held ? "DROP FROM SELECTION" : "ADD TO SELECTION", held ? AvIcon.Minus : AvIcon.Plus,
                    () => SelectTrack(map, unit, true));
                if (nearby > 1)
                    Add("SELECT STACK  /  " + nearby, AvIcon.Stack2, () => SelectNearby(map, click));
                Add("SELECT ALL OF THIS TYPE", AvIcon.Filter, () => SelectVisibleType(map, unit));
            }
            else if (airbase != null)
            {
                AirbaseMapIcon target = airbase;
                Add("FRAME", AvIcon.Focus2, () => Focus(ground, true), true);
                Add("SELECT BASE", AvIcon.Runway, () =>
                {
                    ClearSelection(map);
                    if (target != null && target.airbase != null) map.SelectIcon(target.airbase);
                    HideMenu();
                }, true);
            }
            else
            {
                Add("CENTER", AvIcon.CurrentLocation, () => Focus(ground, false), true);
                Add("ZOOM IN", AvIcon.Maximize, () => Focus(ground, true), true);
                if (nearby > 1)
                    Add("SELECT NEARBY  /  " + nearby, AvIcon.Stack2, () => SelectNearby(map, click));
            }
            // The wing's orders for this click: Wing owns and runs them, the card only presses.
            wingRows.Clear();
            if (airbase == null && ModuleServices.TryGet(out IMapWingMenu wing))
            {
                int first = actions.Count;
                wing.Fill(contact != null ? contact.unit : null, ground, wingRows, actions);
                for (int i = first; i < actions.Count; i++)
                {
                    MapMenuAction a = actions[i];
                    Action run = a.Invoke;
                    a.Invoke = () =>
                    {
                        HideMenu();
                        run?.Invoke();
                    };
                    actions[i] = a;
                }
            }
            // The world camera is the player's only while they fly nothing (the vanilla icon-click rule).
            if (!GameManager.GetLocalAircraft(out Aircraft flown) || flown == null || flown.disabled)
            {
                Unit goTo = contact != null ? contact.unit : null;
                Add(goTo != null ? "GO TO UNIT" : "GO TO PLACE", AvIcon.Camera, () => GoTo(goTo, ground));
            }
            // The landing zone stands until moved or cleared: every HALO stick and glider this
            // pilot releases aims at it (Urban Combat owns the zone and its host sync).
            bool zoned = TryZone(out _);
            if (ModuleServices.TryGet(out ILandingZone _) && GameManager.GetLocalAircraft(out Aircraft _))
            {
                Add(zoned ? "MOVE LANDING ZONE" : "SET LANDING ZONE", AvIcon.Parachute, () => SetLandingZone(ground), zoned);
                if (zoned) Add("CLEAR LANDING ZONE", AvIcon.Eraser, () => SetLandingZone(null), true);
            }
            bool marked = mark.HasValue;
            Add(marked ? "MOVE MARK" : "DROP RANGE MARK HERE", AvIcon.Ruler2, () => SetMark(ground), marked);
            if (marked) Add("CLEAR MARK", AvIcon.Eraser, () => SetMark(null), true);
            Add("COPY FIX", AvIcon.Copy, () =>
            {
                GUIUtility.systemCopyBuffer = "X " + AvNum.Fixed(ground.x, 0) + "  Z " + AvNum.Fixed(ground.z, 0);
                HideMenu();
                Echo("GROUND FIX COPIED");
            }, selected > 0);
            if (selected > 0)
                Add("CLEAR SEL  /  " + selected, AvIcon.Trash, () =>
                {
                    ClearSelection(map);
                    HideMenu();
                    Echo("SELECTION CLEARED");
                }, true);
            int lines = 0;
            for (int i = 0; i < actions.Count; i++, lines++)
                if (Paired(i)) i++;
            Color tone = MapContextRead.Fill(map, ground, mark, rows);
            // A wingman's own rows lead the card: that is what the click was about.
            rows.InsertRange(0, wingRows);
            float body = HeadHeight + rows.Count * RowHeight + 8f;
            float height = body + lines * (ActionHeight + ActionGap) + 5f;
            var go = new GameObject("NOAvionics.MapContext", typeof(RectTransform));
            menu = go.GetComponent<RectTransform>();
            menu.SetParent(parent, false);
            menu.anchorMin = menu.anchorMax = Vector2.zero;
            menu.pivot = new Vector2(0f, 1f);
            menu.sizeDelta = new Vector2(MenuWidth, height);
            if (ToCanvas(parent, Input.mousePosition, out Vector2 at))
                menu.anchoredPosition = new Vector2(Mathf.Clamp(at.x + 10f, 6f,
                    parent.rect.width - MenuWidth - 6f),
                    Mathf.Clamp(at.y - 10f, height + 6f, parent.rect.height - 6f));
            MfdChromeLay.Panel(menu, "Back", new Rect(0f, 0f, MenuWidth, height),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(0.97f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame), AvChamfer.Diagonal(6f));
            // The rail down the fix card carries the holder's colour; the GROUND row says it in words.
            MfdChromeLay.Rule(menu, "Tone", new Rect(1f, -8f, 3f, body - 14f), tone);
            string title = contact != null ? FirstLine(contact.GetInfoText()) :
                airbase != null ? FirstLine(airbase.GetInfoText()) : "GROUND FIX";
            // Rects are top-anchored with y already assigned directly: rows go down with negative y.
            TMP_Text titleText = AvText.Make(menu, "Title", AvTextRole.Head, title, TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(titleText.rectTransform, new Rect(12f, -5f, MenuWidth - 20f, 18f));
            titleText.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            AvText.Fit(titleText, false);
            Color key = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            for (int i = 0; i < rows.Count; i++)
            {
                float top = -(HeadHeight + i * RowHeight);
                TMP_Text keyText = AvText.Make(menu, "Key " + rows[i].Key, AvTextRole.Micro, rows[i].Key,
                    TextAlignmentOptions.MidlineLeft);
                MfdChromeLay.Place(keyText.rectTransform, new Rect(12f, top, KeyWidth, RowHeight));
                keyText.color = key;
                AvText.Fit(keyText, false);
                TMP_Text valueText = AvText.Make(menu, "Value " + rows[i].Key, AvTextRole.DataSmall, rows[i].Value,
                    TextAlignmentOptions.MidlineLeft);
                MfdChromeLay.Place(valueText.rectTransform,
                    new Rect(12f + KeyWidth, top, MenuWidth - 20f - KeyWidth, RowHeight));
                valueText.color = rows[i].Tone;
                AvText.Fit(valueText, false);
            }
            MfdChromeLay.Rule(menu, "Split", new Rect(8f, -(body - 4f), MenuWidth - 16f, 1f),
                AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
            float y = body;
            for (int i = 0; i < actions.Count; i++, y += ActionHeight + ActionGap)
            {
                bool pair = Paired(i);
                float width = pair ? (MenuWidth - 16f - ActionGap) * .5f : MenuWidth - 16f;
                PlaceAction(actions[i], 8f, y, width);
                if (pair) PlaceAction(actions[++i], 8f + width + ActionGap, y, width);
            }
            menu.SetAsLastSibling();
        }

        private static void Add(string label, AvIcon icon, Action invoke, bool half = false) =>
            actions.Add(new MapMenuAction { Label = label, Icon = icon, Invoke = invoke, Half = half });

        private static bool Paired(int index) =>
            actions[index].Half && index + 1 < actions.Count && actions[index + 1].Half;

        private static void PlaceAction(MapMenuAction action, float x, float y, float width)
        {
            AvControl button = AvControl.Make(menu,
                new AvControl.Spec(action.Label, action.Invoke, AvButtonStyle.Default, action.Icon));
            MfdChromeLay.Place(button.Rect, new Rect(x, -y, width, ActionHeight));
            button.SingleLine();
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
            HideMenu();
            Echo(point.HasValue ? "LANDING ZONE SET  /  PARATROOPERS AND GLIDERS AIM THERE" : "LANDING ZONE CLEARED");
        }

        private static void SetMark(GlobalPosition? point)
        {
            mark = point;
            HideMenu();
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
                markGlyph = glyph.rectTransform;
                markGlyph.sizeDelta = new Vector2(26f, 26f);
            }
            markGlyph.gameObject.SetActive(true);
            markGlyph.localPosition = new Vector3(local.x, local.y, 0f);
        }

        private static void DrawLandingZone(DynamicMap map)
        {
            RectTransform viewport = map.mapBackground.rectTransform;
            bool has = TryZone(out GlobalPosition point);
            RectTransform sheet = has ? MfdTerrainRelief.MapImageRect(map.mapImage) : null;
            Vector3 local = default;
            bool shown = sheet != null &&
                MfdTerrainRelief.TryProject(point.x, point.z, sheet.rect, out Vector2 at) &&
                viewport.rect.Contains(local = viewport.InverseTransformPoint(sheet.TransformPoint(at)));
            if (!shown)
            {
                if (lzGlyph != null) lzGlyph.gameObject.SetActive(false);
                return;
            }
            if (lzGlyph == null || lzGlyph.parent != viewport)
            {
                if (lzGlyph != null) Object.Destroy(lzGlyph.gameObject);
                Color tone = AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
                TMP_Text glyph = AvIcons.Make(viewport, AvIcon.Parachute, 22f, tone);
                glyph.name = "NOAvionics.LandingZone";
                lzGlyph = glyph.rectTransform;
                lzGlyph.sizeDelta = new Vector2(26f, 26f);
                TMP_Text label = AvText.Make(lzGlyph, "Label", AvTextRole.Micro, "LZ",
                    TextAlignmentOptions.Center);
                label.color = tone;
                label.raycastTarget = false;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax =
                    label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                label.rectTransform.sizeDelta = new Vector2(30f, 12f);
                label.rectTransform.anchoredPosition = new Vector2(0f, -15f);
            }
            lzGlyph.gameObject.SetActive(true);
            lzGlyph.localPosition = new Vector3(local.x, local.y, 0f);
        }

        private static void Focus(GlobalPosition ground, bool zoom)
        {
            ReliefNavigator.Focus(ground, zoom);
            HideMenu();
            Echo(zoom ? "AREA FRAMED" : "VIEW CENTERED");
        }

        /// <summary>Sends the world camera to follow the unit, or to look down on the clicked ground. An enemy
        /// whose fix is not accurate gets the ground point: the camera must not reveal where it really is.</summary>
        private static void GoTo(Unit unit, GlobalPosition ground)
        {
            HideMenu();
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
            HideMenu();
            if (map == null || unit == null || unit.disabled || Excluded(unit)) return;
            if (GameManager.GetLocalAircraft(out Aircraft localAircraft) && unit == localAircraft) return;
            if (add && TrySelectedUnit(map, unit)) Deselect(map, unit);
            else
            {
                if (!add) ClearSelection(map);
                Select(map, unit);
            }
            Echo($"{map.selectedIcons?.Count ?? 0} TRACKS SELECTED");
        }

        private static bool TrySelectedUnit(DynamicMap map, Unit unit)
        {
            foreach (MapIcon icon in map.selectedIcons)
                if (icon is UnitMapIcon selected && selected.unit == unit) return true;
            return false;
        }

        private static void SelectVisibleType(DynamicMap map, Unit exemplar)
        {
            HideMenu();
            if (map == null || exemplar == null || exemplar.definition == null || map.mapIcons == null) return;
            candidates.Clear();
            GameManager.GetLocalAircraft(out Aircraft localAircraft);
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit; i++)
            {
                if (!(map.mapIcons[i] is UnitMapIcon icon) || icon.unit == null ||
                    icon.unit == localAircraft || icon.unit.disabled ||
                    icon.unit.definition != exemplar.definition ||
                    icon.unit.NetworkHQ != exemplar.NetworkHQ || !icon.gameObject.activeInHierarchy ||
                    icon.iconImage == null || !icon.iconImage.gameObject.activeInHierarchy ||
                    (!icon.iconImage.enabled && !MfdTerrainRelief.ClusterHidden(icon.iconImage)) ||
                    Excluded(icon.unit)) continue;
                if (!MapUiPointer.Contains(map.mapBackground.rectTransform, IconScreen(icon))) continue;
                if (candidates.Count < MaximumSelected) candidates.Add(icon.unit);
            }
            ClearSelection(map);
            for (int i = 0; i < candidates.Count; i++) Select(map, candidates[i]);
            Echo($"{candidates.Count} TRACKS SELECTED");
        }

        private static int CountNearby(DynamicMap map, Vector2 screen)
        {
            if (map.mapIcons == null) return 0;
            int count = 0;
            GameManager.GetLocalAircraft(out Aircraft localAircraft);
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit; i++)
            {
                if (!(map.mapIcons[i] is UnitMapIcon icon) || icon.unit == null ||
                    icon.unit == localAircraft ||
                    icon.unit.disabled || icon.iconImage == null ||
                    !icon.gameObject.activeInHierarchy || !icon.iconImage.gameObject.activeInHierarchy ||
                    (!icon.iconImage.enabled && !MfdTerrainRelief.ClusterHidden(icon.iconImage)) ||
                    Excluded(icon.unit)) continue;
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
                    if (!(map.mapIcons[i] is UnitMapIcon icon) || icon.unit == null ||
                        icon.unit == localAircraft || icon.unit.disabled || icon.iconImage == null ||
                        !icon.gameObject.activeInHierarchy || !icon.iconImage.gameObject.activeInHierarchy ||
                        (!icon.iconImage.enabled && !MfdTerrainRelief.ClusterHidden(icon.iconImage)) ||
                        Excluded(icon.unit)) continue;
                    if ((IconScreen(icon) - screen).sqrMagnitude <= 28f * 28f)
                        candidates.Add(icon.unit);
                }
            }
            ClearSelection(map);
            for (int i = 0; i < candidates.Count; i++) Select(map, candidates[i]);
            HideMenu();
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
                if (!(map.mapIcons[i] is UnitMapIcon icon) || icon.unit == null ||
                    icon.unit == localAircraft || icon.unit.disabled ||
                    !icon.gameObject.activeInHierarchy || icon.iconImage == null ||
                    !icon.iconImage.gameObject.activeInHierarchy ||
                    (!icon.iconImage.enabled && !MfdTerrainRelief.ClusterHidden(icon.iconImage)) ||
                    Excluded(icon.unit)) continue;
                float distance = (IconScreen(icon) - screen).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = icon;
            }
            return nearest;
        }

        private static Vector2 IconScreen(MapIcon icon)
        {
            Canvas canvas = icon.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(camera, icon.iconImage.transform.position);
        }

        private static bool Excluded(Unit unit)
        {
            TargetListSelector selector = SceneSingleton<TargetListSelector>.i;
            return selector != null && selector.CheckExclusions(unit);
        }

        private static void DrawBox(Canvas canvas, Vector2 start, Vector2 end)
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
                frame.Paint(AvStyleHost.FuiColor("info", AvTheme.RailInfo).WithAlpha(.13f),
                    AvStyleHost.FuiColor("select", AvTheme.Accent));
            }
            box.anchoredPosition = Vector2.Min(a, b);
            box.sizeDelta = new Vector2(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
            box.SetAsLastSibling();
        }

        private static bool ToCanvas(RectTransform parent, Vector2 screen, out Vector2 point)
        {
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
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

        /// <summary>
        /// A left drag draws a selection box instead of panning. Shift or Ctrl asks for it; with the
        /// DRAG tool set to BOX it is the other way round, and the modifier pans.
        /// </summary>
        internal static bool BoxDrag() => (ControlHeld() || ShiftHeld()) !=
            (Plugin.Settings?.Command?.MapLeftDragBox?.Value == true);

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

        /// <summary>The CLEAR tool on the map corner.</summary>
        internal static void ClearSelectionTool()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null) return;
            ClearSelection(map);
            Echo("SELECTION CLEARED");
        }
        private static bool ControlHeld() => Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);
        private static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift);
        private static bool BoxBlocked() => MapPicker.IsBusy ||
            (ModuleServices.TryGet(out IMapBoxInput input) && input.BlocksBoxSelection);
        private static void Echo(string value)
        {
            feedback = value;
            feedbackUntil = Time.unscaledTime + 2f;
        }
        private static bool Typing()
        {
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        private static void CancelBox()
        {
            boxPress = false;
            boxDragged = false;
            if (box != null) Object.Destroy(box.gameObject);
            box = null;
        }

        private static void HideMenu()
        {
            if (menu != null) Object.Destroy(menu.gameObject);
            menu = null;
        }

        /// <summary>Scene reset: everything <see cref="Restore"/> drops, and the range mark with it.</summary>
        internal static void Reset()
        {
            Restore();
            mark = null;
        }

        internal static void Restore()
        {
            CancelBox();
            HideMenu();
            if (markGlyph != null) Object.Destroy(markGlyph.gameObject);
            markGlyph = null;
            if (lzGlyph != null) Object.Destroy(lzGlyph.gameObject);
            lzGlyph = null;
            rows.Clear();
            wingRows.Clear();
            rightPress = false;
            leftPress = false;
            boxReleaseFrame = -1;
            iconClickFrame = clearFrame = -1;
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
