#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UnityCheckHarness;

/// <summary>
/// The console gate every offline panel harness shares (lifted out of the old COC check, annex 05 P12 item 1): text overflow, the 11 px floor, the gutter,
/// contrast 4.5, overlapping parts, section icons, colliding texts, the fold line and the honesty rule. Each gate appends to <see cref="Failures"/>;
/// the run fails when any entry exists. A gate that never fails proves nothing, so the harness has a self-test that injects faults.
/// </summary>
public static class PanelGate
{
    public static readonly List<string> Failures = new List<string>();
    public static int CheckedTexts;

    private static readonly Regex ZeroPercent = new Regex(@"\b0\s*%");

    public static void Fail(string where, string message) => Failures.Add(where + ": " + message);

    /// <summary>The gutter's left edge: the right margin the page flows keep clear.</summary>
    public static float GutterLeft => AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;

    /// <summary>Overflow, floor, gutter and contrast of every active text under <paramref name="scope"/>.</summary>
    public static void GateTexts(RectTransform scope, string where, float gutterLeft, RectTransform space)
    {
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        foreach (TMP_Text t in scope.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            CheckedTexts++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon");
            if (!icon)
            {
                Bounds b = t.textBounds;
                if (b.size.x > r.width + 1.5f)
                    Fail(where, "overflows width (" + b.size.x.ToString("0") + " > " + r.width.ToString("0") + ") '" + t.text + "'");
                if (b.size.y > r.height + 1.5f)
                    Fail(where, "overflows height (" + b.size.y.ToString("0") + " > " + r.height.ToString("0") + ") '" + t.text + "'");
                if (t.fontSize < AvTypeScale.Floor - 0.01f && t.name != "Number")
                    Fail(where, "below the 11 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
            }
            if (gutterLeft < float.MaxValue && t.GetComponentInParent<ScrollRect>() != null)
            {
                var corners = new Vector3[4];
                t.rectTransform.GetWorldCorners(corners);
                float right = space.InverseTransformPoint(corners[2]).x;
                if (right > gutterLeft) Fail(where, "enters the gutter (" + right.ToString("0") + ") '" + t.text + "'");
            }
            if (!icon && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Fail(where, "contrast " + contrast.ToString("0.00") + " for '" + t.text + "' (" + t.name + ")");
            }
        }
    }

    /// <summary>No two active top-level parts of a page may intersect.</summary>
    public static void PartOverlap(RectTransform content, string where)
    {
        var rects = new List<RectTransform>();
        foreach (Transform child in content)
            if (child.gameObject.activeSelf) rects.Add((RectTransform)child);
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                RectTransform a = rects[i], b = rects[j];
                float ax0 = a.anchoredPosition.x, ax1 = ax0 + a.rect.width, ay0 = -a.anchoredPosition.y, ay1 = ay0 + a.rect.height;
                float bx0 = b.anchoredPosition.x, bx1 = bx0 + b.rect.width, by0 = -b.anchoredPosition.y, by1 = by0 + b.rect.height;
                bool overlap = ax0 < bx1 - 0.5f && bx0 < ax1 - 0.5f && ay0 < by1 - 0.5f && by0 < ay1 - 0.5f;
                if (overlap && a.rect.height > 0.5f && b.rect.height > 0.5f)
                    Fail(where, "part '" + a.name + "' overlaps '" + b.name + "'");
            }
    }

    /// <summary>Glyph bounds of two active texts of one page may not overlap (the part overlap gate cannot see inside a part).</summary>
    public static void TextCollisions(RectTransform content, RectTransform space, string where)
    {
        var texts = new List<TMP_Text>();
        var boxes = new List<Rect>();
        foreach (TMP_Text t in content.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t) || t.name.StartsWith("Icon")) continue;
            if (t.GetComponentInParent<AvControl>() != null) continue;   // a button's own label sits in its own frame
            t.ForceMeshUpdate();
            Bounds b = t.textBounds;
            Vector3 lo = space.InverseTransformPoint(t.transform.TransformPoint(b.min)), hi = space.InverseTransformPoint(t.transform.TransformPoint(b.max));
            texts.Add(t);
            // Line boxes carry a little leading: the glyphs themselves occupy the inner part, so test the box shrunk by 2 px above and below.
            boxes.Add(Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y) + 2f, Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y) - 2f));
        }
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
            {
                Rect a = boxes[i], c = boxes[j];
                float dx = Mathf.Min(a.xMax, c.xMax) - Mathf.Max(a.xMin, c.xMin), dy = Mathf.Min(a.yMax, c.yMax) - Mathf.Max(a.yMin, c.yMin);
                if (dx > 1.5f && dy > 1f) Fail(where, "text '" + texts[i].text + "' collides with '" + texts[j].text + "'");
            }
    }

    /// <summary>Every section header carries an icon.</summary>
    public static void SectionIcons(RectTransform root, string where)
    {
        foreach (Transform s in root.GetComponentsInChildren<Transform>(true))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null) Fail(where, "section without icon " + s.name);
    }

    /// <summary>Distance of a rect's bottom edge below the top of <paramref name="space"/> (a console root pivoted at its top left).</summary>
    public static float BottomFromTop(RectTransform rect, RectTransform space)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float lowest = float.MaxValue;
        foreach (Vector3 c in corners) lowest = Mathf.Min(lowest, space.InverseTransformPoint(c).y);
        return -lowest;
    }

    /// <summary>The element at the end of a slash path of object names ("Hero/Ticket 1/Control ACCEPT"), active in the hierarchy, or null.</summary>
    public static RectTransform FindRect(Transform root, string path)
    {
        string[] names = path.Split('/');
        foreach (RectTransform r in root.GetComponentsInChildren<RectTransform>(false))
        {
            Transform x = r;
            bool match = true;
            for (int i = names.Length - 1; i >= 0; i--)
            {
                if (x == null || x.name != names[i]) { match = false; break; }
                x = x.parent;
            }
            if (match) return r;
        }
        return null;
    }

    /// <summary>Honesty: a readout the part names <c>val.*</c> is a dash while the fixture is unknown, and no text anywhere reads a percentage of zero.</summary>
    public static void Honesty(RectTransform scope, string where, bool unknownFixture)
    {
        foreach (TMP_Text t in scope.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            if (ZeroPercent.IsMatch(t.text)) Fail(where, "reads a zero percentage '" + t.text + "'");
            if (unknownFixture && t.name.StartsWith("val.") && t.text != "—") Fail(where, "an unknown readout is not a dash: " + t.name + " = '" + t.text + "'");
        }
    }

    private static bool Covers(RectTransform rect, Vector3 worldPoint)
    {
        Vector3 p = rect.InverseTransformPoint(worldPoint);
        return rect.rect.Contains(new Vector2(p.x, p.y));
    }

    /// <summary>Every active text of the scope, upper-cased, for the spec's must-appear strings.</summary>
    public static List<string> ActiveTexts(RectTransform scope)
    {
        var all = new List<string>();
        foreach (TMP_Text t in scope.GetComponentsInChildren<TMP_Text>(false))
            if (t.isActiveAndEnabled && t.text.Length > 0 && CanvasOn(t)) all.Add(t.text);
        return all;
    }

    private static Color BackgroundOf(TMP_Text t, Color ground)
    {
        // The text's own centre decides which fill is behind it: a sibling frame counts only when it covers that point.
        var corners = new Vector3[4];
        t.rectTransform.GetWorldCorners(corners);
        Vector3 centre = (corners[0] + corners[2]) * 0.5f;
        for (Transform x = t.transform.parent; x != null; x = x.parent)
        {
            foreach (Transform child in x)
            {
                var f = child.GetComponent<AvFrame>();
                if (f != null && f.enabled && f.Fill && f.FillColor.a > 0.35f && child != t.transform && f.gameObject.activeInHierarchy && Covers(f.rectTransform, centre))
                {
                    Rgba o = f.FillColor.ToRgba().Over(ground.ToRgba());
                    return new Color(o.R, o.G, o.B);
                }
            }
            var img = x.GetComponent<Image>();
            if (img != null && img.enabled && img.color.a > 0.35f)
                return new Color(img.color.r, img.color.g, img.color.b);
        }
        return ground;
    }
}
#endif
