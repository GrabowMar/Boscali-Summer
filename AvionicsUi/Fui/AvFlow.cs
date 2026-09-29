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
    /// Parts that change height after the first layout (text arrives, rows appear, a part hides) re-lay the
    /// page: kit setters report through <see cref="AvPart.Changed"/> (checked every fast tick) and a slow
    /// sweep re-measures every part of a visible page, so content can never spill over its neighbour.
    /// Lines whose parts are all hidden collapse, gap included.
    /// </summary>
    public sealed class AvFlow
    {
        private static readonly ProfilerMarker RelayoutMarker = new ProfilerMarker("NOA.UI.Relayout");
        private readonly List<AvPart[]> lines = new List<AvPart[]>(32);
        private readonly List<int> lineColumns = new List<int>(32);
        private readonly AvFlowMath math;
        private const int MaxSuspects = 64;
        private readonly List<AvPart> suspects = new List<AvPart>(8);
        private bool pending, tickHooked;
        private Canvas pageCanvas;

        public AvFlow(RectTransform content, AvTicker ticker, float width, float gutter = AvGridTokens.Gutter)
        {
            Content = content; Ticker = ticker; Width = width;
            math = new AvFlowMath(width, AvGridTokens.Pad, gutter);
            HookTicks();
        }

        /// <summary>The part that hosts this flow (a card), told when the flow's height changes.</summary>
        internal AvPart Host;

        public RectTransform Content { get; }
        public AvTicker Ticker { get; }
        public float Width { get; }
        public float Inner => math.Inner;
        public float ContentHeight { get; private set; }

        public T Add<T>(T part) where T : AvPart
        {
            lines.Add(new AvPart[] { part }); lineColumns.Add(1);
            Adopt(part);
            RequestRelayout();
            return part;
        }

        public void Row(params AvPart[] parts)
        {
            lines.Add(parts); lineColumns.Add(parts.Length);
            foreach (AvPart p in parts) Adopt(p);
            RequestRelayout();
        }

        public void Space(float h) { lines.Add(new AvPart[] { new AvSpacer(h) }); lineColumns.Add(1); RequestRelayout(); }

        public AvCellGrid Grid(int columns) => new AvCellGrid(this, Math.Max(1, columns));

        public AvSection Section(AvIcon icon, string title, string caption = null) => Add(new AvSection(Content, icon, title, caption));

        public AvButtons Buttons(params AvControl.Spec[] specs) => Add(new AvButtons(Content, specs));

        internal void AppendToGrid(AvPart part, int columns)
        {
            Adopt(part);
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

        private void Adopt(AvPart part)
        {
            if (part == null) return;
            part.Owner = this;
            Ticker?.Register(part);
        }

        public void RequestRelayout()
        {
            pending = true;
            if (Ticker == null) Relayout();
        }

        internal void MarkChanged(AvPart part)
        {
            if (Ticker == null) { if (Moved(part)) Relayout(); return; }
            if (pending || suspects.Contains(part)) return;
            if (suspects.Count >= MaxSuspects) { pending = true; return; }
            suspects.Add(part);
        }

        private void HookTicks()
        {
            if (tickHooked || Ticker == null) return;
            tickHooked = true;
            Ticker.Add(-1, AvTickRate.Fast, FastTick);   // one entry per flow; the slow sweep runs every 5th tick
        }

        private int sweep;

        private void FastTick()
        {
            if (++sweep >= 5) { sweep = 0; SlowTick(); }
            if (!pending && suspects.Count > 0)
                for (int i = 0; i < suspects.Count && !pending; i++) pending = Moved(suspects[i]);
            suspects.Clear();
            if (pending) Relayout();
        }

        // Safety net for parts whose text changes without Changed() (custom parts writing TMP directly).
        private void SlowTick()
        {
            if (pending || !Visible()) return;
            for (int i = 0; i < lines.Count && !pending; i++)
                foreach (AvPart p in lines[i]) if (Moved(p)) { pending = true; break; }
            if (pending) Relayout();
        }

        private bool Visible()
        {
            if (Content == null || !Content.gameObject.activeInHierarchy) return false;
            if (pageCanvas == null) pageCanvas = Content.GetComponentInParent<Canvas>();
            return pageCanvas == null || pageCanvas.enabled;
        }

        private static bool Moved(AvPart p)
        {
            if (p == null || p.PlacedWidth < 0f) return false;
            bool shown = p.Shown;
            if (shown != p.PlacedShown) return true;
            return shown && Mathf.Abs(p.Measure(p.PlacedWidth) - p.PlacedHeight) > 0.5f;
        }

        public void Relayout()
        {
            using (RelayoutMarker.Auto())
            {
                pending = false;
                suspects.Clear();
                float before = ContentHeight;
                math.Reset();
                for (int i = 0; i < lines.Count; i++)
                {
                    AvPart[] line = lines[i];
                    int cols = lineColumns[i];
                    float w = cols == 1 ? math.Inner : AvFlowMath.ColumnWidth(math.Inner, cols, AvGridTokens.Gap);
                    float h = 0f;
                    bool any = false;
                    foreach (AvPart p in line)
                    {
                        bool shown = p.Shown;
                        p.PlacedShown = shown; p.PlacedWidth = w;
                        if (!shown) { p.PlacedHeight = 0f; continue; }
                        any = true;
                        float ph = p.Measure(w);
                        p.PlacedHeight = ph;
                        h = Mathf.Max(h, ph);
                    }
                    if (!any) continue;   // a fully hidden line collapses, gap included
                    if (cols == 1) line[0].Place(math.Take(h));
                    else
                    {
                        AvSlot[] slots = math.TakeColumns(cols, h);
                        for (int c = 0; c < line.Length; c++) if (line[c].PlacedShown) line[c].Place(slots[c]);
                    }
                }
                ContentHeight = math.ContentHeight;
                Content.sizeDelta = new Vector2(Width, ContentHeight);
                if (Host != null && Mathf.Abs(ContentHeight - before) > 0.5f) Host.Changed();
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
    public sealed class AvCellGrid
    {
        private readonly AvFlow flow;
        private readonly int columns;
        internal AvCellGrid(AvFlow flow, int columns) { this.flow = flow; this.columns = columns; }

        public AvCell Toggle(string title, string sub, Func<bool> get, Action<bool> set)
        {
            var cell = AvCell.Toggle(flow.Content, title, sub, get, set);
            flow.AppendToGrid(cell, columns);
            return cell;
        }

        public void Add(AvPart p) => flow.AppendToGrid(p, columns);
    }
}
