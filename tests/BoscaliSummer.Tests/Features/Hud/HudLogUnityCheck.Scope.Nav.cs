#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// The NAV DATA block (HUD holo pass C, 2026-10-12): the cockpit view's bottom-right corner, from the production StatusPanel with a frame a reader would have filled. The check judges what is DRAWN (the words and the quads of the
// block's own surface) against the block's rules: a captioned shelf with its node and stub mirrored for the right side, right-aligned rows (a DIM label and an INK / BRIGHT value, BINGO or JOKER in bold amber), the words
// whole (a long base name loses whole words, never gets dots), 260 wide and 24 units in from the screen's right and bottom edges, rows 18 apart, nothing overlapping, nothing taking a click; and that it is gone in third person,
// at Minimal, with its switch off and with the board off, has no caption at Lean, and follows the pilot's Opacity and Shade. Renders nav-<case>-<W>x<H>.png (over the check's hazy sky with a dark dash under it, the two
// grounds the ink meets). Run alone with -pages nav.
public static partial class HudLogUnityCheck
{
    private sealed class NavRig
    {
        public Canvas Canvas;
        public object Status, Frame;
        public Vector2 Size;
        /// <summary>A design unit in canvas units (pixels here; never under 0.85).</summary>
        public float U;
    }

    private static readonly Regex MarkUp = new Regex("<[^>]*>", RegexOptions.CultureInvariant);

    private static object NavInputOf(AvUnits units, params object[] nameValue)
    {
        object nav = Fill(Boxed("NavInput"), "Units", units);
        return Fill(nav, nameValue);
    }

    /// <summary>Everything known: a base 97 degrees 23.4 km away, a target 271 degrees 8.7 km away closing at 250 m/s, 65 minutes of fuel and 12 minutes 25 seconds of mission.</summary>
    private static object NavAll(AvUnits units = AvUnits.Metric, params object[] over)
    {
        object nav = NavInputOf(units, "HasHome", true, "HomeName", "ANNEX CLIFF", "HomeBearingDeg", 97f, "HomeM", 23400f,
            "HasTarget", true, "TargetName", "FS-20 VORTEX", "TargetBearingDeg", 271f, "TargetM", 8700f, "ClosureMps", 250f,
            "HasFuel", true, "EnduranceS", 3900f, "HasClock", true, "ClockS", 745f);
        return Fill(nav, over);
    }

    /// <summary>The cockpit's dash under the bottom third of the picture (the sky is the check's hazy gradient): ink at the bottom right meets both.</summary>
    private static void NavBackdrop(Canvas canvas)
    {
        var dash = new GameObject("Dash", typeof(RectTransform), typeof(Image));
        dash.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)dash.transform;
        rect.anchorMin = new Vector2(0f, 0f); rect.anchorMax = new Vector2(1f, 0.3f); rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = dash.GetComponent<Image>();
        image.raycastTarget = false;
        image.color = new Color(0.16f, 0.17f, 0.19f);
        // a second, pale slab at the right end: a cloud bank behind the corner, where the ink is hardest to read
        var cloud = new GameObject("DashHaze", typeof(RectTransform), typeof(Image));
        cloud.transform.SetParent(dash.transform, false);
        var haze = (RectTransform)cloud.transform;
        haze.anchorMin = new Vector2(0.78f, 0.55f); haze.anchorMax = new Vector2(1f, 1f); haze.offsetMin = haze.offsetMax = Vector2.zero;
        var hazeImage = cloud.GetComponent<Image>();
        hazeImage.raycastTarget = false;
        hazeImage.color = new Color(0.78f, 0.82f, 0.86f);
    }

    private static NavRig MakeNav(Vector2 size, object options, object nav, bool cockpit = true, bool dash = true)
    {
        var rig = new NavRig { Size = size, U = Mathf.Max(size.y / 1080f, 0.85f) };
        MakeBoard(size, 2f, out rig.Canvas, out rig.Status, out rig.Frame);
        if (dash) NavBackdrop(rig.Canvas);
        var scene = new ScopeScene(2f, size, rig.Frame);
        FixtureSystems(2f, scene);
        StoresFixture("systems", 2f, scene);
        Set(rig.Frame, "CockpitView", cockpit);
        Set(rig.Frame, "Nav", nav);
        Set(rig.Status, "Options", options);
        PresentNav(rig);
        return rig;
    }

    private static void PresentNav(NavRig rig)
    {
        Call(rig.Status, "Present", rig.Frame);
        foreach (string name in new[] { "Dash", "Sky", "Cockpit" })
        {
            Transform t = rig.Canvas.transform.Find(name);
            if (t != null) t.SetAsFirstSibling();
        }
        Canvas.ForceUpdateCanvases();
    }

    private static void EndNav(NavRig rig)
    {
        Call(rig.Status, "Destroy");
        Object.DestroyImmediate(rig.Canvas.gameObject);
        ScopeReset();
    }

    /// <summary>The words of the block's own surface (mark-up stripped) and their glyph boxes from the canvas's top-left; the TMP label too, for colour and weight.</summary>
    private static Dictionary<string, Rect> NavWords(NavRig rig, out Dictionary<string, TextMeshProUGUI> labels)
    {
        labels = new Dictionary<string, TextMeshProUGUI>();
        var words = new Dictionary<string, Rect>();
        var surface = (RectTransform)Field(rig.Status, "navSurface");
        if (surface == null) return words;
        var canvasRect = (RectTransform)rig.Canvas.transform;
        foreach (TextMeshProUGUI label in surface.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (!label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text)) continue;
            label.ForceMeshUpdate();
            string text = MarkUp.Replace(label.text, "");
            TMP_TextInfo info = label.textInfo;
            var lo = new Vector2(float.MaxValue, float.MaxValue);
            var hi = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo c = info.characterInfo[i];
                if (!c.isVisible) continue;
                Vector3 a = canvasRect.InverseTransformPoint(label.transform.TransformPoint(c.bottomLeft)), b = canvasRect.InverseTransformPoint(label.transform.TransformPoint(c.topRight));
                Rect cr = canvasRect.rect;
                lo = new Vector2(Mathf.Min(lo.x, a.x - cr.xMin), Mathf.Min(lo.y, cr.yMax - b.y));
                hi = new Vector2(Mathf.Max(hi.x, b.x - cr.xMin), Mathf.Max(hi.y, cr.yMax - a.y));
            }
            if (lo.x > hi.x) continue;
            words[text] = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
            labels[text] = label;
        }
        return words;
    }

    private static List<Rect> NavQuads(NavRig rig)
    {
        var list = new List<Rect>();
        var vector = (AvVector)Field(rig.Status, "navVector");
        if (vector == null) return list;
        AvQuadBuffer buffer = vector.Buffer;
        float height = rig.Size.y;
        for (int q = 0; q < buffer.Count; q++)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int v = 0; v < 4; v++)
            {
                x0 = Mathf.Min(x0, buffer.X[q * 4 + v]); x1 = Mathf.Max(x1, buffer.X[q * 4 + v]);
                y0 = Mathf.Min(y0, buffer.Y[q * 4 + v]); y1 = Mathf.Max(y1, buffer.Y[q * 4 + v]);
            }
            list.Add(Rect.MinMaxRect(x0, height - y1, x1, height - y0));
        }
        return list;
    }

    private static bool NavUp(NavRig rig) => Up("navFurniture", rig.Status);

    /// <summary>The plan the block was drawn from (design units from the screen's top-left): left, top, right, bottom, the first row's middle.</summary>
    private static float[] NavPlanOf(NavRig rig)
    {
        object plan = Field(rig.Status, "navPlan");
        System.Type t = plan.GetType();
        return new[] { (float)t.GetField("Left").GetValue(plan), (float)t.GetField("Top").GetValue(plan), (float)t.GetField("Right").GetValue(plan), (float)t.GetField("Bottom").GetValue(plan), (float)t.GetField("FirstRowY").GetValue(plan) };
    }

    private static void NavShot(NavRig rig, string name)
    {
        string tag = "nav-" + name + "-" + (int)rig.Size.x + "x" + (int)rig.Size.y;
        foreach (string root in new[] { "navShade", "navFurniture" })
        {
            var tree = (RectTransform)Field(rig.Status, root);
            if (tree != null) foreach (Graphic g in tree.GetComponentsInChildren<Graphic>(true)) Check(!g.raycastTarget, "NAV DATA takes no click: " + g.name + " " + tag);
        }
        var vector = (AvVector)Field(rig.Status, "navVector");
        Check(vector == null || !vector.Buffer.Overflowed, "The NAV DATA buffer did not fill: " + tag);
        Capture(rig.Canvas, tag + ".png");
    }

    /// <summary>
    /// The block as drawn against the rules: <paramref name="rows"/> are the (label, value) pairs from the top row down; the caption is on its shelf when <paramref name="shelf"/>. The geometry is checked against the constants
    /// the design gives (260 wide, 24 in from the right and bottom, 18 apart), not against the plan the panel itself keeps.
    /// </summary>
    private static void NavPlaces(NavRig rig, bool shelf, string[][] rows, string tag)
    {
        float u = rig.U, w = rig.Size.x, h = rig.Size.y;
        Dictionary<string, Rect> words = NavWords(rig, out Dictionary<string, TextMeshProUGUI> labels);
        List<Rect> quads = NavQuads(rig);
        Check(NavUp(rig), "NAV DATA is up: " + tag);
        Check(words.ContainsKey("NAV DATA") == shelf, "NAV DATA: the caption is on its shelf only with captions on (" + shelf + "): " + tag);
        float right = w - 24f * u, bottomRow = h - 24f * u - 6f * u, nudge = 12f * u * 0.07f;
        float previous = float.NaN;
        for (int i = 0; i < rows.Length; i++)
        {
            string label = rows[i][0], value = rows[i][1], flag = rows[i].Length > 2 ? rows[i][2] : null;
            Check(words.ContainsKey(label) && words.ContainsKey(value), "NAV DATA: row " + label + " '" + value + "' is drawn: " + tag + " in " + string.Join(" | ", words.Keys));
            if (!(words.ContainsKey(label) && words.ContainsKey(value))) continue;
            Rect l = words[label], v = words[value];
            float rowCentreY = bottomRow - (rows.Length - 1 - i) * 18f * u - nudge;
            Check(Mathf.Abs(v.center.y - rowCentreY) <= 4.5f && Mathf.Abs(l.center.y - rowCentreY) <= 4.5f, "NAV DATA: row " + label + " stands " + (rows.Length - 1 - i) + " rows over the bottom margin, rows 18 apart (" + rowCentreY + "): " + l.center.y + " / " + v.center.y + " " + tag);
            float tail = flag != null && words.ContainsKey(flag) ? words[flag].xMax : v.xMax;
            Check(Mathf.Abs(tail - right) <= 3.5f, "NAV DATA: row " + label + " is right-aligned to 24 units from the screen's edge (" + right + "): " + tail + " " + tag);
            Check(l.xMax < v.xMin && v.xMin - l.xMax >= 3f * u && v.xMin - l.xMax <= 14f * u, "NAV DATA: the label stands a character before its value: " + l + " " + v + " " + tag);
            if (flag != null)
            {
                Check(words.ContainsKey(flag) && words[flag].xMin > v.xMax, "NAV DATA: the flag " + flag + " follows the value: " + tag);
                if (labels.ContainsKey(flag))
                {
                    Color c = labels[flag].color, amber = new Color(1f, 216f / 255f, 74f / 255f);      // the board's AMBER (LAYOUT.md 1: #ffd84a)
                    Check(Mathf.Abs(c.r - amber.r) < 0.03f && Mathf.Abs(c.g - amber.g) < 0.03f && Mathf.Abs(c.b - amber.b) < 0.03f && (labels[flag].fontStyle & FontStyles.Bold) != 0, "NAV DATA: " + flag + " is bold amber: " + c + " " + labels[flag].fontStyle + " " + tag);
                }
            }
            if (!float.IsNaN(previous)) Check(Mathf.Abs(l.center.y - previous - 18f * u) <= 2f, "NAV DATA: rows are 18 apart: " + (l.center.y - previous) + " " + tag);
            previous = l.center.y;
            Check(!value.Contains("…") && !value.Contains("..."), "NAV DATA: no word is cut with dots: " + value + " " + tag);
            Check(!labels[value].isTextTruncated && !labels[value].isTextOverflowing, "NAV DATA: the value is shown whole: " + value + " " + tag);
        }
        // The shelf: a hairline 6 under the block's top ending in a 4 x 4 node at the LEFT end (260 left of the right edge) and a stub up and to the left; the caption right-aligned 12 in from the right end.
        if (shelf && rows.Length > 0)
        {
            float top = (h - 24f * u) - (24f * u + 18f * u * (rows.Length - 1) + 6f * u);      // the block's top: the bottom margin, up by the shelf row, the rows and the last row's half
            float cy = top + 6f * u, left = right - 260f * u;
            Check(HasLine(quads, true, cy, right - 8f * u, right), "NAV DATA: the shelf's line runs to the right end, 6 under the block's top: " + tag + " cy " + cy);
            Check(HasLine(quads, true, cy, left, left + 12f * u), "NAV DATA: ...and from the node at its left end: " + tag);
            bool node = false, stub = false;
            foreach (Rect r in quads)
            {
                if (Mathf.Abs(r.width - 4f * u) <= 1.2f && Mathf.Abs(r.height - 4f * u) <= 1.2f && Mathf.Abs(r.center.x - left) <= 2f && Mathf.Abs(r.center.y - cy) <= 2f) node = true;
                if (r.center.x < left - 1f && r.center.x > left - 8f * u && r.center.y < cy - 1f && r.center.y > cy - 8f * u) stub = true;
            }
            Check(node, "NAV DATA: a 4 x 4 node at the shelf's left end: " + tag);
            Check(stub, "NAV DATA: ...and the 45 degree stub up and to the left of it: " + tag);
            if (words.ContainsKey("NAV DATA")) Check(Mathf.Abs(words["NAV DATA"].xMax - (right - 12f * u)) <= 3.5f && Mathf.Abs(words["NAV DATA"].center.y - cy) <= 4.5f, "NAV DATA: the caption is right-aligned 12 in from the right end, on the line: " + words["NAV DATA"] + " " + tag);
            // The 6 clear between the caption's glyphs and the first row's: 12 high glyphs on an 18 pitch.
            string first = rows[0][0];
            if (words.ContainsKey("NAV DATA") && words.ContainsKey(first)) Check(words[first].yMin - words["NAV DATA"].yMax >= 4f * u, "NAV DATA: the first row clears the caption: " + tag);
        }
        else Check(quads.Count == 0, "NAV DATA: without captions nothing but words is drawn (no shelf, no node): " + quads.Count + " " + tag);
        // Nothing overlaps, and the block stays 260 wide on the screen.
        var list = new List<KeyValuePair<string, Rect>>(words);
        for (int i = 0; i < list.Count; i++)
        {
            Check(list[i].Value.xMax <= w - 24f * u + 3.5f && list[i].Value.yMax <= h - 24f * u + 2f && list[i].Value.xMin >= w - 24f * u - 260f * u - 1f, "NAV DATA: '" + list[i].Key + "' stays inside its 260 x block: " + list[i].Value + " " + tag);
            for (int j = i + 1; j < list.Count; j++) Check(!list[i].Value.Overlaps(list[j].Value), "NAV DATA: '" + list[i].Key + "' and '" + list[j].Key + "' do not overlap: " + tag);
        }
        // It keeps out of the board's box (top right) and the kill feed's and map's corners: they are at the other corners.
        Rect board = CanvasRectOf((RectTransform)rig.Canvas.transform, (RectTransform)Field(rig.Status, "root"));
        foreach (KeyValuePair<string, Rect> word in words) Check(!word.Value.Overlaps(board), "NAV DATA: '" + word.Key + "' is clear of the board: " + tag);
    }

    private static void NavHidden(NavRig rig, string why, string tag)
    {
        Check(!NavUp(rig) && !Up("navShade", rig.Status), "NAV DATA is not drawn " + why + ": " + tag);
        Check(NavWords(rig, out _).Count == 0, "NAV DATA has no words " + why + ": " + tag);
    }

    private static void ScopeNav()
    {
        foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720) })
        {
            string res = (int)size.x + "x" + (int)size.y;
            string[][] all =
            {
                new[] { "HOME", "ANNEX CLIFF 097 / 23KM" }, new[] { "TGT", "FS-20 VORTEX 271 / 8.7KM" }, new[] { "CLS", "CLOSING 900KM/H" },
                new[] { "FUEL", "65MIN" }, new[] { "CLOCK", "0:12:25" },
            };

            // Every row, the shade up, the pilot's opacity on the block's own group.
            NavRig rig = MakeNav(size, PilotOptions("Opacity", 0.6f), NavAll());
            NavPlaces(rig, true, all, "full " + res);
            Check(Up("navShade", rig.Status) && ((RawImage)Field(rig.Status, "navCloud")).gameObject.activeInHierarchy, "NAV DATA: the cloud shade is behind it: " + res);
            Check(Mathf.Abs(((CanvasGroup)Field(rig.Status, "navGroup")).alpha - 0.6f) < 0.001f, "NAV DATA follows the pilot's opacity (0.6): " + res);
            // Written only when changed: the same data again leaves every word as it was; the clock ticking changes its word and no other.
            NavWords(rig, out Dictionary<string, TextMeshProUGUI> held);
            var heldText = new Dictionary<string, string>();
            foreach (KeyValuePair<string, TextMeshProUGUI> kv in held) heldText[kv.Key] = kv.Value.text;
            PresentNav(rig);
            foreach (KeyValuePair<string, TextMeshProUGUI> kv in NavLabels(rig)) Check(heldText.ContainsKey(kv.Key) && heldText[kv.Key] == kv.Value.text, "NAV DATA: a tick with the same data changes no word: " + kv.Key + " " + res);
            Set(rig.Frame, "Nav", NavAll(AvUnits.Metric, "ClockS", 746f));
            PresentNav(rig);
            Dictionary<string, Rect> after = NavWords(rig, out _);
            Check(after.ContainsKey("0:12:26") && !after.ContainsKey("0:12:25") && after.ContainsKey("ANNEX CLIFF 097 / 23KM"), "NAV DATA: the clock moved one second and nothing else changed: " + res);
            NavShot(rig, "full");
            EndNav(rig);

            // The same block over the bare hazy sky (an aircraft with no cockpit in the corner): the shade alone carries the ink.
            rig = MakeNav(size, PilotOptions(), NavAll(), dash: false);
            NavPlaces(rig, true, all, "over the sky " + res);
            NavShot(rig, "full-sky");
            EndNav(rig);

            // Home alone (a base, no target selected, fuel unknown, no clock): one row, the block shorter and still anchored to the corner.
            rig = MakeNav(size, PilotOptions(), NavInputOf(AvUnits.Metric, "HasHome", true, "HomeName", "ANNEX CLIFF", "HomeBearingDeg", 97f, "HomeM", 23400f));
            NavPlaces(rig, true, new[] { new[] { "HOME", "ANNEX CLIFF 097 / 23KM" } }, "home only " + res);
            Check(!NavWords(rig, out _).ContainsKey("TGT") && !NavWords(rig, out _).ContainsKey("CLOCK") && !NavWords(rig, out _).ContainsKey("FUEL"), "NAV DATA: a row with no data does not exist: " + res);
            NavShot(rig, "home");
            EndNav(rig);

            // BINGO and JOKER: bold amber after the endurance.
            rig = MakeNav(size, PilotOptions(), NavAll(AvUnits.Metric, "Bingo", true, "EnduranceS", 420f));
            NavPlaces(rig, true, new[] { all[0], all[1], all[2], new[] { "FUEL", "7MIN", "BINGO" }, all[4] }, "bingo " + res);
            NavShot(rig, "bingo");
            EndNav(rig);
            rig = MakeNav(size, PilotOptions(), NavAll(AvUnits.Imperial, "Joker", true));
            NavPlaces(rig, true, new[] { new[] { "HOME", "ANNEX CLIFF 097 / 12.6NM" }, new[] { "TGT", "FS-20 VORTEX 271 / 4.7NM" }, new[] { "CLS", "CLOSING 486KT" }, new[] { "FUEL", "65MIN", "JOKER" }, all[4] }, "joker, imperial " + res);
            EndNav(rig);

            // Long names: whole words that fit, never dots.
            rig = MakeNav(size, PilotOptions(), NavAll(AvUnits.Metric, "HomeName", "WESTERN HIGHWAY STRIP NORTH", "TargetName", "HYPERION CLASS CARRIER"));
            NavPlaces(rig, true, new[] { new[] { "HOME", "WESTERN HIGHWAY 097 / 23KM" }, new[] { "TGT", "HYPERION CLASS 271 / 8.7KM" }, all[2], all[3], all[4] }, "long names " + res);
            NavShot(rig, "long");
            EndNav(rig);

            // Lean: the rows stay, the caption shelf goes. Minimal, the switch off, the board off, third person: nothing.
            rig = MakeNav(size, PilotOptions("Declutter", Declutter("Lean")), NavAll());
            NavPlaces(rig, false, all, "lean " + res);
            NavShot(rig, "lean");
            EndNav(rig);
            rig = MakeNav(size, PilotOptions("Declutter", Declutter("Minimal")), NavAll());
            NavHidden(rig, "at Minimal", res);
            EndNav(rig);
            rig = MakeNav(size, PilotOptions("ShowNavData", false), NavAll());
            NavHidden(rig, "with its switch off", res);
            EndNav(rig);
            rig = MakeNav(size, PilotOptions("Enabled", false), NavAll());
            NavHidden(rig, "with the board off", res);
            EndNav(rig);
            rig = MakeNav(size, PilotOptions(), NavAll(), cockpit: false);
            NavHidden(rig, "in third person (the cluster's target card owns the corner)", res);
            EndNav(rig);
            rig = MakeNav(size, PilotOptions(), NavInputOf(AvUnits.Metric));
            NavHidden(rig, "with nothing to say", res);
            EndNav(rig);

            // Shade 0: no cloud, the ink alone; a live change takes the block away and brings it back.
            rig = MakeNav(size, PilotOptions("Shade", 0f), NavAll());
            Check(NavUp(rig) && !Up("navShade", rig.Status), "NAV DATA: Shade 0 draws no cloud and keeps the block: " + res);
            Set(rig.Status, "Options", PilotOptions("Declutter", Declutter("Minimal")));
            PresentNav(rig);
            NavHidden(rig, "after the level goes to Minimal", res);
            Set(rig.Status, "Options", PilotOptions());
            PresentNav(rig);
            NavPlaces(rig, true, all, "back at Full " + res);
            Set(rig.Status, "cornersSuppressed", true);
            Call(rig.Status, "CornersFrame", true, false);
            NavHidden(rig, "while the game's gate is shut (a pause, the maximised map)", res);
            Set(rig.Status, "cornersSuppressed", false);
            PresentNav(rig);
            Check(NavUp(rig), "NAV DATA: and back when the gate opens: " + res);
            EndNav(rig);
        }
    }

    private static Dictionary<string, TextMeshProUGUI> NavLabels(NavRig rig)
    {
        NavWords(rig, out Dictionary<string, TextMeshProUGUI> labels);
        return labels;
    }
}
#endif
