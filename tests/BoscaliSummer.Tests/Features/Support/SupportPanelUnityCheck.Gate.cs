#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The current C2 CAP geometry/readability gate; model facts are checked in the other partial.</summary>
public static partial class SupportPanelUnityCheck
{
    private static void GateConsole(RectTransform root, string where)
    {
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        var placed = new List<KeyValuePair<TMP_Text, Rect>>(260);
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
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
                    Fail(where + ": below the 10 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
                if (t.isTextTruncated) Fail(where + ": text must be complete: " + t.text);
            }
            if (!icon && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Fail(where + ": contrast " + contrast.ToString("0.00") + " for '" + t.text + "' (" + t.name + ")");
            }
            if (!icon)
            {
                // Glyph ink bounds in console space: two texts whose ink overlaps have collided.
                Vector3 lo = root.InverseTransformPoint(t.rectTransform.TransformPoint(b.min));
                Vector3 hi = root.InverseTransformPoint(t.rectTransform.TransformPoint(b.max));
                Rect ink = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y));
                assertions++;
                if (ink.xMin < root.rect.xMin - 1f || ink.xMax > root.rect.xMax + 1f || ink.yMin < root.rect.yMin - 1f)
                    Fail(where + ": text outside the page '" + t.text + "'");
                placed.Add(new KeyValuePair<TMP_Text, Rect>(t, ink));
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
            // A solid slab (banner, state slab) is a sibling Image behind the label, not an ancestor: it counts when it covers the label's centre.
            for (int ci = x.childCount - 1; ci >= 0; ci--) // later siblings draw on top
            {
                Transform child = x.GetChild(ci);
                var slab = child.GetComponent<Image>();
                if (slab == null || !slab.enabled || slab.color.a <= 0.35f || child == t.transform || !child.gameObject.activeInHierarchy) continue;
                var corners = new Vector3[4];
                ((RectTransform)child).GetWorldCorners(corners);
                Vector3 centre = t.rectTransform.TransformPoint(t.rectTransform.rect.center);
                if (centre.x >= corners[0].x && centre.x <= corners[2].x && centre.y >= corners[0].y && centre.y <= corners[2].y && (corners[2].x - corners[0].x) > 12f)
                {
                    Rgba o = slab.color.ToRgba().Over(ground.ToRgba());
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
