using System;

namespace NOAvionics
{
    /// <summary>A placed rectangle in top-left panel units (y grows downwards).</summary>
    public readonly struct AvSlot
    {
        public readonly float X, Y, W, H;
        public AvSlot(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float Bottom => Y + H;
    }

    /// <summary>Kit v2 grid (spec §5.3). The gutter is always reserved: scroll bars live in it, text never does.</summary>
    public static class AvGridTokens
    {
        public const float Pad = 8f, Gap = 5f, Gutter = 6f;
        public const float RowDense = 24f, Row = 28f, ToolCell = 38f;
        public const float Tab = 28f, Header = 28f, ChipStrip = 18f, Metric = 50f, Footer = 76f;
        public const float IconInline = 14f, IconHead = 16f, IconTool = 20f;
    }

    /// <summary>Key line versus value line for one metric tile. Both false means the unit is not drawn.</summary>
    public readonly struct MetricLine
    {
        public readonly bool UnitOnTop;
        public readonly bool UnitByValue;
        public MetricLine(bool onTop, bool byValue) { UnitOnTop = onTop; UnitByValue = byValue; }
        public bool UnitShown => UnitOnTop || UnitByValue;
    }

    /// <summary>One-pass vertical stacking. Engine-free so layout arithmetic is testable.</summary>
    public sealed class AvFlowMath
    {
        private readonly float pad, gap;
        private bool any;

        public AvFlowMath(float width, float pad = AvGridTokens.Pad, float gutter = AvGridTokens.Gutter, float gap = AvGridTokens.Gap)
        {
            Width = width;
            this.pad = pad;
            this.gap = gap;
            Inner = Math.Max(0f, width - 2f * pad - gutter);
            Y = pad;
        }

        public float Width { get; }
        public float Inner { get; }
        public float Y { get; private set; }
        public float ContentHeight => any ? Y - gap + pad : 0f;

        public AvSlot Take(float height)
        {
            var s = new AvSlot(pad, Y, Inner, Math.Max(0f, height));
            Advance(s.H);
            return s;
        }

        public AvSlot[] TakeColumns(int columns, float height)
        {
            columns = Math.Max(1, columns);
            float w = ColumnWidth(Inner, columns, gap);
            var slots = new AvSlot[columns];
            for (int i = 0; i < columns; i++) slots[i] = new AvSlot(pad + i * (w + gap), Y, w, Math.Max(0f, height));
            Advance(Math.Max(0f, height));
            return slots;
        }

        public void Space(float h) { Y += Math.Max(0f, h); }

        public void Reset() { Y = pad; any = false; }

        public static float ColumnWidth(float inner, int columns, float gap)
        {
            columns = Math.Max(1, columns);
            return Math.Max(0f, (inner - gap * (columns - 1)) / columns);
        }

        /// <summary>
        /// Where a metric's unit goes. A unit that fits neither beside the key nor beside the value is
        /// omitted: the tile cannot grow, and a clipped unit would fail the overflow gate.
        /// </summary>
        public static MetricLine PlaceMetric(float avail, float keyW, float unitW, float valueW, float gap = 6f)
        {
            if (unitW <= 0.5f || avail <= 0f) return new MetricLine(false, false);
            if (keyW + unitW + gap <= avail) return new MetricLine(true, false);
            if (valueW + unitW + gap <= avail) return new MetricLine(false, true);
            return new MetricLine(false, false);
        }

        private void Advance(float h) { Y += h + gap; any = true; }
    }
}
