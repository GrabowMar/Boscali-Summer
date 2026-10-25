#if !PUBLIC_RELEASE
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using UnityEngine;
using UnityEngine.EventSystems;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Hook for the unattended smooth-scroll check: <c>Wheel {notches}</c> picks the open kit scroller with the most travel, raycasts
    /// at its viewport centre like the input module does, sends the wheel to whatever owns the hit and samples the content position
    /// for 30 frames; <c>Read</c> returns those samples. Dev tooling.
    /// </summary>
    public static class ScrollAutomation
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
                    float overflow = ShowLongestPage();
                    if (overflow > 0f) return new Dictionary<string, object> { ["ok"] = true, ["code"] = code, ["overflow"] = overflow };
                }
            return Failure("ScrollAutomation", "Open", "no screen in the list has an overflowing page");
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
