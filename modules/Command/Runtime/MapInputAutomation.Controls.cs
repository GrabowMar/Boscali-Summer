#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    public static partial class MapInputAutomation
    {
        private static Dictionary<string, object> PressDrawerControl(DynamicMap map, Dictionary<string, object> args)
        {
            string label = Text(args, "label") ?? "";
            if (!MfdMapControls.Shown || MfdMapControls.Folded)
                return Failure("MapInputAutomation", "controls-press", "drawer content is hidden");
            foreach (AvControl control in MfdMapControls.Root.GetComponentsInChildren<AvControl>())
            {
                if (!control.isActiveAndEnabled || !control.Interactable ||
                    !(string.Equals(control.Label, label, StringComparison.OrdinalIgnoreCase) || string.Equals(control.name, "Control " + label, StringComparison.OrdinalIgnoreCase))) continue;
                if (Bool(args, "reveal")) RevealControl(control.Rect);
                Rect bounds = ScreenBounds(control.Rect);
                if (!WithinScreen(bounds)) return Failure("MapInputAutomation", "controls-press", "control is outside the screen: " + label);
                bool fullyVisible = true;
                foreach (ScrollRect scroll in control.GetComponentsInParent<ScrollRect>())
                {
                    if (scroll.viewport != null && !MapUiPointer.Contains(scroll.viewport, bounds.center))
                        return Failure("MapInputAutomation", "controls-press", "control is clipped; pass reveal=true: " + label);
                    Rect viewport = ScreenBounds(scroll.viewport);
                    fullyVisible &= bounds.xMin >= viewport.xMin - 1f && bounds.xMax <= viewport.xMax + 1f &&
                        bounds.yMin >= viewport.yMin - 1f && bounds.yMax <= viewport.yMax + 1f;
                }
                AvHit hit = control.GetComponentInChildren<AvHit>();
                if (hit == null) return Failure("MapInputAutomation", "controls-press", "control has no AvHit: " + label);
                bool before = control.Latched;
                var data = WindowPointer.Data(); data.position = bounds.center;
                hit.OnPointerDown(data); hit.OnPointerUp(data); hit.OnPointerClick(data);
                Dictionary<string, object> state = State(map);
                state["ok"] = true; state["label"] = label;
                state["latchedBefore"] = before; state["latchedAfter"] = control.Latched;
                state["latchedChanged"] = before != control.Latched;
                state["controlFullyVisible"] = fullyVisible;
                return state;
            }
            return Failure("MapInputAutomation", "controls-press", "no active enabled control " + label);
        }

        private static void RevealControl(RectTransform control)
        {
            foreach (ScrollRect scroll in control.GetComponentsInParent<ScrollRect>())
            {
                if (scroll.content == null || scroll.viewport == null || !control.IsChildOf(scroll.content)) continue;
                var corners = new Vector3[4]; control.GetWorldCorners(corners);
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                float left = float.PositiveInfinity, right = float.NegativeInfinity;
                foreach (Vector3 corner in corners)
                {
                    Vector3 point = scroll.viewport.InverseTransformPoint(corner);
                    low = Mathf.Min(low, point.y); high = Mathf.Max(high, point.y);
                    left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x);
                }
                Rect view = scroll.viewport.rect;
                float delta = high > view.yMax ? view.yMax - high : low < view.yMin ? view.yMin - low : 0f;
                scroll.StopMovement();
                Vector2 position = scroll.content.anchoredPosition;
                if (scroll.vertical) position.y = Mathf.Clamp(position.y + delta, 0f, Mathf.Max(0f, scroll.content.rect.height - view.height));
                if (scroll.horizontal)
                {
                    float dx = right > view.xMax ? view.xMax - right : left < view.xMin ? view.xMin - left : 0f;
                    position.x = Mathf.Clamp(position.x + dx, Mathf.Min(0f, view.width - scroll.content.rect.width), 0f);
                }
                scroll.content.anchoredPosition = position;
                scroll.Rebuild(CanvasUpdate.PostLayout);
            }
        }

        private static Dictionary<string, object> PressPanelKey(DynamicMap map, Dictionary<string, object> args)
        {
            string code = Text(args, "code") ?? "MAP";
            foreach (MfdRailKey key in MfdRail.Keys)
            {
                if (key.Entry.Code != code || key.Source == null || !key.Source.interactable) continue;
                if (Bool(args, "native")) key.Source.onClick.Invoke();
                else key.Press();
                Dictionary<string, object> state = State(map);
                state["ok"] = true;
                state["panelCode"] = code;
                state["panelActive"] = key.Screen != null && key.Screen.isActive;
                return state;
            }
            return Failure("MapInputAutomation", "panel-key", "no available rail key " + code);
        }

        // These hooks dispatch the rendered button handlers. They do not emulate physical
        // Rewired mouse input; the pointer predicates are reported separately below.
        private static Dictionary<string, object> ControlsAction(DynamicMap map, Dictionary<string, object> args)
        {
            string action = Text(args, "do"), selected = SelectionKey(map), modes = SelectionModes();
            string pose = (string)Read(map)["pose"];
            MFDScreen left = MfdSinglePanelPatch.ActiveScreen;
            bool columnFolded = MfdColumnFold.Folded;
            AvControl control = MfdMapControls.Root != null ? MfdMapControls.Root.Find("Collapse")?.GetComponent<AvControl>() : null;
            AvHit hit = control != null && control.isActiveAndEnabled && control.Interactable ? control.GetComponentInChildren<AvHit>() : null;
            if (hit == null) return Failure("MapInputAutomation", action, "no active drawer fold control");
            var data = WindowPointer.Data();
            hit.OnPointerDown(data); hit.OnPointerUp(data); hit.OnPointerClick(data);
            Dictionary<string, object> state = State(map);
            state["ok"] = true;
            state["preservedSelection"] = selected == SelectionKey(map);
            state["preservedPose"] = pose == (string)state["pose"];
            state["preservedModes"] = modes == SelectionModes();
            state["preservedLeftPanel"] = left == MfdSinglePanelPatch.ActiveScreen && columnFolded == MfdColumnFold.Folded;
            return state;
        }

        private static string SelectionKey(DynamicMap map)
        {
            var ids = new List<int>();
            if (map.selectedIcons != null) foreach (var icon in map.selectedIcons) if (icon != null) ids.Add(icon.GetInstanceID());
            ids.Sort(); return string.Join(",", ids);
        }

        private static string SelectionModes()
        {
            var settings = Plugin.Settings?.Command;
            return settings?.MapClickAdds?.Value + "/" + settings?.MapLeftDragBox?.Value + "/" + settings?.MapBoxSide?.Value;
        }

        private static void ReadControls(DynamicMap map, Dictionary<string, object> state)
        {
            RectTransform root = MfdMapControls.Root;
            bool shown = MfdMapControls.Shown;
            state["controlsShown"] = shown;
            state["controlsFolded"] = MfdMapControls.Folded;
            var rows = new List<float>();
            if (shown && !MfdMapControls.Folded)
            {
                RectTransform feedback = MfdMapControls.Footer.Rect;
                rows.Add(root.InverseTransformPoint(feedback.TransformPoint(feedback.rect.center)).y);
                foreach (AvControl control in root.GetComponentsInChildren<AvControl>())
                {
                    if (!control.isActiveAndEnabled || control.name == "Collapse") continue;
                    float y = root.InverseTransformPoint(control.Rect.TransformPoint(control.Rect.rect.center)).y;
                    bool found = false;
                    foreach (float row in rows) if (Mathf.Abs(row - y) < 2f) { found = true; break; }
                    if (!found) rows.Add(y);
                }
            }
            state["controlsRows"] = rows.Count;
            state["controlsHeight"] = root != null ? root.rect.height : 0f;
            ScrollRect drawerScroll = root != null ? root.Find("Body/View")?.GetComponent<ScrollRect>() : null;
            state["controlsHorizontalScroll"] = drawerScroll != null && drawerScroll.horizontal && !drawerScroll.vertical;
            state["controlsScrollNeeded"] = drawerScroll != null && drawerScroll.content.rect.width > drawerScroll.viewport.rect.width + .5f;
            state["controlsScrollX"] = drawerScroll != null ? drawerScroll.content.anchoredPosition.x : 0f;
            if (drawerScroll != null)
            {
                float contentWidth = drawerScroll.content.rect.width, viewportWidth = drawerScroll.viewport.rect.width;
                float x = drawerScroll.content.anchoredPosition.x;
                state["controlsContentWidth"] = contentWidth;
                state["controlsViewportWidth"] = viewportWidth;
                state["controlsScrollGeometryCorrect"] = drawerScroll.horizontal && !drawerScroll.vertical &&
                    x <= .5f && x >= Mathf.Min(0f, viewportWidth - contentWidth) - .5f &&
                    (drawerScroll.horizontalScrollbar != null && drawerScroll.horizontalScrollbar.gameObject.activeInHierarchy) ==
                    (shown && !MfdMapControls.Folded && contentWidth > viewportWidth + .5f);
            }
            state["cameraFly"] = ReliefNavigator.Flying;
            state["controlsSelectionActive"] = MfdMapControls.SelectionSlot != null && MfdMapControls.SelectionSlot.gameObject.activeInHierarchy;
            state["controlsViewActive"] = MfdMapControls.ViewSlot != null && MfdMapControls.ViewSlot.gameObject.activeInHierarchy;
            state["controlsTheaterActive"] = MfdMapControls.TheaterSlot != null && MfdMapControls.TheaterSlot.gameObject.activeInHierarchy;
            int roots = 0;
            foreach (RectTransform rect in Resources.FindObjectsOfTypeAll<RectTransform>())
                if (rect.name == "NOAvionics.MapControls" && rect.gameObject.scene.IsValid()) roots++;
            state["controlsRootCount"] = roots;
            var prompt = root != null ? root.Find("Status")?.GetComponent<TMPro.TMP_Text>() : null;
            state["controlsPrompt"] = prompt != null ? prompt.text : "";
            state["controlsPromptVisible"] = shown && prompt != null && prompt.isActiveAndEnabled && !string.IsNullOrEmpty(MapPicker.Prompt) && prompt.text == MapPicker.Prompt;
            int extraControls = 0;
            if (root != null) foreach (AvControl control in root.GetComponentsInChildren<AvControl>(true))
                if (control.Label == "LAYERS" || control.Label == "PLOT" || control.Label == "VIEW" || control.Label == "CANCEL") extraControls++;
            state["controlsExtraMenuCount"] = extraControls;
            state["clickAdds"] = Plugin.Settings?.Command?.MapClickAdds?.Value == true;
            state["selectionIds"] = SelectionKey(map);
            state["selectionModes"] = SelectionModes();
            Rect controls = ScreenBounds(root);
            // DynamicMap's own masked rect is the viewport. The map image is zoomable
            // content and its background intentionally bleeds beyond the viewport.
            Rect viewport = ScreenBounds(map.GetComponent<RectTransform>());
            Canvas canvas = map.maximizedMapCanvas;
            Transform footerTransform = canvas != null ? canvas.transform.Find("NOAvionics.MapFooter") : null;
            Rect footer = ScreenBounds(footerTransform as RectTransform);
            state["controlsBounds"] = RectData(controls);
            state["mapBounds"] = RectData(viewport);
            state["footerBounds"] = RectData(footer);
            state["controlsOnScreen"] = shown && WithinScreen(controls);
            state["controlsBelowMap"] = shown && controls.yMax <= viewport.yMin + 1f;
            state["controlsAboveFooter"] = shown && footer.height > 0f && controls.yMin >= footer.yMax - 1f;
            state["controlsAlignedMap"] = shown && Mathf.Abs(controls.xMin - viewport.xMin) <= 1f && Mathf.Abs(controls.xMax - viewport.xMax) <= 1f;
            Vector2 header = new Vector2(controls.center.x, controls.yMax - Mathf.Min(12f, controls.height * .5f));
            state["controlsHeaderGuard"] = shown && MapUiPointer.OverControls(header);
            state["mapCentreUnblocked"] = DynamicMap.mapMaximized && viewport.height > 0f && !MapUiPointer.OverControls(viewport.center);
            // Just above the collapsed header is space returned to the map. A hidden body
            // must not remain registered here and swallow raw map drag/wheel input.
            Vector2 reclaimed = new Vector2(controls.center.x, controls.yMax + 24f);
            state["controlsReclaimedUnblocked"] = shown && MfdMapControls.Folded && viewport.Contains(reclaimed) && !MapUiPointer.OverControls(reclaimed);
        }

        private static Dictionary<string, object> RectData(Rect rect) => new Dictionary<string, object>
        { ["x"] = rect.x, ["y"] = rect.y, ["width"] = rect.width, ["height"] = rect.height };

        private static bool WithinScreen(Rect rect) => rect.width > 0f && rect.height > 0f && rect.xMin >= -1f && rect.yMin >= -1f && rect.xMax <= Screen.width + 1f && rect.yMax <= Screen.height + 1f;

        private static Rect ScreenBounds(RectTransform rect)
        {
            if (rect == null) return default;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Camera camera = MapUiPointer.UiCamera(rect.GetComponentInParent<Canvas>());
            Vector2 lo = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 hi = lo;
            for (int i = 1; i < 4; i++)
            { Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]); lo = Vector2.Min(lo, point); hi = Vector2.Max(hi, point); }
            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }
    }
}
#endif
