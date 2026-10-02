using NOAvionics;
using System;
using BoscaliSummer.Modules.Progression.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>State colours for the local parts: rail hue, value ink, and the selection accent.</summary>
    internal static class SqdTone
    {
        public static Color Rail(AvState state)
        {
            switch (state)
            {
                case AvState.Ready: return AvTheme.RailReady;
                case AvState.Caution: return AvTheme.RailCaution;
                case AvState.Danger: return AvTheme.RailDanger;
                case AvState.Info: return AvTheme.RailInfo;
                default: return AvTheme.RailInert;
            }
        }

        public static Color Text(AvState state)
        {
            if (state == AvState.Info)
                return AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip info").Color, AvTheme.RailInfo);
            return AvStyleHost.Resolve(AvStyleHost.FuiStyle(
                state == AvState.Inert ? "row-value" : "row-value " + AvStates.Class(state)).Color, AvTheme.TextPrimary);
        }

        public static Color Select =>
            AvStyleHost.Resolve(AvStyleHost.FuiStyle("cell", "on").Border, AvTheme.Selected);

        public static Color Ink => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
        public static Color Dim => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        public static Color Key => AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-title").Color, AvTheme.RailInfo);
        public static Color Caption => AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Disabled);

        /// <summary>Tabler glyph that says the state without colour.</summary>
        public static AvIcon Glyph(AvState state)
        {
            switch (state)
            {
                case AvState.Ready: return AvIcon.CircleCheck;
                case AvState.Caution: return AvIcon.AlertTriangle;
                case AvState.Danger: return AvIcon.AlertCircle;
                default: return AvIcon.Circle;
            }
        }
    }

    /// <summary>
    /// Small local kit v2 parts (built from <c>AvPart</c>/<c>AvFrame</c>/<c>AvText</c>/<c>AvLay</c>)
    /// that the SQD console's pages share. Every one of them reports <see cref="AvPart.Changed"/>
    /// when its text can change its height, so a value that arrives after the first layout never
    /// spills into the part below it.
    /// </summary>
    internal sealed class AvTextBlock : AvPart
    {
        private readonly TMP_Text text;

        public AvTextBlock(RectTransform parent, AvTextRole role = AvTextRole.ProseSmall)
        {
            Rect = AvLay.Child(parent, "Text");
            text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public Color Color { set => text.color = value; }

        public void Set(string value)
        {
            string v = value ?? "";
            if (text.text == v) return;
            text.text = v;
            Changed();
        }

        public override float Measure(float width) => Mathf.Max(14f, AvText.Height(text, width));

        public override void Place(AvSlot slot) { base.Place(slot); AvLay.Fill(text.rectTransform); }

        public override void Restyle() => text.color = SqdTone.Dim;
    }

    /// <summary>A small read-only stat: caption over a mono value. Used for compact tile rows.</summary>
    internal sealed class AvStatTile : AvPart
    {
        private readonly AvFrame frame;
        private readonly TMP_Text caption, value;
        private AvState state = AvState.Inert;

        public AvStatTile(RectTransform parent, string captionText)
        {
            Rect = AvLay.Child(parent, "Tile " + captionText);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(5f)); AvLay.Fill(frame.rectTransform);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, captionText ?? "");
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "—");
            AvText.Fit(caption, false); AvText.Fit(value, false);
            Restyle();
        }

        public void Set(string v, AvState st = AvState.Inert)
        {
            string body = string.IsNullOrEmpty(v) ? "—" : v;
            if (value.text != body) value.text = body;
            if (st != state) { state = st; Restyle(); }
        }

        public override float Measure(float width) => AvGridTokens.ToolCell;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(caption.rectTransform, 8f, 4f, s.W - 16f, 14f);
            AvLay.Place(value.rectTransform, 8f, 19f, s.W - 16f, s.H - 23f);
        }

        public override void Restyle()
        {
            frame.Paint(AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Background, AvTheme.SurfaceInert),
                AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Border, AvTheme.Hairline));
            caption.color = SqdTone.Caption;
            value.color = SqdTone.Text(state);
        }
    }

    /// <summary>A framed portrait box: an <see cref="Image"/> with a NO VISUAL fallback word.</summary>
    internal sealed class AvPortrait : AvPart
    {
        private readonly AvFrame frame;
        private readonly Image image;
        private readonly TMP_Text fallback;
        private readonly float height;

        public AvPortrait(RectTransform parent, string name, string fallbackWord = "NO VISUAL",
            float measuredHeight = AvGridTokens.Metric)
        {
            height = measuredHeight;
            Rect = AvLay.Child(parent, "Portrait " + name);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(4f)); AvLay.Fill(frame.rectTransform);
            var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            image = go.AddComponent<Image>();
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            fallback = AvText.Make(Rect, "Fallback", AvTextRole.Micro, fallbackWord, TextAlignmentOptions.Center, true);
            Restyle();
        }

        private Vector2 area;

        /// <summary>Hover help shown in the console footer while the pointer is over the frame.</summary>
        public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value); } }

        public void Set(Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
            fallback.gameObject.SetActive(sprite == null);
            PlaceImage();
        }

        /// <summary>The sprite is fitted inside the frame and centred (a corner-pivoted image would hug the top-left).</summary>
        private void PlaceImage()
        {
            float w = area.x, h = area.y;
            Sprite sprite = image.sprite;
            if (sprite != null && sprite.rect.height > 0f && w > 0f && h > 0f)
            {
                float aspect = sprite.rect.width / sprite.rect.height;
                if (w / h > aspect) w = h * aspect; else h = w / aspect;
            }
            AvLay.Place((RectTransform)image.transform, 3f + (area.x - w) * .5f, 3f + (area.y - h) * .5f, w, h);
        }

        public override float Measure(float width) => height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            area = new Vector2(Mathf.Max(0f, s.W - 6f), Mathf.Max(0f, s.H - 6f));
            PlaceImage();
            AvLay.Place(fallback.rectTransform, 4f, 4f, s.W - 8f, s.H - 8f);
        }

        public override void Restyle()
        {
            frame.Paint(AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Background, AvTheme.SurfaceInert),
                AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Border, AvTheme.Hairline));
            fallback.color = SqdTone.Caption;
        }
    }

    /// <summary>A thin progress bar drawn from two solid images; placed by its owning part.</summary>
    internal sealed class SqdBar
    {
        private readonly Image track, fill;
        private float x, y, w, h, fraction;

        public SqdBar(RectTransform parent, string name)
        {
            track = AvLay.Solid(parent, name + " Track", Color.clear);
            fill = AvLay.Solid(parent, name + " Fill", Color.clear);
            Restyle();
        }

        public void Place(float px, float py, float pw, float ph)
        {
            x = px; y = py; w = pw; h = ph;
            Apply();
        }

        public void Set(float value, Color color)
        {
            fraction = Mathf.Clamp01(value);
            fill.color = color;
            Apply();
        }

        public void SetShown(bool shown) { track.gameObject.SetActive(shown); fill.gameObject.SetActive(shown); }

        private void Apply()
        {
            AvLay.Place(track.rectTransform, x, y, w, h);
            AvLay.Place(fill.rectTransform, x, y, Mathf.Round(w * fraction), h);
        }

        public void Restyle() => track.color = AvTheme.Hairline;
    }

    /// <summary>
    /// A prose block set as a quotation: a rail at the left and the text measured at the
    /// width it will really be drawn at. Used for the pilot's service note.
    /// </summary>
    internal sealed class SqdQuote : AvPart
    {
        private const float PadLeft = 16f, PadRight = 8f, PadY = 8f;
        private readonly AvFrame frame;
        private readonly Image bar;
        private readonly TMP_Text text;

        public SqdQuote(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Quote");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer)); AvLay.Fill(frame.rectTransform);
            bar = AvLay.Solid(Rect, "Bar", Color.clear);
            text = AvText.Make(Rect, "Text", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public void Set(string value)
        {
            string v = value ?? "";
            if (text.text == v) return;
            text.text = v;
            Changed();
        }

        public override float Measure(float width) =>
            Mathf.Max(AvGridTokens.Row, PadY + AvText.Height(text, width - PadLeft - PadRight) + PadY);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(bar.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(text.rectTransform, PadLeft, PadY, s.W - PadLeft - PadRight, s.H - 2f * PadY);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), Color.clear);
            bar.color = AvTheme.RailInfo;
            text.color = SqdTone.Ink;
        }
    }

    /// <summary>
    /// One compact card for "nothing here yet": an icon, what is missing, what to do, and
    /// optionally the button that does it. Replaces empty section bodies and stray captions.
    /// </summary>
    internal sealed class SqdEmptyCard : AvPart
    {
        private const float PadX = 12f, PadY = 10f, IconW = 20f, ActionH = 26f;
        private readonly AvFrame frame;
        private readonly TMP_Text icon, title, hint;
        private readonly AvControl action;

        public SqdEmptyCard(RectTransform parent, AvIcon glyph, string titleText, string hintText,
            string actionLabel = null, Action onAction = null, AvIcon actionIcon = AvIcon.None)
        {
            Rect = AvLay.Child(parent, "Empty " + titleText);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f)); AvLay.Fill(frame.rectTransform);
            icon = AvIcons.Make(Rect, glyph, AvGridTokens.IconTool, Color.white);
            title = AvText.Make(Rect, "Title", AvTextRole.Label, titleText, TextAlignmentOptions.TopLeft, true);
            hint = AvText.Make(Rect, "Hint", AvTextRole.ProseSmall, hintText, TextAlignmentOptions.TopLeft, true);
            if (!string.IsNullOrEmpty(actionLabel))
                action = AvControl.Make(Rect, new AvControl.Spec(actionLabel, onAction, AvButtonStyle.Quiet, actionIcon));
            Restyle();
        }

        public void Set(string titleText, string hintText)
        {
            bool grew = false;
            if (title.text != (titleText ?? "")) { title.text = titleText ?? ""; grew = true; }
            if (hint.text != (hintText ?? "")) { hint.text = hintText ?? ""; grew = true; }
            if (grew) Changed();
        }

        private float TextWidth(float width) => width - 2f * PadX - IconW - 8f;

        public override float Measure(float width)
        {
            float w = TextWidth(width);
            float h = PadY + AvText.Height(title, w) + (hint.text.Length > 0 ? 3f + AvText.Height(hint, w) : 0f);
            if (action != null) h += 8f + ActionH;
            return Mathf.Max(AvGridTokens.Row + 8f, h + PadY);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextWidth(s.W), x = PadX + IconW + 8f;
            float th = AvText.Height(title, w);
            float hh = hint.text.Length > 0 ? AvText.Height(hint, w) : 0f;
            float content = th + (hh > 0f ? 3f + hh : 0f) + (action != null ? 8f + ActionH : 0f);
            float top = Mathf.Max(PadY, (s.H - content) * 0.5f);
            AvLay.Place(icon.rectTransform, PadX, top - 1f, IconW, IconW);
            AvLay.Place(title.rectTransform, x, top, w, th);
            AvLay.Place(hint.rectTransform, x, top + th + 3f, w, hh);
            if (action != null)
                AvLay.Place(action.Rect, x, top + th + (hh > 0f ? 3f + hh : 0f) + 8f, Mathf.Min(170f, w), ActionH);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            icon.color = SqdTone.Caption;
            title.color = SqdTone.Ink;
            hint.color = SqdTone.Dim;
            action?.Restyle();
        }
    }

    /// <summary>
    /// A compact two-column readout grid inside one frame: a small caps label over a mono
    /// value in every cell, with hairline dividers. Replaces long label/value tables.
    /// </summary>
    internal sealed class SqdStatGrid : AvPart
    {
        private const float CellH = 40f, PadX = 12f;
        private readonly int Columns;
        private readonly AvFrame frame;
        private readonly System.Collections.Generic.List<TMP_Text> keys = new System.Collections.Generic.List<TMP_Text>(12);
        private readonly System.Collections.Generic.List<TMP_Text> values = new System.Collections.Generic.List<TMP_Text>(12);
        private readonly System.Collections.Generic.List<AvState> states = new System.Collections.Generic.List<AvState>(12);
        private readonly System.Collections.Generic.List<Image> rules = new System.Collections.Generic.List<Image>(8);
        private readonly System.Collections.Generic.List<Image> dividers = new System.Collections.Generic.List<Image>(3);

        /// <summary>Hover help for the whole grid (a grid is one frame, so one tip covers every cell).</summary>
        public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value); } }

        public SqdStatGrid(RectTransform parent, int columns = 2)
        {
            Columns = Mathf.Clamp(columns, 1, 4);
            Rect = AvLay.Child(parent, "StatGrid");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f)); AvLay.Fill(frame.rectTransform);
            for (int i = 0; i < Columns - 1; i++) dividers.Add(AvLay.Solid(Rect, "Divider", Color.clear));
            Restyle();
        }

        public int Count => keys.Count;

        public int Add(string label)
        {
            TMP_Text key = AvText.Make(Rect, "Key " + label, AvTextRole.Micro, label ?? "");
            TMP_Text value = AvText.Make(Rect, "Value " + label, AvTextRole.DataStrong, "—");
            AvText.Fit(key, false); AvText.Fit(value, false);
            keys.Add(key); values.Add(value); states.Add(AvState.Inert);
            if (keys.Count > Columns && (keys.Count - 1) % Columns == 0) rules.Add(AvLay.Solid(Rect, "Rule", Color.clear));
            Restyle();
            Changed();
            return keys.Count - 1;
        }

        public void Set(int index, string value, AvState state = AvState.Inert)
        {
            if (index < 0 || index >= values.Count) return;
            string body = string.IsNullOrEmpty(value) ? "—" : value;
            if (values[index].text != body) values[index].text = body;
            if (states[index] != state) { states[index] = state; values[index].color = SqdTone.Text(state); }
        }

        private int Rows => (keys.Count + Columns - 1) / Columns;

        public override float Measure(float width) => Mathf.Max(CellH, Rows * CellH);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float col = s.W / Columns;
            for (int i = 0; i < keys.Count; i++)
            {
                float x = (i % Columns) * col + PadX, y = (i / Columns) * CellH;
                AvLay.Place(keys[i].rectTransform, x, y + 5f, col - PadX - 6f, 14f);
                AvLay.Place(values[i].rectTransform, x, y + 19f, col - PadX - 6f, 18f);
            }
            for (int d = 0; d < dividers.Count; d++)
                AvLay.Place(dividers[d].rectTransform, col * (d + 1), 6f, 1f, Mathf.Max(0f, s.H - 12f));
            for (int r = 0; r < rules.Count; r++)
                AvLay.Place(rules[r].rectTransform, 8f, (r + 1) * CellH, s.W - 16f, 1f);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            foreach (Image divider in dividers) divider.color = AvTheme.Hairline;
            foreach (Image rule in rules) rule.color = AvTheme.Hairline;
            for (int i = 0; i < keys.Count; i++)
            {
                keys[i].color = SqdTone.Caption;
                values[i].color = SqdTone.Text(states[i]);
            }
        }
    }

    /// <summary>
    /// One roster line: state rail, optional portrait thumb, a name, a wrapped sub line, and a
    /// right-hand value with its own sub word. Every text is placed at construction-independent
    /// slots on each layout, so a value that arrives late can never float unplaced. Replaces
    /// AvRow where the right column is filled in after the first layout.
    /// </summary>
    internal sealed class SqdRosterRow : AvPart
    {
        private const float PadX = 10f, PadY = 6f, ThumbW = 34f, ThumbH = 44f, MaxRight = 150f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly AvPortrait thumb;
        private readonly TMP_Text title, sub, value, valueSub;
        private readonly SqdBar meter;
        private AvState state = AvState.Inert;
        private bool hover, armed, interactable = true, hasMeter;

        public SqdRosterRow(RectTransform parent, bool withThumb = false, Action onClick = null)
        {
            Rect = AvLay.Child(parent, "RosterRow");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer)); AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            if (withThumb) thumb = new AvPortrait(Rect, "Thumb", "");
            title = AvText.Make(Rect, "Title", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
            sub = AvText.Make(Rect, "Sub", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.TopRight);
            valueSub = AvText.Make(Rect, "ValueSub", AvTextRole.Micro, "", TextAlignmentOptions.TopRight);
            AvText.Fit(value, false); AvText.Fit(valueSub, false);
            meter = new SqdBar(Rect, "Meter");
            meter.SetShown(false);
            if (onClick != null)
            {
                AvHit hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => onClick();
            }
            Restyle();
        }

        /// <summary>Hover help shown in the console footer.</summary>
        public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value); } }

        public bool Armed { get => armed; set { if (armed == value) return; armed = value; Restyle(); } }

        public void SetThumb(Sprite sprite) => thumb?.Set(sprite);

        /// <summary>A thin condition bar along the bottom edge; null hides it.</summary>
        public void SetMeter(float? fraction, Color color)
        {
            bool show = fraction.HasValue;
            if (show != hasMeter) { hasMeter = show; meter.SetShown(show); Changed(); }
            if (show) meter.Set(fraction.Value, color);
        }

        public void Set(string titleText, string subText, string valueText, string valueSubText, AvState st)
        {
            bool grew = false;
            if (title.text != (titleText ?? "")) { title.text = titleText ?? ""; grew = true; }
            if (sub.text != (subText ?? "")) { sub.text = subText ?? ""; grew = true; }
            if (value.text != (valueText ?? "")) { value.text = valueText ?? ""; grew = true; }
            if (valueSub.text != (valueSubText ?? "")) { valueSub.text = valueSubText ?? ""; grew = true; }
            if (st != state) { state = st; Restyle(); }
            if (grew) Changed();
        }

        private float RightWidth() =>
            Mathf.Min(MaxRight, Mathf.Max(value.text.Length > 0 ? AvText.Width(value) : 0f,
                valueSub.text.Length > 0 ? AvText.Width(valueSub) : 0f));

        private float LeftInset => PadX + 4f + (thumb != null ? ThumbW + 8f : 0f);

        private float TextWidth(float width)
        {
            float right = RightWidth();
            return width - LeftInset - PadX - (right > 0f ? right + 8f : 0f);
        }

        public override float Measure(float width)
        {
            float tw = TextWidth(width);
            float h = PadY + AvText.Height(title, tw) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, tw) : 0f) + PadY;
            float rightH = PadY + (value.text.Length > 0 ? 18f : 0f) + (valueSub.text.Length > 0 ? 14f : 0f) + PadY;
            float min = thumb != null ? ThumbH + 2f * PadY : AvGridTokens.Row + 4f;
            return Mathf.Max(min, Mathf.Max(h, rightH)) + (hasMeter ? 6f : 0f);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float right = RightWidth();
            float tw = TextWidth(s.W);
            float x0 = LeftInset;
            float th = AvText.Height(title, tw);
            AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
            if (thumb != null) thumb.Place(new AvSlot(PadX + 2f, PadY, ThumbW, s.H - 2f * PadY - (hasMeter ? 6f : 0f)));
            AvLay.Place(title.rectTransform, x0, PadY, tw, th);
            AvLay.Place(sub.rectTransform, x0, PadY + th + 2f, tw, AvText.Height(sub, tw));
            float rx = s.W - PadX - right;
            AvLay.Place(value.rectTransform, rx, PadY, right, 18f);
            AvLay.Place(valueSub.rectTransform, rx, PadY + (value.text.Length > 0 ? 18f : 0f), right, 14f);
            meter.Place(PadX + 4f, s.H - 7f, s.W - 2f * PadX - 4f, 3f);
        }

        public override void Restyle()
        {
            string st = !interactable ? "disabled" : armed ? "armed" : hover ? "hover" : null;
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state), st);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
            title.color = SqdTone.Ink;
            sub.color = SqdTone.Dim;
            value.color = SqdTone.Text(state);
            valueSub.color = SqdTone.Dim;
            meter.Restyle();
            thumb?.Restyle();
        }
    }

    /// <summary>
    /// The PILOT page's skills summary: one row per qualification lane, its grades as pips (held /
    /// not yet), the lane name at the left and the count at the right. Rows share the part's height
    /// (within a cap), so the part can take a page's leftover height without stretching one giant cell.
    /// </summary>
    internal sealed class SkillLaneStrip : AvPart
    {
        private const float GapY = 4f, MinRow = 30f, NameW = 112f, CountW = 40f, PadX = 10f, PipGap = 4f;
        private readonly int lanes, grades;
        private readonly AvFrame[] backs;
        private readonly TMP_Text[] names, counts;
        private readonly TMP_Text[][] gradeNames;
        private readonly Image[][] pips;
        private readonly bool[][] held;
        private readonly bool[] closed;

        public SkillLaneStrip(RectTransform parent, string[] laneNames, int gradeCount)
        {
            lanes = laneNames.Length; grades = gradeCount;
            Rect = AvLay.Child(parent, "SkillLanes");
            backs = new AvFrame[lanes]; names = new TMP_Text[lanes]; counts = new TMP_Text[lanes];
            gradeNames = new TMP_Text[lanes][];
            pips = new Image[lanes][]; held = new bool[lanes][]; closed = new bool[lanes];
            for (int l = 0; l < lanes; l++)
            {
                backs[l] = AvFrame.Add(Rect, "Lane " + l, AvChamfer.Diagonal(4f));
                names[l] = AvText.Make(Rect, "LaneName " + l, AvTextRole.Label, laneNames[l], TextAlignmentOptions.MidlineLeft);
                AvText.Fit(names[l], false);
                counts[l] = AvText.Make(Rect, "LaneCount " + l, AvTextRole.DataSmall, "0/" + grades, TextAlignmentOptions.MidlineRight);
                AvText.Fit(counts[l], false);
                pips[l] = new Image[grades]; held[l] = new bool[grades];
                gradeNames[l] = new TMP_Text[grades];
                for (int g = 0; g < grades; g++)
                {
                    pips[l][g] = AvLay.Solid(Rect, "Pip " + l + "." + g, Color.clear);
                    string title = "GRADE " + (g + 1), help = title;
                    foreach (PerkDefinition perk in PerkCatalog.All)
                        if (perk.Lane == laneNames[l] && perk.Grade == g + 1)
                        { title = perk.Name.Replace(" Qualification", " AUTH"); help = perk.Name + ": " + perk.Description; break; }
                    gradeNames[l][g] = AvText.Make(Rect, "Grade " + l + "." + g, AvTextRole.Micro,
                        title.ToUpperInvariant(), TextAlignmentOptions.TopLeft, true);
                    pips[l][g].raycastTarget = true;
                    AvHelpTip.Attach(pips[l][g].gameObject, help);
                    gradeNames[l][g].raycastTarget = true;
                    AvHelpTip.Attach(gradeNames[l][g].gameObject, help);
                }
            }
            Restyle();
        }

        /// <summary>Hover help for every lane row.</summary>
        public string Help
        {
            set
            {
                for (int l = 0; l < lanes; l++) { backs[l].raycastTarget = true; AvHelpTip.Attach(backs[l].gameObject, value); }
            }
        }

        /// <summary>Repaint one lane: which grades are held, and whether the career cap closed the lane.</summary>
        public void SetLane(int lane, bool[] gradesHeld, bool laneClosed)
        {
            if (lane < 0 || lane >= lanes) return;
            int taken = 0;
            for (int g = 0; g < grades; g++)
            {
                held[lane][g] = g < gradesHeld.Length && gradesHeld[g];
                if (held[lane][g]) taken++;
            }
            closed[lane] = laneClosed;
            counts[lane].text = taken + "/" + grades;
            RestyleLane(lane);
        }

        public override float Measure(float width) => lanes * MinRow + Mathf.Max(0, lanes - 1) * GapY;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float rowH = Mathf.Max(MinRow, (s.H - (lanes - 1) * GapY) / Mathf.Max(1, lanes));
            float detailH = 0f, detailW = (s.W - 2f * PadX) / grades - PipGap;
            for (int l = 0; l < lanes; l++)
                for (int g = 0; g < grades; g++) detailH = Mathf.Max(detailH, AvText.Height(gradeNames[l][g], detailW));
            bool detailed = rowH >= 38f + detailH;
            float pipsX = PadX + NameW + 6f, pipsW = s.W - pipsX - CountW - PadX - 4f;
            float pipW = Mathf.Max(6f, (pipsW - (grades - 1) * PipGap) / Mathf.Max(1, grades)), pipH = Mathf.Min(rowH - 14f, 24f);
            for (int l = 0; l < lanes; l++)
            {
                float y = l * (rowH + GapY);
                AvLay.Place(backs[l].rectTransform, 0f, y, s.W, rowH);
                AvLay.Place(names[l].rectTransform, PadX, y, NameW, detailed ? 24f : rowH);
                AvLay.Place(counts[l].rectTransform, s.W - PadX - CountW, y, CountW, detailed ? 24f : rowH);
                for (int g = 0; g < grades; g++)
                {
                    float x = detailed ? PadX + g * (s.W - 2f * PadX) / grades : pipsX + g * (pipW + PipGap);
                    float w = detailed ? (s.W - 2f * PadX) / grades - PipGap : pipW;
                    AvLay.Place(pips[l][g].rectTransform, x, y + (detailed ? 25f : (rowH - pipH) * .5f), w, detailed ? 4f : pipH);
                    gradeNames[l][g].gameObject.SetActive(detailed);
                    if (detailed) AvLay.Place(gradeNames[l][g].rectTransform, x, y + 34f, w, rowH - 38f);
                }
            }
        }

        private void RestyleLane(int l)
        {
            names[l].color = closed[l] ? AvTheme.Disabled : SqdTone.Ink;
            counts[l].color = closed[l] ? AvTheme.Disabled : SqdTone.Dim;
            for (int g = 0; g < grades; g++)
            {
                pips[l][g].color = held[l][g] ? SqdTone.Key : closed[l] ? SqdTone.Caption.WithAlpha(.15f) : SqdTone.Caption.WithAlpha(.3f);
                gradeNames[l][g].color = held[l][g] ? SqdTone.Ink : SqdTone.Dim;
            }
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            for (int l = 0; l < lanes; l++)
            {
                backs[l].Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                RestyleLane(l);
            }
        }
    }
}
