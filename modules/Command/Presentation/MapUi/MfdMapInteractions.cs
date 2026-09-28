using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
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
        private const float MenuWidth = 240f;

        private static readonly List<Unit> candidates = new List<Unit>(MaximumSelected);
        private static readonly List<Unit> friendlies = new List<Unit>(MaximumSelected);
        private static readonly List<MenuAction> actions = new List<MenuAction>(5);
        private struct MenuAction { internal string Label; internal Action Invoke; }

        private static RectTransform box;
        private static RectTransform menu;
        private static bool boxPress;
        private static bool boxDragged;
        private static Vector2 boxStart;
        private static int boxReleaseFrame = -1;
        private static bool rightPress;
        private static Vector2 rightStart;
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
            }
            if (Input.GetKeyDown(KeyCode.Escape))
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
                if (menu != null && !ContainsMenu(Input.mousePosition)) HideMenu();
                boxPress = ControlHeld() && !BoxBlocked() && !MapUiPointer.OverControls() &&
                    !Typing() && map.TryGetCursorCoordinates(out _);
                boxDragged = false;
                if (boxPress) boxStart = Input.mousePosition;
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
            friendlies.Clear();
            if (map.mapIcons == null) return;
            GameManager.GetLocalAircraft(out Aircraft localAircraft);
            int limit = Mathf.Min(map.mapIcons.Count, MaximumScannedIcons);
            for (int i = 0; i < limit; i++)
            {
                if (!(map.mapIcons[i] is UnitMapIcon icon) || icon.unit == null ||
                    icon.unit == localAircraft || icon.unit.disabled ||
                    !icon.gameObject.activeInHierarchy || icon.iconImage == null ||
                    !icon.iconImage.gameObject.activeInHierarchy || Excluded(icon.unit)) continue;
                Vector2 screen = IconScreen(icon);
                if (!region.Contains(screen) ||
                    !MapUiPointer.Contains(map.mapBackground.rectTransform, screen)) continue;
                if (candidates.Count < MaximumSelected) candidates.Add(icon.unit);
                if (DynamicMap.GetFactionMode(icon.unit.NetworkHQ) == FactionMode.Friendly &&
                    friendlies.Count < MaximumSelected) friendlies.Add(icon.unit);
            }
            // As in RTS Commander, mixed rectangles resolve to friendlies first.
            List<Unit> chosen = friendlies.Count > 0 ? friendlies : candidates;
            if (!add) map.UnselectAll();
            for (int i = 0; i < chosen.Count; i++) map.SelectIcon(chosen[i]);
            Echo(chosen.Count > 0 ? $"{chosen.Count} TRACKS SELECTED" : "SELECTION CLEARED");
        }

        private static void ShowMenu(DynamicMap map, GlobalPosition ground)
        {
            HideMenu();
            Canvas canvas = map.maximizedMapCanvas;
            RectTransform parent = canvas.transform as RectTransform;
            if (parent == null) return;
            UnitMapIcon contact = ContactAt(map, Input.mousePosition);
            actions.Clear();
            if (contact != null)
            {
                Unit unit = contact.unit;
                actions.Add(new MenuAction { Label = "SELECT TRACK", Invoke = () => SelectTrack(map, unit, false) });
                actions.Add(new MenuAction { Label = "ADD TO SELECTION", Invoke = () => SelectTrack(map, unit, true) });
                actions.Add(new MenuAction { Label = "SELECT VISIBLE TYPE", Invoke = () => SelectVisibleType(map, unit) });
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

            float height = 50f + actions.Count * 44f;
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
            string title = contact != null ? FirstLine(contact.GetInfoText()) : "TERRAIN FIX";
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
                MfdChromeLay.Place(button.Rect, new Rect(8f, -(45f + i * 44f), MenuWidth - 16f, 40f));
            }
            menu.SetAsLastSibling();
        }

        private static void SelectTrack(DynamicMap map, Unit unit, bool add)
        {
            HideMenu();
            if (map == null || unit == null || unit.disabled || Excluded(unit)) return;
            if (GameManager.GetLocalAircraft(out Aircraft localAircraft) && unit == localAircraft) return;
            if (!add) map.UnselectAll();
            map.SelectIcon(unit);
            Echo($"{map.selectedIcons?.Count ?? 0} TRACKS SELECTED");
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
                    icon.iconImage == null || !icon.iconImage.enabled || Excluded(icon.unit)) continue;
                if (!MapUiPointer.Contains(map.mapBackground.rectTransform, IconScreen(icon))) continue;
                if (candidates.Count < MaximumSelected) candidates.Add(icon.unit);
            }
            map.UnselectAll();
            for (int i = 0; i < candidates.Count; i++) map.SelectIcon(candidates[i]);
            Echo($"{candidates.Count} TRACKS SELECTED");
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
                    !icon.iconImage.enabled || Excluded(icon.unit)) continue;
                float distance = (IconScreen(icon) - screen).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = icon;
            }
            return nearest;
        }

        private static Vector2 IconScreen(UnitMapIcon icon)
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
        private static bool AddHeld() => Input.GetKey(KeyCode.LeftAlt) ||
            Input.GetKey(KeyCode.RightAlt);
        private static bool BoxBlocked() => MapPicker.IsBusy ||
            (ModServices.TryGet(out IMapBoxInput input) && input.BlocksBoxSelection);
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
            boxReleaseFrame = -1;
            candidates.Clear();
            friendlies.Clear();
            actions.Clear();
            feedback = null;
            feedbackUntil = 0f;
        }
    }
}
