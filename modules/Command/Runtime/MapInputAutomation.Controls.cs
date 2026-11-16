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
                if (!control.isActiveAndEnabled || !control.Interactable || !string.Equals(control.Label, label, StringComparison.OrdinalIgnoreCase)) continue;
                if (Bool(args, "reveal")) RevealControl(control.Rect);
                Rect bounds = ScreenBounds(control.Rect);
                if (!WithinScreen(bounds)) return Failure("MapInputAutomation", "controls-press", "control is outside the screen: " + label);
                foreach (ScrollRect scroll in control.GetComponentsInParent<ScrollRect>())
                    if (scroll.viewport != null && !MapUiPointer.Contains(scroll.viewport, bounds.center))
                        return Failure("MapInputAutomation", "controls-press", "control is clipped; pass reveal=true: " + label);
                AvHit hit = control.GetComponentInChildren<AvHit>();
                if (hit == null) return Failure("MapInputAutomation", "controls-press", "control has no AvHit: " + label);
                bool before = control.Latched;
                MapOptions options = NativeMapOptions(map);
                bool gridBefore = options != null && options.showGridLabels;
                var data = WindowPointer.Data(); data.position = bounds.center;
                hit.OnPointerDown(data); hit.OnPointerUp(data); hit.OnPointerClick(data);
                Dictionary<string, object> state = State(map);
                state["ok"] = true; state["label"] = label;
                state["latchedBefore"] = before; state["latchedAfter"] = control.Latched;
                state["latchedChanged"] = before != control.Latched;
                state["nativeGridBefore"] = gridBefore;
                state["nativeGridChanged"] = options != null && gridBefore != options.showGridLabels;
                state["nativeGridMatchesLatch"] = options != null && control.Latched == options.showGridLabels;
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
                foreach (Vector3 corner in corners)
                { float y = scroll.viewport.InverseTransformPoint(corner).y; low = Mathf.Min(low, y); high = Mathf.Max(high, y); }
                Rect view = scroll.viewport.rect;
                float delta = high > view.yMax ? view.yMax - high : low < view.yMin ? view.yMin - low : 0f;
                scroll.StopMovement();
                Vector2 position = scroll.content.anchoredPosition;
                position.y = Mathf.Clamp(position.y + delta, 0f, Mathf.Max(0f, scroll.content.rect.height - view.height));
                scroll.content.anchoredPosition = position;
                scroll.Rebuild(CanvasUpdate.PostLayout);
            }
        }

        private static MapOptions NativeMapOptions(DynamicMap map)
        {
            VirtualMFD mfd = MapMfdLookup.Resolve(map.maximizedMapCanvas);
            foreach (List<MFDScreen> screens in new[] { MapUiAccess.GetLeftScreens(mfd), MapUiAccess.GetRightScreens(mfd) })
                if (screens != null) foreach (MFDScreen screen in screens)
                    if (screen != null && screen.GetComponent<MapOptions>() is MapOptions options) return options;
            return null;
        }

        // These hooks dispatch the rendered button handlers. They do not emulate physical
        // Rewired mouse input; the pointer predicates are reported separately below.
        private static Dictionary<string, object> ControlsAction(DynamicMap map, Dictionary<string, object> args)
        {
            string action = Text(args, "do"), selected = SelectionKey(map), modes = SelectionModes();
            string pose = (string)Read(map)["pose"];
            MFDScreen left = MfdSinglePanelPatch.ActiveScreen;
            bool columnFolded = MfdColumnFold.Folded;
            if (action == "controls-key" || action == "controls-native-key")
            {
                bool found = false;
                foreach (MfdRailKey key in MfdRail.Keys)
                    if (key.Entry.Code == "MAP" && key.Source != null)
                    {
                        if (action == "controls-native-key")
                        { key.Source.onClick.Invoke(); found = true; break; }
                        AvHit railHit = key.Rect.GetComponentInChildren<AvHit>();
                        if (railHit == null) break;
                        var pointer = WindowPointer.Data();
                        railHit.OnPointerDown(pointer); railHit.OnPointerUp(pointer); railHit.OnPointerClick(pointer);
                        found = true; break;
                    }
                if (!found) return Failure("MapInputAutomation", action, "MAP rail key is unavailable");
            }
            else
            {
                int tab = Mathf.RoundToInt(Number(args, "tab", 0));
                if (action == "controls-selecttab" && (tab < 0 || tab > 2))
                    return Failure("MapInputAutomation", action, "tab must be 0 VIEW, 1 LAYERS or 2 PLOT");
                string name = action == "controls-fold" ? "Collapse" : action == "controls-cancel" ? "CANCEL" : new[] { "VIEW", "LAYERS", "PLOT" }[tab];
                AvControl control = null;
                if (MfdMapControls.Root != null)
                    foreach (AvControl item in MfdMapControls.Root.GetComponentsInChildren<AvControl>())
                        if (item.isActiveAndEnabled && item.Interactable &&
                            (item.name == name || string.Equals(item.Label, name, StringComparison.OrdinalIgnoreCase)))
                        { control = item; break; }
                AvHit hit = control != null ? control.GetComponentInChildren<AvHit>() : null;
                if (hit == null) return Failure("MapInputAutomation", action, "no active AvHit control " + name);
                var data = WindowPointer.Data();
                hit.OnPointerDown(data); hit.OnPointerUp(data); hit.OnPointerClick(data);
            }
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
            state["controlsTab"] = MfdMapControls.SelectedTab;
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
            state["controlsCancelActive"] = shown && root.Find("Cancel")?.gameObject.activeInHierarchy == true;
            state["pickerBusy"] = MapPicker.IsBusy;
            state["commsPlotArmed"] = MapPicker.IsOwner(Modules.Comms.Runtime.CommsManager.PickerOwner);
            MapOptions nativeOptions = NativeMapOptions(map);
            state["nativeGridAvailable"] = nativeOptions != null;
            state["nativeGridShown"] = nativeOptions != null && nativeOptions.showGridLabels;
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
