using System;
using System.Collections.Generic;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>How a text sits on its anchor point.</summary>
    internal enum ScopeAlign : byte { Left, Center, Right }

    /// <summary>
    /// Every word of one scope surface: a pool of TMP labels in the native HUD font, reused frame to frame in draw order (a property
    /// or the characters are only written when they changed, so a steady frame allocates nothing and re-meshes nothing).
    /// Positions are PIXELS of the surface (origin top-left, y down), sizes are the em of the design size in pixels. The
    /// prototype's face advances 0.65 em per character (B612 Mono: 7.8 px at 12): the pool measures the native font once and sizes its
    /// labels so every character advances exactly that, so a column budget worked out in the prototype holds here.
    /// </summary>
    internal sealed class ScopeText
    {
        /// <summary>Advance of one character in em of the design size: the prototype's mono face.</summary>
        public const float DesignAdvance = 0.65f;

        private struct Slot
        {
            public TextMeshProUGUI Text;
            public char[] Chars;
            public int Length;
            public float Size, Spacing, X, Y, W, H;
            public Color Color;
            public TextAlignmentOptions Alignment;
            public bool Bold;
        }

        private readonly RectTransform parent;
        private readonly TMP_FontAsset font;
        private readonly Material material;
        private readonly List<Slot> slots = new List<Slot>(64);
        private float advance = -1f, boldSpacing;
        private int used;

        public ScopeText(RectTransform parent, TMP_FontAsset font, Material material)
        {
            this.parent = parent;
            this.font = font;
            this.material = material;
        }

        public void Begin() => used = 0;

        public void End()
        {
            for (int i = used; i < slots.Count; i++)
                if (slots[i].Text.gameObject.activeSelf) slots[i].Text.gameObject.SetActive(false);
        }

        /// <summary>
        /// One word. <paramref name="x"/> is the anchor (left edge, centre or right edge by <paramref name="align"/>), <paramref name="yMid"/> the
        /// middle of the line; <paramref name="size"/> the design size in pixels (12 px at 1080p is 12, at 720p 8).
        /// </summary>
        public void Draw(ReadOnlySpan<char> text, float x, float yMid, float size, ScopeAlign align, Color color, bool bold)
        {
            if (text.Length == 0 || color.a <= 0.001f) return;
            Measure();
            Slot s;
            if (used < slots.Count) s = slots[used];
            else
            {
                s = new Slot { Text = NewLabel(), Chars = new char[32], Size = -1f, Alignment = (TextAlignmentOptions)(-1), Spacing = float.NaN, X = float.NaN };
                s.Text.gameObject.SetActive(false);
            }
            if (!s.Text.gameObject.activeSelf) s.Text.gameObject.SetActive(true);

            float fontSize = size * DesignAdvance / advance;
            if (s.Size != fontSize) { s.Text.fontSize = fontSize; s.Size = fontSize; }
            float spacing = bold ? boldSpacing : 0f;
            if (s.Bold != bold || float.IsNaN(s.Spacing)) { s.Text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal; s.Bold = bold; }
            if (s.Spacing != spacing) { s.Text.characterSpacing = spacing; s.Spacing = spacing; }
            TextAlignmentOptions alignment = align == ScopeAlign.Left ? TextAlignmentOptions.MidlineLeft : align == ScopeAlign.Right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Midline;
            if (s.Alignment != alignment) { s.Text.alignment = alignment; s.Alignment = alignment; }
            if (s.Color != color) { s.Text.color = color; s.Color = color; }

            if (s.Length != text.Length || !text.SequenceEqual(new ReadOnlySpan<char>(s.Chars, 0, s.Length)))
            {
                if (s.Chars.Length < text.Length) s.Chars = new char[Math.Max(text.Length, s.Chars.Length * 2)];
                text.CopyTo(s.Chars);
                s.Length = text.Length;
                s.Text.SetCharArray(s.Chars, 0, s.Length);
            }

            // The rect only matters for alignment: wide enough for the text, anchored at its left, centre or right.
            float w = text.Length * DesignAdvance * size * 1.1f + 6f, h = size * 1.5f;
            float left = align == ScopeAlign.Left ? x : align == ScopeAlign.Right ? x - w : x - w * 0.5f, top = yMid - h * 0.5f;
            if (s.X != left || s.Y != top || s.W != w || s.H != h)
            {
                RectTransform rt = s.Text.rectTransform;
                rt.anchoredPosition = new Vector2(left, -top);
                rt.sizeDelta = new Vector2(w, h);
                s.X = left; s.Y = top; s.W = w; s.H = h;
            }
            if (used < slots.Count) slots[used] = s; else slots.Add(s);
            used++;
        }

        /// <summary>Hide everything and drop the labels' objects (the surface is being rebuilt or destroyed).</summary>
        public void Destroy()
        {
            foreach (Slot s in slots) if (s.Text != null) UnityEngine.Object.Destroy(s.Text.gameObject);
            slots.Clear();
            used = 0;
        }

        /// <summary>Measures the font once: how far a character advances per unit of font size, and the spacing that cancels TMP's synthetic-bold widening.</summary>
        private void Measure()
        {
            if (advance > 0f) return;
            TextMeshProUGUI probe = NewLabel();
            probe.fontSize = 100f;
            const string Row = "MMMMMMMMMMMMMMMMMMMM";
            float normal = probe.GetPreferredValues(Row, 0f, 0f).x / Row.Length;
            probe.fontStyle = FontStyles.Bold;
            float bold = probe.GetPreferredValues(Row, 0f, 0f).x / Row.Length;
            UnityEngine.Object.Destroy(probe.gameObject);
            if (normal < 1f) normal = 60f;
            advance = normal / 100f;
            // characterSpacing is in hundredths of an em and both advances were measured at size 100: the difference brings bold back to regular.
            boldSpacing = Mathf.Clamp(normal - bold, -20f, 0f);
        }

        private TextMeshProUGUI NewLabel()
        {
            var go = new GameObject("Word", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.font = font;
            if (material != null) label.fontSharedMaterial = material;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
            label.raycastTarget = false;
            label.enableAutoSizing = false;
            label.enableKerning = false;
            label.isTextObjectScaleStatic = true;
            return label;
        }
    }
}
