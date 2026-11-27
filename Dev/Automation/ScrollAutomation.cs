#if !PUBLIC_RELEASE
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Modules.Wing.Presentation;
using NOAvionics;
using UnityEngine;
using UnityEngine.EventSystems;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

// Preserve the hook name used by nomodkit scenarios; this driver spans multiple modules.
namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Hook for the unattended smooth-scroll check: <c>Wheel {notches}</c> picks the open kit scroller with the most travel, raycasts
    /// at its viewport centre like the input module does, sends the wheel to whatever owns the hit and samples the content position
    /// for 30 frames; <c>Read</c> returns those samples. Dev tooling.
    /// </summary>
    public static partial class ScrollAutomation
    {
        private static readonly List<float> samples = new List<float>();
        private static string hit = "";

        /// <summary>
        /// Maximises the map and presses the rail keys named in <c>codes</c> (comma list, the click path) in turn until one shows a
        /// console page taller than its viewport; that page stays up.
        /// </summary>
        public static Dictionary<string, object> Open(Dictionary<string, object> args)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            foreach (string code in (Text(args, "codes") ?? "").Split(','))
                foreach (MfdRailKey key in MfdRail.Keys)
                {
                    if (key.Entry.Code != code || key.Screen == null) continue;
                    if (!key.Screen.isActive) key.Source.onClick.Invoke();
                    // "page": show that page of every open console (SET COMPAT is client page 6) instead of the longest.
                    int page = (int)Number(args, "page", -1);
                    if (page >= 0) return new Dictionary<string, object> { ["ok"] = true, ["code"] = code, ["shown"] = ShowPage(page) };
                    float overflow = ShowLongestPage();
                    if (overflow > 0f) return new Dictionary<string, object> { ["ok"] = true, ["code"] = code, ["overflow"] = overflow };
                }
            return Failure("ScrollAutomation", "Open", "no screen in the list has an overflowing page");
        }

        /// <summary>Presses a rendered, enabled kit button. Optional code scopes a console; nth disambiguates repeated navigation labels.</summary>
        public static Dictionary<string, object> Press(Dictionary<string, object> args)
        {
            string label = Text(args, "label") ?? "";
            string code = Text(args, "code");
            AvConsole scope = string.IsNullOrEmpty(code) ? null : VisibleConsole(code);
            if (!string.IsNullOrEmpty(code) && scope == null) return Failure("ScrollAutomation", "Press", "no visible console " + code);
            int nth = Mathf.Max(0, (int)Number(args, "nth", 0f)), seen = 0;
            AvControl[] controls = scope != null ? scope.Root.GetComponentsInChildren<AvControl>() : Object.FindObjectsOfType<AvControl>();
            foreach (AvControl c in controls)
            {
                if (!c.isActiveAndEnabled || !c.Interactable || !Rendered(c.transform) ||
                    !string.Equals(c.Label, label, System.StringComparison.OrdinalIgnoreCase) || seen++ != nth) continue;
                AvHit target = c.GetComponentInChildren<AvHit>();
                if (target == null) return Failure("ScrollAutomation", "Press", "shown button has no AvHit handler: " + c.name);
                // Native Rewired UI can have no EventSystem.current. Match the real window input path;
                // PointerEventData accepts that null context and the same AvHit still owns refusal/click semantics.
                var data = BoscaliSummer.Core.Ui.WindowPointer.Data();
                target.OnPointerDown(data); target.OnPointerUp(data); target.OnPointerClick(data);
                return new Dictionary<string, object> { ["ok"] = true, ["label"] = c.Label, ["latched"] = c.Latched };
            }
            return Failure("ScrollAutomation", "Press", "no shown button labelled " + label);
        }

        /// <summary>Reads the actually visible console, including SET's client/server instances with the same ID.</summary>
        public static Dictionary<string, object> ReadView(Dictionary<string, object> args)
        {
            string code = Text(args, "code") ?? "";
            AvConsole c = VisibleConsole(code);
            if (c == null) return Failure("ScrollAutomation", "ReadView", "no visible console " + code);
            return View(c);
        }

        /// <summary>Requests a window size for a rendered sandbox scenario after the game has applied its startup settings.</summary>
        public static Dictionary<string, object> SetResolution(Dictionary<string, object> args)
        {
            float width = Number(args, "width", 0f), height = Number(args, "height", 0f);
            int limit = SystemInfo.maxTextureSize;
            if (!(width >= 1f && width <= limit && width == Mathf.Floor(width) &&
                  height >= 1f && height <= limit && height == Mathf.Floor(height)))
                return Failure("ScrollAutomation", "SetResolution", "Width and height must be positive whole pixels within the device texture limit.");
            if (Application.isBatchMode) return Failure("ScrollAutomation", "SetResolution", "A rendered window is required.");

            Screen.SetResolution((int)width, (int)height, FullScreenMode.Windowed);
            // Unity applies the request after the frame. The scenario waits for Screen.width and Screen.height.
            return new Dictionary<string, object> { ["ok"] = true };
        }

        /// <summary>Positions a console for repeatable screenshots; input routing is tested separately by Wheel.</summary>
        public static Dictionary<string, object> Position(Dictionary<string, object> args)
        {
            string code = Text(args, "code") ?? "";
            AvConsole c = VisibleConsole(code);
            if (c == null) return Failure("ScrollAutomation", "Position", "no visible console " + code);
            var view = Field(c, "scrollView") as AvScrollView;
            if (view == null) return Failure("ScrollAutomation", "Position", "no console scroll view");
            view.Scroll.StopMovement();
            view.Scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(Number(args, "fraction", 0f));
            return View(c);
        }

        /// <summary>Read-only rail/page inventory. Hidden tabs are reported, never forced open.</summary>
        public static Dictionary<string, object> Inventory(Dictionary<string, object> args)
        {
            var keys = new List<object>();
            foreach (MfdRailKey key in MfdRail.Keys)
                keys.Add(new Dictionary<string, object> { ["code"] = key.Entry.Code, ["active"] = key.Screen != null && key.Screen.isActive });
            var consoles = new List<object>();
            foreach (AvConsole c in Consoles()) consoles.Add(View(c));
            return new Dictionary<string, object> { ["ok"] = true, ["keys"] = keys, ["consoles"] = consoles };
        }

        private static object Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);

        private static HashSet<AvConsole> Consoles()
        {
            var result = new HashSet<AvConsole>();
            var byId = (IDictionary)typeof(AvConsole).GetField("byId", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (AvConsole c in byId.Values) if (c.Root != null) result.Add(c);
            var all = (IList)typeof(AvConsole).GetField("all", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (AvConsole c in all) if (c.Root != null) result.Add(c);
            return result;
        }

        private static AvConsole VisibleConsole(string code)
        {
            foreach (AvConsole c in Consoles())
                if ((string)Field(c, "id") == code && Rendered(c.Root)) return c;
            return null;
        }

        private static bool Rendered(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return false;
            for (Transform t = target; t != null; t = t.parent)
            {
                Canvas canvas = t.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled) return false;
            }
            return true;
        }

        private static Dictionary<string, object> View(AvConsole c)
        {
            var canvases = (Canvas[])Field(c, "pageCanvases");
            var names = Field(c, "pageNames") as string[];
            var bar = Field(c, "tabs") as AvTabBar;
            var hidden = bar != null ? Field(bar, "hidden") as bool[] : null;
            var pages = new List<object>();
            int visiblePages = 0;
            for (int p = 0; p < c.PageCount; p++)
            {
                if (canvases[p] != null && canvases[p].isActiveAndEnabled) visiblePages++;
                pages.Add(new Dictionary<string, object> { ["index"] = p,
                    ["name"] = names != null && p < names.Length ? names[p] : "",
                    ["tabShown"] = hidden == null || !hidden[p] });
            }
            int current = c.CurrentPage;
            AvFlow flow = c.Page(current);
            var view = Field(c, "scrollView") as AvScrollView;
            bool scrollbar = view != null && view.Bar.gameObject.activeInHierarchy;
            Rect bounds = ScreenBounds(c.Root);
            MFDScreen screen = c.Root.GetComponentInParent<MFDScreen>();
            RectTransform dock = screen != null && MfdPanelDock.IsDocked(screen) ? screen.transform.parent.parent as RectTransform : null;
            RectTransform wire = typeof(MfdNewsTicker).GetField("root", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as RectTransform;
            GameObject mapFrame = typeof(MfdRailPatch).GetField("mapFrame", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as GameObject;
            GameObject mapTray = typeof(MfdMapDeck).GetField("tray", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as GameObject;
            var result = new Dictionary<string, object>
            {
                ["ok"] = true, ["code"] = (string)Field(c, "id"), ["rootVisible"] = Rendered(c.Root),
                ["page"] = current, ["pageName"] = c.CurrentPageName ?? "", ["pageCount"] = c.PageCount,
                ["pageVisible"] = current >= 0 && canvases[current] != null && canvases[current].isActiveAndEnabled,
                ["visiblePageCount"] = visiblePages, ["pages"] = pages,
                ["contentHeight"] = flow.ContentHeight, ["viewportHeight"] = flow.ViewportHeight,
                ["overflow"] = Mathf.Max(0f, flow.ContentHeight - flow.ViewportHeight), ["scrollY"] = flow.Content.anchoredPosition.y,
                ["scrollbarVisible"] = scrollbar,
                ["scrollbarCorrect"] = scrollbar == (flow.ContentHeight > flow.ViewportHeight + .01f),
                ["rootX"] = bounds.x, ["rootY"] = bounds.y, ["rootWidth"] = bounds.width, ["rootHeight"] = bounds.height,
                ["rootOnScreen"] = Within(bounds, new Rect(0f, 0f, Screen.width, Screen.height)),
                ["rootWithinDock"] = dock != null && Within(bounds, ScreenBounds(dock)),
                ["tickerVisible"] = wire != null && Rendered(wire) && Within(ScreenBounds(wire), new Rect(0f, 0f, Screen.width, Screen.height)),
                ["mapFrameVisible"] = mapFrame != null && Rendered(mapFrame.transform),
                ["mapTrayVisible"] = mapTray != null && Rendered(mapTray.transform),
                ["panelAlpha"] = c.SurfaceOpacity,
            };
            if ((string)Field(c, "id") == "SET" && c.CurrentPageName == "MAP")
            {
                var words = new System.Text.StringBuilder();
                foreach (TMPro.TMP_Text text in flow.Content.GetComponentsInChildren<TMPro.TMP_Text>(false)) words.Append(text.text).Append('|');
                string pageText = words.ToString();
                result["hasMfdOpacity"] = pageText.Contains("MFD OPACITY");
                int retired = 0;
                foreach (string label in new[] { "EXPANDED LAYOUT", "TERRAIN STRENGTH", "CONSOLE OPACITY", "MAP DARKENING", "TICKER SPEED" })
                    if (pageText.Contains(label)) retired++;
                result["retiredMapControls"] = retired;
            }
            if ((string)Field(c, "id") == "PILOT" && ModuleServices.TryGet(out IWingSquad host))
            {
                object squadron = Field(host, "squadron");
                var nested = squadron != null ? Field(squadron, "subPages") as WmcSubPages : null;
                if (nested != null && nested.Current >= 0 && nested.Flow(nested.Current).Content.IsChildOf(c.Root))
                {
                    result["squadronPage"] = nested.Current;
                    result["squadronVisible"] = Rendered(nested.Flow(nested.Current).Content);
                    int shown = 0;
                    foreach (RectTransform root in (RectTransform[])Field(nested, "rects")) if (Rendered(root)) shown++;
                    result["squadronVisiblePages"] = shown;
                }
            }
            return result;
        }

        private static bool Within(Rect inner, Rect outer) => inner.width > 1f && inner.height > 1f &&
            inner.xMin >= outer.xMin - 1f && inner.yMin >= outer.yMin - 1f &&
            inner.xMax <= outer.xMax + 1f && inner.yMax <= outer.yMax + 1f;

        private static Rect ScreenBounds(RectTransform target)
        {
            var corners = new Vector3[4]; target.GetWorldCorners(corners);
            Camera camera = MapUiPointer.UiCamera(target.GetComponentInParent<Canvas>());
            Vector2 first = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 min = first, max = first;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public static Dictionary<string, object> Wheel(Dictionary<string, object> args)
        {
            AvSmoothScroll best = null; float travel = 0f;
            foreach (AvSmoothScroll s in Object.FindObjectsOfType<AvSmoothScroll>())
            {
                if (s.content == null || s.viewport == null) continue;
                float t = s.content.rect.height - s.viewport.rect.height;
                if (t > travel) { travel = t; best = s; }
            }
            if (best == null) return Failure("ScrollAutomation", "Wheel", "no open scroller with travel");
            EventSystem es = EventSystem.current != null ? EventSystem.current : Object.FindObjectOfType<EventSystem>();
            if (es == null) return Failure("ScrollAutomation", "Wheel", "no EventSystem");

            Canvas canvas = best.GetComponentInParent<Canvas>().rootCanvas;
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4]; best.viewport.GetWorldCorners(corners);
            Vector2 at = RectTransformUtility.WorldToScreenPoint(cam, (corners[0] + corners[2]) * 0.5f);
            var data = new PointerEventData(es) { position = at, scrollDelta = new Vector2(0f, Number(args, "notches", -1f)) };
            var hits = new List<RaycastResult>(); es.RaycastAll(data, hits);
            GameObject target = hits.Count > 0 ? hits[0].gameObject : null;
            hit = target == null ? "" : target.name;
            GameObject handler = target == null ? null : ExecuteEvents.GetEventHandler<IScrollHandler>(target);

            samples.Clear();
            samples.Add(best.content.anchoredPosition.y);
            if (handler != null) ExecuteEvents.Execute(handler, data, ExecuteEvents.scrollHandler);
            best.StartCoroutine(Sample(best));
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["travel"] = travel, ["hit"] = hit,
                ["handlerIsScroller"] = handler == best.gameObject, ["start"] = samples[0],
            };
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            int n = samples.Count;
            int moving = 0; bool monotonic = true;
            for (int i = 1; i < n; i++)
            {
                float d = samples[i] - samples[i - 1];
                if (Mathf.Abs(d) > 0.01f) moving++;
                if (i > 1 && d * (samples[1] - samples[0]) < -0.01f) monotonic = false;
            }
            return new Dictionary<string, object>
            {
                ["ok"] = n > 1, ["hit"] = hit, ["frames"] = n,
                ["first"] = n > 1 ? samples[1] - samples[0] : 0f,
                ["moved"] = n > 0 ? samples[n - 1] - samples[0] : 0f,
                ["movingFrames"] = moving, ["monotonic"] = monotonic,
                ["samples"] = string.Join(",", samples.ConvertAll(v => v.ToString("0.0"))),
            };
        }

        private static int ShowPage(int page)
        {
            var all = (IList)typeof(AvConsole).GetField("all", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            int shown = 0;
            foreach (AvConsole c in all)
                if (c.Root != null && c.Root.gameObject.activeInHierarchy && page < c.PageCount) { c.SetPage(page); shown++; }
            return shown;
        }

        // Shows the page that overflows its viewport most on every open console; returns that overflow in px.
        private static float ShowLongestPage()
        {
            var all = (IList)typeof(AvConsole).GetField("all", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            float most = 0f;
            foreach (AvConsole c in all)
            {
                if (c.Root == null || !c.Root.gameObject.activeInHierarchy) continue;
                int pick = -1;
                for (int p = 0; p < c.PageCount; p++)
                {
                    float over = c.Page(p).ContentHeight - c.Page(p).ViewportHeight;
                    if (over > most) { most = over; pick = p; }
                }
                if (pick >= 0) c.SetPage(pick);
            }
            return most;
        }

        private static IEnumerator Sample(AvSmoothScroll scroll)
        {
            for (int i = 0; i < 30 && scroll != null && scroll.content != null; i++)
            {
                yield return null;
                samples.Add(scroll.content.anchoredPosition.y);
            }
        }
    }
}
#endif
