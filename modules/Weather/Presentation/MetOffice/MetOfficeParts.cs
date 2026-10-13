using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// The effect-class mark of the mockup: a 2 px strip on a control's top edge, solid for SMOOTH, dotted for FADE, hatched for CUT. The TOP edge, because the kit draws a latched
    /// control's own 2 px rail on the bottom edge. A mesh overlay with no raycast, so it never touches the window's own hit test.
    /// Colours are kit roles (info, key, caution), read again whenever the window restyles.
    /// </summary>
    internal static class MetRail
    {
        public static EnvGraphic Attach(RectTransform host, MetEffect effect)
        {
            if (effect == MetEffect.None) return null;
            EnvGraphic g = EnvGraphic.Create(host, "EffectRail");
            RectTransform r = g.rectTransform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(0.5f, 1f);
            r.offsetMin = new Vector2(4f, -2f); r.offsetMax = new Vector2(-4f, 0f);
            g.Draw = c => Paint(c, effect, r.rect.width);
            return g;
        }

        private static void Paint(EnvCanvas c, MetEffect effect, float w)
        {
            if (w <= 0f) return;
            switch (effect)
            {
                case MetEffect.Smooth:
                    c.Quad(0f, 0f, w, 2f, EnvInk.Role("info"));
                    break;
                case MetEffect.Fade:
                    for (float x = 0f; x < w; x += 6f) c.Quad(x, 0f, Mathf.Min(3f, w - x), 2f, EnvInk.Role("key"));
                    break;
                case MetEffect.Cut:
                    for (float x = 0f; x < w - 2f; x += 7f) c.Line(x, 2f, x + 3f, 0f, 1.4f, EnvInk.Role("caution"));
                    break;
            }
        }
    }

    /// <summary>Repaints a list of mesh overlays when the theme changes (parts registered with the ticker get a restyle pass; bare overlays do not).</summary>
    internal sealed class MetRepaint : AvPart
    {
        private readonly List<EnvGraphic> graphics = new List<EnvGraphic>(64);
        public void Track(EnvGraphic g) { if (g != null) graphics.Add(g); }
        public override void Place(AvSlot slot) { }
        public override void Restyle() { for (int i = 0; i < graphics.Count; i++) if (graphics[i] != null) graphics[i].SetVerticesDirty(); }
    }

    /// <summary>
    /// A grid of equal kit buttons with a chosen row height (AvButtons is fixed to wrapped-label height). Rows are 4 apart. Labels may carry a second line,
    /// which the button's own fit shrinks toward the 11 px floor; the window gates truncation offline.
    /// </summary>
    internal sealed class MetControlGrid : AvPart
    {
        private readonly int columns;
        private readonly float rowHeight;
        public AvControl[] Controls { get; }

        public MetControlGrid(RectTransform parent, AvControl.Spec[] specs, int columns, float rowHeight)
        {
            Rect = AvLay.Child(parent, "ControlGrid");
            this.columns = Mathf.Max(1, columns);
            this.rowHeight = rowHeight;
            Controls = new AvControl[specs.Length];
            for (int i = 0; i < specs.Length; i++) Controls[i] = AvControl.Make(Rect, specs[i]);
        }

        private int Rows => (Controls.Length + columns - 1) / columns;

        public override float Measure(float width) => Rows * rowHeight + (Rows - 1) * MetOfficeLayout.CellGap;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float cw = AvFlowMath.ColumnWidth(s.W, columns, MetOfficeLayout.CellGap);
            for (int i = 0; i < Controls.Length; i++)
                AvLay.Place(Controls[i].Rect, (i % columns) * (cw + MetOfficeLayout.CellGap), (i / columns) * (rowHeight + MetOfficeLayout.CellGap), cw, rowHeight);
        }

        public override void Restyle() { foreach (AvControl c in Controls) c.Restyle(); }
    }

    /// <summary>
    /// One row of the mockup's stepper: [label or button] [-] value [+] [extra button]. The value is a flexible mono readout in a sunken box, so a story or sky
    /// name has the room it needs (the kit's AvStepper fixes it at 90 px). Built fluently; the minus and plus buttons always exist.
    /// </summary>
    internal sealed class MetStepRow : AvPart
    {
        private const float Btn = 28f;
        private readonly AvControl minus, plus;
        private readonly AvFrame box;
        private readonly TMP_Text value;
        private TMP_Text label;
        private float leftW;

        public AvControl Minus => minus;
        public AvControl Plus => plus;

        public MetStepRow(RectTransform parent, AvControl.Spec minusSpec, AvControl.Spec plusSpec)
        {
            Rect = AvLay.Child(parent, "StepRow");
            box = AvFrame.Add(Rect, "ValueBox", default(AvChamfer));
            box.Stroke = 1f;
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.Center);
            AvText.Fit(value, false);
            minus = AvControl.Make(Rect, minusSpec);
            plus = AvControl.Make(Rect, plusSpec);
            Restyle();
        }

        public MetStepRow WithLabel(string text, float width)
        {
            label = AvText.Make(Rect, "Label", AvTextRole.Label, text, TextAlignmentOptions.MidlineLeft);
            AvText.Fit(label, false);
            leftW = width;
            Restyle();
            return this;
        }

        public void SetValue(string text) => AvText.Set(value, text);

        public override float Measure(float width) => MetOfficeLayout.Row;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float x = 0f;
            float y = (s.H - 26f) * 0.5f;
            if (label != null) { AvLay.Place(label.rectTransform, 0f, 0f, leftW, s.H); x = leftW; }
            float right = s.W;
            AvLay.Place(minus.Rect, x, y, Btn, 26f);
            AvLay.Place(plus.Rect, right - Btn, y, Btn, 26f);
            AvLay.Place(box.rectTransform, x + Btn, y, Mathf.Max(1f, right - Btn - x - Btn), 26f);
            AvLay.Place(value.rectTransform, x + Btn + 4f, y, Mathf.Max(1f, right - Btn - x - Btn - 8f), 26f);
        }

        public override void Restyle()
        {
            box.Paint(EnvInk.Role("surface-sunken"), EnvInk.Role("frame"));
            value.color = EnvInk.Role("ink");
            if (label != null) label.color = EnvInk.Role("ink-dim");
            minus.Restyle(); plus.Restyle();
        }
    }

    /// <summary>The mockup's footer legend of the availability tags: LIVE works now, S5 arrives with tape motion, in the two tint colours the cards' tags use. Sits on its own plate.</summary>
    internal sealed class MetTagLegend : AvPart
    {
        public const float Width = 98f;
        private static readonly string[] Words = { "LIVE", "S5" };
        private static readonly MetAvail[] Avail = { MetAvail.Live, MetAvail.Motion5 };
        private static readonly float[] Widths = { 38f, 26f };
        private readonly Image plate;
        private readonly AvFrame[] box = new AvFrame[2];
        private readonly TMP_Text[] text = new TMP_Text[2];

        public MetTagLegend(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "TagLegend");
            plate = AvLay.Solid(Rect, "Plate", Color.clear);
            for (int i = 0; i < box.Length; i++)
            {
                box[i] = AvFrame.Add(Rect, "Tag" + i, default(AvChamfer));
                box[i].Stroke = 1f; box[i].Fill = false;
                text[i] = AvText.Make(Rect, "TagWord" + i, AvTextRole.DataSmall, Words[i], TextAlignmentOptions.Center);
                AvText.Fit(text[i], false);
            }
            Restyle();
        }

        public override float Measure(float width) => 14f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(plate.rectTransform, -4f, -2f, s.W + 8f, s.H + 4f);
            float x = 0f;
            for (int i = 0; i < box.Length; i++)
            {
                AvLay.Place(box[i].rectTransform, x, 0f, Widths[i], s.H);
                AvLay.Place(text[i].rectTransform, x, 0f, Widths[i], s.H);
                x += Widths[i] + 4f;
            }
        }

        public override void Restyle()
        {
            plate.color = EnvInk.Role("ground");
            for (int i = 0; i < box.Length; i++)
            {
                Color tint = EnvInk.Role(Avail[i] == MetAvail.Live ? "key" : "caution");
                box[i].Paint(Color.clear, tint);
                text[i].color = tint;
            }
        }
    }

    /// <summary>
    /// The mockup's card: a chamfered frame with the title in key colour, a muted caption and up to two availability tags on the right, an optional 3 px hero rail,
    /// and a stack of fixed-height parts below. The kit's AvCard has no caption or tags, and its nested flow pads 8 px on every side, which the 540 px columns cannot afford.
    /// </summary>
    internal sealed class MetCard : AvPart
    {
        private const float PadX = 10f, TitleTop = 6f, TitleH = 14f, BodyTop = 24f, BottomPad = 6f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text title, caption;
        private readonly TMP_Text[] tagText = new TMP_Text[2];
        private readonly AvFrame[] tagBox = new AvFrame[2];
        private readonly MetAvail[] tagAvail = new MetAvail[2];
        private readonly List<AvPart> parts = new List<AvPart>(8);
        private readonly List<float> heights = new List<float>(8), gaps = new List<float>(8);
        private readonly AvTicker ticker;
        private AvControl headerControl;
        private float headerControlW;
        private AvState railState;
        private bool hasRail;

        /// <summary>The rect child parts are created under.</summary>
        public RectTransform Content => Rect;

        public MetCard(RectTransform parent, AvTicker ticker, string titleText)
        {
            this.ticker = ticker;
            Rect = AvLay.Child(parent, "Card " + titleText);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.Bracket = 8f;
            rail = AvLay.Solid(Rect, "HeroRail", Color.clear);
            rail.gameObject.SetActive(false);
            title = AvText.Make(Rect, "Title", AvTextRole.Label, titleText, TextAlignmentOptions.MidlineLeft);
            AvText.Fit(title, false);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(caption, false);
            for (int i = 0; i < tagBox.Length; i++)
            {
                tagBox[i] = AvFrame.Add(Rect, "Tag" + i, default(AvChamfer));
                tagBox[i].Stroke = 1f; tagBox[i].Fill = false;
                tagText[i] = AvText.Make(Rect, "TagWord" + i, AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
                AvText.Fit(tagText[i], false);
                tagBox[i].gameObject.SetActive(false); tagText[i].gameObject.SetActive(false);
            }
            Restyle();
        }

        /// <summary>Adds a fixed-height part under the previous one.</summary>
        public T Add<T>(T part, float height, float gapBefore = MetOfficeLayout.Gap) where T : AvPart
        {
            parts.Add(part); heights.Add(height); gaps.Add(parts.Count == 1 ? 0f : gapBefore);
            part.Parent = this;
            ticker?.Register(part);
            Changed();
            return part;
        }

        public void SetCaption(string text) { if (AvText.Set(caption, text) && placed) Place(lastSlot); }

        /// <summary>A tag on the header's right edge: <paramref name="word"/> in the colour of when the feature arrives (LIVE cyan, S5 amber). Index 0 is the rightmost.</summary>
        public void SetTag(int index, string word, MetAvail avail)
        {
            tagAvail[index] = avail;
            bool on = !string.IsNullOrEmpty(word);
            tagBox[index].gameObject.SetActive(on); tagText[index].gameObject.SetActive(on);
            AvText.Set(tagText[index], word);
            AvHelpTip.Attach(tagBox[index].gameObject, on ? MetCatalog.TagHelp : null);   // what LIVE and S5 mean: the legend the footer of the mockup carried
            Restyle();
            if (placed) Place(lastSlot);
        }

        /// <summary>A button in the header (the identity card's RE-ROLL), right-aligned before any tags.</summary>
        public AvControl SetHeaderControl(AvControl.Spec spec, float width)
        {
            headerControl = AvControl.Make(Rect, spec);
            headerControlW = width;
            return headerControl;
        }

        public void SetRail(AvState state) { hasRail = true; railState = state; rail.gameObject.SetActive(true); Restyle(); }

        public override float Measure(float width)
        {
            float h = BodyTop;
            for (int i = 0; i < parts.Count; i++) h += gaps[i] + heights[i];
            return h + BottomPad;
        }

        private AvSlot lastSlot;
        private bool placed;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s; placed = true;
            float right = s.W - PadX;
            for (int i = 0; i < tagBox.Length; i++)
            {
                if (!tagBox[i].gameObject.activeSelf) continue;
                float tw = Mathf.Ceil(AvText.Width(tagText[i])) + 8f;
                AvLay.Place(tagBox[i].rectTransform, right - tw, TitleTop, tw, TitleH);
                AvLay.Place(tagText[i].rectTransform, right - tw, TitleTop, tw, TitleH);
                right -= tw + 4f;
            }
            if (headerControl != null)
            {
                AvLay.Place(headerControl.Rect, right - headerControlW, 4f, headerControlW, 18f);
                right -= headerControlW + 6f;
            }
            float titleW = Mathf.Ceil(AvText.Width(title)) + 4f;
            AvLay.Place(title.rectTransform, PadX, TitleTop, titleW, TitleH);
            float capX = PadX + titleW + 6f;
            AvLay.Place(caption.rectTransform, capX, TitleTop, Mathf.Max(1f, right - capX), TitleH);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            float y = BodyTop;
            for (int i = 0; i < parts.Count; i++)
            {
                y += gaps[i];
                parts[i].Place(new AvSlot(PadX, y, s.W - PadX - 9f, heights[i]));
                y += heights[i];
            }
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            frame.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
            frame.SetVerticesDirty();
            title.color = EnvInk.Role("key");
            caption.color = EnvInk.Role("ink-muted");
            if (hasRail) rail.color = EnvInk.State(railState == AvState.Info ? AvState.Ready : railState);
            for (int i = 0; i < tagBox.Length; i++)
            {
                Color tint = EnvInk.Role(tagAvail[i] == MetAvail.Live ? "key" : "caution");
                tagBox[i].Paint(Color.clear, tint);
                tagText[i].color = tint;
            }
            headerControl?.Restyle();
        }
    }
}
