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
        public const float Pad = 14f, Gap = 8f, Gutter = 8f;
        public const float RowDense = 28f, Row = 32f, ToolCell = 44f;
        public const float Tab = 32f, Header = 30f, ChipStrip = 22f, Metric = 64f, Footer = 26f;
        public const float IconInline = 14f, IconHead = 16f, IconTool = 20f;
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

        private void Advance(float h) { Y += h + gap; any = true; }
    }
}
