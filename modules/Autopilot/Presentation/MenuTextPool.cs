using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Presentation
{
    /// <summary>
    /// Every text of the menu: a pool of TMP labels on the kit's mono face (reused frame to frame, a property is only written when it
    /// changed) and the measuring the layout needs (<see cref="IMenuMetrics"/>). Rects are menu pixels, origin top-left; the pool flips y.
    /// </summary>
    internal sealed class MenuTextPool : IMenuMetrics
    {
        public enum Align : byte { Left, Center, Right }

        private struct Slot
        {
            public TMP_Text Text;
            public string Last;
            public float Size, Spacing, Lines, X, Y, W, H;
            public Color Color;
            public TextAlignmentOptions Alignment;
            public bool Strong;
        }

        /// <summary>Plates, rows, headers, title and hint carry the kit's bold mono (the prototype's face reads heavier than the regular cut); state lines, badges and group headings stay regular.</summary>
        private static bool Strong(MenuFont f) => f != MenuFont.Badge && f != MenuFont.State && f != MenuFont.Group;

        private readonly RectTransform parent;
        private readonly List<Slot> slots = new List<Slot>(96);
        private readonly Dictionary<(MenuFont, string), float> widths = new Dictionary<(MenuFont, string), float>(256);
        private TMP_Text probe;
        private int used;
        private float screenHeight;

        public MenuTextPool(RectTransform parent) { this.parent = parent; }

        // ---- measuring ---------------------------------------------------------------------------------------------------
        public float Width(string text, MenuFont font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            if (widths.TryGetValue((font, text), out float cached)) return cached;
            Probe(font);
            // TMP drops leading and trailing blanks from a preferred width (a lone space measures 0), so a blank-edged text is bracketed
            bool blank = text[0] == ' ' || text[text.Length - 1] == ' ';
            float raw = blank ? probe.GetPreferredValues("|" + text + "|", 0f, 0f).x - probe.GetPreferredValues("||", 0f, 0f).x : probe.GetPreferredValues(text, 0f, 0f).x;
            // 2% of slack: TMP lays the real label out with its own rounding, and a plate sized to the exact width would truncate it
            float w = raw * 1.02f;
            widths[(font, text)] = w;
            return w;
        }

        private void Probe(MenuFont font)
        {
            if (probe == null)
            {
                probe = NewLabel("Probe");
                probe.gameObject.SetActive(false);
                probe.enableWordWrapping = false;
            }
            probe.font = Face(Strong(font));
            probe.fontSize = MenuFonts.Size(font);
            probe.characterSpacing = MenuFonts.Tracking(font) * 100f;
        }

        private static TMP_FontAsset Face(bool strong)
        {
            TMP_FontAsset face = AvType.Face(strong ? AvFace.MonoStrong : AvFace.Mono);
            return face != null ? face : TMP_Settings.defaultFontAsset;
        }

        private readonly Dictionary<MenuFont, float> naturalLines = new Dictionary<MenuFont, float>(8);
        private readonly Dictionary<MenuFont, float> lineSpacings = new Dictionary<MenuFont, float>(8);

        /// <summary>The face's own line pitch as a multiple of the font size (before <see cref="MenuFonts.LinePitch"/> is forced).</summary>
        private float NaturalLine(MenuFont font)
        {
            if (naturalLines.TryGetValue(font, out float cached)) return cached;
            Probe(font);
            probe.lineSpacing = 0f;
            float pitch = (probe.GetPreferredValues("A\nA", 0f, 0f).y - probe.GetPreferredValues("A", 0f, 0f).y) / probe.fontSize;
            naturalLines[font] = pitch;
            return pitch;
        }

        /// <summary>TMP lineSpacing (font units) that brings the face's line pitch to <see cref="MenuFonts.LinePitch"/>; measured once, not assumed.</summary>
        private float LineSpacing(MenuFont font)
        {
            if (lineSpacings.TryGetValue(font, out float cached)) return cached;
            float natural = NaturalLine(font), spacing = 0f;
            if (natural > MenuFonts.LinePitch)
            {
                Probe(font);
                probe.lineSpacing = -10f;
                float tighter = (probe.GetPreferredValues("A\nA", 0f, 0f).y - probe.GetPreferredValues("A", 0f, 0f).y) / probe.fontSize;
                probe.lineSpacing = 0f;
                float perUnit = (natural - tighter) / 10f;
                if (perUnit > 1e-5f) spacing = -(natural - MenuFonts.LinePitch) / perUnit;
            }
            lineSpacings[font] = spacing;
            return spacing;
        }

        // ---- drawing -----------------------------------------------------------------------------------------------------
        public void Begin(float screenH) { used = 0; screenHeight = screenH; }

        /// <summary>Sets one label. <paramref name="size"/> is in pixels already scaled.</summary>
        public void Draw(string text, MenuFont font, float size, MenuRect r, Align align, Color color, bool top = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            // TMP flags a label as truncated when its rect is shorter than its lines: give every label its real line pitch times its lines
            // (and a little air), grown around its centre (or downward for top-aligned blocks)
            int lineCount = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lineCount++;
            float minH = size * (NaturalLine(font) + MenuFonts.LinePitch * (lineCount - 1) + 0.1f);
            if (r.H < minH)
            {
                float mid = (r.T + r.B) / 2f;
                r = top ? new MenuRect(r.L, r.T, r.R, r.T + minH) : new MenuRect(r.L, mid - minH / 2f, r.R, mid + minH / 2f);
            }
            Slot s;
            if (used < slots.Count) s = slots[used];
            else s = new Slot { Text = NewLabel("Label" + slots.Count), Size = -1f };
            TextAlignmentOptions alignment = top
                ? (align == Align.Left ? TextAlignmentOptions.TopLeft : align == Align.Right ? TextAlignmentOptions.TopRight : TextAlignmentOptions.Top)
                : (align == Align.Left ? TextAlignmentOptions.MidlineLeft : align == Align.Right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Midline);
            float spacing = MenuFonts.Tracking(font) * 100f;
            if (!s.Text.gameObject.activeSelf) s.Text.gameObject.SetActive(true);
            if (s.Size != size) { s.Text.fontSize = size; s.Size = size; }
            if (s.Spacing != spacing) { s.Text.characterSpacing = spacing; s.Spacing = spacing; }
            float lines = LineSpacing(font);
            if (s.Lines != lines) { s.Text.lineSpacing = lines; s.Lines = lines; }
            bool strong = Strong(font);
            if (s.Strong != strong) { s.Text.font = Face(strong); s.Strong = strong; }
            if (s.Alignment != alignment) { s.Text.alignment = alignment; s.Alignment = alignment; }
            if (s.Color != color) { s.Text.color = color; s.Color = color; }
            if (s.Last != text) { s.Text.text = text; s.Last = text; }
            float y = screenHeight - r.B;
            if (s.X != r.L || s.Y != y || s.W != r.W || s.H != r.H)
            {
                var rt = s.Text.rectTransform;
                rt.anchoredPosition = new Vector2(r.L, y);
                rt.sizeDelta = new Vector2(r.W, r.H);
                s.X = r.L; s.Y = y; s.W = r.W; s.H = r.H;
            }
            if (used < slots.Count) slots[used] = s; else slots.Add(s);
            used++;
        }

        public void End()
        {
            for (int i = used; i < slots.Count; i++)
                if (slots[i].Text.gameObject.activeSelf) slots[i].Text.gameObject.SetActive(false);
        }

        public void HideAll() { used = 0; End(); }

        // ---- construction ---------------------------------------------------------------------------------------------------
        private TMP_Text NewLabel(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = Face(false);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = false;
            label.raycastTarget = false;
            label.enableAutoSizing = false;
            label.isTextObjectScaleStatic = true;
            return label;
        }
    }
}
