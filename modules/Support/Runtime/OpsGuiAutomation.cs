#if !PUBLIC_RELEASE
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using NOAvionics;
using TMPro;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Dev-build GUI driver for the in-game OPS window check (<c>tests/ingame/ops-gui.json</c>). It plays scripted pointer frames through
    /// <see cref="FrontPointer"/>, so a press walks the window's own hit test and a map click the room's own gesture code, exactly as a mouse would:
    /// <c>press</c> a control by its words, click a <c>map</c> point, <c>drag</c> (right button pan), <c>wheel</c>, <c>esc</c>; <c>audit</c> checks that every
    /// enabled control takes a click at its centre (nothing covers it) and sits on screen; <c>state</c> reads the window and the active room.
    /// </summary>
    public static class OpsGuiAutomation
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static Dictionary<string, object> Gui(Dictionary<string, object> args)
        {
            FrontController fronts = Find<FrontController>();
            OpsFrontWindow window = fronts != null ? Field(fronts, "window") as OpsFrontWindow : null;
            string what = Text(args, "do") ?? "state";
            if (what == "release") { FrontPointer.Release(); return State(fronts, window); }
            if (window == null || !fronts.IsOpen) return Failure("OpsGuiAutomation", "Gui", "the front window is not open");
            switch (what)
            {
                case "press": return Press(fronts, window, (Text(args, "label") ?? "").ToUpperInvariant(), (int)Number(args, "nth", 0f));
                case "map": return MapClick(fronts, window, Number(args, "u", 0.5f), Number(args, "v", 0.5f));
                case "drag": { Vector2 at = Aim(window, args); return Drag(fronts, window, at.x, at.y, Number(args, "du", 0.1f), Number(args, "dv", 0f), Mathf.Clamp((int)Number(args, "steps", 12f), 2, 600)); }
                case "wheel": { Vector2 at = Aim(window, args); return Wheel(fronts, window, at.x, at.y, Number(args, "delta", 1f)); }
                case "esc": FrontPointer.Play(FrontPointer.Position, 0, 0f, true, ClickGap); FrontPointer.Play(FrontPointer.Position, 0); return State(fronts, window);
                case "audit": return Audit(fronts, window);
                case "labels": return Labels(fronts, window);
                default: return State(fronts, window);
            }
        }

        // ---- Gestures --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Hover three frames, hold the button two, release two: a click the window's hit test and the rooms' press/release logic both accept.
        /// The first hover waits until <see cref="ClickGap"/> real seconds have passed since the last scripted frame, so on a slow machine two clicks never land
        /// inside the window's press debounce (FrontController.PressGap 0.35 s) and get dropped.
        /// </summary>
        private const float ClickGap = 0.45f;

        private static void Click(Vector2 at, int button = 1)
        {
            FrontPointer.Play(at, 0, 0f, false, ClickGap);
            for (int i = 0; i < 2; i++) FrontPointer.Play(at, 0);
            for (int i = 0; i < 2; i++) FrontPointer.Play(at, button);
            for (int i = 0; i < 2; i++) FrontPointer.Play(at, 0);
        }

        private static Dictionary<string, object> Press(FrontController fronts, OpsFrontWindow window, string label, int nth)
        {
            List<AvHit> hits = Hits(window);
            AvHit target = null;
            int seen = 0;
            string words = "";
            for (int pass = 0; pass < 2 && target == null; pass++) // exact words first, then containing
                foreach (AvHit h in hits)
                {
                    string w = Words(window, h);
                    if (pass == 0 ? w != label : !w.Contains(label)) continue;
                    if (seen++ < nth) continue;
                    target = h; words = w;
                    break;
                }
            Dictionary<string, object> r = State(fronts, window);
            r["found"] = target != null ? 1 : 0;
            if (target == null) { r["error"] = "no control reads '" + label + "'"; return r; }
            Vector2 at = Centre((RectTransform)target.transform);
            AvHit under = TopHit(hits, at);
            r["words"] = words;
            r["interactable"] = target.Interactable ? 1 : 0;
            r["covered"] = target.Interactable && under != target ? 1 : 0; // a disabled control takes no click, so nothing can cover it
            if (target.Interactable && under != null && under != target) r["coveredBy"] = Describe(window, under);
            r["x"] = Mathf.RoundToInt(at.x); r["y"] = Mathf.RoundToInt(at.y);
            if (target.Interactable) Click(at);
            r["pressed"] = target.Interactable ? 1 : 0;
            return r;
        }

        private static Dictionary<string, object> MapClick(FrontController fronts, OpsFrontWindow window, float u, float v)
        {
            Dictionary<string, object> r = State(fronts, window);
            if (!MapScreen(window, u, v, out Vector2 at)) { r["error"] = "the active room has no map plate"; return r; }
            Click(at);
            r["x"] = Mathf.RoundToInt(at.x); r["y"] = Mathf.RoundToInt(at.y);
            return r;
        }

        /// <summary>A right-button pan from (u, v) by (du, dv) map units over <paramref name="steps"/> frames (a long one keeps the map panning through a perf window).</summary>
        private static Dictionary<string, object> Drag(FrontController fronts, OpsFrontWindow window, float u, float v, float du, float dv, int steps)
        {
            Dictionary<string, object> r = State(fronts, window);
            if (!MapScreen(window, u, v, out Vector2 a) || !MapScreen(window, u + du, v + dv, out Vector2 b)) { r["error"] = "the active room has no map plate"; return r; }
            FrontPointer.Play(a, 0);
            for (int i = 0; i <= steps; i++) FrontPointer.Play(Vector2.Lerp(a, b, i / (float)steps), 2);
            FrontPointer.Play(b, 0);
            return r;
        }

        private static Dictionary<string, object> Wheel(FrontController fronts, OpsFrontWindow window, float u, float v, float delta)
        {
            Dictionary<string, object> r = State(fronts, window);
            if (!MapScreen(window, u, v, out Vector2 at)) { r["error"] = "the active room has no map plate"; return r; }
            FrontPointer.Play(at, 0);
            FrontPointer.Play(at, 0, delta);
            FrontPointer.Play(at, 0);
            return r;
        }

        // ---- Checks ----------------------------------------------------------------------------------------------------

        /// <summary>Every enabled control of the open room and the rail must take a click at its centre (the window's own topmost-first test) and sit on screen.</summary>
        private static Dictionary<string, object> Audit(FrontController fronts, OpsFrontWindow window)
        {
            List<AvHit> hits = Hits(window);
            int enabled = 0, covered = 0, off = 0;
            var notes = new List<object>(8);
            foreach (AvHit h in hits)
            {
                if (!h.Interactable) continue;
                enabled++;
                Vector2 at = Centre((RectTransform)h.transform);
                if (at.x < 0f || at.y < 0f || at.x > Screen.width || at.y > Screen.height) { off++; if (notes.Count < 8) notes.Add("OFF " + Describe(window, h)); continue; }
                AvHit top = TopHit(hits, at);
                if (top == h) continue;
                covered++;
                if (notes.Count < 8) notes.Add(Describe(window, h) + " <- " + (top != null ? Describe(window, top) : "nothing"));
            }
            Dictionary<string, object> r = State(fronts, window);
            r["enabled"] = enabled; r["covered"] = covered; r["offscreen"] = off; r["notes"] = notes;
            return r;
        }

        private static Dictionary<string, object> Labels(FrontController fronts, OpsFrontWindow window)
        {
            var list = new List<object>(96);
            foreach (AvHit h in Hits(window)) if (list.Count < 96) list.Add((h.Interactable ? "" : "~") + Words(window, h));
            Dictionary<string, object> r = State(fronts, window);
            r["labels"] = list;
            return r;
        }

        /// <summary>The window and the active room as numbers a scenario can assert on.</summary>
        private static Dictionary<string, object> State(FrontController fronts, OpsFrontWindow window)
        {
            var r = new Dictionary<string, object> { { "ok", true }, { "queued", FrontPointer.Pending } };
            if (fronts == null) return r;
            r["open"] = fronts.IsOpen ? 1 : 0;
            r["front"] = (int)fronts.Current;
            r["words"] = fronts.LastWords ?? "";
            r["selectedNode"] = Int(Field(fronts, "selectedNode"));
            r["selectedTeam"] = Int(Field(fronts, "selectedTeam"));
            r["selectedTarget"] = Int(Field(fronts, "selectedTarget"));
            if (Field(window, "lastView") is FrontRoomView lv)
            {
                int moving = 0;
                foreach (OrbitTrackView b in lv.Birds) if (b.Own && b.Relocating) moving++;
                r["birdsMoving"] = moving;
                for (int i = 0; i < lv.Shares.Length; i++) r["share" + i] = lv.Shares[i];
                r["queue"] = lv.Queue.Count;
            }
            object room = Room(window);
            if (room == null) return r;
            r["room"] = room.GetType().Name;
            r["selKind"] = Int(Field(room, "selKind"));
            r["relocating"] = Bool(Field(room, "relocating"));
            r["hasPending"] = Bool(Field(room, "hasPending"));
            if (Field(room, "plate") is FrontMapPlate plate)
            {
                r["zoom"] = plate.View.Zoom; r["centerU"] = plate.View.Center.x; r["centerV"] = plate.View.Center.y;
                Vector2 c = plate.Screen(plate.View.Center.x, plate.View.Center.y);
                r["centerX"] = Mathf.RoundToInt(c.x); r["centerY"] = Mathf.RoundToInt(c.y);
            }
            r["played"] = FrontPointer.Played; r["frame"] = Time.frameCount;
            r["lastWheel"] = FrontPointer.LastWheel; r["lastWheelFrame"] = FrontPointer.LastWheelFrame;
            r["lastWheelX"] = Mathf.RoundToInt(FrontPointer.LastWheelAt.x); r["lastWheelY"] = Mathf.RoundToInt(FrontPointer.LastWheelAt.y);
            return r;
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------------

        private static object Room(OpsFrontWindow window)
        {
            if (window == null || !(Field(window, "rooms") is IFrontRoom[] rooms) || !(Field(window, "lastView") is FrontRoomView v)) return null;
            return rooms[(int)v.Front];
        }

        /// <summary>The map point a gesture aims at: <c>u</c>, <c>v</c>, or with <c>at: "center"</c> the middle of the current view (always on screen, whatever the zoom and pan).</summary>
        private static Vector2 Aim(OpsFrontWindow window, Dictionary<string, object> args)
        {
            if (Text(args, "at") == "center" && Field(Room(window), "plate") is FrontMapPlate plate) return plate.View.Center;
            return new Vector2(Number(args, "u", 0.5f), Number(args, "v", 0.5f));
        }

        private static bool MapScreen(OpsFrontWindow window, float u, float v, out Vector2 at)
        {
            at = default;
            if (!(Field(Room(window), "plate") is FrontMapPlate plate)) return false;
            at = plate.Screen(u, v);
            return true;
        }

        /// <summary>The window's active hit areas in draw order (later draws on top), as its own pointer loop collects them.</summary>
        private static List<AvHit> Hits(OpsFrontWindow window)
        {
            var hits = new List<AvHit>(160);
            window.Root.GetComponentsInChildren(false, hits);
            hits.RemoveAll(h => h == null || !h.isActiveAndEnabled);
            return hits;
        }

        /// <summary>The window's own pick: the last enabled hit area containing the point.</summary>
        private static AvHit TopHit(List<AvHit> hits, Vector2 at)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Interactable && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)hits[i].transform, at, null)) return hits[i];
            return null;
        }

        private static Vector2 Centre(RectTransform t) => RectTransformUtility.WorldToScreenPoint(null, t.TransformPoint(t.rect.center));

        /// <summary>What a control says: its kit label, else every visible text whose centre lies on it (row cards), upper case, rich tags stripped.</summary>
        private static string Words(OpsFrontWindow window, AvHit h)
        {
            AvControl c = h.GetComponentInParent<AvControl>();
            if (c != null && c.transform == h.transform.parent) return Strip(c.Label);
            var sb = new StringBuilder();
            var rt = (RectTransform)h.transform;
            foreach (TMP_Text t in window.Root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(t.text) || !RectTransformUtility.RectangleContainsScreenPoint(rt, Centre(t.rectTransform), null)) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Strip(t.text));
                if (sb.Length > 120) break;
            }
            return sb.ToString();
        }

        /// <summary>A control's words, or for a wordless hit area (a map glyph) its place in the hierarchy and its screen rect.</summary>
        private static string Describe(OpsFrontWindow window, AvHit h)
        {
            string w = Words(window, h).Trim();
            if (w.Length > 0) return w;
            var t = (RectTransform)h.transform;
            Vector2 c = Centre(t);
            return (t.parent != null ? t.parent.name + "/" : "") + t.name + " @" + Mathf.RoundToInt(c.x) + "," + Mathf.RoundToInt(c.y) + " " + Mathf.RoundToInt(t.rect.width) + "x" + Mathf.RoundToInt(t.rect.height);
        }

        private static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]+>", "").ToUpperInvariant();

        private static object Field(object o, string name) => o?.GetType().GetField(name, Any)?.GetValue(o);
        private static int Int(object o) => o is int i ? i : -1;
        private static int Bool(object o) => o is bool b ? (b ? 1 : 0) : -1;

        // The module runtime root is HideAndDontSave, which Object.FindObjectOfType never returns.
        private static T Find<T>() where T : Object
        {
            T[] all = Resources.FindObjectsOfTypeAll<T>();
            return all.Length > 0 ? all[0] : null;
        }
    }
}
#endif
