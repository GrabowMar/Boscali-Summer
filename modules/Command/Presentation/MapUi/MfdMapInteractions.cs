using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        private const float MenuWidth = 248f;

        private static readonly List<Unit> candidates = new List<Unit>(MaximumSelected);
        private static readonly List<MenuAction> actions = new List<MenuAction>(8);
        private struct MenuAction { internal string Label; internal Action Invoke; }

        private static RectTransform box;
        private static RectTransform menu;
        private static bool boxPress;
        private static bool boxDragged;
        private static Vector2 boxStart;
        private static int boxReleaseFrame = -1;
        private static int iconClickFrame = -1;
        private static bool rightPress;
        private static Vector2 rightStart;
        private static bool leftPress;
        private static Vector2 leftStart;
        private static string feedback;
        private static float feedbackUntil;

        internal static string Feedback => Time.unscaledTime < feedbackUntil ? feedback : null;

        internal static bool ContainsMenu(Vector2 screen) => MapUiPointer.Contains(menu, screen);

        /// <summary>Suppress the native immediate RMB waypoint and the native Ctrl-drag pan.</summary>
        internal static bool BlockNativeControls(DynamicMap map)
        {
            if (map == null || !DynamicMap.mapMaximized || map.mapBackground == null) return false;
            if (Input.GetMouseButtonDown(1)) return true;
            if (boxPress && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0))) return true;
            return (Input.GetMouseButton(0) || Input.GetMouseButtonDown(0)) && ControlHeld() &&
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
            iconClickFrame = Time.frameCount;
            leftPress = false;
            HideMenu();
            bool add = ShiftHeld() || ControlHeld();
            if (add && map.selectedIcons.Contains(icon)) map.DeselectIcon(icon.unit);
            else
            {
                if (!add) map.UnselectAll();
                map.SelectIcon(icon.unit);
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
            iconClickFrame = Time.frameCount;
            leftPress = false;
            HideMenu();
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
                bool open = rightPress && !MapPicker.IsBusy &&
                    ((Vector2)Input.mousePosition - rightStart).sqrMagnitude < DragSlop * DragSlop &&
                    !MapUiPointer.OverControls() && map.TryGetCursorCoordinates(out point);
                rightPress = false;
                if (open) ShowMenu(map, point);
            }

            if (Input.GetMouseButtonDown(0))
            {
                bool onMenu = ContainsMenu(Input.mousePosition);
                if (menu != null && !onMenu) HideMenu();
                boxPress = ControlHeld() && !BoxBlocked() && !MapUiPointer.OverControls() &&
                    !Typing() && map.TryGetCursorCoordinates(out _);
                boxDragged = false;
                if (boxPress) boxStart = Input.mousePosition;
                leftStart = Input.mousePosition;
                leftPress = !boxPress && !onMenu && !ControlHeld() && !BoxBlocked() &&
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
                if (open && iconClickFrame != Time.frameCount && !ShiftHeld())
                {
                    map.UnselectAll();
                    Echo("SELECTION CLEARED  /  RIGHT CLICK FOR MAP ACTIONS");
                }
            }
            if (!boxPress) return;
            if (!ControlHeld() || BoxBlocked() || MapUiPointer.OverControls() ||
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
                        Mathf.Abs(current.y - boxStart.y)), AddHeld());
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
                Vector2 screen = IconScreen(icon);
                if (!region.Contains(screen) ||
                    !MapUiPointer.Contains(map.mapBackground.rectTransform, screen)) continue;
                if (candidates.Count < MaximumSelected) candidates.Add(icon.unit);
            }
            if (!add) map.UnselectAll();
            for (int i = 0; i < candidates.Count; i++) map.SelectIcon(candidates[i]);
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
            actions.Clear();
            if (contact != null)
            {
                Unit unit = contact.unit;
                actions.Add(new MenuAction { Label = "FRAME THIS CONTACT", Invoke = () => Focus(ground, true) });
                actions.Add(new MenuAction { Label = "SELECT THIS TRACK", Invoke = () => SelectTrack(map, unit, false) });
                actions.Add(new MenuAction { Label = "ADD / REMOVE TRACK", Invoke = () => SelectTrack(map, unit, true) });
                if (nearby > 1)
                    actions.Add(new MenuAction { Label = "SELECT STACK  /  " + nearby,
                        Invoke = () => SelectNearby(map, click) });
                actions.Add(new MenuAction { Label = "SELECT MATCHING TYPE", Invoke = () => SelectVisibleType(map, unit) });
            }
            else if (airbase != null)
            {
                AirbaseMapIcon target = airbase;
                actions.Add(new MenuAction { Label = "FRAME AIRBASE", Invoke = () => Focus(ground, true) });
                actions.Add(new MenuAction { Label = "SELECT AIRBASE", Invoke = () =>
                {
                    map.UnselectAll();
                    if (target != null && target.airbase != null) map.SelectIcon(target.airbase);
                    HideMenu();
                }});
            }
            else
            {
                actions.Add(new MenuAction { Label = "CENTER VIEW HERE", Invoke = () => Focus(ground, false) });
                actions.Add(new MenuAction { Label = "ZOOM TO THIS AREA", Invoke = () => Focus(ground, true) });
                if (nearby > 1)
                    actions.Add(new MenuAction { Label = "SELECT NEARBY  /  " + nearby,
                        Invoke = () => SelectNearby(map, click) });
            }
            actions.Add(new MenuAction { Label = "COPY GROUND FIX", Invoke = () =>
            {
                GUIUtility.systemCopyBuffer = "X " + AvNum.Fixed(ground.x, 0) + "  Z " + AvNum.Fixed(ground.z, 0);
                HideMenu();
                Echo("GROUND FIX COPIED");
            }});
            if (map.selectedIcons != null && map.selectedIcons.Count > 0)
                actions.Add(new MenuAction { Label = "CLEAR SELECTION", Invoke = () =>
                {
                    map.UnselectAll();
                    HideMenu();
                    Echo("SELECTION CLEARED");
                }});
            float height = 45f + actions.Count * 31f;
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
            string title = contact != null ? FirstLine(contact.GetInfoText()) :
                airbase != null ? FirstLine(airbase.GetInfoText()) : "MAP / TERRAIN";
            // Rects are top-anchored with y already assigned directly: rows go down with negative y.
            TMP_Text titleText = AvText.Make(menu, "Title", AvTextRole.Head, title, TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(titleText.rectTransform, new Rect(9f, -4f, MenuWidth - 18f, 18f));
            titleText.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            AvText.Fit(titleText, false);
            TMP_Text fixText = AvText.Make(menu, "Fix",  AvTextRole.Micro,
                "X " + AvNum.Fixed(ground.x / 1000f, 1) + "  Z " + AvNum.Fixed(ground.z / 1000f, 1) + " KM  /  " +
                AvNum.Fixed(map.selectedIcons?.Count ?? 0, 0) + " SELECTED", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(fixText.rectTransform, new Rect(9f, -22f, MenuWidth - 18f, 18f));
            fixText.color = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            AvText.Fit(fixText, false);
            for (int i = 0; i < actions.Count; i++)
            {
                MenuAction action = actions[i];
                AvControl button = AvControl.Make(menu, new AvControl.Spec(action.Label, action.Invoke));
                MfdChromeLay.Place(button.Rect, new Rect(8f, -(42f + i * 31f), MenuWidth - 16f, 28f));
            }
            menu.SetAsLastSibling();
        }

        private static void Focus(GlobalPosition ground, bool zoom)
        {
            ReliefNavigator.Focus(ground, zoom);
            HideMenu();
            Echo(zoom ? "AREA FRAMED" : "VIEW CENTERED");
        }

        private static void SelectTrack(DynamicMap map, Unit unit, bool add)
        {
            HideMenu();
            if (map == null || unit == null || unit.disabled || Excluded(unit)) return;
            if (GameManager.GetLocalAircraft(out Aircraft localAircraft) && unit == localAircraft) return;
            if (add && TrySelectedUnit(map, unit)) map.DeselectIcon(unit);
            else
            {
                if (!add) map.UnselectAll();
                map.SelectIcon(unit);
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
            map.UnselectAll();
            for (int i = 0; i < candidates.Count; i++) map.SelectIcon(candidates[i]);
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
            map.UnselectAll();
            for (int i = 0; i < candidates.Count; i++) map.SelectIcon(candidates[i]);
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

        private static bool ControlHeld() => Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);
        private static bool AddHeld() => ShiftHeld() || Input.GetKey(KeyCode.LeftAlt) ||
            Input.GetKey(KeyCode.RightAlt);
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

        internal static void Restore()
        {
            CancelBox();
            HideMenu();
            rightPress = false;
            leftPress = false;
            boxReleaseFrame = -1;
            iconClickFrame = -1;
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
