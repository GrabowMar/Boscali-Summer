#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The current CALLS geometry/readability gate; model facts are checked in the other partial.</summary>
public static partial class SupportPanelUnityCheck
{
    private static void GateConsole(AvConsole con, string where)
    {
        float gutterLeft = AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        var placed = new List<KeyValuePair<TMP_Text, Rect>>(160);
        foreach (TMP_Text t in con.Root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            gatedTexts++;
            assertions++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon");
            Bounds b = t.textBounds; // what TMP actually laid out, after wrapping and auto-size
            if (!icon)
            {
                if (b.size.x > r.width + 1.5f)
                    Fail(where + ": overflows width (" + b.size.x.ToString("0") + " > " + r.width.ToString("0") + ") '" + t.text + "'");
                if (b.size.y > r.height + 1.5f)
                    Fail(where + ": overflows height (" + b.size.y.ToString("0") + " > " + r.height.ToString("0") + ") '" + t.text + "'");
                if (t.fontSize < AvTokens.FontMicro - 0.01f)
                    Fail(where + ": below the CALLS 10 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
                if (t.isTextTruncated) Fail(where + ": text must be complete: " + t.text);
            }
            var corners = new Vector3[4];
            t.rectTransform.GetWorldCorners(corners);
            if (t.GetComponentInParent<ScrollRect>() != null)
            {
                float right = con.Root.InverseTransformPoint(corners[2]).x;
                if (right > gutterLeft) Fail(where + ": enters the gutter (" + right.ToString("0") + ") '" + t.text + "'");
            }
            if (!icon && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Fail(where + ": contrast " + contrast.ToString("0.00") + " for '" + t.text + "' (" + t.name + ")");
            }
            if (!icon && t.GetComponentInParent<ScrollRect>() != null)
            {
                // Glyph ink bounds (page texts only: the chrome above the page is the kit gallery's to gate) in console space: two texts whose ink overlaps have collided.
                Vector3 lo = con.Root.InverseTransformPoint(t.rectTransform.TransformPoint(b.min));
                Vector3 hi = con.Root.InverseTransformPoint(t.rectTransform.TransformPoint(b.max));
                placed.Add(new KeyValuePair<TMP_Text, Rect>(t, Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y))));
            }
        }
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
            {
                Rect a = placed[i].Value, b = placed[j].Value;
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                assertions++;
                if (w > 1f && h > 1.5f)
                    Fail(where + ": texts overlap: '" + placed[i].Key.text + "' (" + placed[i].Key.name + ") and '" + placed[j].Key.text + "' (" + placed[j].Key.name + ")");
            }
        foreach (AvControl tab in con.Root.GetComponentsInChildren<AvControl>(true))
            if (tab.transform.parent != null && tab.transform.parent.name == "Tabs" && tab.transform.Find("Label") != null
                && tab.GetComponentsInChildren<TMP_Text>(true).Length < 2)
                Fail(where + ": tab without icon " + tab.name);
        foreach (Transform s in con.Root.GetComponentsInChildren<Transform>(true))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null)
                Fail(where + ": section without icon " + s.name);
        // Parts of one flow never overlap each other (shown parts only).
        NoPartOverlap(con.Page(0).Content, where);
    }

    private static bool CanvasOn(Component c)
    {
        for (Transform x = c.transform; x != null; x = x.parent)
        {
            var canvas = x.GetComponent<Canvas>();
            if (canvas != null && !canvas.enabled) return false;
        }
        return true;
    }

    private static void NoPartOverlap(RectTransform content, string where)
    {
        var rects = new List<RectTransform>();
        foreach (Transform child in content)
            if (child.gameObject.activeSelf && child is RectTransform rt && rt.rect.height > 0.5f) rects.Add(rt);
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                Rect a = InSpace(content, rects[i]), b = InSpace(content, rects[j]);
                float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                assertions++;
                if (w > 1f && h > 1f) Fail(where + ": parts overlap: " + rects[i].name + " and " + rects[j].name);
            }
    }

    private static Rect InSpace(RectTransform space, RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector3 lo = space.InverseTransformPoint(c[0]), hi = space.InverseTransformPoint(c[2]);
        return Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y));
    }

    // The nearest opaque-ish fill behind a label: an AvFrame sibling/ancestor fill, else an Image, else ground.
    private static Color BackgroundOf(TMP_Text t, Color ground)
    {
        for (Transform x = t.transform.parent; x != null; x = x.parent)
        {
            foreach (Transform child in x)
            {
                var f = child.GetComponent<AvFrame>();
                if (f != null && f.enabled && f.Fill && f.FillColor.a > 0.35f && child != t.transform)
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
