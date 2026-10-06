using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>Shared shape of the Portal data widgets: "// KEY" left, value right, a graphic underneath.</summary>
    public abstract class AvGraphicPart<T> : AvPart where T : MaskableGraphic
    {
        protected const float Head = 15f;
        protected readonly TMP_Text Key, Value;
        protected readonly T Graphic;
        protected AvState State = AvState.Ready;
        private readonly float graphicH;

        protected AvGraphicPart(RectTransform parent, string name, string keyText, float height)
        {
            graphicH = height;
            Rect = AvLay.Child(parent, name + " " + keyText);
            Key = AvText.Make(Rect, "Key", AvTextRole.Micro, "// " + keyText);
            Value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(Key, false); AvText.Fit(Value, false);
            var go = new GameObject("Graphic", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            Graphic = go.AddComponent<T>();
            Graphic.raycastTarget = false;
        }

        /// <summary>Hover help shown in the console footer while the pointer is over the graphic.</summary>
        public string Help
        {
            get => tip != null ? tip.Text : null;
            set { Graphic.raycastTarget = !string.IsNullOrEmpty(value); tip = AvHelpTip.Attach(Graphic.gameObject, value); }
        }
        private AvHelpTip tip;

        public override float Measure(float width) => Head + 3f + graphicH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(Key.rectTransform, 0f, 0f, s.W * 0.6f, Head);
            AvLay.Place(Value.rectTransform, s.W * 0.6f, 0f, s.W * 0.4f, Head);
            float gh = Mathf.Max(graphicH, s.H - Head - 3f);   // a growing part hands its extra height to the graphic
            AvLay.Place((RectTransform)Graphic.transform, 0f, Head + 3f, s.W, gh);
            OnGraphicHeight(gh);
        }

        protected void SetText(string v, AvState st)
        {
            string t = AvStates.Glyph(st) + (v ?? "");
            if (Value.text != t) Value.text = t;
            if (st != State) { State = st; Restyle(); }
        }

        protected Color Fill() => AvStyleHost.FuiFill("metric-fill " + AvStates.Class(State), AvTheme.Accent);
        protected Color Track() => AvStyleHost.FuiFill("metric-track", AvTheme.Hairline);

        public override void Restyle()
        {
            Key.color = AvStyleHost.FuiInk("metric-key", AvTheme.RailInfo);
            Value.color = AvStyleHost.FuiInk(State == AvState.Ready || State == AvState.Info || State == AvState.Inert ? "metric-value" : "chip " + AvStates.Class(State), AvTheme.TextPrimary);
            Paint();
            Graphic.SetVerticesDirty();
        }

        protected virtual void OnGraphicHeight(float h) { }

        protected abstract void Paint();
    }

    /// <summary>Hazard-stripe progress bar: cooldowns, timers, risk. State picks the stripe colour.</summary>
    public sealed class AvHazardBar : AvGraphicPart<AvHazardGraphic>
    {
        public AvHazardBar(RectTransform parent, string keyText) : base(parent, "Hazard", keyText, 10f) { Restyle(); }
        public void Set(float v01, string text, AvState st = AvState.Ready) { Graphic.Value = v01; SetText(text, st); }
        protected override void Paint() { Graphic.FillColor = Fill(); Graphic.FrameColor = Track(); }
    }

    /// <summary>Equalizer histogram: signal, audio, load, any short series.</summary>
    public sealed class AvEqualizer : AvGraphicPart<AvEqualizerGraphic>
    {
        public AvEqualizer(RectTransform parent, string keyText, float height = 36f) : base(parent, "Equalizer", keyText, height) { Restyle(); }
        public void Set(float[] values, string text, AvState st = AvState.Ready) { Graphic.SetValues(values); SetText(text, st); }
        protected override void Paint() { Graphic.FillColor = Fill(); Graphic.BaseColor = Track(); }
    }

    /// <summary>Heat grid: availability, queues, status per slot. One cell can be marked selected.</summary>
    public sealed class AvHeatGrid : AvGraphicPart<AvHeatGraphic>
    {
        private readonly int cols;
        private readonly int fullRows;
        private int rows;
        private const float CellH = 9f, Gap = 2f;

        public AvHeatGrid(RectTransform parent, string keyText, int columns, int cellCount)
            : base(parent, "Heat", keyText, AvPortalMath.HeatRows(cellCount, columns) * (CellH + Gap) - Gap)
        {
            cols = columns;
            fullRows = rows = AvPortalMath.HeatRows(cellCount, columns);
            Graphic.Cols = cols; Graphic.CellH = CellH; Graphic.Gap = Gap;
            Restyle();
        }

        private float[] liveCells = System.Array.Empty<float>();

        /// <param name="live">When set, only the first <paramref name="live"/> cells are drawn and they
        /// take the graphic's height. Padding past the real series would otherwise grow into empty slabs.</param>
        public void Set(float[] cells, int selected, string text, AvState st = AvState.Ready, int live = -1)
        {
            if (live >= 0)
            {
                int n = cells == null ? 0 : Mathf.Clamp(live, 0, cells.Length);
                if (liveCells.Length != n) liveCells = new float[n];
                for (int i = 0; i < n; i++) liveCells[i] = cells[i];
                // An empty board keeps its real slot count, so the outline is the instrument and not one stretched row.
                rows = n == 0 ? fullRows : Mathf.Max(1, AvPortalMath.HeatRows(n, cols));
                Graphic.SetCells(liveCells, selected);
            }
            else Graphic.SetCells(cells, selected);
            SetText(text, st);
        }
        protected override void Paint() { Graphic.FillColor = Fill(); }

        protected override void OnGraphicHeight(float h)
        {
            float cell = Mathf.Max(CellH, (h - (rows - 1) * Gap) / rows);
            if (Mathf.Abs(cell - Graphic.CellH) < 0.1f) return;
            Graphic.CellH = cell; Graphic.SetVerticesDirty();
        }
    }

    /// <summary>Solid tag ("slab") for a title or alert word; sized to its text, left aligned.</summary>
    public sealed class AvSlab : AvPart
    {
        private readonly AvFrame back;
        private readonly TMP_Text text;
        private AvState state = AvState.Ready;

        public AvSlab(RectTransform parent, string label, AvState st = AvState.Ready)
        {
            Rect = AvLay.Child(parent, "Slab " + label);
            back = AvFrame.Add(Rect, "Back", default(AvChamfer)); back.Stroke = 0f;
            text = AvText.Make(Rect, "Text", AvTextRole.Micro, label, TextAlignmentOptions.Center);
            AvText.Fit(text, false);
            state = st;
            Restyle();
        }

        public void Set(string label, AvState st)
        {
            if (text.text != label) { text.text = label ?? ""; Changed(); }
            if (st != state) { state = st; Restyle(); }
        }

        public override float Measure(float width) => 18f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = Mathf.Min(s.W, AvText.Width(text) + 14f);
            AvLay.Place(back.rectTransform, 0f, 0f, w, s.H);
            AvLay.Place(text.rectTransform, 0f, 0f, w, s.H);
        }

        public override void Restyle()
        {
            back.Paint(AvStyleHost.FuiFill("slab " + AvStates.Class(state), AvTheme.Accent), Color.clear);
            text.color = AvStyleHost.FuiInk("slab", Color.black);
        }
    }
}
