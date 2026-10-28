using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>A grid of equal kit buttons with a chosen row height (AvButtons is fixed to wrapped-label height). Rows are 4 apart.</summary>
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
    /// The console's card: a chamfered frame with the title in key colour, a muted caption, an optional header button on the right, an optional 3 px rail,
    /// and a stack of fixed-height parts below. The kit's AvCard has no caption, and its nested flow pads 8 px on every side.
    /// </summary>
    internal sealed class MetCard : AvPart
    {
        private const float PadX = 10f, TitleTop = 6f, TitleH = 14f, BodyTop = 24f, BottomPad = 6f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text title, caption;
        private readonly List<AvPart> parts = new List<AvPart>(4);
        private readonly List<float> heights = new List<float>(4), gaps = new List<float>(4);
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

        /// <summary>A button in the header, right-aligned.</summary>
        public AvControl SetHeaderControl(AvControl.Spec spec, float width)
        {
            headerControl = AvControl.Make(Rect, spec);
            headerControlW = width;
            return headerControl;
        }

        public void SetRail(AvState state)
        {
            if (hasRail && railState == state) return;
            hasRail = true; railState = state; rail.gameObject.SetActive(true); Restyle();
        }

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
            headerControl?.Restyle();
        }
    }
}
