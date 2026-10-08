using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>Repaint callbacks of every static part of one window; <see cref="Apply"/> re-reads the live style sheet roles.</summary>
    internal sealed class FrontSkin
    {
        private readonly List<Action> parts = new List<Action>(64);

        public void Add(Action restyle) { parts.Add(restyle); restyle(); }

        public void Apply() { for (int i = 0; i < parts.Count; i++) parts[i](); }
    }

    /// <summary>Absolute-placement helpers of the front windows (panel pixels, origin top-left). Colours come from kit roles only.</summary>
    internal static class FrontKit
    {
        public const float HeaderH = 22f;

        /// <summary>A fixed-size mono line in a box; call <see cref="Set"/> to change it.</summary>
        public static TMP_Text Mono(RectTransform p, string name, float x, float y, float w, float h, float size, TextAlignmentOptions a,
            bool strong = false, float tracking = 0f)
        {
            TMP_Text t = C2Kit.Mono(p, name, size, a, strong, tracking);
            AvLay.Place(t.rectTransform, x, y, w, h);
            t.color = AvInk.Ink;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A fixed-size condensed (heading face) line.</summary>
        public static TMP_Text Cond(RectTransform p, string name, float x, float y, float w, float h, float size, TextAlignmentOptions a,
            AvTextRole role = AvTextRole.Head)
        {
            TMP_Text t = C2Kit.Cond(p, name, role, size, a);
            AvLay.Place(t.rectTransform, x, y, w, h);
            t.color = AvInk.Ink;
            t.raycastTarget = false;
            return t;
        }

        public static void Set(TMP_Text t, string s, Color c)
        {
            AvText.Set(t, s);
            if (t.color != c) t.color = c;
        }

        public static Image Solid(RectTransform p, string name, float x, float y, float w, float h, Color c)
        {
            Image i = AvLay.Solid(p, name, c);
            AvLay.Place(i.rectTransform, x, y, w, h);
            return i;
        }

        public static AvFrame Panel(RectTransform p, string name, float x, float y, float w, float h, float chamfer = 0f, float bracket = 0f)
        {
            AvFrame f = AvFrame.Add(p, name, chamfer > 0f ? AvChamfer.Diagonal(chamfer) : default(AvChamfer));
            f.Bracket = bracket;
            AvLay.Place(f.rectTransform, x, y, w, h);
            return f;
        }

        /// <summary>m:ss.</summary>
        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        public static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

        /// <summary>The ink a state word wears (ready green, caution amber, danger red, info neutral).</summary>
        public static Color Tone(AvState s) => s == AvState.Inert ? AvInk.Muted : s == AvState.Info ? AvInk.Key : OpsInk.Word(s);
    }

    /// <summary>
    /// A titled box: chamfered frame with brackets, a header band (icon, stencil title, right-hand meta) and a body rect.
    /// The same chrome frames every section of the rail and every pane of a room.
    /// </summary>
    internal sealed class FrontBox
    {
        private readonly AvFrame frame;
        private readonly Image band, rule, rail;
        private readonly TMP_Text title, meta, icon;
        private Color metaTone;

        public RectTransform Rect { get; }
        public float Width { get; }
        public float Height { get; }

        public FrontBox(RectTransform parent, FrontSkin skin, string name, float x, float y, float w, float h, string heading, AvIcon glyph = AvIcon.None)
        {
            Width = w; Height = h;
            Rect = AvLay.Child(parent, "Box " + name);
            AvLay.Place(Rect, x, y, w, h);
            frame = FrontKit.Panel(Rect, "Frame", 0f, 0f, w, h, 6f, 7f);
            band = FrontKit.Solid(Rect, "Band", 1f, 1f, w - 2f, FrontKit.HeaderH - 1f, Color.clear);
            rail = FrontKit.Solid(Rect, "Rail", 1f, 1f, 3f, FrontKit.HeaderH - 1f, Color.clear);
            rule = FrontKit.Solid(Rect, "Rule", 1f, FrontKit.HeaderH, w - 2f, 1f, Color.clear);
            float tx = 10f;
            if (glyph != AvIcon.None)
            {
                icon = AvIcons.Make(Rect, glyph, 14f, AvInk.Dim);
                AvLay.Place(icon.rectTransform, 8f, 4f, 14f, 14f);
                tx = 28f;
            }
            title = FrontKit.Cond(Rect, "Title", tx, 0f, w * 0.55f, FrontKit.HeaderH, 12.5f, TextAlignmentOptions.MidlineLeft);
            title.characterSpacing = 3f;
            title.text = heading;
            meta = FrontKit.Mono(Rect, "Meta", w * 0.45f - 8f, 0f, w * 0.55f, FrontKit.HeaderH, 10.5f, TextAlignmentOptions.MidlineRight);
            skin.Add(Restyle);
        }

        public void SetMeta(string text, Color tone) { metaTone = tone; FrontKit.Set(meta, text, tone); }

        private void Restyle()
        {
            frame.Paint(AvInk.Surface.WithAlpha(0.62f), AvInk.Hairline);
            frame.BracketColor = AvInk.Frame.WithAlpha(0.8f);
            band.color = AvInk.Raised.WithAlpha(0.7f);
            rail.color = AvInk.Select;
            rule.color = AvInk.Frame.WithAlpha(0.55f);
            title.color = AvInk.Ink;
            if (icon != null) icon.color = AvInk.Dim;
            if (meta != null && metaTone.a > 0f) meta.color = metaTone;
            else if (meta != null) meta.color = AvInk.Dim;
        }
    }

    /// <summary>A flat progress bar: dark well, coloured fill, optional end ticks. Fill and colour are set per paint.</summary>
    internal sealed class FrontBar
    {
        private readonly Image well, fill;
        private readonly float w, h;
        private float shown = -1f;
        private Color tone = Color.clear;

        public FrontBar(RectTransform parent, FrontSkin skin, float x, float y, float width, float height)
        {
            w = width; h = height;
            well = FrontKit.Solid(parent, "BarWell", x, y, width, height, Color.clear);
            fill = FrontKit.Solid(parent, "BarFill", x + 1f, y + 1f, 0f, height - 2f, Color.clear);
            skin.Add(() => well.color = AvInk.Ground.WithAlpha(0.92f));
        }

        public void Set(float fraction, Color color)
        {
            fraction = Mathf.Clamp01(fraction);
            if (Mathf.Abs(fraction - shown) > 0.0005f)
            {
                shown = fraction;
                fill.rectTransform.sizeDelta = new Vector2(Mathf.Round((w - 2f) * fraction), h - 2f);
            }
            if (tone != color) { tone = color; fill.color = color; }
        }

        public void Show(bool on)
        {
            if (well.gameObject.activeSelf != on) { well.gameObject.SetActive(on); fill.gameObject.SetActive(on); }
        }
    }
}
