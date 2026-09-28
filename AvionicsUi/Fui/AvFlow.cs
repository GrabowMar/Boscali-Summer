using System;
using System.Collections.Generic;
using NOAvionics;
using Unity.Profiling;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>
    /// Vertical flow of parts inside a page. A line is one full-width part or N equal columns.
    /// Every line is measured at its real width, so copy wraps and the line grows — nothing clips.
    /// </summary>
    public sealed class AvFlow
    {
        private static readonly ProfilerMarker RelayoutMarker = new ProfilerMarker("NOA.UI.Relayout");
        private readonly List<AvPart[]> lines = new List<AvPart[]>(32);
        private readonly List<int> lineColumns = new List<int>(32);
        private readonly AvFlowMath math;
        private bool pending, tickHooked;

        public AvFlow(RectTransform content, AvTicker ticker, float width, float gutter = AvGridTokens.Gutter)
        {
            Content = content; Ticker = ticker; Width = width;
            math = new AvFlowMath(width, AvGridTokens.Pad, gutter);
        }

        public RectTransform Content { get; }
        public AvTicker Ticker { get; }
        public float Width { get; }
        public float Inner => math.Inner;
        public float ContentHeight { get; private set; }

        public T Add<T>(T part) where T : AvPart
        {
            lines.Add(new AvPart[] { part }); lineColumns.Add(1);
            Ticker?.Register(part);
            RequestRelayout();
            return part;
        }

        public void Row(params AvPart[] parts)
        {
            lines.Add(parts); lineColumns.Add(parts.Length);
            foreach (AvPart p in parts) Ticker?.Register(p);
            RequestRelayout();
        }

        public void Space(float h) { lines.Add(new AvPart[] { new AvSpacer(h) }); lineColumns.Add(1); RequestRelayout(); }

        public AvGrid Grid(int columns) => new AvGrid(this, Math.Max(1, columns));

        public AvSection Section(AvIcon icon, string title, string caption = null) => Add(new AvSection(Content, icon, title, caption));

        public AvButtons Buttons(params AvControl.Spec[] specs) => Add(new AvButtons(Content, specs));

        internal void AppendToGrid(AvPart part, int columns)
        {
            Ticker?.Register(part);
            int last = lines.Count - 1;
            if (last >= 0 && lineColumns[last] == columns && lines[last].Length < columns && lines[last][0] is AvCell == part is AvCell)
            {
                AvPart[] grown = new AvPart[lines[last].Length + 1];
                lines[last].CopyTo(grown, 0); grown[grown.Length - 1] = part;
                lines[last] = grown;
            }
            else { lines.Add(new[] { part }); lineColumns.Add(columns); }
            RequestRelayout();
        }

        public void RequestRelayout()
        {
            pending = true;
            if (!tickHooked && Ticker != null) { tickHooked = true; Ticker.Add(-1, AvTickRate.Fast, () => { if (pending) Relayout(); }); }
            if (Ticker == null) Relayout();
        }

        public void Relayout()
        {
            using (RelayoutMarker.Auto())
            {
                pending = false;
                math.Reset();
                for (int i = 0; i < lines.Count; i++)
                {
                    AvPart[] line = lines[i];
                    int cols = lineColumns[i];
                    float colW = AvFlowMath.ColumnWidth(math.Inner, cols, AvGridTokens.Gap);
                    float h = 0f;
                    foreach (AvPart p in line) h = Mathf.Max(h, p.Measure(cols == 1 ? math.Inner : colW));
                    if (cols == 1) line[0].Place(math.Take(h));
                    else
                    {
                        AvSlot[] slots = math.TakeColumns(cols, h);
                        for (int c = 0; c < line.Length; c++) line[c].Place(slots[c]);
                    }
                }
                ContentHeight = math.ContentHeight;
                Content.sizeDelta = new Vector2(Width, ContentHeight);
            }
        }

        private sealed class AvSpacer : AvPart
        {
            private readonly float h;
            public AvSpacer(float height) { h = height; }
            public override float Measure(float width) => h;
            public override void Place(AvSlot slot) { }
        }
    }

    /// <summary>Fills lines of N equal cells. Cells can be mixed with other parts in the same grid.</summary>
    public sealed class AvGrid
    {
        private readonly AvFlow flow;
        private readonly int columns;
        internal AvGrid(AvFlow flow, int columns) { this.flow = flow; this.columns = columns; }

        public AvCell Toggle(string title, string sub, Func<bool> get, Action<bool> set)
        {
            var cell = AvCell.Toggle(flow.Content, title, sub, get, set);
            flow.AppendToGrid(cell, columns);
            return cell;
        }

        public void Add(AvPart p) => flow.AppendToGrid(p, columns);
    }
}
